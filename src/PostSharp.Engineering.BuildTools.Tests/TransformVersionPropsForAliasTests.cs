// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using PostSharp.Engineering.BuildTools.Dependencies;
using System.IO;
using System.Xml.Linq;
using Xunit;

namespace PostSharp.Engineering.BuildTools.Tests;

public class TransformVersionPropsForAliasTests
{
    [Fact]
    public void RenamesProducerPrefixedProperties()
    {
        using var directory = new TempDirectory();
        var sourceFile = directory.WriteFile( "source.props", """
            <Project>
                <PropertyGroup>
                    <MetalamaVersion>0.5.220</MetalamaVersion>
                    <MetalamaMainVersion>0.5</MetalamaMainVersion>
                    <MetalamaArtifactsDirectory>artifacts/x</MetalamaArtifactsDirectory>
                </PropertyGroup>
            </Project>
            """ );

        var destinationFile = Path.Combine( directory.Path, "destination.props" );

        DependenciesHelper.TransformVersionPropsForAlias( sourceFile, destinationFile, "Metalama", "Metalama20260" );

        var document = XDocument.Load( destinationFile );
        var propertyGroup = document.Root!.Element( "PropertyGroup" )!;

        Assert.Equal( "0.5.220", propertyGroup.Element( "Metalama20260Version" )?.Value );
        Assert.Equal( "0.5", propertyGroup.Element( "Metalama20260MainVersion" )?.Value );
        Assert.Equal( "artifacts/x", propertyGroup.Element( "Metalama20260ArtifactsDirectory" )?.Value );
        Assert.Null( propertyGroup.Element( "MetalamaVersion" ) );
    }

    [Fact]
    public void DoesNotRenameTransitiveDependencyProperties()
    {
        // Transitive Feed dependency version properties (e.g. <PostSharpEngineeringVersion>) and properties whose suffix
        // is not in the curated list (<MetalamaCompilerVersion>) must NOT be renamed.
        using var directory = new TempDirectory();
        var sourceFile = directory.WriteFile( "source.props", """
            <Project>
                <PropertyGroup>
                    <MetalamaVersion>0.5.220</MetalamaVersion>
                    <MetalamaCompilerVersion>1.0.0</MetalamaCompilerVersion>
                    <PostSharpEngineeringVersion>2026.0.500</PostSharpEngineeringVersion>
                </PropertyGroup>
            </Project>
            """ );

        var destinationFile = Path.Combine( directory.Path, "destination.props" );

        DependenciesHelper.TransformVersionPropsForAlias( sourceFile, destinationFile, "Metalama", "Metalama20260" );

        var document = XDocument.Load( destinationFile );
        var propertyGroup = document.Root!.Element( "PropertyGroup" )!;

        Assert.Equal( "0.5.220", propertyGroup.Element( "Metalama20260Version" )?.Value );
        Assert.Equal( "1.0.0", propertyGroup.Element( "MetalamaCompilerVersion" )?.Value ); // unchanged
        Assert.Equal( "2026.0.500", propertyGroup.Element( "PostSharpEngineeringVersion" )?.Value ); // unchanged
        Assert.Null( propertyGroup.Element( "Metalama20260CompilerVersion" ) );
    }

    [Fact]
    public void RenamesItemTypes()
    {
        using var directory = new TempDirectory();
        var sourceFile = directory.WriteFile( "source.props", """
            <Project>
                <ItemGroup>
                    <MetalamaDependencies Include="PostSharp.Engineering">
                        <SourceKind>Feed</SourceKind>
                        <Version>2026.0.500</Version>
                    </MetalamaDependencies>
                </ItemGroup>
            </Project>
            """ );

        var destinationFile = Path.Combine( directory.Path, "destination.props" );

        DependenciesHelper.TransformVersionPropsForAlias( sourceFile, destinationFile, "Metalama", "Metalama20260" );

        var document = XDocument.Load( destinationFile );
        var item = document.Root!.Element( "ItemGroup" )!.Element( "Metalama20260Dependencies" );

        Assert.NotNull( item );
        Assert.Equal( "PostSharp.Engineering", item.Attribute( "Include" )?.Value );

        // Item metadata (SourceKind, Version) must NOT be renamed.
        Assert.Equal( "Feed", item.Element( "SourceKind" )?.Value );
        Assert.Equal( "2026.0.500", item.Element( "Version" )?.Value );
    }

    [Fact]
    public void AbsolutizesRelativeImportPaths()
    {
        using var directory = new TempDirectory();
        var nestedDirectory = Path.Combine( directory.Path, "sub" );
        Directory.CreateDirectory( nestedDirectory );

        var sourceFile = Path.Combine( nestedDirectory, "source.props" );
        File.WriteAllText( sourceFile, """
            <Project>
                <Import Project="../target.props" Condition="Exists('../target.props')"/>
            </Project>
            """ );

        var destinationFile = Path.Combine( directory.Path, "out", "destination.props" );

        DependenciesHelper.TransformVersionPropsForAlias( sourceFile, destinationFile, "Metalama", "Metalama20260" );

        var document = XDocument.Load( destinationFile );
        var importElement = document.Root!.Element( "Import" )!;
        var projectAttribute = importElement.Attribute( "Project" )!.Value;

        Assert.True( Path.IsPathRooted( projectAttribute ), $"Expected absolute path, got '{projectAttribute}'" );
        Assert.EndsWith( "target.props", projectAttribute, System.StringComparison.Ordinal );

        // Condition's path is also absolutized.
        var condition = importElement.Attribute( "Condition" )!.Value;
        Assert.DoesNotContain( "../target.props", condition, System.StringComparison.Ordinal );
    }

    [Fact]
    public void IsIdempotentOnAlreadyTransformedFile()
    {
        using var directory = new TempDirectory();
        var sourceFile = directory.WriteFile( "source.props", """
            <Project>
                <PropertyGroup>
                    <Metalama20260Version>0.5.220</Metalama20260Version>
                </PropertyGroup>
            </Project>
            """ );

        var destinationFile = Path.Combine( directory.Path, "destination.props" );

        // Running with prefix "Metalama" should not double-rename "Metalama20260Version" because
        // the remainder "20260Version" is not in the curated suffix list.
        DependenciesHelper.TransformVersionPropsForAlias( sourceFile, destinationFile, "Metalama", "Metalama20260" );

        var document = XDocument.Load( destinationFile );
        var propertyGroup = document.Root!.Element( "PropertyGroup" )!;

        Assert.NotNull( propertyGroup.Element( "Metalama20260Version" ) );
    }
}

internal sealed class TempDirectory : System.IDisposable
{
    public string Path { get; }

    public TempDirectory()
    {
        this.Path = System.IO.Path.Combine( System.IO.Path.GetTempPath(), "ps-eng-tests-" + System.Guid.NewGuid().ToString( "N" ) );
        Directory.CreateDirectory( this.Path );
    }

    public string WriteFile( string name, string content )
    {
        var path = System.IO.Path.Combine( this.Path, name );
        File.WriteAllText( path, content );
        return path;
    }

    public void Dispose()
    {
        if ( Directory.Exists( this.Path ) )
        {
            try { Directory.Delete( this.Path, true ); } catch { /* best-effort */ }
        }
    }
}
