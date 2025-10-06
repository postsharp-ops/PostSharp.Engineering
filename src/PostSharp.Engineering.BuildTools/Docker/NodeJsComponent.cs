// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PostSharp.Engineering.BuildTools.Docker;

public class NodeJsComponent : ContainerComponent
{
    private readonly string _version;

    public NodeJsComponent( string version )
    {
        this._version = version;
    }

    public override string Name => "Install Node.js";

    public override ContainerComponentKind Kind => ContainerComponentKind.NodeJs;

    public override void WriteDockerfile( TextWriter writer )
    {
        writer.WriteLine(
            $"""
             RUN choco install nodejs --version="{this._version}"
             """ );
    }

    public override void AddRequirements( IReadOnlyList<ContainerComponent> components, Action<ContainerComponent> add )
    {
        base.AddRequirements( components, add );

        if ( !components.OfType<ChocolateyComponent>().Any() )
        {
            add( new ChocolateyComponent() );
        }
    }
}