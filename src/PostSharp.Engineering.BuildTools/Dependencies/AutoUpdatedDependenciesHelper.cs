// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using PostSharp.Engineering.BuildTools.Build;
using PostSharp.Engineering.BuildTools.Build.Publishing;
using PostSharp.Engineering.BuildTools.Dependencies.Model;
using PostSharp.Engineering.BuildTools.Utilities;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace PostSharp.Engineering.BuildTools.Dependencies;

internal static class AutoUpdatedDependenciesHelper
{
    public static bool TryParseAndVerifyDependencies( BuildContext context, PublishSettings settings, out bool dependenciesUpdated )
    {
        context.Console.WriteImportantMessage( $"Checking versions of auto-updated dependencies." );

        dependenciesUpdated = false;

        // Get dependenciesOverrideFile from Versions.Public.g.props.
        if ( !DependenciesConfigurationFile.TryLoad( context, settings, settings.BuildConfiguration, out var dependenciesOverrideFile ) )
        {
            return false;
        }

        var autoUpdatedDependencies = dependenciesOverrideFile.Dependencies
            .Where( d => d.Value.SourceKind != DependencySourceKind.Feed )
            .ToArray();

        if ( autoUpdatedDependencies.Length == 0 )
        {
            context.Console.WriteMessage( "There are no auto-updated dependencies to check." );

            return true;
        }

        var autoUpdatedVersionsFilePath = Path.Combine( context.RepoDirectory, context.Product.AutoUpdatedVersionsFilePath );
        var autoUpdatedVersionsFileName = Path.GetFileName( autoUpdatedVersionsFilePath );
        var currentVersionDocument = XDocument.Load( autoUpdatedVersionsFilePath, LoadOptions.PreserveWhitespace );

        var currentVersionPropertyGroup = currentVersionDocument.Root!.Element( "PropertyGroup" )!;

        foreach ( var dependencyOverride in autoUpdatedDependencies )
        {
            var dependencySource = dependencyOverride.Value;

            var dependency = context.Product.ProductFamily.GetDependencyDefinition( dependencyOverride.Key );

            // Path to the downloaded build version file.
            string? dependencyVersionPath;

            if ( dependencyOverride.Value.SourceKind == DependencySourceKind.Local )
            {
                var localImportDocumentPath = dependencySource.VersionFile;

                if ( !File.Exists( localImportDocumentPath ) )
                {
                    context.Console.WriteError( $"Local import document of '{dependency.Name}' does not exist." );

                    return false;
                }

                var localImportDocument = XDocument.Load( localImportDocumentPath );

                var dependencyVersionRelativePath = localImportDocument.Root!.Element( "Import" )!.Attribute( "Project" )!.Value;
                var localImportDocumentDirectory = Path.GetDirectoryName( localImportDocumentPath )!;
                dependencyVersionPath = Path.Combine( localImportDocumentDirectory, dependencyVersionRelativePath );
            }
            else
            {
                dependencyVersionPath = dependencySource.VersionFile;
            }

            if ( !File.Exists( dependencyVersionPath ) )
            {
                context.Console.WriteError( $"'{dependencyVersionPath}' version file of '{dependency.Name}' does not exist." );

                return false;
            }

            // Load the up-to-date version file of dependency.
            var dependencyVersionDocument = XDocument.Load( dependencyVersionPath );

            var currentDependencyVersionValue =
                dependencyVersionDocument.Root!.Element( "PropertyGroup" )!.Element( $"{dependency.NameWithoutDot}Version" )!.Value;

            // Load dependency version from public version.
            var versionElementName = $"{dependency.NameWithoutDot}Version";
            var versionElement = currentVersionPropertyGroup.Element( versionElementName );

            if ( versionElement == null )
            {
                context.Console.WriteWarning( $"No property '{versionElementName}'." );
            }

            var oldVersionValue = versionElement?.Value;

            // We don't need to rewrite the file if there is no change in version.
            if ( oldVersionValue == currentDependencyVersionValue )
            {
                context.Console.WriteMessage( $"Version of '{dependency.Name}' dependency is up to date." );

                continue;
            }

            if ( versionElement == null )
            {
                versionElement = new XElement( versionElementName );
                currentVersionPropertyGroup.Add( versionElement );
            }

            versionElement.Value = currentDependencyVersionValue;
            dependenciesUpdated = true;

            context.Console.WriteMessage( $"Bumping version dependency '{dependency}' from '{oldVersionValue}' to '{currentDependencyVersionValue}'." );
        }

        if ( dependenciesUpdated )
        {
            context.Console.WriteImportantMessage( $"{(settings.Dry ? "Dry run: " : "")}Writing updated '{autoUpdatedVersionsFileName}'." );

            if ( !settings.Dry )
            {
                var xmlWriterSettings =
                    new XmlWriterSettings { OmitXmlDeclaration = true, Indent = true, IndentChars = "    ", Encoding = new UTF8Encoding( false ) };

                using ( var xmlWriter = XmlWriter.Create( autoUpdatedVersionsFilePath, xmlWriterSettings ) )
                {
                    currentVersionDocument.Save( xmlWriter );
                }
            }
        }

        return true;
    }

    public static bool TryUpdateAutoUpdatedDependencies( BuildContext context, PublishSettings settings )
    {
        // Go through all dependencies and update their fixed version in AutoUpdatedVersions.props file.
        if ( !TryParseAndVerifyDependencies( context, settings, out var dependenciesUpdated ) )
        {
            return false;
        }

        // Commit and push if dependencies versions were updated in previous step.
        if ( dependenciesUpdated )
        {
            if ( settings.Dry )
            {
                context.Console.WriteImportantMessage( "Dry run: Updating auto-updated dependencies." );
            }
            else
            {
                // Adds AutoUpdatedVersions.props with updated dependencies versions to Git staging area.
                if ( !ToolInvocationHelper.InvokeTool(
                        context.Console,
                        "git",
                        $"add {context.Product.AutoUpdatedVersionsFilePath}",
                        context.RepoDirectory ) )
                {
                    return false;
                }

                // Returns the remote origin.
                if ( !ToolInvocationHelper.InvokeTool(
                        context.Console,
                        "git",
                        "remote get-url origin",
                        context.RepoDirectory,
                        out _,
                        out var gitOrigin ) )
                {
                    return false;
                }

                if ( !ToolInvocationHelper.InvokeTool(
                        context.Console,
                        "git",
                        "commit -m \"<<DEPENDENCIES_UPDATED>>\"",
                        context.RepoDirectory ) )
                {
                    return false;
                }

                if ( !ToolInvocationHelper.InvokeTool(
                        context.Console,
                        "git",
                        $"push {gitOrigin.Trim()}",
                        context.RepoDirectory ) )
                {
                    return false;
                }
            }
        }

        return true;
    }
}