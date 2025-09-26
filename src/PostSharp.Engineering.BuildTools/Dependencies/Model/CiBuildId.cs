// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using PostSharp.Engineering.BuildTools.Build;
using System;
using System.Text.RegularExpressions;

namespace PostSharp.Engineering.BuildTools.Dependencies.Model;

public record CiBuildId( int BuildNumber, string? BuildTypeId ) : ICiBuildSpec
{
    // This is a hack to guess the build configuration from the BuildTypeId because this information is not available
    // when we restore artifacts.
    public BuildConfiguration BuildConfiguration
    {
        get
        {
            if ( this.BuildTypeId == null )
            {
                throw new InvalidOperationException( "BuildTypeId is null." );
            }

            var match = Regex.Match( this.BuildTypeId, "_([A-Z][a-z]+)Build$" );

            if ( !match.Success )
            {
                throw new InvalidOperationException( $"BuildTypeId '{this.BuildTypeId}' cannot be parsed." );
            }

            return Enum.Parse<BuildConfiguration>( match.Groups[1].Value );
        }
    }

    public override string ToString() => $"{this.BuildTypeId}:{this.BuildNumber}";
}