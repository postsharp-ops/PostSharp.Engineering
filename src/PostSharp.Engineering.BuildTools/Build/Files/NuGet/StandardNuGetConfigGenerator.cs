// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using PostSharp.Engineering.BuildTools.Dependencies.Model;
using System.IO;

namespace PostSharp.Engineering.BuildTools.Build.Files.NuGet;

/// <summary>
/// Generates standard nuget.config with absolute Windows paths.
/// </summary>
internal class StandardNuGetConfigGenerator : NuGetConfigGenerator
{
    protected override string GetTargetFilePath( BuildContext context, BuildConfiguration configuration )
        => Path.Combine( context.RepoDirectory, "nuget.config" );

    protected override string TransformPath( string path, BuildContext context ) => path; // No transformation - use absolute paths as-is

    protected override string GetCurrentProductDirectory( BuildContext context, BuildConfiguration configuration )
        => context.Product.GetPrivateArtifactsAbsoluteDirectory( context, configuration );

    protected override string GetDependencyDirectory(
        BuildContext context,
        string dependencyKey,
        DependencySource dependencySource,
        DependencyDefinition dependencyDefinition,
        BuildConfiguration configuration )
    {
        var dependencyDirectory = Path.GetDirectoryName( dependencySource.VersionFile )!;

        if ( dependencySource.SourceKind == DependencySourceKind.Local )
        {
            dependencyDirectory = Path.Combine(
                dependencyDirectory,
                dependencyDefinition.GetPrivateArtifactsDirectory( configuration ) );
        }

        return dependencyDirectory;
    }
}