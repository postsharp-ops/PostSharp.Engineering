// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using Microsoft.Build.Evaluation;
using PostSharp.Engineering.BuildTools.Build.MSBuild;
using System;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
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
        int? OurPatchVersion,
        string path )
    {
        this.MainVersion = MainVersion;
        this.OverriddenPatchVersion = OverriddenPatchVersion;
        this.PackageVersionSuffix = PackageVersionSuffix;
        this.OurPatchVersion = OurPatchVersion;
        this.FilePath = path;
    }

    public string FilePath { get; }

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
            ourPatchVersion != null ? int.Parse( ourPatchVersion, CultureInfo.InvariantCulture ) : null,
            versionFile.FullPath );

        return true;
    }

    public bool TryWrite(
        BuildContext context,
        Version version,
        int? patchVersion,
        out MainVersionFile updatedFile )
        => this.TryWrite( context, version.ToString(), patchVersion, out updatedFile );

    public bool TryWrite(
        BuildContext context,
        string version,
        int? patchVersion,
        out MainVersionFile updatedFile )
    {
        updatedFile = this;

        if ( !File.Exists( this.FilePath ) )
        {
            context.Console.WriteError( $"Could not save '{this.FilePath}': the file does not exist." );

            return false;
        }

        var document = XDocument.Load( this.FilePath );
        var project = document.Root;
        var properties = project!.Element( "PropertyGroup" );

        var mainVersionElement = properties!.Element( "MainVersion" );
        mainVersionElement!.Value = version;

        updatedFile = updatedFile with { MainVersion = version };

        if ( patchVersion != null )
        {
            var ourPatchVersionElement = properties!.Element( "OurPatchVersion" );

            // Fail on missing <OurPatchVersion> property or replace its value.
            if ( ourPatchVersionElement == null )
            {
                context.Console.WriteError( $"OurPatchVersion property is missing in '{this.FilePath}'" );

                return false;
            }

            ourPatchVersionElement.Value = patchVersion.Value.ToString( CultureInfo.InvariantCulture );
            updatedFile = updatedFile with { OurPatchVersion = patchVersion };
        }

        // Using settings to keep the indentation as well as encoding identical to original MainVersion.props.
        var xmlWriterSettings =
            new XmlWriterSettings { OmitXmlDeclaration = true, Indent = true, IndentChars = "    ", Encoding = new UTF8Encoding( false ) };

        using ( var xmlWriter = XmlWriter.Create( this.FilePath, xmlWriterSettings ) )
        {
            document.Save( xmlWriter );
        }

        context.Console.WriteMessage( $"Writing '{this.FilePath}': MainVersion='{version};, OurPatchVersion='patchVersion'." );

        return true;
    }
}