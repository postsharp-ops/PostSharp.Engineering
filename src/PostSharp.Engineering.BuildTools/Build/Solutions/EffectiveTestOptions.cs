// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using System.Collections.Immutable;

namespace PostSharp.Engineering.BuildTools.Build.Solutions;

/// <summary>
/// The options of a single run of a scenario, i.e. a <see cref="TestOptions"/> merged with one entry of its
/// <see cref="TestOptions.Matrix"/>.
/// </summary>
public sealed record EffectiveTestOptions(
    string? Name,
    ImmutableDictionary<string, string> Properties,
    string? Target,
    bool IgnoreExitCode,
    bool FailOnUnexpectedDiagnostics,
    string[]? ErrorRegexes,
    string[]? ExpectedDiagnosticsRegexes,
    string[]? ForbiddenDiagnosticsRegexes,
    bool BuildOnly )
{
    /// <summary>
    /// Gets the default options, used when there is no <c>test.json</c> file.
    /// </summary>
    public static EffectiveTestOptions Default { get; } = new(
        null,
        ImmutableDictionary<string, string>.Empty,
        null,
        false,
        false,
        null,
        null,
        null,
        false );

    /// <summary>
    /// Gets a name that can be used as a part of a file name, or <c>null</c> if this run is the only run of the scenario.
    /// </summary>
    public string? GetLogNameSuffix() => this.Name;
}
