// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using JetBrains.Annotations;
using System.Collections.Immutable;

namespace PostSharp.Engineering.DocFx.AiSkills;

/// <summary>
/// Describes the AI skill and the Claude Code and OpenAI Codex plugin that <see cref="AiSkillSolution"/> builds
/// from a documentation repository.
/// </summary>
[PublicAPI]
public sealed record AiSkillOptions
{
    /// <summary>
    /// Gets the name of the marketplace, the plugin and the skill, for instance <c>metalama</c>. It is also
    /// the name users type in <c>/plugin install</c>.
    /// </summary>
    public required string PluginName { get; init; }

    /// <summary>
    /// Gets the human-readable product name shown by Codex, for instance <c>Metalama</c>.
    /// </summary>
    public required string DisplayName { get; init; }

    /// <summary>
    /// Gets the description of the marketplace as a whole. The plugin description comes from the
    /// <c>description:</c> field of the <c>SKILL.md</c> front matter instead.
    /// </summary>
    public required string MarketplaceDescription { get; init; }

    /// <summary>
    /// Gets the plugin description used when <c>SKILL.md</c> has no <c>description:</c> field.
    /// </summary>
    public required string FallbackDescription { get; init; }

    /// <summary>
    /// Gets the URL of the online documentation.
    /// </summary>
    public required string Homepage { get; init; }

    /// <summary>
    /// Gets the URL of the GitHub repository the plugin is published to.
    /// </summary>
    public required string RepositoryUrl { get; init; }

    /// <summary>
    /// Gets the keywords of the Codex plugin manifest.
    /// </summary>
    public ImmutableArray<string> Keywords { get; init; } = ImmutableArray<string>.Empty;

    /// <summary>
    /// Gets the prefix of the zip file name. The full name is <c>{ZipFilePrefix}.{PackageVersion}.zip</c>.
    /// </summary>
    public required string ZipFilePrefix { get; init; }

    /// <summary>
    /// Gets the pattern matching the zip file, to be used in <c>PublicArtifacts</c> and publishers.
    /// </summary>
    public string PackageFilePattern => $"{this.ZipFilePrefix}.*.zip";

    public string OwnerName { get; init; } = "PostSharp Technologies";

    public string OwnerEmail { get; init; } = "hello@postsharp.net";

    public string Category { get; init; } = "Developer Tools";

    /// <summary>
    /// Gets the repository directory holding the hand-written <c>SKILL.md</c> and <c>README.md</c>, and the
    /// optional <c>scripts</c> and <c>assets</c> subdirectories copied into the skill. The <c>find-api</c> and <c>find-doc</c>
    /// scripts (Python and PowerShell) are always included; a file of the same name in <c>scripts</c> overrides them.
    /// </summary>
    public string SkillSourceDirectory { get; init; } = "claude";

    /// <summary>
    /// Gets the repository directory of the conceptual Markdown articles. It is copied into the skill under the same name.
    /// </summary>
    public required string ContentDirectory { get; init; }

    /// <summary>
    /// Gets the path of the root <c>toc.yml</c>, relative to the repository, from which <c>index.yml</c> is built.
    /// </summary>
    public required string TocPath { get; init; }

    /// <summary>
    /// Gets the repository directory of the code samples (<c>*.cs</c>) copied into the skill, or <c>null</c> if there is none.
    /// </summary>
    public string? CodeDirectory { get; init; }

    /// <summary>
    /// Gets the directory of the DocFx API metadata (<c>*.yml</c> and <c>.manifest</c>), relative to the repository.
    /// </summary>
    public string ApiDirectory { get; init; } = Path.Combine( "artifacts", "api" );

    /// <summary>
    /// Gets the rules that move some API files into a subdirectory of <c>api/</c>.
    /// </summary>
    public ImmutableArray<AiSkillApiRelocation> ApiRelocations { get; init; } = ImmutableArray<AiSkillApiRelocation>.Empty;
}
