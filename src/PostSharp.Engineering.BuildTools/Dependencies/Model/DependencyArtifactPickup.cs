// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

namespace PostSharp.Engineering.BuildTools.Dependencies.Model;

/// <summary>
/// Specifies how a referenced dependency's artifacts should be picked up by the consumer's CI build.
/// </summary>
public enum DependencyArtifactPickup
{
    /// <summary>
    /// Default: a TeamCity snapshot dependency is generated, chaining the producer's build before the consumer's.
    /// The consumer always builds against artifacts from a freshly-built (or reused) producer build.
    /// </summary>
    Snapshot,

    /// <summary>
    /// Only an artifact dependency is generated, with <c>buildRule = lastSuccessful()</c>. The producer is not
    /// chained as a snapshot dependency. Use this when the producer is a frozen / released line and the consumer
    /// only wants the last published artifacts.
    /// </summary>
    LastSuccessful
}
