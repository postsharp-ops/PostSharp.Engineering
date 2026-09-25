// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using System;

namespace PostSharp.Engineering.BuildTools.ContinuousIntegration.TeamCity;

internal static class KotlinHelper
{
    public static string EscapeString( string value )
    {
        // Escape for Kotlin string: \ => \\, " => \", $ => ${'$'}, and a line break => \n.
        //
        // A Kotlin string literal cannot span lines, so a value carrying a line break used to produce a settings file
        // that does not compile. Escaping it instead is what lets a generated build step hold a readable multi-line
        // script rather than one long line of statements separated by semicolons. Both spellings of a line break
        // collapse to \n, because the script is written out and run on the agent, which may be Unix.
        return value
            .Replace( "\\", "\\\\", StringComparison.Ordinal )
            .Replace( "\"", "\\\"", StringComparison.Ordinal )
            .Replace( "$", "${'$'}", StringComparison.Ordinal )
            .Replace( "\r\n", "\\n", StringComparison.Ordinal )
            .Replace( "\n", "\\n", StringComparison.Ordinal )
            .Replace( "\r", "\\n", StringComparison.Ordinal );
    }
}