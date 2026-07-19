// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using JetBrains.Annotations;
using Newtonsoft.Json;
using PostSharp.Engineering.BuildTools.Build.Model;
using PostSharp.Engineering.BuildTools.Tools.TeamCity;
using PostSharp.Engineering.BuildTools.Utilities;
using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace PostSharp.Engineering.BuildTools.Build.Solutions;

/// <summary>
/// A <see cref="Solution"/> that builds a single project or solution and whose build can be asserted by a
/// <c>test.json</c> file placed next to it. Derived classes only supply the command line of the build engine;
/// the reading of <c>test.json</c>, the property matrix and the assertion of the diagnostics are implemented here
/// so that all engines behave identically.
/// </summary>
/// <remarks>
/// The point of this class is that the assertion logic is engine-independent. It used to live on the <c>dotnet</c>
/// implementation only, which meant a scenario could not be moved between engines without losing its assertions.
/// Keep engine-specific concerns in <see cref="Invoke"/>: anything added here must make sense for every engine.
/// </remarks>
[PublicAPI]
public abstract class TestableSolution : Solution
{
    protected TestableSolution( string solutionPath ) : base( solutionPath ) { }

    /// <summary>
    /// Gets the full path of the project or solution, with links and junctions resolved.
    /// </summary>
    protected string GetFinalSolutionPath( BuildContext context )
        => FileSystemHelper.GetFinalPath( Path.Combine( context.RepoDirectory, this.SolutionPath ) );

    protected ToolInvocationOptions CreateInvocationOptions() => new( this.EnvironmentVariables );

    /// <summary>
    /// Gets a value indicating whether <see cref="Test"/> produces <c>.trx</c> files that must be imported into TeamCity.
    /// Defaults to <c>false</c>, because a scenario that merely compiles — which is what most scenarios do — produces
    /// no test results, and reporting an empty result set to TeamCity would be noise.
    /// </summary>
    protected virtual bool ProducesTestResults => false;

    /// <summary>
    /// Builds the command line of the build engine and executes it. This is the only difference between the
    /// implementations of <see cref="TestableSolution"/>.
    /// </summary>
    /// <param name="logName">A unique name, valid as a file name, identifying this run. It must be used to name the build log.</param>
    /// <param name="captureOutput">When <c>true</c>, the output must be returned in <paramref name="output"/> instead of being written to the console.</param>
    /// <returns><c>true</c> if the tool ran and reported success. Callers that assert on the output ignore this value
    /// and judge <paramref name="exitCode"/> and <paramref name="output"/> instead, because a non-zero exit code is a
    /// legitimate expected outcome for a scenario. Implementations must therefore always set both <c>out</c>
    /// parameters when <paramref name="captureOutput"/> is <c>true</c>, including on failure.</returns>
    protected abstract bool Invoke(
        BuildContext context,
        BuildSettings settings,
        SolutionCommand command,
        EffectiveTestOptions options,
        string logName,
        bool captureOutput,
        out int exitCode,
        out string output );

    public override bool Build( BuildContext context, BuildSettings settings ) => this.RunBuildOrTest( context, settings, test: false );

    public override bool Test( BuildContext context, BuildSettings settings ) => this.RunBuildOrTest( context, settings, test: true );

    /// <summary>
    /// Reads the <c>test.json</c> of the scenario, if any, then executes one run per matrix entry.
    /// </summary>
    private bool RunBuildOrTest( BuildContext context, BuildSettings settings, bool test )
    {
        var projectOrSolution = this.GetFinalSolutionPath( context );
        var projectOrSolutionDirectory = Path.GetDirectoryName( Path.GetFullPath( projectOrSolution ) );

        if ( projectOrSolutionDirectory == null )
        {
            context.Console.WriteError( $"Unexpected format of project or solution file path '{projectOrSolution}'." );

            return false;
        }

        // The test.json file is located next to the project or solution file.
        var testJsonFile = Path.Combine( projectOrSolutionDirectory, "test.json" );

        ImmutableArray<EffectiveTestOptions> runs;

        // The presence of test.json decides whether the output is asserted or merely streamed to the console. This is
        // also why it is threaded down into RunOnce: a scenario without test.json must keep behaving exactly as it did
        // before assertions existed, i.e. live output judged by the exit code alone.
        var hasTestJson = File.Exists( testJsonFile );

        if ( hasTestJson )
        {
            var testOptions = JsonConvert.DeserializeObject<TestOptions>( File.ReadAllText( testJsonFile ) );

            if ( testOptions == null )
            {
                context.Console.WriteError( $"No test options found in file '{testJsonFile}'." );

                return false;
            }

            // BuildOnly is a property of the scenario as a whole, not of a matrix entry, so it is honored before the
            // matrix is expanded.
            if ( test && testOptions.BuildOnly )
            {
                context.Console.WriteMessage( $"Test skipped for '{projectOrSolution}' as configured in '{testJsonFile}'." );

                return true;
            }

            runs = testOptions.GetRuns();
        }
        else
        {
            runs = [EffectiveTestOptions.Default];
        }

        var success = true;

        foreach ( var run in runs )
        {
            // Deliberately not short-circuiting: every matrix entry runs even after one has failed, so that a CI log
            // reports all the failing entries at once instead of only the first one.
            if ( !this.RunOnce( context, settings, test, run, hasTestJson, testJsonFile ) )
            {
                success = false;
            }
        }

        if ( test && this.ProducesTestResults && context.IsContinuousIntegrationBuild )
        {
            // Export test result files to TeamCity. This happens once per scenario rather than once per matrix entry,
            // because the message imports a glob over the whole results directory, which every entry has written to.
            TeamCityHelper.SendImportDataMessage(
                "vstest",
                Path.Combine( context.Product.TestResultsDirectory, "*.trx" ).Replace( Path.DirectorySeparatorChar, '/' ),
                Path.GetFileName( projectOrSolution ),
                false );
        }

        return success;
    }

    /// <summary>
    /// Executes one entry of the matrix, i.e. one invocation of the build engine, and asserts its outcome.
    /// </summary>
    private bool RunOnce(
        BuildContext context,
        BuildSettings settings,
        bool test,
        EffectiveTestOptions run,
        bool hasTestJson,
        string testJsonFile )
    {
        var command = test ? SolutionCommand.Test : SolutionCommand.Build;

        // The suffix keeps the log of each matrix entry distinct. Without it, entries would overwrite each other's
        // logs and only the last one would be diagnosable from CI.
        var suffix = run.GetLogNameSuffix();
        var logName = suffix == null ? this.Name : $"{this.Name}.{SanitizeFileName( suffix )}";

        // The matrix properties reach the engine as ordinary `-p:` arguments, which is why they are merged into the
        // settings rather than passed to Invoke separately.
        var runSettings = settings.WithAdditionalProperties( run.Properties );

        if ( !hasTestJson )
        {
            // Nothing to assert, so let the output stream to the console and trust the engine's own verdict.
            return this.Invoke( context, runSettings, command, run, logName, captureOutput: false, out _, out _ );
        }

        if ( suffix == null )
        {
            context.Console.WriteMessage( $"Running the {command} command as configured in '{testJsonFile}'." );
        }
        else
        {
            context.Console.WriteMessage( $"Running the {command} command with '{suffix}' as configured in '{testJsonFile}'." );
        }

        // The return value is discarded on purpose: a failing build is a legitimate expected outcome here, so the
        // verdict comes from EvaluateOutput and not from the engine's exit status.
        _ = this.Invoke( context, runSettings, command, run, logName, captureOutput: true, out var exitCode, out var output );

        return EvaluateOutput( context, run, exitCode, output );
    }

    /// <summary>
    /// Asserts the outcome of a single run against its <see cref="EffectiveTestOptions"/>. This logic is
    /// engine-independent.
    /// </summary>
    /// <remarks>
    /// There are three mutually exclusive modes, in this order of precedence: assert on the diagnostics; report a
    /// non-zero exit code; match <see cref="EffectiveTestOptions.ErrorRegexes"/> against the whole output. Note that
    /// the last one is only reached when the build succeeded, which is intentional — it exists to catch a build that
    /// passed but should not have.
    /// </remarks>
    private static bool EvaluateOutput( BuildContext context, EffectiveTestOptions run, int exitCode, string output )
    {
        var success = exitCode == 0 || run.IgnoreExitCode;
        var writeOutputOnSuccess = true;

        if ( run.ExpectedDiagnosticsRegexes != null || run.ForbiddenDiagnosticsRegexes != null || run.FailOnUnexpectedDiagnostics )
        {
            // Both `dotnet` and MSBuild.exe emit diagnostics in the canonical MSBuild format, so matching on
            // `: error ` / `: warning ` works for every engine. This is what makes the assertions portable across
            // engines, and it is also why the whole output is needed rather than just the exit code: the symptom being
            // asserted is often a warning, which does not fail the build.
            var diagnostics = output.Split( '\n' )
                .Select( l => l.Trim() )
                .Where( l => l.Contains( ": error ", StringComparison.Ordinal ) || l.Contains( ": warning ", StringComparison.Ordinal ) )
                .ToArray();

            // Tracks which diagnostics were matched by an expected pattern, so that FailOnUnexpectedDiagnostics below
            // can report the remainder.
            var isDiagnosticExpected = new bool[diagnostics.Length];

            foreach ( var regex in run.ExpectedDiagnosticsRegexes ?? [] )
            {
                var found = false;

                // The loop does not stop at the first match: every matching line must be marked, otherwise a
                // legitimately repeated diagnostic would later be reported as unexpected.
                for ( var i = 0; i < diagnostics.Length; i++ )
                {
                    if ( Regex.IsMatch( diagnostics[i], regex, RegexOptions.IgnoreCase ) )
                    {
                        isDiagnosticExpected[i] = true;
                        found = true;
                    }
                }

                if ( !found )
                {
                    context.Console.WriteError( $"Expected diagnostic not found for pattern '{regex}'." );

                    success = false;
                }
            }

            // Forbidden patterns express "must not appear" precisely. FailOnUnexpectedDiagnostics can express it too,
            // but only by also firing on incidental unrelated warnings, which makes a regression test brittle.
            foreach ( var regex in run.ForbiddenDiagnosticsRegexes ?? [] )
            {
                foreach ( var diagnostic in diagnostics )
                {
                    if ( Regex.IsMatch( diagnostic, regex, RegexOptions.IgnoreCase ) )
                    {
                        context.Console.WriteError( $"Forbidden diagnostic matching '{regex}' found: {diagnostic}" );

                        success = false;
                    }
                }
            }

            if ( run.FailOnUnexpectedDiagnostics )
            {
                for ( var i = 0; i < diagnostics.Length; i++ )
                {
                    if ( !isDiagnosticExpected[i] )
                    {
                        context.Console.WriteError( $"Unexpected diagnostic: {diagnostics[i]}" );
                        success = false;
                    }
                }
            }

            // On failure, dump the full output and the verdict on each diagnostic (Y = matched an expected pattern).
            // This is what makes a CI failure diagnosable without reproducing the build locally.
            if ( !success )
            {
                context.Console.WriteError( "" );
                context.Console.WriteError( "Output:" );
                context.Console.WriteError( output );
                context.Console.WriteError( "" );
                context.Console.WriteError( "Diagnostics:" );

                for ( var i = 0; i < diagnostics.Length; i++ )
                {
                    context.Console.WriteError( $"{i}/{(isDiagnosticExpected[i] ? "Y" : "N")}: {diagnostics[i]}" );
                }
            }
        }
        else if ( exitCode != 0 )
        {
            context.Console.WriteError( output );

            // The output has just been written as an error; suppress the success dump below so it is not printed twice
            // when IgnoreExitCode turns this failure into a success.
            writeOutputOnSuccess = false;
        }
        else
        {
            foreach ( var regex in run.ErrorRegexes ?? [] )
            {
                if ( Regex.IsMatch( output, regex, RegexOptions.IgnoreCase ) )
                {
                    context.Console.WriteError( $"Output matched for pattern '{regex}'." );
                    context.Console.WriteError( output );

                    success = false;
                }
            }
        }

        // The output was captured rather than streamed, so it has to be replayed explicitly, otherwise a successful
        // run would leave no trace on the console at all.
        if ( success && writeOutputOnSuccess )
        {
            context.Console.WriteMessage( output );
        }

        return success;
    }

    /// <summary>
    /// Makes a matrix entry name usable as a file name. Names come from <c>test.json</c>, i.e. from a scenario author
    /// rather than from this code, so they cannot be assumed to be free of path characters.
    /// </summary>
    private static string SanitizeFileName( string name )
        => string.Join( "_", name.Split( Path.GetInvalidFileNameChars() ) );
}
