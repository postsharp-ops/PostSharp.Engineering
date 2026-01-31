// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;

namespace PostSharp.Engineering.BuildTools.Utilities;

/// <summary>
/// Helper class to invoke Claude Code for merge conflict resolution.
/// </summary>
internal static class ClaudeCodeHelper
{
    private enum MessageType
    {
        Normal,
        Important,
        Success,
        Warning,
        Error
    }

    // Regex to match ANSI escape sequences (e.g., [31;1m, [0m, etc.)
    private static readonly Regex _ansiEscapeRegex = new( @"\x1b\[[0-9;]*m|\[\d+(?:;\d+)*m", RegexOptions.Compiled );

    /// <summary>
    /// Removes non-printable characters and ANSI escape codes from a string.
    /// Only keeps basic ASCII to avoid encoding issues.
    /// </summary>
    private static string SanitizeOutput( string? input )
    {
        if ( string.IsNullOrEmpty( input ) )
        {
            return "";
        }

        // First, strip ANSI escape sequences
        var stripped = _ansiEscapeRegex.Replace( input, "" );

        var sb = new StringBuilder( stripped.Length );

        foreach ( var c in stripped )
        {
            // Keep only printable ASCII (32-126), newlines, tabs, carriage returns
            if ( c >= 32 && c <= 126 || c == '\n' || c == '\r' || c == '\t' )
            {
                sb.Append( c );
            }
            else if ( char.IsWhiteSpace( c ) )
            {
                // Convert other whitespace to space
                sb.Append( ' ' );
            }

            // Skip all other characters (including extended Unicode)
        }

        return sb.ToString();
    }

    public static bool TryResolveMergeConflicts(
        ConsoleHelper console,
        string workingDirectory,
        string sourceBranch,
        string targetBranch,
        out string prBodyText )
    {
        var prompt = BuildMergeConflictPrompt( sourceBranch, targetBranch );

        // Mask secret environment variables before invoking Claude
        console.WriteMessage( "Masking secret environment variables..." );
        var maskedEnvVars = MaskSecretEnvironmentVariables();
        console.WriteMessage( $"Masked {maskedEnvVars.Count( kv => kv.Value == "<redacted>" )} secret variable(s)." );


        // Build the command arguments
        // -p: Print mode - required for piped input, outputs to stdout
        // --output-format stream-json: Stream JSON events in real-time
        // --verbose: Show full turn-by-turn output
        // --allowedTools: Tools Claude can use, with Bash patterns specified inline
        // Bash patterns: git commands and ./build.sh commands are allowed (NO dotnet - must use build.sh)
        // The prompt is sent via stdin to avoid command line length limits
        var claudeArgs = "-p --output-format stream-json --verbose --model opus " +
                         "--allowedTools \"Edit\" \"Read\" \"Bash(cd *)\" \"Bash(git *)\" \"Bash(./build.sh *)\" \"Grep\" \"Glob\"";

        console.WriteMessage( $"Invoking Claude Code with args: {claudeArgs}" );
        console.WriteMessage( $"Working directory: {workingDirectory}" );
        console.WriteMessage( "(prompt sent via stdin)" );
        console.WriteMessage( "" );

        // Collect raw output for PR body extraction
        var rawOutputBuilder = new StringBuilder();

        // Process each JSON line and translate to human-readable output
        void HandleOutput( string line )
        {
            lock ( rawOutputBuilder )
            {
                rawOutputBuilder.AppendLine( line );
            }

            var (humanReadable, messageType) = TranslateJsonToHumanReadable( line );
            humanReadable = SanitizeOutput( humanReadable );

            if ( !string.IsNullOrEmpty( humanReadable ) )
            {
                switch ( messageType )
                {
                    case MessageType.Error:
                        console.WriteError( humanReadable );

                        break;

                    case MessageType.Warning:
                        console.WriteWarning( humanReadable );

                        break;

                    case MessageType.Success:
                        console.WriteSuccess( humanReadable );

                        break;

                    case MessageType.Important:
                        console.WriteImportantMessage( humanReadable );

                        break;

                    default:
                        console.WriteMessage( humanReadable );

                        break;
                }
            }
        }

        // Invoke claude with custom output handler
        var success = ToolInvocationHelper.InvokeTool(
            console,
            "claude",
            claudeArgs,
            workingDirectory,
            out var exitCode,
            HandleOutput,
            HandleOutput,
            new ToolInvocationOptions { EnvironmentVariables = maskedEnvVars, StandardInput = prompt },
            ConsoleHelper.CancellationToken );

        console.WriteMessage( "" );
        console.WriteMessage( $"Claude exit code: {exitCode}" );
        console.WriteMessage( $"Claude invocation success: {success}" );

        // Extract conclusion from the collected output
        var rawOutput = rawOutputBuilder.ToString();
        prBodyText = ExtractConclusionFromJsonStream( rawOutput );

        return success && exitCode == 0;
    }

    /// <summary>
    /// Translates a JSON stream line to human-readable text with message type for coloring.
    /// </summary>
    private static (string Text, MessageType Type) TranslateJsonToHumanReadable( string jsonLine )
    {
        if ( string.IsNullOrWhiteSpace( jsonLine ) )
        {
            return ("", MessageType.Normal);
        }

        try
        {
            using var doc = JsonDocument.Parse( jsonLine );
            var root = doc.RootElement;

            // Check for event type
            if ( root.TryGetProperty( "type", out var typeElement ) )
            {
                var type = typeElement.GetString();

                switch ( type )
                {
                    case "system":
                        // System init message - show brief info
                        if ( root.TryGetProperty( "subtype", out var subtype ) && subtype.GetString() == "init" )
                        {
                            var model = root.TryGetProperty( "model", out var m ) ? m.GetString() : "unknown";

                            return ($"[Claude Code initialized - model: {model}]", MessageType.Success);
                        }

                        break;

                    case "assistant":
                        // Assistant message with content
                        if ( root.TryGetProperty( "message", out var message ) &&
                             message.TryGetProperty( "content", out var content ) &&
                             content.ValueKind == JsonValueKind.Array )
                        {
                            var sb = new StringBuilder();

                            foreach ( var block in content.EnumerateArray() )
                            {
                                if ( block.TryGetProperty( "type", out var blockType ) )
                                {
                                    var blockTypeStr = blockType.GetString();

                                    if ( blockTypeStr == "text" && block.TryGetProperty( "text", out var text ) )
                                    {
                                        sb.AppendLine( text.GetString() );
                                    }
                                    else if ( blockTypeStr == "tool_use" )
                                    {
                                        var toolName = block.TryGetProperty( "name", out var name ) ? name.GetString() : "unknown";
                                        sb.Append( "[Tool: " ).Append( toolName ).AppendLine( "]" );

                                        if ( block.TryGetProperty( "input", out var input ) )
                                        {
                                            // Show abbreviated input for some tools
                                            if ( toolName == "Read" && input.TryGetProperty( "file_path", out var filePath ) )
                                            {
                                                sb.Append( "  Reading: " ).AppendLine( filePath.GetString() );
                                            }
                                            else if ( toolName == "Edit" && input.TryGetProperty( "file_path", out var editPath ) )
                                            {
                                                sb.Append( "  Editing: " ).AppendLine( editPath.GetString() );
                                            }
                                            else if ( toolName == "Bash" && input.TryGetProperty( "command", out var cmd ) )
                                            {
                                                sb.Append( "  $ " ).AppendLine( cmd.GetString() );
                                            }
                                            else if ( toolName == "Grep" && input.TryGetProperty( "pattern", out var pattern ) )
                                            {
                                                sb.Append( "  Searching: " ).AppendLine( pattern.GetString() );
                                            }
                                            else if ( toolName == "Glob" && input.TryGetProperty( "pattern", out var globPattern ) )
                                            {
                                                sb.Append( "  Finding: " ).AppendLine( globPattern.GetString() );
                                            }
                                        }
                                    }
                                }
                            }

                            return (sb.ToString().TrimEnd(), MessageType.Important);
                        }

                        break;

                    case "user":
                        // User message - typically tool results
                        if ( root.TryGetProperty( "message", out var userMessage ) &&
                             userMessage.TryGetProperty( "content", out var userContent ) &&
                             userContent.ValueKind == JsonValueKind.Array )
                        {
                            var sb = new StringBuilder();
                            var hasError = false;

                            foreach ( var block in userContent.EnumerateArray() )
                            {
                                if ( block.TryGetProperty( "type", out var blockType ) &&
                                     blockType.GetString() == "tool_result" )
                                {
                                    var isError = block.TryGetProperty( "is_error", out var errFlag ) && errFlag.GetBoolean();

                                    if ( isError )
                                    {
                                        hasError = true;
                                    }

                                    var contentStr = block.TryGetProperty( "content", out var resultContent )
                                        ? resultContent.GetString() ?? ""
                                        : "";

                                    sb.AppendLine( isError ? $"  [ERROR] {contentStr}" : $"  ->{contentStr}" );
                                }
                            }

                            return (sb.ToString().TrimEnd(), hasError ? MessageType.Error : MessageType.Normal);
                        }

                        // Also check for tool_use_result at root level
                        if ( root.TryGetProperty( "tool_use_result", out var toolResult ) )
                        {
                            var resultStr = toolResult.GetString() ?? "";

                            return ($"  ->{resultStr}", MessageType.Normal);
                        }

                        break;

                    case "result":
                        // Final result
                        if ( root.TryGetProperty( "result", out var result ) )
                        {
                            return ("[Session completed]", MessageType.Success);
                        }

                        break;

                    case "error":
                        // Error message
                        if ( root.TryGetProperty( "error", out var error ) )
                        {
                            var errorMsg = error.TryGetProperty( "message", out var errMessage )
                                ? errMessage.GetString()
                                : error.ToString();

                            return ($"[ERROR] {errorMsg}", MessageType.Error);
                        }

                        break;
                }
            }

            // Unknown format, return empty
            return ("", MessageType.Normal);
        }
        catch ( JsonException )
        {
            // Not valid JSON, might be plain text output
            return (jsonLine, MessageType.Normal);
        }
    }

    /// <summary>
    /// Extracts the conclusion text from the JSON stream output.
    /// </summary>
    private static string ExtractConclusionFromJsonStream( string jsonStream )
    {
        var fullText = new StringBuilder();

        // Parse each line and extract text content
        foreach ( var line in jsonStream.Split( '\n', StringSplitOptions.RemoveEmptyEntries ) )
        {
            try
            {
                using var doc = JsonDocument.Parse( line );
                var root = doc.RootElement;

                if ( root.TryGetProperty( "type", out var typeElement ) &&
                     typeElement.GetString() == "assistant" &&
                     root.TryGetProperty( "message", out var message ) &&
                     message.TryGetProperty( "content", out var content ) &&
                     content.ValueKind == JsonValueKind.Array )
                {
                    foreach ( var block in content.EnumerateArray() )
                    {
                        if ( block.TryGetProperty( "type", out var blockType ) &&
                             blockType.GetString() == "text" &&
                             block.TryGetProperty( "text", out var text ) )
                        {
                            fullText.AppendLine( text.GetString() );
                        }
                    }
                }
            }
            catch ( JsonException )
            {
                // Skip invalid JSON lines
            }
        }

        return RestructureOutputForPR( fullText.ToString() );
    }

    /// <summary>
    /// Extracts the conclusion block and moves it to the top of the output.
    /// </summary>
    private static string RestructureOutputForPR( string output )
    {
        const string startMarker = "===CONCLUSION===";
        const string endMarker = "===END_CONCLUSION===";

        var startIndex = output.IndexOf( startMarker, StringComparison.Ordinal );
        var endIndex = output.IndexOf( endMarker, StringComparison.Ordinal );

        if ( startIndex < 0 || endIndex < 0 || endIndex <= startIndex )
        {
            // No valid conclusion block found, return as-is
            return output;
        }

        var conclusionStart = startIndex + startMarker.Length;
        var conclusion = output.Substring( conclusionStart, endIndex - conclusionStart ).Trim();

        // Remove the conclusion block from the original output
        var beforeConclusion = output.Substring( 0, startIndex ).TrimEnd();
        var afterConclusion = output.Substring( endIndex + endMarker.Length ).TrimStart();
        var details = (beforeConclusion + "\n" + afterConclusion).Trim();

        // Restructure: conclusion first, separator, then details
        return $"{conclusion}\n\n---\n\n<details>\n<summary>Claude's detailed work log</summary>\n\n{details}\n</details>";
    }

    /// <summary>
    /// Gets a dictionary of environment variables with secrets replaced by "&lt;redacted&gt;".
    /// </summary>
    private static ImmutableDictionary<string, string?> MaskSecretEnvironmentVariables()
    {
        var secretNames = GetSecretEnvironmentVariableNames();
        var builder = ImmutableDictionary.CreateBuilder<string, string?>();

        foreach ( var key in Environment.GetEnvironmentVariables().Keys.Cast<string>() )
        {
            var value = Environment.GetEnvironmentVariable( key );

            if ( value != null )
            {
                builder[key] = secretNames.Contains( key ) ? "<redacted>" : value;
            }
        }

        return builder.ToImmutable();
    }

    /// <summary>
    /// Uses reflection to find all fields in EnvironmentVariableNames marked with [Secret].
    /// </summary>
    private static HashSet<string> GetSecretEnvironmentVariableNames()
    {
        return typeof( EnvironmentVariableNames )
            .GetFields( BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static )
            .Where( f => f.GetCustomAttribute<SecretAttribute>() != null )
            .Select( f => (string) f.GetValue( null )! )
            .ToHashSet();
    }

    private static string BuildMergeConflictPrompt( string sourceBranch, string targetBranch )
    {
        return $"""
            # Merge Conflict Resolution Task

            You are resolving merge conflicts from branch '{sourceBranch}' (upstream) into '{targetBranch}' (current).
            This is a NON-INTERACTIVE session - you must complete the task without asking questions.

            ## Your Goal
            Resolve all merge conflicts so the code builds successfully.

            ## Generated Files (can be ignored)
            The following files are auto-generated. If they have conflicts, you can resolve them by keeping EITHER version
            (preferably the current/HEAD version), then they will be regenerated in step 3.
            - Build.ps1
            - DockerBuild.ps1
            - Dockerfile
            - Dockerfile.claude
            - .teamcity/settings.kts
            - .teamcity/pom.xml
            - eng/Versions.*.g.props
            - eng/DockerMounts.g.ps1

            ## Step-by-Step Process

            ### 1. Assess the situation
            Run `git status` to see which files have conflicts (marked as "both modified" or "unmerged").

            ### 2. For EACH conflicting file:
            a) Read the file to see the conflict markers (<<<<<<<, =======, >>>>>>>)
            b) Understand what each side of the conflict is trying to do:
               - The section after <<<<<<< HEAD is the current branch ({targetBranch})
               - The section after ======= is from the incoming branch ({sourceBranch})
            c) Decide the correct resolution:
               - If both changes are needed, combine them logically
               - If one supersedes the other, keep the appropriate one
               - Consider the semantic meaning, not just the text
               - For generated files (see list above), just keep HEAD version
            d) Edit the file to remove ALL conflict markers and leave only the resolved code
            e) Run `git add <file>` to mark it as resolved

            ### 3. Regenerate scripts
            After resolving all conflicts semantically, run `./build.sh generate-scripts` to regenerate the generated files.
            Then run `git add .` to stage the regenerated files.

            ### 4. Build and verify (conditional)
            **SKIP THIS STEP** if the ONLY files with conflicts were:
            - Generated files (listed above) - they will be regenerated by `generate-scripts`
            - Version files (`*.version.props`, `Directory.Packages.props`, `global.json`)
            - Files containing only version number changes

            In these cases, `generate-scripts` is sufficient - no build needed.

            **OTHERWISE**, if any source code files (*.cs, *.csproj, etc.) had conflicts, run `./build.sh build` to ensure the code compiles.
            If there are build errors or warnings, fix them and rebuild.

            ### 5. Final check
            Run `git status` to confirm all conflicts are resolved and files are staged.

            ## Conflict Resolution Rules
            - **Package versions**: When there's a conflict in package/dependency versions (in Directory.Packages.props, .csproj, etc.), always choose the HIGHER version number
            - **Code conflicts**: Analyze the intent of both changes and merge them logically
            - **Generated files**: Keep HEAD version, they will be regenerated

            ## Important Constraints
            - Do NOT run `git commit` - leave that to the calling process
            - Do NOT run `git push`
            - Do NOT run `dotnet build` for the first build - it will FAIL. Always use `./build.sh build` as the first build.
              When a specific project fails, you can iterate with `dotnet build` for this specific project. To continue with other projects, do `./build.sh build`.
            - Do NOT modify files that don't have conflicts unless needed to fix build errors
            - If you encounter a conflict you cannot resolve confidently, resolve it to the best of your ability and note it in your output

            ## Success Criteria
            - All conflict markers removed from all files
            - All previously conflicting files staged with `git add`
            - `./build.sh generate-scripts` completed successfully
            - If code changes were involved: `./build.sh build` completes successfully with no errors or warnings

            ## Output Format
            At the very end of your work, write a conclusion block in this exact format:

            ```
            ===CONCLUSION===
            [One-paragraph summary of what was merged and any notable decisions made]

            Files resolved:
            - file1.cs: [brief description of resolution]
            - file2.csproj: [brief description of resolution]

            Build status: [PASSED/SKIPPED (version-only changes)/FAILED with brief note]
            ===END_CONCLUSION===
            ```

            This conclusion will be extracted and placed at the TOP of the PR description.

            Begin now.
            """;
    }
}
