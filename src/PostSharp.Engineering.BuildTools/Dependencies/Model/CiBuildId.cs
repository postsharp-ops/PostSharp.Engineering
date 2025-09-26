// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using PostSharp.Engineering.BuildTools.Build;
using System;
using System.Text.RegularExpressions;

namespace PostSharp.Engineering.BuildTools.Dependencies.Model;

public record CiBuildId( int BuildNumber, string? BuildTypeId ) : ICiBuildSpec
{
    // This is a hack to guess the build configuration from the BuildTypeId because this information is not available
    // when we restore artifacts. It is only useful for Metalama.Compiler, all other products do not take the BuildConfiguration
    // into account.
    public BuildConfiguration BuildConfiguration
    {
        get
        {
            if ( this.BuildTypeId == null )
            {
                return default;
            }

            var match = Regex.Match( this.BuildTypeId, "_([A-Z][a-z]+)Build$" );

            if ( !match.Success )
            {
                return default;
            }

            return Enum.Parse<BuildConfiguration>( match.Groups[1].Value );
        }
    }

    public override string ToString() => $"{this.BuildTypeId}:{this.BuildNumber}";
}