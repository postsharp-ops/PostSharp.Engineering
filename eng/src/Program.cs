// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using PostSharp.Engineering.BuildTools;
using PostSharp.Engineering.BuildTools.Build;
using PostSharp.Engineering.BuildTools.Build.Model;
using PostSharp.Engineering.BuildTools.Build.Solutions;
using PostSharp.Engineering.BuildTools.Dependencies.Definitions;
using PostSharp.Engineering.BuildTools.Docker;

// The only SDK of the build container, and the one pinned in global.json. The libraries also target net8.0 and
// net9.0, but a target framework older than the SDK is compiled from the targeting packs that the SDK restores from
// NuGet, so no older SDK has to be installed.
var sdkVersion = DevelopmentDependencies.Family.PreferredVersions.DotNetSdk.V_10_0;

var product = new Product( DevelopmentDependencies.PostSharpEngineering )
{
    GenerateNuGetConfig = true,
    DotNetSdkVersion = new DotNetSdkVersion( sdkVersion ),
    OverriddenBuildAgentRequirements = new ContainerRequirements( ContainerHostKind.Windows )
    {
        Components =
        [
            new DotNetComponent( sdkVersion, DotNetComponentKind.Sdk )
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