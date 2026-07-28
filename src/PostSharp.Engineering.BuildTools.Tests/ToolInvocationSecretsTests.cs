// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using PostSharp.Engineering.BuildTools.Utilities;
using Spectre.Console;
using System.Collections.Immutable;
using System.IO;
using Xunit;

namespace PostSharp.Engineering.BuildTools.Tests;

/// <summary>
/// Keeping a secret passed on a command line out of the build log.
/// </summary>
/// <remarks>
/// <c>MsDeployPublisher</c> has to put an App Service publish password in an msdeploy argument, because msdeploy
/// accepts it no other way. That password was recovered in clear text from a TeamCity build log: the command line
/// is echoed before the process starts, and repeated in the "failed with exit code" message, and neither is child
/// output so no output filter touched them.
/// </remarks>
public class ToolInvocationSecretsTests
{
    private const string _password = "lqbvKeWQ1wkgb1pdwE6qhAe5wcQ";

    private static ToolInvocationOptions WithSecret( params string[] secrets )
        => ToolInvocationOptions.Default with { Secrets = ImmutableArray.Create( secrets ) };

    [Fact]
    public void ASecretInACommandLineIsRedacted()
    {
        var commandLine =
            $"-verb:sync -dest:auto,ComputerName='https://site.scm.azurewebsites.net/msdeploy.axd',UserName='$site',Password='{_password}',AuthType='Basic'";

        var redacted = WithSecret( _password ).Redact( commandLine );

        Assert.DoesNotContain( _password, redacted, System.StringComparison.Ordinal );
        Assert.Contains( "Password='***'", redacted, System.StringComparison.Ordinal );

        // The rest of the line has to survive, or the log stops being useful for diagnosing the failure that
        // printed it.
        Assert.Contains( "-verb:sync", redacted, System.StringComparison.Ordinal );
        Assert.Contains( "UserName='$site'", redacted, System.StringComparison.Ordinal );
    }

    [Fact]
    public void EverySecretIsRedacted()
    {
        var redacted = WithSecret( "first-secret", "second-secret" ).Redact( "a first-secret b second-secret c" );

        Assert.Equal( "a *** b *** c", redacted );
    }

    [Fact]
    public void EveryOccurrenceIsRedacted()
    {
        // The publish password appears twice in one failed invocation: once in the echoed command line and once in
        // the error that repeats it.
        Assert.Equal( "*** and ***", WithSecret( _password ).Redact( $"{_password} and {_password}" ) );
    }

    [Fact]
    public void AnEmptySecretIsIgnored()
    {
        // An empty string matches at every position. Replacing it would turn the line into asterisks, hiding the
        // message without protecting anything.
        Assert.Equal( "unchanged", WithSecret( "" ).Redact( "unchanged" ) );
    }

    [Fact]
    public void NoSecretsMeansNoChange()
    {
        Assert.Equal( "unchanged", ToolInvocationOptions.Default.Redact( "unchanged" ) );
    }

    [Fact]
    public void RedactionIsSafeOnNothing()
    {
        Assert.Equal( "", WithSecret( _password ).Redact( "" ) );
        Assert.Null( WithSecret( _password ).Redact( null! ) );
    }

    /// <summary>
    /// Captures everything written to the console, so a test can assert on what would have reached a build log.
    /// </summary>
    private sealed class CapturingConsoleHelper : ConsoleHelper
    {
        private readonly StringWriter _writer;

        private CapturingConsoleHelper( StringWriter writer, IAnsiConsole console ) : base( console, console )
        {
            this._writer = writer;
        }

        public string Captured => this._writer.ToString();

        public static CapturingConsoleHelper Create()
        {
            var writer = new StringWriter();
            var console = AnsiConsole.Create( new AnsiConsoleSettings { Out = new AnsiConsoleOutput( writer ), Ansi = AnsiSupport.No } );

            // Spectre wraps at the profile width. Left at a terminal default, a long command line would be broken
            // across lines mid-secret and an assertion on the whole string would pass for the wrong reason.
            console.Profile.Width = 10_000;

            return new CapturingConsoleHelper( writer, console );
        }
    }

    /// <summary>
    /// The end-to-end version, and the one that would have caught the original defect: a unit test on
    /// <see cref="ToolInvocationOptions.Redact"/> passes whether or not any caller remembers to declare its secret.
    /// </summary>
    [Fact]
    public void AFailedInvocationDoesNotLogItsSecret()
    {
        var console = CapturingConsoleHelper.Create();

        // A command that fails, so both leak sites fire: the command line echoed before the process starts, and the
        // "failed with exit code" message that repeats it. `dotnet` also prints the bad argument back, which
        // exercises the child-output path.
        var succeeded = ToolInvocationHelper.InvokeTool(
            console,
            "dotnet",
            $"no-such-command-{_password}",
            null,
            WithSecret( _password ) );

        Assert.False( succeeded );
        Assert.NotEmpty( console.Captured );
        Assert.DoesNotContain( _password, console.Captured, System.StringComparison.Ordinal );
        Assert.Contains( "***", console.Captured, System.StringComparison.Ordinal );
    }
}
