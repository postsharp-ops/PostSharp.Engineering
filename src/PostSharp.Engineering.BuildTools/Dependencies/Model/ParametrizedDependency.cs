// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using PostSharp.Engineering.BuildTools.Build;
using System;

namespace PostSharp.Engineering.BuildTools.Dependencies.Model;

/// <summary>
/// Represents a dependency including the parameter values that can be supplied by the referencing project.
/// </summary>
public record ParametrizedDependency
{
    /// <summary>
    /// Represents a dependency including the parameter values that can be supplied by the referencing project.
    /// </summary>
    /// <param name="definition"></param>
    public ParametrizedDependency( DependencyDefinition definition )
    {
        this.Definition = definition ?? throw new ArgumentNullException( nameof(definition) );
    }

    public ConfigurationSpecific<BuildConfiguration> ConfigurationMapping { get; init; } = new(
        BuildConfiguration.Debug,
        BuildConfiguration.Release,
        BuildConfiguration.Public );

    public string Name => this.Definition.Name;

    public string NameWithoutDot => this.Definition.NameWithoutDot;

    /// <summary>
    /// Gets an optional consumer-side alias that disambiguates this reference from other references to the same
    /// <see cref="DependencyDefinition.Name"/>. When set, the alias is used in place of the definition name as the key
    /// for MSBuild property names, generated config-file dictionary keys, and the per-dependency directory under <c>dependencies/</c>.
    /// </summary>
    /// <remarks>
    /// Aliases are only needed when a referencing product depends on two dependencies whose <see cref="DependencyDefinition.Name"/>
    /// would otherwise collide (e.g., the same logical product across two different <see cref="ProductFamily"/> versions).
    /// When <c>null</c>, behavior is identical to having no alias and <see cref="Key"/> falls back to <see cref="Name"/>.
    /// </remarks>
    public string? Alias { get; init; }

    /// <summary>
    /// Gets the consumer-side key for this dependency: <see cref="Alias"/> when set, otherwise <see cref="Name"/>.
    /// </summary>
    public string Key => this.Alias ?? this.Definition.Name;

    /// <summary>
    /// Gets <see cref="Key"/> with all dots removed, for use in MSBuild property names.
    /// </summary>
    public string KeyWithoutDot => this.Alias != null
        ? this.Alias.Replace( ".", "", StringComparison.Ordinal )
        : this.Definition.NameWithoutDot;

    /// <summary>
    /// Gets the artifact pickup mode for this reference. Defaults to <see cref="DependencyArtifactPickup.Snapshot"/>.
    /// </summary>
    public DependencyArtifactPickup ArtifactPickup { get; init; } = DependencyArtifactPickup.Snapshot;

    /// <summary>
    /// Gets a value indicating whether the public builds of this reference are looked up on
    /// <see cref="DependencyDefinition.PublishingBranch"/> instead of <see cref="DependencyDefinition.Branch"/>.
    /// </summary>
    /// <remarks>
    /// This is needed once a family is released: it keeps building its non-public configurations on the development
    /// branch, but it produces public builds only on the release branch, so the newest public build left on the
    /// development branch eventually gets old enough that the build server cleans up its artifacts. Only the public
    /// configuration is redirected, because the other configurations are not built on the publishing branch at all.
    /// </remarks>
    public bool UsesPublishingBranch { get; init; }

    /// <summary>
    /// Gets the branch on which the builds of this reference are looked up for a given mapped
    /// <paramref name="dependencyConfiguration"/>. See <see cref="UsesPublishingBranch"/>.
    /// </summary>
    public string GetBuildBranch( BuildConfiguration dependencyConfiguration )
        => this.UsesPublishingBranch && dependencyConfiguration == BuildConfiguration.Public
            ? this.Definition.PublishingBranch
            : this.Definition.Branch;

    /// <summary></summary>
    public DependencyDefinition Definition { get; init; }

    public static implicit operator ParametrizedDependency( DependencyDefinition definition ) => new( definition );
}

/// <summary>
/// Fluent helpers for building <see cref="ParametrizedDependency"/> values at the use site.
/// </summary>
public static class ParametrizedDependencyExtensions
{
    /// <summary>
    /// Returns a copy of <paramref name="dependency"/> with the specified consumer-side <paramref name="alias"/>.
    /// </summary>
    public static ParametrizedDependency WithAlias( this ParametrizedDependency dependency, string alias )
        => dependency with { Alias = alias };

    /// <summary>
    /// Returns a copy of <paramref name="dependency"/> with <see cref="ParametrizedDependency.ArtifactPickup"/> set to
    /// <see cref="DependencyArtifactPickup.LastSuccessful"/>: artifact-only TeamCity dependency, no snapshot.
    /// </summary>
    public static ParametrizedDependency WithLastSuccessfulOnly( this ParametrizedDependency dependency )
        => dependency with { ArtifactPickup = DependencyArtifactPickup.LastSuccessful };

    /// <summary>
    /// Returns a copy of <paramref name="dependency"/> whose builds are looked up on
    /// <see cref="DependencyDefinition.PublishingBranch"/> instead of <see cref="DependencyDefinition.Branch"/>.
    /// See <see cref="ParametrizedDependency.UsesPublishingBranch"/>.
    /// </summary>
    public static ParametrizedDependency WithPublishingBranch( this ParametrizedDependency dependency )
        => dependency with { UsesPublishingBranch = true };
}
