// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using PostSharp.Engineering.BuildTools.Utilities;
using Xunit;

namespace PostSharp.Engineering.BuildTools.Tests;

/// <summary>
/// <see cref="ParsedPackageVersion"/> replaces NuGet.Versioning, so these tests pin the NuGet behaviours that its
/// callers rely on: the accepted syntax, the normalized form used in package file names, and the ordering used to
/// decide whether a tool must be updated.
/// </summary>
public class ParsedPackageVersionTests
{
    [Theory]
    [InlineData( "1", "1.0.0" )]
    [InlineData( "1.2", "1.2.0" )]
    [InlineData( "1.2.3", "1.2.3" )]
    [InlineData( "1.2.3.0", "1.2.3" )]
    [InlineData( "1.2.3.4", "1.2.3.4" )]
    [InlineData( "11.0.100-rc.1.26425.128", "11.0.100-rc.1.26425.128" )]
    [InlineData( "2027.0.1.16-dev-debug", "2027.0.1.16-dev-debug" )]
    [InlineData( "1.0.0-beta+sha.abc", "1.0.0-beta" )]
    [InlineData( " 10.0.100\r\n", "10.0.100" )]
    public void ParsesAndNormalizes( string input, string normalized )
    {
        Assert.True( ParsedPackageVersion.TryParse( input, out var version ) );
        Assert.Equal( normalized, version.ToNormalizedString() );
    }

    [Theory]
    [InlineData( "" )]
    [InlineData( "a.b" )]
    [InlineData( "1.2.3.4.5" )]
    [InlineData( "1..2" )]
    [InlineData( "1.0-" )]
    [InlineData( "1.0-beta..1" )]
    [InlineData( "1.0+" )]
    [InlineData( "-1.0" )]
    [InlineData( "1.0-beta_1" )]
    public void RejectsInvalidVersions( string input ) => Assert.False( ParsedPackageVersion.TryParse( input, out _ ) );

    [Fact]
    public void FullStringKeepsMetadata()
        => Assert.Equal( "1.0.0-beta+sha.abc", ParsedPackageVersion.Parse( "1.0.0-beta+sha.abc" ).ToFullString() );

    [Theory]
    [InlineData( "1.0.0", "1.0.1" )]
    [InlineData( "1.0.0", "1.0.0.1" )]
    [InlineData( "1.0.0-beta", "1.0.0" )]
    [InlineData( "1.0.0-alpha", "1.0.0-beta" )]
    [InlineData( "1.0.0-beta.2", "1.0.0-beta.10" )]
    [InlineData( "1.0.0-beta.9", "1.0.0-beta.alpha" )]
    [InlineData( "1.0.0-beta", "1.0.0-beta.1" )]
    [InlineData( "2027.0.1-preview", "2027.0.1.2-dev-debug" )]
    [InlineData( "11.0.100-rc.1.26425.128", "11.0.100" )]
    public void OrdersVersions( string lower, string higher )
    {
        var lowerVersion = ParsedPackageVersion.Parse( lower );
        var higherVersion = ParsedPackageVersion.Parse( higher );

        Assert.True( lowerVersion < higherVersion );
        Assert.True( higherVersion > lowerVersion );
    }

    [Theory]
    [InlineData( "1.0.0", "1.0" )]
    [InlineData( "1.0.0-BETA", "1.0.0-beta" )]
    [InlineData( "1.0.0+a", "1.0.0+b" )]
    public void TreatsVersionsAsEqual( string left, string right )
        => Assert.Equal( 0, ParsedPackageVersion.Parse( left ).CompareTo( ParsedPackageVersion.Parse( right ) ) );

    [Theory]
    [InlineData( "1.0", "1.0.0" )]
    [InlineData( "[1.0]", "1.0.0" )]
    [InlineData( "[1.0,2.0)", "1.0.0" )]
    [InlineData( "(1.0,)", "1.0.0" )]
    [InlineData( "[2027.0.1-preview, )", "2027.0.1-preview" )]
    public void ParsesRangeMinimum( string range, string minimum )
    {
        Assert.True( ParsedPackageVersion.TryParseRangeMinimum( range, out var version ) );
        Assert.Equal( minimum, version!.ToNormalizedString() );
    }

    [Theory]
    [InlineData( "(,1.0]" )]
    [InlineData( "[,1.0)" )]
    public void ParsesRangeWithoutMinimum( string range )
    {
        Assert.True( ParsedPackageVersion.TryParseRangeMinimum( range, out var version ) );
        Assert.Null( version );
    }

    [Theory]
    [InlineData( "" )]
    [InlineData( "[]" )]
    [InlineData( "[1.0" )]
    [InlineData( "[1.0,2.0,3.0]" )]
    [InlineData( "[x,2.0]" )]
    [InlineData( "[1.0,x]" )]
    public void RejectsInvalidRanges( string range ) => Assert.False( ParsedPackageVersion.TryParseRangeMinimum( range, out _ ) );
}
