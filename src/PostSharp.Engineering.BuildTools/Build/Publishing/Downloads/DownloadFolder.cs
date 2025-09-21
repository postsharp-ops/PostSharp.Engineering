// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using JetBrains.Annotations;
using PostSharp.Engineering.BuildTools.Build.Model;
using System;
using System.Collections.Generic;

namespace PostSharp.Engineering.BuildTools.Build.Publishing.Downloads;

[PublicAPI]
public record DownloadFolder(
    string Name,
    string Order,
    DateTime CreatedAt,
    string? Description,
    string? LongDescription,
    string? Instructions,
    IEnumerable<DownloadFile> Files )
{
    public static DownloadFolder Create(
        BuildContext context,
        BuildInfo buildInfo,
        string? description = null,
        string? longDescription = null,
        string? instructions = null,
        IEnumerable<DownloadFile>? files = null )
    {
        var familyName = context.Product.ProductFamily.Name;
        var packageVersion = buildInfo.PackageVersion!;
        var name = $"{familyName} {packageVersion}";
        var order = packageVersion.Split( '-' )[0];
        var createdAt = DateTime.UtcNow;
        files ??= [];

        return new DownloadFolder( name, order, createdAt, description, longDescription, instructions, files );
    }

    public DownloadFolder WithFiles( IEnumerable<DownloadFile> files ) => this with { Files = files };
}