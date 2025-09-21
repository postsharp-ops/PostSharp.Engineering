// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using JetBrains.Annotations;
using PostSharp.Engineering.BuildTools.Utilities;
using Spectre.Console.Cli;
using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml;
using System.Xml.Linq;
using System.Xml.XPath;

namespace PostSharp.Engineering.BuildTools.Tools.NuGet
{
    /// <summary>
    /// Renames <c>Microsoft</c> into <c>Metalama.Roslyn</c> in all NuGet packages of a directory.
    /// </summary>
    [UsedImplicitly]
    public class RenamePackagesCommand : Command<RenamePackageCommandSettings>
    {
        public override int Execute( CommandContext context, RenamePackageCommandSettings settings )
            => Execute( new ConsoleHelper(), settings ) ? 0 : 2;

        [PublicAPI]
        public static bool Execute( ConsoleHelper console, RenamePackageCommandSettings settings )
        {
            var success = true;

            var directory = new DirectoryInfo( settings.Directory );

            var files = Directory.GetFiles( directory.FullName, "Microsoft.*.nupkg" )
                .Concat( Directory.GetFiles( directory.FullName, "Microsoft.*.snupkg" ) )
                .ToArray();

            if ( files.Length == 0 )
            {
                console.WriteError( $"No matching package found in '{directory.FullName}'." );

                return false;
            }

            foreach ( var file in files )
            {
                success &= RenamePackage( console, directory.FullName, file );
            }

            return success;
        }

        private static bool RenamePackage( ConsoleHelper console, string directory, string inputPath )
        {
            console.WriteMessage( "Processing " + inputPath );

            var outputPath = Path.Combine(
                Path.GetDirectoryName( inputPath )!,
                Path.GetFileName( inputPath )
                    .Replace( "Microsoft", "Metalama.Roslyn", StringComparison.OrdinalIgnoreCase ) );

            File.Copy( inputPath, outputPath, true );

            using var archive = ZipFile.Open( outputPath, ZipArchiveMode.Update );

            var oldNuspecEntry = archive.Entries.SingleOrDefault(
                entry =>
                    entry.FullName.EndsWith( ".nuspec", StringComparison.OrdinalIgnoreCase ) );

            if ( oldNuspecEntry == null )
            {
                console.WriteError( "Usage: Cannot find the nuspec file." );

                return false;
            }

            XDocument nuspecXml;
            XmlReader xmlReader;

            using ( var nuspecStream = oldNuspecEntry.Open() )
            {
                xmlReader = new XmlTextReader( nuspecStream );
                nuspecXml = XDocument.Load( xmlReader );
            }

            var ns = nuspecXml.Root!.Name.Namespace.NamespaceName;

            // Rename the packageId.
            var packageIdElement =
                nuspecXml.Root.Element( XName.Get( "metadata", ns ) )!.Element( XName.Get( "id", ns ) )!;

            var oldPackageId = packageIdElement.Value;

            var newPackageId =
                oldPackageId.Replace( "Microsoft", "Metalama.Roslyn", StringComparison.OrdinalIgnoreCase );

            var packageVersion = nuspecXml.Root.Element( XName.Get( "metadata", ns ) )!
                .Element( XName.Get( "version", ns ) )!.Value;

            packageIdElement.Value = newPackageId;

            // Rename the dependencies.
            var namespaceManager = new XmlNamespaceManager( xmlReader.NameTable );
            namespaceManager.AddNamespace( "p", ns );

            foreach ( var dependency in nuspecXml.XPathSelectElements( "//p:dependency", namespaceManager ) )
            {
                var dependentId = dependency.Attribute( "id" )!.Value;

                if ( dependentId.StartsWith( "Microsoft", StringComparison.OrdinalIgnoreCase ) )
                {
                    var dependencyPath = Path.Combine( directory, dependentId + "." + packageVersion + ".nupkg" );

                    if ( File.Exists( dependencyPath ) )
                    {
                        dependency.Attribute( "id" )!.Value = dependentId.Replace(
                            "Microsoft",
                            "Metalama.Roslyn",
                            StringComparison.OrdinalIgnoreCase );
                    }
                    else
                    {
                        // The dependency is not produced by this repo.
                    }
                }
            }

            oldNuspecEntry.Delete();
            var newNuspecEntry = archive.CreateEntry( newPackageId + ".nuspec", CompressionLevel.Optimal );

            using ( var outputStream = newNuspecEntry.Open() )
            {
                nuspecXml.Save( outputStream );
            }

            return true;
        }
    }
}