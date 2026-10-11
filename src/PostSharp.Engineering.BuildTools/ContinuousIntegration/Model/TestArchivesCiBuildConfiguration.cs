// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using PostSharp.Engineering.BuildTools.Build;
using PostSharp.Engineering.BuildTools.Build.Model;
using PostSharp.Engineering.BuildTools.Build.Testing;
using PostSharp.Engineering.BuildTools.ContinuousIntegration.TeamCity;
using PostSharp.Engineering.BuildTools.ContinuousIntegration.TeamCity.Generation;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace PostSharp.Engineering.BuildTools.ContinuousIntegration.Model;

/// <summary>
/// A build configuration that runs test archives with the generated <c>RunTests.ps1</c>. <see cref="TestArchiveCells"/>
/// creates them from <see cref="Build.Model.Product.TestAgents"/> and the test applications of the product.
/// </summary>
/// <remarks>
/// It downloads the archives that its snapshot dependency names, and the packages of its applications that are not from
/// nuget.org, from the builds that publish them, and nothing else. It runs what the build published and builds nothing, so it
/// does not take the other artifacts of the products this product depends on, nor write the configuration files that a build
/// would read.
/// </remarks>
internal sealed class TestArchivesCiBuildConfiguration : PowershellAdditionalCiBuildConfiguration
{
    public TestArchivesCiBuildConfiguration( string id, string name, string arguments ) : base( id, name, TestArchives.ScriptName, arguments ) { }

    internal override string GetScriptPath( ProductProperties productProperties ) => $"{productProperties.Product.EngineeringDirectory}/{TestArchives.ScriptName}";

    internal override bool PreparesBuildEnvironment => false;

    /// <summary>
    /// Gets the build configuration of the product whose archives the configuration runs, which gives the builds of the
    /// dependencies too.
    /// </summary>
    public BuildConfiguration ArtifactsConfiguration { get; init; }

    /// <summary>
    /// Gets the artifact rules that download packages from the build of each dependency, by the key of the dependency.
    /// </summary>
    public ImmutableDictionary<string, string[]> PackageArtifactRules { get; init; } = ImmutableDictionary<string, string[]>.Empty;

    internal override IReadOnlyList<TeamCitySnapshotDependency> GetProductDependencySnapshots( Product product )
        => this.PackageArtifactRules
            .OrderBy( r => r.Key, StringComparer.Ordinal )
            .Select(
                r => ConfigurationProperties.CreateDependencySnapshot(
                    TestArchiveCells.GetDependency( product, this.ArtifactsConfiguration, r.Key ),
                    this.ArtifactsConfiguration,
                    string.Join( @"\n", r.Value ) ) )
            .ToList();
}
