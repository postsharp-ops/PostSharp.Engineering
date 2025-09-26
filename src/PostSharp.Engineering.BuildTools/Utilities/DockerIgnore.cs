// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace PostSharp.Engineering.BuildTools.Utilities;

public class DockerIgnore
{
    private readonly List<Regex> _regexPatterns;

    public DockerIgnore( string path )
    {
        this._regexPatterns =
            File.ReadAllLines( path )
                .Where( line => !string.IsNullOrWhiteSpace( line ) && !line.TrimStart().StartsWith( "#", StringComparison.Ordinal ) )
                .SelectMany( line =>
                {
                    var pattern = ConvertStarPatternToRegex( line );

                    if ( pattern.Negation )
                    {
                        // Negations are not implemented yet.
                        return [];
                    }
                    else
                    {
                        return new[] { pattern.Pattern, pattern.Pattern + "/.+" };
                    }
                } )
                .Select( pattern => new Regex( pattern, RegexOptions.Compiled | RegexOptions.IgnoreCase ) )
                .ToList();
    }

    public bool ShouldIgnore( string filePath )
    {
        var relativePath = filePath.Replace( '\\', '/' );

        // It's convenient for debugging to store the match.exit
        var match = this._regexPatterns.FirstOrDefault( pattern => pattern.IsMatch( relativePath ) );

        return match != null;
    }

    private static (string Pattern, bool Negation) ConvertStarPatternToRegex( string sourcePattern )
    {
        var pattern = sourcePattern.Trim();

        // Handle negation
        var isNegated = pattern.StartsWith( "!", StringComparison.Ordinal );

        if ( isNegated )
        {
            pattern = pattern.Substring( 1 );
        }

        // Escape regex special characters
        pattern = Regex.Escape( pattern );

        // Convert ** to .*
        pattern = pattern.Replace( @"\*\*", ".*", StringComparison.Ordinal );

        // Convert * to [^/]*
        pattern = pattern.Replace( @"\*", "[^/]*", StringComparison.Ordinal );

        // Convert ? to .
        pattern = pattern.Replace( @"\?", ".", StringComparison.Ordinal );

        return (pattern, isNegated);
    }
}