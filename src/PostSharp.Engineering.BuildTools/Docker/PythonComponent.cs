// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using JetBrains.Annotations;
using System.IO;

namespace PostSharp.Engineering.BuildTools.Docker;

[PublicAPI]
public class PythonComponent : ContainerComponent
{
    private readonly string _version;

    public PythonComponent( string version )
    {
        this._version = version;
    }

    public override string Name => $"Install Python {this._version}";

    public override ContainerComponentKind Kind => ContainerComponentKind.Python;

    public override void WriteDockerfile( TextWriter writer, ContainerOperatingSystem operatingSystem )
    {
        writer.WriteLine( "RUN choco install -y python311" );
    }
}