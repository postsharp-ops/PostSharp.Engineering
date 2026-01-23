// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using System;
using System.Globalization;
using System.IO;

namespace PostSharp.Engineering.BuildTools.Build.Files.NuGet;

/// <summary>
/// Generates nuget.wsl.config with WSL-formatted paths (/mnt/c/...).
/// Inherits from <see cref="StandardNuGetConfigGenerator"/> and only overrides path transformation.
/// </summary>
internal sealed class WslNuGetConfigGenerator : StandardNuGetConfigGenerator
{
    protected override string GetTargetFilePath( BuildContext context, BuildConfiguration configuration )
        => Path.Combine( context.RepoDirectory, "nuget.wsl.config" );

    protected override string TransformPath( string path, BuildContext context ) => ConvertToWslPath( path );

    private static string ConvertToWslPath( string path )
    {
        // Convert Windows path to WSL: C:\path -> /mnt/c/path
        if ( path is [_, ':', _, ..] && (path[2] == '\\' || path[2] == '/') )
        {
            var drive = char.ToLower( path[0], CultureInfo.InvariantCulture );
            var remainder = path.Substring( 2 ).Replace( "\\", "/", StringComparison.Ordinal );

            return $"/mnt/{drive}{remainder}";
        }

        return path;
    }
}
