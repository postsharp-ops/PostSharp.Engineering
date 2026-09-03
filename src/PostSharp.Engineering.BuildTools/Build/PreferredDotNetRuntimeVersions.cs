// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

// ReSharper disable InconsistentNaming

using JetBrains.Annotations;

namespace PostSharp.Engineering.BuildTools.Build;

/// <summary>
/// Preferred versions of the .NET runtime, one per major version of .NET. A product family sets this set through
/// <see cref="PreferredDotNetVersions.DotNetRuntime"/>.
/// </summary>
[PublicAPI]
public record PreferredDotNetRuntimeVersions
{
    // These values are the single source of truth of the default versions. The obsolete PreferredVersions class
    // exposes them as constants, which is why they are declared as constants here instead of as literals in the
    // property initializers.
    internal const string DefaultV_10_0 = "10.0.2";
    internal const string DefaultV_9_0 = "9.0.12";
    internal const string DefaultV_8_0 = "8.0.23";
    internal const string DefaultV_6_0 = "6.0.36";

    /// <summary>
    /// Gets the preferred version of the .NET 10.0 runtime.
    /// </summary>
    public string V_10_0 { get; init; } = DefaultV_10_0;

    /// <summary>
    /// Gets the preferred version of the .NET 9.0 runtime.
    /// </summary>
    public string V_9_0 { get; init; } = DefaultV_9_0;

    /// <summary>
    /// Gets the preferred version of the .NET 8.0 runtime.
    /// </summary>
    public string V_8_0 { get; init; } = DefaultV_8_0;

    /// <summary>
    /// Gets the preferred version of the .NET 6.0 runtime.
    /// </summary>
    public string V_6_0 { get; init; } = DefaultV_6_0;
}
