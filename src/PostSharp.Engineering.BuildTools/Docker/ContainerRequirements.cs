// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using JetBrains.Annotations;
using PostSharp.Engineering.BuildTools.Build;
using PostSharp.Engineering.BuildTools.ContinuousIntegration.Model;
using PostSharp.Engineering.BuildTools.Utilities;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;

namespace PostSharp.Engineering.BuildTools.Docker;

[PublicAPI]
public record ContainerRequirements : BuildAgentRequirements
{
    public ContainerRequirements( ContainerHostKind hostKind ) : base( new BuildAgentRequirement( "env.BuildAgentType", GetBuildAgentType( hostKind ) ) ) { }

    public ContainerComponent[] Components { get; init; } = [];

    private static string GetBuildAgentType( ContainerHostKind hostKind )
        => hostKind switch
        {
            ContainerHostKind.Windows => "docker-win-x64-md",
            _ => throw new ArgumentOutOfRangeException( nameof(hostKind) )
        };

    public override bool IsDockerized => true;

    public bool WriteDockerfile( BuildContext context )
    {
        var contextDirectory = Path.Combine( context.RepoDirectory, context.Product.EngineeringDirectory, "docker-context" );

        Directory.CreateDirectory( contextDirectory );

        // Add components.
        var allComponents = new List<ContainerComponent>();
        allComponents.Add( new PrologComponent() );
        allComponents.Add( new PowershellComponent() );
        allComponents.Add( new GitComponent() );
        allComponents.Add( new EpilogueComponent() );
        allComponents.AddRange( this.Components );

        // Add required components.
        foreach ( var component in allComponents.ToList() )
        {
            void Add( ContainerComponent c )
            {
                allComponents.Add( c );
                c.AddRequirements( allComponents, Add );
            }

            component.AddRequirements( allComponents, Add );
        }

        // Validate components.
        foreach ( var component in allComponents )
        {
            if ( !component.Validate( context, contextDirectory ) )
            {
                return false;
            }
        }

        // Validate publishers and testers.
        var hasMissingRequirement = false;

        foreach ( var buildComponent in context.Product.GetBuildComponents() )
        {
            hasMissingRequirement = !buildComponent.VerifyContainerRequirements( context, this );
        }

        if ( hasMissingRequirement )
        {
            return false;
        }

        // Order components.
        var orderedComponents = allComponents.OrderBy( x => x ).ToList();

        var dockerfilePath = Path.Combine( context.RepoDirectory, "Dockerfile" );
        using var dockerfileContent = new StringWriter();

        foreach ( var component in orderedComponents )
        {
            context.Console.WriteMessage( $"Processing container component '{component.Name}'." );

            if ( component.Kind != ContainerComponentKind.Prolog )
            {
                dockerfileContent.WriteLine();
                dockerfileContent.WriteLine();
                dockerfileContent.WriteLine( $"# {component.Name}" );
            }

            component.PopulateContextDirectory( context, contextDirectory );
            component.WriteDockerfile( dockerfileContent );
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

    public bool WriteClaudeDockerfile( BuildContext context )
    {
        // Start with base image components to check for version conflicts
        var allComponents = new List<ContainerComponent>( this.Components );

        // Add Claude component
        var claudeComponent = new ClaudeComponent();
        allComponents.Add( claudeComponent );

        // Track which components are new (not in base image)
        var newComponents = new List<ContainerComponent> { claudeComponent };

        // Use AddRequirements to auto-add NodeJs if missing, or throw if version too low
        void Add( ContainerComponent c )
        {
            allComponents.Add( c );
            newComponents.Add( c );
            c.AddRequirements( allComponents, Add );
        }

        claudeComponent.AddRequirements( allComponents, Add );

        // Order only the new components (base image components are already installed)
        var orderedNewComponents = newComponents.OrderBy( x => x ).ToList();

        // Build Claude Dockerfile that layers on top of the base image
        var dockerfilePath = Path.Combine( context.RepoDirectory, "Dockerfile.claude" );
        using var dockerfileContent = new StringWriter();

        // Write prolog for Claude Dockerfile (FROM base image)
        dockerfileContent.WriteLine(
            """
            # escape=`

            # This file is auto-generated by PostSharp.Engineering.
            # This Dockerfile builds a Claude-enabled image on top of the base product image.

            ARG BASE_IMAGE
            FROM ${BASE_IMAGE}

            """ );

        // Add only the new components (not already in base image)
        foreach ( var component in orderedNewComponents )
        {
            context.Console.WriteMessage( $"Processing Claude container component '{component.Name}'." );
            dockerfileContent.WriteLine();
            dockerfileContent.WriteLine( $"# {component.Name}" );
            component.WriteDockerfile( dockerfileContent );
        }

        TextFileHelper.WriteIfDifferent( dockerfilePath, dockerfileContent.ToString(), context );

        return true;
    }
}