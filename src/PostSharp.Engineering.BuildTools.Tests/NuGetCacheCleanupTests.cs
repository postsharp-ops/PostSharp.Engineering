// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using PostSharp.Engineering.BuildTools.Build.MSBuild;
using PostSharp.Engineering.BuildTools.Build.Model;
using PostSharp.Engineering.BuildTools.ContinuousIntegration;
using PostSharp.Engineering.BuildTools.ContinuousIntegration.TeamCity;
using PostSharp.Engineering.BuildTools.Dependencies.Definitions;
using PostSharp.Engineering.BuildTools.Docker;
using PostSharp.Engineering.BuildTools.Utilities;
using System;
using System.IO;
using Xunit;

namespace PostSharp.Engineering.BuildTools.Tests;

/// <summary>
/// The removal of the stale product packages from the NuGet cache before a build restores anything, and the reason it
/// has to happen inside the container on a Unix agent.
/// </summary>
/// <remarks>
/// <para>
/// Every continuous-integration build of a product carries the same public package version, and NuGet never extracts a
/// version already present in the global packages folder. A build that does not delete the previous copy first restores
/// that copy instead of the artifacts it depends on, so a test can pass against code that no longer exists.
/// </para>
/// <para>
/// The step that did this on the agent removed nothing on every Linux agent and reported success: a container runs as
/// root while the agent does not, the cache is a bind mount, and so the agent cannot unlink what a container of an
/// earlier build extracted -- while <c>-ErrorAction SilentlyContinue</c> hid the refusal, leaving a summary line that
/// said "Removed 0 package directory(ies)" and an exit code of 0. Thirty directories were found and none removed, on
/// every Linux configuration of the farm, which is why the deletion now happens where root is: in the container that is
/// about to restore.
/// </para>
/// </remarks>
public sealed class NuGetCacheCleanupTests : IDisposable
{
    private readonly string _cache = Path.Combine( Path.GetTempPath(), $"nuget-cleanup-{Guid.NewGuid():N}" );

    /// <summary>
    /// The patterns a product yields: the package itself and everything under its identifier.
    /// </summary>
    private const string _patterns = "'testproduct', 'testproduct.*'";

    public NuGetCacheCleanupTests()
    {
        foreach ( var package in new[] { "testproduct", "testproduct.core", "other.package" } )
        {
            Directory.CreateDirectory( Path.Combine( this._cache, package, "1.0" ) );
            File.WriteAllText( Path.Combine( this._cache, package, "1.0", "p.nupkg" ), "stale" );
        }
    }

    private bool Exists( string package ) => Directory.Exists( Path.Combine( this._cache, package ) );

    /// <summary>
    /// Builds a script that sets up what <c>DockerBuild.ps1</c> has in scope where it writes <c>Init.g.ps1</c>, then
    /// asks the shipped function for the text it puts in that file and runs it. The text is the subject, so it is taken
    /// from the script rather than restated here.
    /// </summary>
    private static string CreateInitHarness( string patterns, string cache, string prologue = "" )
        => $$"""
             {{DockerBuildScript.ExtractTopLevelFunction( "ConvertTo-PowerShellLiteral" )}}

             {{DockerBuildScript.ExtractTopLevelFunction( "New-NuGetCacheCleanupScript" )}}

             $NuGetCachePackagePatterns = @({{patterns}})
             $env:NUGET_PACKAGES = '{{DockerBuildScript.Escape( cache )}}'

             {{prologue}}

             # The container runs this as the body of Init.g.ps1.
             Invoke-Expression ( New-NuGetCacheCleanupScript )
             """;

    [Fact]
    public void TheInitScriptRemovesTheProductPackagesAndNothingElse()
    {
        var powerShell = DockerBuildScript.FindPowerShell();

        if ( powerShell == null )
        {
            return;
        }

        var (exitCode, output) = DockerBuildScript.TryRun( powerShell, CreateInitHarness( _patterns, this._cache ) );

        Assert.Equal( 0, exitCode );
        Assert.False( this.Exists( "testproduct" ), $"The product package was not removed:\n{output}" );
        Assert.False( this.Exists( "testproduct.core" ), $"A package of the product was not removed:\n{output}" );

        // A third-party package is not the product's to delete, and deleting it would make every build restore the
        // whole dependency graph over the network.
        Assert.True( this.Exists( "other.package" ), $"An unrelated package was removed:\n{output}" );
    }

    /// <summary>
    /// The case the defect consisted of. A directory that is still there after the removal fails the container, so the
    /// build cannot go on to restore it. The refusal is simulated by shadowing <c>Remove-Item</c>, because the state
    /// that produces it on an agent -- a directory owned by root -- cannot be created by a test that is not root.
    /// </summary>
    [Fact]
    public void TheInitScriptFailsWhenADirectoryCannotBeRemoved()
    {
        var powerShell = DockerBuildScript.FindPowerShell();

        if ( powerShell == null )
        {
            return;
        }

        const string prologue = """
                               function Remove-Item
                               {
                                   param([string]$LiteralPath, [switch]$Recurse, [switch]$Force, $ErrorAction)
                               }
                               """;

        var (exitCode, output) = DockerBuildScript.TryRun( powerShell, CreateInitHarness( _patterns, this._cache, prologue ) );

        Assert.NotEqual( 0, exitCode );
        Assert.Contains( "testproduct", output, StringComparison.Ordinal );
        Assert.Contains( "would restore them", output, StringComparison.Ordinal );
    }

    /// <summary>
    /// A cache holding nothing of the product is the normal state of a freshly cleaned agent, and must not fail.
    /// </summary>
    [Fact]
    public void TheInitScriptSucceedsWhenThereIsNothingToRemove()
    {
        var powerShell = DockerBuildScript.FindPowerShell();

        if ( powerShell == null )
        {
            return;
        }

        var (exitCode, _) = DockerBuildScript.TryRun( powerShell, CreateInitHarness( "'nothing.of.this.product'", this._cache ) );

        Assert.Equal( 0, exitCode );
        Assert.True( this.Exists( "testproduct" ) );
    }

    /// <summary>
    /// A test container does not run <c>Init.g.ps1</c>, because a test image is chosen for the tool chain under test and
    /// need not carry PowerShell 7, and a Docker test configuration starts no build container at all. The removal
    /// therefore rides in front of the test command, in the shell the container already runs it with.
    /// </summary>
    [Fact]
    public void TheTestCommandCarriesTheRemoval()
    {
        var powerShell = DockerBuildScript.FindPowerShell();

        if ( powerShell == null )
        {
            return;
        }

        // The delimiters are asserted on, so the value is read back with only the line break the host added removed.
        var command = Emit( powerShell, "/opt/teamcity-home/.nuget/packages" );

        // The directory is quoted and the pattern is not: the shell has to expand the pattern. `rm -rf` then gives the
        // semantics wanted at both ends -- a pattern matching nothing is not a failure, and a directory it cannot
        // remove makes the command exit non-zero, so the test command after the `&&` never runs.
        Assert.Equal(
            "rm -rf '/opt/teamcity-home/.nuget/packages'/testproduct "
            + "'/opt/teamcity-home/.nuget/packages'/testproduct.* && ",
            command );

        // An apostrophe in the path ends the quoting, is escaped, and the quoting resumes -- the POSIX spelling, since
        // a single-quoted word cannot hold an apostrophe at all. A home directory may legitimately contain one.
        Assert.Equal(
            "rm -rf '/home/o'\\''brien/.nuget/packages'/testproduct "
            + "'/home/o'\\''brien/.nuget/packages'/testproduct.* && ",
            Emit( powerShell, "/home/o'brien/.nuget/packages" ) );
    }

    /// <summary>
    /// Asks the shipped function what it puts in front of a test command for the given cache directory.
    /// </summary>
    private static string Emit( string powerShell, string cache )
    {
        var harness = $$"""
                        {{DockerBuildScript.ExtractTopLevelFunction( "New-NuGetCacheCleanupShellCommand" )}}

                        $NuGetCachePackagePatterns = @({{_patterns}})

                        # Written rather than returned, because the host strips the trailing space of a returned value.
                        [System.IO.File]::WriteAllText(
                            $env:NUGET_CACHE_CLEANUP_OUTPUT,
                            ( New-NuGetCacheCleanupShellCommand '{{DockerBuildScript.Escape( cache )}}' ) )
                        """;

        var output = Path.Combine( Path.GetTempPath(), $"cleanup-command-{Guid.NewGuid():N}.txt" );
        Environment.SetEnvironmentVariable( "NUGET_CACHE_CLEANUP_OUTPUT", output );

        try
        {
            DockerBuildScript.Run( powerShell, harness );

            return File.ReadAllText( output );
        }
        finally
        {
            Environment.SetEnvironmentVariable( "NUGET_CACHE_CLEANUP_OUTPUT", null );
            File.Delete( output );
        }
    }

    /// <summary>
    /// The agent-side step, which is what cleans the cache of a build that runs no container at all. It is generated as
    /// PowerShell and run here against a directory laid out like a NuGet cache, because the text of the step it replaced
    /// read correctly and still removed nothing.
    /// </summary>
    [Fact]
    public void TheAgentStepRemovesTheProductPackagesAndNothingElse()
    {
        var powerShell = DockerBuildScript.FindPowerShell();

        if ( powerShell == null )
        {
            return;
        }

        var script = $"$env:NUGET_PACKAGES = '{DockerBuildScript.Escape( this._cache )}'\n"
                     + TeamCityBuildConfiguration.GenerateNuGetCacheCleanupCommand( ["testproduct", "testproduct.*"] );

        var (exitCode, output) = DockerBuildScript.TryRun( powerShell, script );

        Assert.Equal( 0, exitCode );
        Assert.False( this.Exists( "testproduct" ), $"The product package was not removed:\n{output}" );
        Assert.False( this.Exists( "testproduct.core" ), $"A package of the product was not removed:\n{output}" );
        Assert.True( this.Exists( "other.package" ), $"An unrelated package was removed:\n{output}" );
        Assert.Contains( "Removed 2 package directory(ies) and 2 file(s)", output, StringComparison.Ordinal );
    }

    /// <summary>
    /// A survivor the step cannot explain by ownership fails the build. On Windows that is every survivor; on Unix it is
    /// every survivor that belongs to the account the step runs as, since only another account's files are the
    /// container's to remove.
    /// </summary>
    [Fact]
    public void TheAgentStepFailsWhenADirectoryItOwnsCannotBeRemoved()
    {
        var powerShell = DockerBuildScript.FindPowerShell();

        if ( powerShell == null )
        {
            return;
        }

        var script = $"$env:NUGET_PACKAGES = '{DockerBuildScript.Escape( this._cache )}'\n"
                     + "function Remove-Item { param([string]$LiteralPath, [switch]$Recurse, [switch]$Force, $ErrorAction) }\n"
                     + TeamCityBuildConfiguration.GenerateNuGetCacheCleanupCommand( ["testproduct", "testproduct.*"] );

        var (exitCode, output) = DockerBuildScript.TryRun( powerShell, script );

        Assert.NotEqual( 0, exitCode );
        Assert.Contains( "would restore stale packages", output, StringComparison.Ordinal );
        Assert.Contains( "testproduct", output, StringComparison.Ordinal );
    }

    /// <summary>
    /// An empty cache, or one holding nothing of this product, is success. A step that failed on it would fail every
    /// build on a freshly cleaned agent.
    /// </summary>
    [Fact]
    public void TheAgentStepSucceedsWhenThereIsNothingToRemove()
    {
        var powerShell = DockerBuildScript.FindPowerShell();

        if ( powerShell == null )
        {
            return;
        }

        var script = $"$env:NUGET_PACKAGES = '{DockerBuildScript.Escape( this._cache )}'\n"
                     + TeamCityBuildConfiguration.GenerateNuGetCacheCleanupCommand( ["nothing.of.this.product"] );

        var (exitCode, output) = DockerBuildScript.TryRun( powerShell, script );

        Assert.Equal( 0, exitCode );
        Assert.Contains( "Removed 0 package directory(ies)", output, StringComparison.Ordinal );
    }

    /// <summary>
    /// A cache folder that does not exist is not a failure either: the first build on a new agent has none.
    /// </summary>
    [Fact]
    public void TheAgentStepSucceedsWhenTheCacheDoesNotExist()
    {
        var powerShell = DockerBuildScript.FindPowerShell();

        if ( powerShell == null )
        {
            return;
        }

        var missing = Path.Combine( this._cache, "does-not-exist" );

        var script = $"$env:NUGET_PACKAGES = '{DockerBuildScript.Escape( missing )}'\n"
                     + TeamCityBuildConfiguration.GenerateNuGetCacheCleanupCommand( ["testproduct"] );

        var (exitCode, output) = DockerBuildScript.TryRun( powerShell, script );

        Assert.Equal( 0, exitCode );
        Assert.Contains( "NuGet packages folder not found", output, StringComparison.Ordinal );
    }

    /// <summary>
    /// The list reaches <c>DockerBuild.ps1</c> through <c>generate-scripts</c>, and both generators read it from
    /// <see cref="NuGetCachePatterns"/>: a list that differed between the agent step and the container would leave the
    /// difference in the cache of every agent where only the container can delete.
    /// </summary>
    [Fact]
    public void TheGeneratedScriptCarriesThePatternsOfTheProduct()
    {
        // Reading the dependencies goes through MSBuild, whose assemblies are located at run time.
        MSBuildHelper.InitializeLocator();

        using var directory = new TempDirectory();

        var product = new Product( MetalamaDependencies.V2026_1.Metalama )
        {
            GenerateTeamCitySettings = false,
            GenerateDockerfiles = false,

            // DockerBuild.ps1 is generated for a product that builds in a container, which is the only kind whose
            // containers can pollute the cache of the agent.
            OverriddenBuildAgentRequirements = new ContainerRequirements( ContainerHostKind.Windows )
        };

        Assert.True( GenerateScriptsCommand.Execute( TestBuildContext.Create( directory.Path, product ), new CommonCommandSettings() ) );

        var script = File.ReadAllText( Path.Combine( directory.Path, "DockerBuild.ps1" ) );

        Assert.DoesNotContain( "<NUGET_CACHE_PACKAGE_PATTERNS>", script, StringComparison.Ordinal );

        var patterns = NuGetCachePatterns.GetPatterns( product );
        Assert.NotEmpty( patterns );

        var line = Array.Find(
            script.ReplaceLineEndings( "\n" ).Split( '\n' ),
            l => l.StartsWith( "$NuGetCachePackagePatterns = @(", StringComparison.Ordinal ) );

        Assert.NotNull( line );

        foreach ( var pattern in patterns )
        {
            // Quoted, and in the casing NuGet gives the directories of the global packages folder, which is what the
            // agents match them in.
            Assert.Contains( $"'{pattern}'", line, StringComparison.Ordinal );
            Assert.Equal( pattern, pattern.ToLowerInvariant() );
        }
    }

    /// <summary>
    /// A generated build step holds a multi-line script, so a line break has to survive being escaped into a Kotlin
    /// string literal -- which cannot span lines. An unescaped one produces a settings file that does not compile, and
    /// the failure surfaces in TeamCity rather than here.
    /// </summary>
    [Fact]
    public void TheGeneratedStepIsOneKotlinStringLiteral()
    {
        var command = TeamCityBuildConfiguration.GenerateNuGetCacheCleanupCommand( ["testproduct"] );

        Assert.Contains( "\n", command, StringComparison.Ordinal );

        var escaped = KotlinHelper.EscapeString( command );

        Assert.DoesNotContain( "\n", escaped, StringComparison.Ordinal );
        Assert.DoesNotContain( "\r", escaped, StringComparison.Ordinal );
        Assert.Contains( "\\n", escaped, StringComparison.Ordinal );
    }

    /// <summary>
    /// The container must not go on to build when the clean-up refused. <c>exit</c> inside a script invoked with
    /// <c>&amp;</c> ends that script alone, so the command that invokes <c>Init.g.ps1</c> has to test the exit code
    /// itself; the first assertion below is the PowerShell behaviour that makes the second one necessary.
    /// </summary>
    [Fact]
    public void AFailingInitScriptStopsTheContainer()
    {
        var powerShell = DockerBuildScript.FindPowerShell();

        if ( powerShell == null )
        {
            return;
        }

        var initScript = Path.Combine( this._cache, "Init.g.ps1" );
        File.WriteAllText( initScript, "Write-Output 'INIT-RAN'\nexit 3\n" );

        var invocation = $"& '{DockerBuildScript.Escape( initScript )}'";

        // Without the check, the build runs anyway and the container reports the exit code of the build.
        var (unguarded, unguardedOutput) = DockerBuildScript.TryRun( powerShell, $"{invocation}; Write-Output 'BUILD-RAN'; exit 0" );

        Assert.Equal( 0, unguarded );
        Assert.Contains( "BUILD-RAN", unguardedOutput, StringComparison.Ordinal );

        // With it, which is what DockerBuild.ps1 now emits, the exit code of the init script is the exit code of the
        // container and the build never starts.
        var (guarded, guardedOutput) = DockerBuildScript.TryRun(
            powerShell,
            $"$LASTEXITCODE = 0; {invocation}; if ( $LASTEXITCODE -ne 0 ) {{ exit $LASTEXITCODE }}; Write-Output 'BUILD-RAN'; exit 0" );

        Assert.Equal( 3, guarded );
        Assert.DoesNotContain( "BUILD-RAN", guardedOutput, StringComparison.Ordinal );

        // The guard is in the shipped script, in the command that invokes Init.g.ps1.
        Assert.Contains(
            "$LASTEXITCODE = 0; & '$containerInitScript'; if ( $LASTEXITCODE -ne 0 ) { exit $LASTEXITCODE }; ",
            DockerBuildScript.Text.Replace( "`$", "$", StringComparison.Ordinal ),
            StringComparison.Ordinal );
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete( this._cache, true );
        }
        catch ( IOException )
        {
            // A leftover temporary directory is not worth failing a test over, and DirectoryNotFoundException is one of
            // these: a test may legitimately have removed the whole tree.
        }
    }
}
