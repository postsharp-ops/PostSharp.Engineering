// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

namespace PostSharp.Engineering.BuildTools.BillOfMaterials;

/// <summary>
/// Represents an override of values retrieved from nuget.org. In case of <see cref="UsageKind"/>,
/// it overrides <see cref="ProjectUsageInfo"/>.
/// </summary>
public record DependentPackageInfoOverride
{
    public required string Name { get; init; }

    public string? License { get; init; }

    public string? RepositoryUrl { get; init; }

    public DependentPackageUsageKind? UsageKind { get; init; }
}

public record DependentPackageExclusion( string Namespace, string Justification );