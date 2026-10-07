// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using System.Collections.Immutable;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace PostSharp.Engineering.DocFx.AiSkills;

/// <summary>
/// Transforms DocFx API YML files for inclusion in the AI skill: strips sections that are only
/// used for HTML rendering (they account for ~88% of file size) and relocates the files matched by
/// an <see cref="AiSkillApiRelocation"/> to a subdirectory.
/// </summary>
internal static class ApiDocTransformer
{
    /// <summary>
    /// Top-level YML sections that DocFx uses for hyperlink rendering only. They contain no
    /// documentation content (the <c>items:</c> section has summary, syntax, parameters, returns).
    /// </summary>
    private static readonly HashSet<string> _strippedSections = new( StringComparer.Ordinal ) { "references", "memberLayout" };

    private static readonly Regex _topLevelKeyRegex = new( "^([A-Za-z][A-Za-z0-9]*):", RegexOptions.Compiled );

    /// <summary>
    /// Returns the path of an API file relative to the skill's <c>api/</c> folder, applying the first matching relocation.
    /// </summary>
    public static string GetRelocatedPath( string relativePath, ImmutableArray<AiSkillApiRelocation> relocations )
    {
        var fileName = Path.GetFileName( relativePath );

        foreach ( var relocation in relocations )
        {
            if ( fileName.StartsWith( relocation.FileNamePrefix, StringComparison.Ordinal ) )
            {
                return relocation.Subdirectory + "/" + relativePath;
            }
        }

        return relativePath;
    }

    /// <summary>
    /// Removes the <c>references:</c> and <c>memberLayout:</c> top-level sections from a DocFx
    /// ManagedReference YML document.
    /// </summary>
    public static string StripRenderingSections( string yaml )
    {
        var stringBuilder = new StringBuilder( yaml.Length );
        var skipping = false;

        foreach ( var rawLine in yaml.Split( '\n' ) )
        {
            var line = rawLine.TrimEnd( '\r' );
            var keyMatch = _topLevelKeyRegex.Match( line );

            if ( keyMatch.Success )
            {
                skipping = _strippedSections.Contains( keyMatch.Groups[1].Value );
            }

            if ( !skipping )
            {
                stringBuilder.Append( line );
                stringBuilder.Append( '\n' );
            }
        }

        return stringBuilder.ToString();
    }

    /// <summary>
    /// Rewrites the <c>.manifest</c> UID-to-file index so that entries pointing to relocated
    /// files include their subdirectory.
    /// </summary>
    public static string TransformManifest( string manifestJson, ImmutableArray<AiSkillApiRelocation> relocations )
    {
        var map = JsonSerializer.Deserialize<Dictionary<string, string>>( manifestJson )
                  ?? throw new InvalidOperationException( "The API manifest could not be parsed." );

        foreach ( var key in map.Keys.ToList() )
        {
            map[key] = GetRelocatedPath( map[key], relocations );
        }

        // Keep the manifest sorted and one-entry-per-line so it remains greppable.
        var sorted = new SortedDictionary<string, string>( map, StringComparer.Ordinal );
        var options = new JsonSerializerOptions { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

        return JsonSerializer.Serialize( sorted, options );
    }
}
