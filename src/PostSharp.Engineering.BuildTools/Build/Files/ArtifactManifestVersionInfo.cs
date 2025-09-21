// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using System;

namespace PostSharp.Engineering.BuildTools.Build.Files;

internal record ArtifactManifestVersionInfo( Version Version, string PackageVersionSuffix )
{
    public string PackageVersion => this.Version.ToString() + this.PackageVersionSuffix;
}