// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using PostSharp.Engineering.BuildTools.Build;
using System;

namespace PostSharp.Engineering.BuildTools.Dependencies.Model;

/// <summary>
/// Represents a dependency including the parameter values that can be supplied by the referencing project.
/// </summary>
public record ParametrizedDependency
{
    /// <summary>
    /// Represents a dependency including the parameter values that can be supplied by the referencing project.
    /// </summary>
    /// <param name="definition"></param>
    public ParametrizedDependency( DependencyDefinition definition )
    {
        this.Definition = definition ?? throw new ArgumentNullException( nameof(definition) );
    }

    public ConfigurationSpecific<BuildConfiguration> ConfigurationMapping { get; init; } = new(
        BuildConfiguration.Debug,
        BuildConfiguration.Release,
        BuildConfiguration.Public );

    public string Name => this.Definition.Name;

    public string NameWithoutDot => this.Definition.NameWithoutDot;

    /// <summary></summary>
    public DependencyDefinition Definition { get; init; }

    public static implicit operator ParametrizedDependency( DependencyDefinition definition ) => new( definition );

    public void Deconstruct( out DependencyDefinition Definition )
    {
        Definition = this.Definition;
    }
}