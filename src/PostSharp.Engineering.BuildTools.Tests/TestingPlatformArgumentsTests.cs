// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using PostSharp.Engineering.BuildTools.Build.Testing;
using Xunit;

namespace PostSharp.Engineering.BuildTools.Tests;

/// <summary>
/// Checks the options that <see cref="TestingPlatform"/> passes to <c>dotnet test</c> for each version of the .NET SDK.
/// </summary>
public sealed class TestingPlatformArgumentsTests
{
    [Theory]
    [InlineData( 11 )]
    [InlineData( 12 )]
    [InlineData( null )]
    public void TheSdk11AndLaterWriteOneDirectoryPerApplication( int? sdkMajorVersion )
    {
        var arguments = TestingPlatform.GetArguments( "results", null, sdkMajorVersion );

        Assert.Equal(
            "--report-trx --report-trx-filename report.trx --results-directory \"results\" --results-directory-layout per-module --no-artifact-post-processing",
            arguments );
    }

    /// <summary>
    /// The SDK 10 passes the options that it does not know to the test applications, which exit with code 5.
    /// </summary>
    [Fact]
    public void TheSdk10GetsOnlyTheOptionsItAccepts()
    {
        var arguments = TestingPlatform.GetArguments( "results", null, 10 );

        Assert.Equal( "--report-trx --results-directory \"results\"", arguments );
    }

    [Fact]
    public void AFilterAcceptsAnApplicationWithoutTests()
    {
        var arguments = TestingPlatform.GetArguments( "results", "FullyQualifiedName~Cache", 10 );

        Assert.Equal( "--report-trx --results-directory \"results\" --filter \"FullyQualifiedName~Cache\" --ignore-exit-code 8", arguments );
    }
}
