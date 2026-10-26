// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using PostSharp.Engineering.BuildTools.Tools.TeamCity;
using Xunit;

namespace PostSharp.Engineering.BuildTools.Tests;

/// <summary>
/// The values of a TeamCity service message must escape the characters that TeamCity gives a meaning, otherwise TeamCity
/// rejects the message.
/// </summary>
public class TeamCityServiceMessageTests
{
    [Theory]
    [InlineData( "artifacts/testResults/run.trx", "artifacts/testResults/run.trx" )]
    [InlineData( "artifacts/testResults/run_net10.0[1].trx", "artifacts/testResults/run_net10.0|[1|].trx" )]
    [InlineData( "it's", "it|'s" )]
    [InlineData( "a|b", "a||b" )]
    [InlineData( "line1\r\nline2", "line1|r|nline2" )]
    [InlineData( "\u0085\u2028\u2029", "|x|l|p" )]
    public void EscapeServiceMessageValue( string value, string expected )
        => Assert.Equal( expected, TeamCityHelper.EscapeServiceMessageValue( value ) );
}
