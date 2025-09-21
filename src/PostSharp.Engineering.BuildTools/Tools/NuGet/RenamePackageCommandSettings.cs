// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using JetBrains.Annotations;
using Spectre.Console.Cli;
using System.ComponentModel;

namespace PostSharp.Engineering.BuildTools.Tools.NuGet;

/// <summary>
/// Settings for <see cref="RenamePackagesCommand"/>.
/// </summary>
[PublicAPI]
public class RenamePackageCommandSettings : CommandSettings
{
    [Description( "Directory containing the packages" )]
    [CommandArgument( 0, "<directory>" )]
    public string Directory { get; init; } = null!;
}