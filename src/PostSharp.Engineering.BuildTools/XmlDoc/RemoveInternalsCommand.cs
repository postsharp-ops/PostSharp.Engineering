// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using JetBrains.Annotations;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.MSBuild;
using PostSharp.Engineering.BuildTools.Build;
using System.Collections.Generic;
using System.IO;
using System.Xml.Linq;

namespace PostSharp.Engineering.BuildTools.XmlDoc;

[UsedImplicitly]
public class RemoveInternalsCommand : BaseCommand<RemoveInternalsCommandSettings>
{
    protected override bool ExecuteCore( BuildContext context, RemoveInternalsCommandSettings settings )
    {
        if ( !File.Exists( settings.XmlPath ) )
        {
            context.Console.WriteError( $"The file '{settings.XmlPath}' does not exist." );

            return false;
        }

        var xmlDocument = XDocument.Load( settings.XmlPath );

        var workspace = MSBuildWorkspace.Create();
        var project = workspace.OpenProjectAsync( settings.ProjectPath ).Result;
        var compilation = project.GetCompilationAsync().Result!;

        var membersToRemove = new List<XElement>();
        var unresolvedSymbols = 0;

        foreach ( var element in xmlDocument.Root!.Element( "members" )!.Elements( "member" ) )
        {
            var id = element.Attribute( "name" )!.Value;
            var symbol = DocumentationCommentId.GetFirstSymbolForDeclarationId( id, compilation );

            if ( symbol == null || !IsVisible( symbol ) )
            {
                if ( symbol == null )
                {
                    unresolvedSymbols++;

                    if ( settings.Verbose )
                    {
                        context.Console.WriteMessage( $"Cannot resolve '{id}'. Removing." );
                    }
                }
                else if ( settings.Verbose )
                {
                    context.Console.WriteMessage( $"Removing '{id}'." );
                }

                membersToRemove.Add( element );
            }
            else { }
        }

        foreach ( var memberToRemove in membersToRemove )
        {
            memberToRemove.Remove();
        }

        if ( membersToRemove.Count > 0 )
        {
            if ( unresolvedSymbols > 0 )
            {
                context.Console.WriteMessage( $"{unresolvedSymbols} symbol(s) could not be resolved from '{settings.XmlPath}'." );
            }

            context.Console.WriteMessage( $"Removed {membersToRemove.Count} internals from '{settings.XmlPath}'." );

            if ( !settings.Dry )
            {
                xmlDocument.Save( settings.XmlPath );
            }
            else
            {
                context.Console.WriteMessage( "Not saving because this is a dry run." );
            }
        }
        else
        {
            context.Console.WriteMessage( $"Nothing to remove from '{settings.XmlPath}'." );
        }

        return true;
    }

    private static bool IsVisible( ISymbol symbol )
        => symbol.DeclaredAccessibility switch
        {
            Accessibility.Internal => false,
            Accessibility.Private => false,
            Accessibility.NotApplicable => false,
            Accessibility.ProtectedAndInternal => false,
            _ => symbol.ContainingType == null || IsVisible( symbol.ContainingType )
        };
}