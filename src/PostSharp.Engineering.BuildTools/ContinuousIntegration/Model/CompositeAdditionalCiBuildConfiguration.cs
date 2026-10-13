// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using JetBrains.Annotations;
using PostSharp.Engineering.BuildTools.Build;
using PostSharp.Engineering.BuildTools.ContinuousIntegration.TeamCity;
using PostSharp.Engineering.BuildTools.ContinuousIntegration.TeamCity.Generation;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace PostSharp.Engineering.BuildTools.ContinuousIntegration.Model;

/// <summary>
/// A build configuration that runs nothing itself and instead aggregates others, so that a whole group can be
/// started, and its result read, as one.
/// </summary>
/// <remarks>
/// A product that partitions its test suite over many cells needs an entry point for each group; without one, a
/// person wanting "all the .NET 8 tests" has to start a dozen configurations by hand and then read a dozen
/// results. TeamCity calls this a composite build: it has no agent and no steps, only snapshot dependencies on
/// its children, and it succeeds exactly when all of them do.
/// </remarks>
[PublicAPI]
public class CompositeAdditionalCiBuildConfiguration : AdditionalCiBuildConfiguration
{
    public CompositeAdditionalCiBuildConfiguration( string id, string name, params string[] dependencyIds ) : base( id, name )
    {
        this.DependencyIds = dependencyIds;
    }

    /// <summary>
    /// Gets the identifiers of the configurations this one aggregates.
    /// </summary>
    public IReadOnlyList<string> DependencyIds { get; }

    internal override TeamCityBuildConfiguration TeamCityBuildConfiguration(
            ProductProperties productProperties,
            IReadOnlyDictionary<BuildConfiguration, TeamCityBuildConfiguration> teamCityBuildBuildConfigurations )

        // The build agent requirements are deliberately left null: that is what makes the generated configuration
        // composite, and a composite that carried any build step would be rejected when the code is written.
        => new(
            this.Id,
            this.Name,
            this.Branch ?? productProperties.Branch,
            productProperties.VcsId )
        {
            // Empty rather than null: the generator dereferences the step array unconditionally.
            BuildSteps = [],
            SnapshotDependencies = this.GetSnapshotDependencies()
                .Select( d => d.ToTeamCitySnapshotDependency( d.ConfigurationId!, "", this.EffectiveReuseLastSuccessfulBuild ) )
                .ToArray(),
            Parameters = this.Parameters,
            TimeoutInMinutes = this.TimeoutInMinutes,
            BuildTriggers = this.BuildTriggers
        };

    /// <summary>
    /// Gets the configurations this one aggregates, as dependencies. A composite downloads nothing from them, hence
    /// the empty rule set: it waits for them and reports their combined result. Expressing the edges this way is
    /// what puts them in reach of the validation that rejects an unknown identifier or a cycle.
    /// </summary>
    internal override ImmutableArray<SnapshotDependency> GetSnapshotDependencies()
        => [.. this.DependencyIds.Select( id => new SnapshotDependency( id ) { ArtifactRules = [] } )];

    /// <summary>
    /// Always <c>false</c>. A composite waits for its children and downloads nothing from them, and reusing their
    /// last successful build would suppress the wait, which is the only thing a composite does.
    /// </summary>
    internal override bool EffectiveReuseLastSuccessfulBuild => false;
}