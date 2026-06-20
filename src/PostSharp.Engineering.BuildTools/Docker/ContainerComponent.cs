// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using PostSharp.Engineering.BuildTools.Build;
using System;
using System.Collections.Generic;
using System.IO;

namespace PostSharp.Engineering.BuildTools.Docker;

public abstract class ContainerComponent : IComparable<ContainerComponent>
{
    public abstract string Name { get; }

    /// <summary>
    /// Gets a unique key that identifies this component instance, including all parameters.
    /// Used for deduplication: two components with the same key are considered identical.
    /// </summary>
    public virtual string Key => this.GetType().Name;

    public abstract ContainerComponentKind Kind { get; }

    /// <summary>
    /// Gets the name of the chained-image layer this component belongs to. Defaults to
    /// <see cref="ContainerLayers.Build"/>. Components on a non-default layer (e.g. VS Build Tools on
    /// <see cref="ContainerLayers.Vs17"/>, Claude on <see cref="ContainerLayers.Claude"/>) cause that layer
    /// to be emitted as its own image in the chain.
    /// </summary>
    public virtual string Layer => ContainerLayers.Build;

    public abstract void WriteDockerfile( TextWriter writer, ContainerOperatingSystem operatingSystem );

    public virtual void AddRequirements( IReadOnlyList<ContainerComponent> components, Action<ContainerComponent> add ) { }

    public virtual bool Validate( BuildContext context, string contextDirectory ) => true;

    public virtual int CompareTo( ContainerComponent? other )
    {
        if ( ReferenceEquals( this, other ) )
        {
            return 0;
        }

        if ( other == null )
        {
            return 1;
        }

        return ((int) this.Kind).CompareTo( (int) other.Kind );
    }

    public virtual void PopulateContextDirectory( BuildContext context, string directory ) { }

    public override string ToString() => this.Kind.ToString();
}