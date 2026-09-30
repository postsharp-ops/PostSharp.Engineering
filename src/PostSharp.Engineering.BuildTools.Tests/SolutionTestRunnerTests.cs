// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using PostSharp.Engineering.BuildTools.Build;
using PostSharp.Engineering.BuildTools.Build.Model;
using PostSharp.Engineering.BuildTools.Build.Solutions;
using PostSharp.Engineering.BuildTools.Build.Testing;
using PostSharp.Engineering.BuildTools.Dependencies.Definitions;
using Xunit;

namespace PostSharp.Engineering.BuildTools.Tests;

/// <summary>
/// A solution can use Microsoft.Testing.Platform in a product whose tests use VSTest (<see cref="Solution.TestRunner"/>).
/// <c>Build.ps1 test</c> then runs the test archives of the solution on the build host instead of <c>dotnet test</c>.
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
    public void OnlyATestingPlatformSolutionInAVSTestProductRunsItsArchivesOnTheHost()
    {
        var testingPlatformSolution = new DotNetSolution( "A.sln" ) { TestRunner = TestRunner.MicrosoftTestingPlatform };
        var defaultSolution = new DotNetSolution( "B.sln" );

        Assert.True( testingPlatformSolution.RunsTestArchivesOnHost( CreateProduct( TestRunner.VSTest ) ) );
        Assert.False( testingPlatformSolution.RunsTestArchivesOnHost( CreateProduct( TestRunner.MicrosoftTestingPlatform ) ) );
        Assert.False( defaultSolution.RunsTestArchivesOnHost( CreateProduct( TestRunner.VSTest ) ) );
        Assert.False( defaultSolution.RunsTestArchivesOnHost( CreateProduct( TestRunner.MicrosoftTestingPlatform ) ) );
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
    public void ATestingPlatformSolutionInAVSTestProductMustContainTestApplications()
    {
        using var directory = new TempDirectory();
        var context = TestBuildContext.Create( directory.Path, CreateProduct( TestRunner.VSTest ) );
        var solution = new DotNetSolution( "A.sln" ) { TestRunner = TestRunner.MicrosoftTestingPlatform };

        // The check fails before anything is built.
        Assert.False( TestArchives.RunOnHost( context, new BuildSettings(), solution ) );
    }
}
