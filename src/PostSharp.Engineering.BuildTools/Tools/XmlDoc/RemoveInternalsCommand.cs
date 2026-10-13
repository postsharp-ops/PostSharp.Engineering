// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using JetBrains.Annotations;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.MSBuild;
using PostSharp.Engineering.BuildTools.Build;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace PostSharp.Engineering.BuildTools.Tools.XmlDoc;

[UsedImplicitly]
internal class RemoveInternalsCommand : BaseCommand<RemoveInternalsCommandSettings>
{
    protected override bool ExecuteCore( BuildContext context, RemoveInternalsCommandSettings settings )
    {
        if ( !File.Exists( settings.XmlPath ) )
        {
            context.Console.WriteError( $"The file '{settings.XmlPath}' does not exist." );

            return false;
        }

        // The project path is checked before the workspace opens it, because MSBuildWorkspace throws a FileNotFoundException,
        // and an unhandled exception exits with a stack trace and the exit code of an internal error instead of a message.
        if ( !File.Exists( settings.ProjectPath ) )
        {
            context.Console.WriteError( $"The project '{settings.ProjectPath}' does not exist." );

            return false;
        }

        var xmlDocument = XDocument.Load( settings.XmlPath );

        using var workspace = MSBuildWorkspace.Create( settings.MSBuildProperties );

        var project = workspace
            .OpenProjectAsync( settings.ProjectPath, cancellationToken: context.CancellationToken )
            .Result;

        foreach ( var diagnostic in workspace.Diagnostics )
        {
            context.Console.WriteWarning( $"Loading '{settings.ProjectPath}': {diagnostic.Message}" );
        }

        var compilation = project.GetCompilationAsync( context.CancellationToken ).Result!;

        // A compilation without metadata references resolves almost every signature to an error type, so nearly every member
        // of the documentation file would be reported as unresolved. This happens when the project does not restore for the
        // target framework it was loaded for.
        if ( compilation.ExternalReferences.Length == 0 )
        {
            context.Console.WriteError(
                $"The compilation of '{settings.ProjectPath}' has no metadata reference, so no symbol can be resolved. "
                + "Check the warnings above, and make sure the project restores for the target framework given by --msbuild-property." );

            return false;
        }

        var (membersToRemove, unresolvedMemberIds) = FindMembersToRemove( xmlDocument, compilation );

        if ( settings.Verbose )
        {
            foreach ( var memberToRemove in membersToRemove )
            {
                context.Console.WriteMessage( $"Removing '{memberToRemove.Attribute( "name" )!.Value}'." );
            }

            foreach ( var id in unresolvedMemberIds )
            {
                context.Console.WriteMessage( $"Cannot resolve '{id}'. Keeping." );
            }
        }

        if ( unresolvedMemberIds.Length > 0 )
        {
            context.Console.WriteWarning(
                $"{unresolvedMemberIds.Length} member(s) of '{settings.XmlPath}' could not be resolved and have been kept. Use --verbose to list them." );
        }

        foreach ( var memberToRemove in membersToRemove )
        {
            memberToRemove.Remove();
        }

        if ( membersToRemove.Length > 0 )
        {
            context.Console.WriteMessage( $"Removed {membersToRemove.Length} internals from '{settings.XmlPath}'." );

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

    /// <summary>
    /// Returns the members of <paramref name="xmlDocument"/> whose symbol is not visible outside of the assembly, and the identifiers
    /// of the members whose symbol could not be resolved in <paramref name="compilation"/>. A member that cannot be resolved is kept,
    /// because a resolution failure is not evidence that the member is internal. Explicit interface implementations, for instance,
    /// never resolve, because their documentation comment identifier does not round-trip.
    /// </summary>
    internal static (ImmutableArray<XElement> MembersToRemove, ImmutableArray<string> UnresolvedMemberIds) FindMembersToRemove(
        XDocument xmlDocument,
        Compilation compilation )
    {
        var membersToRemove = ImmutableArray.CreateBuilder<XElement>();
        var unresolvedMemberIds = ImmutableArray.CreateBuilder<string>();

        var members = xmlDocument.Root?.Element( "members" )?.Elements( "member" ) ?? Enumerable.Empty<XElement>();

        foreach ( var element in members )
        {
            var id = element.Attribute( "name" )?.Value;

            if ( id == null )
            {
                continue;
            }

            var symbol = DocumentationCommentId.GetFirstSymbolForDeclarationId( id, compilation );

            if ( symbol == null )
            {
                unresolvedMemberIds.Add( id );
            }
            else if ( !IsVisible( symbol ) )
            {
                membersToRemove.Add( element );
            }
        }

        return (membersToRemove.ToImmutable(), unresolvedMemberIds.ToImmutable());
    }

    private static bool IsVisible( ISymbol symbol )
    {
        // An explicit interface implementation is declared private, but it belongs to the public API surface, because it is
        // reachable through the interface. It is therefore visible when both its containing type and the interface member it
        // implements are visible.
        var explicitImplementations = GetExplicitInterfaceImplementations( symbol );

        if ( !explicitImplementations.IsEmpty )
        {
            return (symbol.ContainingType == null || IsVisible( symbol.ContainingType ))
                   && explicitImplementations.Any( IsVisible );
        }

        return symbol.DeclaredAccessibility switch
        {
            Accessibility.Internal => false,
            Accessibility.Private => false,
            Accessibility.NotApplicable => false,
            Accessibility.ProtectedAndInternal => false,
            _ => symbol.ContainingType == null || IsVisible( symbol.ContainingType )
        };
    }

    private static ImmutableArray<ISymbol> GetExplicitInterfaceImplementations( ISymbol symbol )
        => symbol switch
        {
            IMethodSymbol method => ImmutableArray<ISymbol>.CastUp( method.ExplicitInterfaceImplementations ),
            IPropertySymbol property => ImmutableArray<ISymbol>.CastUp( property.ExplicitInterfaceImplementations ),
            IEventSymbol @event => ImmutableArray<ISymbol>.CastUp( @event.ExplicitInterfaceImplementations ),
            _ => ImmutableArray<ISymbol>.Empty
        };
}