// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using System.IO;

namespace PostSharp.Engineering.BuildTools.Docker;

public class NodeJsComponent : ContainerComponent
{
    private readonly string _version;
    private readonly string _layer;

    /// <param name="version">The Node.js version to install.</param>
    /// <param name="layer">
    /// The chained-image layer to install Node.js on. Defaults to <see cref="ContainerLayers.Build"/> so that a
    /// product that needs Node.js itself (e.g. for Gulp) gets it in the build image CI builds. Claude auto-adds it
    /// on <see cref="ContainerLayers.ClaudePre"/> instead, keeping Claude-only Node.js out of the CI build image.
    /// </param>
    public NodeJsComponent( string version, string? layer = null )
    {
        this._version = version;
        this._layer = layer ?? ContainerLayers.Build;
    }

    public string Version => this._version;

    public override string Name => "Install Node.js";

    // The layer is intentionally NOT part of the key: there must be a single Node.js install regardless of which
    // component requested it. If the product already added Node.js (on the build layer), Claude reuses it rather
    // than adding a second copy on the nodejs layer (see ClaudeComponent.AddRequirements).
    public override string Key => $"{nameof(NodeJsComponent)}:{this._version}";

    public override string Layer => this._layer;

    public override ContainerComponentKind Kind => ContainerComponentKind.NodeJs;

    public override void WriteDockerfile( TextWriter writer, ContainerOperatingSystem operatingSystem )
    {
        var version = this._version;

        writer.WriteLine(
            $$"""
              RUN Invoke-WebRequest -Uri "https://nodejs.org/dist/v{{version}}/node-v{{version}}-win-x64.zip" -OutFile node.zip; `
                  Expand-Archive node.zip -DestinationPath C:\; `
                  Rename-Item "C:\node-v{{version}}-win-x64" "C:\nodejs"; `
                  Remove-Item node.zip

              ENV NPM_CONFIG_PREFIX=C:\npm
              ENV PATH="C:\nodejs;C:\npm;${PATH}"
              """ );
    }
}