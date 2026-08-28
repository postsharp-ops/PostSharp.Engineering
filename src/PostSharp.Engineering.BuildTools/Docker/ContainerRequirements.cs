// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using JetBrains.Annotations;
using PostSharp.Engineering.BuildTools.Build;
using PostSharp.Engineering.BuildTools.Utilities;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;

namespace PostSharp.Engineering.BuildTools.Docker;

/// <summary>
/// Indicates that the build script must run within a Docker container.
/// </summary>
[PublicAPI]
public record ContainerRequirements : ContainerHostRequirements
{
    public ContainerRequirements( ContainerHostKind hostKind ) : base( hostKind ) { }

    public ContainerComponent[] Components { get; init; } = [];

    public string? ImageName { get; init; }

    /// <summary>
    /// Gets the operating system of the generated image chain. Each component emits the form appropriate to
    /// this value, so a product can declare a Linux chain beside its Windows one.
    /// </summary>
    public ContainerOperatingSystem OperatingSystem { get; init; } = ContainerOperatingSystem.Windows2025;

    /// <summary>
    /// Gets the external base image of the chain root, e.g. <c>ubuntu:22.04</c>. When null, the default for
    /// <see cref="OperatingSystem"/> is used.
    /// </summary>
    public string? BaseImage { get; init; }

    public override bool IsDockerized => true;

    /// <summary>
    /// Emits the chain of Dockerfiles for one image family (the main product, or an additional Dockerfile).
    /// Components are partitioned by <see cref="ContainerComponent.Layer"/>; each active layer is written as its
    /// own <c>&lt;stem&gt;.Dockerfile</c> building <c>FROM</c> its nearest active ancestor (the lowest active
    /// layer builds from the external OS base). The Claude layer is always part of the chain.
    /// </summary>
    /// <remarks>
    /// The Dockerfile <b>file name</b> (stem) carries no product/version prefix - it is just the layer name
    /// (e.g. <c>build.Dockerfile</c>), or <c>&lt;additionalName&gt;-&lt;layer&gt;.Dockerfile</c> for an additional
    /// chain. The product/version prefix lives only in the <b>image tag</b>, which <c>DockerBuild.ps1</c> forms
    /// as <c>&lt;DOCKER_IMAGE_PREFIX&gt;-&lt;stem&gt;:&lt;hash&gt;</c>.
    /// </remarks>
    public bool WriteDockerfiles(
        BuildContext context,
        string? additionalName,
        ContainerComponent[] extraComponents,
        bool validateBuildComponents )
    {
        var dockerContextRoot = Path.Combine( context.RepoDirectory, context.Product.EngineeringDirectory, "docker-context" );
        Directory.CreateDirectory( dockerContextRoot );

        var dockerfilesDir = Path.Combine( context.RepoDirectory, context.Product.EngineeringDirectory, "docker" );
        Directory.CreateDirectory( dockerfilesDir );

        // The file stem (also the context-dir name and the ARG BASE_IMAGE reference) is prefix-free: just the
        // layer name, or "<additionalName>-<layer>" to disambiguate additional chains.
        string Stem( string layerName )
            => additionalName == null ? layerName : $"{additionalName.ToLowerInvariant()}-{layerName}";

        // Assemble components. The prolog is layer-specific and is prepended per layer below, so it is not here.
        // The Claude layer is always part of the chain.
        var allComponents = new List<ContainerComponent>
        {
            new PowershellComponent(), new GitComponent(), new EpilogueComponent(), new ClaudeComponent(), new ClaudeAddInsComponent()
        };

        allComponents.AddRange( this.Components );
        allComponents.AddRange( extraComponents );

        void Add( ContainerComponent c )
        {
            allComponents.Add( c );
            c.AddRequirements( allComponents, Add );
        }

        // Resolve component requirements (recursive).
        foreach ( var component in allComponents.ToList() )
        {
            component.AddRequirements( allComponents, Add );
        }

        // A layer's own components are magically added whenever the layer is active. Loop until stable, since an
        // own component can activate a further layer.
        bool addedOwnComponent;

        do
        {
            addedOwnComponent = false;
            var activeLayerNames = allComponents.Select( c => c.Layer ).Distinct().ToList();

            foreach ( var layerName in activeLayerNames )
            {
                foreach ( var ownComponent in ContainerLayers.Get( layerName ).OwnComponents )
                {
                    if ( allComponents.All( c => c.Key != ownComponent.Key ) )
                    {
                        Add( ownComponent );
                        addedOwnComponent = true;
                    }
                }
            }
        }
        while ( addedOwnComponent );

        // Deduplicate components by key (last occurrence wins).
        var seen = new HashSet<string>();
        var deduplicatedComponents = new List<ContainerComponent>();

        for ( var i = allComponents.Count - 1; i >= 0; i-- )
        {
            if ( seen.Add( allComponents[i].Key ) )
            {
                deduplicatedComponents.Add( allComponents[i] );
            }
        }

        deduplicatedComponents.Reverse();
        allComponents = deduplicatedComponents;

        // Validate publishers and testers (only for the main build image).
        if ( validateBuildComponents )
        {
            var hasMissingRequirement = false;

            foreach ( var buildComponent in context.Product.GetBuildComponents() )
            {
                hasMissingRequirement = !buildComponent.VerifyContainerRequirements( context, this );
            }

            if ( hasMissingRequirement )
            {
                return false;
            }
        }

        // Partition components by layer.
        var componentsByLayer = allComponents.GroupBy( c => c.Layer ).ToDictionary( g => g.Key, g => g.ToList() );

        // Walk the standard chain root → leaf and emit each active layer.
        foreach ( var layer in ContainerLayers.StandardChain )
        {
            if ( !componentsByLayer.TryGetValue( layer.Name, out var layerComponents ) )
            {
                continue;
            }

            // Resolve the actual parent: the nearest active ancestor (null ⇒ this layer is the chain root).
            string? parentStem = null;

            for ( var ancestor = layer.PreferredParentLayer; ancestor != null; ancestor = ContainerLayers.Get( ancestor ).PreferredParentLayer )
            {
                if ( componentsByLayer.ContainsKey( ancestor ) )
                {
                    parentStem = Stem( ancestor );

                    break;
                }
            }

            var stem = Stem( layer.Name );
            var perImageContext = Path.Combine( dockerContextRoot, stem );
            Directory.CreateDirectory( perImageContext );

            // Prepend the layer's own prolog: a root prolog (FROM the OS base) when this layer has no active
            // ancestor, otherwise a stem prolog (FROM ${BASE_IMAGE} = the parent stem).
            ContainerComponent prolog = parentStem == null
                ? new RootPrologComponent( this.BaseImage )
                : new ChildPrologComponent( parentStem );

            var orderedComponents = layerComponents
                .Prepend( prolog )
                .OrderBy( x => x )
                .ToList();

            using var dockerfileContent = new StringWriter();

            foreach ( var component in orderedComponents )
            {
                if ( !component.Validate( context, perImageContext ) )
                {
                    return false;
                }

                context.Console.WriteMessage( $"Processing component '{component.Name}' in layer '{layer.Name}'." );

                if ( component.Kind != ContainerComponentKind.Prolog )
                {
                    dockerfileContent.WriteLine();
                    dockerfileContent.WriteLine();
                    dockerfileContent.WriteLine( $"# {component.Name}" );
                }

                component.PopulateContextDirectory( context, perImageContext );
                component.WriteDockerfile( dockerfileContent, this.OperatingSystem );
            }

            TextFileHelper.WriteIfDifferent( Path.Combine( dockerfilesDir, $"{stem}.Dockerfile" ), dockerfileContent.ToString(), context );
        }

        return true;
    }

    public bool RequireComponent<T>( BuildContext context )
        where T : ContainerComponent
        => this.RequireComponent<T>( context, out _ );

    public bool RequireComponent<T>( BuildContext context, [NotNullWhen( true )] out T? component )
        where T : ContainerComponent
    {
        component = this.Components.OfType<T>().SingleOrDefault();

        if ( component == null )
        {
            context.Console.WriteError( $"The {typeof(T).Name} component is required." );

            return false;
        }

        return true;
    }
}