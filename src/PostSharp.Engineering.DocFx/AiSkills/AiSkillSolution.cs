// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using JetBrains.Annotations;
using PostSharp.Engineering.BuildTools.Build;
using PostSharp.Engineering.BuildTools.Build.Model;
using PostSharp.Engineering.BuildTools.Utilities;
using System.IO.Compression;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace PostSharp.Engineering.DocFx.AiSkills;

/// <summary>
/// Solution that builds an AI skill from the documentation, packaged as a plugin marketplace for
/// Claude Code and OpenAI Codex. The output goes to <c>artifacts/marketplace</c>, and <see cref="Pack"/>
/// zips it to <c>artifacts/publish/private</c>, from where a <c>GitRepoPublisher</c> can push it to the
/// marketplace repository.
/// </summary>
/// <remarks>
/// This solution must come after <see cref="DocFxApiSolution"/> in the product's solutions, because it reads the API metadata.
/// Markdown, toc files and code samples are copied verbatim: the hand-written <c>SKILL.md</c> tells the agent how to
/// resolve xrefs and directives.
/// </remarks>
[PublicAPI]
public class AiSkillSolution : Solution
{
    private const string _scriptResourcePrefix = "AiSkills.Scripts.";

    private static readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private readonly AiSkillOptions _options;

    /// <summary>
    /// Initializes a new instance of the <see cref="AiSkillSolution"/> class.
    /// </summary>
    /// <param name="options">The description of the skill and the plugin, and the locations of their sources in the repository.</param>
    public AiSkillSolution( AiSkillOptions options ) : base( "AI Skill" )
    {
        this._options = options;
        this.BuildMethod = BuildTools.Build.Model.BuildMethod.Pack;
    }

    /// <summary>
    /// Reads the plugin description from the <c>description:</c> field of the <c>SKILL.md</c>
    /// front matter, so that the skill, plugin, and marketplace manifests cannot drift apart.
    /// </summary>
    private string GetPluginDescription( string repoDir )
    {
        var skillPath = Path.Combine( repoDir, this._options.SkillSourceDirectory, "SKILL.md" );

        if ( File.Exists( skillPath ) )
        {
            var match = Regex.Match( File.ReadAllText( skillPath ), @"^description:\s*(.+)$", RegexOptions.Multiline );

            if ( match.Success )
            {
                return match.Groups[1].Value.Trim();
            }
        }

        return this._options.FallbackDescription;
    }

    private static string GetMarketplaceOutputDirectory( string repoDir ) => Path.Combine( repoDir, "artifacts", "marketplace" );

    private string GetPluginDirectory( string marketplaceOutputDir ) => Path.Combine( marketplaceOutputDir, "plugins", this._options.PluginName );

    /// <summary>
    /// Builds the marketplace under <c>artifacts/marketplace</c>, with the version of the product family in the manifests.
    /// </summary>
    public override bool Build( BuildContext context, BuildSettings settings )
        => this.Build( context.RepoDirectory, context.Product.ProductFamily.Version, context.Console );

    // Does not take a BuildContext so that it can be tested without one.
    internal bool Build( string repoDir, string version, ConsoleHelper console )
    {
        var marketplaceOutputDir = GetMarketplaceOutputDirectory( repoDir );
        var description = this.GetPluginDescription( repoDir );
        var skillSourceDir = Path.Combine( repoDir, this._options.SkillSourceDirectory );

        console.WriteHeading( "Building the AI skill marketplace" );

        try
        {
            if ( Directory.Exists( marketplaceOutputDir ) )
            {
                Directory.Delete( marketplaceOutputDir, true );
            }

            Directory.CreateDirectory( marketplaceOutputDir );

            var marketplaceConfigDir = Path.Combine( marketplaceOutputDir, ".claude-plugin" );
            var codexMarketplaceConfigDir = Path.Combine( marketplaceOutputDir, ".agents", "plugins" );
            var pluginDir = this.GetPluginDirectory( marketplaceOutputDir );
            var pluginConfigDir = Path.Combine( pluginDir, ".claude-plugin" );
            var codexPluginConfigDir = Path.Combine( pluginDir, ".codex-plugin" );
            var skillDir = Path.Combine( pluginDir, "skills", this._options.PluginName );

            Directory.CreateDirectory( marketplaceConfigDir );
            Directory.CreateDirectory( codexMarketplaceConfigDir );
            Directory.CreateDirectory( pluginConfigDir );
            Directory.CreateDirectory( codexPluginConfigDir );
            Directory.CreateDirectory( skillDir );

            // 1. Manifests. Claude Code reads .claude-plugin; Codex reads .agents/plugins and .codex-plugin.
            this.GenerateMarketplaceJson( marketplaceConfigDir, version, description );
            this.GenerateCodexMarketplaceJson( codexMarketplaceConfigDir, description );
            this.GeneratePluginJson( pluginConfigDir, version, description );
            this.GenerateCodexPluginJson( codexPluginConfigDir, version, description );
            console.WriteMessage( "Generated the marketplace and plugin manifests." );

            // 2. README.md of the marketplace repository.
            var readmeSourcePath = Path.Combine( skillSourceDir, "README.md" );

            if ( File.Exists( readmeSourcePath ) )
            {
                File.Copy( readmeSourcePath, Path.Combine( marketplaceOutputDir, "README.md" ), true );
                console.WriteMessage( "Copied README.md." );
            }

            // 3. SKILL.md, with the version placeholder replaced.
            var skillSourcePath = Path.Combine( skillSourceDir, "SKILL.md" );

            if ( File.Exists( skillSourcePath ) )
            {
                var skillContent = File.ReadAllText( skillSourcePath );
                skillContent = skillContent.Replace( "<version>", version, StringComparison.Ordinal );
                File.WriteAllText( Path.Combine( skillDir, "SKILL.md" ), skillContent );
                console.WriteMessage( $"Copied SKILL.md (version: {version})." );
            }
            else
            {
                console.WriteWarning( $"SKILL.md not found at {skillSourcePath}." );
            }

            // 4. Conceptual documentation, verbatim. It keeps its repository-relative path so that the paths in index.yml resolve.
            var contentSourceDir = Path.Combine( repoDir, this._options.ContentDirectory );
            var contentDestDir = Path.Combine( skillDir, this._options.ContentDirectory );
            // The index is generated first, because it gives the articles that the toc lists.
            var indexGenerator = new SkillIndexGenerator( repoDir, this._options.ContentDirectory, this._options.TocPath, console );
            var index = indexGenerator.GenerateIndex();

            Func<string, bool>? articleFilter = null;

            if ( this._options.IncludeOnlyReachableArticles )
            {
                var reachableArticlePaths = indexGenerator.GetReachableArticlePaths();

                articleFilter = relativePath => reachableArticlePaths.Contains(
                    Path.Combine( this._options.ContentDirectory, relativePath ).Replace( '\\', '/' ) );
            }

            CopyDirectory( contentSourceDir, contentDestDir, "*.md", articleFilter );
            CopyDirectory( contentSourceDir, contentDestDir, "*.yml" );
            console.WriteMessage( $"Copied {this._options.ContentDirectory}/." );

            // 5. Code samples, verbatim.
            if ( this._options.CodeDirectory != null )
            {
                CopyDirectory( Path.Combine( repoDir, this._options.CodeDirectory ), Path.Combine( skillDir, this._options.CodeDirectory ), "*.cs" );
                console.WriteMessage( $"Copied {this._options.CodeDirectory}/." );
            }

            // 6. Helper scripts and assets. The product-neutral scripts come from this assembly; a repository can add
            // its own scripts, or override ours, in its scripts directory.
            WriteBundledScripts( Path.Combine( skillDir, "scripts" ) );
            CopyDirectory( Path.Combine( skillSourceDir, "scripts" ), Path.Combine( skillDir, "scripts" ), "*" );
            CopyDirectory( Path.Combine( skillSourceDir, "assets" ), Path.Combine( skillDir, "assets" ), "*" );

            // 7. API documentation, without the rendering-only sections.
            this.CopyApiDocumentation( console, Path.Combine( repoDir, this._options.ApiDirectory ), Path.Combine( skillDir, "api" ) );

            // 8. index.yml.
            File.WriteAllText( Path.Combine( skillDir, "index.yml" ), index );
            console.WriteMessage( "Generated index.yml." );

            console.WriteSuccess( $"The AI skill marketplace was created at {marketplaceOutputDir}." );

            return true;
        }
        catch ( Exception ex )
        {
            console.WriteError( $"Failed to build the AI skill marketplace: {ex.Message}" );

            return false;
        }
    }

    private void CopyApiDocumentation( ConsoleHelper console, string apiSourceDir, string apiDestDir )
    {
        if ( !Directory.Exists( apiSourceDir ) )
        {
            console.WriteWarning( $"API directory not found at {apiSourceDir}. Run the DocFx API generation first." );

            return;
        }

        var apiFileCount = 0;

        foreach ( var file in Directory.GetFiles( apiSourceDir, "*.yml", SearchOption.AllDirectories ) )
        {
            var relativePath = ApiDocTransformer.GetRelocatedPath( Path.GetRelativePath( apiSourceDir, file ), this._options.ApiRelocations );
            var destPath = Path.Combine( apiDestDir, relativePath );
            Directory.CreateDirectory( Path.GetDirectoryName( destPath )! );
            File.WriteAllText( destPath, ApiDocTransformer.StripRenderingSections( File.ReadAllText( file ) ) );
            apiFileCount++;
        }

        // The .manifest maps UIDs to files; rewrite the paths of relocated files.
        var manifestSource = Path.Combine( apiSourceDir, ".manifest" );

        if ( File.Exists( manifestSource ) )
        {
            Directory.CreateDirectory( apiDestDir );

            File.WriteAllText(
                Path.Combine( apiDestDir, ".manifest" ),
                ApiDocTransformer.TransformManifest( File.ReadAllText( manifestSource ), this._options.ApiRelocations ) );
        }

        console.WriteMessage( $"Copied api/ ({apiFileCount} files, rendering sections stripped)." );
    }

    private void GenerateMarketplaceJson( string marketplaceDir, string version, string description )
    {
        var marketplace = new
        {
            name = this._options.PluginName,
            owner = new { name = this._options.OwnerName, email = this._options.OwnerEmail },
            description = this._options.MarketplaceDescription,
            plugins = new[] { new { name = this._options.PluginName, source = $"./plugins/{this._options.PluginName}", description, version } }
        };

        WriteJson( Path.Combine( marketplaceDir, "marketplace.json" ), marketplace );
    }

    private void GenerateCodexMarketplaceJson( string codexMarketplaceDir, string description )
    {
        // OpenAI Codex marketplace catalog (https://developers.openai.com/codex/plugins/build).
        // Local plugin sources are resolved relative to the repository root. Codex requires the policy and the
        // category of each entry; the plugin has no connected service, so authentication never actually happens.
        var marketplace = new
        {
            name = this._options.PluginName,
            @interface = new { displayName = this._options.DisplayName },
            plugins = new[]
            {
                new
                {
                    name = this._options.PluginName,
                    description,
                    source = $"./plugins/{this._options.PluginName}",
                    policy = new { installation = "AVAILABLE", authentication = "ON_INSTALL" },
                    category = this._options.Category
                }
            }
        };

        WriteJson( Path.Combine( codexMarketplaceDir, "marketplace.json" ), marketplace );
    }

    private void GeneratePluginJson( string pluginConfigDir, string version, string description )
    {
        var plugin = new { name = this._options.PluginName, version, description };

        WriteJson( Path.Combine( pluginConfigDir, "plugin.json" ), plugin );
    }

    private void GenerateCodexPluginJson( string codexPluginConfigDir, string version, string description )
    {
        // Codex requires .codex-plugin/plugin.json and does not fall back to .claude-plugin/plugin.json.
        // The skills/ layout is shared with Claude Code. In this compatibility format, the presentation fields go
        // under interface (https://developers.openai.com/plugins/deploy/submission).
        var plugin = new
        {
            name = this._options.PluginName,
            version,
            description,
            author = new { name = this._options.OwnerName, email = this._options.OwnerEmail },
            homepage = this._options.Homepage,
            repository = this._options.RepositoryUrl,
            keywords = this._options.Keywords.ToArray(),
            skills = "./skills/",
            @interface = new
            {
                displayName = this._options.DisplayName,
                shortDescription = this._options.MarketplaceDescription,
                developerName = this._options.OwnerName,
                category = this._options.Category,
                websiteURL = this._options.Homepage
            }
        };

        WriteJson( Path.Combine( codexPluginConfigDir, "plugin.json" ), plugin );
    }

    private static void WriteBundledScripts( string scriptsDir )
    {
        var assembly = typeof(AiSkillSolution).Assembly;

        Directory.CreateDirectory( scriptsDir );

        foreach ( var resourceName in assembly.GetManifestResourceNames().Where( n => n.StartsWith( _scriptResourcePrefix, StringComparison.Ordinal ) ) )
        {
            using var resource = assembly.GetManifestResourceStream( resourceName )!;
            using var file = File.Create( Path.Combine( scriptsDir, resourceName.Substring( _scriptResourcePrefix.Length ) ) );
            resource.CopyTo( file );
        }
    }

    private static void WriteJson( string path, object value ) => File.WriteAllText( path, JsonSerializer.Serialize( value, _jsonOptions ) );

    private static void CopyDirectory( string sourceDir, string destDir, string pattern, Func<string, bool>? filter = null )
    {
        if ( !Directory.Exists( sourceDir ) )
        {
            return;
        }

        foreach ( var file in Directory.GetFiles( sourceDir, pattern, SearchOption.AllDirectories ) )
        {
            var relativePath = Path.GetRelativePath( sourceDir, file );

            // Skip build outputs (e.g. compiler-generated .cs files under obj/).
            if ( IsInBuildOutputDirectory( relativePath ) || (filter != null && !filter( relativePath )) )
            {
                continue;
            }

            var destPath = Path.Combine( destDir, relativePath );
            Directory.CreateDirectory( Path.GetDirectoryName( destPath )! );
            File.Copy( file, destPath, true );
        }
    }

    private static bool IsInBuildOutputDirectory( string relativePath )
        => relativePath.Split( Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar )
            .Any( segment => segment.Equals( "obj", StringComparison.OrdinalIgnoreCase ) || segment.Equals( "bin", StringComparison.OrdinalIgnoreCase ) );

    /// <summary>
    /// Builds the marketplace with the full package version in the manifests, and zips it to
    /// <c>artifacts/publish/private/{ZipFilePrefix}.{PackageVersion}.zip</c>.
    /// </summary>
    public override bool Pack( BuildContext context, BuildSettings settings )
    {
        if ( !this.Build( context, settings ) )
        {
            return false;
        }

        if ( !BuildArguments.TryReadFromAutoUpdatedVersionsFile( context, settings.BuildConfiguration, out var buildArguments ) )
        {
            context.Console.WriteError( "Failed to read the package version from AutoUpdatedVersions.props." );

            return false;
        }

        if ( buildArguments.PackageVersion == null )
        {
            context.Console.WriteError( "The package version is null." );

            return false;
        }

        var repoDir = context.RepoDirectory;
        var marketplaceOutputDir = GetMarketplaceOutputDirectory( repoDir );
        var pluginDir = this.GetPluginDirectory( marketplaceOutputDir );
        var publishDir = Path.Combine( repoDir, "artifacts", "publish", "private" );
        var zipFileName = $"{this._options.ZipFilePrefix}.{buildArguments.PackageVersion}.zip";
        var zipPath = Path.Combine( publishDir, zipFileName );

        try
        {
            // The packed manifests carry the full package version, so that agents detect updates between builds of the same family.
            // The Codex marketplace manifest has no version.
            var description = this.GetPluginDescription( repoDir );
            this.GenerateMarketplaceJson( Path.Combine( marketplaceOutputDir, ".claude-plugin" ), buildArguments.PackageVersion, description );
            this.GeneratePluginJson( Path.Combine( pluginDir, ".claude-plugin" ), buildArguments.PackageVersion, description );
            this.GenerateCodexPluginJson( Path.Combine( pluginDir, ".codex-plugin" ), buildArguments.PackageVersion, description );
            context.Console.WriteMessage( $"Updated the manifests with the package version {buildArguments.PackageVersion}." );

            Directory.CreateDirectory( publishDir );

            if ( File.Exists( zipPath ) )
            {
                File.Delete( zipPath );
            }

            ZipFile.CreateFromDirectory( marketplaceOutputDir, zipPath, CompressionLevel.Optimal, false );

            context.Console.WriteSuccess( $"Created {zipFileName} in {publishDir}." );

            return true;
        }
        catch ( Exception ex )
        {
            context.Console.WriteError( $"Failed to pack the AI skill marketplace: {ex.Message}" );

            return false;
        }
    }

    /// <summary>
    /// Does nothing: the solution has nothing to restore.
    /// </summary>
    public override bool Restore( BuildContext context, BuildSettings settings ) => true;

    /// <summary>
    /// Does nothing: the generator is tested in PostSharp.Engineering, not in the repositories that use it.
    /// </summary>
    public override bool Test( BuildContext context, BuildSettings settings ) => true;
}
