// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using Microsoft.Build.Evaluation;
using PostSharp.Engineering.BuildTools.Build.MSBuild;
using System;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace PostSharp.Engineering.BuildTools.Build.Files;

/// <summary>
/// Represents and reads the file <c>eng/MainVersion.props</c>.
/// </summary>
internal record MainVersionFile
{
    private MainVersionFile(
        string MainVersion,
        string? OverriddenPatchVersion,
        string PackageVersionSuffix,
        int? OurPatchVersion )
    {
        this.MainVersion = MainVersion;
        this.OverriddenPatchVersion = OverriddenPatchVersion;
        this.PackageVersionSuffix = PackageVersionSuffix;
        this.OurPatchVersion = OurPatchVersion;
    }

    public string Release => new Version( this.MainVersion ).ToString( 2 );

    public string MainVersion { get; init; }

    public string? OverriddenPatchVersion { get; init; }

    public string PackageVersionSuffix { get; init; }

    public int? OurPatchVersion { get; init; }

    /// <summary>
    /// Reads MainVersion.props but does not interpret anything.
    /// </summary>
    public static bool TryRead(
        BuildContext context,
        [NotNullWhen( true )] out MainVersionFile? mainVersionFileInfo )
        => TryRead( context, out mainVersionFileInfo, out _ );

    /// <summary>
    /// Reads MainVersion.props but does not interpret anything.
    /// </summary>
    public static bool TryRead(
        BuildContext context,
        [NotNullWhen( true )] out MainVersionFile? mainVersionFileInfo,
        out string mainVersionFilePath )
    {
        var product = context.Product;

        mainVersionFileInfo = null;

        mainVersionFilePath = Path.Combine(
            context.RepoDirectory,
            product.MainVersionFilePath );

        if ( !File.Exists( mainVersionFilePath ) )
        {
            context.Console.WriteError( $"The file '{mainVersionFilePath}' does not exist." );

            return false;
        }

        var versionFile = Project.FromFile( mainVersionFilePath, MSBuildLoadOptions.IgnoreImportErrors );

        return TryRead( context, versionFile, out mainVersionFileInfo );
    }

    public static bool TryParse( BuildContext context, string content, [NotNullWhen( true )] out MainVersionFile? mainVersionFileInfo )
    {
        var document = XDocument.Parse( content );
        var project = Project.FromXmlReader( document.CreateReader(), MSBuildLoadOptions.IgnoreImportErrors );

        return TryRead( context, project, out mainVersionFileInfo );
    }

    public static bool TryRead( BuildContext context, Project versionFile, [NotNullWhen( true )] out MainVersionFile? mainVersionFileInfo )
    {
        var mainVersion = versionFile
            .Properties
            .SingleOrDefault( p => p.Name == "MainVersion" )
            ?.EvaluatedValue;

        var overriddenPatchVersion = versionFile
            .Properties
            .SingleOrDefault( p => p.Name == "OverriddenPatchVersion" )
            ?.EvaluatedValue;

        var ourPatchVersion = versionFile
            .Properties
            .SingleOrDefault( p => p.Name == "OurPatchVersion" )
            ?.EvaluatedValue;

        if ( string.IsNullOrEmpty( mainVersion ) )
        {
            context.Console.WriteError( $"MainVersion should not be null in '{versionFile.FullPath}'." );

            mainVersionFileInfo = null;

            return false;
        }

        var suffix = versionFile
                         .Properties
                         .SingleOrDefault( p => p.Name == "PackageVersionSuffix" )
                         ?.EvaluatedValue
                     ?? "";

        // Empty suffixes are allowed and mean RTM.

        ProjectCollection.GlobalProjectCollection.UnloadAllProjects();

        mainVersionFileInfo = new MainVersionFile(
            mainVersion,
            overriddenPatchVersion,
            suffix,
            ourPatchVersion != null ? int.Parse( ourPatchVersion, CultureInfo.InvariantCulture ) : null );

        return true;
    }
}