// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using Newtonsoft.Json;
using PostSharp.Engineering.BuildTools.Build.Solutions;
using System.Linq;
using Xunit;

namespace PostSharp.Engineering.BuildTools.Tests;

public class TestOptionsTests
{
    [Fact]
    public void NoMatrix_ProducesASingleUnnamedRun()
    {
        var options = JsonConvert.DeserializeObject<TestOptions>(
            """
            {
                "BuildOnly": true,
                "IgnoreExitCode": true,
                "ExpectedDiagnosticsRegexes": [ "LAMA0120" ],
                "FailOnUnexpectedDiagnostics": true
            }
            """ )!;

        var runs = options.GetRuns();

        var run = Assert.Single( runs );
        Assert.True( run.BuildOnly );
        Assert.True( run.IgnoreExitCode );
        Assert.True( run.FailOnUnexpectedDiagnostics );
        Assert.Equal( ["LAMA0120"], run.ExpectedDiagnosticsRegexes! );
        Assert.Empty( run.Properties );
        Assert.Null( run.Target );

        // A single run must not get a log name suffix, so the log file keeps the name of the scenario.
        Assert.Null( run.GetLogNameSuffix() );
    }

    [Fact]
    public void Matrix_ProducesOneRunPerEntry_AndInheritsUnsetProperties()
    {
        var options = JsonConvert.DeserializeObject<TestOptions>(
            """
            {
                "Target": "Rebuild",
                "IgnoreExitCode": true,
                "Properties": { "Shared": "1" },
                "ForbiddenDiagnosticsRegexes": [ "CS8785" ],
                "Matrix": [
                    { "Properties": { "UseSharedCompilation": "true" } },
                    {
                        "Name": "no-shared",
                        "Properties": { "UseSharedCompilation": "false" },
                        "IgnoreExitCode": false,
                        "ForbiddenDiagnosticsRegexes": [ "CS9248" ]
                    }
                ]
            }
            """ )!;

        var runs = options.GetRuns();

        Assert.Equal( 2, runs.Length );

        // The first entry inherits everything but its own properties.
        Assert.Equal( "Rebuild", runs[0].Target );
        Assert.True( runs[0].IgnoreExitCode );
        Assert.Equal( ["CS8785"], runs[0].ForbiddenDiagnosticsRegexes! );
        Assert.Equal( "1", runs[0].Properties["Shared"] );
        Assert.Equal( "true", runs[0].Properties["UseSharedCompilation"] );

        // The second entry overrides.
        Assert.False( runs[1].IgnoreExitCode );
        Assert.Equal( ["CS9248"], runs[1].ForbiddenDiagnosticsRegexes! );
        Assert.Equal( "false", runs[1].Properties["UseSharedCompilation"] );

        // Log names must be distinct so that a matrix entry does not overwrite the log of another one.
        var suffixes = runs.Select( r => r.GetLogNameSuffix() ).ToArray();
        Assert.Equal( "UseSharedCompilation-true", suffixes[0] );
        Assert.Equal( "no-shared", suffixes[1] );
    }

    [Fact]
    public void MatrixEntryProperties_OverrideSharedPropertiesOfTheSameName()
    {
        var options = new TestOptions
        {
            Properties = new() { ["UseSharedCompilation"] = "true" },
            Matrix = [new TestMatrixEntry { Properties = new() { ["UseSharedCompilation"] = "false" } }]
        };

        var run = Assert.Single( options.GetRuns() );

        Assert.Equal( "false", run.Properties["UseSharedCompilation"] );
    }
}
