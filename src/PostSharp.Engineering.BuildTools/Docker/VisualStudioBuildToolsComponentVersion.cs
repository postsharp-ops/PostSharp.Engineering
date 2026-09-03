// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

namespace PostSharp.Engineering.BuildTools.Docker;

/// <summary>
/// A pinned version of the Visual Studio Build Tools, i.e. the triple of channel manifest, installation
/// catalogue and bootstrapper that <see cref="VisualStudioBuildToolsComponent"/> feeds to the installer.
/// </summary>
/// <remarks>
/// To add a version, download <c>https://aka.ms/vs/{major}/{channel}/channel</c> (<c>17/release</c> for Dev17,
/// <c>18/stable</c> for Dev18), save it to <c>Resources</c> under the name given by
/// <see cref="ManifestFilename"/>, and read <see cref="InstallCatalogueUri"/> out of it: it is the payload URL
/// of the <c>Microsoft.VisualStudio.Manifests.VisualStudio</c> channel item.
/// </remarks>
public sealed class VisualStudioBuildToolsComponentVersion
{
    private const string _dev17Bootstrapper = "https://aka.ms/vs/17/release/vs_buildtools.exe";
    private const string _dev18Bootstrapper = "https://aka.ms/vs/18/stable/vs_buildtools.exe";

    /// <summary>
    /// Gets the product display version, e.g. <c>17.14.39</c>.
    /// </summary>
    internal string Version { get; }

    /// <summary>
    /// Gets the name of the embedded channel manifest resource.
    /// </summary>
    internal string ManifestFilename => $"VisualStudio.{this.Version}.Release.chman";

    /// <summary>
    /// Gets the URI of the <c>VisualStudio.vsman</c> installation catalogue.
    /// </summary>
    internal string InstallCatalogueUri { get; }

    /// <summary>
    /// Gets the URI of the <c>vs_buildtools.exe</c> bootstrapper. It must belong to the same product line as
    /// <see cref="InstallCatalogueUri"/>, because a Dev17 bootstrapper cannot install a Dev18 channel.
    /// </summary>
    internal string BootstrapperUri { get; }

    private VisualStudioBuildToolsComponentVersion( string version, string installCatalogueUri, string bootstrapperUri )
    {
        this.Version = version;
        this.InstallCatalogueUri = installCatalogueUri;
        this.BootstrapperUri = bootstrapperUri;
    }

    // This is interpolated into VisualStudioBuildToolsComponent.Key, i.e. into the Docker layer cache key, so
    // it is what makes two versions resolve to two different images. Without it the default implementation
    // would return the type name and every version would share a single cached layer.
    public override string ToString() => this.Version;

    // ReSharper disable once InconsistentNaming
    public static readonly VisualStudioBuildToolsComponentVersion v17_14_15 = new(
        "17.14.15",
        "https://download.visualstudio.microsoft.com/download/pr/eb5f7427-d28f-4e06-95cc-093f6c2070c8/3480d7a528bad877857c92843bb1e9ce8ebd48a2bffcee366a98a7343f4d32fb/VisualStudio.vsman",
        _dev17Bootstrapper );

    // ReSharper disable once InconsistentNaming
    public static readonly VisualStudioBuildToolsComponentVersion v17_14_23 = new(
        "17.14.23",
        "https://download.visualstudio.microsoft.com/download/pr/a80deb24-6a28-4d30-b99f-13b6e89c9727/cd752233e77a8cf93a6b83ca3be9d3b8b78f030bfc4abc774c774f64284c8844/VisualStudio.vsman",
        _dev17Bootstrapper );

    // ReSharper disable once InconsistentNaming
    public static readonly VisualStudioBuildToolsComponentVersion v17_14_39 = new(
        "17.14.39",
        "https://download.visualstudio.microsoft.com/download/pr/fa619120-9c0e-47e6-bfe0-3ee96fb671b2/bd98dd01efa4195cb1c11030da63b9e4a3bcec7bc406799a9db80339d6dabd79/VisualStudio.vsman",
        _dev17Bootstrapper );

    /// <summary>
    /// Visual Studio 2026 (Dev18) 18.9.2, the Stable release of 25 August 2026. This is the lowest line that
    /// officially supports targeting <c>net10.0</c>: MSBuild 17.14 accepts the .NET 10 SDK but warns and is
    /// unsupported for <c>net10.0</c>. It bundles the .NET 10.0.4xx SDK, the last .NET 10 feature band.
    /// </summary>
    /// <remarks>
    /// The Stable channel is the only one available: the 2026-LTSC channel is not published until November
    /// 2026. Re-pin to it once it exists.
    /// </remarks>
    // ReSharper disable once InconsistentNaming
    public static readonly VisualStudioBuildToolsComponentVersion v18_9_2 = new(
        "18.9.2",
        "https://download.visualstudio.microsoft.com/download/pr/fe4fb3e6-ea32-4ae3-b154-72821a274f0d/29d05070615bd4bbe095bee9716d248be7661e516424fb9d06597ce4f3ab99ca/VisualStudio.vsman",
        _dev18Bootstrapper );
}