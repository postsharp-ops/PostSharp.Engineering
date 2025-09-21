// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using JetBrains.Annotations;
using PostSharp.Engineering.BuildTools.Build;
using PostSharp.Engineering.BuildTools.ContinuousIntegration.Model;
using PostSharp.Engineering.BuildTools.Utilities;
using System;
using System.Collections.Generic;
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

    public bool Prepare( BuildContext context )
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

        // Order components.
        var orderedComponents = allComponents.OrderBy( x => x ).ToList();

        var dockerfilePath = Path.Combine( context.RepoDirectory, "Dockerfile" );
        context.Console.WriteMessage( $"Writing '{dockerfilePath}'." );
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
}