// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using PostSharp.Engineering.BuildTools.Utilities;
using PostSharp.Engineering.DocFx.AiSkills;
using System;
using System.Collections.Immutable;
using System.Diagnostics;
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
        var skillDirectory = this.BuildSampleSkill();
        var marketplaceDirectory = Path.Combine( this._repoDirectory, "artifacts", "marketplace" );

        Assert.True( File.Exists( Path.Combine( marketplaceDirectory, "README.md" ) ) );
        Assert.Contains( "This skill pertains to Sample 2027.0.", File.ReadAllText( Path.Combine( skillDirectory, "SKILL.md" ) ), StringComparison.Ordinal );
        Assert.True( File.Exists( Path.Combine( skillDirectory, "docs", "sub", "details.md" ) ) );
        Assert.True( File.Exists( Path.Combine( skillDirectory, "docs", "toc.yml" ) ) );

        // The bundled scripts are written, and the repository's own scripts are added to them.
        foreach ( var script in new[] { "find-api.py", "find-api.ps1", "find-doc.py", "find-doc.ps1", "extra.py" } )
        {
            Assert.True( File.Exists( Path.Combine( skillDirectory, "scripts", script ) ), script );
        }

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

    // The Python and PowerShell variants of each script must behave the same. A variant whose interpreter is not
    // installed is not tested.
    [Theory]
    [InlineData( ScriptLanguage.Python )]
    [InlineData( ScriptLanguage.PowerShell )]
    public void Scripts_FindApiAndArticles( ScriptLanguage language )
    {
        if ( !IsInterpreterAvailable( language ) )
        {
            return;
        }

        var skillDirectory = this.BuildSampleSkill();

        AssertScript( language, skillDirectory, "find-api", ["Sample.Type"], 0, "1 match(es)", "- uid: Sample.Type", "Sample type summary." );
        AssertScript( language, skillDirectory, "find-api", ["sample.ty"], 0, "match(es)", "api/Sample.Type.yml" );
        AssertScript( language, skillDirectory, "find-api", ["Type"], 0, "api/migration/Legacy.Type.yml" );
        AssertScript( language, skillDirectory, "find-api", ["ZzzNotAnApi"], 1, "No API matching" );
        AssertScript( language, skillDirectory, "find-doc", ["details", "summary"], 0, "path: docs/sub/details.md", "1 match(es)" );
        AssertScript( language, skillDirectory, "find-doc", ["zzznotadoc"], 1, "0 match(es)" );
    }

    public enum ScriptLanguage
    {
        Python,
        PowerShell
    }

    private static void AssertScript(
        ScriptLanguage language,
        string skillDirectory,
        string script,
        string[] arguments,
        int expectedExitCode,
        params string[] expectedFragments )
    {
        var scriptPath = Path.Combine( skillDirectory, "scripts", script + (language == ScriptLanguage.Python ? ".py" : ".ps1") );
        string[] interpreterArguments = language == ScriptLanguage.Python ? [scriptPath] : ["-NoProfile", "-ExecutionPolicy", "Bypass", "-File", scriptPath];

        Assert.True( TryRun( language, skillDirectory, [..interpreterArguments, ..arguments], out var exitCode, out var output ), $"Cannot run {script}." );
        Assert.True( exitCode == expectedExitCode, $"{script} {string.Join( ' ', arguments )} exited with {exitCode}:\n{output}" );

        foreach ( var fragment in expectedFragments )
        {
            Assert.True( output.Contains( fragment, StringComparison.OrdinalIgnoreCase ), $"{script} {string.Join( ' ', arguments )} did not print '{fragment}':\n{output}" );
        }
    }

    // A missing interpreter fails to start; the Windows Store shim of Python starts but returns an error.
    private static bool IsInterpreterAvailable( ScriptLanguage language )
        => TryRun( language, Path.GetTempPath(), language == ScriptLanguage.Python ? ["--version"] : ["-NoProfile", "-Command", "exit 0"], out var exitCode, out _ )
           && exitCode == 0;

    private static bool TryRun( ScriptLanguage language, string workingDirectory, string[] arguments, out int exitCode, out string output )
    {
        var startInfo = new ProcessStartInfo( language == ScriptLanguage.Python ? "python" : OperatingSystem.IsWindows() ? "powershell" : "pwsh" );

        foreach ( var argument in arguments )
        {
            startInfo.ArgumentList.Add( argument );
        }

        startInfo.WorkingDirectory = workingDirectory;
        startInfo.RedirectStandardOutput = true;
        startInfo.RedirectStandardError = true;

        try
        {
            using var process = Process.Start( startInfo )!;
            var standardError = process.StandardError.ReadToEndAsync();
            output = process.StandardOutput.ReadToEnd() + standardError.Result;
            process.WaitForExit();
            exitCode = process.ExitCode;

            return true;
        }
        catch ( System.ComponentModel.Win32Exception )
        {
            exitCode = -1;
            output = "";

            return false;
        }
    }

    private string BuildSampleSkill()
    {
        this.WriteFile( "claude/SKILL.md", "---\nname: sample\ndescription: Sample skill description.\n---\nThis skill pertains to Sample <version>.\n" );
        this.WriteFile( "claude/README.md", "# Sample marketplace\n" );
        this.WriteFile( "claude/scripts/extra.py", "print('hello')\n" );
        this.WriteFile( "docs/intro.md", "---\nuid: intro\nsummary: Introduction summary.\n---\n# Intro\n" );
        this.WriteFile( "docs/sub/details.md", "---\nuid: details\nsummary: Details summary.\nkeywords: \"a, b\"\n---\n# Details\n" );
        this.WriteFile( "docs/toc.yml", "items:\n- name: Intro\n  topicUid: intro\n  items:\n  - name: Details\n    topicUid: details\n" );
        this.WriteFile( "artifacts/api/Sample.Type.yml", "items:\n- uid: Sample.Type\n  summary: Sample type summary.\nreferences:\n- uid: System.Object\n" );
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

        return Path.Combine( this._repoDirectory, "artifacts", "marketplace", "plugins", "sample", "skills", "sample" );
    }

    private void WriteFile( string relativePath, string content )
    {
        var path = Path.Combine( this._repoDirectory, relativePath );
        Directory.CreateDirectory( Path.GetDirectoryName( path )! );
        File.WriteAllText( path, content );
    }
}
