// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;

namespace PostSharp.Engineering.BuildTools.Utilities;

#pragma warning disable CA1416 // Non-portable API.

public static class ProcessKiller
{
    public static bool Kill( ConsoleHelper console, bool dry = false )
    {
        var currentSessionId = Process.GetCurrentProcess().SessionId;

        var processesToKill = Process.GetProcesses()
            .Where( p => p.SessionId == currentSessionId )
            .Where(
                p =>
                {
                    if ( p.ProcessName.StartsWith( "redis", StringComparison.OrdinalIgnoreCase ) )
                    {
                        return true;
                    }

                    if ( p.ProcessName.Equals( "VBCSCompiler", StringComparison.OrdinalIgnoreCase )
                         || p.ProcessName.Equals( "MSBuild", StringComparison.OrdinalIgnoreCase )
                         || p.ProcessName.Contains( "PostSharp", StringComparison.OrdinalIgnoreCase ) )
                    {
                        return true;
                    }

                    if ( p.ProcessName.Equals( "dotnet", StringComparison.OrdinalIgnoreCase ) &&
                         ReferencesAny( console, p, ["Metalama", "VBCSCompiler", "MSBuild"] ) )
                    {
                        return true;
                    }

                    if ( p.ProcessName.StartsWith( "testhost", StringComparison.OrdinalIgnoreCase ) )
                    {
                        return true;
                    }

                    return false;
                } )
            .ToList();

        if ( processesToKill.Count == 0 )
        {
            console.WriteImportantMessage( "No process found." );
        }
        else
        {
            foreach ( var process in processesToKill )
            {
                console.WriteMessage( $"Killing process {process.Id} ({process.ProcessName}): {GetCommandLine( process )}" );

                if ( !dry )
                {
                    try
                    {
                        process.Kill( true );
                    }
                    catch ( Exception e )
                    {
                        console.WriteWarning( $"Cannot kill {process.Id} ({process.ProcessName}): {e.Message}" );
                    }
                }
            }
        }

        return true;
    }

    private static string? GetCommandLine( Process process )
    {
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

    private static bool ReferencesAny( ConsoleHelper console, Process process, string[] substrings )
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
}