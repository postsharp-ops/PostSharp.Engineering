// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using JetBrains.Annotations;
using Spectre.Console;
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

// ReSharper disable AccessToDisposedClosure

namespace PostSharp.Engineering.BuildTools.Utilities
{
    [PublicAPI]
    public static class ToolInvocationHelper
    {
        public static bool InvokePowershell(
            ConsoleHelper console,
            string fileName,
            string commandLine,
            string? workingDirectory = null,
            ToolInvocationOptions? options = null )
            => InvokeTool(
                console,
                "powershell",
                $"-NonInteractive -File {fileName} {commandLine}",
                workingDirectory,
                options );

        public static bool InvokeTool(
            ConsoleHelper console,
            string fileName,
            string commandLine,
            string? workingDirectory = null,
            ToolInvocationOptions? options = null )
        {
            if ( !InvokeTool(
                    console,
                    fileName,
                    commandLine,
                    workingDirectory,
                    out var exitCode,
                    options ) )
            {
                return false;
            }
            else if ( exitCode != 0 )
            {
                console.WriteError( $"The process `\"{fileName}\" {commandLine}` failed with exit code {exitCode}." );

                return false;
            }
            else
            {
                return true;
            }
        }

        public static bool InvokeTool(
            ConsoleHelper console,
            string fileName,
            string commandLine,
            string? workingDirectory,
            out int exitCode,
            ToolInvocationOptions? options = null )
        {
            options ??= ToolInvocationOptions.Default;

            if ( options.FilterOutput && options.OutputReadingTimeout < ToolInvocationOptions.LongOutputReadingTimeout )
            {
                options = options with { OutputReadingTimeout = ToolInvocationOptions.LongOutputReadingTimeout };
            }

            return InvokeTool(
                console,
                fileName,
                commandLine,
                workingDirectory,
                out exitCode,
                HandleErrorData,
                HandleOutputData,
                options,
                ConsoleHelper.CancellationToken );

            void HandleErrorData( string s )
            {
                if ( options.FilterOutput )
                {
                    if ( !string.IsNullOrWhiteSpace( s ) )
                    {
                        console.WriteMessage( s );
                    }
                }
                else
                {
                    Console.Error.WriteLine( s );
                }
            }

            void HandleOutputData( string s )
            {
                if ( options.FilterOutput )
                {
                    if ( !string.IsNullOrWhiteSpace( s ) )
                    {
                        foreach ( var replace in options.ReplacePatterns )
                        {
                            if ( replace.Regex.IsMatch( s ) )
                            {
                                s = replace.Regex.Replace( s, replace.GetReplacement );

                                break;
                            }
                        }

                        if ( options.SilentPatterns.Any( p => p.IsMatch( s ) ) )
                        {
                            // Ignored.
                        }
                        else if ( options.ErrorPatterns.Any( p => p.IsMatch( s ) ) )
                        {
                            console.WriteError( s );
                        }
                        else if ( options.WarningPatterns.Any( p => p.IsMatch( s ) ) )
                        {
                            console.WriteWarning( s );
                        }
                        else if ( options.SuccessPatterns.Any( p => p.IsMatch( s ) ) )
                        {
                            console.WriteSuccess( s );
                        }
                        else if ( options.ImportantMessagePatterns.Any( p => p.IsMatch( s ) ) )
                        {
                            console.WriteImportantMessage( s );
                        }
                        else
                        {
                            console.WriteMessage( s );
                        }
                    }
                }
                else
                {
                    Console.Out.WriteLine( s );
                }
            }
        }

        public static bool InvokeTool(
            ConsoleHelper console,
            string fileName,
            string commandLine,
            string? workingDirectory,
            out int exitCode,
            out string output,
            ToolInvocationOptions? options = null )
        {
            StringBuilder outputBuilder = new();

            options ??= ToolInvocationOptions.Default;

            if ( options.OutputReadingTimeout < ToolInvocationOptions.LongOutputReadingTimeout )
            {
                options = options with { OutputReadingTimeout = ToolInvocationOptions.LongOutputReadingTimeout };
            }

            var success =
                InvokeTool(
                    console,
                    fileName,
                    commandLine,
                    workingDirectory,
                    out exitCode,
                    s =>
                    {
                        lock ( outputBuilder )
                        {
                            outputBuilder.Append( s );
                            outputBuilder.Append( '\n' );
                        }
                    },
                    s =>
                    {
                        lock ( outputBuilder )
                        {
                            outputBuilder.Append( s );
                            outputBuilder.Append( '\n' );
                        }
                    },
                    options,
                    ConsoleHelper.CancellationToken );

            output = outputBuilder.ToString();

            return success && exitCode == 0;
        }

        public static bool InvokeTool(
            ConsoleHelper console,
            string fileName,
            string commandLine,
            string? workingDirectory,
            out int exitCode,
            Action<string> handleErrorData,
            Action<string> handleOutputData,
            ToolInvocationOptions? options,
            CancellationToken cancellationToken )
        {
            exitCode = 0;
            options ??= ToolInvocationOptions.Default;
            var processShouldRetry = false;
            var retryAttempts = 3;

            for ( var attempt = 0; attempt < retryAttempts; attempt++ )
            {
                cancellationToken.ThrowIfCancellationRequested();

#pragma warning disable CA1307 // There is no string.Contains that takes a StringComparison
                if ( fileName.Contains( new string( Path.DirectorySeparatorChar, 1 ) ) && !File.Exists( fileName ) )
                {
                    console.WriteError( "Cannot execute \"{0}\": file not found.", fileName );

                    return false;
                }
#pragma warning restore CA1307

                const int restartLimit = 3;
                var restartCount = 0;
            start:

                ProcessStartInfo startInfo =
                    new()
                    {
                        FileName = fileName,
                        Arguments = Environment.ExpandEnvironmentVariables( commandLine ),
                        WorkingDirectory = workingDirectory ?? Environment.CurrentDirectory,
                        ErrorDialog = false,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardError = true,
                        RedirectStandardOutput = true
                    };

                if ( options.EnvironmentVariables != null )
                {
                    foreach ( var pair in options.EnvironmentVariables )
                    {
                        if ( pair.Value == null )
                        {
                            startInfo.Environment.Remove( pair.Key );
                        }
                        else
                        {
                            startInfo.Environment[pair.Key] = pair.Value;
                        }
                    }
                }

                // Some environment variables must not be passed from the current process to the child process.
                foreach ( var blockedEnvironmentVariable in options.BlockedEnvironmentVariables )
                {
                    startInfo.Environment.Remove( blockedEnvironmentVariable );
                }

                Process process = new() { StartInfo = startInfo };

                using ( ManualResetEvent stdErrorClosed = new( false ) )
                using ( ManualResetEvent stdOutClosed = new( false ) )
                {
                    // Filters process output where matching RegEx value indicates process failure.
                    void FilterProcessOutput( string output )
                    {
                        if ( options.Retry is { Regex: { } retryRegex } && retryRegex.IsMatch( output ) )
                        {
                            processShouldRetry = true;
                        }
                    }

                    process.ErrorDataReceived += ( _, args ) =>
                    {
                        try
                        {
                            if ( args.Data == null )
                            {
                                stdErrorClosed.Set();
                            }
                            else
                            {
                                FilterProcessOutput( args.Data );

                                handleErrorData( args.Data );
                            }
                        }
                        catch ( Exception e )
                        {
                            if ( console.Error == null )
                            {
                                Console.Error.WriteLine( e.ToString() );
                            }
                            else
                            {
                                console.Error.WriteException( e );
                            }
                        }
                    };

                    process.OutputDataReceived += ( _, args ) =>
                    {
                        try
                        {
                            if ( args.Data == null )
                            {
                                stdOutClosed.Set();
                            }
                            else
                            {
                                FilterProcessOutput( args.Data );

                                handleOutputData( args.Data );
                            }
                        }
                        catch ( Exception e )
                        {
                            if ( console.Error == null )
                            {
                                Console.Error.WriteLine( e.ToString() );
                            }
                            else
                            {
                                console.Error.WriteException( e );
                            }
                        }
                    };

                    // Log the command line, but not the one with expanded environment variables, so we don't expose secrets.
                    if ( !options.Silent )
                    {
                        console.WriteImportantMessage( "{0} {1}", process.StartInfo.FileName, commandLine );
                    }

                    using ( process )
                    {
                        try
                        {
                            process.Start();
                        }
                        catch ( Win32Exception e ) when ( (uint) e.NativeErrorCode == 0x80004005 )
                        {
                            if ( restartCount < restartLimit )
                            {
                                console.WriteWarning(
                                    "Access denied when starting a process. This might be caused by an anti virus software. Waiting 1000 ms and restarting." );

                                Thread.Sleep( 1000 );
                                restartCount++;

                                goto start;
                            }

                            throw;
                        }

                        if ( !cancellationToken.CanBeCanceled )
                        {
                            process.BeginErrorReadLine();
                            process.BeginOutputReadLine();

                            process.WaitForExit();
                        }
                        else
                        {
                            using ( ManualResetEvent cancelledEvent = new( false ) )
                            using ( ManualResetEvent exitedEvent = new( false ) )
                            {
                                process.EnableRaisingEvents = true;

                                process.Exited += ( _, _ ) =>
                                {
                                    try
                                    {
                                        exitedEvent.Set();
                                    }
                                    catch ( ObjectDisposedException )
                                    {
                                        // This happens on Ubuntu.
                                    }
                                };

                                using ( cancellationToken.Register( () => cancelledEvent.Set() ) )
                                {
                                    process.BeginErrorReadLine();
                                    process.BeginOutputReadLine();

                                    if ( !process.HasExited )
                                    {
                                        var signal = WaitHandle.WaitAny( [exitedEvent, cancelledEvent] );

                                        if ( signal == 1 )
                                        {
                                            cancellationToken.ThrowIfCancellationRequested();
                                        }
                                    }
                                }
                            }
                        }

                        // We will wait for a while for all output to be processed.
                        var outputTimeout = options.OutputReadingTimeout;

                        if ( !cancellationToken.CanBeCanceled )
                        {
                            WaitHandle.WaitAll( [stdErrorClosed, stdOutClosed], outputTimeout );
                        }
                        else
                        {
                            var spinTime = TimeSpan.FromMilliseconds( 100 );

                            var i = 0;
                            var n = outputTimeout / spinTime;

                            while ( !WaitHandle.WaitAll( [stdErrorClosed, stdOutClosed], spinTime ) &&
                                    i++ < n )
                            {
                                cancellationToken.ThrowIfCancellationRequested();
                            }
                        }

                        if ( !stdErrorClosed.WaitOne( TimeSpan.Zero ) || !stdOutClosed.WaitOne( TimeSpan.Zero ) )
                        {
                            console.WriteError( $"Output processing didn't finish within {outputTimeout.TotalSeconds} seconds." );
                        }

                        exitCode = process.ExitCode;

                        // We will retry if the process output indicates we should retry and exit code indicates failure.
                        if ( processShouldRetry && options.Retry != null && exitCode == options.Retry.ExitCode )
                        {
                            console.WriteWarning( "Build failed. Retrying." );

                            continue;
                        }

                        cancellationToken.ThrowIfCancellationRequested();

                        return true;
                    }
                }
            }

            return false;
        }

        public static bool TryFindInPath( string file, [NotNullWhen( true )] out string? path )
        {
            foreach ( var directory in Environment.GetEnvironmentVariable( "PATH" )!.Split( ";" ) )
            {
                path = Path.Combine( directory, file );

                if ( File.Exists( path ) )
                {
                    return true;
                }
            }

            path = null;

            return false;
        }
    }
}