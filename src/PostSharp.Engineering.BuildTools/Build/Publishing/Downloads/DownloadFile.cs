// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using JetBrains.Annotations;
using System;
using System.IO;

namespace PostSharp.Engineering.BuildTools.Build.Publishing.Downloads;

[PublicAPI]
public record DownloadFile( string Name, DateTime CreatedAt, string? Description, string? Instructions )
{
    public static DownloadFile Create( string path, string? description, string? instructions )
    {
        var name = Path.GetFileName( path );
        var createdAt = File.GetCreationTimeUtc( path );

        return new( name, createdAt, description, instructions );
    }
}