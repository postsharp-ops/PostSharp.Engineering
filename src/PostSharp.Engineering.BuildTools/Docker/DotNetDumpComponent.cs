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
        if ( operatingSystem == ContainerOperatingSystem.Linux )
        {
            writer.WriteLine( "RUN dotnet tool install --global dotnet-dump" );
            writer.WriteLine();
            writer.WriteLine( """ENV PATH="/root/.dotnet/tools:${PATH}" """.TrimEnd() );
        }
        else
        {
            writer.WriteLine( "RUN dotnet tool install --global dotnet-dump;" );

            // The `dotnet tool install --global` shim is placed in %USERPROFILE%\.dotnet\tools, which is
            // not on PATH by default. Add it so the tool (and any other globally installed .NET tools)
            // is callable by name during container runs.
            writer.WriteLine();
            writer.WriteLine( """ENV PATH="C:\Users\ContainerAdministrator\.dotnet\tools;${PATH}" """.TrimEnd() );
        }
    }
}