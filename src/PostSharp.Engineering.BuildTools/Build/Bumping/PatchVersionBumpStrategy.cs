// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using PostSharp.Engineering.BuildTools.Build.Files;
using PostSharp.Engineering.BuildTools.Build.Model;
using System;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace PostSharp.Engineering.BuildTools.Build.Bumping;

public class PatchVersionBumpStrategy : IBumpStrategy
{
    public bool TryBumpVersion(
        Product product,
        BuildContext context,
        [NotNullWhen( true )] out Version? oldVersion,
        [NotNullWhen( true )] out Version? newVersion )
    {
        if ( !MainVersionFile.TryRead( context, out var currentMainVersionFile, out var mainVersionFile ) )
        {
            oldVersion = null;
            newVersion = null;

            return false;
        }

        var oldOurPatchVersion = currentMainVersionFile.OurPatchVersion;
        oldVersion = new Version( currentMainVersionFile.MainVersion );

        if ( oldOurPatchVersion == null )
        {
            context.Console.WriteError( "OurPatchVersion property value is null." );
            oldVersion = null;
            newVersion = null;

            return false;
        }

        // Increment the version.
        var newOurPatchVersion = oldOurPatchVersion + 1;

        // Save the MainVersion.props with new version.
        if ( !TrySavePatchedMainVersion( context, mainVersionFile, newOurPatchVersion.Value ) )
        {
            oldVersion = null;
            newVersion = null;

            return false;
        }

        if ( !MainVersionFile.TryRead( context, out var updatedMainVersionFile ) )
        {
            oldVersion = null;
            newVersion = null;

            return false;
        }

        newVersion = new Version( updatedMainVersionFile.MainVersion );

        context.Console.WriteSuccess( $"Bumping the '{context.Product.ProductName}' version from '{oldVersion}' to '{newVersion}' was successful." );

        return true;
    }

    private static bool TrySavePatchedMainVersion(
        BuildContext context,
        string mainVersionFile,
        int ourPatchVersion )
    {
        if ( !File.Exists( mainVersionFile ) )
        {
            context.Console.WriteError( $"Could not save '{mainVersionFile}': the file does not exist." );

            return false;
        }

        var document = XDocument.Load( mainVersionFile );
        var project = document.Root;
        var properties = project!.Element( "PropertyGroup" );
        var ourPatchVersionElement = properties!.Element( "OurPatchVersion" );

        // Fail on missing <OurPatchVersion> property or replace its value.
        if ( ourPatchVersionElement == null )
        {
            context.Console.WriteError( $"OurPatchVersion property is missing in '{mainVersionFile}'" );

            return false;
        }

        ourPatchVersionElement.Value = ourPatchVersion.ToString( CultureInfo.InvariantCulture );

        // Using settings to keep the indentation as well as encoding identical to original MainVersion.props.
        var xmlWriterSettings =
            new XmlWriterSettings { OmitXmlDeclaration = true, Indent = true, IndentChars = "    ", Encoding = new UTF8Encoding( false ) };

        using ( var xmlWriter = XmlWriter.Create( mainVersionFile, xmlWriterSettings ) )
        {
            document.Save( xmlWriter );
        }

        context.Console.WriteMessage( $"Writing '{mainVersionFile}'." );

        return true;
    }
}