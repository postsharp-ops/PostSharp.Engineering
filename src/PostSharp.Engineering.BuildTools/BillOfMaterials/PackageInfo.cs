// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using System.Collections.Generic;

namespace PostSharp.Engineering.BuildTools.BillOfMaterials;

internal class PackageInfo
{
    public required string Name { get; init; }

    public Dictionary<string, PackageVersionInfo> Versions { get; } = [];
}