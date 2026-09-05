// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using JetBrains.Annotations;
using System.Collections.Generic;
using System.Linq;

namespace PostSharp.Engineering.BuildTools.ContinuousIntegration.Model;

public enum RequirementComparisonType
{
    Equals,
    Matches,

    /// <summary>
    /// The parameter is numerically greater than the value. Used to keep a build off under-provisioned agents,
    /// for example <c>teamcity.agent.hardware.memorySizeMb</c> greater than <c>8192</c>.
    /// </summary>
    MoreThan,

    /// <summary>
    /// The parameter does not contain the value. Needed where no positive match expresses the exclusion: the
    /// TeamCity JVM on Windows-on-ARM runs under x64 emulation and therefore reports <c>os.arch=amd64</c>, so an
    /// x64-only build is kept off that agent by requiring <c>env.PROCESSOR_IDENTIFIER</c> not to contain
    /// <c>ARMv8</c>.
    /// </summary>
    DoesNotContain
}

public record BuildAgentRequirement( string Name, string Value, RequirementComparisonType ComparisonType = RequirementComparisonType.Equals );

[PublicAPI]
public record BuildAgentRequirements
{
    public BuildAgentRequirements( params BuildAgentRequirement[] items )
    {
        this.Items = items;
    }

    public static BuildAgentRequirements Empty { get; } = new();

    public static BuildAgentRequirements Default { get; } = SelfHosted( "caravela04cloud" );

    public static BuildAgentRequirements SelfHosted( string name ) => new( new BuildAgentRequirement( "env.BuildAgentType", name ) );

    public static BuildAgentRequirements JetBrainsHosted( string name ) => new( new BuildAgentRequirement( "teamcity.agent.name", name ) );

    public IReadOnlyList<BuildAgentRequirement> Items { get; init; }

    public BuildAgentRequirements Combine( BuildAgentRequirements other ) => new( this.Items.Concat( other.Items ).ToArray() );

    public virtual bool IsDockerized => false;
}