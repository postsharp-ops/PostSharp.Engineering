// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using JetBrains.Annotations;
using Spectre.Console.Cli;

namespace PostSharp.Engineering.BuildTools.Tools.Processes;

[UsedImplicitly]
internal sealed class DumpAndKillCommandSettings : CommonCommandSettings
{
    [CommandArgument( 0, "<process-id>" )]
    public int ProcessId { get; init; }
}