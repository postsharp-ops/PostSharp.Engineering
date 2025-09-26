// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using JetBrains.Annotations;
using PostSharp.Engineering.BuildTools.Build;
using System;
using System.Diagnostics;
using System.Threading.Tasks;

namespace PostSharp.Engineering.BuildTools.Tools.Processes;

[UsedImplicitly]
internal class WaitCommand : BaseCommand<WaitCommandSettings>
{
    protected override bool ExecuteCore( BuildContext context, WaitCommandSettings settings )
    {
        var waitTime = TimeSpan.FromSeconds( settings.Seconds );
        var timeout = settings.Timeout != null ? TimeSpan.FromMinutes( settings.Timeout.Value ).ToString() : "none";
        context.Console.WriteMessage( $"Waiting for {waitTime}. Timeout: {timeout}. Current PID: {Process.GetCurrentProcess().Id}." );

        Task.Delay( waitTime, context.CancellationToken ).Wait();

        return true;
    }
}