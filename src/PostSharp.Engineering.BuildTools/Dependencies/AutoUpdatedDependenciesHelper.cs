// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using PostSharp.Engineering.BuildTools.Build;
using PostSharp.Engineering.BuildTools.Build.Publishing;
using PostSharp.Engineering.BuildTools.Utilities;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace PostSharp.Engineering.BuildTools.Dependencies;

internal static class AutoUpdatedDependenciesHelper
{
    public static bool TryParseAndVerifyDependencies( BuildContext context, PublishSettings settings, out bool dependenciesUpdated )
    {
        context.Console.WriteImportantMessage( $"Checking versions of auto-updated dependencies." );

        dependenciesUpdated = false;

        var autoUpdatedDependencies = context.Product.DependencyDefinition.GetAllDependencies( BuildConfiguration.Public )
            .Where( d => d.Definition.AutoUpdateVersion )
            .ToArray();

        if ( autoUpdatedDependencies.Length == 0 )
        {
            context.Console.WriteMessage( "There are no auto-updated dependencies to check." );

            return true;
        }

        var thisAutoUpdatedVersionsFilePath = Path.Combine( context.RepoDirectory, context.Product.AutoUpdatedVersionsFilePath );
        var thisAutoUpdatedVersionsFileName = Path.GetFileName( thisAutoUpdatedVersionsFilePath );
        var thisAutoUpdatedVersionsDocument = XDocument.Load( thisAutoUpdatedVersionsFilePath, LoadOptions.PreserveWhitespace );

        var thisAutoUpdatedVersionsPropertyGroupElement = thisAutoUpdatedVersionsDocument.Root!.Element( "PropertyGroup" )!;

        var errors = 0;

        foreach ( var dependencyConfiguration in autoUpdatedDependencies )
        {
            var dependency = dependencyConfiguration.Definition;

            var theirAutoUpdatedVersionsFilePath = Path.Combine( context.RepoDirectory, "..", dependency.Name, "eng", "AutoUpdatedVersions.props" );

            if ( !File.Exists( theirAutoUpdatedVersionsFilePath ) )
            {
                context.Console.WriteError( $"File not found: '{theirAutoUpdatedVersionsFilePath}'." );

                errors++;

                continue;
            }

            var theirAutoUpdatedVersionsDocument = XDocument.Load( theirAutoUpdatedVersionsFilePath );

            var releasedVersionPropertyName = $"{dependency.NameWithoutDot}ReleaseVersion";

            var dependencyReleasedVersion = theirAutoUpdatedVersionsDocument.Root?.Element( "Project" )
                ?.Element( "PropertyGroup" )
                ?.Element( releasedVersionPropertyName )
                ?.Value;

            if ( string.IsNullOrEmpty( dependencyReleasedVersion ) )
            {
                context.Console.WriteError( $"Cannot find the '{dependencyReleasedVersion}' in '{theirAutoUpdatedVersionsFilePath}'." );
                errors++;

                continue;
            }

            // Load dependency version from public version.
            var versionElementName = $"{dependency.NameWithoutDot}Version";
            var versionElement = thisAutoUpdatedVersionsPropertyGroupElement.Element( versionElementName );
            var oldVersionValue = versionElement?.Value;

            // We don't need to rewrite the file if there is no change in version.
            if ( oldVersionValue == dependencyReleasedVersion )
            {
                context.Console.WriteMessage( $"Version of '{dependency.Name}' dependency is up to date." );

                continue;
            }

            if ( versionElement == null )
            {
                versionElement = new XElement( versionElementName );
                thisAutoUpdatedVersionsPropertyGroupElement.Add( versionElement );
            }

            versionElement.Value = dependencyReleasedVersion;
            dependenciesUpdated = true;

            context.Console.WriteMessage( $"Setting version dependency '{dependency}' from '{oldVersionValue}' to '{dependencyReleasedVersion}'." );
        }

        if ( errors > 0 )
        {
            return false;
        }

        if ( dependenciesUpdated )
        {
            context.Console.WriteImportantMessage( $"{(settings.Dry ? "Dry run: " : "")}Writing updated '{thisAutoUpdatedVersionsFileName}'." );

            if ( !settings.Dry )
            {
                TextFileHelper.WriteIfDifferent( thisAutoUpdatedVersionsFilePath, thisAutoUpdatedVersionsDocument.ToString(), context );
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

                // Gets the remote origin.
                if ( !GitHelper.TryGetRemoteUrl( context, out var gitOrigin ) )
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