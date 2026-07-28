// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using System;
using System.Collections.Immutable;
using System.Text.RegularExpressions;

namespace PostSharp.Engineering.BuildTools.Utilities;

public record ToolInvocationOptions(
    ImmutableDictionary<string, string?>? EnvironmentVariables = null,
    bool Silent = false,
    ImmutableArray<string> BlockedEnvironmentVariables = default,
    ToolInvocationRetry? Retry = null )
{
    public static ToolInvocationOptions Default { get; } = new();

    // Some environment variables are set by the Microsoft.Build package and must not be passed to the child process.
    public ImmutableArray<string> BlockedEnvironmentVariables { get; init; } =
        BlockedEnvironmentVariables.IsDefault ? ["DOTNET_ROOT_X64", "MSBUILD_EXE_PATH", "MSBuildSDKsPath"] : BlockedEnvironmentVariables;

    public ImmutableArray<Regex> ErrorPatterns { get; init; } = [new( @"\: error\b" )];

    public ImmutableArray<Regex> WarningPatterns { get; init; } = [new( @"\: warning\b" )];

    public ImmutableArray<Regex> SuccessPatterns { get; init; } = [new( "Passed! " )];

    public ImmutableArray<Regex> ImportantMessagePatterns { get; init; } = [new( "Test run for " )];

    public ImmutableArray<Regex> SilentPatterns { get; init; } = ImmutableArray<Regex>.Empty;

    public ImmutableArray<ReplacePattern> ReplacePatterns { get; init; } = ImmutableArray<ReplacePattern>.Empty;

    /// <summary>
    /// Literal values that must never reach the log: passwords, tokens, connection strings. Every line the tool
    /// helper writes goes through <see cref="Redact"/> first.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A tool that takes a secret on its command line leaks it twice, and neither place is output the child
    /// produced, so no output filter catches them: once where the command line is echoed before the process
    /// starts, and again in the "failed with exit code" message. Both survive into the CI build log, readable by
    /// anyone with access to the build.
    /// </para>
    /// <para>
    /// This is not hypothetical. <c>MsDeployPublisher</c> passes an App Service publish password this way, and it
    /// was recovered in clear text from a TeamCity log. Its arguments are even assembled with a <c>$(Password)</c>
    /// placeholder so that the dry run stays safe, which shows the leak was never intended, only unguarded.
    /// </para>
    /// <para>
    /// <b>Where a tool accepts the secret by environment variable or file, prefer that over this</b>:
    /// <see cref="EnvironmentVariables"/> is already kept out of the echoed command line. Use this when the tool
    /// offers no alternative.
    /// </para>
    /// </remarks>
    public ImmutableArray<string> Secrets { get; init; } = ImmutableArray<string>.Empty;

    /// <summary>
    /// Replaces every value in <see cref="Secrets"/> with <c>***</c>.
    /// </summary>
    public string Redact( string text )
    {
        if ( this.Secrets.IsDefaultOrEmpty || string.IsNullOrEmpty( text ) )
        {
            return text;
        }

        foreach ( var secret in this.Secrets )
        {
            // An empty secret matches at every position, which would replace the whole line with asterisks: that
            // hides the message without protecting anything.
            if ( !string.IsNullOrEmpty( secret ) )
            {
                text = text.Replace( secret, "***", StringComparison.Ordinal );
            }
        }

        return text;
    }

    public bool FilterOutput { get; init; } = true;

    /// <summary>
    /// When true and output is being captured, also echo the output to the console in real-time.
    /// </summary>
    public bool EchoOutputToConsole { get; init; }

    /// <summary>
    /// Content to send to the process via standard input. If set, stdin will be redirected and this content written to it.
    /// </summary>
    public string? StandardInput { get; init; }

    public TimeSpan OutputReadingTimeout { get; init; } = TimeSpan.FromSeconds( 10 );

    public static TimeSpan LongOutputReadingTimeout => TimeSpan.FromSeconds( 60 );

    public ToolInvocationOptions WithEnvironmentVariables( ImmutableDictionary<string, string?> additionalEnvironmentVariables )
        => this with { EnvironmentVariables = this.EnvironmentVariables?.AddRange( additionalEnvironmentVariables ) ?? additionalEnvironmentVariables };
}