// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using PostSharp.Engineering.BuildTools.Build.MSBuild;
using PostSharp.Engineering.BuildTools.Build.Model;
using PostSharp.Engineering.BuildTools.ContinuousIntegration;
using PostSharp.Engineering.BuildTools.Dependencies.Definitions;
using PostSharp.Engineering.BuildTools.Dependencies.Model;
using PostSharp.Engineering.BuildTools.Docker;
using System.Collections.Generic;
using System.IO;
using Xunit;

namespace PostSharp.Engineering.BuildTools.Tests;

public class GenerateScriptsTests
{
    // Writing 'eng/DockerMounts.g.ps1' requires the dependencies to have been fetched or restored, but the file is
    // excluded from source control and holds machine-local paths. The command must therefore still write the tracked
    // scripts and succeed, as required by the upstream merge, which regenerates the scripts after a conflict
    // resolution in a checkout that has no 'dependencies' directory.
    [Fact]
    public void Execute_SucceedsWhenTheDependenciesCannotBeRead()
    {
        // Reading the dependencies goes through MSBuild, whose assemblies are located at run time. Without this call,
        // the method that reads them cannot even be entered in a test host.
        MSBuildHelper.InitializeLocator();

        using var directory = new TempDirectory();

        var product = new Product( MetalamaDependencies.V2026_1.Metalama )
        {
            // The TeamCity settings and the Dockerfiles have their own tests and need a full product definition.
            GenerateTeamCitySettings = false,
            GenerateDockerfiles = false,
            OverriddenBuildAgentRequirements = new ContainerRequirements( ContainerHostKind.Windows )
        };

        var context = TestBuildContext.Create( directory.Path, product );

        // The directory has no 'eng/Versions.props', so the dependency configuration cannot be read.
        Assert.True( GenerateScriptsCommand.Execute( context, new CommonCommandSettings() ) );

        // The tracked generated files, which are the contract of this command, have been written.
        Assert.True( File.Exists( Path.Combine( directory.Path, "Build.ps1" ) ) );
        Assert.True( File.Exists( Path.Combine( directory.Path, "build.sh" ) ) );
        Assert.True( File.Exists( Path.Combine( directory.Path, "DockerBuild.ps1" ) ) );
        Assert.True( File.Exists( Path.Combine( directory.Path, "eng", "RunClaude.ps1" ) ) );

        // The git-ignored mount file has been skipped.
        Assert.False( File.Exists( Path.Combine( directory.Path, "eng", "DockerMounts.g.ps1" ) ) );
    }

    [Fact]
    public void GetUnfetchedDependencies_ReturnsTheDependenciesThatHaveNoVersionFile()
    {
        var fetched = DependencySource.CreateRestoredDependency( null, DependencyConfigurationOrigin.Default );
        fetched.VersionFile = "dependencies/Fetched/Fetched.version.props";

        var dependencies = new Dictionary<string, DependencySource>
        {
            // A feed dependency is consumed from a package feed, so it has no local directory to mount.
            ["Feed"] = DependencySource.CreateFeed( "1.0.0", DependencyConfigurationOrigin.Default ),
            ["Fetched"] = fetched,
            ["Unfetched"] = DependencySource.CreateRestoredDependency( null, DependencyConfigurationOrigin.Default )
        };

        Assert.Equal( new[] { "Unfetched" }, GenerateScriptsCommand.GetUnfetchedDependencies( dependencies ) );
    }
}
