// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using PostSharp.Engineering.BuildTools;
using PostSharp.Engineering.BuildTools.Build.Model;
using PostSharp.Engineering.BuildTools.Build.Solutions;
using PostSharp.Engineering.BuildTools.Dependencies.Definitions;
using PostSharp.Engineering.BuildTools.Docker;
using Spectre.Console.Cli;

const string sdkVersion = "9.0.305";
var product = new Product( DevelopmentDependencies.PostSharpEngineering )
{
    GenerateNuGetConfig = true,
    DotNetSdkVersion = new DotNetSdkVersion( sdkVersion ),
    OverriddenBuildAgentRequirements = new ContainerRequirements( ContainerHostKind.Windows )
    {
        Components = [ new DotNetComponent( sdkVersion, DotNetComponentKind.Sdk )]
    },
    Solutions =
    [
        new DotNetSolution( "PostSharp.Engineering.sln" ) { SupportsTestCoverage = true, CanFormatCode = true }
    ],
    PublicArtifacts = Pattern.Create(
        "PostSharp.Engineering.Sdk.$(PackageVersion).nupkg",
        "PostSharp.Engineering.BuildTools.$(PackageVersion).nupkg",
        "PostSharp.Engineering.DocFx.$(PackageVersion).nupkg" ),
    RequiresEngineeringSdk = false,
    ExportedProperties = { { "Directory.Packages.props", ["DocFxVersion"] } },
    IsPublishingNonReleaseBranchesAllowed = true
};

var app = new EngineeringApp( product );

return app.Run( args );