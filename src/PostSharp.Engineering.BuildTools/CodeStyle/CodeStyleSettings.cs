// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using JetBrains.Annotations;
using Spectre.Console.Cli;
using System.ComponentModel;

namespace PostSharp.Engineering.BuildTools.CodeStyle;

/// <summary>
/// Settings for <see cref="PushCodeStyleCommand"/> and <see cref="PullCodeStyleCommand"/>.
/// </summary>
[PublicAPI]
internal class CodeStyleSettings : CommonCommandSettings
{
    [Description( "Clones the repo if it does not exist." )]
    [CommandOption( "--create" )]
    public bool Create { get; init; }

    [Description( "Path to PostSharp.Engineering.CodeStyle. The default value is `../PostSharp.Engineering.CodeStyle`." )]
    [CommandArgument( 0, "[path]" )]
    public string Path { get; init; } = "../PostSharp.Engineering.CodeStyle";

    [Description( "Remote URL of the repo." )]
    [CommandOption( "-u|--url" )]
    public string Url { get; init; } =
        "https://github.com/postsharp/PostSharp.Engineering.CodeStyle.git";
}