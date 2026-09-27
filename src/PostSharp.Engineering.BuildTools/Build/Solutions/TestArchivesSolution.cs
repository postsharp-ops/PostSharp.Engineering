// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using JetBrains.Annotations;
using PostSharp.Engineering.BuildTools.Build.Model;
using PostSharp.Engineering.BuildTools.Utilities;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;

namespace PostSharp.Engineering.BuildTools.Build.Solutions;

/// <summary>
/// A test-only <see cref="Solution"/> that runs the test archives of the repository with the generated
/// <c>RunTests.ps1</c>.
/// </summary>
/// <remarks>
/// <para>
/// A test archive is a zip file that holds one published Microsoft.Testing.Platform test application and its manifest.
/// The test projects of the other solutions write their archives into <c>artifacts/tests</c> while they are built, with
/// the <c>TestArchive.targets</c> file of <c>PostSharp.Engineering.Sdk</c>. This solution builds nothing: it runs what
/// they wrote, with the script that a test agent runs, so that <c>Build.ps1 test</c> tests what the agents test.
/// </para>
/// <para>
/// See <c>doc/test-archives.md</c>.
/// </para>
/// </remarks>
[PublicAPI]
public sealed class TestArchivesSolution : Solution
{
    /// <summary>
    /// The name of the script that <c>generate-scripts</c> writes into the engineering directory.
    /// </summary>
    public const string ScriptName = "RunTests.ps1";

    /// <summary>
    /// The directory of the archives, relative to the repository root. <c>TestArchive.targets</c> writes them there
    /// unless a project sets <c>TestArchiveDirectory</c>.
    /// </summary>
    public const string ArchivesDirectory = "artifacts/tests";

    public TestArchivesSolution() : base( ArchivesDirectory )
    {
        this.IsTestOnly = true;
    }

    public override string Name => "TestArchives";

    /// <summary>
    /// Gets the tags that select the archives to run. An archive runs when its manifest has at least one of them. When
    /// the list is empty, the tags do not restrict the archives.
    /// </summary>
    public string[] Tags { get; init; } = [];

    /// <summary>
    /// Gets the tags of the archives not to run.
    /// </summary>
    public string[] ExcludeTags { get; init; } = [];

    /// <summary>
    /// Adds <c>PublishTestArchive=true</c> to the properties of the build of the solutions when the product has a
    /// <see cref="TestArchivesSolution"/>, so that its test projects write the archives that it runs.
    /// </summary>
    internal static BuildSettings AddBuildProperties( Product product, BuildSettings settings )
        => product.Solutions.Any( s => s is TestArchivesSolution ) && !settings.Properties.ContainsKey( "PublishTestArchive" )
            ? settings.WithAdditionalProperties( ImmutableDictionary<string, string>.Empty.Add( "PublishTestArchive", "true" ) )
            : settings;

    // The archives are written by the build of the other solutions.
    public override bool Build( BuildContext context, BuildSettings settings ) => true;

    public override bool Pack( BuildContext context, BuildSettings settings )
        => throw new NotSupportedException( "The test archives are not packed." );

    public override bool Restore( BuildContext context, BuildSettings settings ) => true;

    public override bool Test( BuildContext context, BuildSettings settings )
    {
        if ( settings.TestsFilter != null )
        {
            // The filter syntax of a test application is that of its test framework, and the archives of one repository
            // can use several of them. RunTests.ps1 -ApplicationArguments passes a filter to the applications.
            context.Console.WriteWarning( $"The test filter '{settings.TestsFilter}' does not apply to the test archives." );
        }

        var script = Path.Combine( context.RepoDirectory, context.Product.EngineeringDirectory, ScriptName );

        if ( !File.Exists( script ) )
        {
            context.Console.WriteError( $"The script '{script}' does not exist. Run 'Build.ps1 generate-scripts'." );

            return false;
        }

        return ToolInvocationHelper.InvokeTool(
            context.Console,
            "pwsh",
            $"-NoProfile -NonInteractive -Command \"{GetCommand( script, this.Tags, this.ExcludeTags )}\"",
            context.RepoDirectory );
    }

    /// <summary>
    /// Gets the PowerShell command that runs the script. The script is called from a command rather than with
    /// <c>-File</c>, because <c>-File</c> passes an argument such as <c>a,b</c> to a string array parameter as one
    /// string.
    /// </summary>
    internal static string GetCommand( string script, IReadOnlyList<string> tags, IReadOnlyList<string> excludeTags )
    {
        var command = $"& {Quote( script )}";

        if ( tags.Count > 0 )
        {
            command += $" -Tags {string.Join( ",", tags.Select( Quote ) )}";
        }

        if ( excludeTags.Count > 0 )
        {
            command += $" -ExcludeTags {string.Join( ",", excludeTags.Select( Quote ) )}";
        }

        // Without it, the exit code of pwsh tells only whether the last command succeeded, as 0 or 1, and not the exit
        // code of the script.
        return command + "; exit $LASTEXITCODE";
    }

    // A single-quoted PowerShell literal, in which only the apostrophe is special. The command line is enclosed in
    // double quotes, which none of these values may contain.
    private static string Quote( string value )
    {
        if ( value.Contains( '"', StringComparison.Ordinal ) )
        {
            throw new ArgumentOutOfRangeException( nameof(value), $"The value '{value}' cannot contain a double quote." );
        }

        return $"'{value.Replace( "'", "''", StringComparison.Ordinal )}'";
    }
}
