// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using PostSharp.Engineering.BuildTools.Build.Model;
using PostSharp.Engineering.BuildTools.Tools.TeamCity;
using PostSharp.Engineering.BuildTools.Utilities;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;

namespace PostSharp.Engineering.BuildTools.Build.Solutions;

/// <summary>
/// Runs the Microsoft.Testing.Platform test applications of a solution whose <see cref="TestRunner"/> is
/// <see cref="TestRunner.MicrosoftTestingPlatform"/>.
/// </summary>
/// <remarks>
/// <para>
/// <c>dotnet test</c> runs these applications only in the mode that <c>global.json</c> selects for the whole repository, and
/// the <c>Test</c> target of a solution fails on every project that does not define one. This class writes a project that
/// calls a target on every project of the solution, skipping those that do not define it, and a targets file that defines
/// that target, which it gives to the projects as <c>CustomAfterMicrosoftCommonTargets</c>. In a test application, the target
/// runs the application that the build has already written, with the <c>InvokeTestingPlatform</c> target of
/// <c>Microsoft.Testing.Platform.MSBuild</c>. Nothing is built.
/// </para>
/// <para>
/// The applications that set <c>TestApplicationRunAlone</c> run first, one at a time, and the others then run in parallel.
/// The TRX reports are imported into TeamCity once all the applications have exited.
/// </para>
/// </remarks>
internal static class TestingPlatformTestRunner
{
    private const string _targetsResourceName = "PostSharp.Engineering.BuildTools.Resources.TestingPlatform.targets";

    private static readonly string[] _projectExtensions = [".csproj", ".vbproj", ".fsproj"];

    /// <summary>
    /// Runs the test applications of a solution.
    /// </summary>
    /// <param name="solutionPath">The full path of the solution, solution filter or project.</param>
    /// <param name="runMSBuild">Runs a target of a project with the MSBuild of the solution, and the properties of the build.</param>
    public static bool Test(
        BuildContext context,
        BuildSettings settings,
        Solution solution,
        string solutionPath,
        Func<string, string, bool> runMSBuild )
    {
        if ( !TryGetProjects( context, solutionPath, out var projects ) )
        {
            return false;
        }

        var runKey = TestResultsStaging.GetRunKey( Path.GetRelativePath( context.RepoDirectory, solutionPath ), solution.Name );
        var stagingDirectory = TestResultsStaging.GetStagingDirectory( context.RepoDirectory, context.Product.TestResultsDirectory, runKey );

        // The files are written under the repository, because dotnet resolves its SDK from the global.json of the working
        // directory, and both files are kept after the run, so that a failed run can be diagnosed from them.
        var workDirectory = Path.Combine( context.RepoDirectory, "artifacts", "testing-platform", runKey );

        DeleteDirectory( stagingDirectory );
        DeleteDirectory( workDirectory );
        Directory.CreateDirectory( workDirectory );

        var targetsPath = Path.Combine( workDirectory, "TestingPlatform.targets" );
        File.WriteAllText( targetsPath, ReadTargets(), new UTF8Encoding( false ) );

        var projectPath = Path.Combine( workDirectory, $"{solution.Name}.Test.proj" );
        CreateProject( projects, targetsPath, stagingDirectory, settings.TestsFilter ).Save( projectPath );

        context.Console.WriteMessage( $"Running the test applications of {projects.Count} project(s) of '{solution.Name}'." );

        try
        {
            return runMSBuild( projectPath, "Test" );
        }
        finally
        {
            // Also on failure: the reports of the applications that failed tell which tests failed.
            ImportResults( context, stagingDirectory, runKey, solution.Name );
        }
    }

    /// <summary>
    /// Gets the managed projects of a solution, solution filter or project, by their full path.
    /// </summary>
    public static bool TryGetProjects( BuildContext context, string solutionPath, out IReadOnlyList<string> projects )
    {
        var extension = Path.GetExtension( solutionPath );

        if ( extension.Equals( ".slnf", StringComparison.OrdinalIgnoreCase ) )
        {
            // A solution filter names the projects by a path relative to the directory of its solution.
            using var document = JsonDocument.Parse( File.ReadAllText( solutionPath ) );
            var solutionElement = document.RootElement.GetProperty( "solution" );
            var filteredSolution = Path.GetFullPath( solutionElement.GetProperty( "path" ).GetString()!, Path.GetDirectoryName( solutionPath )! );

            projects = solutionElement.GetProperty( "projects" )
                .EnumerateArray()
                .Select( p => Path.GetFullPath( p.GetString()!.Replace( '\\', Path.DirectorySeparatorChar ), Path.GetDirectoryName( filteredSolution )! ) )
                .Where( IsManagedProject )
                .ToList();

            return true;
        }

        if ( !extension.Equals( ".sln", StringComparison.OrdinalIgnoreCase ) && !extension.Equals( ".slnx", StringComparison.OrdinalIgnoreCase ) )
        {
            projects = [solutionPath];

            return true;
        }

        if ( !ToolInvocationHelper.InvokeTool( context.Console, "dotnet", $"sln \"{solutionPath}\" list", context.RepoDirectory, out _, out var output ) )
        {
            context.Console.WriteError( $"Cannot list the projects of '{solutionPath}'." );
            context.Console.WriteError( output );
            projects = [];

            return false;
        }

        // The output has a header, and names each project by a path relative to the directory of the solution.
        projects = output
            .Split( '\r', '\n' )
            .Select( l => l.Trim() )
            .Where( IsManagedProject )
            .Select( p => Path.GetFullPath( p.Replace( '\\', Path.DirectorySeparatorChar ), Path.GetDirectoryName( solutionPath )! ) )
            .ToList();

        return true;
    }

    // A test application is a managed project. The other projects of a solution, such as a shared project, cannot always be
    // evaluated alone, and evaluating a native project costs time for nothing.
    private static bool IsManagedProject( string path ) => _projectExtensions.Any( e => path.EndsWith( e, StringComparison.OrdinalIgnoreCase ) );

    private static string ReadTargets()
    {
        using var stream = typeof(TestingPlatformTestRunner).Assembly.GetManifestResourceStream( _targetsResourceName )
                           ?? throw new InvalidOperationException( $"Cannot find the embedded resource '{_targetsResourceName}'." );

        using var reader = new StreamReader( stream );

        return reader.ReadToEnd();
    }

    /// <summary>
    /// Creates the project that calls <c>PostSharpEngineeringTest</c> on every project, once for the applications that run
    /// alone and once for the others.
    /// </summary>
    internal static XDocument CreateProject( IEnumerable<string> projects, string targetsPath, string resultsDirectory, string? filter )
    {
        var properties = string.Join(
            ";",
            $"CustomAfterMicrosoftCommonTargets={Escape( targetsPath )}",
            $"CustomAfterMicrosoftCommonCrossTargetingTargets={Escape( targetsPath )}",
            $"PostSharpEngineeringTestResultsDirectory={Escape( resultsDirectory )}",
            $"PostSharpEngineeringTestFilter={Escape( filter ?? "" )}" );

        XElement CallTarget( string phase, bool inParallel )
            => new(
                "MSBuild",
                new XAttribute( "Projects", "@(_TestProject)" ),
                new XAttribute( "Targets", "PostSharpEngineeringTest" ),
                new XAttribute( "SkipNonexistentTargets", "true" ),
                new XAttribute( "BuildInParallel", inParallel ? "true" : "false" ),
                new XAttribute( "Properties", $"{properties};PostSharpEngineeringTestPhase={phase}" ),
                new XAttribute( "ContinueOnError", "ErrorAndContinue" ) );

        return new XDocument(
            new XElement(
                "Project",
                new XElement( "ItemGroup", projects.Select( p => new XElement( "_TestProject", new XAttribute( "Include", Escape( p ) ) ) ) ),
                new XElement( "Target", new XAttribute( "Name", "Test" ), CallTarget( "RunAlone", false ), CallTarget( "Parallel", true ) ) ) );
    }

    // The characters that MSBuild reads in a property value or an item specification.
    private static string Escape( string value )
    {
        var builder = new StringBuilder( value.Length );

        foreach ( var character in value )
        {
            if ( character is '%' or ';' or '$' or '@' or '\'' or '*' or '?' )
            {
                builder.Append( '%' ).Append( ((int) character).ToString( "X2", System.Globalization.CultureInfo.InvariantCulture ) );
            }
            else
            {
                builder.Append( character );
            }
        }

        return builder.ToString();
    }

    private static void ImportResults( BuildContext context, string stagingDirectory, string runKey, string flowId )
    {
        var resultsDirectory = Path.Combine( context.RepoDirectory, context.Product.TestResultsDirectory );

        foreach ( var report in TestResultsStaging.Publish( context.Console, stagingDirectory, resultsDirectory, runKey ) )
        {
            try
            {
                NameDataRows( report );
            }
            catch ( Exception e ) when ( e is IOException or System.Xml.XmlException )
            {
                context.Console.WriteWarning( $"The data rows of '{report}' cannot be named: {e.Message}" );
            }

            if ( context.IsContinuousIntegrationBuild )
            {
                TeamCityHelper.SendImportDataMessage(
                    "mstest",
                    Path.GetRelativePath( context.RepoDirectory, report ).Replace( Path.DirectorySeparatorChar, '/' ),
                    flowId,
                    false );
            }
        }
    }

    /// <summary>
    /// Gives each data row of a theory its own name in a TRX report written by Microsoft.Testing.Platform.
    /// </summary>
    /// <remarks>
    /// The report names a row in the <c>name</c> attribute of its <c>UnitTest</c> element, for example
    /// <c>Namespace.Class.Method(value: 1)</c>, but its <c>TestMethod</c> element carries the name of the method only. The
    /// MSTest importer of TeamCity names a test from <c>TestMethod</c>, so it reports every row of a theory as one test that
    /// ran several times. This writes the name of the row, without the class, into <c>TestMethod</c>. <c>RunTests.ps1</c>
    /// does the same for the archives.
    /// </remarks>
    internal static void NameDataRows( string path )
    {
        var document = XDocument.Load( path, LoadOptions.PreserveWhitespace );
        var ns = document.Root!.Name.Namespace;
        var renamed = 0;

        foreach ( var unitTest in document.Descendants( ns + "UnitTest" ) )
        {
            var testMethod = unitTest.Element( ns + "TestMethod" );
            var testName = (string?) unitTest.Attribute( "name" );
            var className = (string?) testMethod?.Attribute( "className" );

            if ( testMethod == null || testName == null || className == null )
            {
                continue;
            }

            var rowName = testName.StartsWith( className + ".", StringComparison.Ordinal ) ? testName[(className.Length + 1)..] : testName;

            if ( rowName != (string?) testMethod.Attribute( "name" ) )
            {
                testMethod.SetAttributeValue( "name", rowName );
                renamed++;
            }
        }

        if ( renamed > 0 )
        {
            document.Save( path, SaveOptions.DisableFormatting );
        }
    }

    private static void DeleteDirectory( string directory )
    {
        if ( Directory.Exists( directory ) )
        {
            Directory.Delete( directory, true );
        }
    }
}
