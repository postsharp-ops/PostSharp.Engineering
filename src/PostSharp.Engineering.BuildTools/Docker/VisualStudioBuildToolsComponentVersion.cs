// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

namespace PostSharp.Engineering.BuildTools.Docker;

public sealed class VisualStudioBuildToolsComponentVersion
{
    internal string ManifestFilename { get; }

    internal string InstallCatalogueUri { get; }

    private VisualStudioBuildToolsComponentVersion( string manifestFilename, string installCatalogueUri )
    {
        this.ManifestFilename = manifestFilename;
        this.InstallCatalogueUri = installCatalogueUri;
    }

    // ReSharper disable once InconsistentNaming
    public static readonly VisualStudioBuildToolsComponentVersion v17_14_15 = new(
        "VisualStudio.17.14.15.Release.chman",
        "https://download.visualstudio.microsoft.com/download/pr/eb5f7427-d28f-4e06-95cc-093f6c2070c8/3480d7a528bad877857c92843bb1e9ce8ebd48a2bffcee366a98a7343f4d32fb/VisualStudio.vsman" );

    // ReSharper disable once InconsistentNaming
    public static readonly VisualStudioBuildToolsComponentVersion v17_14_23 = new(
        "VisualStudio.17.14.23.Release.chman",
        "https://download.visualstudio.microsoft.com/download/pr/a80deb24-6a28-4d30-b99f-13b6e89c9727/cd752233e77a8cf93a6b83ca3be9d3b8b78f030bfc4abc774c774f64284c8844/VisualStudio.vsman" );
}