// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace PostSharp.Engineering.BuildTools.Utilities;

/// <summary>
/// A NuGet package version: one to four numeric components, optional pre-release labels and optional build metadata.
/// Ordering follows the NuGet rules, which extend SemVer 2.0 with a fourth numeric component.
/// </summary>
/// <remarks>
/// This type replaces the NuGet.Versioning package. The .NET SDK loads its own copy of NuGet.Versioning into the
/// process when a command evaluates a project through MSBuildLocator. A copy shipped next to this assembly is loaded
/// first, and the SDK build tasks then fail with a <see cref="System.IO.FileLoadException"/> whenever the SDK requires
/// a newer version than the one shipped. The .NET 11 SDK requires NuGet.Versioning 7.11.
/// </remarks>
internal sealed class ParsedPackageVersion : IComparable<ParsedPackageVersion>
{
    private ParsedPackageVersion( int major, int minor, int patch, int revision, IReadOnlyList<string> releaseLabels, string? metadata )
    {
        this.Major = major;
        this.Minor = minor;
        this.Patch = patch;
        this.Revision = revision;
        this.ReleaseLabels = releaseLabels;
        this.Metadata = metadata;
    }

    public int Major { get; }

    public int Minor { get; }

    public int Patch { get; }

    public int Revision { get; }

    public IReadOnlyList<string> ReleaseLabels { get; }

    public string? Metadata { get; }

    public bool IsPrerelease => this.ReleaseLabels.Count > 0;

    public static ParsedPackageVersion Parse( string value )
        => TryParse( value, out var version ) ? version : throw new FormatException( $"'{value}' is not a valid package version." );

    public static bool TryParse( string? value, out ParsedPackageVersion version )
    {
        version = null!;

        if ( string.IsNullOrWhiteSpace( value ) )
        {
            return false;
        }

        var remainder = value.Trim();
        string? metadata = null;

        var plusIndex = remainder.IndexOf( '+', StringComparison.Ordinal );

        if ( plusIndex >= 0 )
        {
            metadata = remainder.Substring( plusIndex + 1 );
            remainder = remainder.Substring( 0, plusIndex );

            if ( !metadata.Split( '.' ).All( IsValidIdentifier ) )
            {
                return false;
            }
        }

        IReadOnlyList<string> releaseLabels = Array.Empty<string>();

        var dashIndex = remainder.IndexOf( '-', StringComparison.Ordinal );

        if ( dashIndex >= 0 )
        {
            var labels = remainder.Substring( dashIndex + 1 ).Split( '.' );
            remainder = remainder.Substring( 0, dashIndex );

            if ( !labels.All( IsValidReleaseLabel ) )
            {
                return false;
            }

            releaseLabels = labels;
        }

        var components = remainder.Split( '.' );

        if ( components.Length is < 1 or > 4 )
        {
            return false;
        }

        var numbers = new int[4];

        for ( var i = 0; i < components.Length; i++ )
        {
            if ( components[i].Length == 0 || !components[i].All( char.IsAsciiDigit )
                                           || !int.TryParse( components[i], NumberStyles.None, CultureInfo.InvariantCulture, out numbers[i] ) )
            {
                return false;
            }
        }

        version = new ParsedPackageVersion( numbers[0], numbers[1], numbers[2], numbers[3], releaseLabels, metadata );

        return true;
    }

    /// <summary>
    /// Parses a NuGet version range, as found in the <c>version</c> attribute of a nuspec dependency, and returns its
    /// lower bound.
    /// </summary>
    /// <param name="value">A bare version, which means a minimum inclusive version, or an interval such as
    /// <c>[1.0,2.0)</c>. Floating versions such as <c>1.*</c> are rejected, although NuGet accepts them: a nuspec in
    /// a built package never contains one, because <c>pack</c> writes the resolved lower bound.</param>
    /// <param name="minimum">The lower bound, or <c>null</c> when the range has no lower bound.</param>
    /// <returns><c>true</c> if <paramref name="value"/> is a valid range.</returns>
    public static bool TryParseRangeMinimum( string? value, out ParsedPackageVersion? minimum )
    {
        minimum = null;

        if ( string.IsNullOrWhiteSpace( value ) )
        {
            return false;
        }

        var range = value.Trim();

        if ( range[0] is not ('[' or '(') )
        {
            if ( !TryParse( range, out var version ) )
            {
                return false;
            }

            minimum = version;

            return true;
        }

        if ( range.Length < 3 || range[^1] is not (']' or ')') )
        {
            return false;
        }

        var isMinInclusive = range[0] == '[';
        var isMaxInclusive = range[^1] == ']';
        var bounds = range.Substring( 1, range.Length - 2 ).Split( ',' );

        if ( bounds.Length == 1 )
        {
            // A single bound is an exact version, which NuGet accepts only in the inclusive form '[1.0]'.
            if ( !isMinInclusive || !isMaxInclusive || !TryParse( bounds[0], out var exactVersion ) )
            {
                return false;
            }

            minimum = exactVersion;

            return true;
        }

        if ( bounds.Length > 2 )
        {
            return false;
        }

        var lowerBound = bounds[0].Trim();
        var upperBound = bounds[1].Trim();
        ParsedPackageVersion? lowerVersion = null;
        ParsedPackageVersion? upperVersion = null;

        if ( lowerBound.Length == 0 && upperBound.Length == 0 )
        {
            return false;
        }

        if ( (lowerBound.Length > 0 && !TryParse( lowerBound, out lowerVersion ))
             || (upperBound.Length > 0 && !TryParse( upperBound, out upperVersion )) )
        {
            return false;
        }

        if ( lowerVersion != null && upperVersion != null )
        {
            // NuGet rejects reversed bounds, and equal bounds with one inclusive end and one exclusive end. It accepts
            // equal bounds that are both exclusive, such as '(1.0,1.0)'.
            var comparison = lowerVersion.CompareTo( upperVersion );

            if ( comparison > 0 || (comparison == 0 && isMinInclusive != isMaxInclusive) )
            {
                return false;
            }
        }

        minimum = lowerVersion;

        return true;
    }

    /// <summary>
    /// Gets the version in the form NuGet uses for package file names: the fourth component only when it is not
    /// zero, the pre-release labels, and no metadata.
    /// </summary>
    public string ToNormalizedString()
    {
        var text = $"{this.Major}.{this.Minor}.{this.Patch}";

        if ( this.Revision != 0 )
        {
            text += $".{this.Revision}";
        }

        if ( this.IsPrerelease )
        {
            text += "-" + string.Join( ".", this.ReleaseLabels );
        }

        return text;
    }

    /// <summary>
    /// Gets the normalized version followed by the build metadata, if any.
    /// </summary>
    public string ToFullString() => this.Metadata == null ? this.ToNormalizedString() : $"{this.ToNormalizedString()}+{this.Metadata}";

    public override string ToString() => this.ToNormalizedString();

    public int CompareTo( ParsedPackageVersion? other )
    {
        if ( other == null )
        {
            return 1;
        }

        var result = this.Major.CompareTo( other.Major );

        if ( result == 0 )
        {
            result = this.Minor.CompareTo( other.Minor );
        }

        if ( result == 0 )
        {
            result = this.Patch.CompareTo( other.Patch );
        }

        if ( result == 0 )
        {
            result = this.Revision.CompareTo( other.Revision );
        }

        if ( result != 0 )
        {
            return result;
        }

        // A release version is greater than any pre-release of the same numeric version.
        if ( this.IsPrerelease != other.IsPrerelease )
        {
            return this.IsPrerelease ? -1 : 1;
        }

        for ( var i = 0; i < Math.Min( this.ReleaseLabels.Count, other.ReleaseLabels.Count ); i++ )
        {
            result = CompareReleaseLabels( this.ReleaseLabels[i], other.ReleaseLabels[i] );

            if ( result != 0 )
            {
                return result;
            }
        }

        // When all shared labels are equal, the version with more labels is greater. Metadata does not take part.
        return this.ReleaseLabels.Count.CompareTo( other.ReleaseLabels.Count );
    }

    public static bool operator <( ParsedPackageVersion left, ParsedPackageVersion right ) => left.CompareTo( right ) < 0;

    public static bool operator >( ParsedPackageVersion left, ParsedPackageVersion right ) => left.CompareTo( right ) > 0;

    public static bool operator <=( ParsedPackageVersion left, ParsedPackageVersion right ) => left.CompareTo( right ) <= 0;

    public static bool operator >=( ParsedPackageVersion left, ParsedPackageVersion right ) => left.CompareTo( right ) >= 0;

    // Numeric labels compare numerically and are lower than alphanumeric labels. Other labels compare ordinally
    // without regard to case. As in NuGet, a label is numeric only when it fits in an Int32: a longer run of digits
    // compares as a string, so 1.0.0-2147483648 is greater than 1.0.0-10000000000.
    private static int CompareReleaseLabels( string left, string right )
    {
        var leftIsNumeric = int.TryParse( left, NumberStyles.None, CultureInfo.InvariantCulture, out var leftNumber );
        var rightIsNumeric = int.TryParse( right, NumberStyles.None, CultureInfo.InvariantCulture, out var rightNumber );

        return (leftIsNumeric, rightIsNumeric) switch
        {
            (true, true) => leftNumber.CompareTo( rightNumber ),
            (true, false) => -1,
            (false, true) => 1,
            _ => StringComparer.OrdinalIgnoreCase.Compare( left, right )
        };
    }

    private static bool IsValidIdentifier( string identifier ) => identifier.Length > 0 && identifier.All( c => char.IsAsciiLetterOrDigit( c ) || c == '-' );

    // NuGet rejects a numeric release label with a leading zero, such as the '01' of 1.0.0-beta.01, but accepts an
    // alphanumeric one such as '00a'. Numeric version components and metadata may have leading zeros.
    private static bool IsValidReleaseLabel( string label )
        => IsValidIdentifier( label ) && !(label.Length > 1 && label[0] == '0' && label.All( char.IsAsciiDigit ));
}
