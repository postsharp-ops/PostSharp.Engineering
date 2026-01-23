// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using PostSharp.Engineering.BuildTools.Dependencies.Model;
using PostSharp.Engineering.BuildTools.Utilities;
using System;
using System.IO;
using System.Xml.Linq;

namespace PostSharp.Engineering.BuildTools.Build.Files;

/// <summary>
/// Writes <c>nuget.config</c>.
/// </summary>
internal static class NuGetConfigFile
{
    private static string ConvertToWslPath( string path )
    {
        // Convert Windows path to WSL: C:\path -> /mnt/c/path
        if ( path is [_, ':', _, ..] && (path[2] == '\\' || path[2] == '/') )
        {
            var drive = char.ToLower( path[0], System.Globalization.CultureInfo.InvariantCulture );
            var remainder = path.Substring( 2 ).Replace( "\\", "/", StringComparison.Ordinal );

            return $"/mnt/{drive}{remainder}";
        }

        return path;
    }

    internal static bool TryWrite( BuildContext context, DependenciesConfigurationFile dependenciesConfigurationFile, BuildConfiguration configuration )
    {
        var product = context.Product;

        if ( !product.GenerateNuGetConfig )
        {
            return true;
        }

        // Fetch to resolve the VersionFile properties.
        if ( !dependenciesConfigurationFile.Fetch( context ) )
        {
            return false;
        }

        // Generate regular nuget.config
        if ( !GenerateNuGetConfig( context, dependenciesConfigurationFile, configuration, "nuget.config", path => path ) )
        {
            return false;
        }

        // Generate WSL version if AddWslSupport is enabled
        if ( product.AddWslSupport )
        {
            if ( !GenerateNuGetConfig( context, dependenciesConfigurationFile, configuration, "nuget.wsl.config", ConvertToWslPath ) )
            {
                return false;
            }
        }

        return true;
    }

    private static bool GenerateNuGetConfig(
        BuildContext context,
        DependenciesConfigurationFile dependenciesConfigurationFile,
        BuildConfiguration configuration,
        string targetFileName,
        Func<string, string> pathTransform )
    {
        var product = context.Product;
        var baseFilePath = Path.Combine( context.RepoDirectory, "nuget.base.config" );
        var targetFilePath = Path.Combine( context.RepoDirectory, targetFileName );

        XDocument document;
        XElement rootElement;

        if ( File.Exists( baseFilePath ) )
        {
            document = XDocument.Load( baseFilePath );
            rootElement = document.Root!;
        }
        else
        {
            document = new XDocument();
            rootElement = new XElement( "configuration" );
            document.Add( rootElement );
        }

        var packageSourcesElement = rootElement.Element( "packageSources" );

        if ( packageSourcesElement == null )
        {
            packageSourcesElement = new XElement( "packageSources" );
            rootElement.Add( packageSourcesElement );

            // If the element is not present (typical if no nuget.base.config), add default values.
            packageSourcesElement.Add( new XElement( "clear" ) );
            var defaultSource = new XElement( "add" );
            packageSourcesElement.Add( defaultSource );
            defaultSource.Add( new XAttribute( "key", "nuget.org" ) );
            defaultSource.Add( new XAttribute( "value", "https://api.nuget.org/v3/index.json" ) );
        }

        var packageSourceMappingElement = rootElement.Element( "packageSourceMapping" );

        if ( packageSourceMappingElement == null )
        {
            packageSourceMappingElement = new XElement( "packageSourceMapping" );
            rootElement.Add( packageSourceMappingElement );

            // If the element is not present (typical if no nuget.base.config), add default values.
            var defaultSourceMapping = new XElement( "packageSource" );
            defaultSourceMapping.Add( new XAttribute( "key", "nuget.org" ) );
            defaultSourceMapping.Add( new XElement( "package", new XAttribute( "pattern", "*" ) ) );
            packageSourceMappingElement.Add( defaultSourceMapping );
        }

        // Add the current artifact directory.
        var artifactDirectory =
            product.GetPrivateArtifactsAbsoluteDirectory( context, configuration );

        AddDirectory( product.ProductName, artifactDirectory, product.DependencyDefinition.PackagePatterns );

        // Add dependencies.
        foreach ( var dependencySource in dependenciesConfigurationFile.Dependencies )
        {
            if ( dependencySource.Value.SourceKind == DependencySourceKind.Feed )
            {
                // Skip any feed dependency, so it will be fall back to the default package source.
                packageSourcesElement.Add( new XComment( $" {dependencySource.Key} maps to the default nuget.org. " ) );

                continue;
            }
            else if ( dependencySource.Value.VersionFile == null )
            {
                context.Console.WriteWarning( $"Cannot determine the package directory for dependency '{dependencySource.Key}'." );

                continue;
            }

            var dependencyDefinition = product.GetDependencyDefinition( dependencySource.Key );
            var dependencyDirectory = Path.GetDirectoryName( dependencySource.Value.VersionFile )!;

            if ( dependencySource.Value.SourceKind == DependencySourceKind.Local )
            {
                dependencyDirectory = Path.Combine(
                    dependencyDirectory,
                    dependencyDefinition.GetPrivateArtifactsDirectory( configuration ) );
            }

            if ( !AddDirectory( dependencySource.Key, dependencyDirectory, dependencyDefinition.PackagePatterns ) )
            {
                return false;
            }
        }

        TextFileHelper.WriteIfDifferent( targetFilePath, document, context );

        return true;

        bool AddDirectory( string name, string directory, string[]? patterns )
        {
            if ( directory == null )
            {
                throw new ArgumentNullException( nameof(directory), $"Null directory for source '{name}'." );
            }

            // Apply path transformation (e.g., Windows to WSL)
            var transformedDirectory = pathTransform( directory );

            var addElement = new XElement( "add" );
            addElement.Add( new XAttribute( "key", name ) );
            addElement.Add( new XAttribute( "value", transformedDirectory ) );
            packageSourcesElement.Add( addElement );

            var packageSourceElement = new XElement( "packageSource" );
            packageSourceElement.Add( new XAttribute( "key", name ) );
            packageSourceMappingElement.Add( packageSourceElement );

            foreach ( var pattern in patterns ?? [] )
            {
                AddPattern( pattern );
            }

            return true;

            void AddPattern( string pattern )
            {
                var packageElement = new XElement( "package" );
                packageElement.Add( new XAttribute( "pattern", pattern ) );
                packageSourceElement.Add( packageElement );
            }
        }
    }
}