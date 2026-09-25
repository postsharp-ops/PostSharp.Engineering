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
        Directory.CreateDirectory( Path.GetDirectoryName( Path.Combine( stagingDirectory, fileName ) )! );
        File.WriteAllText( Path.Combine( stagingDirectory, fileName ), content );

        return Path.Combine( stagingDirectory, fileName );
    }

    private string GetStagingDirectory( string logName = "Scenario" )
        => TestResultsStaging.GetStagingDirectory( this._directory, Path.Combine( "artifacts", "testResults" ), logName );

    /// <summary>
    /// The staging directory must not be inside the results directory. A subdirectory would be published as a build
    /// artifact and found by anything scanning the results directory recursively, which is what the staging exists to
    /// prevent.
    /// </summary>
    [Fact]
    public void TheStagingDirectoryIsOutsideTheResultsDirectory()
    {
        var staging = this.GetStagingDirectory();

        Assert.False(
            staging.StartsWith( this._results + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase ),
            $"'{staging}' is inside the results directory." );
    }

    /// <summary>
    /// Two runs of one scenario, and two scenarios, must never share a staging directory, or one would move the
    /// half-written results of the other.
    /// </summary>
    [Fact]
    public void EachRunGetsItsOwnStagingDirectory()
    {
        Assert.NotEqual( this.GetStagingDirectory( "Scenario" ), this.GetStagingDirectory( "Scenario.matrix-entry" ) );
    }

    /// <summary>
    /// A log name is built from the name of the scenario and of the matrix entry, neither of which is constrained to
    /// be a valid file name.
    /// </summary>
    [Fact]
    public void AStagingDirectoryNameIsAValidFileName()
    {
        var staging = this.GetStagingDirectory( "Scenario:net8.0/net9.0" );

        Assert.DoesNotContain( Path.GetFileName( staging ), c => Path.GetInvalidFileNameChars().Contains( c ) );
    }

    [Fact]
    public void PublishMovesTheFilesAndRemovesTheStagingDirectory()
    {
        var staging = this.GetStagingDirectory();
        CreateStagedFile( staging, "results.trx" );

        var published = TestResultsStaging.Publish( this._console, staging, this._results );

        Assert.Equal( Path.Combine( this._results, "results.trx" ), Assert.Single( published ) );
        Assert.True( File.Exists( Path.Combine( this._results, "results.trx" ) ) );
        Assert.False( Directory.Exists( staging ) );
    }

    /// <summary>
    /// A <c>.trx</c> names its attachments, so a parser that reads it the moment it appears must find them already
    /// there.
    /// </summary>
    [Fact]
    public void AttachmentsAreMovedBeforeTheTrxThatNamesThem()
    {
        var staging = this.GetStagingDirectory();
        CreateStagedFile( staging, "results.trx" );
        CreateStagedFile( staging, Path.Combine( "results", "attachment.log" ) );
        CreateStagedFile( staging, Path.Combine( "results", "dump.txt" ) );

        var published = TestResultsStaging.Publish( this._console, staging, this._results );

        Assert.Equal( 3, published.Count );
        Assert.EndsWith( ".trx", published.Last(), StringComparison.Ordinal );
    }

    /// <summary>
    /// <c>dotnet test</c> names a <c>.trx</c> after the machine and a timestamp of one-second resolution, so two runs
    /// can propose the same name. Overwriting would discard the results of one of them.
    /// </summary>
    [Fact]
    public void ACollidingNameIsMadeDistinctInsteadOfOverwriting()
    {
        var first = this.GetStagingDirectory( "First" );
        CreateStagedFile( first, "host_2026-09-25.trx", "first" );
        TestResultsStaging.Publish( this._console, first, this._results );

        var second = this.GetStagingDirectory( "Second" );
        CreateStagedFile( second, "host_2026-09-25.trx", "second" );
        var published = TestResultsStaging.Publish( this._console, second, this._results );

        Assert.Equal( Path.Combine( this._results, "host_2026-09-25.1.trx" ), Assert.Single( published ) );
        Assert.Equal( "first", File.ReadAllText( Path.Combine( this._results, "host_2026-09-25.trx" ) ) );
        Assert.Equal( "second", File.ReadAllText( Path.Combine( this._results, "host_2026-09-25.1.trx" ) ) );
    }

    /// <summary>
    /// A run whose build failed writes no results at all, and that is not an error.
    /// </summary>
    [Fact]
    public void PublishOfAnAbsentStagingDirectoryPublishesNothing()
    {
        Assert.Empty( TestResultsStaging.Publish( this._console, this.GetStagingDirectory(), this._results ) );
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
