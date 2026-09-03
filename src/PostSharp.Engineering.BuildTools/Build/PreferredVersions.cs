// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

// ReSharper disable InconsistentNaming

using JetBrains.Annotations;
using PostSharp.Engineering.BuildTools.Dependencies.Model;
using System;

namespace PostSharp.Engineering.BuildTools.Build;

/// <summary>
/// List of preferred versions of .NET SDK and .NET.
/// </summary>
/// <remarks>
/// A single global set cannot serve all product families, because a family pins its own Visual Studio baseline and
/// Visual Studio installs a .NET SDK of its own. The versions are therefore a member of the product family. See
/// <see cref="ProductFamily.PreferredVersions"/>.
/// </remarks>
[PublicAPI]
[Obsolete( ObsoleteMessage, DiagnosticId = ObsoleteDiagnosticId )]
public static class PreferredVersions
{
    private const string ObsoleteMessage =
        "Use ProductFamily.PreferredVersions of the product family of the product, for instance MetalamaDependencies.V2027_0.Family.PreferredVersions.";

    /// <summary>
    /// Identifier of the diagnostic that reports a use of this class. It is a custom identifier instead of CS0618 so
    /// that a repository can suppress or escalate it on its own, and so that CodeQuality.targets can report it without
    /// failing a continuous integration build while the consuming repositories are being migrated.
    /// </summary>
    public const string ObsoleteDiagnosticId = "PSENG0001";

    public static class DotNetSdk
    {
        public const string V_10_0 = PreferredDotNetSdkVersions.DefaultV_10_0;
        public const string V_9_0 = PreferredDotNetSdkVersions.DefaultV_9_0;
        public const string V_8_0 = PreferredDotNetSdkVersions.DefaultV_8_0;
        public const string V_6_0 = PreferredDotNetSdkVersions.DefaultV_6_0;
    }

    public static class DotNet
    {
        public const string V_10_0 = PreferredDotNetRuntimeVersions.DefaultV_10_0;
        public const string V_9_0 = PreferredDotNetRuntimeVersions.DefaultV_9_0;
        public const string V_8_0 = PreferredDotNetRuntimeVersions.DefaultV_8_0;
        public const string V_6_0 = PreferredDotNetRuntimeVersions.DefaultV_6_0;
    }
}
