// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using System;

namespace PostSharp.Engineering.BuildTools.ContinuousIntegration.Model;

/// <summary>
/// 
/// </summary>
/// <param name="SourcePath">The directory in the source repository.</param>
/// <param name="ArtifactPath">The directory in the artifacts.</param>
/// <param name="Exclude"></param>
/// <param name="IsAbsolute"></param>
/// <param name="AllFiles">Whether the <c>/**/*</c> suffix is appended.</param>
public record ArtifactRule( string SourcePath, string ArtifactPath, bool Exclude = false, bool IsAbsolute = false, bool AllFiles = true ) : IComparable<ArtifactRule>
{
    internal string GetPublishRule( string checkoutDirectory )
    {
        var prefix = this.Exclude ? "-" : "+";
        var suffix = this.AllFiles ? "/**/*" : "";

        if ( this.IsAbsolute )
        {
            return $"{prefix}:{this.SourcePath}{suffix} => {this.ArtifactPath}";
        }
        else
        {
            return $"{prefix}:{checkoutDirectory}/{this.SourcePath}{suffix} => {this.ArtifactPath}";
        }
    }

    internal string GetRestoreRule( string checkoutDirectory )
    {
        var sign = this.Exclude ? "-" : "+";

        return $"{sign}:{this.ArtifactPath}/**/* => {checkoutDirectory}/{this.SourcePath}";
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

        var sourceComparison = string.Compare( this.SourcePath, other.SourcePath, StringComparison.Ordinal );

        if ( sourceComparison != 0 )
        {
            return sourceComparison;
        }

        var targetComparison = string.Compare( this.ArtifactPath, other.ArtifactPath, StringComparison.Ordinal );

        if ( targetComparison != 0 )
        {
            return targetComparison;
        }

        return this.Exclude.CompareTo( other.Exclude );
    }
}