// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using JetBrains.Annotations;
using PostSharp.Engineering.BuildTools.Utilities;

#pragma warning disable CA1416 // Available on Windows only.

namespace PostSharp.Engineering.BuildTools.Build
{
    /// <summary>
    /// Kills all processes that may lock build artefacts.
    /// </summary>
    [UsedImplicitly]
    public class KillCommand : BaseCommand<KillCommandSettings>
    {
        protected override bool ExecuteCore( BuildContext context, KillCommandSettings settings )
        {
            context.Console.WriteHeading( "Killing processes" );

            return ProcessKiller.Kill( context.Console, settings.Dry );
        }
    }
}