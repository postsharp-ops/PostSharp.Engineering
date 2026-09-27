// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using PostSharp.Engineering.BuildTools.Build;
using PostSharp.Engineering.BuildTools.Build.Model;
using PostSharp.Engineering.BuildTools.Build.Solutions;
using PostSharp.Engineering.BuildTools.Dependencies.Definitions;
using PostSharp.Engineering.BuildTools.Utilities;
using System;
using System.IO;
using System.Linq;
using Xunit;

namespace PostSharp.Engineering.BuildTools.Tests;

/// <summary>
/// Runs <see cref="TestingPlatformTestRunner"/> on a solution whose projects imitate Microsoft.Testing.Platform test
/// applications.
/// </summary>
/// <remarks>
/// Each project defines its own <c>InvokeTestingPlatform</c> target, which is the target of
/// <c>Microsoft.Testing.Platform.MSBuild</c> that the runner calls. The imitation records the phase and the command line it
/// was given, and writes a report where the command line asks for it, so the test needs no package, no restore and no
/// build.
/// </remarks>
public sealed class TestingPlatformTestRunnerTests : IDisposable
{
    private readonly TempDirectory _directory = new();

    private static readonly string _targetFramework = $"net{Environment.Version.Major}.0";

    private string InvocationsFile => Path.Combine( this._directory.Path, "invocations.txt" );

    /// <summary>
    /// Writes a project. Its <c>InvokeTestingPlatform</c> records one line, and writes a report in which a data row is
    /// named as Microsoft.Testing.Platform names it.
    /// </summary>
    private void CreateProject( string name, string properties, bool fails = false )
    {
        var directory = Path.Combine( this._directory.Path, name );
        Directory.CreateDirectory( directory );

        File.WriteAllText(
            Path.Combine( directory, $"{name}.csproj" ),
            $"""
             <Project Sdk="Microsoft.NET.Sdk">
               <PropertyGroup>
                 <TargetFrameworks>{_targetFramework};netstandard2.0</TargetFrameworks>
                 <IsTestingPlatformApplication Condition="'$(TargetFramework)' != 'netstandard2.0'">true</IsTestingPlatformApplication>
                 {properties}
               </PropertyGroup>
               <ItemGroup>
                 <TestingPlatformBuilderHook Include="2006B3F7-93D2-4D9C-9C69-F41A1F21C9C7">
                   <DisplayName>Microsoft.Testing.Extensions.TrxReport</DisplayName>
                 </TestingPlatformBuilderHook>
               </ItemGroup>
               <Target Name="InvokeTestingPlatform">
                 <WriteLinesToFile File="{this.InvocationsFile}" Lines="$(MSBuildProjectName)|$(TargetFramework)|$(PostSharpEngineeringTestPhase)|$(TestingPlatformCommandLineArguments.Replace(';', ','))" />
                 <MakeDir Directories="$(PostSharpEngineeringTestResultsDirectory)" />
                 <WriteLinesToFile
                   File="$(PostSharpEngineeringTestResultsDirectory)/$(AssemblyName).$(TargetFramework).trx"
                   Lines="&lt;TestRun xmlns=&quot;http://microsoft.com/schemas/VisualStudio/TeamTest/2010&quot;&gt;&lt;TestDefinitions&gt;&lt;UnitTest name=&quot;Probe.Tests.Rows(value: 1)&quot;&gt;&lt;TestMethod className=&quot;Probe.Tests&quot; name=&quot;Rows&quot; /&gt;&lt;/UnitTest&gt;&lt;/TestDefinitions&gt;&lt;/TestRun&gt;" />
                 <Error Text="The tests of $(MSBuildProjectName) failed." Condition="{(fails ? "true" : "false")}" />
               </Target>
             </Project>
             """ );
    }

    /// <summary>
    /// Writes a solution of the given projects, in the format that every version of the .NET SDK reads.
    /// </summary>
    private string CreateSolution( params string[] projects )
    {
        var lines = projects.SelectMany(
            p => new[]
            {
                $"Project(\"{{9A19103F-16F7-4668-BE54-9A1E7A4F7556}}\") = \"{p}\", \"{p}\\{p}.csproj\", \"{{{Guid.NewGuid().ToString().ToUpperInvariant()}}}\"",
                "EndProject"
            } );

        var path = Path.Combine( this._directory.Path, "Probe.sln" );

        File.WriteAllLines(
            path,
            ["Microsoft Visual Studio Solution File, Format Version 12.00", ..lines, "Global", "EndGlobal"] );

        return path;
    }

    private bool RunTests( string solutionPath, out string output )
    {
        var product = new Product( MetalamaDependencies.V2026_1.Metalama );
        var context = TestBuildContext.Create( this._directory.Path, product );
        var solution = new DotNetSolution( solutionPath ) { TestRunner = TestRunner.MicrosoftTestingPlatform };
        var log = "";

        var success = TestingPlatformTestRunner.Test(
            context,
            new BuildSettings(),
            solution,
            solutionPath,
            ( project, target ) =>
            {
                var options = new ToolInvocationOptions(
                    BlockedEnvironmentVariables: ToolInvocationOptions.Default.BlockedEnvironmentVariables.Add( "TEAMCITY_VERSION" ) )
                {
                    FilterOutput = false
                };

                ToolInvocationHelper.InvokeTool(
                    new ConsoleHelper(),
                    "dotnet",
                    $"msbuild \"{project}\" -t:{target} -nologo -nodeReuse:false",
                    this._directory.Path,
                    out var exitCode,
                    out log,
                    options );

                return exitCode == 0;
            } );

        output = log;

        return success;
    }

    [Fact]
    public void TheApplicationsRunInTheirPhaseAndReport()
    {
        this.CreateProject( "Parallel", "" );
        this.CreateProject( "Alone", "<TestApplicationRunAlone>true</TestApplicationRunAlone>" );
        this.CreateProject( "Skipped", "<TestApplicationSkip>Not today</TestApplicationSkip>" );

        Assert.True( this.RunTests( this.CreateSolution( "Parallel", "Alone", "Skipped" ), out var output ), output );

        var invocations = File.ReadAllLines( this.InvocationsFile );

        // One invocation per test application: the .NET Standard build is not one, and the skipped application runs nothing.
        Assert.Equal( 2, invocations.Length );
        Assert.Contains( invocations, i => i.StartsWith( $"Alone|{_targetFramework}|RunAlone|", StringComparison.Ordinal ) );
        Assert.Contains( invocations, i => i.StartsWith( $"Parallel|{_targetFramework}|Parallel|", StringComparison.Ordinal ) );
        Assert.Contains( "Skipped (" + _targetFramework + ") is skipped: Not today", output, StringComparison.Ordinal );

        // The options of an extension are passed only when the application has it.
        var parallel = invocations.Single( i => i.StartsWith( "Parallel|", StringComparison.Ordinal ) );
        Assert.Contains( "--results-directory", parallel, StringComparison.Ordinal );
        Assert.Contains( "--report-trx", parallel, StringComparison.Ordinal );
        Assert.DoesNotContain( "--hangdump", parallel, StringComparison.Ordinal );

        // The reports reach the results directory, with each data row named after its arguments.
        var reports = Directory.GetFiles( Path.Combine( this._directory.Path, "artifacts", "testResults" ), "*.trx", SearchOption.AllDirectories );
        Assert.Equal( 2, reports.Length );
        Assert.All( reports, r => Assert.Contains( "name=\"Rows(value: 1)\"", File.ReadAllText( r ), StringComparison.Ordinal ) );
    }

    /// <summary>
    /// A failed application fails the run without stopping the others, and its report is still published, because it says
    /// which tests failed.
    /// </summary>
    [Fact]
    public void AFailedApplicationFailsTheRunAndTheOthersStillRun()
    {
        this.CreateProject( "Failing", "", fails: true );
        this.CreateProject( "Passing", "" );

        Assert.False( this.RunTests( this.CreateSolution( "Failing", "Passing" ), out var output ), output );

        Assert.Equal( 2, File.ReadAllLines( this.InvocationsFile ).Length );
        Assert.Equal( 2, Directory.GetFiles( Path.Combine( this._directory.Path, "artifacts", "testResults" ), "*.trx", SearchOption.AllDirectories ).Length );
    }

    public void Dispose() => this._directory.Dispose();
}
