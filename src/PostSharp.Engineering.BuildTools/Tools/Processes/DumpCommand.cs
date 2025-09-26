// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using JetBrains.Annotations;
using PostSharp.Engineering.BuildTools.Build;
using PostSharp.Engineering.BuildTools.Utilities;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace PostSharp.Engineering.BuildTools.Tools.Processes;

[UsedImplicitly]
internal class DumpCommand : BaseCommand<DumpCommandSettings>
{
    protected override bool ExecuteCore( BuildContext context, DumpCommandSettings settings )
    {
        var console = context.Console;

        // List all child processes.
        var processes = ProcessHelper.GetProcessTree( console, Process.GetCurrentProcess().Id );

        console.WriteMessage( "Process tree:" );

        foreach ( var node in processes )
        {
            var indent = new string( '-', (node.NestingLevel + 1) * 3 );
            console.WriteMessage( $"+{indent} {node.Process.Id} {ProcessHelper.GetCommandLine( node.Process )}" );
        }

        // Dump these processes.
        ProcessHelper.DumpProcesses( console, processes.Select( p => p.Process ), Path.GetTempPath() );

        return true;
    }
}