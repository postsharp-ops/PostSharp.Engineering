// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using PostSharp.Engineering.BuildTools.Build;
using PostSharp.Engineering.BuildTools.Build.Model;
using PostSharp.Engineering.BuildTools.Dependencies.Model;
using System;
using System.IO;
using System.Linq;

namespace PostSharp.Engineering.BuildTools.ContinuousIntegration.TeamCity.Generation;

internal class ConfigurationProperties
{
    private readonly Product _product;

    public BuildConfiguration Configuration { get; }

    public TeamCitySnapshotDependency[] SnapshotDependenciesForBuildConfiguration { get; }

    public string PrivateArtifactsDirectory { get; }

    public BuildConfigurationInfo BuildConfigurationInfo => this._product.Configurations[this.Configuration];

    public ConfigurationProperties( Product product, BuildConfiguration configuration )
    {
        this._product = product;
        this.Configuration = configuration;

        // Calculate configuration-specific artifact directory
        this.PrivateArtifactsDirectory = product.GetPrivateArtifactsRelativeDirectory( configuration ).Replace( "\\", "/", StringComparison.Ordinal );

        var dependencies = product.DependencyDefinition.GetAllDependencies( configuration )
            .Where( d => d.Definition.GenerateSnapshotDependency )
            .ToList();

        var snapshotDependencies = dependencies
            .Select( d => CreateDependencySnapshot( d, configuration, $"+:{GetPrivateArtifactsDirectory( d )}/**/*=>dependencies/{d.Key}" ) )
            .ToList();

        var sourceSnapshotDependencies = product.SourceDependencies.Where( d => d.GenerateSnapshotDependency )
            .Select( d => new TeamCitySnapshotDependency( d.CiConfiguration.BuildTypes[configuration], true ) );

        this.SnapshotDependenciesForBuildConfiguration = snapshotDependencies.Concat( sourceSnapshotDependencies ).OrderBy( d => d.ObjectId ).ToArray();
    }

    /// <summary>
    /// Gets the directory of the private artifacts of the build of a dependency, relative to its repository, with forward slashes.
    /// </summary>
    public static string GetPrivateArtifactsDirectory( DependencyConfiguration dependency )
        => dependency.Definition.GetPrivateArtifactsDirectory( dependency.Configuration ).Replace( Path.DirectorySeparatorChar, '/' );

    /// <summary>
    /// Creates the snapshot dependency of a build configuration of the product on the build of a dependency, which takes
    /// the same build of the dependency as the build of the product in the same chain.
    /// </summary>
    public static TeamCitySnapshotDependency CreateDependencySnapshot( DependencyConfiguration dependency, BuildConfiguration configuration, string artifactRules )
        => new(
            dependency.Definition.CiConfiguration.BuildTypes[dependency.Configuration],
            true,
            artifactRules,
            ReuseBuilds: dependency.ArtifactPickup == DependencyArtifactPickup.LastSuccessful ? ReuseBuilds.LastSuccessful : ReuseBuilds.Default,
            Branch: dependency.ArtifactPickup == DependencyArtifactPickup.LastSuccessful && configuration == BuildConfiguration.Public
                ? dependency.Definition.ReleaseBranch
                : null );
}