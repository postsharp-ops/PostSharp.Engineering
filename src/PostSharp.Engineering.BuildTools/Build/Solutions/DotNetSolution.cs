// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using Newtonsoft.Json;
using PostSharp.Engineering.BuildTools.Build.Model;
using PostSharp.Engineering.BuildTools.ContinuousIntegration;
using PostSharp.Engineering.BuildTools.Utilities;
using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace PostSharp.Engineering.BuildTools.Build.Solutions
{
    /// <summary>
    /// An implementation of <see cref="Solution"/> that uses the <c>dotnet</c> utility to build projects.
    /// </summary>
    public class DotNetSolution : Solution
    {
        public DotNetSolution( string solutionPath ) : base( solutionPath ) { }

        public override bool Build( BuildContext context, BuildSettings settings ) => this.RunBuildOrTests( context, settings, test: false );

        public override bool Pack( BuildContext context, BuildSettings settings ) => this.RunDotNet( context, settings, "pack", "", addConfigurationFlag: true );

        public override bool Test( BuildContext context, BuildSettings settings ) => this.RunBuildOrTests( context, settings, test: true );

        public override bool Restore( BuildContext context, BuildSettings settings ) => this.RunDotNet( context, settings, "restore", "--no-cache", addConfigurationFlag: false );

        private string GetFinalSolutionPath( BuildContext context )
            => FileSystemHelper.GetFinalPath( Path.Combine( context.RepoDirectory, this.SolutionPath ) );
        
        private ToolInvocationOptions CreateInvocationOptions()
            => new ToolInvocationOptions( this.EnvironmentVariables );

        private bool RunDotNet(
            BuildContext context,
            BuildSettings settings,
            string command,
            string arguments,
            bool addConfigurationFlag )
            => DotNetHelper.Run(
                context,
                settings,
                this.GetFinalSolutionPath( context ),
                command,
                arguments,
                addConfigurationFlag,
                this.CreateInvocationOptions() );
        
        private bool RunBuildOrTests(
            BuildContext context,
            BuildSettings settings,
            bool test )
        {
            var resultsRelativeDirectory =
                context.Product.TestResultsDirectory.ToString( new BuildInfo( null, settings.BuildConfiguration, context.Product, null ) );

            var resultsDirectory = Path.Combine( context.RepoDirectory, resultsRelativeDirectory );
            var projectOrSolution = this.GetFinalSolutionPath( context );
            var projectOrSolutionDirectory = Path.GetDirectoryName( Path.GetFullPath( projectOrSolution ) );

            if ( projectOrSolutionDirectory == null )
            {
                context.Console.WriteError( $"Unexpected format of project or solution file path '{projectOrSolution}'." );

                return false;
            }

            // Get the test.json file location relative to solution file based on full solution location path.
            var testJsonFile = Path.Combine( projectOrSolutionDirectory, "test.json" );

            string command;
            string args;

            if ( test )
            {
                command = "test";
                args = $"--logger \"trx\" --logger \"console;verbosity=minimal\" --results-directory \"{resultsDirectory}\"";

                if ( !string.IsNullOrEmpty( settings.TestsFilter ) )
                {
                    args += $" --filter \"{settings.TestsFilter}\"";
                }
            }
            else
            {
                command = "build";
                args = "";
            }

            var options = this.CreateInvocationOptions();

            bool success;

            if ( File.Exists( testJsonFile ) )
            {
                var testJsonFileContent = File.ReadAllText( testJsonFile );
                var testOptions = JsonConvert.DeserializeObject<TestOptions>( testJsonFileContent );

                if ( testOptions == null )
                {
                    context.Console.WriteError( $"No test options found in file '{testJsonFile}'." );

                    return false;
                }
                
                if ( test && testOptions.BuildOnly )
                {
                    context.Console.WriteMessage( $"dotnet test skipped for '{projectOrSolution}' as configured in '{testJsonFile}'." );
                    
                    return true;
                }
                
                context.Console.WriteMessage( $"Running {(test ? "test" : "build")} as configured in '{testJsonFile}'." );

                _ = DotNetHelper.Run(
                    context,
                    settings,
                    projectOrSolution,
                    command,
                    args,
                    true,
                    out var exitCode,
                    out var output,
                    options );

                success = exitCode == 0 || testOptions.IgnoreExitCode;
                var writeOutputOnSuccess = true;

                if ( testOptions.ExpectedDiagnosticsRegexes != null || testOptions.FailOnUnexpectedDiagnostics )
                {
                    var diagnostics = output.Split( '\n' ).Select( l => l.Trim() ).Where( l => l.Contains( ": error ", StringComparison.Ordinal ) || l.Contains( ": warning ", StringComparison.Ordinal ) ).ToArray();
                    var isDiagnosticExpected = new bool[diagnostics.Length];
                        
                    foreach ( var regex in testOptions.ExpectedDiagnosticsRegexes ?? [] )
                    {
                        var found = false;

                        for ( var i = 0; i < diagnostics.Length; i++ )
                        {
                            var line = diagnostics[i];
                                
                            if ( Regex.IsMatch( line, regex, RegexOptions.IgnoreCase ) )
                            {
                                isDiagnosticExpected[i] = true;
                                found = true;
                            }
                        }

                        if ( !found )
                        {
                            context.Console.WriteError( $"Expected disagnostic not found for pattern '{regex}'." );

                            success = false;
                        }
                    }

                    if ( testOptions.FailOnUnexpectedDiagnostics )
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
                    writeOutputOnSuccess = false;
                }
                else
                {
                    if ( testOptions.ErrorRegexes != null )
                    {
                        foreach ( var regex in testOptions.ErrorRegexes )
                        {
                            if ( Regex.IsMatch( output, regex, RegexOptions.IgnoreCase ) )
                            {
                                context.Console.WriteError( $"Output matched for pattern '{regex}'." );
                                context.Console.WriteError( output );

                                success = false;
                            }
                        }
                    }
                }
                
                if ( success && writeOutputOnSuccess )
                {
                    context.Console.WriteMessage( output );
                }
            }
            else
            {
                success = DotNetHelper.Run(
                    context,
                    settings,
                    projectOrSolution,
                    command,
                    args,
                    true,
                    options );
            }

            if ( test && TeamCityHelper.IsTeamCityBuild( settings ) )
            {
                // Export test result files to TeamCity.
                TeamCityHelper.SendImportDataMessage(
                    "vstest",
                    Path.Combine( resultsRelativeDirectory, "*.trx" ).Replace( Path.DirectorySeparatorChar, '/' ),
                    Path.GetFileName( projectOrSolution ),
                    false );
            }

            return success;
        }
    }
}