// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using JetBrains.Annotations;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;

namespace PostSharp.Engineering.BuildTools.Build.Solutions;

/// <summary>
/// The content of the <c>test.json</c> file that can be placed next to a scenario built by
/// <see cref="ManyDotNetSolutions"/> or <see cref="ManyMSBuildSolutions"/>.
/// </summary>
[PublicAPI]
public class TestOptions
{
    public bool IgnoreExitCode { get; set; }

    public string[]? ErrorRegexes { get; set; }

    public string[]? ExpectedDiagnosticsRegexes { get; set; }

    /// <summary>
    /// Gets or sets the regexes of diagnostics that must <i>not</i> appear in the build output. Unlike
    /// <see cref="FailOnUnexpectedDiagnostics"/>, this does not fire on incidental unrelated diagnostics.
    /// </summary>
    public string[]? ForbiddenDiagnosticsRegexes { get; set; }

    public bool FailOnUnexpectedDiagnostics { get; set; }

    public bool BuildOnly { get; set; }

    /// <summary>
    /// Gets or sets the MSBuild target. Only honored by engines that support target selection, i.e. by
    /// <see cref="ManyMSBuildSolutions"/>.
    /// </summary>
    public string? Target { get; set; }

    /// <summary>
    /// Gets or sets the MSBuild properties passed to every entry of the <see cref="Matrix"/>.
    /// </summary>
    public Dictionary<string, string>? Properties { get; set; }

    /// <summary>
    /// Gets or sets the property matrix. When set, the scenario is built once per entry, and each run is asserted
    /// independently. When not set, the scenario is built exactly once.
    /// </summary>
    public TestMatrixEntry[]? Matrix { get; set; }

    /// <summary>
    /// Returns the effective options of each run of this scenario. Always returns at least one item.
    /// </summary>
    public ImmutableArray<EffectiveTestOptions> GetRuns()
    {
        if ( this.Matrix == null || this.Matrix.Length == 0 )
        {
            return [this.GetRun( null, 0 )];
        }

        return [..this.Matrix.Select( this.GetRun )];
    }

    private EffectiveTestOptions GetRun( TestMatrixEntry? entry, int index )
    {
        var properties = ImmutableDictionary<string, string>.Empty;

        if ( this.Properties != null )
        {
            properties = properties.SetItems( this.Properties );
        }

        if ( entry?.Properties != null )
        {
            properties = properties.SetItems( entry.Properties );
        }

        return new EffectiveTestOptions(
            GetName( entry, index ),
            properties,
            entry?.Target ?? this.Target,
            entry?.IgnoreExitCode ?? this.IgnoreExitCode,
            entry?.FailOnUnexpectedDiagnostics ?? this.FailOnUnexpectedDiagnostics,
            entry?.ErrorRegexes ?? this.ErrorRegexes,
            entry?.ExpectedDiagnosticsRegexes ?? this.ExpectedDiagnosticsRegexes,
            entry?.ForbiddenDiagnosticsRegexes ?? this.ForbiddenDiagnosticsRegexes,
            this.BuildOnly );
    }

    /// <summary>
    /// Returns the name identifying a matrix entry, or <c>null</c> if the scenario is built exactly once. The name is
    /// derived from the properties of the entry only, and not from the properties shared by the whole matrix, because
    /// it must tell the entries apart.
    /// </summary>
    private static string? GetName( TestMatrixEntry? entry, int index )
    {
        if ( entry == null )
        {
            return null;
        }

        if ( entry.Name != null )
        {
            return entry.Name;
        }

        if ( entry.Properties is not { Count: > 0 } )
        {
            return index.ToString( CultureInfo.InvariantCulture );
        }

        return string.Join( "_", entry.Properties.OrderBy( p => p.Key, StringComparer.Ordinal ).Select( p => $"{p.Key}-{p.Value}" ) );
    }
}
