// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using JetBrains.Annotations;
using PostSharp.Engineering.BuildTools.Build;
using PostSharp.Engineering.BuildTools.Utilities;

#pragma warning disable CA1416 // Available on Windows only.

namespace PostSharp.Engineering.BuildTools.Tools.Processes
{
    /// <summary>
    /// Kills all processes that may lock build artefacts.
    /// </summary>
    [UsedImplicitly]
    internal class KillCommand : BaseCommand<KillCommandSettings>
    {
        protected override bool ExecuteCore( BuildContext context, KillCommandSettings settings )
        {
            context.Console.WriteHeading( "Killing processes" );

            return ProcessKiller.KillWellKnownProcesses( context.Console, settings.Dry );
        }
    }
}