#load "./paths.cake"
#load "./artifacts.cake"
#load "./credentials.cake"
#load "./version.cake"

public class BuildParameters
{
    public string Target { get; private set; }
    public string Configuration { get; private set; }

    public bool DisableUnitTests { get; private set; }
    public bool DisablePublishGem { get; private set; }
    public bool DisablePublishTfs { get; private set; }
    public bool DisablePublishNuget { get; private set; }
    public bool DisablePublishChocolatey { get; private set; }
    public bool DisablePublishDocker { get; private set; }

    public bool IsRunningOnUnix { get; private set; }
    public bool IsRunningOnWindows { get; private set; }
    public bool IsRunningOnLinux { get; private set; }
    public bool IsRunningOnMacOS { get; private set; }

    public bool IsLocalBuild { get; private set; }
    public bool IsRunningOnAppVeyor { get; private set; }
    public bool IsRunningOnTravis { get; private set; }
    public bool IsRunningOnAzurePipeline { get; private set; }

    public bool IsMainRepo { get; private set; }
    public bool IsMainBranch { get; private set; }
    public bool IsTagged { get; private set; }

    public BuildCredentials Credentials { get; private set; }
    public BuildVersion Version { get; private set; }
    public BuildPaths Paths { get; private set; }
    public BuildPackages Packages { get; private set; }
    public BuildArtifacts Artifacts { get; private set; }
    public Dictionary<string, DirectoryPath> PackagesBuildMap { get; private set; }

    public bool IsStableRelease() => !IsLocalBuild && IsMainRepo && IsMainBranch && IsTagged;
    public bool IsPreRelease()    => !IsLocalBuild && IsMainRepo && IsMainBranch && !IsTagged;

    public bool CanPostToGitter => !string.IsNullOrWhiteSpace(Credentials.Gitter.Token) && !string.IsNullOrWhiteSpace(Credentials.Gitter.RoomId);

    public static BuildParameters GetParameters(ICakeContext context)
    {
        if (context == null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        var target = context.Argument("target", "Default");
        var buildSystem = context.BuildSystem();

        return new BuildParameters {
            Target        = target,
            Configuration = context.Argument("configuration", "Release"),

            DisableUnitTests            = IsDisabled(context, "DISABLE_UNIT_TESTS"),
            DisablePublishGem           = IsDisabled(context, "DISABLE_PUBLISH_GEM"),
            DisablePublishTfs           = IsDisabled(context, "DISABLE_PUBLISH_TFS"),
            DisablePublishNuget         = IsDisabled(context, "DISABLE_PUBLISH_NUGET"),
            DisablePublishChocolatey    = IsDisabled(context, "DISABLE_PUBLISH_CHOCOLATEY"),
            DisablePublishDocker        = IsDisabled(context, "DISABLE_PUBLISH_DOCKER"),

            IsRunningOnUnix    = context.IsRunningOnUnix(),
            IsRunningOnWindows = context.IsRunningOnWindows(),
            IsRunningOnLinux   = context.Environment.Platform.Family == PlatformFamily.Linux,
            IsRunningOnMacOS   = context.Environment.Platform.Family == PlatformFamily.OSX,

            IsLocalBuild             = buildSystem.IsLocalBuild,
            IsRunningOnAppVeyor      = buildSystem.IsRunningOnAppVeyor,
            IsRunningOnTravis        = buildSystem.IsRunningOnTravisCI,
            IsRunningOnAzurePipeline = buildSystem.IsRunningOnVSTS
        };
    }

    public void Initialize(ICakeContext context, GitVersion gitVersion)
    {
        Version = BuildVersion.Calculate(context, this, gitVersion);

        Paths = BuildPaths.GetPaths(context, Configuration, Version);

        Packages = BuildPackages.GetPackages(
            Paths.Directories.NugetRoot,
            Version.SemVersion,
            new [] { "GitVersion.CommandLine.DotNetCore", "GitVersion.CommandLine", "GitVersionCore", "GitVersionTask" },
            new [] { "GitVersion.Portable" });

        var files = Paths.Files;
        Artifacts = BuildArtifacts.GetArtifacts(new[] {
            files.ZipArtifactPathDesktop,
            files.ZipArtifactPathCoreClr,
            files.TestCoverageOutputFilePath,
            files.ReleaseNotesOutputFilePath,
            files.VsixOutputFilePath,
            files.GemOutputFilePath
        });

        PackagesBuildMap = new Dictionary<string, DirectoryPath>
        {
            ["GitVersion.CommandLine.DotNetCore"] = Paths.Directories.ArtifactsBinNetCore,
            ["GitVersion.CommandLine"] = Paths.Directories.ArtifactsBinFullFxCmdline,
            ["GitVersion.Portable"] = Paths.Directories.ArtifactsBinFullFxPortable,
        };

        Credentials = BuildCredentials.GetCredentials(context);

        IsMainRepo   = IsOnMainRepo(context);
        IsMainBranch = IsOnMainBranch(context);
        IsTagged     = IsBuildTagged(context, gitVersion);
    }

    private static bool IsOnMainRepo(ICakeContext context)
    {
        var buildSystem = context.BuildSystem();
        string repositoryName = null;
        if (buildSystem.IsRunningOnAppVeyor)
        {
            repositoryName = buildSystem.AppVeyor.Environment.Repository.Name;
        }
        else if (buildSystem.IsRunningOnTravisCI)
        {
            repositoryName = buildSystem.TravisCI.Environment.Repository.Slug;
        }
        else if (buildSystem.IsRunningOnVSTS)
        {
            repositoryName = buildSystem.TFBuild.Environment.Repository.RepoName;
        }

        context.Information("Repository Name: {0}" , repositoryName);

        return !string.IsNullOrWhiteSpace(repositoryName) && StringComparer.OrdinalIgnoreCase.Equals("gittools/gitversion", repositoryName);
    }

    private static bool IsOnMainBranch(ICakeContext context)
    {
        var buildSystem = context.BuildSystem();
        string repositoryBranch = null;
        if (buildSystem.IsRunningOnAppVeyor)
        {
            repositoryBranch = buildSystem.AppVeyor.Environment.Repository.Branch;
        }
        else if (buildSystem.IsRunningOnTravisCI)
        {
            repositoryBranch = buildSystem.TravisCI.Environment.Build.Branch;
        }
        else if (buildSystem.IsRunningOnVSTS)
        {
            repositoryBranch = buildSystem.TFBuild.Environment.Repository.Branch;
        }

        context.Information("Repository Branch: {0}" , repositoryBranch);

        return !string.IsNullOrWhiteSpace(repositoryBranch) && StringComparer.OrdinalIgnoreCase.Equals("master", repositoryBranch);
    }

    private static bool IsBuildTagged(ICakeContext context, GitVersion gitVersion)
    {
        var gitPath = context.Tools.Resolve(context.IsRunningOnWindows() ? "git.exe" : "git");
        context.StartProcess(gitPath, new ProcessSettings { Arguments = "tag --points-at " + gitVersion.Sha, RedirectStandardOutput = true }, out var redirectedOutput);

        return redirectedOutput.Any();
    }

    private static bool IsDisabled(ICakeContext context, string envVar)
    {
        var value = context.EnvironmentVariable(envVar);

        return !string.IsNullOrWhiteSpace(value) && bool.Parse(value);
    }
}
