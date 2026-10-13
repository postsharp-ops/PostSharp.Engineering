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
        if ( operatingSystem == ContainerOperatingSystem.Linux )
        {
            // dotnet-install.sh detects the architecture itself.
            var runtimeArgument = this.DotNetComponentKind switch
            {
                DotNetComponentKind.Sdk => "",
                DotNetComponentKind.DotNetRuntime => " --runtime dotnet",
                DotNetComponentKind.AspNetCoreRuntime => " --runtime aspnetcore",
                _ => throw new InvalidOperationException( $"'{this.DotNetComponentKind}' is not available on Linux." )
            };

            writer.WriteLine(
                $"""
                 RUN /usr/local/bin/dotnet-install.sh --version {this.Version}{runtimeArgument} --install-dir $DOTNET_ROOT
                 """ );
        }
        else
        {
            // From .NET 11 on, dotnet-install.ps1 downloads a .tar.gz on Windows instead of a .zip and extracts it by
            // invoking tar as an external process. GitComponent, which is added to every generated Windows image, puts
            // C:\git\usr\bin ahead of System32 in PATH, so that call resolves to the GNU tar of Git for Windows instead
            // of the bsdtar of Windows. GNU tar reads the leading 'C:' of the archive path as the name of a remote host,
            // in the host:path form it accepts, and fails with "Cannot connect to C: resolve failed".
            // DOTNET_INSTALL_SKIP_TAR makes the script take the .zip path and extract the archive in-process with
            // System.IO.Compression, which depends on no external program. Do not remove this assignment: without it,
            // every image that installs a .NET 11 or later SDK or runtime fails to build. It is set on the RUN itself
            // instead of an image-wide ENV so that this component stays self-contained and adds no layer.
            //
            // The assignment is restricted to version 11 and later, although it is harmless for earlier versions,
            // because the text of the RUN instruction is part of the image content hash. Writing it unconditionally
            // would change every Windows Dockerfile and invalidate every cached image, including those of products
            // that install no .NET 11. A version that does not parse keeps the earlier form; every version string a
            // product declares parses.
            var skipTar = this.ParsedVersion is { Major: >= 11 } ? "$env:DOTNET_INSTALL_SKIP_TAR = '1'; " : "";

            // Run script directly since we're already in a PowerShell shell
            if ( this.DotNetComponentKind == DotNetComponentKind.Sdk )
            {
                writer.WriteLine(
                    $"""
                     RUN {skipTar}& .\dotnet-install.ps1 -Version {this.Version} -InstallDir 'C:\Program Files\dotnet'
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
                     RUN {skipTar}& .\dotnet-install.ps1 -Version {this.Version} -Runtime {runtime} -InstallDir 'C:\Program Files\dotnet'
                     """ );
            }
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