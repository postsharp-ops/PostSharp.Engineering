// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using JetBrains.Annotations;
using System.IO;

namespace PostSharp.Engineering.BuildTools.Docker;

[PublicAPI]
public sealed class DotNetDumpComponent : ContainerComponent
{
    public override string Name => ".NET Dump Tool";

    public override ContainerComponentKind Kind => ContainerComponentKind.DotNetDump;

    public override void WriteDockerfile( TextWriter writer, ContainerOperatingSystem operatingSystem )
    {
        writer.WriteLine( "RUN dotnet tool install --global dotnet-dump;" );
    }
}