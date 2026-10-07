// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using PostSharp.Engineering.BuildTools.Utilities;
using System.Text.RegularExpressions;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace PostSharp.Engineering.DocFx.AiSkills;

/// <summary>
/// Generates the index.yml file of the AI skill by parsing Markdown front matter
/// and mirroring the toc.yml hierarchy. Paths in the index are relative to the repository,
/// which is also the layout of the skill directory.
/// </summary>
internal class SkillIndexGenerator
{
    private readonly string _repoDir;
    private readonly string _contentDirectory;
    private readonly string _tocPath;
    private readonly ConsoleHelper _console;
    private readonly Dictionary<string, MarkdownMetadata> _metadataByUid = new();
    private readonly Dictionary<string, string> _pathByUid = new();
    private readonly HashSet<string> _tocArticlePaths = new( StringComparer.OrdinalIgnoreCase );
    private readonly Dictionary<string, List<string>> _linkedUidsByPath = new( StringComparer.OrdinalIgnoreCase );

    // Both delimiters must be complete lines, so that a value containing "---" does not end the front matter.
    // Matches both <xref:uid> and [text](xref:uid#anchor).
    private static readonly Regex _xrefRegex = new( @"xref:([A-Za-z0-9_.`\-]+)", RegexOptions.Compiled );

    private static readonly Regex _frontMatterRegex = new( @"\A---[ \t]*\r?\n(.*?)\r?\n---[ \t]*(\r?\n|\z)", RegexOptions.Singleline | RegexOptions.Compiled );

    public SkillIndexGenerator( string repoDir, string contentDirectory, string tocPath, ConsoleHelper console )
    {
        this._repoDir = repoDir;
        this._contentDirectory = contentDirectory;
        this._tocPath = tocPath;
        this._console = console;
    }

    /// <summary>
    /// Gets the repository-relative paths, with forward slashes, of the articles that the toc lists and of the articles
    /// that they link to with an xref, transitively. Call it after <see cref="GenerateIndex"/>.
    /// </summary>
    public HashSet<string> GetReachableArticlePaths()
    {
        var reachable = new HashSet<string>( this._tocArticlePaths, StringComparer.OrdinalIgnoreCase );
        var queue = new Queue<string>( this._tocArticlePaths );

        while ( queue.TryDequeue( out var path ) )
        {
            if ( !this._linkedUidsByPath.TryGetValue( path, out var linkedUids ) )
            {
                continue;
            }

            foreach ( var uid in linkedUids )
            {
                // API uids and external uids have no article, so they are not in the dictionary.
                if ( this._pathByUid.TryGetValue( uid, out var linkedPath ) && reachable.Add( linkedPath ) )
                {
                    queue.Enqueue( linkedPath );
                }
            }
        }

        return reachable;
    }

    public string GenerateIndex()
    {
        // Step 1: Scan all Markdown files and extract front matter
        this.ScanMarkdownFiles();

        // Step 2: Parse the main toc.yml and build hierarchical structure
        var tocPath = Path.Combine( this._repoDir, this._tocPath );
        var indexItems = this.ParseTocFile( tocPath );

        // Step 3: Serialize to YAML
        var serializer = new SerializerBuilder()
            .WithNamingConvention( CamelCaseNamingConvention.Instance )
            .ConfigureDefaultValuesHandling( DefaultValuesHandling.OmitNull )
            .Build();

        return serializer.Serialize( indexItems );
    }

    private void ScanMarkdownFiles()
    {
        var contentDir = Path.Combine( this._repoDir, this._contentDirectory );

        if ( !Directory.Exists( contentDir ) )
        {
            return;
        }

        foreach ( var file in Directory.GetFiles( contentDir, "*.md", SearchOption.AllDirectories ) )
        {
            var content = File.ReadAllText( file );
            var metadata = ParseMarkdownFrontMatter( content );
            var relativePath = Path.GetRelativePath( this._repoDir, file ).Replace( '\\', '/' );

            if ( metadata != null && !string.IsNullOrEmpty( metadata.Uid ) )
            {
                this._metadataByUid[metadata.Uid] = metadata;
                this._pathByUid[metadata.Uid] = relativePath;
            }

            this._linkedUidsByPath[relativePath] = _xrefRegex.Matches( content ).Select( m => m.Groups[1].Value.TrimEnd( '*' ) ).Distinct().ToList();
        }

        this._console.WriteMessage( $"Scanned {this._metadataByUid.Count} Markdown files with UIDs" );
    }

    private static MarkdownMetadata? ParseMarkdownFrontMatter( string content )
    {
        try
        {

            // Check for YAML front matter (starts with ---)
            var match = _frontMatterRegex.Match( content );

            if ( !match.Success )
            {
                return null;
            }

            var frontMatter = match.Groups[1].Value;

            var deserializer = new DeserializerBuilder()
                .WithNamingConvention( CamelCaseNamingConvention.Instance )
                .IgnoreUnmatchedProperties()
                .Build();

            return deserializer.Deserialize<MarkdownMetadata>( frontMatter );
        }
        catch
        {
            return null;
        }
    }

    // A missing or invalid toc throws, so that the build fails instead of publishing an incomplete index.
    private List<IndexItem> ParseTocFile( string tocPath )
    {
        if ( !File.Exists( tocPath ) )
        {
            throw new FileNotFoundException( $"The toc file '{tocPath}' does not exist.", tocPath );
        }

        TocRoot? tocRoot;

        try
        {
            var content = File.ReadAllText( tocPath );

            var deserializer = new DeserializerBuilder()
                .WithNamingConvention( CamelCaseNamingConvention.Instance )
                .IgnoreUnmatchedProperties()
                .Build();

            tocRoot = deserializer.Deserialize<TocRoot>( content );
        }
        catch ( Exception ex )
        {
            throw new InvalidOperationException( $"Failed to parse '{tocPath}': {ex.Message}", ex );
        }

        if ( tocRoot?.Items == null )
        {
            return new List<IndexItem>();
        }

        return this.ConvertTocItems( tocRoot.Items, Path.GetDirectoryName( tocPath )! );
    }

    private List<IndexItem> ConvertTocItems( List<TocItem> tocItems, string currentDir )
    {
        var result = new List<IndexItem>();

        foreach ( var tocItem in tocItems )
        {
            var indexItem = new IndexItem { Name = tocItem.Name };

            // If there's a topicUid, look up the metadata. The uid lets an agent resolve an <xref:uid> link to the article.
            if ( !string.IsNullOrEmpty( tocItem.TopicUid ) )
            {
                indexItem.Uid = tocItem.TopicUid;

                if ( this._pathByUid.TryGetValue( tocItem.TopicUid, out var path ) )
                {
                    indexItem.Path = path;
                    this._tocArticlePaths.Add( path );
                }

                if ( this._metadataByUid.TryGetValue( tocItem.TopicUid, out var metadata ) )
                {
                    indexItem.Summary = metadata.Summary;
                    indexItem.Keywords = metadata.Keywords;
                }
            }

            // An href to an article lists it like a topicUid does.
            if ( !string.IsNullOrEmpty( tocItem.Href ) && tocItem.Href.EndsWith( ".md", StringComparison.OrdinalIgnoreCase ) )
            {
                var articlePath = Path.GetRelativePath( this._repoDir, Path.Combine( currentDir, tocItem.Href ) ).Replace( '\\', '/' );
                indexItem.Path ??= articlePath;
                this._tocArticlePaths.Add( articlePath );
            }

            // If there's an href to another toc.yml, recurse into it
            if ( !string.IsNullOrEmpty( tocItem.Href ) && tocItem.Href.EndsWith( "toc.yml", StringComparison.Ordinal ) )
            {
                var subTocPath = Path.Combine( currentDir, tocItem.Href.Replace( '/', Path.DirectorySeparatorChar ) );
                var subItems = this.ParseTocFile( subTocPath );

                if ( subItems.Count > 0 )
                {
                    indexItem.Items = subItems;
                }
            }

            // If there are nested items in the current toc
            if ( tocItem.Items != null && tocItem.Items.Count > 0 )
            {
                var childItems = this.ConvertTocItems( tocItem.Items, currentDir );

                if ( childItems.Count > 0 )
                {
                    indexItem.Items = indexItem.Items != null
                        ? indexItem.Items.Concat( childItems ).ToList()
                        : childItems;
                }
            }

            result.Add( indexItem );
        }

        return result;
    }

    // Classes for YAML deserialization
    private class MarkdownMetadata
    {
        public string? Uid { get; set; }

        public string? Summary { get; set; }

        public string? Keywords { get; set; }

        public int? Level { get; set; }
    }

    private class TocRoot
    {
        public List<TocItem>? Items { get; set; }
    }

    private class TocItem
    {
        public string? Name { get; set; }

        public string? TopicUid { get; set; }

        public string? Href { get; set; }

        public List<TocItem>? Items { get; set; }
    }

    // Class for YAML serialization of output
    private class IndexItem
    {
        public string? Name { get; set; }

        public string? Uid { get; set; }

        public string? Path { get; set; }

        public string? Summary { get; set; }

        public string? Keywords { get; set; }

        public List<IndexItem>? Items { get; set; }
    }
}
