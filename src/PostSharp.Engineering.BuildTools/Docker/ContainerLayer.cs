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
/// The standard chain is <c>vs17|vs18 → build → claude</c> (root → leaf). Referencing any component on a layer
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
    public const string Vs18 = "vs18";
    public const string Build = "build";

    /// <summary>
    /// Returns the name of the Visual Studio Build Tools layer of a given major version, e.g. <c>vs18</c> for
    /// <c>18</c>. Each major version gets its own layer, because the Build Tools of one major version are a
    /// different installation from those of another and cannot be layered over them.
    /// </summary>
    public static string VisualStudio( string majorVersion ) => $"vs{majorVersion}";

    /// <summary>
    /// Layer for Claude's prerequisites (e.g. Node.js) that are required by Claude but not by the product itself.
    /// Sits between <see cref="Build"/> and <see cref="Claude"/> so these prerequisites stay out of the build image.
    /// </summary>
    public const string ClaudePre = "claude-pre";

    public const string Claude = "claude";

    /// <summary>
    /// The standard chain ordered root → leaf. <see cref="ContainerLayer.PreferredParentLayer"/> names the
    /// preferred parent; when that parent is inactive, the emitter links to the nearest active ancestor (or
    /// builds <c>FROM</c> the external OS base when there is none).
    /// </summary>
    /// <remarks>
    /// <para>
    /// The <see cref="ClaudePre"/> layer sits between <see cref="Build"/> and <see cref="Claude"/> so that
    /// prerequisites required <i>only by Claude</i> (e.g. Node.js) stay out of the build image that CI builds: CI
    /// builds the <see cref="Build"/> leaf (the prerequisites are a child layer, never built), while the Claude dev
    /// image picks them up. When the product itself needs Node.js (e.g. Gulp), it adds <see cref="NodeJsComponent"/>
    /// on the default <see cref="Build"/> layer instead, leaving the <see cref="ClaudePre"/> layer inactive and the
    /// chain collapsing to <c>build → claude</c>.
    /// </para>
    /// <para>
    /// <see cref="Vs17"/> and <see cref="Vs18"/> are one position of the chain, not two: a chain carries at most
    /// one <see cref="VisualStudioBuildToolsComponent"/>, so the two are never active together. They are listed
    /// as parent and child so that <see cref="Build"/>, whose preferred parent is the newest of them, walks down
    /// to whichever one the product pins, and builds from the operating system base when it pins none.
    /// </para>
    /// </remarks>
    public static readonly IReadOnlyList<ContainerLayer> StandardChain =
    [
        new ContainerLayer( Vs17 ),
        new ContainerLayer( Vs18, Vs17 ),
        new ContainerLayer( Build, Vs18 ),
        new ContainerLayer( ClaudePre, Build ),
        new ContainerLayer( Claude, ClaudePre )
    ];

    public static ContainerLayer Get( string name )
        => StandardChain.SingleOrDefault( l => l.Name == name )
           ?? throw new InvalidOperationException(
               $"Unknown container layer '{name}'. Known layers: {string.Join( ", ", StandardChain.Select( l => l.Name ) )}." );
}
