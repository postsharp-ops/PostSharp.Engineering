// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

// ReSharper disable InconsistentNaming

using JetBrains.Annotations;

namespace PostSharp.Engineering.BuildTools.Build;

/// <summary>
/// List of preferred versions of .NET SDK and .NET.
/// The goal is to increase the reuse of Docker layers by using a small subset of versions.
/// Update only when necessary.
/// </summary>
[PublicAPI]
public static class PreferredVersions
{
    public static class DotNetSdk
    {
        public const string V_10_0 = "10.0.102";
        public const string V_9_0 = "9.0.310";
        public const string V_8_0 = "8.0.417";
        public const string V_6_0 = "6.0.428";
    }

    public static class DotNet
    {
        public const string V_10_0 = "10.0.2";
        public const string V_9_0 = "9.0.12";
        public const string V_8_0 = "8.0.23";
        public const string V_6_0 = "6.0.36";
    }
}   