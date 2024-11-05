// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using System;
using System.Collections.Immutable;
using System.Text.RegularExpressions;

namespace PostSharp.Engineering.BuildTools.Utilities;

public record ToolInvocationOptions(
    ImmutableDictionary<string, string?>? EnvironmentVariables = null,
    bool Silent = false,
    ImmutableArray<string> BlockedEnvironmentVariables = default,
    ToolInvocationRetry? Retry = null )
{
    public static ToolInvocationOptions Default { get; } = new();

    // Some environment variables are set by the Microsoft.Build package and must not be passed to the child process.
    public ImmutableArray<string> BlockedEnvironmentVariables { get; init; } =
        BlockedEnvironmentVariables.IsDefault ? ["DOTNET_ROOT_X64", "MSBUILD_EXE_PATH", "MSBuildSDKsPath"] : BlockedEnvironmentVariables;

    public ImmutableArray<Regex> ErrorPatterns { get; init; } = [new Regex( @"\: error\b" )];

    public ImmutableArray<Regex> WarningPatterns { get; init; } = [new Regex( @"\: warning\b" )];

    public ImmutableArray<Regex> SuccessPatterns { get; init; } = [new Regex( "Passed! " )];

    public ImmutableArray<Regex> ImportantMessagePatterns { get; init; } = [new Regex( "Test run for " )];

    public ImmutableArray<Regex> SilentPatterns { get; init; } = ImmutableArray<Regex>.Empty;

    public ImmutableArray<ReplacePattern> ReplacePatterns { get; init; } = ImmutableArray<ReplacePattern>.Empty;

    public bool FilterOutput { get; init; } = true;

    public TimeSpan OutputReadingTimeout { get; init; } = TimeSpan.FromSeconds( 10 );

    public static TimeSpan LongOutputReadingTimeout => TimeSpan.FromSeconds( 60 );

    public ToolInvocationOptions WithEnvironmentVariables( ImmutableDictionary<string, string?> additionalEnvironmentVariables )
        => this with
        {
            EnvironmentVariables = this.EnvironmentVariables == null
                ? additionalEnvironmentVariables
                : this.EnvironmentVariables.AddRange( additionalEnvironmentVariables )
        };
}