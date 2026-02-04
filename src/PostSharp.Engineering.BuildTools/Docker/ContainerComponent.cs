// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using PostSharp.Engineering.BuildTools.Build;
using System;
using System.Collections.Generic;
using System.IO;

namespace PostSharp.Engineering.BuildTools.Docker;

public abstract class ContainerComponent : IComparable<ContainerComponent>
{
    public abstract string Name { get; }

    public abstract ContainerComponentKind Kind { get; }

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