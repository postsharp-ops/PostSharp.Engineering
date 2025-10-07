// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PostSharp.Engineering.BuildTools.Docker;

public class GulpComponent : ContainerComponent
{
    public override string Name => "Install Gulp";

    public override ContainerComponentKind Kind => ContainerComponentKind.Gulp;

    public override void WriteDockerfile( TextWriter writer )
    {
        writer.WriteLine(
            """
            RUN npm install --global gulp-cli gulp
            """ );
    }

    public override void AddRequirements( IReadOnlyList<ContainerComponent> components, Action<ContainerComponent> add )
    {
        base.AddRequirements( components, add );

        if ( !components.OfType<NodeJsComponent>().Any() )
        {
            throw new InvalidOperationException( $"{nameof(NodeJsComponent)} is required." );
        }
    }
}