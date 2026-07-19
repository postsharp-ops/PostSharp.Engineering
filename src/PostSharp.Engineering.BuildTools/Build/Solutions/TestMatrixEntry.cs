// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using JetBrains.Annotations;
using System.Collections.Generic;

namespace PostSharp.Engineering.BuildTools.Build.Solutions;

/// <summary>
/// An entry of the <see cref="TestOptions.Matrix"/>. Any property left unset falls back to the value defined on the
/// <see cref="TestOptions"/> itself.
/// </summary>
[PublicAPI]
public class TestMatrixEntry
{
    /// <summary>
    /// Gets or sets the name of the matrix entry. It is used to name the log file, so it must be unique within a
    /// scenario and must be a valid file name. Defaults to the properties of the entry.
    /// </summary>
    public string? Name { get; set; }

    public Dictionary<string, string>? Properties { get; set; }

    public string? Target { get; set; }

    public bool? IgnoreExitCode { get; set; }

    public bool? FailOnUnexpectedDiagnostics { get; set; }

    public string[]? ErrorRegexes { get; set; }

    public string[]? ExpectedDiagnosticsRegexes { get; set; }

    public string[]? ForbiddenDiagnosticsRegexes { get; set; }
}
