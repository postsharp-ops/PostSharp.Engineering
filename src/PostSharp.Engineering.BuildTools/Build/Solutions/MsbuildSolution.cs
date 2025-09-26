// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using JetBrains.Annotations;
using PostSharp.Engineering.BuildTools.Build.Model;
using PostSharp.Engineering.BuildTools.Build.MSBuild;
using PostSharp.Engineering.BuildTools.Tools.TeamCity;
using PostSharp.Engineering.BuildTools.Utilities;
using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace PostSharp.Engineering.BuildTools.Build.Solutions
{
    /// <summary>
    /// An implementation of <see cref="Solution"/> that uses the <c>msbuild.exe</c> utility (shipped with Visual Studio) to build projects.
    /// </summary>
    [PublicAPI]
    public class MsbuildSolution : Solution
    {
        public MsbuildSolution( string solutionPath ) : base( solutionPath ) { }

        /// <summary>
        /// Gets or sets the MSBuild version used for this solution. Overrides the <see cref="Product.MSBuildVersion"/> property of the <see cref="Product"/> class.
        /// </summary>
        public Version? MSBuildVersion { get; init; }

        public override bool Build( BuildContext context, BuildSettings settings )
            => this.RunMSBuild( context, settings, this.SolutionPath, "Build", "-p:RestorePackages=false" );

        public override bool Pack( BuildContext context, BuildSettings settings )
            => this.RunMSBuild( context, settings, this.SolutionPath, "Pack", "-p:RestorePackages=false" );

        public override bool Test( BuildContext context, BuildSettings settings )
            => this.RunMSBuild( context, settings, this.SolutionPath, "Test", "-p:RestorePackages=false" );

        public override bool Restore( BuildContext context, BuildSettings settings )
        {
            if ( !this.RunMSBuild( context, settings, this.SolutionPath, "Restore" ) )
            {
                return false;
            }

            // "msbuild -t Restore" doesn't call the Restore target for projects referencing NuGet packages using packages.config in a solution.
            // We call the Restore target ourselves on each such project in the solution.
            if ( Path.GetExtension( this.SolutionPath ) == ".sln" )
            {
                var exe = "dotnet";
                var args = $"sln \"{this.SolutionPath}\" list";

                if ( !ToolInvocationHelper.InvokeTool( context.Console, exe, args, context.RepoDirectory, out _, out var slnListOutput ) )
                {
                    context.Console.WriteError( $"Error executing {exe} {args}" );
                    context.Console.WriteError( slnListOutput );

                    return false;
                }

                // The "dotnet sln list" command output contains a header, so we need to filter the rows.
                var projectList = slnListOutput
                    .Split( '\r', '\n' )
                    .Where( l => l.EndsWith( "proj", StringComparison.OrdinalIgnoreCase ) )
                    .Where( p => File.Exists( Path.Combine( Path.GetDirectoryName( p )!, "packages.config" ) ) )
                    .ToArray();

                foreach ( var project in projectList )
                {
                    context.Console.WriteMessage( $"Restoring packages.config of '{project}' project" );

                    if ( !this.RunMSBuild( context, settings, project, "Restore" ) )
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        private bool RunMSBuild( BuildContext context, BuildSettings settings, string project, string target, string arguments = "" )
        {
            if ( !string.IsNullOrEmpty( settings.TestsFilter ) )
            {
                // TODO if needed
                context.Console.WriteError( "Test filters are not implemented for non-SDK-style projects." );

                return false;
            }

            var msbuildPath = MSBuildHelper.FindMSBuildExe( context, this.MSBuildVersion );

            if ( msbuildPath == null )
            {
                context.Console.WriteError( "Could not find msbuild.exe." );

                return false;
            }

            var argsBuilder = new StringBuilder();
            var path = Path.Combine( context.RepoDirectory, project );

            var binaryLogFilePath = Path.Combine(
                context.RepoDirectory,
                context.Product.LogsDirectory,
                $"{this.Name}.{target}.binlog" );

            argsBuilder.Append(
                CultureInfo.InvariantCulture,
                $"-t:{target} -p:Configuration={context.Product.DependencyDefinition.MSBuildConfiguration[settings.BuildConfiguration]} \"{path}\" -v:{settings.Verbosity.ToAlias()} -NoLogo" );

            argsBuilder.Append( settings.NoConcurrency ? " -m:1" : " -m" );

            argsBuilder.Append( CultureInfo.InvariantCulture, $" -bl:\"{binaryLogFilePath}\"" );

            foreach ( var property in settings.Properties )
            {
                argsBuilder.Append( CultureInfo.InvariantCulture, $" -p:{property.Key}={property.Value}" );
            }

            if ( context.IsContinuousIntegrationBuild )
            {
                argsBuilder.Append( " -p:ContinuousIntegrationBuild=True" );
            }

            if ( !string.IsNullOrWhiteSpace( arguments ) )
            {
                argsBuilder.Append( " " + arguments.Trim() );
            }

            var toolInvocationOptions = new ToolInvocationOptions( TeamCityHelper.GetSimulatedContinuousIntegrationEnvironmentVariables( settings ) );

            return ToolInvocationHelper.InvokeTool(
                context.Console,
                msbuildPath,
                argsBuilder.ToString(),
                Environment.CurrentDirectory,
                toolInvocationOptions );
        }
    }
}