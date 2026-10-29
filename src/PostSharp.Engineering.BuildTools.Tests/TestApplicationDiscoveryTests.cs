// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using PostSharp.Engineering.BuildTools.Build;
using PostSharp.Engineering.BuildTools.Build.Model;
using PostSharp.Engineering.BuildTools.Build.MSBuild;
using PostSharp.Engineering.BuildTools.Build.Solutions;
using PostSharp.Engineering.BuildTools.Build.Testing;
using PostSharp.Engineering.BuildTools.ContinuousIntegration;
using PostSharp.Engineering.BuildTools.ContinuousIntegration.Model;
using PostSharp.Engineering.BuildTools.Dependencies.Definitions;
using PostSharp.Engineering.BuildTools.Docker;
using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using Xunit;

namespace PostSharp.Engineering.BuildTools.Tests;

/// <summary>
/// Evaluates projects with <see cref="TestApplicationDiscovery"/>, without restoring or building them.
/// </summary>
public sealed class TestApplicationDiscoveryTests : IDisposable
{
    private readonly TempDirectory _directory = new();

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

    public TestApplicationDiscoveryTests()
    {
        // The targets file is imported after the body of the projects, as the documentation says.
        File.WriteAllText(
            Path.Combine( this._directory.Path, "Directory.Build.targets" ),
            $"""
             <Project>
               <Import Project="{Path.Combine( SdkDirectory, "TestArchive.targets" )}" />
             </Project>
             """ );
    }

    private void CreateProject( string name, string body )
    {
        var directory = Path.Combine( this._directory.Path, name );
        Directory.CreateDirectory( directory );

        File.WriteAllText(
            Path.Combine( directory, $"{name}.csproj" ),
            $"""
             <Project Sdk="Microsoft.NET.Sdk">
               {body}
             </Project>
             """ );
    }

    private void WriteSolution( string[] projects )
    {
        var lines = projects.SelectMany( p => new[]
        {
            $"Project(\"{{9A19103F-16F7-4668-BE54-9A1E7A4F7556}}\") = \"{p}\", \"{p}\\{p}.csproj\", \"{{{Guid.NewGuid().ToString().ToUpperInvariant()}}}\"",
            "EndProject"
        } );

        File.WriteAllLines(
            Path.Combine( this._directory.Path, "Probe.sln" ),
            ["Microsoft Visual Studio Solution File, Format Version 12.00", .. lines, "Global", "EndGlobal"] );
    }

    private bool Discover( string[] projects, out ImmutableArray<TestApplication> applications )
    {
        this.WriteSolution( projects );

        var product = new Product( MetalamaDependencies.V2026_1.Metalama )
        {
            Solutions =
            [
                new DotNetSolution( "Probe.sln" ) { ContainsTestApplications = true },

                // A solution that does not declare test applications is not read, even when its projects are some.
                new DotNetSolution( "Other.sln" )
            ]
        };

        MSBuildHelper.InitializeLocator();

        return TestApplicationDiscovery.TryDiscover( TestBuildContext.Create( this._directory.Path, product ), BuildConfiguration.Debug, out applications );
    }

    [Fact]
    public void TheApplicationsAreReadFromTheEvaluationOfEachTargetFramework()
    {
        // The markers that a project sets itself, because the one of the test frameworks needs a restore.
        this.CreateProject(
            "Xunit",
            """
            <PropertyGroup>
              <OutputType>Exe</OutputType>
              <TargetFrameworks>net8.0;net48;netstandard2.0</TargetFrameworks>
              <UseMicrosoftTestingPlatformRunner Condition="'$(TargetFramework)' != 'netstandard2.0'">true</UseMicrosoftTestingPlatformRunner>
              <TestApplicationRunAlone>true</TestApplicationRunAlone>
            </PropertyGroup>
            <ItemGroup>
              <TestApplicationTag Include="TimeSensitive" />
            </ItemGroup>
            """ );

        this.CreateProject(
            "MSTest",
            """
            <PropertyGroup>
              <OutputType>Exe</OutputType>
              <TargetFramework>net8.0-windows</TargetFramework>
              <EnableWindowsTargeting>true</EnableWindowsTargeting>
              <EnableMSTestRunner>true</EnableMSTestRunner>
              <TestApplicationSkip>Not today</TestApplicationSkip>
              <TestApplicationArtifacts>artifacts/publish/public/Product.*.nupkg; artifacts\other\Tool.zip</TestApplicationArtifacts>
            </PropertyGroup>
            """ );

        // A project that references a test project receives the props of its test framework, and says it is not one.
        this.CreateProject(
            "Manual",
            """
            <PropertyGroup>
              <OutputType>Exe</OutputType>
              <TargetFramework>net8.0</TargetFramework>
              <UseMicrosoftTestingPlatformRunner>true</UseMicrosoftTestingPlatformRunner>
              <IsTestingPlatformApplication>false</IsTestingPlatformApplication>
            </PropertyGroup>
            """ );

        // An application built for one processor architecture. Windows on ARM64 runs an x86 application, and not the
        // reverse.
        this.CreateProject(
            "Arm64",
            """
            <PropertyGroup>
              <OutputType>Exe</OutputType>
              <TargetFramework>net48</TargetFramework>
              <RuntimeIdentifier>win-arm64</RuntimeIdentifier>
              <UseMicrosoftTestingPlatformRunner>true</UseMicrosoftTestingPlatformRunner>
            </PropertyGroup>
            """ );

        this.CreateProject(
            "X86",
            """
            <PropertyGroup>
              <OutputType>Exe</OutputType>
              <TargetFramework>net48</TargetFramework>
              <RuntimeIdentifier>win-x86</RuntimeIdentifier>
              <UseMicrosoftTestingPlatformRunner>true</UseMicrosoftTestingPlatformRunner>
            </PropertyGroup>
            """ );

        Assert.True( this.Discover( ["Xunit", "MSTest", "Manual", "Arm64", "X86"], out var applications ) );

        Assert.Equal( ["win-arm64"], applications.Single( a => a.ArchiveName == "Arm64.net48.win-arm64" ).Platforms.ToArray() );
        Assert.Equal( ["win-x64", "win-arm64"], applications.Single( a => a.ArchiveName == "X86.net48.win-x86" ).Platforms.ToArray() );

        Assert.Equal(
            ["Arm64.net48.win-arm64", "MSTest.net8.0-windows", "X86.net48.win-x86", "Xunit.net48", "Xunit.net8.0"],
            applications.Select( a => a.ArchiveName ).Order( StringComparer.Ordinal ).ToArray() );

        var net8 = applications.Single( a => a.ArchiveName == "Xunit.net8.0" );
        Assert.Equal( ["win-x64", "win-arm64", "linux-x64", "linux-arm64", "osx-x64", "osx-arm64"], net8.Platforms.ToArray() );
        Assert.Equal( ["TimeSensitive"], net8.Tags.ToArray() );
        Assert.True( net8.RunAlone );
        Assert.Null( net8.Skip );

        // A .NET Framework application runs on Windows, and its default runtime identifier does not name its archive.
        Assert.Equal( ["win-x64", "win-arm64"], applications.Single( a => a.ArchiveName == "Xunit.net48" ).Platforms.ToArray() );

        // The restore graph names the target of a .NET Framework application by this moniker (see TestArchivePackagesTargetTests).
        Assert.Equal( ".NETFramework,Version=v4.8", applications.Single( a => a.ArchiveName == "Xunit.net48" ).TargetFrameworkMoniker );
        Assert.Equal( ".NETCoreApp,Version=v8.0", net8.TargetFrameworkMoniker );

        var windows = applications.Single( a => a.ArchiveName == "MSTest.net8.0-windows" );
        Assert.Equal( ["win-x64", "win-arm64"], windows.Platforms.ToArray() );
        Assert.Equal( "Not today", windows.Skip );

        // The wildcards of the artifacts are kept, because the artifacts do not exist when generate-scripts runs.
        Assert.Equal( ["artifacts/publish/public/Product.*.nupkg", "artifacts/other/Tool.zip"], windows.Artifacts.ToArray() );
        Assert.Empty( net8.Artifacts );
    }

    /// <summary>
    /// An artifact is downloaded to its own path, so a path outside the repository, or with a wildcard in its directory,
    /// cannot be downloaded.
    /// </summary>
    [Theory]
    [InlineData( "/artifacts/Product.nupkg" )]
    [InlineData( "../artifacts/Product.nupkg" )]
    [InlineData( "artifacts/*/Product.nupkg" )]
    [InlineData( "Product.nupkg" )]
    public void AnInvalidArtifactIsAnError( string artifact )
    {
        this.CreateProject(
            "Probe",
            $"""
             <PropertyGroup>
               <OutputType>Exe</OutputType>
               <TargetFramework>net8.0</TargetFramework>
               <UseMicrosoftTestingPlatformRunner>true</UseMicrosoftTestingPlatformRunner>
               <TestApplicationArtifacts>{artifact}</TestApplicationArtifacts>
             </PropertyGroup>
             """ );

        Assert.False( this.Discover( ["Probe"], out _ ) );
    }

    /// <summary>
    /// Two applications with one archive name would write one archive, and one of them would never be tested.
    /// </summary>
    /// <summary>
    /// A project that describes an archive of the ps1 kind is a test application, although no test framework marks it, so
    /// that generate-scripts plans a build configuration for it.
    /// </summary>
    [Fact]
    public void AProjectOfThePs1KindIsATestApplication()
    {
        this.CreateProject(
            "Native",
            """
            <PropertyGroup>
              <TargetFramework>net48</TargetFramework>
              <TestApplicationKind>ps1</TestApplicationKind>
              <TestApplicationEntry>RunTest.ps1</TestApplicationEntry>
              <TestApplicationPlatforms>win-x64;win-arm64</TestApplicationPlatforms>
            </PropertyGroup>
            """ );

        Assert.True( this.Discover( ["Native"], out var applications ) );

        var application = Assert.Single( applications );
        Assert.Equal( "Native.net48", application.ArchiveName );
        Assert.Equal( ["win-x64", "win-arm64"], application.Platforms.ToArray() );
    }

    [Fact]
    public void TwoApplicationsWithOneArchiveNameAreAnError()
    {
        const string body = """
                            <PropertyGroup>
                              <OutputType>Exe</OutputType>
                              <TargetFramework>net8.0</TargetFramework>
                              <AssemblyName>Shared</AssemblyName>
                              <UseMicrosoftTestingPlatformRunner>true</UseMicrosoftTestingPlatformRunner>
                            </PropertyGroup>
                            """;

        this.CreateProject( "First", body );
        this.CreateProject( "Second", body );

        Assert.False( this.Discover( ["First", "Second"], out var applications ) );
        Assert.Equal( 2, applications.Length );
    }

    /// <summary>
    /// With <c>--no-restore</c>, <c>generate-scripts</c> discovers the archives from the projects and takes their packages from
    /// the existing list, so it succeeds where a restore fails, as in a checkout whose product is not built.
    /// </summary>
    [Fact]
    public void WithoutRestoreThePackagesAreTakenFromTheList()
    {
        // The package does not exist, so a restore of the project would fail.
        const string body = """
                            <PropertyGroup>
                              <OutputType>Exe</OutputType>
                              <TargetFramework>net8.0</TargetFramework>
                              <UseMicrosoftTestingPlatformRunner>true</UseMicrosoftTestingPlatformRunner>
                            </PropertyGroup>
                            <ItemGroup>
                              <PackageReference Include="Product.NotBuilt" Version="1.0.0" />
                            </ItemGroup>
                            """;

        this.CreateProject( "Listed", body );
        this.CreateProject( "Added", body );
        this.WriteSolution( ["Listed", "Added"] );

        // The list names an archive that the projects no longer write, which is dropped.
        var listPath = Path.Combine( this._directory.Path, "eng", TestArchives.ListFileName );
        Directory.CreateDirectory( Path.GetDirectoryName( listPath )! );
        File.WriteAllLines( listPath, ["# Comment", "Listed.net8.0: Metalama/Product.NotBuilt Metalama/Other", "Removed.net8.0: Metalama/Product.NotBuilt"] );

        var product = new Product( MetalamaDependencies.V2026_1.Metalama )
        {
            GenerateDockerfiles = false,
            OverriddenBuildAgentRequirements = new ContainerRequirements( ContainerHostKind.Windows ),
            Solutions = [new DotNetSolution( "Probe.sln" ) { ContainsTestApplications = true }],
            TestAgents = [new TestAgent( "win-x64", "TestWinX64", "Windows x64", BuildAgentRequirements.Empty )],
            Configurations = Product.DefaultConfigurations.WithValue( BuildConfiguration.Release, c => c with { RunsTestArchives = true } )
        };

        MSBuildHelper.InitializeLocator();

        Assert.True(
            GenerateScriptsCommand.Execute( TestBuildContext.Create( this._directory.Path, product ), new CommonCommandSettings(), noRestore: true ) );

        Assert.True( TestArchives.TryReadList( TestBuildContext.Create( this._directory.Path, product ), out var packagesByArchive ) );

        Assert.Equal( ["Added.net8.0", "Listed.net8.0"], packagesByArchive.Keys.Order( StringComparer.Ordinal ).ToArray() );
        Assert.Equal( ["Metalama/Product.NotBuilt", "Metalama/Other"], packagesByArchive["Listed.net8.0"].ToArray() );
        Assert.Empty( packagesByArchive["Added.net8.0"] );
    }

    /// <summary>
    /// <c>generate-scripts</c> writes <c>global.json</c> before it evaluates the projects, because a project that imports
    /// PostSharp.Engineering.Sdk cannot be evaluated without it, as in a checkout where <c>prepare</c> has not run.
    /// </summary>
    [Fact]
    public void GenerateScriptsWritesGlobalJsonBeforeTheEvaluation()
    {
        this.CreateProject(
            "Probe",
            """
            <PropertyGroup>
              <TargetFramework>net8.0</TargetFramework>
              <OutputType>Exe</OutputType>
              <UseMicrosoftTestingPlatformRunner>true</UseMicrosoftTestingPlatformRunner>
            </PropertyGroup>
            """ );

        this.WriteSolution( ["Probe"] );

        var globalJsonPath = Path.Combine( this._directory.Path, "global.json" );
        var globalJsonExistedAtEvaluation = false;

        var product = new Product( MetalamaDependencies.V2026_1.Metalama )
        {
            GenerateDockerfiles = false,
            DotNetSdkVersion = new DotNetSdkVersion( "10.0.100" ) { RollForward = RollForwardPolicy.LatestMajor, AllowPrerelease = true },
            OverriddenBuildAgentRequirements = new ContainerRequirements( ContainerHostKind.Windows ),
            Solutions = [new DotNetSolution( "Probe.sln" ) { ContainsTestApplications = true }],
            TestAgents = [new TestAgent( "win-x64", "TestWinX64", "Windows x64", BuildAgentRequirements.Empty )],
            Configurations = Product.DefaultConfigurations.WithValue( BuildConfiguration.Release, c => c with { RunsTestArchives = true } )
        };

        product.TestDiscoveryStarting += _ => globalJsonExistedAtEvaluation = File.Exists( globalJsonPath );

        MSBuildHelper.InitializeLocator();

        Assert.True(
            GenerateScriptsCommand.Execute( TestBuildContext.Create( this._directory.Path, product ), new CommonCommandSettings(), noRestore: true ) );

        Assert.True( globalJsonExistedAtEvaluation );
        Assert.Contains( "PostSharp.Engineering.Sdk", File.ReadAllText( globalJsonPath ), StringComparison.Ordinal );
    }

    /// <summary>
    /// <c>generate-scripts</c> raises <see cref="Product.TestDiscoveryStarting"/> before it evaluates the projects, so that the
    /// product can write the files that the evaluation needs, as in a checkout where <c>prepare</c> has not run.
    /// </summary>
    [Fact]
    public void TheProductPreparesTheEvaluationOfTheTestProjects()
    {
        // The project imports a file that is not in the checkout.
        this.CreateProject(
            "Probe",
            """
            <Import Project="$(MSBuildThisFileDirectory)..\Generated.props" />
            <PropertyGroup>
              <OutputType>Exe</OutputType>
              <UseMicrosoftTestingPlatformRunner>true</UseMicrosoftTestingPlatformRunner>
            </PropertyGroup>
            """ );

        this.WriteSolution( ["Probe"] );

        Product CreateProduct()
            => new( MetalamaDependencies.V2026_1.Metalama )
            {
                GenerateDockerfiles = false,
                OverriddenBuildAgentRequirements = new ContainerRequirements( ContainerHostKind.Windows ),
                Solutions = [new DotNetSolution( "Probe.sln" ) { ContainsTestApplications = true }],
                TestAgents = [new TestAgent( "win-x64", "TestWinX64", "Windows x64", BuildAgentRequirements.Empty )],
                Configurations = Product.DefaultConfigurations.WithValue( BuildConfiguration.Release, c => c with { RunsTestArchives = true } )
            };

        MSBuildHelper.InitializeLocator();

        // A handler that fails fails the command.
        var failing = CreateProduct();
        failing.TestDiscoveryStarting += args => args.IsFailed = true;

        Assert.False(
            GenerateScriptsCommand.Execute( TestBuildContext.Create( this._directory.Path, failing ), new CommonCommandSettings(), noRestore: true ) );

        // The handler writes the file, which defines the target framework of the project.
        var product = CreateProduct();

        product.TestDiscoveryStarting += args => File.WriteAllText(
            Path.Combine( args.Context.RepoDirectory, "Generated.props" ),
            """
            <Project>
              <PropertyGroup>
                <TargetFramework>net8.0</TargetFramework>
              </PropertyGroup>
            </Project>
            """ );

        Assert.True(
            GenerateScriptsCommand.Execute( TestBuildContext.Create( this._directory.Path, product ), new CommonCommandSettings(), noRestore: true ) );

        Assert.True( TestArchives.TryReadList( TestBuildContext.Create( this._directory.Path, product ), out var packagesByArchive ) );
        Assert.Equal( ["Probe.net8.0"], packagesByArchive.Keys.ToArray() );
    }

    public void Dispose() => this._directory.Dispose();
}