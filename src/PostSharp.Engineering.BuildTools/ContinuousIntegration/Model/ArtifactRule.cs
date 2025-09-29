// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using PostSharp.Engineering.BuildTools.Build.Model;
using System;
using System.Collections.Generic;

namespace PostSharp.Engineering.BuildTools.ContinuousIntegration.Model;

public record ArtifactRule( string Source, string Target, bool Exclude = false, bool IsAbsolute = false, bool AllFiles = true ) : IComparable<ArtifactRule>
{
    internal string GetPublishRule( string checkoutDirectory )
    {
        var prefix = this.Exclude ? "-" : "+";
        var suffix = this.AllFiles ? "/**/*" : "";

        if ( this.IsAbsolute )
        {
            return $"{prefix}:{this.Source}{suffix} => {this.Target}";
        }
        else
        {
            return $"{prefix}:{checkoutDirectory}/{this.Source}{suffix} => {this.Target}";
        }
    }

    internal string GetRestoreRule( string checkoutDirectory )
    {
        var sign = this.Exclude ? "-" : "+";

        return $"{sign}:{this.Target}/**/* => {checkoutDirectory}/{this.Source}";
    }

    public int CompareTo( ArtifactRule? other )
    {
        if ( ReferenceEquals( this, other ) )
        {
            return 0;
        }

        if ( other is null )
        {
            return 1;
        }

        if ( this.Exclude != other.Exclude && this.Exclude )
        {
            return 1;
        }

        var sourceComparison = string.Compare( this.Source, other.Source, StringComparison.Ordinal );

        if ( sourceComparison != 0 )
        {
            return sourceComparison;
        }

        var targetComparison = string.Compare( this.Target, other.Target, StringComparison.Ordinal );

        if ( targetComparison != 0 )
        {
            return targetComparison;
        }

        return this.Exclude.CompareTo( other.Exclude );
    }
}