// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using PostSharp.Engineering.BuildTools.Build.Files;
using PostSharp.Engineering.BuildTools.Build.Model;
using PostSharp.Engineering.BuildTools.Build.Solutions;
using PostSharp.Engineering.BuildTools.Dependencies.Definitions;
using System.IO;
using System.Text.Json;
using Xunit;

namespace PostSharp.Engineering.BuildTools.Tests;

/// <summary>
/// A solution can use Microsoft.Testing.Platform in a product whose tests use VSTest (<see cref="Solution.TestRunner"/>).
/// <c>dotnet test</c> then runs from a directory whose <c>global.json</c> selects that mode.
/// </summary>
public sealed class SolutionTestRunnerTests
{
    private static Product CreateProduct( TestRunner testRunner )
        => new( MetalamaDependencies.V2026_1.Metalama ) { DotNetSdkVersion = new DotNetSdkVersion( "10.0.100" ), TestRunner = testRunner };

    [Fact]
    public void ASolutionUsesTheTestRunnerOfTheProductByDefault()
    {
        var solution = new DotNetSolution( "A.sln" );

        Assert.Equal( TestRunner.MicrosoftTestingPlatform, solution.GetTestRunner( CreateProduct( TestRunner.MicrosoftTestingPlatform ) ) );
        Assert.Equal( TestRunner.VSTest, solution.GetTestRunner( CreateProduct( TestRunner.VSTest ) ) );
    }

    [Fact]
    public void TheTestRunnerOfASolutionOverridesTheOneOfTheProduct()
    {
        var solution = new DotNetSolution( "A.sln" ) { TestRunner = TestRunner.MicrosoftTestingPlatform };

        Assert.Equal( TestRunner.MicrosoftTestingPlatform, solution.GetTestRunner( CreateProduct( TestRunner.VSTest ) ) );
    }

    [Fact]
    public void ASolutionCannotUseVSTestInAProductThatUsesTheTestingPlatform()
    {
        using var directory = new TempDirectory();
        var context = TestBuildContext.Create( directory.Path, CreateProduct( TestRunner.MicrosoftTestingPlatform ) );
        var solution = new DotNetSolution( "A.sln" ) { TestRunner = TestRunner.VSTest };

        Assert.False( solution.TryGetTestRunner( context, out _ ) );
    }

    [Fact]
    public void TheTestingPlatformDirectoryHasTheSdkOfTheRepositoryAndSelectsTheTestingPlatform()
    {
        using var directory = new TempDirectory();
        var context = TestBuildContext.Create( directory.Path, CreateProduct( TestRunner.VSTest ) );

        Assert.True( GlobalJsonFile.TryWrite( context ) );

        var testingPlatformDirectory = GlobalJsonFile.WriteTestingPlatformDirectory( context );

        Assert.Equal( Path.Combine( directory.Path, GlobalJsonFile.TestingPlatformDirectory ), testingPlatformDirectory );

        var globalJson = JsonDocument.Parse( File.ReadAllText( Path.Combine( testingPlatformDirectory, "global.json" ) ) ).RootElement;

        Assert.Equal( "Microsoft.Testing.Platform", globalJson.GetProperty( "test" ).GetProperty( "runner" ).GetString() );
        Assert.Equal( "10.0.100", globalJson.GetProperty( "sdk" ).GetProperty( "version" ).GetString() );
        Assert.True( globalJson.TryGetProperty( "msbuild-sdks", out _ ) );

        // The global.json of the repository still selects no mode.
        var repositoryGlobalJson = JsonDocument.Parse( File.ReadAllText( Path.Combine( directory.Path, "global.json" ) ) ).RootElement;
        Assert.False( repositoryGlobalJson.TryGetProperty( "test", out _ ) );
    }

    [Fact]
    public void TheTestingPlatformDirectoryIsWrittenWithoutAGlobalJsonInTheRepository()
    {
        using var directory = new TempDirectory();
        var context = TestBuildContext.Create( directory.Path, CreateProduct( TestRunner.VSTest ) );

        var testingPlatformDirectory = GlobalJsonFile.WriteTestingPlatformDirectory( context );

        var globalJson = JsonDocument.Parse( File.ReadAllText( Path.Combine( testingPlatformDirectory, "global.json" ) ) ).RootElement;

        Assert.Equal( "Microsoft.Testing.Platform", globalJson.GetProperty( "test" ).GetProperty( "runner" ).GetString() );
    }
}
