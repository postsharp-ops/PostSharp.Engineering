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
}