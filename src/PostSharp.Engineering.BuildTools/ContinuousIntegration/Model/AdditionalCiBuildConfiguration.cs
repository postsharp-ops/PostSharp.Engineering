// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using JetBrains.Annotations;
using PostSharp.Engineering.BuildTools.Build;
using PostSharp.Engineering.BuildTools.ContinuousIntegration.TeamCity;
using PostSharp.Engineering.BuildTools.ContinuousIntegration.TeamCity.Arguments;
using PostSharp.Engineering.BuildTools.ContinuousIntegration.TeamCity.Generation;
using PostSharp.Engineering.BuildTools.Docker;
using System.Collections.Generic;

namespace PostSharp.Engineering.BuildTools.ContinuousIntegration.Model;

[PublicAPI]
public abstract class AdditionalCiBuildConfiguration
{
    public string Name { get; }

    public string Id { get; }

    public string? Branch { get; init; }

    public SourceDependenciesRequirements SourceDependenciesRequirements { get; init; }

    /// <summary>
    /// Gets or sets the build configuration on which the current <see cref="AdditionalCiBuildConfiguration"/> depends.
    /// </summary>
    public BuildConfiguration? BuildSnapshotDependency { get; init; }

    /// <summary>
    /// Gets or sets a value indicating whether the snapshot dependency should accept the last successful build
    /// regardless of the current source snapshot. When <c>true</c>, TeamCity will reuse the last successful build
    /// of the dependency instead of requiring a build from the exact same source revision.
    /// </summary>
    public bool ReuseLastSuccessfulBuild { get; init; }

    public bool OnlyCheckoutEngineering { get; init; }

    protected AdditionalCiBuildConfiguration( string id, string name )
    {
        this.Id = id;
        this.Name = name;
    }

    internal abstract TeamCityBuildConfiguration TeamCityBuildConfiguration(
        ProductProperties productProperties,
        IReadOnlyDictionary<BuildConfiguration, TeamCityBuildConfiguration> teamCityBuildBuildConfigurations );

    public BuildAgentRequirements? BuildAgentRequirements { get; init; }

    public BuildConfigurationParameter[]? Parameters { get; init; }

    public string? Dockerfile { get; init; }

    /// <summary>
    /// Gets the GitHub App connection and parameter that replace the ones inherited from the repository, or <c>null</c>
    /// to use the repository's own. A build configuration issues a single build-scoped token, so setting this
    /// substitutes the identity of the token rather than adding a second one.
    /// </summary>
    public GitHubAppTokenOverride? GitHubAppToken { get; init; }
}