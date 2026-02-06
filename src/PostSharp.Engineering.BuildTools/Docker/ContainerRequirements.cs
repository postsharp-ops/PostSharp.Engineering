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

    public override bool IsDockerized => true;

    public bool WriteAllVariants(
        BuildContext context,
        string suffix,
        ContainerComponent[] extraComponents )
    {
        var name = string.IsNullOrEmpty( suffix ) ? "Dockerfile" : $"Dockerfile.{suffix}";
        var claudeComponents = new ContainerComponent[] { new ClaudeComponent(), new ClaudeAddInsComponent() };
        var validate = string.IsNullOrEmpty( suffix );

        // Win2025
        if ( !this.WriteDockerfileCore(
                context,
                name,
                ContainerOperatingSystem.Windows2025,
                extraComponents,
                validate ) )
        {
            return false;
        }

        // Win2022
        if ( !this.WriteDockerfileCore(
                context,
                $"{name}.win2022",
                ContainerOperatingSystem.Windows2022,
                extraComponents,
                false ) )
        {
            return false;
        }

        // Win2025 + Claude
        if ( !this.WriteDockerfileCore(
                context,
                $"{name}.claude",
                ContainerOperatingSystem.Windows2025,
                [..extraComponents, ..claudeComponents],
                false ) )
        {
            return false;
        }

        // Win2022 + Claude
        if ( !this.WriteDockerfileCore(
                context,
                $"{name}.claude.win2022",
                ContainerOperatingSystem.Windows2022,
                [..extraComponents, ..claudeComponents],
                false ) )
        {
            return false;
        }

        return true;
    }

    private bool WriteDockerfileCore(
        BuildContext context,
        string dockerfileName,
        ContainerOperatingSystem operatingSystem,
        ContainerComponent[] additionalComponents,
        bool validateBuildComponents )
    {
        var contextDirectory = Path.Combine( context.RepoDirectory, context.Product.EngineeringDirectory, "docker-context" );

        Directory.CreateDirectory( contextDirectory );

        var dockerfilesDir = Path.Combine( context.RepoDirectory, context.Product.EngineeringDirectory, "docker" );
        Directory.CreateDirectory( dockerfilesDir );

        // Add base components
        var allComponents = new List<ContainerComponent> { new PrologComponent(), new PowershellComponent(), new GitComponent(), new EpilogueComponent() };

        allComponents.AddRange( this.Components );

        // Add additional component if specified (e.g., Claude)
        foreach ( var additionalComponent in additionalComponents )
        {
            allComponents.Add( additionalComponent );
        }

        // Add required components
        foreach ( var component in allComponents.ToList() )
        {
            void Add( ContainerComponent c )
            {
                allComponents.Add( c );
                c.AddRequirements( allComponents, Add );
            }

            component.AddRequirements( allComponents, Add );
        }

        // Deduplicate components by key (last occurrence wins)
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

        // Validate components
        foreach ( var component in allComponents )
        {
            if ( !component.Validate( context, contextDirectory ) )
            {
                return false;
            }
        }

        // Validate publishers and testers (only for base Dockerfile)
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

        // Order components
        var orderedComponents = allComponents.OrderBy( x => x ).ToList();

        var dockerfilePath = Path.Combine( dockerfilesDir, dockerfileName );
        using var dockerfileContent = new StringWriter();

        foreach ( var component in orderedComponents )
        {
            context.Console.WriteMessage( $"Processing component '{component.Name}'." );

            if ( component.Kind != ContainerComponentKind.Prolog )
            {
                dockerfileContent.WriteLine();
                dockerfileContent.WriteLine();
                dockerfileContent.WriteLine( $"# {component.Name}" );
            }

            component.PopulateContextDirectory( context, contextDirectory );
            component.WriteDockerfile( dockerfileContent, operatingSystem );
        }

        TextFileHelper.WriteIfDifferent( dockerfilePath, dockerfileContent.ToString(), context );

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