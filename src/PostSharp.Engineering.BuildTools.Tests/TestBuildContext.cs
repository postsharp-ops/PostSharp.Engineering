// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using PostSharp.Engineering.BuildTools.Build;
using PostSharp.Engineering.BuildTools.Build.Model;
using PostSharp.Engineering.BuildTools.Dependencies.Definitions;
using PostSharp.Engineering.BuildTools.Utilities;
using Spectre.Console.Cli;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace PostSharp.Engineering.BuildTools.Tests;

/// <summary>
/// Creates a <see cref="BuildContext"/> over a given directory. Commands that only read and write files can then be
/// exercised without a git repository and without a command line.
/// </summary>
internal static class TestBuildContext
{
    private sealed class NoRemainingArguments : IRemainingArguments
    {
        public ILookup<string, string?> Parsed { get; } = Array.Empty<string>().ToLookup( s => s, _ => (string?) null );

        public IReadOnlyList<string> Raw { get; } = [];
    }

    public static BuildContext Create( string directory, Product? product = null )
        => new(
            new ConsoleHelper(),
            directory,
            new BaseCommandData( product ?? new Product( MetalamaDependencies.V2026_1.Metalama ) ),
            "develop/2026.1",
            new CommandContext( [], new NoRemainingArguments(), "test", null ),
            useProjectDirectoryAsWorkingDirectory: false,
            new CommonCommandSettings(),
            CancellationToken.None );
}
