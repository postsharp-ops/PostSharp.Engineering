// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using JetBrains.Annotations;
using PostSharp.Engineering.BuildTools.Build;
using PostSharp.Engineering.BuildTools.Utilities;
using System;
using System.Diagnostics;
using System.IO;

namespace PostSharp.Engineering.BuildTools.Tools.Processes;

[UsedImplicitly]
internal class DumpAndKillCommand : BaseCommand<DumpAndKillCommandSettings>
{
    protected override bool ExecuteCore( BuildContext context, DumpAndKillCommandSettings settings )
    {
        Process process;

        try
        {
            process = Process.GetProcessById( settings.ProcessId );
        }
        catch ( ArgumentException e )
        {
            context.Console.WriteError( e.Message );

            return false;
        }

        ProcessHelper.DumpAndKillProcessTree( context.Console, process, Path.GetTempPath() );

        return true;
    }
}