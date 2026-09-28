// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using PostSharp.Engineering.BuildTools.Build;
using PostSharp.Engineering.BuildTools.Build.Model;
using PostSharp.Engineering.BuildTools.Dependencies.Definitions;
using Xunit;

namespace PostSharp.Engineering.BuildTools.Tests;

/// <summary>
/// The versioning scheme of a build depends on its configuration, on <see cref="BuildConfigurationInfo.VersionKind"/>, and
/// on whether it is a build of the build server (<see cref="BuildSettings.BuildNumber"/>).
/// </summary>
public sealed class VersionSpecTests
{
    private static readonly Product _defaultProduct = new( MetalamaDependencies.V2026_1.Metalama );

    private static readonly Product _publicReleaseProduct = new( MetalamaDependencies.V2026_1.Metalama )
    {
        Configurations = Product.DefaultConfigurations.WithValue(
            BuildConfiguration.Release,
            Product.DefaultConfigurations.Release with { VersionKind = VersionKind.Public } )
    };

    [Fact]
    public void ThePublicConfigurationHasAPublicVersion()
    {
        Assert.Equal( VersionKind.Public, new BuildSettings().GetVersionSpec( _defaultProduct, BuildConfiguration.Public ).Kind );
        Assert.Equal( VersionKind.Public, new BuildSettings { BuildNumber = 12 }.GetVersionSpec( _defaultProduct, BuildConfiguration.Public ).Kind );
    }

    [Fact]
    public void AnotherConfigurationHasANumberedVersionOnTheBuildServerByDefault()
    {
        var spec = new BuildSettings { BuildNumber = 12 }.GetVersionSpec( _defaultProduct, BuildConfiguration.Release );

        Assert.Equal( VersionKind.Numbered, spec.Kind );
        Assert.Equal( 12, spec.Number );
    }

    [Fact]
    public void AConfigurationCanHaveAPublicVersionOnTheBuildServer()
        => Assert.Equal(
            VersionKind.Public,
            new BuildSettings { BuildNumber = 12 }.GetVersionSpec( _publicReleaseProduct, BuildConfiguration.Release ).Kind );

    [Fact]
    public void ALocalBuildOfAnotherConfigurationHasALocalVersion()
    {
        Assert.Equal( VersionKind.Local, new BuildSettings().GetVersionSpec( _defaultProduct, BuildConfiguration.Release ).Kind );
        Assert.Equal( VersionKind.Local, new BuildSettings().GetVersionSpec( _publicReleaseProduct, BuildConfiguration.Release ).Kind );
    }
}
