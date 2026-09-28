// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using PostSharp.Engineering.BuildTools.Build;
using PostSharp.Engineering.BuildTools.Build.Model;
using PostSharp.Engineering.BuildTools.ContinuousIntegration.Model;
using PostSharp.Engineering.BuildTools.Dependencies.Definitions;
using PostSharp.Engineering.BuildTools.Utilities;
using System;
using Xunit;

namespace PostSharp.Engineering.BuildTools.Tests;

/// <summary>
/// A product can declare that a configuration cannot be built, because its build configuration on the build server produces
/// it otherwise (<see cref="BuildConfigurationInfo.SupportsBuild"/>).
/// </summary>
public sealed class SupportsBuildTests : IDisposable
{
    private readonly TempDirectory _directory = new();

    private static Product CreateProduct( AdditionalCiBuildConfiguration? publicBuild )
        => new( MetalamaDependencies.V2026_1.Metalama )
        {
            Configurations = Product.DefaultConfigurations.WithValue(
                BuildConfiguration.Public,
                c => c with { SupportsBuild = false, CustomBuildConfiguration = publicBuild } )
        };

    private static PowershellAdditionalCiBuildConfiguration CreatePublicBuild()
        => new( "PublicBuild", "Build [Public]", "eng/PublicBuild.ps1", "" ) { SnapshotDependencies = [new SnapshotDependency( BuildConfiguration.Release )], BuildSnapshotDependency = BuildConfiguration.Release };

    [Fact]
    public void TheBuildRefusesAConfigurationThatDoesNotSupportIt()
    {
        var context = TestBuildContext.Create( this._directory.Path, CreateProduct( CreatePublicBuild() ) );

        Assert.False( BuildCommand.CheckSupportsBuild( context, BuildConfiguration.Public ) );
        Assert.True( BuildCommand.CheckSupportsBuild( context, BuildConfiguration.Release ) );
    }

    /// <summary>
    /// The stock build step runs 'Build.ps1 test', so a configuration that cannot be built needs a build configuration of its own
    /// on the build server.
    /// </summary>
    [Fact]
    public void AnExportedConfigurationThatDoesNotSupportTheBuildNeedsACustomBuildConfiguration()
    {
        Assert.False( SnapshotDependencyGraph.TryValidate( new ConsoleHelper(), CreateProduct( null ) ) );
        Assert.True( SnapshotDependencyGraph.TryValidate( new ConsoleHelper(), CreateProduct( CreatePublicBuild() ) ) );
    }

    public void Dispose() => this._directory.Dispose();
}
