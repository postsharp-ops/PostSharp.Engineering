// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using PostSharp.Engineering.BuildTools.Build.Solutions;
using PostSharp.Engineering.BuildTools.Utilities;
using System;
using System.IO;
using System.Linq;
using Xunit;

namespace PostSharp.Engineering.BuildTools.Tests;

/// <summary>
/// The staging of the test result files, which keeps a <c>.trx</c> file out of the directory that TeamCity watches
/// until it is complete and closed.
/// </summary>
/// <remarks>
/// TeamCity used to fail the build with "The process cannot access the file because it is being used by another
/// process" while parsing a <c>.trx</c>. The scenarios of a <c>ManySolutions</c> run in parallel into one results
/// directory, and the parser opened a file that another <c>dotnet test</c> was still writing.
/// </remarks>
public sealed class TestResultsStagingTests : IDisposable
{
    private readonly string _directory = Path.Combine( Path.GetTempPath(), $"trx-staging-{Guid.NewGuid():N}" );
    private readonly string _results;
    private readonly ConsoleHelper _console = new();

    public TestResultsStagingTests()
    {
        this._results = Path.Combine( this._directory, "artifacts", "testResults" );
        Directory.CreateDirectory( this._results );
    }

    private static string CreateStagedFile( string stagingDirectory, string fileName, string content = "content" )
    {
        var path = Path.Combine( stagingDirectory, fileName );

        Directory.CreateDirectory( Path.GetDirectoryName( path )! );
        File.WriteAllText( path, content );

        return path;
    }

    private string GetStagingDirectory( string runKey )
        => TestResultsStaging.GetStagingDirectory( this._directory, Path.Combine( "artifacts", "testResults" ), runKey );

    /// <summary>
    /// The staging directory must not be inside the results directory. A subdirectory would be published as a build
    /// artifact and found by anything scanning the results directory recursively, which is what the staging exists to
    /// prevent.
    /// </summary>
    [Fact]
    public void TheStagingDirectoryIsOutsideTheResultsDirectory()
    {
        var staging = this.GetStagingDirectory( "Scenario" );

        Assert.False(
            staging.StartsWith( this._results + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase ),
            $"'{staging}' is inside the results directory." );
    }

    /// <summary>
    /// The regression behind the run key. <c>Solution.Name</c> is only the file name of the project, so the log name
    /// of two scenarios in different directories is the same. Sharing a staging directory would let two parallel
    /// runs publish or delete each other's files.
    /// </summary>
    [Fact]
    public void TwoScenariosWithTheSameFileNameGetDifferentKeys()
    {
        Assert.NotEqual(
            TestResultsStaging.GetRunKey( Path.Combine( "a", "Tests.csproj" ), "Tests.csproj" ),
            TestResultsStaging.GetRunKey( Path.Combine( "b", "Tests.csproj" ), "Tests.csproj" ) );
    }

    /// <summary>
    /// The key must not depend on the separator of the platform, or the results of a Windows agent and of a Linux
    /// agent would land in differently named directories for the same scenario.
    /// </summary>
    [Fact]
    public void TheRunKeyIsIndependentOfThePathSeparator()
    {
        Assert.Equal(
            TestResultsStaging.GetRunKey( "a/b/Tests.csproj", "Tests.csproj" ),
            TestResultsStaging.GetRunKey( @"a\b\Tests.csproj", "Tests.csproj" ) );
    }

    /// <summary>
    /// Two entries of the matrix of one scenario are two runs, and each writes its own results.
    /// </summary>
    [Fact]
    public void EachMatrixEntryGetsItsOwnKey()
    {
        Assert.NotEqual(
            TestResultsStaging.GetRunKey( "a/Tests.csproj", "Tests.csproj" ),
            TestResultsStaging.GetRunKey( "a/Tests.csproj", "Tests.csproj.net8.0" ) );
    }

    /// <summary>
    /// A log name is built from the name of the scenario and of the matrix entry, neither of which is constrained to
    /// be a valid file name.
    /// </summary>
    [Fact]
    public void AStagingDirectoryNameIsAValidFileName()
    {
        var staging = this.GetStagingDirectory( TestResultsStaging.GetRunKey( "a/Tests.csproj", "Scenario:net8.0/net9.0" ) );

        Assert.DoesNotContain( Path.GetFileName( staging ), c => Path.GetInvalidFileNameChars().Contains( c ) );
    }

    [Fact]
    public void PublishMovesTheResultsAndRemovesTheStagingDirectory()
    {
        var staging = this.GetStagingDirectory( "run" );
        CreateStagedFile( staging, "results.trx" );

        var published = TestResultsStaging.Publish( this._console, staging, this._results, "run" );

        Assert.Equal( Path.Combine( this._results, "run", "results.trx" ), Assert.Single( published ) );
        Assert.False( Directory.Exists( staging ) );
    }

    /// <summary>
    /// A <c>.trx</c> names its attachments by a path relative to itself, so flattening the directory that
    /// <c>dotnet test</c> produced would leave those references pointing at nothing, and would merge same-named
    /// attachments of different tests.
    /// </summary>
    [Fact]
    public void TheLayoutOfTheAttachmentsIsPreserved()
    {
        var staging = this.GetStagingDirectory( "run" );
        CreateStagedFile( staging, "results.trx" );
        CreateStagedFile( staging, Path.Combine( "results", "first", "attachment.log" ), "first" );
        CreateStagedFile( staging, Path.Combine( "results", "second", "attachment.log" ), "second" );

        TestResultsStaging.Publish( this._console, staging, this._results, "run" );

        Assert.Equal( "first", File.ReadAllText( Path.Combine( this._results, "run", "results", "first", "attachment.log" ) ) );
        Assert.Equal( "second", File.ReadAllText( Path.Combine( this._results, "run", "results", "second", "attachment.log" ) ) );
    }

    /// <summary>
    /// Only a <c>.trx</c> is a test report. Reporting an attachment as one makes TeamCity parse a log or a dump as an
    /// XML test report.
    /// </summary>
    [Fact]
    public void OnlyTheTrxFilesAreReported()
    {
        var staging = this.GetStagingDirectory( "run" );
        CreateStagedFile( staging, "results.trx" );
        CreateStagedFile( staging, Path.Combine( "results", "attachment.log" ) );
        CreateStagedFile( staging, Path.Combine( "results", "dump.txt" ) );

        var published = TestResultsStaging.Publish( this._console, staging, this._results, "run" );

        Assert.EndsWith( ".trx", Assert.Single( published ), StringComparison.Ordinal );
    }

    /// <summary>
    /// A run key is unique within a build, but the results of a previous build may still be there, and overwriting
    /// them would discard results the build is about to publish.
    /// </summary>
    [Fact]
    public void ATakenDestinationIsNotOverwritten()
    {
        var first = this.GetStagingDirectory( "run" );
        CreateStagedFile( first, "results.trx", "first" );
        TestResultsStaging.Publish( this._console, first, this._results, "run" );

        var second = this.GetStagingDirectory( "run" );
        CreateStagedFile( second, "results.trx", "second" );
        var published = TestResultsStaging.Publish( this._console, second, this._results, "run" );

        Assert.Equal( Path.Combine( this._results, "run.1", "results.trx" ), Assert.Single( published ) );
        Assert.Equal( "first", File.ReadAllText( Path.Combine( this._results, "run", "results.trx" ) ) );
        Assert.Equal( "second", File.ReadAllText( Path.Combine( this._results, "run.1", "results.trx" ) ) );
    }

    /// <summary>
    /// A run whose build failed writes no results at all, and that is not an error.
    /// </summary>
    [Fact]
    public void PublishOfAnAbsentStagingDirectoryPublishesNothing()
    {
        Assert.Empty( TestResultsStaging.Publish( this._console, this.GetStagingDirectory( "run" ), this._results, "run" ) );
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete( this._directory, true );
        }
        catch ( IOException )
        {
            // A leftover temporary directory is not worth failing a test over.
        }
    }
}
