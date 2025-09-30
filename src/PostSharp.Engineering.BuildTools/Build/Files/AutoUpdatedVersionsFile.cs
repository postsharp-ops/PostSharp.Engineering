// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using PostSharp.Engineering.BuildTools.Build.Model;
using PostSharp.Engineering.BuildTools.Dependencies.Model;
using PostSharp.Engineering.BuildTools.Utilities;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace PostSharp.Engineering.BuildTools.Build.Files;

internal static class AutoUpdatedVersionsFile
{
    public const string FileName = "AutoUpdatedVersions.props";

    public static bool TryRead(
        BuildContext context,
        [NotNullWhen( true )] out string? dependencyReleasedVersion,
        [NotNullWhen( true )] out string? releasedMainVersionPropertyValue )
        => TryRead(
            context,
            context.Product.DependencyDefinition,
            Path.Combine( context.RepoDirectory, context.Product.AutoUpdatedVersionsFilePath ),
            out dependencyReleasedVersion,
            out releasedMainVersionPropertyValue );

    private static bool TryRead(
        BuildContext context,
        DependencyDefinition dependency,
        string path,
        [NotNullWhen( true )] out string? dependencyReleasedVersion,
        [NotNullWhen( true )] out string? releasedMainVersionPropertyValue )
    {
        var theirAutoUpdatedVersionsDocument = XDocument.Load( path );

        var releasedVersionPropertyName = $"{dependency.NameWithoutDot}ReleaseVersion";

        dependencyReleasedVersion = theirAutoUpdatedVersionsDocument.Root
            ?.Element( "PropertyGroup" )
            ?.Element( releasedVersionPropertyName )
            ?.Value;

        if ( string.IsNullOrEmpty( dependencyReleasedVersion ) )
        {
            context.Console.WriteError( $"The '{releasedVersionPropertyName}' property in '{path}' is not defined." );

            releasedMainVersionPropertyValue = null;
            dependencyReleasedVersion = null;

            return false;
        }

        var releasedMainVersionPropertyName = $"{dependency.NameWithoutDot}ReleaseMainVersion";

        releasedMainVersionPropertyValue = theirAutoUpdatedVersionsDocument.Root
            ?.Element( "PropertyGroup" )
            ?.Element( releasedMainVersionPropertyName )
            ?.Value;

        if ( string.IsNullOrEmpty( releasedMainVersionPropertyValue ) )
        {
            context.Console.WriteError( $"The '{releasedMainVersionPropertyName}' property in '{path}' is not defined." );

            releasedMainVersionPropertyValue = null;
            dependencyReleasedVersion = null;

            return false;
        }

        return true;
    }

    public static bool TryWrite(
        BuildContext context,
        bool dry,
        out bool hasDependenciesChanges,
        out bool hasChanges,
        [NotNullWhen( true )] out string? packageVersion,
        [NotNullWhen( true )] out string? mainVersion )
    {
        context.Console.WriteImportantMessage( $"Checking versions of auto-updated dependencies." );

        hasChanges = false;
        hasDependenciesChanges = false;

        var autoUpdatedDependencies = context.Product.DependencyDefinition.GetAllDependencies( BuildConfiguration.Public )
            .Where( d => d.Definition.AutoUpdateVersion )
            .ToArray();

        // Load XML.
        var thisAutoUpdatedVersionsFilePath = Path.Combine( context.RepoDirectory, context.Product.AutoUpdatedVersionsFilePath );
        var thisAutoUpdatedVersionsDocument = XDocument.Load( thisAutoUpdatedVersionsFilePath, LoadOptions.PreserveWhitespace );
        var thisAutoUpdatedVersionsPropertyGroupElement = thisAutoUpdatedVersionsDocument.Root!.Element( "PropertyGroup" )!;

        // Update dependency versions.
        var errors = 0;
        string? inheritedMainVersion = null;

        foreach ( var dependencyConfiguration in autoUpdatedDependencies )
        {
            var dependency = dependencyConfiguration.Definition;

            string[] filePathCandidates =
            [
                Path.GetFullPath(
                    Path.Combine(
                        context.RepoDirectory,
                        context.Product.SourceDependenciesDirectory,
                        dependency.Name,
                        dependency.EngineeringDirectory,
                        FileName ) ),
                Path.GetFullPath( Path.Combine( context.RepoDirectory, "..", dependency.Name, dependency.EngineeringDirectory, FileName ) )
            ];

            var theirAutoUpdatedVersionsFilePath = filePathCandidates.FirstOrDefault( File.Exists );

            if ( theirAutoUpdatedVersionsFilePath == null )
            {
                context.Console.WriteError( $"None of these files exists: {string.Join( ", ", filePathCandidates.Select( x => $"'{x}'" ) )}." );

                errors++;

                continue;
            }

            if ( !TryRead(
                    context,
                    dependency,
                    theirAutoUpdatedVersionsFilePath,
                    out var dependencyReleasedVersion,
                    out var releasedMainVersionPropertyValue ) )
            {
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
            hasChanges = true;
            hasDependenciesChanges = true;

            context.Console.WriteMessage( $"Setting version dependency '{dependency}' from '{oldVersionValue}' to '{dependencyReleasedVersion}'." );

            // Getting the inherited main version.
            if ( context.Product.MainVersionDependency == dependency )
            {
                inheritedMainVersion = releasedMainVersionPropertyValue;
            }
        }

        // Stop here if errors.
        if ( errors > 0 )
        {
            packageVersion = null;
            mainVersion = null;

            return false;
        }

        // Get the version of this component.
        if ( !MainVersionFile.TryRead( context, out var mainVersionFile ) )
        {
            packageVersion = null;
            mainVersion = null;

            return false;
        }

        if ( !VersionComponents.TryCompute(
                context,
                BuildConfiguration.Public,
                mainVersionFile,
                inheritedMainVersion,
                new VersionSpec( VersionKind.Public ),
                null,
                out var versionComponents ) )
        {
            packageVersion = null;
            mainVersion = null;

            return false;
        }

        // Update our own version.
        var thisVersionElement = thisAutoUpdatedVersionsPropertyGroupElement.Element( $"{context.Product.ProductNameWithoutDot}ReleaseVersion" );
        var thisMainVersionElement = thisAutoUpdatedVersionsPropertyGroupElement.Element( $"{context.Product.ProductNameWithoutDot}ReleaseMainVersion" );

        if ( thisVersionElement == null )
        {
            thisVersionElement = new XElement( $"{context.Product.ProductNameWithoutDot}ReleaseVersion" );
            thisAutoUpdatedVersionsPropertyGroupElement.Add( thisVersionElement );
        }

        if ( thisVersionElement.Value != versionComponents.PackageVersion )
        {
            hasChanges = true;
            thisVersionElement.Value = versionComponents.PackageVersion;
        }

        if ( thisMainVersionElement == null )
        {
            thisMainVersionElement = new XElement( $"{context.Product.ProductNameWithoutDot}ReleaseMainVersion" );
            thisAutoUpdatedVersionsPropertyGroupElement.Add( thisMainVersionElement );
        }

        if ( thisMainVersionElement.Value != versionComponents.MainVersion )
        {
            hasChanges = true;
            thisMainVersionElement.Value = versionComponents.MainVersion;
        }

        // Write changes.
        if ( hasChanges )
        {
            if ( !dry )
            {
                hasChanges = TextFileHelper.WriteIfDifferent( thisAutoUpdatedVersionsFilePath, thisAutoUpdatedVersionsDocument, context );
            }
            else
            {
                context.Console.WriteMessage( $"New content for '{thisAutoUpdatedVersionsFilePath}':" );
                context.Console.WriteMessage( thisAutoUpdatedVersionsDocument.ToNiceString() );
            }
        }

        packageVersion = versionComponents.PackageVersion;
        mainVersion = versionComponents.MainVersion;

        return true;
    }

    public static bool TryWriteAndCommit( BuildContext context, bool dry )
    {
        // Go through all dependencies and update their fixed version in AutoUpdatedVersions.props file.
        if ( !TryWrite( context, dry, out var dependenciesUpdated, out _, out _, out _ ) )
        {
            return false;
        }

        // Commit and push if dependencies versions were updated in previous step.
        if ( dependenciesUpdated )
        {
            if ( dry )
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