// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using JetBrains.Annotations;
using System.Collections.Generic;
using System.Linq;

namespace PostSharp.Engineering.BuildTools.ContinuousIntegration.Model;

public record BuildAgentRequirement( string Name, string Value );

[PublicAPI]
public sealed record BuildAgentRequirements
{
    public BuildAgentRequirements( params BuildAgentRequirement[] items )
    {
        this.Items = items;
    }

    public static BuildAgentRequirements Empty { get; } = new();
    
    public static BuildAgentRequirements Default { get; } = BuildAgentRequirements.SelfHosted( "caravela04cloud" );
    
    public static BuildAgentRequirements WindowsDockerHost { get; } = BuildAgentRequirements.SelfHosted( "buildagent-docker-win-1", true );

    public static BuildAgentRequirements SelfHosted( string name, bool isDockerHost = false ) => new( new BuildAgentRequirement( "env.BuildAgentType", name )  ) { IsDockerHost = isDockerHost };

    public static BuildAgentRequirements JetBrainsHosted( string name ) => new( new BuildAgentRequirement( "teamcity.agent.name", name ) );

    public IReadOnlyList<BuildAgentRequirement> Items { get; init; }

    public BuildAgentRequirements Combine( BuildAgentRequirements other ) => new( this.Items.Concat( other.Items ).ToArray() );
    
    public bool IsDockerHost { get; init; }
}