// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using JetBrains.Annotations;
using PostSharp.Engineering.BuildTools.Dependencies.Model;

namespace PostSharp.Engineering.BuildTools.Build;

/// <summary>
/// Preferred versions of the .NET SDK and of the .NET runtime of a product family. See
/// <see cref="ProductFamily.PreferredVersions"/>.
/// </summary>
/// <remarks>
/// The goal is to increase the reuse of Docker layers by using a small subset of versions. The set is a member of the
/// product family and not a global one because a family pins its own Visual Studio baseline, and Visual Studio installs
/// a .NET SDK of its own. Two families with different baselines therefore cannot share the same .NET SDK version.
/// Update only when necessary.
/// </remarks>
[PublicAPI]
public record PreferredDotNetVersions
{
    /// <summary>
    /// Gets the set of versions used by a product family that does not set <see cref="ProductFamily.PreferredVersions"/>.
    /// </summary>
    public static PreferredDotNetVersions Default { get; } = new();

    /// <summary>
    /// Gets the preferred versions of the .NET SDK.
    /// </summary>
    public PreferredDotNetSdkVersions DotNetSdk { get; init; } = new();

    /// <summary>
    /// Gets the preferred versions of the .NET runtime.
    /// </summary>
    public PreferredDotNetRuntimeVersions DotNetRuntime { get; init; } = new();
}
