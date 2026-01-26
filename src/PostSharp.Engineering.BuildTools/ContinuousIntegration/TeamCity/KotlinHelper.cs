// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using System;

namespace PostSharp.Engineering.BuildTools.ContinuousIntegration.TeamCity;

internal static class KotlinHelper
{
    public static string EscapeString( string value )
    {
        // Escape for Kotlin string: \ => \\, " => \"
        return value
            .Replace( "\\", "\\\\", StringComparison.Ordinal )
            .Replace( "\"", "\\\"", StringComparison.Ordinal );
    }
}