// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using PostSharp.Engineering.BuildTools.Build.Testing;
using System.Collections.Immutable;
using System.Linq;
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

    /// <summary>
    /// With a .NET SDK before 11, <c>net10.0</c> and <c>net10.0-windows</c> of one project write the same report.
    /// </summary>
    [Fact]
    public void TargetFrameworksThatDifferByTheirPlatformOnlyConflict()
    {
        var conflicts = TestingPlatform.FindReportNameConflicts(
        [
            Application( "Common", "net10.0" ),
            Application( "Common", "net10.0-windows" ),
            Application( "Common", "net48" ),
            Application( "Other", "net10.0" ),
            Application( "Native", "net10.0", "win-x64" ),
            Application( "Native", "net10.0", "win-arm64" )
        ] );

        var conflict = Assert.Single( conflicts );
        Assert.Equal( ["net10.0", "net10.0-windows"], conflict.Select( a => a.TargetFramework ) );
    }

    private static TestApplication Application( string name, string targetFramework, string runtimeIdentifier = "" )
        => new( $"/src/{name}/{name}.csproj", name, targetFramework, runtimeIdentifier, [], [], false, null, ImmutableArray<string>.Empty );

    [Fact]
    public void AFilterAcceptsAnApplicationWithoutTests()
    {
        var arguments = TestingPlatform.GetArguments( "results", "FullyQualifiedName~Cache", 10 );

        Assert.Equal( "--report-trx --results-directory \"results\" --filter \"FullyQualifiedName~Cache\" --ignore-exit-code 8", arguments );
    }
}
