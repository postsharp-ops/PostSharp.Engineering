// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using PostSharp.Engineering.BuildTools.Build.Testing;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Xunit;

namespace PostSharp.Engineering.BuildTools.Tests;

/// <summary>
/// The restore graph names the target of a .NET Framework application by the moniker of the framework, and the target of a
/// .NET 5 or later application by the short name of the framework. <see cref="TestArchivePackages"/> accepts both, so that it
/// finds the packages of a .NET Framework application. Before, it found none, and <c>Build.ps1 build</c> reported every
/// package of the archive that is not from nuget.org as a package that no build configuration downloads.
/// </summary>
public sealed class TestArchivePackagesTargetTests
{
    [Theory]
    [InlineData( "net8.0", "net8.0" )]
    [InlineData( "net8.0/win-x64", "net8.0" )]
    [InlineData( ".NETFramework,Version=v4.8", ".NETFramework,Version=v4.8" )]
    [InlineData( ".NETFramework,Version=v4.8/win-x86", ".NETFramework,Version=v4.8" )]
    public void TheTargetOfTheFrameworkIsFound( string targetName, string framework )
        => Assert.True( TestArchivePackages.IsTargetOf( targetName, framework ) );

    [Theory]
    [InlineData( ".NETFramework,Version=v4.8", "net48" )]
    [InlineData( "net8.0", "net8.0-windows" )]
    [InlineData( "net80", "net8.0" )]
    [InlineData( "net8.0", "" )]
    public void TheTargetOfAnotherFrameworkIsNotFound( string targetName, string framework )
        => Assert.False( TestArchivePackages.IsTargetOf( targetName, framework ) );

    /// <summary>
    /// Verifies that the packages of a .NET Framework application are read from the target of the restore graph that the
    /// moniker of the framework names, as discovery records it (see <c>TestApplicationDiscoveryTests</c>).
    /// </summary>
    [Fact]
    public void ThePackagesOfANetFrameworkApplicationAreFound()
    {
        using var directory = new TempDirectory();

        var feed = Path.Combine( directory.Path, "feed" );
        var packageFolder = Path.Combine( directory.Path, "packages" );
        var packageDirectory = Path.Combine( packageFolder, "greeting", "1.0.1" );
        Directory.CreateDirectory( packageDirectory );

        File.WriteAllText(
            Path.Combine( directory.Path, "nuget.config" ),
            $"""
             <configuration>
               <packageSources>
                 <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
                 <add key="feed" value="{feed}" />
               </packageSources>
             </configuration>
             """ );

        // Without a package source mapping, the source of a package is the one that NuGet recorded in its directory.
        File.WriteAllText( Path.Combine( packageDirectory, ".nupkg.metadata" ), JsonSerializer.Serialize( new { version = 2, source = feed } ) );

        var assets = new Dictionary<string, object>
        {
            ["version"] = 3,

            // The name of the target is the moniker of the framework, and not its alias, net48.
            ["targets"] = new Dictionary<string, object>
            {
                [".NETFramework,Version=v4.8"] = new Dictionary<string, object>
                {
                    ["Greeting/1.0.1"] = new { type = "package", runtime = new Dictionary<string, object> { ["lib/net48/Greeting.dll"] = new { } } }
                }
            },
            ["libraries"] = new Dictionary<string, object> { ["Greeting/1.0.1"] = new { type = "package", path = "greeting/1.0.1" } },
            ["packageFolders"] = new Dictionary<string, object> { [packageFolder + Path.DirectorySeparatorChar] = new { } }
        };

        var assetsFile = Path.Combine( directory.Path, "project.assets.json" );
        File.WriteAllText( assetsFile, JsonSerializer.Serialize( assets ) );

        var application = new TestApplication(
            Path.Combine( directory.Path, "Probe.csproj" ),
            "Probe",
            "net48",
            "",
            ["win-x64"],
            [],
            false,
            null,
            [] ) { ProjectAssetsFile = assetsFile, TargetFrameworkMoniker = ".NETFramework,Version=v4.8" };

        var context = TestBuildContext.Create( directory.Path );
        Assert.True( TestArchivePackages.Sources.TryLoad( context, out var sources ) );
        Assert.True( TestArchivePackages.TryGetPackages( context.Console, sources, application, out var packages ) );
        Assert.Equal( ["feed/Greeting"], packages.Select( p => p.Reference ).ToArray() );

        // Without the moniker, as before the fix, the package is not found.
        Assert.True( TestArchivePackages.TryGetPackages( context.Console, sources, application with { TargetFrameworkMoniker = "" }, out var packagesWithoutMoniker ) );
        Assert.Empty( packagesWithoutMoniker );
    }
}
