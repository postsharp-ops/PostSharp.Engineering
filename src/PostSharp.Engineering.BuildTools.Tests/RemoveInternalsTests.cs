// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using PostSharp.Engineering.BuildTools.Tools.XmlDoc;
using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Xunit;

namespace PostSharp.Engineering.BuildTools.Tests;

public class RemoveInternalsTests
{
    private const string _code =
        """
        using System.Collections;
        using System.Collections.Generic;

        namespace Ns
        {
            internal interface IInternalInterface
            {
                void MethodOfInternalInterface();
            }

            public class PublicClass : IEnumerable<int>, IInternalInterface
            {
                public void PublicMethod() { }
                internal void InternalMethod() { }
                IEnumerator<int> IEnumerable<int>.GetEnumerator() => null!;
                IEnumerator IEnumerable.GetEnumerator() => null!;
                void IInternalInterface.MethodOfInternalInterface() { }
            }

            internal class InternalClass
            {
                public void PublicMethodOfInternalClass() { }
            }
        }
        """;

    private static Compilation CreateCompilation()
        => CSharpCompilation.Create(
            "Test",
            [CSharpSyntaxTree.ParseText( _code )],
            ((string) AppContext.GetData( "TRUSTED_PLATFORM_ASSEMBLIES" )!).Split( Path.PathSeparator ).Select( p => MetadataReference.CreateFromFile( p ) ),
            new CSharpCompilationOptions( OutputKind.DynamicallyLinkedLibrary ) );

    private static XDocument CreateXmlDocument( params string[] memberIds )
        => new(
            new XElement(
                "doc",
                new XElement(
                    "members",
                    memberIds.Select( id => new XElement( "member", new XAttribute( "name", id ), new XElement( "summary", "Summary." ) ) ) ) ) );

    [Fact]
    public void VisibleMembersAreKeptAndInvisibleOnesAreRemoved()
    {
        var xmlDocument = CreateXmlDocument(
            "T:Ns.PublicClass",
            "M:Ns.PublicClass.PublicMethod",
            "M:Ns.PublicClass.InternalMethod",
            "T:Ns.InternalClass",
            "M:Ns.InternalClass.PublicMethodOfInternalClass" );

        var (membersToRemove, unresolvedMemberIds) = RemoveInternalsCommand.FindMembersToRemove( xmlDocument, CreateCompilation() );

        Assert.Empty( unresolvedMemberIds );

        Assert.Equal(
            ["M:Ns.PublicClass.InternalMethod", "T:Ns.InternalClass", "M:Ns.InternalClass.PublicMethodOfInternalClass"],
            membersToRemove.Select( m => m.Attribute( "name" )!.Value ) );
    }

    [Fact]
    public void ExplicitInterfaceImplementationsAreKeptUnlessTheInterfaceIsInternal()
    {
        // An explicit interface implementation is declared private, but the documentation of a public interface must be kept.
        var xmlDocument = CreateXmlDocument(
            "M:Ns.PublicClass.System#Collections#IEnumerable#GetEnumerator",
            "M:Ns.PublicClass.Ns#IInternalInterface#MethodOfInternalInterface" );

        var (membersToRemove, unresolvedMemberIds) = RemoveInternalsCommand.FindMembersToRemove( xmlDocument, CreateCompilation() );

        Assert.Empty( unresolvedMemberIds );

        var removedMember = Assert.Single( membersToRemove );
        Assert.Equal( "M:Ns.PublicClass.Ns#IInternalInterface#MethodOfInternalInterface", removedMember.Attribute( "name" )!.Value );
    }

    [Fact]
    public void UnresolvedMembersAreKeptAndReported()
    {
        // The identifier of an explicit interface implementation, as the C# compiler writes it, does not resolve back to a symbol.
        const string explicitImplementationId = "M:Ns.PublicClass.System#Collections#Generic#IEnumerable{System#Int32}#GetEnumerator";

        var xmlDocument = CreateXmlDocument( explicitImplementationId, "M:Ns.PublicClass.MethodOfAnotherTargetFramework" );

        var (membersToRemove, unresolvedMemberIds) = RemoveInternalsCommand.FindMembersToRemove( xmlDocument, CreateCompilation() );

        Assert.Empty( membersToRemove );

        Assert.Equal(
            [explicitImplementationId, "M:Ns.PublicClass.MethodOfAnotherTargetFramework"],
            unresolvedMemberIds.ToArray() );
    }

    [Fact]
    public void MSBuildPropertiesArePassedAsGlobalProperties()
    {
        var settings = new RemoveInternalsCommandSettings { UnparsedMSBuildProperties = ["TargetFramework=net8.0", "Configuration=Release"] };

        Assert.Equal( "net8.0", settings.MSBuildProperties["TargetFramework"] );
        Assert.Equal( "Release", settings.MSBuildProperties["Configuration"] );
    }
}