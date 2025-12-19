// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using System.IO;

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
        var version = this._version;

        writer.WriteLine(
            $$"""
              RUN Invoke-WebRequest -Uri "https://nodejs.org/dist/v{{version}}/node-v{{version}}-win-x64.zip" -OutFile node.zip; `
                  Expand-Archive node.zip -DestinationPath C:\; `
                  Rename-Item "C:\node-v{{version}}-win-x64" "C:\nodejs"; `
                  Remove-Item node.zip

              ENV PATH="C:\nodejs;${PATH}"
              """ );
    }
}