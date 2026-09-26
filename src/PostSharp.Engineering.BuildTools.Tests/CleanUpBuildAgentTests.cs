// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using PostSharp.Engineering.BuildTools.Build.MSBuild;
using PostSharp.Engineering.BuildTools.Build.Model;
using PostSharp.Engineering.BuildTools.ContinuousIntegration;
using PostSharp.Engineering.BuildTools.ContinuousIntegration.Model;
using PostSharp.Engineering.BuildTools.ContinuousIntegration.TeamCity;
using PostSharp.Engineering.BuildTools.ContinuousIntegration.TeamCity.BuildSteps;
using PostSharp.Engineering.BuildTools.Build;
using PostSharp.Engineering.BuildTools.Dependencies.Definitions;
using PostSharp.Engineering.BuildTools.Docker;
using PostSharp.Engineering.BuildTools.Utilities;
using System;
using System.IO;
using System.Text;
using Xunit;

namespace PostSharp.Engineering.BuildTools.Tests;

/// <summary>
/// <c>CleanUpBuildAgent.ps1</c>, the generated script that deletes the stale packages of the product before a build and
/// removes what the build leaves on the agent afterwards. The shipped script is what is run here, with the pattern list
/// substituted as <c>generate-scripts</c> substitutes it.
/// </summary>
/// <remarks>
/// <para>
/// Every continuous-integration build of a product carries the same public package version, and NuGet never extracts a
/// version already in the global packages folder. A build that does not delete the previous copy first restores that
/// copy instead of the artifacts it depends on, so a test can pass against code that no longer exists.
/// </para>
/// <para>
/// The step this replaced removed nothing on every Linux agent and reported success, because a container runs as root
/// while the agent does not and the cache is a bind mount, while <c>-ErrorAction SilentlyContinue</c> hid the refusal.
/// Two of the tests below stand in for that state by shadowing the cmdlet that would refuse: the state itself cannot be
/// created by a test that is not root, and a test that needed root would not run anywhere it matters.
/// </para>
/// </remarks>
public sealed class CleanUpBuildAgentTests : IDisposable
{
    private readonly string _directory = Path.Combine( Path.GetTempPath(), $"cleanup-agent-{Guid.NewGuid():N}" );
    private readonly string _cache;
    private readonly string _script;

    /// <summary>
    /// The patterns of a product: the package itself, and everything under its identifier.
    /// </summary>
    private const string _patterns = "'testproduct', 'testproduct.*'";

    public CleanUpBuildAgentTests()
    {
        this._cache = Path.Combine( this._directory, "packages" );
        this._script = Path.Combine( this._directory, "CleanUpBuildAgent.ps1" );

        foreach ( var package in new[] { "testproduct", "testproduct.core", "other.package" } )
        {
            Directory.CreateDirectory( Path.Combine( this._cache, package, "1.0" ) );
            File.WriteAllText( Path.Combine( this._cache, package, "1.0", "p.nupkg" ), "stale" );
        }

        // The shipped script, with the one thing generate-scripts puts into it.
        File.WriteAllText(
            this._script,
            ReadResource( "CleanUpBuildAgent.ps1" ).Replace( "<NUGET_CACHE_PACKAGE_PATTERNS>", _patterns, StringComparison.Ordinal ),
            new UTF8Encoding( false ) );
    }

    private static string ReadResource( string fileName )
    {
        var name = $"PostSharp.Engineering.BuildTools.Resources.{fileName}";

        using var stream = typeof(EnvironmentVariableNames).Assembly.GetManifestResourceStream( name )
                           ?? throw new InvalidOperationException( $"Cannot find the embedded resource '{name}'." );

        using var reader = new StreamReader( stream );

        return reader.ReadToEnd();
    }

    private bool Exists( string package ) => Directory.Exists( Path.Combine( this._cache, package ) );

    /// <summary>
    /// Runs the script with the given arguments, through a wrapper that may first define stubs. A function defined by
    /// the caller shadows a cmdlet or an executable for the script it invokes, which is how the cases that need root or
    /// a container engine are reached from a test that has neither.
    /// </summary>
    private (int ExitCode, string Output) Run( string powerShell, string arguments, string prologue = "" )
    {
        var wrapper = $"""
                       {prologue}

                       & '{DockerBuildScript.Escape( this._script )}' {arguments}

                       exit $LASTEXITCODE
                       """;

        return DockerBuildScript.TryRun( powerShell, wrapper );
    }

    private string CacheArgument => $"-NuGetCacheDirectory '{DockerBuildScript.Escape( this._cache )}'";

    [Fact]
    public void RemovesThePackagesOfTheProductAndNothingElse()
    {
        var powerShell = DockerBuildScript.FindPowerShell();

        if ( powerShell == null )
        {
            return;
        }

        var (exitCode, output) = this.Run( powerShell, this.CacheArgument );

        Assert.Equal( 0, exitCode );
        Assert.False( this.Exists( "testproduct" ), $"The product package was not removed:\n{output}" );
        Assert.False( this.Exists( "testproduct.core" ), $"A package of the product was not removed:\n{output}" );

        // A third-party package is not the product's to delete, and deleting it would make every build restore the whole
        // dependency graph over the network.
        Assert.True( this.Exists( "other.package" ), $"An unrelated package was removed:\n{output}" );

        Assert.Contains( "Removed 2 package directory(ies) and 2 file(s)", output, StringComparison.Ordinal );
    }

    /// <summary>
    /// The defect itself: a directory that is still there after the removal fails the build, so that nothing goes on to
    /// restore it.
    /// </summary>
    [Fact]
    public void FailsWhenADirectoryCannotBeRemoved()
    {
        var powerShell = DockerBuildScript.FindPowerShell();

        if ( powerShell == null )
        {
            return;
        }

        var (exitCode, output) = this.Run( powerShell, this.CacheArgument, _removeItemDoesNothing );

        Assert.NotEqual( 0, exitCode );
        Assert.Contains( "would restore stale packages", output, StringComparison.Ordinal );
        Assert.Contains( "testproduct", output, StringComparison.Ordinal );
    }

    /// <summary>
    /// The distinction the whole design rests on, against a directory a container of an earlier build really did leave.
    /// Three callers, three answers: the agent of a build that restores in a container names it and passes, because the
    /// container will remove it and failing would mean no build could ever run again on that agent; the agent of a build
    /// that restores by itself fails, having no container to defer to; and the container fails, being root and therefore
    /// the last thing that could have removed it.
    /// </summary>
    /// <remarks>
    /// This needs a real container and a real agent account, so it runs on a development machine with WSL and reports
    /// success where there is none. Nothing about it can be faked: the ownership test cannot be shadowed, the script
    /// defining it itself, and a test that is not root cannot produce a directory belonging to another account.
    /// </remarks>
    [Fact]
    public void ADirectoryOfAnotherAccountIsTheContainersToRemove()
    {
        var script = WslShell.ToWslPath( this._script );

        var shell = $$"""
                     # Proof that a shell ran this at all, for a development machine that has wsl.exe but no
                     # distribution behind it: there wsl.exe starts, prints a message of its own and exits without
                     # running anything, which no exit code tells apart from a real result.
                     echo WSL-RAN-THE-SCRIPT

                     set -e
                     command -v docker >/dev/null || exit 111
                     command -v pwsh >/dev/null || exit 111

                     # On the Linux file system, not under /mnt: a Windows drive shows every file as belonging to the
                     # WSL user whatever the container did, which is the very thing under test.
                     CACHE=$(mktemp -d /tmp/cleanup-agent-XXXXXX)
                     mkdir -p "$CACHE/other.package/1.0"

                     # An earlier build's container extracts a package as root, as it does on the Linux agent.
                     docker run --rm -v "$CACHE:$CACHE" busybox sh -c "mkdir -p $CACHE/testproduct/1.0 && echo stale > $CACHE/testproduct/1.0/p.nupkg" >/dev/null 2>&1

                     echo "OWNER=$(stat -c %u "$CACHE/testproduct") RUNNING-AS=$(id -u)"

                     # The exit code of each run is the subject, so a non-zero one must not end this script.
                     set +e

                     echo "--- as the agent of a build that restores in a container ---"
                     pwsh -NoProfile -File "{{script}}" -NuGetCacheDirectory "$CACHE" -DeferToContainer
                     echo "DEFERRING-EXIT=$?"

                     echo "--- as the agent of a build that restores on the agent ---"
                     pwsh -NoProfile -File "{{script}}" -NuGetCacheDirectory "$CACHE"
                     echo "NATIVE-EXIT=$?"

                     echo "--- as the container ---"
                     pwsh -NoProfile -File "{{script}}" -NuGetCacheDirectory "$CACHE" -InContainer
                     echo "CONTAINER-EXIT=$?"

                     docker run --rm -v "$CACHE:$CACHE" busybox rm -rf "$CACHE" >/dev/null 2>&1 || true
                     """;

        if ( !WslShell.TryRun( shell, out var exitCode, out var output )
             || !output.Contains( "WSL-RAN-THE-SCRIPT", StringComparison.Ordinal )
             || exitCode == 111 )
        {
            // No wsl.exe, no distribution behind it, or no engine and no PowerShell inside that distribution. A
            // build agent is such a machine: it runs one engine natively, and WSL is installed with no distribution.
            return;
        }

        Assert.Contains( "OWNER=0", output, StringComparison.Ordinal );
        Assert.DoesNotContain( "RUNNING-AS=0", output, StringComparison.Ordinal );

        // The agent cannot unlink it, says so, and passes: the container that restores it next is what removes it.
        Assert.Contains( "DEFERRING-EXIT=0", output, StringComparison.Ordinal );
        Assert.Contains( "belong to another account", output, StringComparison.Ordinal );

        // A build that restores on the agent itself has no such container, so the same directory fails it. Deferring
        // there would let the build restore the very package this deletes.
        Assert.Contains( "NATIVE-EXIT=1", output, StringComparison.Ordinal );

        // The container has no one to hand it to, so the same directory fails the build there.
        Assert.Contains( "CONTAINER-EXIT=1", output, StringComparison.Ordinal );
        Assert.Contains( "would restore stale packages", output, StringComparison.Ordinal );
    }

    /// <summary>
    /// An empty cache is the normal state of a freshly cleaned agent. A clean-up that failed on it would fail every
    /// build there.
    /// </summary>
    [Fact]
    public void SucceedsWhenThereIsNothingToRemove()
    {
        var powerShell = DockerBuildScript.FindPowerShell();

        if ( powerShell == null )
        {
            return;
        }

        Directory.Delete( Path.Combine( this._cache, "testproduct" ), true );
        Directory.Delete( Path.Combine( this._cache, "testproduct.core" ), true );

        var (exitCode, output) = this.Run( powerShell, this.CacheArgument );

        Assert.Equal( 0, exitCode );
        Assert.Contains( "Removed 0 package directory(ies)", output, StringComparison.Ordinal );
    }

    /// <summary>
    /// The first build on a new agent has no cache folder at all.
    /// </summary>
    [Fact]
    public void SucceedsWhenTheCacheDoesNotExist()
    {
        var powerShell = DockerBuildScript.FindPowerShell();

        if ( powerShell == null )
        {
            return;
        }

        var missing = Path.Combine( this._directory, "does-not-exist" );

        var (exitCode, output) = this.Run( powerShell, $"-NuGetCacheDirectory '{DockerBuildScript.Escape( missing )}'" );

        Assert.Equal( 0, exitCode );
        Assert.Contains( "does not exist", output, StringComparison.Ordinal );
    }

    /// <summary>
    /// A test container runs the removal in its own shell, because a test image is chosen for the tool chain under test
    /// and is not required to carry PowerShell. <c>DockerBuild.ps1</c> asks this script for the command so that the list
    /// of packages and the way they are deleted stay in one place.
    /// </summary>
    [Fact]
    public void TheTestCommandPrefixQuotesTheDirectoryAndLeavesThePatternToTheShell()
    {
        var powerShell = DockerBuildScript.FindPowerShell();

        if ( powerShell == null )
        {
            return;
        }

        // The directory is quoted and the pattern is not: the shell has to expand the pattern. `rm -rf` then gives the
        // semantics wanted at both ends -- a pattern matching nothing is not a failure, and a directory it cannot remove
        // makes it exit non-zero, so the test command after the `&&` never runs.
        Assert.Equal(
            "rm -rf '/opt/teamcity-home/.nuget/packages'/testproduct "
            + "'/opt/teamcity-home/.nuget/packages'/testproduct.* && ",
            this.Emit( powerShell, "/opt/teamcity-home/.nuget/packages" ) );

        // An apostrophe ends the quoting, is escaped, and the quoting resumes -- the POSIX spelling, a single-quoted
        // word being unable to hold one. A home directory may legitimately contain an apostrophe.
        Assert.Equal(
            "rm -rf '/home/o'\\''brien/.nuget/packages'/testproduct "
            + "'/home/o'\\''brien/.nuget/packages'/testproduct.* && ",
            this.Emit( powerShell, "/home/o'brien/.nuget/packages" ) );
    }

    private string Emit( string powerShell, string cache )
    {
        // Written rather than returned, because the trailing space is part of the value and the host strips it.
        var output = Path.Combine( this._directory, $"prefix-{Guid.NewGuid():N}.txt" );

        var (exitCode, log) = this.Run(
            powerShell,
            $"-EmitTestCommandPrefix -NuGetCacheDirectory '{DockerBuildScript.Escape( cache )}' "
            + $"| Set-Content -Path '{DockerBuildScript.Escape( output )}' -NoNewline" );

        Assert.Equal( 0, exitCode );
        Assert.True( File.Exists( output ), log );

        return File.ReadAllText( output );
    }

    /// <summary>
    /// After the build: the containers go first, so that nothing is still writing when the agent's command starts
    /// reclaiming what they wrote. Reclaiming ownership while a container is still writing would leave whatever it wrote
    /// afterwards owned by root again, which is the state the command exists to prevent.
    /// </summary>
    [Fact]
    public void TheContainersOfTheBuildAreRemovedBeforeTheAgentScriptRuns()
    {
        var powerShell = DockerBuildScript.FindPowerShell();

        if ( powerShell == null )
        {
            return;
        }

        var log = Path.Combine( this._directory, "order.txt" );
        var escapedLog = DockerBuildScript.Escape( log );

        // docker is shadowed by a function, which is enough to observe the order without an engine: a function takes
        // precedence over an executable for the script that calls it.
        var prologue = $$"""
                         function docker
                         {
                             Add-Content -Path '{{escapedLog}}' -Value "docker $args"
                             if ( $args -contains 'ps' ) { return 'stub-container-id' }
                         }

                         $env:BUILDAGENT_CLEANUP_SCRIPT = "Add-Content -Path '{{escapedLog}}' -Value agent-script"
                         """;

        var (exitCode, output) = this.Run( powerShell, "-After -BuildLabel TheBuild_42", prologue );

        Assert.Equal( 0, exitCode );

        var lines = File.ReadAllLines( log );
        var removal = Array.FindIndex( lines, l => l.StartsWith( "docker rm", StringComparison.Ordinal ) );
        var agentScript = Array.IndexOf( lines, "agent-script" );

        Assert.True( removal >= 0, $"The containers of the build are no longer removed:\n{output}" );
        Assert.True( agentScript >= 0, $"The agent's cleanup script is no longer run:\n{output}" );
        Assert.True( removal < agentScript, $"The agent's cleanup script ran before the containers were removed:\n{output}" );

        // Scoped to this build. Removing every container of the agent would take out a build running beside this one.
        Assert.Contains( lines, l => l.Contains( "label=postsharp.build=TheBuild_42", StringComparison.Ordinal ) );
    }

    /// <summary>
    /// Without a label nothing is removed, rather than everything: the containers of a build running beside this one are
    /// not this build's to kill.
    /// </summary>
    [Fact]
    public void NoContainerIsRemovedWithoutALabel()
    {
        var powerShell = DockerBuildScript.FindPowerShell();

        if ( powerShell == null )
        {
            return;
        }

        var (exitCode, output) = this.Run( powerShell, "-After", "function docker { throw 'docker must not be called.' }" );

        Assert.Equal( 0, exitCode );
        Assert.Contains( "No build label was given", output, StringComparison.Ordinal );
    }

    /// <summary>
    /// The post-build clean-up is hygiene: a build that would otherwise pass is not failed by it. It is also the step
    /// that runs when the build has already failed, so failing there would replace the real reason with this one.
    /// </summary>
    [Fact]
    public void AFailingAgentScriptDoesNotFailTheBuild()
    {
        var powerShell = DockerBuildScript.FindPowerShell();

        if ( powerShell == null )
        {
            return;
        }

        var (exitCode, output) = this.Run(
            powerShell,
            "-After",
            "$env:BUILDAGENT_CLEANUP_SCRIPT = 'this-command-does-not-exist'" );

        Assert.Equal( 0, exitCode );
        Assert.Contains( "The agent cleanup script failed", output, StringComparison.Ordinal );
    }

    /// <summary>
    /// The post-build clean-up removes containers and runs a command of the host, neither of which is a container's to
    /// do. A caller that asks for both has misunderstood something, and is told so rather than getting half of it.
    /// </summary>
    [Fact]
    public void AfterIsRefusedInAContainer()
    {
        var powerShell = DockerBuildScript.FindPowerShell();

        if ( powerShell == null )
        {
            return;
        }

        var (exitCode, output) = this.Run( powerShell, "-After -InContainer" );

        Assert.NotEqual( 0, exitCode );
        Assert.Contains( "cannot be combined", output, StringComparison.Ordinal );
    }

    /// <summary>
    /// The script is generated into the engineering directory of every product, with the patterns of that product, and
    /// both the TeamCity steps and <c>DockerBuild.ps1</c> reach it there.
    /// </summary>
    [Fact]
    public void TheScriptIsGeneratedWithThePatternsOfTheProduct()
    {
        // Reading the dependencies goes through MSBuild, whose assemblies are located at run time.
        MSBuildHelper.InitializeLocator();

        using var directory = new TempDirectory();

        var product = new Product( MetalamaDependencies.V2026_1.Metalama )
        {
            GenerateTeamCitySettings = false,
            GenerateDockerfiles = false,
            OverriddenBuildAgentRequirements = new ContainerRequirements( ContainerHostKind.Windows )
        };

        Assert.True( GenerateScriptsCommand.Execute( TestBuildContext.Create( directory.Path, product ), new CommonCommandSettings() ) );

        var generated = Path.Combine( directory.Path, product.EngineeringDirectory, "CleanUpBuildAgent.ps1" );
        Assert.True( File.Exists( generated ), $"The script was not generated at {generated}." );

        var text = File.ReadAllText( generated );
        Assert.DoesNotContain( "<NUGET_CACHE_PACKAGE_PATTERNS>", text, StringComparison.Ordinal );

        var patterns = NuGetCachePatterns.GetPatterns( product );
        Assert.NotEmpty( patterns );

        var line = Array.Find(
            text.ReplaceLineEndings( "\n" ).Split( '\n' ),
            l => l.StartsWith( "$NuGetCachePackagePatterns = @(", StringComparison.Ordinal ) );

        Assert.NotNull( line );

        foreach ( var pattern in patterns )
        {
            Assert.Contains( $"'{pattern}'", line, StringComparison.Ordinal );
        }
    }

    /// <summary>
    /// The whole wiring, through the command a repository actually runs: both steps of the generated TeamCity settings
    /// name the generated script, at the engineering directory of that product rather than at a hardcoded <c>eng</c> --
    /// Metalama's is <c>eng-Metalama</c> -- and the settings carry none of what the script does.
    /// </summary>
    [Fact]
    public void TheStepsAreWiredToTheGeneratedScript()
    {
        MSBuildHelper.InitializeLocator();

        using var directory = new TempDirectory();

        // Metalama.Compiler, because its engineering directory is 'eng-Metalama': a path built from the product rather
        // than a hardcoded 'eng' is the thing worth asserting, and only such a product can show the difference.
        var product = new Product( MetalamaDependencies.V2026_1.MetalamaCompiler )
        {
            GenerateDockerfiles = false, OverriddenBuildAgentRequirements = new ContainerRequirements( ContainerHostKind.Windows )
        };

        Assert.True( GenerateScriptsCommand.Execute( TestBuildContext.Create( directory.Path, product ), new CommonCommandSettings() ) );

        var settings = File.ReadAllText( Path.Combine( directory.Path, ".teamcity", "settings.kts" ) );

        Assert.NotEqual( "eng", product.EngineeringDirectory );
        Assert.Contains( $"path = \"{product.EngineeringDirectory}/CleanUpBuildAgent.ps1\"", settings, StringComparison.Ordinal );
        Assert.Contains( "-After -BuildLabel", settings, StringComparison.Ordinal );
        Assert.Contains( "id = \"CleanNuGetCache\"", settings, StringComparison.Ordinal );

        // The point of the script: none of what it does is in the settings any more.
        Assert.DoesNotContain( "BUILDAGENT_CLEANUP_SCRIPT", settings, StringComparison.Ordinal );
        Assert.DoesNotContain( "Remove-Item", settings, StringComparison.Ordinal );
    }

    /// <summary>
    /// A build only defers a directory it cannot delete to a container when it has one. The generator is what knows
    /// that, so it is what passes <c>-DeferToContainer</c>: a build that restores on the agent itself must fail over
    /// such a directory instead, there being no later container to remove it before NuGet reads it.
    /// </summary>
    [Fact]
    public void OnlyABuildThatRunsAContainerDefersToOne()
    {
        Assert.Contains( "-DeferToContainer", GenerateSteps( startsContainers: true ), StringComparison.Ordinal );
        Assert.DoesNotContain( "-DeferToContainer", GenerateSteps( startsContainers: false ), StringComparison.Ordinal );
    }

    private static string GenerateSteps( bool startsContainers )
    {
        var configuration = new TeamCityBuildConfiguration(
            "Build",
            "Build",
            "develop/2026.1",
            "Vcs",
            BuildAgentRequirements.Empty )
        {
            BuildSteps = [new PowerShellCommandBuildStep( "Step", "A step that restores", "./Build.ps1 build", null )],
            StartsContainers = startsContainers,
            CleanUpBuildAgentScriptPath = "eng/CleanUpBuildAgent.ps1"
        };

        var writer = new StringWriter();
        configuration.GenerateTeamcityCode( writer );

        return writer.ToString();
    }

    /// <summary>
    /// A build container runs the script through <c>Init.g.ps1</c>, which it executes before the build it was started
    /// for. The exit code has to be tested there, because <c>exit</c> inside a script invoked with <c>&amp;</c> ends that
    /// script alone: without the test the clean-up could report that it had left a stale package behind and the build
    /// would restore it anyway.
    /// </summary>
    [Fact]
    public void ABuildContainerRunsTheScriptBeforeTheBuildAndStopsOnFailure()
    {
        var script = DockerBuildScript.Text;

        // The call that goes into Init.g.ps1, and the test of its exit code.
        Assert.Contains( "-InContainer", script, StringComparison.Ordinal );
        Assert.Contains( "CleanUpBuildAgent.ps1", script, StringComparison.Ordinal );

        // The guard on Init.g.ps1 itself, in the command that invokes it.
        Assert.Contains(
            "$LASTEXITCODE = 0; & '$containerInitScript'; if ( $LASTEXITCODE -ne 0 ) { exit $LASTEXITCODE }; ",
            script.Replace( "`$", "$", StringComparison.Ordinal ),
            StringComparison.Ordinal );
    }

    /// <summary>
    /// The PowerShell behaviour the assertion above exists for, measured rather than asserted from memory.
    /// </summary>
    [Fact]
    public void AnExitInsideAnInvokedScriptDoesNotStopTheCaller()
    {
        var powerShell = DockerBuildScript.FindPowerShell();

        if ( powerShell == null )
        {
            return;
        }

        var child = Path.Combine( this._directory, "child.ps1" );
        File.WriteAllText( child, "exit 3\n", new UTF8Encoding( false ) );

        var invocation = $"& '{DockerBuildScript.Escape( child )}'";

        var (unguarded, unguardedOutput) = DockerBuildScript.TryRun( powerShell, $"{invocation}; Write-Output 'BUILD-RAN'; exit 0" );

        Assert.Equal( 0, unguarded );
        Assert.Contains( "BUILD-RAN", unguardedOutput, StringComparison.Ordinal );

        var (guarded, guardedOutput) = DockerBuildScript.TryRun(
            powerShell,
            $"$LASTEXITCODE = 0; {invocation}; if ( $LASTEXITCODE -ne 0 ) {{ exit $LASTEXITCODE }}; Write-Output 'BUILD-RAN'; exit 0" );

        Assert.Equal( 3, guarded );
        Assert.DoesNotContain( "BUILD-RAN", guardedOutput, StringComparison.Ordinal );
    }

    /// <summary>
    /// Shadows the cmdlet that refuses to unlink a root-owned directory on an agent, so that the survivor paths can be
    /// reached by a test that is not root.
    /// </summary>
    private const string _removeItemDoesNothing = """
                                                  function Remove-Item
                                                  {
                                                      param([string]$LiteralPath, [switch]$Recurse, [switch]$Force, $ErrorAction)
                                                  }

                                                  """;

    public void Dispose()
    {
        try
        {
            Directory.Delete( this._directory, true );
        }
        catch ( IOException )
        {
            // A leftover temporary directory is not worth failing a test over, and DirectoryNotFoundException is one of
            // these: a test may legitimately have removed the whole tree.
        }
    }
}
