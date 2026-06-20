// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using JetBrains.Annotations;
using System;
using System.Collections.Generic;
using System.Linq;

namespace PostSharp.Engineering.BuildTools.Docker;

/// <summary>
/// Describes a single image in a chained-Dockerfile build. Each <see cref="ContainerComponent"/> is tagged
/// with a layer name (via <see cref="ContainerComponent.Layer"/>, default <see cref="Build"/>). When a layer
/// has at least one component it is emitted as its own <c>&lt;stem&gt;.Dockerfile</c>, building
/// <c>FROM ${BASE_IMAGE}</c> of its nearest active ancestor in the chain. The lowest active layer builds
/// <c>FROM mcr.microsoft.com/windows/servercore:${WINDOWS_VERSION}</c> (the external <c>&lt;root/os&gt;</c> base).
/// </summary>
/// <remarks>
/// The standard chain is <c>vs17 → build → claude</c> (root → leaf). Referencing any component on a layer
/// "magically" pulls in that layer's <see cref="OwnComponents"/> (reusing the same mechanism as
/// <see cref="ContainerComponent.AddRequirements"/>). The layer's prolog is implicit: the emitter prepends a
/// <see cref="RootPrologComponent"/> (chain root) or a <see cref="ChildPrologComponent"/> (child) based on the
/// resolved parent.
/// </remarks>
[PublicAPI]
public sealed record ContainerLayer( string Name, string? PreferredParentLayer = null )
{
    /// <summary>
    /// Gets components that are added to the set (and forced into this layer) whenever the layer is active.
    /// </summary>
    public ContainerComponent[] OwnComponents { get; init; } = [];
}

/// <summary>
/// The well-known layers and the order in which they chain (root → leaf).
/// </summary>
public static class ContainerLayers
{
    public const string Vs17 = "vs17";
    public const string Build = "build";
    public const string Claude = "claude";

    /// <summary>
    /// The standard chain ordered root → leaf. <see cref="ContainerLayer.PreferredParentLayer"/> names the
    /// preferred parent; when that parent is inactive, the emitter links to the nearest active ancestor (or
    /// builds <c>FROM</c> the external OS base when there is none).
    /// </summary>
    public static readonly IReadOnlyList<ContainerLayer> StandardChain =
    [
        new ContainerLayer( Vs17 ),
        new ContainerLayer( Build, Vs17 ),
        new ContainerLayer( Claude, Build )
    ];

    public static ContainerLayer Get( string name )
        => StandardChain.SingleOrDefault( l => l.Name == name )
           ?? throw new InvalidOperationException(
               $"Unknown container layer '{name}'. Known layers: {string.Join( ", ", StandardChain.Select( l => l.Name ) )}." );
}
