// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using JetBrains.Annotations;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PostSharp.Engineering.BuildTools.Docker;

[PublicAPI]
public sealed class DotNetComponent : ContainerComponent
{
    public string Version { get; }

    public Version? ParsedVersion { get; }

    public DotNetComponentKind DotNetComponentKind { get; }

    public DotNetComponent( string version, DotNetComponentKind dotNetComponentKind )
    {
        this.Version = version;
        this.DotNetComponentKind = dotNetComponentKind;

        var v = this.Version.Split( "-" )[0];

        if ( System.Version.TryParse( v, out var parsedVersion ) )
        {
            this.ParsedVersion = parsedVersion;
        }
    }

    public override string Name => $"Install .NET {this.DotNetComponentKind} {this.Version}";

    public override string Key => $"{nameof(DotNetComponent)}:{this.DotNetComponentKind}:{this.Version}";

    public override ContainerComponentKind Kind => ContainerComponentKind.DotNet;

    public override void AddRequirements( IReadOnlyList<ContainerComponent> components, Action<ContainerComponent> add )
    {
        if ( !components.OfType<DotNetInstallerComponent>().Any() )
        {
            add( new DotNetInstallerComponent() );
        }

        if ( this.DotNetComponentKind == DotNetComponentKind.Sdk && !components.OfType<DotNetDumpComponent>().Any() )
        {
            add( new DotNetDumpComponent() );
        }
    }

    public override void WriteDockerfile( TextWriter writer, ContainerOperatingSystem operatingSystem )
    {
        // Run script directly since we're already in a PowerShell shell
        if ( this.DotNetComponentKind == DotNetComponentKind.Sdk )
        {
            writer.WriteLine(
                $"""
                 RUN & .\dotnet-install.ps1 -Version {this.Version} -InstallDir 'C:\Program Files\dotnet'
                 """ );
        }
        else
        {
            var runtime = this.DotNetComponentKind switch
            {
                DotNetComponentKind.DotNetRuntime => "dotnet",
                DotNetComponentKind.WindowsDesktopRuntime => "windowsdesktop",
                DotNetComponentKind.AspNetCoreRuntime => "aspnetcore",
                _ => throw new InvalidOperationException()
            };

            writer.WriteLine(
                $"""
                 RUN & .\dotnet-install.ps1 -Version {this.Version} -Runtime {runtime} -InstallDir 'C:\Program Files\dotnet'
                 """ );
        }
    }

    public override string ToString() => $"{this.Kind} {this.DotNetComponentKind} {this.Version}";

    public override int CompareTo( ContainerComponent? other )
    {
        var compareBase = base.CompareTo( other );

        if ( compareBase != 0 )
        {
            return compareBase;
        }

        var otherDotNetComponent = (DotNetComponent) other!;

        // Compare the version number.
        if ( this.ParsedVersion != null && otherDotNetComponent.ParsedVersion != null )
        {
            var compareParsedVersion = this.ParsedVersion.CompareTo( otherDotNetComponent.ParsedVersion );

            if ( compareParsedVersion != 0 )
            {
                return compareParsedVersion;
            }
        }

        // Compare the string part of the version number.
        return -string.Compare( this.Version, otherDotNetComponent.Version, StringComparison.Ordinal );
    }
}