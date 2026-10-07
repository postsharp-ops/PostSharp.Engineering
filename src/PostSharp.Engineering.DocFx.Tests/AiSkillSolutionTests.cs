// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using PostSharp.Engineering.BuildTools.Utilities;
using PostSharp.Engineering.DocFx.AiSkills;
using System;
using System.Collections.Immutable;
using System.IO;
using System.Text.Json;
using Xunit;

namespace PostSharp.Engineering.DocFx.Tests;

public sealed class AiSkillSolutionTests : IDisposable
{
    private static readonly ImmutableArray<AiSkillApiRelocation> _relocations = [new AiSkillApiRelocation( "Legacy.", "migration" )];

    private readonly string _repoDirectory = Path.Combine( Path.GetTempPath(), "AiSkillSolutionTests", Guid.NewGuid().ToString( "N" ) );

    public AiSkillSolutionTests()
    {
        Directory.CreateDirectory( this._repoDirectory );
    }

    public void Dispose()
    {
        if ( Directory.Exists( this._repoDirectory ) )
        {
            Directory.Delete( this._repoDirectory, true );
        }
    }

    [Fact]
    public void StripRenderingSections_RemovesReferencesAndMemberLayout()
    {
        const string yaml = "### YamlMime:ManagedReference\nitems:\n- uid: A\n  summary: Kept.\nreferences:\n- uid: B\n  name: B\nmemberLayout: SeparatePages\n";

        var stripped = ApiDocTransformer.StripRenderingSections( yaml );

        Assert.Equal( "### YamlMime:ManagedReference\nitems:\n- uid: A\n  summary: Kept.\n", stripped );
    }

    [Fact]
    public void TransformManifest_RelocatesMatchingFilesAndSorts()
    {
        const string manifest = """{"Z.Type": "Z.Type.yml", "Legacy.Type": "Legacy.Type.yml"}""";

        var transformed = ApiDocTransformer.TransformManifest( manifest, _relocations );

        Assert.Equal( "{\n  \"Legacy.Type\": \"migration/Legacy.Type.yml\",\n  \"Z.Type\": \"Z.Type.yml\"\n}", transformed.ReplaceLineEndings( "\n" ) );
    }

    [Fact]
    public void Build_CreatesMarketplace()
    {
        this.WriteFile( "claude/SKILL.md", "---\nname: sample\ndescription: Sample skill description.\n---\nThis skill pertains to Sample <version>.\n" );
        this.WriteFile( "claude/README.md", "# Sample marketplace\n" );
        this.WriteFile( "claude/scripts/find-doc.py", "print('hello')\n" );
        this.WriteFile( "docs/intro.md", "---\nuid: intro\nsummary: Introduction summary.\n---\n# Intro\n" );
        this.WriteFile( "docs/sub/details.md", "---\nuid: details\nsummary: Details summary.\nkeywords: \"a, b\"\n---\n# Details\n" );
        this.WriteFile( "docs/toc.yml", "items:\n- name: Intro\n  topicUid: intro\n  items:\n  - name: Details\n    topicUid: details\n" );
        this.WriteFile( "artifacts/api/Sample.Type.yml", "items:\n- uid: Sample.Type\nreferences:\n- uid: System.Object\n" );
        this.WriteFile( "artifacts/api/Legacy.Type.yml", "items:\n- uid: Legacy.Type\n" );
        this.WriteFile( "artifacts/api/.manifest", """{"Sample.Type": "Sample.Type.yml", "Legacy.Type": "Legacy.Type.yml"}""" );

        var options = new AiSkillOptions
        {
            PluginName = "sample",
            DisplayName = "Sample",
            MarketplaceDescription = "Sample marketplace.",
            FallbackDescription = "Fallback.",
            Homepage = "https://doc.example.com",
            RepositoryUrl = "https://github.com/example/Sample.AI.Skills",
            Keywords = ["sample"],
            ZipFilePrefix = "Sample.AI.Skills",
            ContentDirectory = "docs",
            TocPath = "docs/toc.yml",
            ApiRelocations = _relocations
        };

        Assert.True( new AiSkillSolution( options ).Build( this._repoDirectory, "2027.0", new ConsoleHelper() ) );

        var marketplaceDirectory = Path.Combine( this._repoDirectory, "artifacts", "marketplace" );
        var skillDirectory = Path.Combine( marketplaceDirectory, "plugins", "sample", "skills", "sample" );

        Assert.True( File.Exists( Path.Combine( marketplaceDirectory, "README.md" ) ) );
        Assert.Contains( "This skill pertains to Sample 2027.0.", File.ReadAllText( Path.Combine( skillDirectory, "SKILL.md" ) ), StringComparison.Ordinal );
        Assert.True( File.Exists( Path.Combine( skillDirectory, "docs", "sub", "details.md" ) ) );
        Assert.True( File.Exists( Path.Combine( skillDirectory, "docs", "toc.yml" ) ) );
        Assert.True( File.Exists( Path.Combine( skillDirectory, "scripts", "find-doc.py" ) ) );

        // API files: rendering sections are stripped, and relocated files go to the subdirectory.
        Assert.DoesNotContain( "references:", File.ReadAllText( Path.Combine( skillDirectory, "api", "Sample.Type.yml" ) ), StringComparison.Ordinal );
        Assert.True( File.Exists( Path.Combine( skillDirectory, "api", "migration", "Legacy.Type.yml" ) ) );
        Assert.Contains( "migration/Legacy.Type.yml", File.ReadAllText( Path.Combine( skillDirectory, "api", ".manifest" ) ), StringComparison.Ordinal );

        // The index mirrors the toc and points to repository-relative paths, which are also skill-relative.
        var index = File.ReadAllText( Path.Combine( skillDirectory, "index.yml" ) );
        Assert.Contains( "path: docs/intro.md", index, StringComparison.Ordinal );
        Assert.Contains( "path: docs/sub/details.md", index, StringComparison.Ordinal );
        Assert.Contains( "summary: Details summary.", index, StringComparison.Ordinal );
        Assert.Contains( "keywords: a, b", index, StringComparison.Ordinal );

        // The plugin description is taken from SKILL.md.
        using var pluginJson = JsonDocument.Parse(
            File.ReadAllText( Path.Combine( marketplaceDirectory, "plugins", "sample", ".claude-plugin", "plugin.json" ) ) );

        Assert.Equal( "Sample skill description.", pluginJson.RootElement.GetProperty( "description" ).GetString() );

        using var codexPluginJson = JsonDocument.Parse(
            File.ReadAllText( Path.Combine( marketplaceDirectory, "plugins", "sample", ".codex-plugin", "plugin.json" ) ) );

        Assert.Equal( "https://github.com/example/Sample.AI.Skills", codexPluginJson.RootElement.GetProperty( "repository" ).GetString() );
        Assert.True( File.Exists( Path.Combine( marketplaceDirectory, ".agents", "plugins", "marketplace.json" ) ) );
        Assert.True( File.Exists( Path.Combine( marketplaceDirectory, ".claude-plugin", "marketplace.json" ) ) );
    }

    private void WriteFile( string relativePath, string content )
    {
        var path = Path.Combine( this._repoDirectory, relativePath );
        Directory.CreateDirectory( Path.GetDirectoryName( path )! );
        File.WriteAllText( path, content );
    }
}
