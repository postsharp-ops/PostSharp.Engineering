// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using PostSharp.Engineering.BuildTools.Build.Model;
using PostSharp.Engineering.BuildTools.ContinuousIntegration.Model;
using PostSharp.Engineering.BuildTools.Utilities;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text;

namespace PostSharp.Engineering.BuildTools.Build.Testing;

/// <summary>
/// The conventions of the test archives of a product whose solutions set <see cref="Solution.ContainsTestApplications"/>. See
/// <c>doc/testing-platform.md</c>.
/// </summary>
internal static class TestArchives
{
    /// <summary>
    /// The name of the script that <c>generate-scripts</c> writes into the engineering directory, and that runs the archives.
    /// </summary>
    public const string ScriptName = "RunTests.ps1";

    /// <summary>
    /// The directory of the archives, relative to the repository root. <c>TestArchive.targets</c> writes them there unless a
    /// project sets <c>TestArchiveDirectory</c>.
    /// </summary>
    public const string Directory = "artifacts/tests";

    /// <summary>
    /// The name of the file, in the engineering directory, that lists the archives that the build configurations of the
    /// test agents download.
    /// </summary>
    public const string ListFileName = "test-archives.txt";

    /// <summary>
    /// Adds <c>PublishTestArchive=true</c> to the properties of the build of the solutions of a product that publishes test
    /// archives, unless the command line sets it. A build in the IDE does not set it, so it does not spend the time of a
    /// publication on every build. On TeamCity, only the builds of the configurations that the test agents download the
    /// archives from write them (see <see cref="IsSourceConfiguration"/>); a local build always writes them, so that
    /// <c>Build.ps1 test</c> can run them.
    /// </summary>
    public static BuildSettings AddBuildProperties( Product product, BuildSettings settings, bool isTeamCityBuild )
        => product.PublishesTestArchives
           && !settings.Properties.ContainsKey( "PublishTestArchive" )
           && (!isTeamCityBuild || IsSourceConfiguration( product, settings.BuildConfiguration ))
            ? settings.WithAdditionalProperties( ImmutableDictionary<string, string>.Empty.Add( "PublishTestArchive", "true" ) )
            : settings;

    /// <summary>
    /// Determines whether the build of a configuration writes and publishes the archives: whether it sets
    /// <see cref="BuildConfigurationInfo.RunsTestArchives"/>.
    /// </summary>
    public static bool IsSourceConfiguration( Product product, BuildConfiguration configuration )
        => product.PublishesTestArchives && product.Configurations[configuration].RunsTestArchives;

    /// <summary>
    /// Runs the tests of a solution whose <see cref="Solution.TestRunner"/> is <see cref="TestRunner.MicrosoftTestingPlatform"/>
    /// in a product whose tests use VSTest: builds the test archives of the solution, and runs the archives that apply to the
    /// platform of the build host with <see cref="ScriptName"/>, as the test agents run them.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The .NET SDK 10 refuses to run a Microsoft.Testing.Platform application in the mode of VSTest, and only a
    /// <c>global.json</c> selects the other mode, so <c>dotnet test</c> cannot run these applications in such a product.
    /// </para>
    /// <para>
    /// The solution is built with <c>PublishTestArchive=true</c> here, because the build of the product writes the archives
    /// only in some configurations (see <see cref="AddBuildProperties"/>). The script reports the results to TeamCity and
    /// writes the TRX reports into the test results directory of the product.
    /// </para>
    /// </remarks>
    public static bool RunOnHost( BuildContext context, BuildSettings settings, Solution solution )
    {
        if ( !solution.ContainsTestApplications )
        {
            context.Console.WriteError(
                $"The solution '{solution.Name}' sets TestRunner to Microsoft.Testing.Platform in a product whose tests use VSTest, so its "
                + "tests run from test archives, but it does not set ContainsTestApplications." );

            return false;
        }

        var scriptPath = Path.Combine( context.RepoDirectory, context.Product.EngineeringDirectory, ScriptName );

        if ( !File.Exists( scriptPath ) )
        {
            context.Console.WriteError( $"The script '{scriptPath}' does not exist. Run 'Build.ps1 generate-scripts'." );

            return false;
        }

        if ( !string.IsNullOrEmpty( settings.TestsFilter ) )
        {
            context.Console.WriteWarning(
                $"The test filter is ignored for the solution '{solution.Name}', whose test applications run from test archives." );
        }

        var buildSettings = settings.Properties.ContainsKey( "PublishTestArchive" )
            ? settings
            : settings.WithAdditionalProperties( ImmutableDictionary<string, string>.Empty.Add( "PublishTestArchive", "true" ) );

        if ( !solution.Build( context, buildSettings ) )
        {
            return false;
        }

        var solutionPath = Path.Combine( context.RepoDirectory, solution.SolutionPath );

        if ( !TestApplicationDiscovery.TryDiscover( context, settings.BuildConfiguration, solutionPath, out var applications ) )
        {
            return false;
        }

        if ( applications.IsEmpty )
        {
            context.Console.WriteError( $"The solution '{solution.Name}' contains no Microsoft.Testing.Platform test application." );

            return false;
        }

        // The script reads a comma-separated list from the command line, and runs only the archives that apply to the platform
        // of the host; it fails when none applies.
        var names = string.Join( ",", applications.Select( a => a.ArchiveName ).Distinct( StringComparer.OrdinalIgnoreCase ) );

        return ToolInvocationHelper.InvokePowershell( context.Console, $"\"{scriptPath}\"", $"-Name {names}", context.RepoDirectory );
    }

    private static string GetListPath( BuildContext context ) =>Path.Combine( context.RepoDirectory, context.Product.EngineeringDirectory, ListFileName );

    /// <summary>
    /// Writes the list of the archives that <c>generate-scripts</c> planned the build configurations from, each with the
    /// packages that its build configurations download from the builds that publish them (<see cref="TestArchivePackages"/>).
    /// </summary>
    public static void WriteList( BuildContext context, IEnumerable<TestApplication> applications )
    {
        var content = new StringBuilder();
        content.AppendLine( "# The test archives that the build configurations of the test agents download, one per line, each followed by the" );
        content.AppendLine( "# packages that are not from nuget.org, which they download from the build that publishes them, as <producer>/<id>." );
        content.AppendLine( "# This file is generated by 'Build.ps1 generate-scripts' from the test applications of the solutions. 'Build.ps1 build'" );
        content.AppendLine( "# fails when the archives or their packages differ, which means that the build configurations need regenerating." );

        foreach ( var application in applications.OrderBy( a => a.ArchiveName, StringComparer.Ordinal ) )
        {
            content.AppendLine(
                application.Packages.IsEmpty
                    ? application.ArchiveName
                    : $"{application.ArchiveName}: {string.Join( " ", application.Packages )}" );
        }

        TextFileHelper.WriteIfDifferent( GetListPath( context ), content.ToString(), context );
    }

    /// <summary>
    /// Compares the archives that the build wrote, and their packages, with the list that <c>generate-scripts</c> wrote, when
    /// there is one.
    /// </summary>
    /// <returns><c>false</c> if they differ. An archive missing from the list would be run by no build configuration, and a
    /// listed archive that the build did not write would fail the download of the build configurations that run it. A package
    /// missing from the list would not be downloaded, and a listed package that no build publishes would fail the download.</returns>
    public static bool Verify( BuildContext context, BuildConfiguration configuration )
        => TestApplicationDiscovery.TryDiscover( context, configuration, out var applications ) && Verify( context, applications );

    /// <param name="applications">The test applications of the build.</param>
    internal static bool Verify( BuildContext context, IReadOnlyList<TestApplication> applications )
    {
        // nuget.config is read only when an application has a restore graph: an archive of the ps1 kind has none.
        var sources = TestArchivePackages.Sources.Empty;

        if ( applications.Any( a => a.ProjectAssetsFile.Length > 0 ) && !TestArchivePackages.Sources.TryLoad( context, out sources ) )
        {
            return false;
        }

        var packagesByArchive = new Dictionary<string, ImmutableArray<TestArchivePackages.Package>>( StringComparer.OrdinalIgnoreCase );

        foreach ( var application in applications )
        {
            if ( !TestArchivePackages.TryGetPackages( context.Console, sources, application, out var packages ) )
            {
                return false;
            }

            packagesByArchive[application.ArchiveName] = packages;
        }

        var archivesDirectory = Path.Combine( context.RepoDirectory, Directory );

        var archivePaths = System.IO.Directory.Exists( archivesDirectory ) ? System.IO.Directory.GetFiles( archivesDirectory, "*.zip" ) : [];

        // A package that a manifest takes from another source than nuget.org, and that the restore graph does not show as
        // publishing a file, would be downloaded by no build configuration. It is a file that the props of a package add.
        var success = true;

        foreach ( var archivePath in archivePaths )
        {
            var name = Path.GetFileNameWithoutExtension( archivePath );

            if ( !packagesByArchive.TryGetValue( name, out var packages ) )
            {
                continue;
            }

            var notDownloaded = TestArchivePackages.GetPackagesWithoutUrl( archivePath )
                .Except( packages.Select( p => p.Id.ToLowerInvariant() ), StringComparer.Ordinal )
                .ToList();

            if ( notDownloaded.Count > 0 )
            {
                context.Console.WriteError(
                    $"The test archive '{name}' takes files from packages that are not from nuget.org and have no file to publish in the restore graph "
                    + $"of its project, so its build configurations do not download them: {string.Join( ", ", notDownloaded )}." );

                success = false;
            }
        }

        if ( !success )
        {
            return false;
        }

        var listPath = GetListPath( context );

        if ( !File.Exists( listPath ) )
        {
            return true;
        }

        var expected = File.ReadAllLines( listPath )
            .Select( l => l.Trim() )
            .Where( l => l.Length > 0 && !l.StartsWith( '#' ) )
            .Select( l => l.Split( ':', 2 ) )
            .ToDictionary(
                l => l[0].Trim(),
                l => l.Length > 1 ? l[1].Split( ' ', StringSplitOptions.RemoveEmptyEntries ).Order( StringComparer.Ordinal ).ToList() : [],
                StringComparer.OrdinalIgnoreCase );

        var actual = archivePaths.Select( Path.GetFileNameWithoutExtension ).ToHashSet( StringComparer.OrdinalIgnoreCase );

        var unexpected = actual.Except( expected.Keys ).Order( StringComparer.Ordinal ).ToList();
        var missing = expected.Keys.Except( actual ).Order( StringComparer.Ordinal ).ToList();

        // The packages of the archives that the build wrote and the list names.
        var differentPackages = actual.Intersect( expected.Keys )
            .Where( a => !packagesByArchive.GetValueOrDefault( a, [] )
                .Select( p => p.Reference )
                .Order( StringComparer.Ordinal )
                .SequenceEqual( expected[a], StringComparer.Ordinal ) )
            .Order( StringComparer.Ordinal )
            .ToList();

        if ( unexpected.Count == 0 && missing.Count == 0 && differentPackages.Count == 0 )
        {
            return true;
        }

        if ( unexpected.Count > 0 )
        {
            context.Console.WriteError( $"The build wrote test archives that no build configuration runs: {string.Join( ", ", unexpected )}." );
        }

        if ( missing.Count > 0 )
        {
            context.Console.WriteError( $"The build did not write test archives that build configurations run: {string.Join( ", ", missing )}." );
        }

        if ( differentPackages.Count > 0 )
        {
            context.Console.WriteError(
                $"The packages that these test archives need differ from the packages that their build configurations download: {string.Join( ", ", differentPackages )}." );
        }

        context.Console.WriteError( $"Run 'Build.ps1 generate-scripts', which plans the build configurations of the test agents and writes '{listPath}'." );

        return false;
    }
}
