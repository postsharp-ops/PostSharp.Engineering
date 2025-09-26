// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using JetBrains.Annotations;
using Spectre.Console.Cli;

namespace PostSharp.Engineering.BuildTools.Tools.Processes;

[UsedImplicitly]
internal class WaitCommandSettings : CommonCommandSettings
{
    [CommandArgument( 0, "<seconds>" )]
    public int Seconds { get; init; }
}