// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using PostSharp.Engineering.BuildTools.Utilities;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PostSharp.Engineering.BuildTools.Build.Solutions;

/// <summary>
/// Moves the test result files of a completed run into the directory that TeamCity watches.
/// </summary>
/// <remarks>
/// <para>
/// TeamCity parses a <c>.trx</c> file as soon as it is told about one, and its parser opens the file without
/// sharing. The scenarios of a <see cref="ManySolutions"/> run in parallel and write into one results directory, so
/// the parser reading the results of one scenario opened the file that another <c>dotnet test</c> was still writing,
/// and the build failed with "The process cannot access the file because it is being used by another process".
/// </para>
/// <para>
/// Each run therefore writes into a staging directory of its own, and its files are moved into the results directory
/// once the test process has exited. A move within one volume is a rename, so a file appears in the watched
/// directory only when it is complete and closed. This is also why the staging directory is a sibling of the results
/// directory and not a subdirectory of it: a subdirectory would be on the same volume, but it would also be
/// published as a build artifact and matched by anything scanning the results directory recursively.
/// </para>
/// </remarks>
internal static class TestResultsStaging
{
    /// <summary>
    /// The suffix that distinguishes the staging directory from the results directory it belongs to.
    /// </summary>
    public const string DirectorySuffix = ".staging";

    /// <summary>
    /// Gets the directory that <c>dotnet test</c> writes the results of one run to.
    /// </summary>
    /// <param name="logName">A name that identifies the run. Every run needs a staging directory of its own,
    /// because the runs of one scenario are sequential but the scenarios are not.</param>
    public static string GetStagingDirectory( string repoDirectory, string testResultsDirectory, string logName )
        => Path.Combine(
            repoDirectory,
            testResultsDirectory + DirectorySuffix,
            string.Join( "_", logName.Split( Path.GetInvalidFileNameChars() ) ) );

    /// <summary>
    /// Moves everything in <paramref name="stagingDirectory"/> into <paramref name="resultsDirectory"/> and deletes
    /// the staging directory.
    /// </summary>
    /// <returns>The full path of each file that was moved, in the order in which it was moved.</returns>
    public static IReadOnlyList<string> Publish( ConsoleHelper console, string stagingDirectory, string resultsDirectory )
    {
        var published = new List<string>();

        if ( !Directory.Exists( stagingDirectory ) )
        {
            return published;
        }

        Directory.CreateDirectory( resultsDirectory );

        // Attachments are moved before the `.trx` files that reference them, so that a parser reading a `.trx` the
        // moment it appears finds everything that it names.
        var files = Directory.GetFiles( stagingDirectory, "*", SearchOption.AllDirectories )
            .OrderBy( f => IsTestResultFile( f ) ? 1 : 0 )
            .ThenBy( f => f, StringComparer.Ordinal );

        foreach ( var file in files )
        {
            var destination = GetUnusedPath( resultsDirectory, Path.GetFileName( file ) );

            try
            {
                File.Move( file, destination );
            }
            catch ( Exception e ) when ( e is IOException or UnauthorizedAccessException )
            {
                // Losing a result file must not fail a build whose tests have already reported their verdict.
                console.WriteWarning( $"Cannot move the test result file '{file}' to '{destination}': {e.Message}" );

                continue;
            }

            published.Add( destination );
        }

        try
        {
            Directory.Delete( stagingDirectory, true );
        }
        catch ( Exception e ) when ( e is IOException or UnauthorizedAccessException )
        {
            // A directory left behind is not worth a warning. It is emptied by the moves above and deleted by the
            // next `Build.ps1 prepare`.
        }

        return published;
    }

    private static bool IsTestResultFile( string path ) => Path.GetExtension( path ).Equals( ".trx", StringComparison.OrdinalIgnoreCase );

    /// <summary>
    /// Gets a path in <paramref name="directory"/> that no file occupies. The name that <c>dotnet test</c> gives a
    /// <c>.trx</c> file is built from the machine name and a timestamp of one-second resolution, so two runs can
    /// propose the same one.
    /// </summary>
    private static string GetUnusedPath( string directory, string fileName )
    {
        var candidate = Path.Combine( directory, fileName );

        for ( var i = 1; File.Exists( candidate ); i++ )
        {
            candidate = Path.Combine( directory, $"{Path.GetFileNameWithoutExtension( fileName )}.{i}{Path.GetExtension( fileName )}" );
        }

        return candidate;
    }
}
