// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;

namespace PostSharp.Engineering.BuildTools.Utilities;

#pragma warning disable CA1416 // Only for Windows.

internal static class ProcessHelper
{
    public static string? GetCommandLine( Process process )
    {
        if ( !RuntimeInformation.IsOSPlatform( OSPlatform.Windows ) )
        {
            return null;
        }

        try
        {
            using ManagementObjectSearcher searcher =
                new( $"SELECT CommandLine FROM Win32_Process WHERE ProcessId = {process.Id}" );

            using ( var objects = searcher.Get() )
            {
                return objects.Cast<ManagementBaseObject>().SingleOrDefault()?["CommandLine"]?.ToString();
            }
        }
        catch
        {
            return null;
        }
    }

    public static bool ReferencesAnyModule( ConsoleHelper console, Process process, string[] substrings )
    {
        try
        {
            foreach ( ProcessModule module in process.Modules )
            {
                if ( module.FileName != null! )
                {
                    if ( substrings.Any( s => Path.GetFileNameWithoutExtension( module.FileName ).Contains( s, StringComparison.OrdinalIgnoreCase ) ) )
                    {
                        return true;
                    }
                }
            }

            return true;
        }
        catch ( Exception e )
        {
            if ( !process.HasExited )
            {
                console.WriteWarning( $"Cannot enumerate the modules of '{process.Id}': {e.Message}." );
            }

            return false;
        }
    }

    /// <summary>
    /// Gets a collection including the parent process and all descendants.
    /// </summary>
    private static IReadOnlyCollection<Process> GetProcessTree(
        ConsoleHelper console,
        int parentId,
        Action<(Process Process, int NestingLevel)> onProcessDiscovered )
    {
        if ( !RuntimeInformation.IsOSPlatform( OSPlatform.Windows ) )
        {
            throw new PlatformNotSupportedException();
        }

        var childProcesses = new Dictionary<int, Process>();
        PopulateProcessTree( console, parentId, childProcesses, 0, onProcessDiscovered );

        return childProcesses.Values;
    }

    private static void PopulateProcessTree(
        ConsoleHelper console,
        int parentId,
        Dictionary<int, Process> processes,
        int nestingLevel,
        Action<(Process Process, int NestingLevel)> onProcessDiscovered )
    {
        try
        {
            if ( processes.ContainsKey( parentId ) )
            {
                return;
            }

            Process parentProcess;

            try
            {
                parentProcess = Process.GetProcessById( parentId );
            }
            catch ( ArgumentException )
            {
                return;
            }

            processes.Add( parentId, parentProcess );
            onProcessDiscovered( (parentProcess, nestingLevel) );

            using var searcher = new ManagementObjectSearcher( $"SELECT ProcessId FROM Win32_Process WHERE ParentProcessId = {parentId}" );

            using var results = searcher.Get();

            foreach ( var o in results )
            {
                if ( o == null )
                {
                    continue;
                }

                var result = (ManagementObject) o;

                try
                {
                    var childPid = Convert.ToInt32( result["ProcessId"], CultureInfo.InvariantCulture );

                    if ( processes.ContainsKey( childPid ) )
                    {
                        // There might be a cycle. Skip it.
                        continue;
                    }

                    // Recursively get grandchildren

                    PopulateProcessTree( console, childPid, processes, nestingLevel + 1, onProcessDiscovered );
                }
                catch ( ArgumentException )
                {
                    // Process may have exited
                }
                catch ( Exception ex )
                {
                    console.WriteMessage( $"Error getting child process: {ex.Message}" );
                }
            }
        }
        catch ( Exception ex )
        {
            console.WriteMessage( $"Error querying child processes: {ex.Message}" );
        }
    }

    public static void DumpAndKillProcessTree( ConsoleHelper console, Process process, string? minidumpDirectory )
    {
        if ( !RuntimeInformation.IsOSPlatform( OSPlatform.Windows ) )
        {
            console.WriteWarning( "Dumping the process tree and killing child processes is not supported on this platform." );

            return;
        }

        console.WriteMessage( "Process tree:" );

        void PrintProcessTreeNode( (Process Process, int NestingLevel) node )
        {
            var indent = new string( '-', (node.NestingLevel + 1) * 3 );
            console.WriteMessage( $"+{indent} {node.Process.Id} {GetCommandLine( node.Process )}" );
        }

        var allProcesses = GetProcessTree( console, process.Id, PrintProcessTreeNode );

        // Create minidumps for all processes
        if ( minidumpDirectory != null )
        {
            foreach ( var p in allProcesses )
            {
                if ( !p.HasExited )
                {
                    TryCaptureMinidump( console, p, minidumpDirectory );
                }
            }
        }

        // Kill all child processes first (bottom-up)
        foreach ( var p in allProcesses.Reverse() )
        {
            try
            {
                if ( !p.HasExited )
                {
                    console.WriteMessage( $"Killing process {p.Id}." );
                    p.Kill( true );
                }
            }
            catch ( Exception ex )
            {
                console.WriteMessage( $"Failed to kill child process PID {p.Id}: {ex.Message}" );
            }
        }
    }

#pragma warning disable CA1416

    private static bool TryCaptureMinidump( ConsoleHelper console, Process process, string directory )
    {
        try
        {
            Directory.CreateDirectory( directory );
            
            var fileName = Path.Combine( directory, $"{process.ProcessName.ToLowerInvariant()}-{process.Id}-{Guid.NewGuid()}.dmp" );

            if ( !ToolInvocationHelper.InvokeTool(
                    console,
                    "dotnet",
                    $"dump collect -p {process.Id} -o \"{fileName}\"",
                    null ) )
            {
                return false;
            }

            var compressedFileName = fileName + ".gz";

            console.WriteMessage( $"Compressing dump to '{fileName}.gz.'" );

            using ( var readStream = File.OpenRead( fileName ) )
            using ( var writeStream = new GZipStream( File.OpenWrite( compressedFileName ), CompressionMode.Compress ) )
            {
                readStream.CopyTo( writeStream );
            }

            File.Delete( fileName );

            return false;
        }
        catch ( Exception e )
        {
            console.WriteWarning( $"Cannot capture a minidump of {process.Id}: {e.Message}" );

            return false;
        }
    }
}