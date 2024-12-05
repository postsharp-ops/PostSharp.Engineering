// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using JetBrains.Annotations;
using Spectre.Console.Cli;
using System.ComponentModel;

namespace PostSharp.Engineering.BuildTools.Git
{
    /// <summary>
    /// Settings for <see cref="GitBulkRenameCommand"/>.
    /// </summary>
    [PublicAPI]
    internal class GitBulkRenameSettings : CommandSettings
    {
        [Description( "The path to the root of the GIT repository where the renaming should take place." )]
        [CommandArgument( 0, "<root>" )]
        public string RepositoryRoot { get; init; } = null!;

        [Description( "The substring to be replaced. The value is case sensitive." )]
        [CommandArgument( 0, "<original>" )]
        public string OriginalSubstring { get; init; } = null!;

        [Description( "The substring by which the original substring is replaced, or EMPTY to represent an empty string." )]
        [CommandArgument( 0, "<replacement>" )]
        public string NewSubstring { get; init; } = null!;
    }
}