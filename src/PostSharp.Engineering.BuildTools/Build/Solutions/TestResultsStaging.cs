// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using PostSharp.Engineering.BuildTools.Utilities;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;

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
/// Each run therefore writes into a staging directory of its own, which is a sibling of the results directory and
/// not a subdirectory of it, and that whole directory is renamed into the results directory once the test process
/// has exited. One rename within one volume is atomic, so nothing partially written is ever visible under the
/// results directory, whatever moment TeamCity chooses to read it. Renaming the directory rather than its files one
/// by one also keeps the layout that <c>dotnet test</c> produced, which matters because a <c>.trx</c> names its
/// attachments by a path relative to itself.
/// </para>
/// </remarks>
internal static class TestResultsStaging
{
    /// <summary>
    /// The suffix that distinguishes the staging directory from the results directory it belongs to.
    /// </summary>
    public const string DirectorySuffix = ".staging";

    /// <summary>
    /// The number of names tried when the destination of the rename is taken. It is only ever above one when a
    /// previous build left its results behind, because a run key is unique within a build.
    /// </summary>
    private const int _maxAttempts = 100;

    /// <summary>
    /// Gets the name that identifies one run of one scenario, and therefore its staging directory and the directory
    /// its results end up in.
    /// </summary>
    /// <remarks>
    /// The log name alone is not enough. It is built from <see cref="Model.Solution.Name"/>, which is only the file
    /// name of the project, so two scenarios such as <c>a/Tests.csproj</c> and <c>b/Tests.csproj</c> would share a
    /// directory and, running in parallel, publish or delete each other's files. The hash of the path of the project
    /// separates them without making the name as long as the path.
    /// </remarks>
    /// <param name="solutionPath">The path of the project or solution, relative to the root of the repository.</param>
    /// <param name="logName">The name that identifies the run within the scenario, i.e. the matrix entry.</param>
    public static string GetRunKey( string solutionPath, string logName )
    {
        var normalized = solutionPath.Replace( '\\', '/' );
        var hash = SHA256.HashData( Encoding.UTF8.GetBytes( normalized ) );

        return string.Create(
            CultureInfo.InvariantCulture,
            $"{SanitizeFileName( logName )}-{Convert.ToHexString( hash )[..8].ToLowerInvariant()}" );
    }

    /// <summary>
    /// Gets the directory that <c>dotnet test</c> writes the results of one run to.
    /// </summary>
    public static string GetStagingDirectory( string repoDirectory, string testResultsDirectory, string runKey )
        => Path.Combine( repoDirectory, testResultsDirectory + DirectorySuffix, SanitizeFileName( runKey ) );

    /// <summary>
    /// Renames the staging directory of one run into the results directory.
    /// </summary>
    /// <returns>The full path of each <c>.trx</c> file now under the results directory. Attachments are moved with
    /// them but are not returned, because only a <c>.trx</c> is a test report: telling TeamCity to import anything
    /// else as one makes it parse a log or a dump as XML.</returns>
    public static IReadOnlyList<string> Publish( ConsoleHelper console, string stagingDirectory, string resultsDirectory, string runKey )
    {
        if ( !Directory.Exists( stagingDirectory ) )
        {
            return [];
        }

        Directory.CreateDirectory( resultsDirectory );

        for ( var attempt = 0; attempt < _maxAttempts; attempt++ )
        {
            var name = attempt == 0 ? runKey : string.Create( CultureInfo.InvariantCulture, $"{runKey}.{attempt}" );
            var destination = Path.Combine( resultsDirectory, SanitizeFileName( name ) );

            if ( Directory.Exists( destination ) )
            {
                continue;
            }

            try
            {
                Directory.Move( stagingDirectory, destination );
            }
            catch ( IOException )
            {
                // The destination was taken between the check and the rename. Nothing is lost and nothing has moved,
                // so the next name is tried. This is why the check above is not an assertion.
                continue;
            }
            catch ( UnauthorizedAccessException e )
            {
                // Losing a result file must not fail a build whose tests have already reported their verdict.
                console.WriteWarning( $"Cannot move the test results of '{runKey}' to '{destination}': {e.Message}" );

                return [];
            }

            return Directory.GetFiles( destination, "*.trx", SearchOption.AllDirectories );
        }

        console.WriteWarning( $"Cannot move the test results of '{runKey}': no unused name was available under '{resultsDirectory}'." );

        return [];
    }

    private static string SanitizeFileName( string name ) => string.Join( "_", name.Split( Path.GetInvalidFileNameChars() ) );
}
