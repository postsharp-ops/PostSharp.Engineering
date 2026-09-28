// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using PostSharp.Engineering.BuildTools.Utilities;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace PostSharp.Engineering.BuildTools.Build.Testing;

/// <summary>
/// The packages of the test applications that the test agents cannot download from nuget.org: the packages that this product
/// and the products it depends on build. See <c>doc/testing-platform.md</c>.
/// </summary>
/// <remarks>
/// <para>
/// A test archive does not hold the files that its application takes from NuGet packages: <c>TestArchive.targets</c> lists
/// them in the manifest, and <c>RunTests.ps1</c> gets each package from the NuGet cache of the agent, from nuget.org, or from
/// <see cref="Directory"/>. A package of another source is one that a build of this product or of a dependency publishes in
/// its artifacts, and the build configuration of a test agent downloads it from that build.
/// </para>
/// <para>
/// A download rule names the package, and not its version, which changes with every build, so <c>generate-scripts</c>
/// needs the packages of each application: it reads them from the restore graph, <c>project.assets.json</c>, with the packages
/// that have a file to publish. <c>Build.ps1 build</c> reads them the same way and fails when they differ from the list that
/// <c>generate-scripts</c> wrote. It also checks that every package that a manifest takes from another source is in the list,
/// which a file that the props of a package add, and the graph does not show, would break.
/// </para>
/// </remarks>
internal static class TestArchivePackages
{
    /// <summary>
    /// The directory, relative to the repository root, to which the build configuration of a test agent downloads the packages
    /// of other sources, in a subdirectory per producer. It is not under <see cref="TestArchives.Directory"/>, which the download
    /// of the archives cleans.
    /// </summary>
    public const string Directory = "artifacts/test-packages";

    private const string _nuGetOrgSource = "https://api.nuget.org/v3/index.json";

    /// <summary>
    /// A package of another source than nuget.org.
    /// </summary>
    /// <param name="Id">The identifier, in the case of the restore graph, which is the case of the file name of the package.</param>
    /// <param name="Producer">The key of the source of the package in <c>nuget.config</c>: the name of this product, or the key
    /// of the dependency whose build publishes it.</param>
    public sealed record Package( string Id, string Version, string Producer )
    {
        /// <summary>
        /// Gets the form of the package in the list of the archives: <c>&lt;producer&gt;/&lt;id&gt;</c>.
        /// </summary>
        public string Reference => $"{this.Producer}/{this.Id}";

        public static (string Producer, string Id) ParseReference( string reference )
        {
            var separator = reference.IndexOf( '/', StringComparison.Ordinal );

            return separator < 0 ? ("", reference) : (reference[..separator], reference[(separator + 1)..]);
        }
    }

    /// <summary>
    /// The sources of <c>nuget.config</c> at the root of the repository, which tell the source of each package.
    /// </summary>
    public sealed class Sources
    {
        private readonly Dictionary<string, string> _sources;
        private readonly List<(string Key, string Pattern)> _mappings;

        private Sources( Dictionary<string, string> sources, List<(string Key, string Pattern)> mappings )
        {
            this._sources = sources;
            this._mappings = mappings;
        }

        public static Sources Empty { get; } = new( [], [] );

        public static bool TryLoad( BuildContext context, out Sources sources )
        {
            var path = Path.Combine( context.RepoDirectory, "nuget.config" );

            if ( !File.Exists( path ) )
            {
                context.Console.WriteError( $"'{path}' does not exist. Prepare the repository first." );
                sources = new Sources( [], [] );

                return false;
            }

            var document = XDocument.Load( path );

            var entries = document.Root?.Element( "packageSources" )?.Elements( "add" )
                              .Select( e => (Key: (string?) e.Attribute( "key" ), Value: (string?) e.Attribute( "value" )) )
                              .Where( e => e.Key != null && e.Value != null )
                              .ToDictionary( e => e.Key!, e => e.Value!, StringComparer.OrdinalIgnoreCase )
                          ?? [];

            var mappings = document.Root?.Element( "packageSourceMapping" )?.Elements( "packageSource" )
                               .SelectMany( s => s.Elements( "package" ).Select( p => ((string) s.Attribute( "key" )!, (string) p.Attribute( "pattern" )!) ) )
                               .ToList()
                           ?? [];

            sources = new Sources( entries, mappings );

            return true;
        }

        /// <summary>
        /// Gets the key of the source of a package: from the package source mapping, as NuGet applies it (an exact identifier,
        /// then the longest prefix), or, without a mapping, from the source that NuGet recorded in the package directory.
        /// </summary>
        public string? GetSourceKey( string id, string packageDirectory )
        {
            if ( this._mappings.Count > 0 )
            {
                var exact = this._mappings.FirstOrDefault( m => m.Pattern.Equals( id, StringComparison.OrdinalIgnoreCase ) );

                if ( exact.Key != null )
                {
                    return exact.Key;
                }

                return this._mappings
                    .Where( m => m.Pattern.EndsWith( '*' ) && id.StartsWith( m.Pattern[..^1], StringComparison.OrdinalIgnoreCase ) )
                    .OrderByDescending( m => m.Pattern.Length )
                    .Select( m => m.Key )
                    .FirstOrDefault();
            }

            var metadataPath = Path.Combine( packageDirectory, ".nupkg.metadata" );

            if ( !File.Exists( metadataPath ) )
            {
                return null;
            }

            using var metadata = JsonDocument.Parse( File.ReadAllText( metadataPath ) );

            if ( !metadata.RootElement.TryGetProperty( "source", out var sourceElement ) || sourceElement.GetString() is not { } source )
            {
                return null;
            }

            return this._sources.FirstOrDefault( s => Normalize( s.Value ) == Normalize( source ) ).Key;

            static string Normalize( string value ) => value.Replace( '\\', '/' ).TrimEnd( '/' ).ToLowerInvariant();
        }

        public bool IsNuGetOrg( string key )
            => this._sources.TryGetValue( key, out var value ) && value.TrimEnd( '/' ).Equals( _nuGetOrgSource, StringComparison.OrdinalIgnoreCase );
    }

    /// <summary>
    /// Gets the packages of an application that do not come from nuget.org, from the <c>project.assets.json</c> file that
    /// the restore of its project wrote.
    /// </summary>
    public static bool TryGetPackages( ConsoleHelper console, Sources sources, TestApplication application, out ImmutableArray<Package> packages )
    {
        packages = [];

        // An archive of the ps1 kind holds files that the project gives it, and no publication.
        if ( application.ProjectAssetsFile.Length == 0 )
        {
            return true;
        }

        if ( !File.Exists( application.ProjectAssetsFile ) )
        {
            console.WriteError( $"'{application.ProjectAssetsFile}' does not exist. The project '{application.ProjectPath}' has not been restored." );

            return false;
        }

        using var document = JsonDocument.Parse( File.ReadAllText( application.ProjectAssetsFile ) );
        var root = document.RootElement;

        var packageFolder = root.GetProperty( "packageFolders" ).EnumerateObject().Select( p => p.Name ).FirstOrDefault();

        if ( packageFolder == null )
        {
            console.WriteError( $"'{application.ProjectAssetsFile}' names no package folder." );

            return false;
        }

        var libraries = root.GetProperty( "libraries" );

        // The target of the framework, and the targets of the framework and a runtime identifier, which the restore of an
        // application with a runtime identifier adds, and of which the publication uses one.
        var names = root.GetProperty( "targets" )
            .EnumerateObject()
            .Where( t => t.Name.Equals( application.TargetFramework, StringComparison.OrdinalIgnoreCase )
                         || t.Name.StartsWith( application.TargetFramework + "/", StringComparison.OrdinalIgnoreCase ) )
            .SelectMany( t => t.Value.EnumerateObject() )
            .Where( l => l.Value.TryGetProperty( "type", out var type ) && type.GetString() == "package" && HasFilesToPublish( l.Value ) )
            .Select( l => l.Name )
            .ToHashSet( StringComparer.OrdinalIgnoreCase );

        var builder = ImmutableArray.CreateBuilder<Package>();
        var success = true;

        foreach ( var name in names )
        {
            var id = name[..name.IndexOf( '/', StringComparison.Ordinal )];
            var version = name[(id.Length + 1)..];

            // The path of the library is <id>/<version> in lower case, which is its directory in the package folder.
            var path = libraries.GetProperty( name ).GetProperty( "path" ).GetString()!;
            var directory = Path.Combine( packageFolder, path.Replace( '/', Path.DirectorySeparatorChar ) );

            var source = sources.GetSourceKey( id, directory );

            if ( source == null )
            {
                console.WriteError( $"The source of the package '{name}' of '{application.ProjectPath}' is not in nuget.config." );
                success = false;

                continue;
            }

            if ( !sources.IsNuGetOrg( source ) )
            {
                builder.Add( new Package( id, version, source ) );
            }
        }

        packages = [..builder.OrderBy( p => p.Reference, StringComparer.Ordinal )];

        return success;
    }

    private static readonly string[] _publishedAssetGroups = ["runtime", "native", "runtimeTargets", "resource", "contentFiles"];

    // Whether a package of a target has a file that the publication copies. A package that has only build files, or the _._
    // placeholder of an empty asset group, such as a package of MSBuild targets, gives the application no file.
    private static bool HasFilesToPublish( JsonElement library )
        => _publishedAssetGroups.Any(
            g => library.TryGetProperty( g, out var group )
                 && group.ValueKind == JsonValueKind.Object
                 && group.EnumerateObject().Any( f => !f.Name.EndsWith( "/_._", StringComparison.Ordinal ) ) );

    /// <summary>
    /// Gets the identifiers, in lower case, of the packages without a URL that the manifest of an archive lists, which the
    /// runner takes from <see cref="Directory"/>.
    /// </summary>
    public static ImmutableArray<string> GetPackagesWithoutUrl( string archivePath )
    {
        using var archive = ZipFile.OpenRead( archivePath );
        var entry = archive.GetEntry( "test.psd1" );

        if ( entry == null )
        {
            return [];
        }

        using var reader = new StreamReader( entry.Open() );

        // TestArchive.targets writes the fields of a package on consecutive lines, in this order.
        return
        [
            ..Regex.Matches( reader.ReadToEnd(), @"Id = '([^']+)'\s+Version = '[^']*'\s+Sha512 = '[^']*'\s+Url = \$null" )
                .Select( m => m.Groups[1].Value )
                .Distinct( StringComparer.Ordinal )
                .Order( StringComparer.Ordinal )
        ];
    }
}
