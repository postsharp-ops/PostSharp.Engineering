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
    /// Gets the effective artifact pickup mode after propagation through the dep graph (set by
    /// <see cref="DependencyDefinition.GetAllDependencies"/>). When a transitive dep is reached only via a
    /// <see cref="DependencyArtifactPickup.LastSuccessful"/> ancestor, this is forced to <see cref="DependencyArtifactPickup.LastSuccessful"/>
    /// so the consumer doesn't generate snapshot dependencies on a subtree whose root build it doesn't trigger.
    /// </summary>
    internal DependencyArtifactPickup? EffectiveArtifactPickup { get; init; }

    /// <summary>
    /// Gets the artifact pickup mode at the consumer's use site, defaulting to <see cref="DependencyArtifactPickup.Snapshot"/>.
    /// Returns <see cref="EffectiveArtifactPickup"/> when set (via <see cref="DependencyDefinition.GetAllDependencies"/>),
    /// otherwise falls back to the parametrized dep's own setting.
    /// </summary>
    public DependencyArtifactPickup ArtifactPickup
        => this.EffectiveArtifactPickup ?? this.Parametrized?.ArtifactPickup ?? DependencyArtifactPickup.Snapshot;

    public virtual bool Equals( DependencyConfiguration? other )
        => other is not null
           && ReferenceEquals( this.Definition, other.Definition )
           && this.Configuration == other.Configuration;

    public override int GetHashCode() => System.HashCode.Combine( this.Definition, this.Configuration );
}
