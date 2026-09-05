// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using JetBrains.Annotations;
using PostSharp.Engineering.BuildTools.Build;
using PostSharp.Engineering.BuildTools.ContinuousIntegration.TeamCity;
using PostSharp.Engineering.BuildTools.ContinuousIntegration.TeamCity.Generation;
using System.Collections.Generic;
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
            SnapshotDependencies = this.DependencyIds
                .Select( x => new TeamCitySnapshotDependency( x, false ) )
                .ToArray(),
            Parameters = this.Parameters,
            TimeoutInMinutes = this.TimeoutInMinutes,
            BuildTriggers = this.BuildTriggers
        };
}
