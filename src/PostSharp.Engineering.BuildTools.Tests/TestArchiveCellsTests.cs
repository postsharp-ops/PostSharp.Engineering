// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using PostSharp.Engineering.BuildTools.Build;
using PostSharp.Engineering.BuildTools.Build.Model;
using PostSharp.Engineering.BuildTools.Build.Solutions;
using PostSharp.Engineering.BuildTools.Build.Testing;
using PostSharp.Engineering.BuildTools.ContinuousIntegration.Model;
using PostSharp.Engineering.BuildTools.Dependencies.Definitions;
using PostSharp.Engineering.BuildTools.Utilities;
using System;
using System.Collections.Immutable;
using System.IO;
using System.IO.Compression;
using System.Linq;
using Xunit;

namespace PostSharp.Engineering.BuildTools.Tests;

/// <summary>
/// Plans the build configurations of the test agents with <see cref="TestArchiveCells"/>, and checks the archives of a build
/// against the list that <c>generate-scripts</c> wrote with <see cref="TestArchives"/>.
/// </summary>
public sealed class TestArchiveCellsTests
{
    private static readonly string[] _everywhere = ["win-x64", "win-arm64", "linux-x64", "linux-arm64", "osx-x64", "osx-arm64"];
    private static readonly string[] _windows = ["win-x64", "win-arm64"];

    private static TestApplication Application(
        string name,
        string targetFramework,
        string[] platforms,
        string[]? tags = null,
        string? skip = null,
        string[]? artifacts = null )
        => new( $"C:\\src\\{name}\\{name}.csproj", name, targetFramework, "", [..platforms], [..tags ?? []], false, skip, [..artifacts ?? []] );

    private static Product CreateProduct( params TestAgent[] agents )
        => new( MetalamaDependencies.V2026_1.Metalama )
        {
            Solutions = [new DotNetSolution( "Tests.sln" ) { ContainsTestApplications = true }], TestAgents = agents,
            Configurations = Product.DefaultConfigurations.WithValue( BuildConfiguration.Public, c => c with { RunsTestArchives = true } )
        };

    /// <summary>
    /// A build configuration downloads the artifacts that the prepare scripts of its applications read, once each.
    /// </summary>
    [Fact]
    public void TheArtifactsOfTheApplicationsAreDownloaded()
    {
        var product = CreateProduct( new TestAgent( "win-x64", "TestWinX64", "Windows x64", BuildAgentRequirements.Empty ) );

        var cells = TestArchiveCells.Create(
            product,
            [
                Application( "Client", "net48", _windows, artifacts: ["artifacts/publish/public/Product.*.nupkg"] ),
                Application( "Other", "net48", _windows, artifacts: ["artifacts/publish/public/Product.*.nupkg"] ),
                Application( "Common", "net10.0", _everywhere )
            ] );

        var net48 = cells.Single( c => c.Id == "TestWinX64Net48" );

        Assert.Equal(
            [
                "+:artifacts/publish/public/Product.*.nupkg=>artifacts/publish/public",
                "+:artifacts/tests/Client.net48.zip=>artifacts/tests",
                "+:artifacts/tests/Other.net48.zip=>artifacts/tests"
            ],
            net48.SnapshotDependencies!.Single().ArtifactRules! );

        Assert.Equal( ["+:artifacts/tests/Common.net10.0.zip=>artifacts/tests"], cells.Single( c => c.Id == "TestWinX64Net100" ).SnapshotDependencies!.Single().ArtifactRules! );
    }

    [Fact]
    public void EachAgentGetsOneConfigurationPerRuntimeAndSeparateTag()
    {
        var product = CreateProduct(
            new TestAgent( "win-x64", "TestWinX64", "Windows x64", BuildAgentRequirements.Empty ) { SeparateTags = ["TimeSensitive"] },
            new TestAgent( "linux-x64", "TestLinuxX64", "Linux x64", BuildAgentRequirements.Empty ) );

        var applications = new[]
        {
            Application( "Common", "net10.0", _everywhere ),
            Application( "Common", "net48", _windows ),
            Application( "Xaml", "net10.0-windows", _windows ),
            Application( "Caching", "net10.0", _everywhere, ["TimeSensitive"] ),
            Application( "Broken", "net10.0", _everywhere, skip: "Not today" )
        };

        var cells = TestArchiveCells.Create( product, applications );

        Assert.Equal(
            ["RunAllTestArchives", "TestLinuxX64Net100", "TestWinX64Net100", "TestWinX64Net100TimeSensitive", "TestWinX64Net48"],
            cells.Select( c => c.Id ).Order( StringComparer.Ordinal ).ToArray() );

        // A -windows target framework runs with its runtime, and the tag that has a configuration of its own is excluded.
        var windows = (PowershellAdditionalCiBuildConfiguration) cells.Single( c => c.Id == "TestWinX64Net100" );
        Assert.Equal( "-Platform win-x64 -ExcludeTags TimeSensitive", windows.Arguments );

        Assert.Equal(
            ["+:artifacts/tests/Common.net10.0.zip=>artifacts/tests", "+:artifacts/tests/Xaml.net10.0-windows.zip=>artifacts/tests"],
            windows.SnapshotDependencies!.Single().ArtifactRules! );

        var timeSensitive = (PowershellAdditionalCiBuildConfiguration) cells.Single( c => c.Id == "TestWinX64Net100TimeSensitive" );
        Assert.Equal( "-Platform win-x64 -Tags TimeSensitive", timeSensitive.Arguments );
        Assert.Equal( ["+:artifacts/tests/Caching.net10.0.zip=>artifacts/tests"], timeSensitive.SnapshotDependencies!.Single().ArtifactRules! );

        // The agent without separate tags runs every application of its platform, and a skipped application is not
        // downloaded.
        var linux = (PowershellAdditionalCiBuildConfiguration) cells.Single( c => c.Id == "TestLinuxX64Net100" );
        Assert.Equal( "-Platform linux-x64", linux.Arguments );

        Assert.Equal(
            ["+:artifacts/tests/Caching.net10.0.zip=>artifacts/tests", "+:artifacts/tests/Common.net10.0.zip=>artifacts/tests"],
            linux.SnapshotDependencies!.Single().ArtifactRules! );
    }

    /// <summary>
    /// A cell that downloads the archives from a product build configuration reads the artifact layout of that
    /// configuration, which is what the validation of the dependency graph requires.
    /// </summary>
    [Fact]
    public void ACellOfAProductConfigurationReadsItsLayout()
    {
        Product CreateReleaseProduct( AdditionalCiBuildConfiguration[] configurations )
            => new( MetalamaDependencies.V2026_1.Metalama )
            {
                Solutions = [new DotNetSolution( "Tests.sln" ) { ContainsTestApplications = true }],
                TestAgents = [new TestAgent( "win-x64", "TestWinX64", "Windows x64", BuildAgentRequirements.Empty )],
                Configurations = Product.DefaultConfigurations.WithValue( BuildConfiguration.Release, c => c with { RunsTestArchives = true } ),
                AdditionalCiBuildConfigurations = configurations
            };

        var cells = TestArchiveCells.Create( CreateReleaseProduct( [] ), [Application( "Common", "net10.0", _everywhere )] );

        Assert.Equal( BuildConfiguration.Release, cells.Single( c => c.Id == "TestWinX64Net100" ).BuildSnapshotDependency );
        Assert.True( SnapshotDependencyGraph.TryValidate( new ConsoleHelper(), CreateReleaseProduct( [..cells] ) ) );
    }

    [Fact]
    public void TheArchivesOfTheBuildAreCheckedAgainstTheList()
    {
        using var directory = new TempDirectory();
        var product = CreateProduct();
        var context = TestBuildContext.Create( directory.Path, product );

        TestArchives.WriteList( context, [Application( "Common", "net10.0", _everywhere ), Application( "Common", "net48", _windows )] );

        var archives = Path.Combine( directory.Path, "artifacts", "tests" );
        Directory.CreateDirectory( archives );
        File.WriteAllText( Path.Combine( archives, "Common.net10.0.zip" ), "" );
        File.WriteAllText( Path.Combine( archives, "Common.net48.zip" ), "" );

        Assert.True( TestArchives.Verify( context, [] ) );

        // A test project added without regenerating the build configurations is run by none of them.
        File.WriteAllText( Path.Combine( archives, "Added.net10.0.zip" ), "" );
        Assert.False( TestArchives.Verify( context, [] ) );
    }

    /// <summary>
    /// A build configuration downloads each package that is not from nuget.org from the build that publishes it, by its
    /// identifier and not by its version. A rule for a package also matches the packages whose identifier extends it, which are
    /// excluded unless they are needed too.
    /// </summary>
    [Fact]
    public void ThePackagesOfOtherSourcesAreDownloadedFromTheirBuild()
    {
        var product = CreateProduct( new TestAgent( "win-x64", "TestWinX64", "Windows x64", BuildAgentRequirements.Empty ) );
        var self = product.ProductName;

        var cells = TestArchiveCells.Create(
            product,
            [
                Application( "Client", "net48", _windows ) with { Packages = [$"{self}/Product.Redist", $"{self}/Product.Tools"] },
                Application( "Other", "net10.0", _everywhere ) with { Packages = [$"{self}/Product.Redist"] },
                Application( "Tools", "net10.0", _everywhere ) with { Packages = [$"{self}/Product.Redist.Tools"] }
            ] );

        Assert.Equal(
            [
                "+:artifacts/publish/private/Product.Redist.*.nupkg=>artifacts/test-packages/" + self,
                "+:artifacts/publish/private/Product.Tools.*.nupkg=>artifacts/test-packages/" + self,
                "+:artifacts/tests/Client.net48.zip=>artifacts/tests",
                "-:artifacts/publish/private/Product.Redist.Tools.*.nupkg"
            ],
            cells.Single( c => c.Id == "TestWinX64Net48" ).SnapshotDependencies!.Single().ArtifactRules! );

        // Both packages are needed, so the rule of the shorter one is enough and nothing is excluded.
        Assert.Equal(
            [
                "+:artifacts/publish/private/Product.Redist.*.nupkg=>artifacts/test-packages/" + self,
                "+:artifacts/publish/private/Product.Redist.Tools.*.nupkg=>artifacts/test-packages/" + self,
                "+:artifacts/tests/Other.net10.0.zip=>artifacts/tests",
                "+:artifacts/tests/Tools.net10.0.zip=>artifacts/tests"
            ],
            cells.Single( c => c.Id == "TestWinX64Net100" ).SnapshotDependencies!.Single().ArtifactRules! );
    }

    /// <summary>
    /// The packages that NuGet restored from a source other than nuget.org, and that have a file to publish in the restore
    /// graph of the target framework, are named after each archive with the key of their source in nuget.config, which the
    /// package source mapping gives. A package added without regenerating the build configurations would be downloaded by
    /// none of them.
    /// </summary>
    [Fact]
    public void ThePackagesOfOtherSourcesAreListed()
    {
        using var directory = new TempDirectory();
        var context = TestBuildContext.Create( directory.Path, CreateProduct() );
        var packageFolder = Path.Combine( directory.Path, "packages" );

        File.WriteAllText(
            Path.Combine( directory.Path, "nuget.config" ),
            """
            <configuration>
              <packageSources>
                <clear />
                <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
                <add key="Backstage" value="C:\artifacts\Backstage" />
              </packageSources>
              <packageSourceMapping>
                <clear />
                <packageSource key="nuget.org"><package pattern="*" /></packageSource>
                <packageSource key="Backstage"><package pattern="Backstage*" /><package pattern="Product" /></packageSource>
              </packageSourceMapping>
            </configuration>
            """ );

        // The target of the framework and the target of the framework and a runtime identifier, a project reference, and a
        // package of MSBuild targets, whose empty asset groups hold the _._ placeholder.
        var assetsFile = Path.Combine( directory.Path, "project.assets.json" );

        File.WriteAllText(
            assetsFile,
            $$"""
              {
                "version": 3,
                "targets": {
                  "net48": {
                    "Backstage/1.0.1": { "type": "package", "runtime": { "lib/net472/Backstage.dll": {} } },
                    "xunit.v3/3.0.0": { "type": "package", "runtime": { "lib/net472/xunit.v3.dll": {} } },
                    "Product/2.0.0": { "type": "package", "runtime": { "lib/netstandard1.0/_._": {} }, "build": { "build/Product.targets": {} } },
                    "Common/1.0.0": { "type": "project" }
                  },
                  "net48/win-x86": {
                    "Backstage.Tools/1.0.1": { "type": "package", "native": { "runtimes/win-x86/native/tools.dll": {} } }
                  },
                  "net10.0": {
                    "Backstage.Other/1.0.0": { "type": "package", "runtime": { "lib/net10.0/Other.dll": {} } }
                  }
                },
                "libraries": {
                  "Backstage/1.0.1": { "type": "package", "path": "backstage/1.0.1" },
                  "Backstage.Tools/1.0.1": { "type": "package", "path": "backstage.tools/1.0.1" },
                  "xunit.v3/3.0.0": { "type": "package", "path": "xunit.v3/3.0.0" },
                  "Backstage.Other/1.0.0": { "type": "package", "path": "backstage.other/1.0.0" },
                  "Product/2.0.0": { "type": "package", "path": "product/2.0.0" },
                  "Common/1.0.0": { "type": "project", "path": "../Common/Common.csproj" }
                },
                "packageFolders": { "{{packageFolder.Replace( "\\", "\\\\", StringComparison.Ordinal )}}": {} }
              }
              """ );

        var application = Application( "Client", "net48", _windows ) with { ProjectAssetsFile = assetsFile };

        Assert.True( TestArchivePackages.Sources.TryLoad( context, out var sources ) );
        Assert.True( TestArchivePackages.TryGetPackages( context.Console, sources, application, out var packages ) );
        Assert.Equal( ["Backstage/Backstage", "Backstage/Backstage.Tools"], packages.Select( p => p.Reference ).ToArray() );

        TestArchives.WriteList( context, [application with { Packages = [..packages.Select( p => p.Reference )] }] );

        Assert.Contains(
            "Client.net48: Backstage/Backstage Backstage/Backstage.Tools",
            File.ReadAllText( Path.Combine( directory.Path, "eng", "test-archives.txt" ) ),
            StringComparison.Ordinal );

        // The archive takes a file from a package of another source.
        var archives = Path.Combine( directory.Path, "artifacts", "tests" );
        Directory.CreateDirectory( archives );
        var archive = Path.Combine( archives, "Client.net48.zip" );

        void WriteArchive( string package )
        {
            File.Delete( archive );

            using var zip = ZipFile.Open( archive, ZipArchiveMode.Create );
            using var writer = new StreamWriter( zip.CreateEntry( "test.psd1" ).Open() );

            writer.Write(
                $$"""
                  @{
                      Packages = @(
                          @{
                              Id = '{{package}}'
                              Version = '1.0.1'
                              Sha512 = 'x'
                              Url = $null
                              Files = @() } )
                  }
                  """ );
        }

        WriteArchive( "backstage" );
        Assert.True( TestArchives.Verify( context, [application] ) );

        // A package that the archive takes from another source and that the restore graph does not show fails the build.
        WriteArchive( "product" );
        Assert.False( TestArchives.Verify( context, [application] ) );

        // A package that the list does not name fails the build.
        WriteArchive( "backstage" );
        TestArchives.WriteList( context, [application with { Packages = ["Backstage/Backstage"] }] );
        Assert.False( TestArchives.Verify( context, [application] ) );
    }
}
