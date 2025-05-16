// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

namespace PostSharp.Engineering.BuildTools.BillOfMaterials;

/// <summary>
/// Specifies how a project will be used by its consumers. Used to construct the SBOM.
/// </summary>
/// <param name="Pattern">A globbing pattern capturing the project path.</param>
/// <param name="Kind">The usage kind.</param>
public record ProjectUsageInfo( string Pattern, DependentPackageUsageKind Kind, string[]? PublicFacingPackages = null );