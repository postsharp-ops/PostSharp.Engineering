// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using PostSharp.Engineering.BuildTools.Build;
using PostSharp.Engineering.BuildTools.Build.Model;
using PostSharp.Engineering.BuildTools.Build.Solutions;
using System;
using System.IO;
using Xunit;

namespace PostSharp.Engineering.BuildTools.Tests;

/// <summary>
/// The target that <see cref="DotNetSolution"/> builds when the scenario is a <c>*.proj</c> file.
/// </summary>
/// <remarks>
/// <para>
/// A <c>*.proj</c> scenario declares no <c>DefaultTargets</c> attribute, so MSBuild builds the first target in
/// evaluation order, and an <c>&lt;Import&gt;</c> is expanded at its position. A scenario that imports
/// <c>Directory.Build.props</c> before it declares its own <c>Build</c> target therefore built whatever target the
/// import chain contributed first. The build succeeded, so the scenario reported success although it had compiled
/// nothing. Four scenarios of the Metalama repository were in that state.
/// </para>
/// <para>
/// These tests run a real <c>dotnet build</c>, because the defect is in what MSBuild decides to do with the command
/// line and not in the command line itself. Each target writes a marker file, and the assertion is which markers
/// exist afterwards.
/// </para>
/// </remarks>
public sealed class DotNetSolutionTargetTests : IDisposable
{
    private readonly string _directory = Path.Combine( Path.GetTempPath(), $"proj-target-{Guid.NewGuid():N}" );

    public DotNetSolutionTargetTests()
    {
        Directory.CreateDirectory( this._directory );

        // The `dotnet` engine writes a binary log under the logs directory of the product.
        Directory.CreateDirectory( Path.Combine( this._directory, "artifacts", "logs" ) );

        // The import declares a target before the scenario declares its own, which is the shape that makes the
        // default target of the scenario the wrong one.
        File.WriteAllText(
            Path.Combine( this._directory, "Directory.Build.props" ),
            """
            <Project>
                <Target Name="VerifyProductDependencies">
                    <WriteLinesToFile File="$(MSBuildThisFileDirectory)imported.marker" Lines="ran" Overwrite="true" />
                </Target>
            </Project>
            """ );
    }

    private void WriteScenario( string fileName, string? testJson = null )
    {
        File.WriteAllText(
            Path.Combine( this._directory, fileName ),
            """
            <Project>
                <Import Project="Directory.Build.props" />
                <Target Name="Build">
                    <WriteLinesToFile File="$(MSBuildThisFileDirectory)build.marker" Lines="ran" Overwrite="true" />
                </Target>
                <Target Name="Verify">
                    <WriteLinesToFile File="$(MSBuildThisFileDirectory)verify.marker" Lines="ran" Overwrite="true" />
                </Target>
                <!-- `dotnet build` restores implicitly, and this project has no SDK to supply the target. -->
                <Target Name="Restore" />
            </Project>
            """ );

        if ( testJson != null )
        {
            File.WriteAllText( Path.Combine( this._directory, "test.json" ), testJson );
        }
    }

    private bool BuildScenario( string fileName )
    {
        var context = TestBuildContext.Create( this._directory );

        var settings = new BuildSettings { BuildConfiguration = BuildConfiguration.Debug, Verbosity = Verbosity.Minimal };

        // The specified configuration only becomes the effective one here, which the command line then reads.
        settings.Initialize( context );

        return new DotNetSolution( fileName ).Build( context, settings );
    }

    private bool MarkerExists( string name ) => File.Exists( Path.Combine( this._directory, name ) );

    /// <summary>
    /// The regression. Before the target was named, only the imported target ran.
    /// </summary>
    [Fact]
    public void AProjScenarioBuildsItsOwnBuildTarget()
    {
        this.WriteScenario( "Scenario.proj" );

        Assert.True( this.BuildScenario( "Scenario.proj" ) );

        Assert.True( this.MarkerExists( "build.marker" ), "The `Build` target of the scenario did not run." );
        Assert.False( this.MarkerExists( "imported.marker" ), "The first target of the import chain ran instead of `Build`." );
    }

    /// <summary>
    /// The <c>Target</c> property of <c>test.json</c> used to be read by the MSBuild engines only, and silently
    /// ignored by this one.
    /// </summary>
    [Fact]
    public void TheTargetOfTestJsonOverridesTheDefault()
    {
        this.WriteScenario( "Scenario.proj", """{ "Target": "Verify" }""" );

        Assert.True( this.BuildScenario( "Scenario.proj" ) );

        Assert.True( this.MarkerExists( "verify.marker" ), "The target named by test.json did not run." );
        Assert.False( this.MarkerExists( "build.marker" ), "The default target ran although test.json named another one." );
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete( this._directory, true );
        }
        catch ( IOException )
        {
            // A leftover temporary directory is not worth failing a test over.
        }
    }
}
