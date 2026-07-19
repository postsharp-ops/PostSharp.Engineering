// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using PostSharp.Engineering.BuildTools.Utilities;
using System;
using System.IO;

namespace PostSharp.Engineering.BuildTools.Build.Solutions
{
    /// <summary>
    /// An implementation of <see cref="Solution"/> that uses the <c>dotnet</c> utility to build projects.
    /// </summary>
    public class DotNetSolution : TestableSolution
    {
        public DotNetSolution( string solutionPath ) : base( solutionPath ) { }

        public bool IsSingleFile => Path.GetExtension( this.SolutionPath ).Equals( ".cs", StringComparison.OrdinalIgnoreCase );

        protected override bool ProducesTestResults => true;

        public override bool Pack( BuildContext context, BuildSettings settings )
            => DotNetHelper.Run( context, settings, this.GetFinalSolutionPath( context ), "pack", "", true, this.CreateInvocationOptions() );

        public override bool Restore( BuildContext context, BuildSettings settings )
        {
            if ( this.IsSingleFile )
            {
                context.Console.WriteImportantMessage( "Restore skipped for single-file program." );

                return true;
            }

            return DotNetHelper.Run( context, settings, this.GetFinalSolutionPath( context ), "restore", "--no-cache", false, this.CreateInvocationOptions() );
        }

        protected override bool Invoke(
            BuildContext context,
            BuildSettings settings,
            SolutionCommand command,
            EffectiveTestOptions options,
            string logName,
            bool captureOutput,
            out int exitCode,
            out string output )
        {
            var projectOrSolution = this.GetFinalSolutionPath( context );

            string verb;
            string args;

            if ( this.IsSingleFile )
            {
                verb = "run";
                args = "";
            }
            else if ( command == SolutionCommand.Test )
            {
                var resultsDirectory = Path.Combine( context.RepoDirectory, context.Product.TestResultsDirectory );

                verb = "test";
                args = $"--logger \"trx\" --logger \"console;verbosity=minimal\" --results-directory \"{resultsDirectory}\"";

                if ( !string.IsNullOrEmpty( settings.TestsFilter ) )
                {
                    args += $" --filter \"{settings.TestsFilter}\"";
                }
            }
            else
            {
                verb = "build";
                args = "";
            }

            var invocationOptions = this.CreateInvocationOptions();

            if ( !captureOutput )
            {
                exitCode = 0;
                output = "";

                return DotNetHelper.Run( context, settings, projectOrSolution, verb, args, true, invocationOptions, logName );
            }

            return DotNetHelper.Run( context, settings, projectOrSolution, verb, args, true, out exitCode, out output, invocationOptions, logName );
        }
    }
}
