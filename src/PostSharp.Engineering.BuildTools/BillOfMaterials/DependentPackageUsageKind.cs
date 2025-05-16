// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

namespace PostSharp.Engineering.BuildTools.BillOfMaterials;

public enum DependentPackageUsageKind
{
    /// <summary>
    /// Flows with the end-product.
    /// </summary>
    Default,

    /// <summary>
    /// Development dependency. Used to build the end-user product, but not to run it.
    /// </summary>
    Development,

    /// <summary>
    /// Private asset to the referring repo. Not used by the end-user - neither at run time nor at run time.
    /// </summary>
    Private,

    /// <summary>
    /// The package is used for reference but is not shipped with the product.
    /// </summary>
    Reference
}