// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using PostSharp.Engineering.BuildTools.Build;
using PostSharp.Engineering.BuildTools.Build.Model;
using PostSharp.Engineering.BuildTools.Build.MSBuild;
using PostSharp.Engineering.BuildTools.Utilities;
using Spectre.Console;
using Spectre.Console.Cli;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;

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
            // We use two CancellationTokenSources: one for timeout, the second for manual Ctrl+C, so that we can react differently
            // according to the cancellation reason.
            CancellationTokenSource? timeoutCancellation = null;

            var mainCancellation = new CancellationTokenSource();

            try
            {
                var stopwatch = Stopwatch.StartNew();

                if ( settings.Debug )
                {
                    Debugger.Launch();
                }

                if ( !BuildContext.TryCreate( context, settings, mainCancellation.Token, out var buildContext ) )
                {
                    return 1;
                }

                if ( settings.UseProjectDirectoryAsWorkingDirectory )
                {
                    buildContext = buildContext.WithUseProjectDirectoryAsWorkingDirectory( true );
                }

                Console.CancelKeyPress += ( _, _ ) => OnCancel( buildContext, mainCancellation );

                // Sets up a timeout. The Timer class does not support long periods, so we use CancellationTokenSource.
                if ( settings.Timeout != null )
                {
                    timeoutCancellation = new CancellationTokenSource( TimeSpan.FromMinutes( settings.Timeout.Value ) );
                    timeoutCancellation.Token.Register( () => OnTimeout( buildContext, stopwatch, mainCancellation ) );
                }

                MSBuildHelper.InitializeLocator();

                if ( buildContext.IsRunningUnderContainer )
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

                    buildContext.Console.WriteMessage(
                        $"Using PostSharp.Engineering v{myVersion}. TeamCity: {buildContext.IsContinuousIntegrationBuild}. Docker: {buildContext.IsRunningUnderContainer}. Runtime: {RuntimeInformation.FrameworkDescription} {RuntimeInformation.RuntimeIdentifier}." );

                    buildContext.Console.Out.WriteLine();
                }

                // Validate the sdk version in global.sdk.
                if ( buildContext.Product.RequiresEngineeringSdk )
                {
                    var globalJsonPath = Path.Combine( buildContext.RepoDirectory, "global.json" );

                    if ( File.Exists( globalJsonPath ) )
                    {
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
                    else
                    {
                        // global.json might be generated by PostSharp.Engineering. 
                    }
                }

                // Initialize the settings with the build context.
                settings.Initialize( buildContext );

                // Execute the command itself.
                var success = this.ExecuteCore( buildContext, settings );

                if ( buildContext.CancellationToken.IsCancellationRequested )
                {
                    buildContext.Console.WriteError( "The build was cancelled." );

                    return (int) ExitCode.Cancelled;
                }

                if ( !settings.NoLogo )
                {
                    buildContext.Console.WriteMessage( $"Finished at {DateTime.Now} after {stopwatch.Elapsed}." );
                }

                return (int) (success ? buildContext.ExitCode : ExitCode.Error);
            }
            catch ( Exception ex )
            {
                AnsiConsole.WriteException( ex );

                return (int) ExitCode.Exception;
            }
            finally
            {
                timeoutCancellation?.Dispose();
            }
        }

        protected abstract bool ExecuteCore( BuildContext context, T settings );

        private static void OnCancel( BuildContext buildContext, CancellationTokenSource mainCancellation )
        {
            var console = buildContext.Console;

            if ( RuntimeInformation.IsOSPlatform( OSPlatform.Windows ) )
            {
                console.WriteError( $"Cancelling and killing all child processes." );

                // List all child processes.
                var processes = ProcessHelper.GetProcessTree( console, Process.GetCurrentProcess().Id );

                console.WriteMessage( "Process tree:" );

                foreach ( var node in processes )
                {
                    var indent = new string( '-', (node.NestingLevel + 1) * 3 );
                    console.WriteMessage( $"+{indent} {node.Process.Id} {ProcessHelper.GetCommandLine( node.Process )}" );
                }

                // Kill all processes (except the current one) in reverse order.
                ProcessHelper.KillProcesses( console, processes.Reverse().Select( x => x.Process ) );
            }

            // Signal the main cancellation source.
            // We don't exit the process so exception handlers and finally blocks can run.
            mainCancellation.Cancel();
        }

        private static void OnTimeout( BuildContext buildContext, Stopwatch stopwatch, CancellationTokenSource mainCancellation )
        {
            var console = buildContext.Console;

            if ( RuntimeInformation.IsOSPlatform( OSPlatform.Windows ) )
            {
                console.WriteError( $"The process timed out after {stopwatch.Elapsed}. Dumping and killing the process tree." );
                var directory = Path.Combine( buildContext.RepoDirectory, buildContext.Product.DumpDirectory );

                // List all child processes.
                var processes = ProcessHelper.GetProcessTree( console, Process.GetCurrentProcess().Id );

                console.WriteMessage( "Process tree:" );

                foreach ( var node in processes )
                {
                    var indent = new string( '-', (node.NestingLevel + 1) * 3 );
                    console.WriteMessage( $"+{indent} {node.Process.Id} {ProcessHelper.GetCommandLine( node.Process )}" );
                }

                // Dump these processes.
                ProcessHelper.DumpProcesses( console, processes.Select( p => p.Process ), directory );

                // Signal the main cancellation source.
                mainCancellation.Cancel();

                // Kill all processes (except the current one) in reverse order.
                ProcessHelper.KillProcesses( console, processes.Reverse().Select( x => x.Process ) );
            }
            else
            {
                console.WriteError( $"The process timed out after {stopwatch.Elapsed}. Exiting." );
                mainCancellation.Cancel();
            }

            // Give the normal cancellation workflow a chance to complete.
            Thread.Sleep( 10 );

            // If we're still here, we're abruptly terminating the process.
            console.WriteWarning( "Terminating the current process." );
            Environment.FailFast( $"The process timed out after {stopwatch.Elapsed}." );
        }
    }
}