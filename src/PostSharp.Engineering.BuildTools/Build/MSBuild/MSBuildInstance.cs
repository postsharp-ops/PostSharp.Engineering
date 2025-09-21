// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using System;

namespace PostSharp.Engineering.BuildTools.Build.MSBuild;

internal sealed record MSBuildInstance(
    string Name,
    Version Version,
    string FullVersion,
    string Path,
    string Source );