// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

namespace PostSharp.Engineering.BuildTools.Build.Model;

internal record VersionComponents(
    string MainVersion,
    string VersionPrefix,
    int PatchNumber,
    string VersionSuffix );