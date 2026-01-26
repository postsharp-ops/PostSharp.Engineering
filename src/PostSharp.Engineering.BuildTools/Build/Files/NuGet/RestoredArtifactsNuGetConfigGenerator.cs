// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using PostSharp.Engineering.BuildTools.Dependencies.Model;
using System.IO;

namespace PostSharp.Engineering.BuildTools.Build.Files.NuGet;

/// <summary>
/// Generates nuget.restored.config with relative paths for TeamCity artifact restoration scenario.
/// Assumes all dependencies have been restored to dependencies/&lt;name&gt;/ directory.
/// All paths are relative to the repository root.
/// </summary>
internal sealed class RestoredArtifactsNuGetConfigGenerator : NuGetConfigGenerator
{
    protected override string GetTargetFilePath( BuildContext context, BuildConfiguration configuration )
        => Path.Combine(
            context.Product.GetPrivateArtifactsAbsoluteDirectory( context, configuration ),
            "nuget.restored.config" );

    protected override string TransformPath( string path, BuildContext context )
    {
        // Make path relative to repository root
        var relativePath = Path.GetRelativePath( context.RepoDirectory, path );

        // Normalize to forward slashes for consistency
        return relativePath.Replace( '\\', '/' );
    }

    protected override string GetCurrentProductDirectory( BuildContext context, BuildConfiguration configuration )
        => context.Product.GetPrivateArtifactsAbsoluteDirectory( context, configuration );

    protected override string GetDependencyDirectory(
        BuildContext context,
        string dependencyKey,
        DependencySource dependencySource,
        DependencyDefinition dependencyDefinition,
        BuildConfiguration configuration )
    {
        // For restored artifacts, dependencies are always under dependencies/<name>
        // TeamCity artifact rule +:{PrivateArtifactsDir}/**/* => dependencies/{name} copies the contents directly
        return Path.Combine(
            context.RepoDirectory,
            "dependencies",
            dependencyKey );
    }

    protected override bool ShouldIncludeDependency( DependencyDefinition dependencyDefinition )
    {
        // Only include dependencies that generate snapshot dependencies
        // Dependencies with GenerateSnapshotDependency = false won't be restored as TeamCity artifacts
        return dependencyDefinition.GenerateSnapshotDependency;
    }

    protected override bool ShouldLoadBaseConfig() => false; // TeamCity scenario doesn't need base config
}