// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using PostSharp.Engineering.BuildTools.Build;

namespace PostSharp.Engineering.BuildTools.Dependencies.Model;

public record DependencyConfiguration( DependencyDefinition Definition, BuildConfiguration Configuration )
{
    /// <summary>
    /// Gets the <see cref="ParametrizedDependency"/> at the consumer's use site, when this configuration was produced
    /// from one. <c>null</c> when the configuration was constructed without parametrized info.
    /// </summary>
    /// <remarks>
    /// Excluded from record equality via the <see cref="Equals(DependencyConfiguration?)"/> and
    /// <see cref="GetHashCode"/> overrides below. Two <see cref="DependencyConfiguration"/> instances with the same
    /// <see cref="Definition"/> and <see cref="Configuration"/> compare equal regardless of the parametrized reference,
    /// preserving HashSet-based deduplication semantics in <c>DependencyDefinition.GetAllDependencies</c>.
    /// </remarks>
    public ParametrizedDependency? Parametrized { get; init; }

    /// <summary>
    /// Gets the consumer-side key: <see cref="ParametrizedDependency.Key"/> when <see cref="Parametrized"/> is set,
    /// otherwise <see cref="DependencyDefinition.Name"/>.
    /// </summary>
    public string Key => this.Parametrized?.Key ?? this.Definition.Name;

    /// <summary>
    /// Gets <see cref="Key"/> with dots removed, for use in MSBuild property names.
    /// </summary>
    public string KeyWithoutDot => this.Parametrized?.KeyWithoutDot ?? this.Definition.NameWithoutDot;

    /// <summary>
    /// Gets the artifact pickup mode at the consumer's use site, defaulting to <see cref="DependencyArtifactPickup.Snapshot"/>.
    /// </summary>
    public DependencyArtifactPickup ArtifactPickup => this.Parametrized?.ArtifactPickup ?? DependencyArtifactPickup.Snapshot;

    public virtual bool Equals( DependencyConfiguration? other )
        => other is not null
           && ReferenceEquals( this.Definition, other.Definition )
           && this.Configuration == other.Configuration;

    public override int GetHashCode() => System.HashCode.Combine( this.Definition, this.Configuration );
}
