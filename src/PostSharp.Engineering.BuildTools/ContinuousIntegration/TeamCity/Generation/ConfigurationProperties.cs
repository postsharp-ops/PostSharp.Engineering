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
            .Select( d => new TeamCitySnapshotDependency(
                         d.Definition.CiConfiguration.BuildTypes[d.Configuration],
                         true,
                         $"+:{d.Definition.GetPrivateArtifactsDirectory( d.Configuration ).Replace( Path.DirectorySeparatorChar, '/' )}/**/*=>dependencies/{d.Key}",
                         ReuseBuilds: d.ArtifactPickup == DependencyArtifactPickup.LastSuccessful ? ReuseBuilds.LastSuccessful : ReuseBuilds.Default ) )
            .ToList();

        var sourceSnapshotDependencies = product.SourceDependencies.Where( d => d.GenerateSnapshotDependency )
            .Select( d => new TeamCitySnapshotDependency( d.CiConfiguration.BuildTypes[configuration], true ) );

        this.SnapshotDependenciesForBuildConfiguration = snapshotDependencies.Concat( sourceSnapshotDependencies ).OrderBy( d => d.ObjectId ).ToArray();
    }
}