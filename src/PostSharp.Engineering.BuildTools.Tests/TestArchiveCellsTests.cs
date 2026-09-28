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
            TestArchivesSourceDependency = new SnapshotDependency( "BuildArtifacts" )
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
    /// The build configurations are valid when the archives come from a product build configuration, such as the default
    /// public build, and not only from an additional build configuration.
    /// </summary>
    [Theory]
    [InlineData( BuildConfiguration.Public )]
    [InlineData( BuildConfiguration.Debug )]
    public void TheArchivesCanComeFromAProductBuildConfiguration( BuildConfiguration configuration )
    {
        var product = new Product( MetalamaDependencies.V2026_1.Metalama )
        {
            Solutions = [new DotNetSolution( "Tests.sln" ) { ContainsTestApplications = true }],
            TestAgents = [new TestAgent( "linux-x64", "TestLinuxX64", "Linux x64", BuildAgentRequirements.Empty )],
            TestArchivesSourceDependency = new SnapshotDependency( configuration )
        };

        var cells = TestArchiveCells.Create( product, [Application( "Common", "net10.0", _everywhere )] );

        Assert.Equal( configuration, cells.Single( c => c.Id == "TestLinuxX64Net100" ).BuildSnapshotDependency );
        Assert.True( SnapshotDependencyGraph.TryValidate( new ConsoleHelper(), product, cells ) );
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

        Assert.True( TestArchives.Verify( context ) );

        // A test project added without regenerating the build configurations is run by none of them.
        File.WriteAllText( Path.Combine( archives, "Added.net10.0.zip" ), "" );
        Assert.False( TestArchives.Verify( context ) );
    }
}
