// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using JetBrains.Annotations;
using PostSharp.Engineering.BuildTools.Build;
using PostSharp.Engineering.BuildTools.Build.Model;
using PostSharp.Engineering.BuildTools.Tools.TeamCity;
using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace PostSharp.Engineering.BuildTools.Utilities
{
    [PublicAPI]
    public static class DotNetHelper
    {
        public static bool Run(
            BuildContext context,
            BuildSettings settings,
            string projectOrSolution,
            string command,
            string arguments = "",
            bool addConfigurationFlag = false,
            ToolInvocationOptions? options = null )
        {
            var argsBuilder = CreateCommandLine( context, settings, projectOrSolution, command, arguments, addConfigurationFlag );

            options = AddSimulatedContinuousIntegrationEnvironmentVariables( settings, options );

            return ToolInvocationHelper.InvokeTool(
                context.Console,
                "dotnet",
                argsBuilder,
                context.GetWorkingDirectory( projectOrSolution ),
                options );
        }

        public static bool Run(
            BuildContext context,
            BuildSettings settings,
            string projectOrSolution,
            string command,
            string arguments,
            bool addConfigurationFlag,
            out int exitCode,
            out string output,
            ToolInvocationOptions? options = null )
        {
            var argsBuilder = CreateCommandLine( context, settings, projectOrSolution, command, arguments, addConfigurationFlag );

            options = AddSimulatedContinuousIntegrationEnvironmentVariables( settings, options );

            return ToolInvocationHelper.InvokeTool(
                context.Console,
                "dotnet",
                argsBuilder,
                context.GetWorkingDirectory( projectOrSolution ),
                out exitCode,
                out output,
                options );
        }

        private static ToolInvocationOptions? AddSimulatedContinuousIntegrationEnvironmentVariables( BuildSettings settings, ToolInvocationOptions? options )
        {
            var environmentVariables = TeamCityHelper.GetSimulatedContinuousIntegrationEnvironmentVariables( settings );

            if ( environmentVariables.IsEmpty )
            {
                return options;
            }
            else if ( options == null )
            {
                return new ToolInvocationOptions( environmentVariables );
            }
            else
            {
                return options.WithEnvironmentVariables( environmentVariables );
            }
        }

        private static string CreateCommandLine(
            BuildContext context,
            BuildSettings settings,
            string projectOrSolution,
            string command,
            string arguments,
            bool addConfigurationFlag )
        {
            var argsBuilder = new StringBuilder();

            var isRunCommand = command == "run";
            var isTestDllCommand = command == "test" && Path.GetExtension( projectOrSolution ) == ".dll";

            var projectPrefix = string.Empty;
            var nologo = " --nologo";

            if ( isRunCommand )
            {
                if ( !Path.GetExtension( projectOrSolution ).Equals( ".cs", StringComparison.OrdinalIgnoreCase ) )
                {
                    // The command `dotnet run SomeProject.csproj` does not work, it requires explicit argument name.
                    projectPrefix = "--project ";
                }

                // dotnet run does not support --nologo.
                nologo = string.Empty;
            }

            argsBuilder.Append(
                CultureInfo.InvariantCulture,
                $"{command} {projectPrefix}\"{projectOrSolution}\" -v:{settings.Verbosity.ToAlias()}{nologo}" );

            if ( addConfigurationFlag )
            {
                argsBuilder.Append(
                    CultureInfo.InvariantCulture,
                    $" -c {context.Product.DependencyDefinition.MSBuildConfiguration[settings.BuildConfiguration]}" );
            }

            if ( settings.NoConcurrency && !isTestDllCommand )
            {
                argsBuilder.Append( " -m:1" );
            }

            foreach ( var property in settings.Properties )
            {
                argsBuilder.Append( CultureInfo.InvariantCulture, $" -p:{property.Key}={property.Value}" );
            }

            if ( context.IsContinuousIntegrationBuild && !isTestDllCommand )
            {
                argsBuilder.Append( " -p:ContinuousIntegrationBuild=True" );
            }

            if ( settings.DisableBuildServers )
            {
                argsBuilder.Append( " --disable-build-servers" );
            }

            if ( settings.NoSign )
            {
                argsBuilder.Append( " -p:DoNotSign=True" );
            }

            if ( !isRunCommand && !isTestDllCommand )
            {
                var binaryLogFilePath = Path.Combine(
                    context.RepoDirectory,
                    context.Product.LogsDirectory,
                    $"{Path.GetFileName( projectOrSolution )}.{command}.binlog" );

                argsBuilder.Append( CultureInfo.InvariantCulture, $" -bl:\"{binaryLogFilePath}\"" );
            }

            if ( !string.IsNullOrWhiteSpace( arguments ) )
            {
                argsBuilder.Append( " " + arguments.Trim() );
            }

            return argsBuilder.ToString();
        }
    }
}