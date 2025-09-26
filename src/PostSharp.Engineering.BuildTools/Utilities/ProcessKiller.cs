// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using System;
using System.Diagnostics;
using System.Linq;

namespace PostSharp.Engineering.BuildTools.Utilities;

#pragma warning disable CA1416 // Non-portable API.

public static class ProcessKiller
{
    public static bool KillWellKnownProcesses( ConsoleHelper console, bool dry = false )
    {
        var currentSessionId = Process.GetCurrentProcess().SessionId;

        var processesToKill = Process.GetProcesses()
            .Where( p => p.SessionId == currentSessionId )
            .Where( p =>
            {
                if ( p.ProcessName.StartsWith( "redis", StringComparison.OrdinalIgnoreCase ) ||
                     p.ProcessName.StartsWith( "WinMerge", StringComparison.OrdinalIgnoreCase ) )
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
                     ProcessHelper.ReferencesAnyModule( console, p, ["Metalama", "VBCSCompiler", "MSBuild"] ) )
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
                console.WriteMessage( $"Killing process {process.Id} ({process.ProcessName}): {ProcessHelper.GetCommandLine( process )}" );

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
}