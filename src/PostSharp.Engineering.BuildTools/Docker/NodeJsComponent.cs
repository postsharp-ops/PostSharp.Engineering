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

    public string Version => this._version;

    public override string Name => "Install Node.js";

    public override ContainerComponentKind Kind => ContainerComponentKind.NodeJs;

    public override void WriteDockerfile( TextWriter writer )
    {
        // Install Node.js directly from official source for reliable path handling
        // Chocolatey's nodejs package has PATH issues in Windows containers
        var majorVersion = this._version.Split( '.' )[0];

        var version = this._version;

        writer.WriteLine(
            $$"""
             # Install Node.js {{version}} directly
             RUN Invoke-WebRequest -Uri "https://nodejs.org/dist/v{{version}}/node-v{{version}}-win-x64.zip" -OutFile node.zip; `
                 Expand-Archive node.zip -DestinationPath C:\; `
                 Rename-Item "C:\node-v{{version}}-win-x64" "C:\nodejs"; `
                 Remove-Item node.zip

             # Add Node.js to PATH using ENV directive (persists across shell switches)
             ENV PATH="C:\nodejs;${PATH}"
             """ );
    }

    public override void AddRequirements( IReadOnlyList<ContainerComponent> components, Action<ContainerComponent> add )
    {
        base.AddRequirements( components, add );
        // Node.js is installed directly from nodejs.org, no Chocolatey dependency needed
    }
}