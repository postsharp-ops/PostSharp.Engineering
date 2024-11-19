// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using JetBrains.Annotations;

namespace PostSharp.Engineering.BuildTools.Utilities;

[PublicAPI]
public class StringTrimmer( int maxLength )
{
    public string Trim( string description )
    {
        if ( description.Length > maxLength )
        {
            description = description.Substring( 0, maxLength ) + "...";
        }

        return description;
    }
}