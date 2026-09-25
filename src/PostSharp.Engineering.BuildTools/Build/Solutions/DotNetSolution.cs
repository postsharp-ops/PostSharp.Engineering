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

        /// <summary>
        /// Gets a value indicating whether the scenario is a <c>*.proj</c> file, i.e. a hand-written MSBuild project
        /// as opposed to a solution or an SDK project.
        /// </summary>
        private bool IsMSBuildProjectFile => Path.GetExtension( this.SolutionPath ).Equals( ".proj", StringComparison.OrdinalIgnoreCase );

        /// <summary>
        /// Gets the MSBuild target used to build a <c>*.proj</c> scenario. It can be overridden per scenario, and per
        /// matrix entry, by the <see cref="TestOptions.Target"/> property of <c>test.json</c>.
        /// </summary>
        public string DefaultTarget { get; init; } = "Build";

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

                // Without `-t:`, MSBuild runs the default targets of the project. A `*.proj` scenario declares no
                // `DefaultTargets` attribute, so the default is the first target in evaluation order, and an
                // `<Import>` is expanded at its position: a scenario that imports `Directory.Build.props` before it
                // declares its own `Build` target therefore builds whatever target the import chain contributed
                // first. In Metalama that is `VerifyProductDependencies`, so four scenarios compiled nothing and
                // reported success. Naming the target removes that dependency on evaluation order.
                //
                // Solutions and SDK projects keep the default, whose first target is `Build` in either case, so
                // that this does not change how the great majority of the scenarios are built.
                var target = options.Target ?? (this.IsMSBuildProjectFile ? this.DefaultTarget : null);

                args = target == null ? "" : $"-t:{target}";
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
