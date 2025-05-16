// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using System.Collections.Generic;

namespace PostSharp.Engineering.BuildTools.BillOfMaterials;

internal class PackageVersionInfo
{
    public required string Version { get; init; }

    public required string? License { get; set; }

    public required string? Owners { get; init; }

    public required string? SourceRepository { get; set; }

    public HashSet<DependentPackageUsageKind> Usage { get; } = [];

    public HashSet<string> UsedBy { get; } = [];
}