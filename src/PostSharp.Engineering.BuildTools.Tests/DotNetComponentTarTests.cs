// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using PostSharp.Engineering.BuildTools.Docker;
using System;
using System.IO;
using Xunit;

namespace PostSharp.Engineering.BuildTools.Tests;

/// <summary>
/// From .NET 11 on, <c>dotnet-install.ps1</c> downloads a <c>.tar.gz</c> on Windows and extracts it by invoking
/// <c>tar</c> as an external process. Because <c>GitComponent</c> puts <c>C:\git\usr\bin</c> ahead of
/// <c>System32</c> in the path of every generated Windows image, that call resolves to the GNU tar of Git for Windows,
/// which reads the leading <c>C:</c> of the archive path as the name of a remote host and fails. The
/// <c>DOTNET_INSTALL_SKIP_TAR</c> assignment that <see cref="DotNetComponent"/> writes is what avoids the tar path
/// altogether. It is pinned here because its absence is only observable after several minutes of image building, and
/// because its presence on a version earlier than 11 would invalidate every cached image.
/// </summary>
public class DotNetComponentTarTests
{
    private const string _skipTar = "$env:DOTNET_INSTALL_SKIP_TAR = '1';";

    private const string _net11 = "11.0.100-preview.7.26381.103";
    private const string _net10 = "10.0.102";

    private static string WriteDockerfile( string version, DotNetComponentKind kind, ContainerOperatingSystem operatingSystem )
    {
        var component = new DotNetComponent( version, kind );
        using var writer = new StringWriter();
        component.WriteDockerfile( writer, operatingSystem );

        return writer.ToString();
    }

    [Theory]
    [InlineData( DotNetComponentKind.Sdk )]
    [InlineData( DotNetComponentKind.DotNetRuntime )]
    [InlineData( DotNetComponentKind.AspNetCoreRuntime )]
    [InlineData( DotNetComponentKind.WindowsDesktopRuntime )]
    public void WindowsInstallationOfNet11SkipsTar( DotNetComponentKind kind )
    {
        var dockerfile = WriteDockerfile( _net11, kind, ContainerOperatingSystem.Windows2025 );

        Assert.Contains( _skipTar, dockerfile, StringComparison.Ordinal );

        // The assignment has to precede the invocation on the same RUN, otherwise the script never sees it.
        Assert.True(
            dockerfile.IndexOf( _skipTar, StringComparison.Ordinal )
            < dockerfile.IndexOf( "dotnet-install.ps1", StringComparison.Ordinal ) );
    }

    [Theory]
    [InlineData( DotNetComponentKind.Sdk )]
    [InlineData( DotNetComponentKind.DotNetRuntime )]
    [InlineData( DotNetComponentKind.AspNetCoreRuntime )]
    [InlineData( DotNetComponentKind.WindowsDesktopRuntime )]
    public void WindowsInstallationOfNet10IsUnchanged( DotNetComponentKind kind )
    {
        // Versions earlier than 11 are published as a .zip and never take the tar path. The instruction text is part
        // of the image content hash, so writing the assignment here would invalidate every cached image.
        var dockerfile = WriteDockerfile( _net10, kind, ContainerOperatingSystem.Windows2025 );

        Assert.DoesNotContain( "DOTNET_INSTALL_SKIP_TAR", dockerfile, StringComparison.Ordinal );
        Assert.StartsWith( "RUN & .", dockerfile, StringComparison.Ordinal );
    }

    [Theory]
    [InlineData( DotNetComponentKind.Sdk )]
    [InlineData( DotNetComponentKind.DotNetRuntime )]
    [InlineData( DotNetComponentKind.AspNetCoreRuntime )]
    public void LinuxInstallationDoesNotSkipTar( DotNetComponentKind kind )
    {
        // dotnet-install.sh extracts the archive itself and does not read DOTNET_INSTALL_SKIP_TAR.
        var dockerfile = WriteDockerfile( _net11, kind, ContainerOperatingSystem.Linux );

        Assert.DoesNotContain( "DOTNET_INSTALL_SKIP_TAR", dockerfile, StringComparison.Ordinal );
    }
}