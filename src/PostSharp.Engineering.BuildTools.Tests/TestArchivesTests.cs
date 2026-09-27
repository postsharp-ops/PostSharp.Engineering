// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using PostSharp.Engineering.BuildTools.Build.Model;
using PostSharp.Engineering.BuildTools.Build.Solutions;
using PostSharp.Engineering.BuildTools.Utilities;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using Xunit;

namespace PostSharp.Engineering.BuildTools.Tests;

/// <summary>
/// Packs a project with <c>TestsPublish.targets</c> and runs the archives with <c>RunTests.ps1</c>.
/// </summary>
/// <remarks>
/// The project imitates a Microsoft.Testing.Platform test application instead of referencing one, so that the test needs
/// no package and no network: it declares itself a test application, registers the TRX extension the way the package of
/// the extension does, writes a TRX report where the options ask for it, and records its command line. That is all the
/// packager and the runner read of a real application.
/// </remarks>
public sealed class TestArchivesTests : IDisposable
{
    private readonly TempDirectory _directory = new();

    private static readonly string _targetFramework = $"net{Environment.Version.Major}.0";

    private static string SdkDirectory
    {
        get
        {
            var directory = AppContext.BaseDirectory;

            while ( directory != null && !File.Exists( Path.Combine( directory, "src", "PostSharp.Engineering.Sdk", "TestsPublish.targets" ) ) )
            {
                directory = Path.GetDirectoryName( directory );
            }

            Assert.NotNull( directory );

            return Path.Combine( directory, "src", "PostSharp.Engineering.Sdk" );
        }
    }

    private string ArchivesDirectory => Path.Combine( this._directory.Path, "artifacts", "tests" );

    private string ResultsDirectory => Path.Combine( this._directory.Path, "artifacts", "testResults" );

    /// <summary>
    /// Writes the repository: a <c>Build.ps1</c> at the root, which is how the packager finds the root, the project, and
    /// the runner generated into <c>eng</c>.
    /// </summary>
    private void CreateRepository()
    {
        File.WriteAllText( Path.Combine( this._directory.Path, "Build.ps1" ), "" );

        var projectDirectory = Path.Combine( this._directory.Path, "src", "Probe" );
        Directory.CreateDirectory( projectDirectory );

        File.WriteAllText(
            Path.Combine( projectDirectory, "Probe.csproj" ),
            $"""
             <Project Sdk="Microsoft.NET.Sdk">
               <PropertyGroup>
                 <OutputType>Exe</OutputType>
                 <TargetFrameworks>{_targetFramework};{_targetFramework}-windows</TargetFrameworks>
                 <EnableWindowsTargeting>true</EnableWindowsTargeting>
                 <IsTestingPlatformApplication>true</IsTestingPlatformApplication>
                 <PublishTestArchive>true</PublishTestArchive>
                 <TestArchiveSkip Condition="$(TargetFramework.EndsWith('-windows'))">Not today; it's broken</TestArchiveSkip>
               </PropertyGroup>
               <ItemGroup>
                 <TestingPlatformBuilderHook Include="2006B3F7-93D2-4D9C-9C69-F41A1F21C9C7">
                   <DisplayName>Microsoft.Testing.Extensions.TrxReport</DisplayName>
                 </TestingPlatformBuilderHook>
                 <TestArchiveTag Include="Fast" />
               </ItemGroup>
               <Import Project="{Path.Combine( SdkDirectory, "TestsPublish.targets" )}" />
             </Project>
             """ );

        // A report with one data row of a theory, as Microsoft.Testing.Platform writes it: the name of the row is in the
        // name of UnitTest, and TestMethod carries the name of the method.
        File.WriteAllText(
            Path.Combine( projectDirectory, "Program.cs" ),
            """
            using System;
            using System.IO;

            var resultsDirectory = args[Array.IndexOf( args, "--results-directory" ) + 1];
            var reportFileName = args[Array.IndexOf( args, "--report-trx-filename" ) + 1];
            File.WriteAllLines( Path.Combine( resultsDirectory, "arguments.txt" ), args );
            File.WriteAllText(
                Path.Combine( resultsDirectory, reportFileName ),
                "<TestRun xmlns=\"http://microsoft.com/schemas/VisualStudio/TeamTest/2010\"><TestDefinitions>"
                + "<UnitTest name=\"Probe.Tests.Rows(value: 1)\"><TestMethod className=\"Probe.Tests\" name=\"Rows\" /></UnitTest>"
                + "</TestDefinitions></TestRun>" );

            return int.Parse( Environment.GetEnvironmentVariable( "PROBE_EXIT_CODE" ) ?? "0" );
            """ );

        var engDirectory = Path.Combine( this._directory.Path, "eng" );
        Directory.CreateDirectory( engDirectory );

        File.WriteAllText(
            Path.Combine( engDirectory, TestArchivesSolution.ScriptName ),
            ReadScript().Replace( "<TEST_RESULTS_PATH>", "artifacts/testResults", StringComparison.Ordinal ),
            new UTF8Encoding( false ) );

        var (exitCode, output) = Build( Path.Combine( projectDirectory, "Probe.csproj" ) );
        Assert.True( exitCode == 0, output );
    }

    private static string ReadScript()
    {
        var resourceName = $"PostSharp.Engineering.BuildTools.Resources.{TestArchivesSolution.ScriptName}";

        using var stream = typeof(Product).Assembly.GetManifestResourceStream( resourceName )
                           ?? throw new InvalidOperationException( $"Cannot find the embedded resource '{resourceName}'." );

        using var reader = new StreamReader( stream );

        return reader.ReadToEnd();
    }

    private (int ExitCode, string Output) RunTests( string arguments, Dictionary<string, string> environment )
    {
        environment["TEAMCITY_VERSION"] = "test";
        var script = Path.Combine( this._directory.Path, "eng", TestArchivesSolution.ScriptName );

        return Run(
            "pwsh",
            $"-NoProfile -NonInteractive -Command \"& '{script}' {arguments}; exit $LASTEXITCODE\"",
            this._directory.Path,
            environment );
    }

    private static (int ExitCode, string Output) Build( string project )
        => Run(
            "dotnet",
            $"build \"{project}\" -nologo -nodeReuse:false -p:UseSharedCompilation=false",
            Path.GetDirectoryName( project )!,
            [],
            blockedEnvironmentVariables: ["TEAMCITY_VERSION"] );

    /// <summary>
    /// Runs a process through <see cref="ToolInvocationHelper"/>, which reads the output after the process has exited and
    /// stops reading after a timeout. Reading the output to its end instead waits for every process that inherited it,
    /// and the worker nodes of MSBuild outlive the build that started them.
    /// </summary>
    private static (int ExitCode, string Output) Run(
        string fileName,
        string commandLine,
        string workingDirectory,
        Dictionary<string, string> environment,
        string[]? blockedEnvironmentVariables = null )
    {
        var options = new ToolInvocationOptions(
            environment.ToImmutableDictionary( v => v.Key, v => (string?) v.Value ),
            BlockedEnvironmentVariables: ToolInvocationOptions.Default.BlockedEnvironmentVariables.AddRange( blockedEnvironmentVariables ?? [] ) )
        {
            FilterOutput = false
        };

        ToolInvocationHelper.InvokeTool( new ConsoleHelper(), fileName, commandLine, workingDirectory, out var exitCode, out var output, options );

        return (exitCode, output);
    }

    private static string ReadManifest( string archive )
    {
        using var zip = ZipFile.OpenRead( archive );
        using var reader = new StreamReader( zip.GetEntry( "test.psd1" )!.Open() );

        return reader.ReadToEnd();
    }

    [Fact]
    public void TheArchivesOfEveryTargetFrameworkRunAndReport()
    {
        if ( DockerBuildScript.FindPowerShell() != "pwsh" )
        {
            return;
        }

        this.CreateRepository();

        // One archive per target framework, each with its manifest.
        var archive = Path.Combine( this.ArchivesDirectory, $"Probe.{_targetFramework}.zip" );
        var windowsArchive = Path.Combine( this.ArchivesDirectory, $"Probe.{_targetFramework}-windows.zip" );
        Assert.True( File.Exists( archive ) );
        Assert.True( File.Exists( windowsArchive ) );

        var manifest = ReadManifest( archive );
        Assert.Contains( $"Entry = 'Probe.dll'", manifest, StringComparison.Ordinal );
        Assert.Contains( "Platforms = @('win-x64', 'win-arm64', 'linux-x64', 'linux-arm64', 'osx-x64', 'osx-arm64')", manifest, StringComparison.Ordinal );
        Assert.Contains( "Extensions = @('Microsoft.Testing.Extensions.TrxReport')", manifest, StringComparison.Ordinal );
        Assert.Contains( "Tags = @('Fast')", manifest, StringComparison.Ordinal );
        Assert.Contains( "Skip = $null", manifest, StringComparison.Ordinal );

        // A Windows target framework runs on Windows only, and the apostrophe and the semicolon of the reason survive.
        var windowsManifest = ReadManifest( windowsArchive );
        Assert.Contains( "Platforms = @('win-x64', 'win-arm64')", windowsManifest, StringComparison.Ordinal );
        Assert.Contains( "Skip = 'Not today; it''s broken'", windowsManifest, StringComparison.Ordinal );

        var (exitCode, output) = this.RunTests( "", [] );
        Assert.True( exitCode == 0, output );
        Assert.Contains( $"Probe.{_targetFramework}-windows : skipped: Not today; it's broken.", output, StringComparison.Ordinal );
        Assert.Contains( "##teamcity[importData", output, StringComparison.Ordinal );

        var results = Path.Combine( this.ResultsDirectory, $"Probe.{_targetFramework}" );
        var arguments = File.ReadAllLines( Path.Combine( results, "arguments.txt" ) );
        Assert.Contains( "--report-trx", arguments );

        // The options of an extension that the application does not have are not passed, because the platform refuses them.
        Assert.DoesNotContain( "--hangdump", arguments );

        // The data row is named after its arguments, which is how TeamCity tells the rows of a theory apart.
        var report = File.ReadAllText( Path.Combine( results, $"Probe.{_targetFramework}.trx" ) );
        Assert.Contains( "name=\"Rows(value: 1)\"", report, StringComparison.Ordinal );
    }

    [Fact]
    public void AFailureOtherThanAFailedTestIsABuildProblem()
    {
        if ( DockerBuildScript.FindPowerShell() != "pwsh" )
        {
            return;
        }

        this.CreateRepository();

        // Exit code 7 is an unhandled exception in the application: no failed test fails the build in that case.
        var (exitCode, output) = this.RunTests( "", new Dictionary<string, string> { ["PROBE_EXIT_CODE"] = "7" } );
        Assert.Equal( 1, exitCode );
        Assert.Contains( "##teamcity[buildProblem", output, StringComparison.Ordinal );
        Assert.Contains( "exit code 7", output, StringComparison.Ordinal );

        // Exit code 2 is a failed test, which the imported report already reports.
        (exitCode, output) = this.RunTests( "", new Dictionary<string, string> { ["PROBE_EXIT_CODE"] = "2" } );
        Assert.Equal( 1, exitCode );
        Assert.DoesNotContain( "##teamcity[buildProblem", output, StringComparison.Ordinal );
    }

    /// <summary>
    /// A run that selects nothing fails. Otherwise a build configuration given the wrong archives, or a tag with a typo,
    /// would report success forever without running a test.
    /// </summary>
    [Fact]
    public void NoArchiveSelectedFails()
    {
        if ( DockerBuildScript.FindPowerShell() != "pwsh" )
        {
            return;
        }

        this.CreateRepository();

        var (exitCode, output) = this.RunTests( "-Tags 'Slow'", [] );
        Assert.Equal( 1, exitCode );
        Assert.Contains( "None of the 2 test archive(s) applies", output, StringComparison.Ordinal );
    }

    [Fact]
    public void NoArchiveIsWrittenUnlessThePropertyIsSet()
    {
        this._directory.WriteFile( "Build.ps1", "" );

        var project = this._directory.WriteFile(
            "Probe.csproj",
            $"""
             <Project Sdk="Microsoft.NET.Sdk">
               <PropertyGroup>
                 <OutputType>Exe</OutputType>
                 <TargetFramework>{_targetFramework}</TargetFramework>
                 <IsTestingPlatformApplication>true</IsTestingPlatformApplication>
               </PropertyGroup>
               <Import Project="{Path.Combine( SdkDirectory, "TestsPublish.targets" )}" />
             </Project>
             """ );

        this._directory.WriteFile( "Program.cs", "return 0;" );

        var (exitCode, output) = Build( project );
        Assert.True( exitCode == 0, output );
        Assert.False( Directory.Exists( this.ArchivesDirectory ) );
    }

    [Fact]
    public void TheCommandPassesEveryTagAsAnArrayElement()
    {
        var command = TestArchivesSolution.GetCommand( "C:\\repo\\eng\\RunTests.ps1", ["A", "it's"], ["B"] );

        Assert.Equal( "& 'C:\\repo\\eng\\RunTests.ps1' -Tags 'A','it''s' -ExcludeTags 'B'; exit $LASTEXITCODE", command );
    }

    public void Dispose() => this._directory.Dispose();
}
