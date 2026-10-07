// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using PostSharp.Engineering.BuildTools.Utilities;
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

    public SkillIndexGenerator( string repoDir, string contentDirectory, string tocPath, ConsoleHelper console )
    {
        this._repoDir = repoDir;
        this._contentDirectory = contentDirectory;
        this._tocPath = tocPath;
        this._console = console;
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
            var metadata = ParseMarkdownFrontMatter( file );

            if ( metadata != null && !string.IsNullOrEmpty( metadata.Uid ) )
            {
                var relativePath = Path.GetRelativePath( this._repoDir, file ).Replace( '\\', '/' );
                this._metadataByUid[metadata.Uid] = metadata;
                this._pathByUid[metadata.Uid] = relativePath;
            }
        }

        this._console.WriteMessage( $"Scanned {this._metadataByUid.Count} Markdown files with UIDs" );
    }

    private static MarkdownMetadata? ParseMarkdownFrontMatter( string filePath )
    {
        try
        {
            var content = File.ReadAllText( filePath );

            // Check for YAML front matter (starts with ---)
            if ( !content.StartsWith( "---", StringComparison.Ordinal ) )
            {
                return null;
            }

            var endIndex = content.IndexOf( "---", 3, StringComparison.Ordinal );

            if ( endIndex < 0 )
            {
                return null;
            }

            var frontMatter = content.Substring( 3, endIndex - 3 ).Trim();

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

    private List<IndexItem> ParseTocFile( string tocPath )
    {
        if ( !File.Exists( tocPath ) )
        {
            return new List<IndexItem>();
        }

        try
        {
            var content = File.ReadAllText( tocPath );

            var deserializer = new DeserializerBuilder()
                .WithNamingConvention( CamelCaseNamingConvention.Instance )
                .IgnoreUnmatchedProperties()
                .Build();

            var tocRoot = deserializer.Deserialize<TocRoot>( content );

            if ( tocRoot?.Items == null )
            {
                return new List<IndexItem>();
            }

            return this.ConvertTocItems( tocRoot.Items, Path.GetDirectoryName( tocPath )! );
        }
        catch ( Exception ex )
        {
            this._console.WriteWarning( $"Failed to parse {tocPath}: {ex.Message}" );

            return new List<IndexItem>();
        }
    }

    private List<IndexItem> ConvertTocItems( List<TocItem> tocItems, string currentDir )
    {
        var result = new List<IndexItem>();

        foreach ( var tocItem in tocItems )
        {
            var indexItem = new IndexItem { Name = tocItem.Name };

            // If there's a topicUid, look up the metadata
            if ( !string.IsNullOrEmpty( tocItem.TopicUid ) )
            {
                if ( this._pathByUid.TryGetValue( tocItem.TopicUid, out var path ) )
                {
                    indexItem.Path = path;
                }

                if ( this._metadataByUid.TryGetValue( tocItem.TopicUid, out var metadata ) )
                {
                    indexItem.Summary = metadata.Summary;
                    indexItem.Keywords = metadata.Keywords;
                }
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

        public string? Path { get; set; }

        public string? Summary { get; set; }

        public string? Keywords { get; set; }

        public List<IndexItem>? Items { get; set; }
    }
}
