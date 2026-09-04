// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using PostSharp.Engineering.BuildTools.Dependencies.Definitions;
using PostSharp.Engineering.BuildTools.Dependencies.Model;
using PostSharp.Engineering.BuildTools.Tools.TeamCity;
using System.IO;
using Xunit;

namespace PostSharp.Engineering.BuildTools.Tests;

public class RestoredDependencySourceTests
{
    private static readonly DependencyDefinition _dependency = MetalamaDependencies.V2026_1.MetalamaCompiler;

    private static void WriteVersionFile( string repoDirectory, string properties )
    {
        var path = TeamCityHelper.GetRestoredDependencyVersionFile( repoDirectory, _dependency.Name );
        Directory.CreateDirectory( Path.GetDirectoryName( path )! );

        File.WriteAllText( path, $"<Project><PropertyGroup>{properties}</PropertyGroup></Project>" );
    }

    // The version file is absent whenever the dependency has not been restored as a build server artifact. The
    // 'Upstream Merge' build configuration is such a case: it declares no artifact dependency, and its own cleaning
    // step removes the 'dependencies' directory. Reading the file used to throw DirectoryNotFoundException there,
    // which hid the diagnostic that the callers implement for an unfetched dependency.
    [Fact]
    public void CreateRestoredDependency_WithoutVersionFile_ReturnsASourceWithoutBuild()
    {
        using var directory = new TempDirectory();
        var context = TestBuildContext.Create( directory.Path );

        var source = DependencySource.CreateRestoredDependency( context, _dependency, DependencyConfigurationOrigin.Default );

        Assert.Equal( DependencySourceKind.RestoredDependency, source.SourceKind );
        Assert.Null( source.BuildServerSource );
        Assert.Null( source.VersionFile );
    }

    [Fact]
    public void CreateRestoredDependency_ReadsTheBuildNumberAndTheBuildType()
    {
        using var directory = new TempDirectory();

        WriteVersionFile(
            directory.Path,
            "<MetalamaCompilerBuildNumber>1234</MetalamaCompilerBuildNumber><MetalamaCompilerBuildType>SomeBuildTypeId</MetalamaCompilerBuildType>" );

        var context = TestBuildContext.Create( directory.Path );

        var source = DependencySource.CreateRestoredDependency( context, _dependency, DependencyConfigurationOrigin.Default );

        Assert.Equal( DependencySourceKind.RestoredDependency, source.SourceKind );

        var buildId = Assert.IsType<CiBuildId>( source.BuildServerSource );

        Assert.Equal( 1234, buildId.BuildNumber );
        Assert.Equal( "SomeBuildTypeId", buildId.BuildTypeId );
    }

    // A restored version file that names no build is accepted, and gives the same source as an absent one.
    [Fact]
    public void CreateRestoredDependency_WithoutBuildNumber_ReturnsASourceWithoutBuild()
    {
        using var directory = new TempDirectory();
        WriteVersionFile( directory.Path, "<MetalamaCompilerVersion>1.0.0</MetalamaCompilerVersion>" );
        var context = TestBuildContext.Create( directory.Path );

        var source = DependencySource.CreateRestoredDependency( context, _dependency, DependencyConfigurationOrigin.Default );

        Assert.Equal( DependencySourceKind.RestoredDependency, source.SourceKind );
        Assert.Null( source.BuildServerSource );
    }
}
