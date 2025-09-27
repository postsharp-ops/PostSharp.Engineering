// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using PostSharp.Engineering.BuildTools.Build;
using PostSharp.Engineering.BuildTools.Build.Files;
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

    public TeamCitySnapshotDependency[] BuildDependencies { get; }

   
    public string PrivateArtifactsDirectory { get; }

    public BuildConfigurationInfo BuildConfigurationInfo => this._product.Configurations[this.Configuration];

    public ConfigurationProperties( Product product, BuildConfiguration configuration, DependenciesConfigurationFile dependenciesOverrideFile )
    {
        this._product = product;
        this.Configuration = configuration;

        // Calculate configuration-specific artifact directory
        this.PrivateArtifactsDirectory = product.GetPrivateArtifactsRelativeDirectory( configuration ).Replace( "\\", "/", StringComparison.Ordinal );

        var dependencies =
            dependenciesOverrideFile.Dependencies.Select( x => (Name: x.Key,
                                                                Definition: product.ProductFamily.GetDependencyDefinition( x.Key ),
                                                                Source: x.Value) )
                .Where( d => d.Definition.GenerateSnapshotDependency )
                .Select( x => (x.Name, x.Definition, Configuration: VersionFileHelper.GetDependencyConfiguration( x.Definition, x.Source )) )
                .ToList();

        var snapshotDependencies = dependencies
            .Select( d => new TeamCitySnapshotDependency(
                         d.Definition.CiConfiguration.BuildTypes[d.Configuration],
                         true,
                         $"+:{d.Definition.GetPrivateArtifactsDirectory( d.Configuration ).Replace( Path.DirectorySeparatorChar, '/' )}/**/*=>dependencies/{d.Name}" ) )
            .ToList();

        var sourceSnapshotDependencies = product.SourceDependencies.Where( d => d.GenerateSnapshotDependency )
            .Select( d => new TeamCitySnapshotDependency( d.CiConfiguration.BuildTypes[configuration], true ) );

        this.BuildDependencies = snapshotDependencies.Concat( sourceSnapshotDependencies ).OrderBy( d => d.ObjectId ).ToArray();

  
    }
}