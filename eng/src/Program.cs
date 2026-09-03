// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using PostSharp.Engineering.BuildTools;
using PostSharp.Engineering.BuildTools.Build;
using PostSharp.Engineering.BuildTools.Build.Model;
using PostSharp.Engineering.BuildTools.Build.Solutions;
using PostSharp.Engineering.BuildTools.Dependencies.Definitions;
using PostSharp.Engineering.BuildTools.Docker;

var preferredVersions = DevelopmentDependencies.Family.PreferredVersions;

// The primary SDK, i.e. the one pinned in global.json and used to build the product.
var sdkVersion = preferredVersions.DotNetSdk.V_10_0;

// Kept on the build agent so that .NET 9 remains a supported target framework.
var legacySdkVersion = preferredVersions.DotNetSdk.V_9_0;

var product = new Product( DevelopmentDependencies.PostSharpEngineering )
{
    GenerateNuGetConfig = true,
    DotNetSdkVersion = new DotNetSdkVersion( sdkVersion ),
    OverriddenBuildAgentRequirements = new ContainerRequirements( ContainerHostKind.Windows )
    {
        Components =
        [
            new DotNetComponent( sdkVersion, DotNetComponentKind.Sdk ),
            new DotNetComponent( legacySdkVersion, DotNetComponentKind.Sdk )
        ]
    },
    Solutions =
    [
        new DotNetSolution( "PostSharp.Engineering.sln" ) { SupportsTestCoverage = true, CanFormatCode = true }
    ],
    Configurations = Product.DefaultConfigurations
        .WithValue( BuildConfiguration.Debug, c => c with { ExportsToTeamCityBuild = false } )
        .WithValue( BuildConfiguration.Release, c => c with { ExportsToTeamCityBuild = false } ),
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