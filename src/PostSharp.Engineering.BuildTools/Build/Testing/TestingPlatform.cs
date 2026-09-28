// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using PostSharp.Engineering.BuildTools.Build.Model;
using PostSharp.Engineering.BuildTools.Build.Solutions;
using PostSharp.Engineering.BuildTools.Tools.TeamCity;
using PostSharp.Engineering.BuildTools.Utilities;
using System;
using System.Globalization;
using System.IO;
using System.Xml;
using System.Xml.Linq;

namespace PostSharp.Engineering.BuildTools.Build.Testing;

/// <summary>
/// Runs the tests of a product whose <see cref="Product.TestRunner"/> is <see cref="TestRunner.MicrosoftTestingPlatform"/>
/// with <c>dotnet test</c> in the mode of Microsoft.Testing.Platform, and reports them.
/// </summary>
internal static class TestingPlatform
{
    /// <summary>
    /// The type of the TeamCity <c>importData</c> message of a TRX report of Microsoft.Testing.Platform. Its data rows are
    /// named by <see cref="NameDataRows"/> first.
    /// </summary>
    public const string ReportType = "mstest";

    /// <summary>
    /// The name of the TRX report of each application. The directory of the report, which is the application's own, tells
    /// which application it is. The default name repeats the name of the assembly, and a .NET Framework application with a
    /// long name failed to write its report into a path of more than 260 characters (<c>DirectoryNotFoundException</c>),
    /// while the applications whose path was shorter wrote theirs.
    /// </summary>
    public const string ReportFileName = "report.trx";

    /// <summary>
    /// The first major version of the .NET SDK whose <c>dotnet test</c> accepts <c>--results-directory-layout</c> and
    /// <c>--no-artifact-post-processing</c>. The SDK 10.0.1xx to 10.0.4xx refuse both options: it passes them to the test
    /// applications, which exit with code 5 (invalid command line).
    /// </summary>
    internal const int ResultsDirectoryLayoutSdkMajorVersion = 11;

    /// <summary>
    /// Gets the options of <c>dotnet test</c> that write the TRX reports into <paramref name="resultsDirectory"/>, for the
    /// .NET SDK that <c>dotnet</c> selects in the repository.
    /// </summary>
    public static string GetArguments( BuildContext context, string resultsDirectory, string? filter )
        => GetArguments( resultsDirectory, filter, GetSdkMajorVersion( context ) );

    /// <summary>
    /// Gets the options of <c>dotnet test</c> that write the TRX reports into <paramref name="resultsDirectory"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// With the .NET SDK 11 and later, each application writes its report into a directory of its own (<c>per-module</c>):
    /// the default layout names a report after the assembly, the target framework and the architecture, so <c>net10.0</c>
    /// and <c>net10.0-windows</c> would write the same file. <c>--no-artifact-post-processing</c> keeps <c>dotnet test</c>
    /// from also writing a merged report, which TeamCity would import beside the others and count every test twice.
    /// </para>
    /// <para>
    /// An earlier SDK accepts neither option, and does not merge the reports. Each application then writes its report into
    /// <paramref name="resultsDirectory"/> under the default name, <c>&lt;assembly&gt;_&lt;target framework&gt;_&lt;architecture&gt;.trx</c>,
    /// and <see cref="ReportFileName"/> is not given, because every application would write the same file. Two target
    /// frameworks of one project that differ only by their operating system still write the same file with such an SDK.
    /// </para>
    /// <para>
    /// A filter can select no test in an application, which is then a success (<c>--ignore-exit-code 8</c>); a run whose
    /// filter selects no test at all still fails.
    /// </para>
    /// </remarks>
    /// <param name="sdkMajorVersion">The major version of the .NET SDK, or <c>null</c> when it is not known, which is
    /// treated as a current SDK.</param>
    internal static string GetArguments( string resultsDirectory, string? filter, int? sdkMajorVersion )
    {
        var arguments = sdkMajorVersion is { } major && major < ResultsDirectoryLayoutSdkMajorVersion
            ? $"--report-trx --results-directory \"{resultsDirectory}\""
            : $"--report-trx --report-trx-filename {ReportFileName} --results-directory \"{resultsDirectory}\" --results-directory-layout per-module --no-artifact-post-processing";

        if ( !string.IsNullOrEmpty( filter ) )
        {
            arguments += $" --filter \"{filter}\" --ignore-exit-code 8";
        }

        return arguments;
    }

    /// <summary>
    /// Gets the major version of the .NET SDK that <c>dotnet</c> selects in the repository, which is the one that the
    /// <c>global.json</c> of the repository names, or <c>null</c> when it cannot be determined.
    /// </summary>
    private static int? GetSdkMajorVersion( BuildContext context )
    {
        if ( !ToolInvocationHelper.InvokeTool( context.Console, "dotnet", "--version", context.RepoDirectory, out var exitCode, out var output )
             || exitCode != 0 )
        {
            context.Console.WriteWarning( "Cannot determine the version of the .NET SDK. The options of the .NET SDK 11 are passed to 'dotnet test'." );

            return null;
        }

        var version = output.Trim();
        var dot = version.IndexOf( '.', StringComparison.Ordinal );

        return dot > 0 && int.TryParse( version[..dot], NumberStyles.None, CultureInfo.InvariantCulture, out var major ) ? major : null;
    }

    /// <summary>
    /// Runs the test applications of a solution that a <see cref="MsbuildSolution"/> built, with <c>dotnet test --no-build</c>,
    /// and imports their reports into TeamCity.
    /// </summary>
    /// <remarks>
    /// The solution is not built by <c>dotnet test</c>: it was built by the MSBuild of Visual Studio, which a solution with
    /// native projects needs, and <c>dotnet test</c> only has to find the applications that the build wrote.
    /// </remarks>
    public static bool Test( BuildContext context, BuildSettings settings, Solution solution, string solutionPath )
    {
        // Build.ps1 build skips a test-only solution, and dotnet test does not build this one, so it is built here, by its
        // own MSBuild.
        if ( solution.IsTestOnly && !solution.Build( context, settings ) )
        {
            return false;
        }

        var runKey = TestResultsStaging.GetRunKey( Path.GetRelativePath( context.RepoDirectory, solutionPath ), solution.Name );
        var stagingDirectory = TestResultsStaging.GetStagingDirectory( context.RepoDirectory, context.Product.TestResultsDirectory, runKey );

        try
        {
            return DotNetHelper.Run(
                context,
                settings,
                solutionPath,
                "test",
                $"--no-build {GetArguments( context, stagingDirectory, settings.TestsFilter )}",
                true,
                logName: solution.Name );
        }
        finally
        {
            // Also on failure: the reports of the applications that failed tell which tests failed.
            var resultsDirectory = Path.Combine( context.RepoDirectory, context.Product.TestResultsDirectory );

            foreach ( var report in TestResultsStaging.Publish( context.Console, stagingDirectory, resultsDirectory, runKey ) )
            {
                NameDataRows( context, report );

                if ( context.IsContinuousIntegrationBuild )
                {
                    TeamCityHelper.SendImportDataMessage(
                        ReportType,
                        Path.GetRelativePath( context.RepoDirectory, report ).Replace( Path.DirectorySeparatorChar, '/' ),
                        solution.Name,
                        false );
                }
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
    public static void NameDataRows( BuildContext context, string path )
    {
        try
        {
            NameDataRows( path );
        }
        catch ( Exception e ) when ( e is IOException or XmlException )
        {
            // A report that cannot be rewritten is still worth importing: only the names of the rows are lost.
            context.Console.WriteWarning( $"The data rows of '{path}' cannot be named: {e.Message}" );
        }
    }

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
}
