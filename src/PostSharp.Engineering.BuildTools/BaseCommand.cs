// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using PostSharp.Engineering.BuildTools.Build;
using PostSharp.Engineering.BuildTools.Build.Model;
using PostSharp.Engineering.BuildTools.Utilities;
using Spectre.Console;
using Spectre.Console.Cli;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace PostSharp.Engineering.BuildTools
{
    /// <summary>
    /// The base class for all commands that require a <see cref="Product"/>.
    /// </summary>
    public abstract class BaseCommand<T> : Command<T>
        where T : CommonCommandSettings
    {
        public sealed override int Execute( CommandContext context, T settings )
        {
            try
            {
                var stopwatch = Stopwatch.StartNew();

                if ( settings.Debug )
                {
                    Debugger.Launch();
                }

                if ( !BuildContext.TryCreate( context, settings, out var buildContext ) )
                {
                    return 1;
                }

                if ( settings.UseProjectDirectoryAsWorkingDirectory )
                {
                    buildContext = buildContext.WithUseProjectDirectoryAsWorkingDirectory( true );
                }

                MSBuildHelper.InitializeLocator();

                if ( DockerHelper.IsDockerBuild() )
                {
                    buildContext.Console.WriteMessage( "Docker detected." );
                }

                // Validate custom properties.
                if ( settings.ListProperties )
                {
                    if ( buildContext.Product.SupportedProperties.Count > 0 )
                    {
                        buildContext.Console.WriteImportantMessage( "The following properties are supported by this product:" );

                        foreach ( var property in buildContext.Product.SupportedProperties )
                        {
                            buildContext.Console.WriteImportantMessage( $"\t{property.Key}: {property.Value}" );
                        }
                    }
                    else
                    {
                        buildContext.Console.WriteImportantMessage( "The current product does not support any property." );
                    }

                    return 1;
                }

                var unsupportedProperties =
                    settings.Properties.Keys
                        .Where( name => !buildContext.Product.SupportedProperties.ContainsKey( name ) )
                        .ToList();

                if ( unsupportedProperties.Count > 0 )
                {
                    buildContext.Console.WriteError(
                        $"The following properties are not supported: {string.Join( ", ", unsupportedProperties )}. Use --list-properties to list the supported properties." );

                    return 1;
                }

                var myVersion = VersionHelper.EngineeringVersion;

                // Display the logo.
                if ( !settings.NoLogo && buildContext.Console.Out != null )
                {
                    buildContext.Console.Out.Write(
                        new FigletText( buildContext.Product.ProductName )
                            .LeftJustified()
                            .Color( Color.Purple ) );

                    buildContext.Console.Out.WriteLine();
                    buildContext.Console.WriteMessage( $"Using PostSharp.Engineering v{myVersion}. TeamCity: {buildContext.IsContinuousIntegrationBuild}. Docker: {buildContext.IsRunningUnderContainer}." );
                    buildContext.Console.Out.WriteLine();
                }

                // Validate the sdk version in global.sdk.
                if ( buildContext.Product.RequiresEngineeringSdk )
                {
                    var globalJsonPath = Path.Combine( buildContext.RepoDirectory, "global.json" );

                    if ( !File.Exists( globalJsonPath ) )
                    {
                        buildContext.Console.WriteWarning( "global.json does not exist." );
                        buildContext.Console.WriteLine();
                    }

                    var globalJson = JsonDocument.Parse( File.ReadAllText( globalJsonPath ) );

                    if ( !globalJson.RootElement.TryGetProperty( "msbuild-sdks", out var sdks ) ||
                         !sdks.TryGetProperty( "PostSharp.Engineering.Sdk", out var sdk ) ||
                         sdk.GetString() == null )
                    {
                        buildContext.Console.WriteWarning( "global.json does not import the PostSharp.Engineering.Sdk." );
                        buildContext.Console.WriteLine();
                    }
                    else
                    {
                        if ( sdk.GetString() != myVersion )
                        {
                            buildContext.Console.WriteWarning(
                                $"global.json imports PostSharp.Engineering.Sdk version {sdk.GetString()}, but the BuildTools version is {myVersion}." );

                            buildContext.Console.WriteLine();
                        }
                    }
                }

                // Initialize the settings with the build context.
                settings.Initialize( buildContext );

                // Execute the command itself.
                var success = this.ExecuteCore( buildContext, settings );

                if ( !settings.NoLogo )
                {
                    buildContext.Console.WriteMessage( $"Finished at {DateTime.Now} after {stopwatch.Elapsed}." );
                }

                return success ? 0 : 1;
            }
            catch ( Exception ex )
            {
                AnsiConsole.WriteException( ex );

                return 10;
            }
        }

        protected abstract bool ExecuteCore( BuildContext context, T settings );
    }
}