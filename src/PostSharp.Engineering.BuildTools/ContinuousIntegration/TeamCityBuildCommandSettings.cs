// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using JetBrains.Annotations;
using PostSharp.Engineering.BuildTools.Build;
using Spectre.Console.Cli;
using System.ComponentModel;

namespace PostSharp.Engineering.BuildTools.ContinuousIntegration;

[PublicAPI]
public class TeamCityBuildCommandSettings : BaseBuildSettings
{
    [Description( $"The TeamCity build to schedule. (Build | Deploy | Bump)" )]
    [CommandArgument( 0, "<type>" )]
    public TeamCityBuildType TeamCityBuildType { get; set; }
    
    [Description( "Product family to schedule the build for." )]
    [CommandArgument( 1, "<family>" )]
    public string ProductFamilyName { get; set; } = null!;
    
    [Description( "Product family version to schedule the build for." )]
    [CommandArgument( 2, "<version>" )]
    public string ProductFamilyVersion { get; set; } = null!;
    
    [Description( "Product name to schedule the build for." )]
    [CommandArgument( 3, "<product>" )]
    public string ProductName { get; set; } = null!;
}