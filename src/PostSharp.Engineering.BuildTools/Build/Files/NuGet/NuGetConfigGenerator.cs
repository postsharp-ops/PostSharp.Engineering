// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using PostSharp.Engineering.BuildTools.Dependencies.Model;
using PostSharp.Engineering.BuildTools.Utilities;
using System;
using System.IO;
using System.Xml.Linq;

namespace PostSharp.Engineering.BuildTools.Build.Files.NuGet;

/// <summary>
/// Abstract base class for generating nuget.config files with different path strategies.
/// </summary>
internal abstract class NuGetConfigGenerator
{
    /// <summary>
    /// Generates a nuget.config file.
    /// </summary>
    public bool TryGenerate(
        BuildContext context,
        DependenciesConfigurationFile dependenciesConfigurationFile,
        BuildConfiguration configuration )
    {
        var product = context.Product;
        var targetFilePath = this.GetTargetFilePath( context, configuration );

        XDocument document;
        XElement rootElement;

        if ( this.ShouldLoadBaseConfig() )
        {
            var baseFilePath = Path.Combine( context.RepoDirectory, "nuget.base.config" );

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
        var artifactDirectory = this.GetCurrentProductDirectory( context, configuration );

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

            if ( !this.ShouldIncludeDependency( dependencyDefinition ) )
            {
                // Skip this dependency based on generator-specific logic
                packageSourcesElement.Add( new XComment( $" {dependencySource.Key} excluded by generator. " ) );

                continue;
            }

            var dependencyDirectory = this.GetDependencyDirectory(
                context,
                dependencySource.Key,
                dependencySource.Value,
                dependencyDefinition,
                configuration );

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

            // Apply path transformation
            var transformedDirectory = this.TransformPath( directory, context );

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

    /// <summary>
    /// Gets the target file path where the nuget.config will be written.
    /// </summary>
    protected abstract string GetTargetFilePath( BuildContext context, BuildConfiguration configuration );

    /// <summary>
    /// Transforms a path (e.g., to WSL format or relative path).
    /// </summary>
    protected abstract string TransformPath( string path, BuildContext context );

    /// <summary>
    /// Gets the directory for the current product's packages.
    /// </summary>
    protected abstract string GetCurrentProductDirectory( BuildContext context, BuildConfiguration configuration );

    /// <summary>
    /// Gets the directory for a dependency's packages.
    /// </summary>
    protected abstract string GetDependencyDirectory(
        BuildContext context,
        string dependencyKey,
        DependencySource dependencySource,
        DependencyDefinition dependencyDefinition,
        BuildConfiguration configuration );

    /// <summary>
    /// Determines whether a dependency should be included in the nuget.config.
    /// </summary>
    /// <param name="dependencyDefinition">The dependency definition to check.</param>
    /// <returns>True if the dependency should be included; otherwise, false.</returns>
    protected virtual bool ShouldIncludeDependency( DependencyDefinition dependencyDefinition ) => true;

    /// <summary>
    /// Determines whether to load the base config file (nuget.base.config).
    /// </summary>
    protected virtual bool ShouldLoadBaseConfig() => true;
}