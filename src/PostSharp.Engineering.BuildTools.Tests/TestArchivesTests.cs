// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using PostSharp.Engineering.BuildTools.Build;
using PostSharp.Engineering.BuildTools.Build.Model;
using PostSharp.Engineering.BuildTools.Build.Solutions;
using PostSharp.Engineering.BuildTools.Build.Testing;
using PostSharp.Engineering.BuildTools.ContinuousIntegration.Model;
using PostSharp.Engineering.BuildTools.Dependencies.Definitions;
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
/// Packs a project with <c>TestArchive.targets</c> and runs the archives with <c>RunTests.ps1</c>.
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

            while ( directory != null && !File.Exists( Path.Combine( directory, "src", "PostSharp.Engineering.Sdk", "TestArchive.targets" ) ) )
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
        this.WriteDirectoryBuildTargets();

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
                 <TestApplicationSkip Condition="$(TargetFramework.EndsWith('-windows'))">Not today; it's broken</TestApplicationSkip>
                 <TestApplicationPrepareScript>Prepare.ps1</TestApplicationPrepareScript>
                 <TestApplicationArtifacts>artifacts/publish/Probe.*.txt</TestApplicationArtifacts>
               </PropertyGroup>
               <ItemGroup>
                 <TestingPlatformBuilderHook Include="2006B3F7-93D2-4D9C-9C69-F41A1F21C9C7">
                   <DisplayName>Microsoft.Testing.Extensions.TrxReport</DisplayName>
                 </TestingPlatformBuilderHook>
                 <TestApplicationTag Include="Fast" />
                 <TestApplicationTag Include="Owner's" />
               </ItemGroup>
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
            File.WriteAllText( Path.Combine( resultsDirectory, "prepared.txt" ), Environment.GetEnvironmentVariable( "PROBE_PREPARED" ) ?? "" );
            File.WriteAllText(
                Path.Combine( resultsDirectory, reportFileName ),
                "<TestRun xmlns=\"http://microsoft.com/schemas/VisualStudio/TeamTest/2010\"><TestDefinitions>"
                + "<UnitTest name=\"Probe.Tests.Rows(value: 1)\"><TestMethod className=\"Probe.Tests\" name=\"Rows\" /></UnitTest>"
                + "</TestDefinitions></TestRun>" );

            return int.Parse( Environment.GetEnvironmentVariable( "PROBE_EXIT_CODE" ) ?? "0" );
            """ );

        // The prepare script reads the artifact that the project declares, and gives its content to the application.
        File.WriteAllText(
            Path.Combine( projectDirectory, "Prepare.ps1" ),
            """
            param([string]$RepositoryRoot, [string]$ApplicationDirectory)

            if ($env:PROBE_PREPARE_FAILS) { throw 'The preparation failed.' }

            $artifact = Get-ChildItem -Path (Join-Path $RepositoryRoot 'artifacts/publish/Probe.*.txt') | Select-Object -First 1
            @{ PROBE_PREPARED = (Get-Content -LiteralPath $artifact.FullName -Raw).Trim() }
            """ );

        var publishDirectory = Path.Combine( this._directory.Path, "artifacts", "publish" );
        Directory.CreateDirectory( publishDirectory );
        File.WriteAllText( Path.Combine( publishDirectory, "Probe.1.0.txt" ), "from the artifact" );

        var engDirectory = Path.Combine( this._directory.Path, "eng" );
        Directory.CreateDirectory( engDirectory );

        File.WriteAllText(
            Path.Combine( engDirectory, TestArchives.ScriptName ),
            ReadScript().Replace( "<TEST_RESULTS_PATH>", "artifacts/testResults", StringComparison.Ordinal ),
            new UTF8Encoding( false ) );

        var (exitCode, output) = Build( Path.Combine( projectDirectory, "Probe.csproj" ) );
        Assert.True( exitCode == 0, output );
    }

    /// <summary>
    /// Imports the targets file from <c>Directory.Build.targets</c>, after the body of the project and the inference of the
    /// target framework by the .NET SDK, which its defaults are computed from.
    /// </summary>
    private void WriteDirectoryBuildTargets()
        => File.WriteAllText(
            Path.Combine( this._directory.Path, "Directory.Build.targets" ),
            $"""
             <Project>
               <Import Project="{Path.Combine( SdkDirectory, "TestArchive.targets" )}" />
             </Project>
             """ );

    private static string ReadScript()
    {
        var resourceName = $"PostSharp.Engineering.BuildTools.Resources.{TestArchives.ScriptName}";

        using var stream = typeof(Product).Assembly.GetManifestResourceStream( resourceName )
                           ?? throw new InvalidOperationException( $"Cannot find the embedded resource '{resourceName}'." );

        using var reader = new StreamReader( stream );

        return reader.ReadToEnd();
    }

    private (int ExitCode, string Output) RunTests( string arguments, Dictionary<string, string> environment )
    {
        environment["TEAMCITY_VERSION"] = "test";
        var script = Path.Combine( this._directory.Path, "eng", TestArchives.ScriptName );

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
        Assert.Contains( "Tags = @('Fast', 'Owner''s')", manifest, StringComparison.Ordinal );
        Assert.Contains( "Skip = $null", manifest, StringComparison.Ordinal );
        Assert.Contains( "Prepare = 'Prepare.ps1'", manifest, StringComparison.Ordinal );
        Assert.Contains( "Artifacts = @('artifacts/publish/Probe.*.txt')", manifest, StringComparison.Ordinal );

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

        // The environment variables that the prepare script returns reach the application.
        Assert.Equal( "from the artifact", File.ReadAllText( Path.Combine( results, "prepared.txt" ) ) );

        // The data row is named after its arguments, which is how TeamCity tells the rows of a theory apart.
        var report = File.ReadAllText( Path.Combine( results, "report.trx" ) );
        Assert.Contains( "name=\"Rows(value: 1)\"", report, StringComparison.Ordinal );
    }

    /// <summary>
    /// A project describes an archive whose entry is a PowerShell script, for tests that are not a .NET application. The
    /// archive holds the script and the files the project gives it, and the runner runs the script with the platform and the
    /// results directory, and imports the reports that it names.
    /// </summary>
    [Fact]
    public void AnArchiveOfThePs1KindRunsItsScript()
    {
        if ( DockerBuildScript.FindPowerShell() != "pwsh" )
        {
            return;
        }

        File.WriteAllText( Path.Combine( this._directory.Path, "Build.ps1" ), "" );
        this.WriteDirectoryBuildTargets();

        var projectDirectory = Path.Combine( this._directory.Path, "src", "Native" );
        Directory.CreateDirectory( projectDirectory );

        File.WriteAllText(
            Path.Combine( projectDirectory, "Native.csproj" ),
            $$"""
             <Project Sdk="Microsoft.NET.Sdk">
               <PropertyGroup>
                 <TargetFramework>{{_targetFramework}}</TargetFramework>
                 <PublishTestArchive>true</PublishTestArchive>
                 <TestApplicationKind>ps1</TestApplicationKind>
                 <TestApplicationEntry>RunTest.ps1</TestApplicationEntry>
                 <TestApplicationReportType>gtest</TestApplicationReportType>
                 <TestApplicationReportFile>{ResultsDirectory}/*.xml</TestApplicationReportFile>
               </PropertyGroup>
               <ItemGroup>
                 <TestApplicationFile Include="data.txt" ArchivePath="x64\data.txt" />
                 <TestApplicationFile Include="readme.txt" />
               </ItemGroup>
             </Project>
             """ );

        File.WriteAllText( Path.Combine( projectDirectory, "data.txt" ), "native" );
        File.WriteAllText( Path.Combine( projectDirectory, "readme.txt" ), "" );

        // The script reads the file that the project put into the archive, and writes one report per run.
        File.WriteAllText(
            Path.Combine( projectDirectory, "RunTest.ps1" ),
            """
            param([string]$Platform, [string]$ResultsDirectory)

            $data = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'x64/data.txt') -Raw
            Set-Content -LiteralPath (Join-Path $ResultsDirectory 'arguments.txt') -Value "$Platform $($data.Trim())"
            Set-Content -LiteralPath (Join-Path $ResultsDirectory 'gtest-x64.xml') -Value '<testsuites tests="1" />'
            exit 0
            """ );

        var engDirectory = Path.Combine( this._directory.Path, "eng" );
        Directory.CreateDirectory( engDirectory );

        File.WriteAllText(
            Path.Combine( engDirectory, TestArchives.ScriptName ),
            ReadScript().Replace( "<TEST_RESULTS_PATH>", "artifacts/testResults", StringComparison.Ordinal ),
            new UTF8Encoding( false ) );

        var (exitCode, output) = Build( Path.Combine( projectDirectory, "Native.csproj" ) );
        Assert.True( exitCode == 0, output );

        var archive = Path.Combine( this.ArchivesDirectory, $"Native.{_targetFramework}.zip" );
        var manifest = ReadManifest( archive );
        Assert.Contains( "Kind = 'ps1'", manifest, StringComparison.Ordinal );
        Assert.Contains( "Entry = 'RunTest.ps1'", manifest, StringComparison.Ordinal );
        Assert.Contains( "ReportType = 'gtest'", manifest, StringComparison.Ordinal );

        // The archive holds the script and the files of the project, not a publication of the project.
        using ( var zip = ZipFile.OpenRead( archive ) )
        {
            Assert.NotNull( zip.GetEntry( "RunTest.ps1" ) );
            Assert.NotNull( zip.GetEntry( "x64/data.txt" ) );

            // A file without ArchivePath is at the root of the archive.
            Assert.NotNull( zip.GetEntry( "readme.txt" ) );
            Assert.Null( zip.GetEntry( "Native.dll" ) );
        }

        (exitCode, output) = this.RunTests( "-Platform win-x64", [] );
        Assert.True( exitCode == 0, output );
        Assert.Contains( "gtest-x64.xml' type='gtest']", output, StringComparison.Ordinal );
        Assert.Equal( "win-x64 native", File.ReadAllText( Path.Combine( this.ResultsDirectory, $"Native.{_targetFramework}", "arguments.txt" ) ).Trim() );
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
    /// A prepare script that fails, or an artifact that is missing, fails the archive without starting the application.
    /// </summary>
    [Fact]
    public void AFailedPreparationIsABuildProblem()
    {
        if ( DockerBuildScript.FindPowerShell() != "pwsh" )
        {
            return;
        }

        this.CreateRepository();

        var (exitCode, output) = this.RunTests( "", new Dictionary<string, string> { ["PROBE_PREPARE_FAILS"] = "1" } );
        Assert.Equal( 1, exitCode );
        Assert.Contains( "##teamcity[buildProblem", output, StringComparison.Ordinal );
        Assert.Contains( "The preparation failed.", output, StringComparison.Ordinal );
        Assert.False( File.Exists( Path.Combine( this.ResultsDirectory, $"Probe.{_targetFramework}", "arguments.txt" ) ) );

        File.Delete( Path.Combine( this._directory.Path, "artifacts", "publish", "Probe.1.0.txt" ) );
        (exitCode, output) = this.RunTests( "", [] );
        Assert.Equal( 1, exitCode );
        Assert.Contains( "The artifact 'artifacts/publish/Probe.*.txt'", output, StringComparison.Ordinal );
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
        this.WriteDirectoryBuildTargets();

        var project = this._directory.WriteFile(
            "Probe.csproj",
            $"""
             <Project Sdk="Microsoft.NET.Sdk">
               <PropertyGroup>
                 <OutputType>Exe</OutputType>
                 <TargetFramework>{_targetFramework}</TargetFramework>
                 <IsTestingPlatformApplication>true</IsTestingPlatformApplication>
               </PropertyGroup>
             </Project>
             """ );

        this._directory.WriteFile( "Program.cs", "return 0;" );

        var (exitCode, output) = Build( project );
        Assert.True( exitCode == 0, output );
        Assert.False( Directory.Exists( this.ArchivesDirectory ) );
    }

    /// <summary>
    /// The build of the solutions writes the archives when the product publishes them, and only then, so that a product
    /// without archives does not spend the time of a publication on every test project. On TeamCity, only the build of the
    /// configuration that TestArchivesSourceDependency names writes them, so that the builds that the test agents do not
    /// download from do not spend that time either. A value given on the command line is kept.
    /// </summary>
    [Fact]
    public void TheBuildWritesTheArchivesOfAProductThatPublishesThem()
    {
        var withArchives = new Product( MetalamaDependencies.V2026_1.Metalama ) { Solutions = [new DotNetSolution( "Tests.sln" ) { ContainsTestApplications = true }] };
        var withoutArchives = new Product( MetalamaDependencies.V2026_1.Metalama );

        Assert.Equal( "true", TestArchives.AddBuildProperties( withArchives, new BuildSettings(), false ).Properties["PublishTestArchive"] );
        Assert.False( TestArchives.AddBuildProperties( withoutArchives, new BuildSettings(), false ).Properties.ContainsKey( "PublishTestArchive" ) );

        // On TeamCity, only in the build of the source configuration, Public by default.
        Assert.False( TestArchives.AddBuildProperties( withArchives, this.Settings( BuildConfiguration.Release ), true ).Properties.ContainsKey( "PublishTestArchive" ) );
        Assert.Equal( "true", TestArchives.AddBuildProperties( withArchives, this.Settings( BuildConfiguration.Public ), true ).Properties["PublishTestArchive"] );

        var explicitSettings = new BuildSettings().WithAdditionalProperties(
            ImmutableDictionary<string, string>.Empty.Add( "PublishTestArchive", "false" ) );

        Assert.Equal( "false", TestArchives.AddBuildProperties( withArchives, explicitSettings, false ).Properties["PublishTestArchive"] );
    }

    /// <summary>
    /// An additional build configuration that publishes the archives must write them and publish them, which only the product
    /// can give it.
    /// </summary>
    [Fact]
    public void AnAdditionalSourceConfigurationMustPublishTheArchives()
    {
        Product CreateProduct( string arguments, string[]? rules )
            => new( MetalamaDependencies.V2026_1.Metalama )
            {
                Solutions = [new DotNetSolution( "Tests.sln" ) { ContainsTestApplications = true }],
                TestArchivesSourceDependency = new SnapshotDependency( "BuildArtifacts" ),
                AdditionalCiBuildConfigurations =
                [
                    new PowershellAdditionalCiBuildConfiguration( "BuildArtifacts", "Build artifacts", "Build.ps1", arguments ) { ArtifactRules = rules }
                ]
            };

        var console = new ConsoleHelper();

        Assert.True(
            TestArchives.TryValidateSource(
                CreateProduct( "build -p:PublishTestArchive=true", ["+:artifacts/tests/*.zip=>artifacts/tests"] ),
                console ) );

        Assert.False( TestArchives.TryValidateSource( CreateProduct( "build", ["+:artifacts/tests/*.zip=>artifacts/tests"] ), console ) );
        Assert.False( TestArchives.TryValidateSource( CreateProduct( "build -p:PublishTestArchive=true", null ), console ) );
    }

    private BuildSettings Settings( BuildConfiguration configuration )
    {
        var settings = new BuildSettings { BuildConfiguration = configuration };
        settings.Initialize( TestBuildContext.Create( this._directory.Path ) );

        return settings;
    }

    /// <summary>
    /// Only the product build configuration that TestArchivesSourceDependency names writes and publishes the archives. When
    /// it names an additional build configuration, no product build configuration does.
    /// </summary>
    [Fact]
    public void OnlyTheSourceConfigurationPublishesTheArchives()
    {
        var solutions = new Solution[] { new DotNetSolution( "Tests.sln" ) { ContainsTestApplications = true } };
        var fromPublic = new Product( MetalamaDependencies.V2026_1.Metalama ) { Solutions = solutions };
        var fromAdditional = new Product( MetalamaDependencies.V2026_1.Metalama )
        {
            Solutions = solutions, TestArchivesSourceDependency = new SnapshotDependency( "BuildArtifacts" )
        };

        Assert.True( TestArchives.IsSourceConfiguration( fromPublic, BuildConfiguration.Public ) );
        Assert.False( TestArchives.IsSourceConfiguration( fromPublic, BuildConfiguration.Release ) );
        Assert.False( TestArchives.IsSourceConfiguration( fromAdditional, BuildConfiguration.Public ) );
        Assert.False( TestArchives.IsSourceConfiguration( new Product( MetalamaDependencies.V2026_1.Metalama ), BuildConfiguration.Public ) );
    }

    public void Dispose() => this._directory.Dispose();
}
