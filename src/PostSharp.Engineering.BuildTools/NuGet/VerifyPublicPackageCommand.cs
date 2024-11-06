// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using JetBrains.Annotations;
using NuGet.Versioning;
using PostSharp.Engineering.BuildTools.Utilities;
using Spectre.Console.Cli;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;
using System.Xml.XPath;

namespace PostSharp.Engineering.BuildTools.NuGet
{
    /// <summary>
    /// Verifies that dependencies of the NuGet packages in a given directory are either published to <c>nuget.org</c> or
    /// present in the same directory.
    /// </summary>
    [UsedImplicitly]
    internal class VerifyPublicPackageCommand : Command<VerifyPublicPackageCommandSettings>
    {
        public override int Execute( CommandContext context, VerifyPublicPackageCommandSettings settings )
        {
            var console = new ConsoleHelper();

            return Execute( console, settings ) ? 0 : 1;
        }

        public static bool Execute( ConsoleHelper console, VerifyPublicPackageCommandSettings settings )
        {
            var directory = new DirectoryInfo( settings.Directory );

            var files = Directory.GetFiles( directory.FullName, "*.nupkg" );
            
            // Automatically include respective symbol NuGet packages.
            files = files.Concat(
                    files.Where( f => f.EndsWith( ".nupkg", StringComparison.OrdinalIgnoreCase ) )
                        .Select( f => f[..^".nupkg".Length] + ".snupkg" )
                        .Where( File.Exists ) )
                .ToArray();

            if ( files.Length == 0 )
            {
                return true;
            }

            console.WriteHeading( "Verifying public packages" );
            var success = true;

            Dictionary<string, Task<HttpResponseMessage>> remotePackageTasks = new();

            foreach ( var file in files )
            {
                success &= ProcessPackage( console, directory.FullName, file, remotePackageTasks );
            }

            console.WriteMessage( $"Waiting for {remotePackageTasks.Count} requests to complete." );

            Task.WhenAll( remotePackageTasks.Values ).Wait();

            foreach ( var invalidDependency in remotePackageTasks.Where( p => !p.Value.Result.IsSuccessStatusCode ) )
            {
                console.WriteError( $"The package {invalidDependency.Key} could not be found." );
                success = false;
            }

            if ( success )
            {
                console.WriteSuccess( "Verifying artifacts was successful: no private dependency found." );
            }

            return success;
        }

        private static bool ProcessPackage(
            ConsoleHelper console,
            string directory,
            string inputPath,
            Dictionary<string, Task<HttpResponseMessage>> remotePackageTasks )
        {
            var inputShortPath = Path.GetFileName( inputPath );
            
            console.WriteMessage( $"Verifying {inputShortPath} package." );

            var success = true;

            using var archive = ZipFile.Open( inputPath, ZipArchiveMode.Read );

            var nuspecEntry = archive.Entries.SingleOrDefault(
                entry =>
                    entry.FullName.EndsWith( ".nuspec", StringComparison.OrdinalIgnoreCase ) );

            if ( nuspecEntry == null )
            {
                console.WriteError( $"{inputPath} Cannot find the nuspec file." );

                return false;
            }

            XDocument nuspecXml;
            XmlReader xmlReader;

            using ( var nuspecStream = nuspecEntry.Open() )
            {
                xmlReader = new XmlTextReader( nuspecStream );
                nuspecXml = XDocument.Load( xmlReader );
            }

            var ns = nuspecXml.Root!.Name.Namespace.NamespaceName;

            var namespaceManager = new XmlNamespaceManager( xmlReader.NameTable );
            namespaceManager.AddNamespace( "p", ns );

            var httpClient = new HttpClient();

            // Verify dependencies.
            foreach ( var dependency in nuspecXml.XPathSelectElements( "//p:dependency", namespaceManager ) )
            {
                // Get dependency id and version.
                var dependentId = dependency.Attribute( "id" )!.Value;
                var versionRangeString = dependency.Attribute( "version" )!.Value;

                if ( !VersionRange.TryParse( versionRangeString, out var versionRange ) )
                {
                    console.WriteError( $"{inputShortPath}: cannot parse the version range '{versionRangeString}'." );
                    success = false;

                    continue;
                }

                if ( versionRange.MinVersion == null )
                {
                    console.WriteError( $"{inputShortPath}: Version range '{versionRangeString}' doesn't contain minimal version." );
                    success = false;
                    
                    continue;
                }

                // Check if it's present in the directory.
                var localFile = Path.Combine(
                    directory,
                    dependentId + "." + versionRange.MinVersion.ToNormalizedString() + ".nupkg" );

                if ( !File.Exists( localFile ) )
                {
                    // Check if the dependency is present on nuget.org.
                    var uri =
                        $"https://www.nuget.org/api/v2/package/{dependentId}/{versionRange.MinVersion.ToNormalizedString()}";

                    console.WriteMessage( $"Verifying {uri}" );

                    if ( !remotePackageTasks.ContainsKey( uri ) )
                    {
                        var task = httpClient.SendAsync( new HttpRequestMessage( HttpMethod.Get, uri ) );
                        remotePackageTasks.Add( uri, task );
                    }
                }
            }

            return success;
        }
    }
}