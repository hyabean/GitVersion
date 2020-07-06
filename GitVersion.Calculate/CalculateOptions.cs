﻿using System.IO;
using GitVersion.Core;
using GitVersion.Core.Infrastructure;

namespace GitVersion.Calculate
{
    [Command("calculate", "Calculates the version object from the git history.")]
    public class CalculateOptions : GitVersionOptions
    {
        [Option("--work-dir", "The working directory with the git repository")]
        public DirectoryInfo WorkDir { get; set; }
    }
}