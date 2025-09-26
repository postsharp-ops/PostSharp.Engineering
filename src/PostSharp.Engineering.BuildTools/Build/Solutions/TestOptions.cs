// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using JetBrains.Annotations;

namespace PostSharp.Engineering.BuildTools.Build.Solutions;

[PublicAPI]
public class TestOptions
{
    public bool IgnoreExitCode { get; set; }

    public string[]? ErrorRegexes { get; set; }

    public string[]? ExpectedDiagnosticsRegexes { get; set; }

    public bool FailOnUnexpectedDiagnostics { get; set; }

    public bool BuildOnly { get; set; }
}