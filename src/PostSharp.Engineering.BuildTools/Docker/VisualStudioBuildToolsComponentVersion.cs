// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using System;

namespace PostSharp.Engineering.BuildTools.Docker;

/// <summary>
/// A pinned version of the Visual Studio Build Tools, i.e. the triple of channel manifest, installation
/// catalogue and bootstrapper that <see cref="VisualStudioBuildToolsComponent"/> feeds to the installer.
/// </summary>
/// <remarks>
/// To add a version, download <c>https://aka.ms/vs/{major}/{channel}/channel</c> (<c>17/release</c> for the
/// Dev17 Release channel, <c>18/stable</c> for the Dev18 Release channel, <c>18/insiders</c> for the Dev18
/// Preview channel), save it to <c>Resources</c> under the name given by <see cref="ManifestFilename"/>, and
/// read <see cref="InstallCatalogueUri"/> out of it: it is the payload URL of the
/// <c>Microsoft.VisualStudio.Manifests.VisualStudio</c> channel item on a Release channel, and of the
/// <c>Microsoft.VisualStudio.Manifests.VisualStudioPreview</c> item on a Preview channel.
/// </remarks>
/// <remarks>
/// Then, if the new build belongs to a line that already has a property, such as <see cref="v17_14"/>, point that
/// property at it and mark the build it replaces with <see cref="UnservicedBuildDiagnosticId"/> if Microsoft no longer
/// services it. If the new build opens a line, add a property for that line instead, and leave the existing ones alone:
/// moving a product to a new minor version is a deliberate edit, not a re-pin. A product names a line property, so a
/// build that no property resolves to is reachable only by a product that names it on purpose.
/// </remarks>
public sealed class VisualStudioBuildToolsComponentVersion
{
    private const string _dev17Bootstrapper = "https://aka.ms/vs/17/release/vs_buildtools.exe";
    private const string _dev18Bootstrapper = "https://aka.ms/vs/18/stable/vs_buildtools.exe";
    private const string _dev18PreviewBootstrapper = "https://aka.ms/vs/18/insiders/vs_buildtools.exe";

    /// <summary>
    /// Identifier of the diagnostic that reports a use of a build that Microsoft no longer services.
    /// </summary>
    /// <remarks>
    /// The two identifiers are custom instead of CS0618 so that a repository can suppress or escalate each on its
    /// own, and so that CodeQuality.targets can report them without failing a continuous integration build while
    /// the consuming repositories move to a line property. They are separate from each other because an unserviced
    /// build is a defect to correct, whereas a pinned build is a deliberate choice that is merely discouraged.
    /// </remarks>
    public const string UnservicedBuildDiagnosticId = "PSENG0002";

    /// <summary>
    /// Identifier of the diagnostic that reports a use of a build-specific field where a line property would do.
    /// </summary>
    /// <seealso cref="UnservicedBuildDiagnosticId"/>
    public const string SpecificBuildDiagnosticId = "PSENG0003";

    /// <summary>
    /// Gets the product display version, e.g. <c>17.14.39</c>.
    /// </summary>
    internal string Version { get; }

    /// <summary>
    /// Gets the major version, e.g. <c>18</c>. It selects the container layer, because the Build Tools of one
    /// major version are a separate installation from those of another.
    /// </summary>
    internal string MajorVersion => this.Version.Split( '.' )[0];

    /// <summary>
    /// Gets the name of the update channel, <c>Release</c> or <c>Preview</c>. The Insiders channel publishes
    /// under <c>Preview</c>, and the installer treats a Preview instance as separate from a Release instance of
    /// the same major version.
    /// </summary>
    internal string Channel { get; }

    /// <summary>
    /// Gets the name of the embedded channel manifest resource.
    /// </summary>
    internal string ManifestFilename => $"VisualStudio.{this.Version}.{this.Channel}.chman";

    /// <summary>
    /// Gets the id of the update channel, e.g. <c>VisualStudio.18.Release</c>. It is the part of the channel
    /// manifest's <c>info.id</c> that precedes the slash, and the installer requires it to identify the
    /// instance in the <c>modify</c> operations that install the components.
    /// </summary>
    internal string ChannelId => $"VisualStudio.{this.MajorVersion}.{this.Channel}";

    /// <summary>
    /// Gets the URI of the <c>VisualStudio.vsman</c> installation catalogue.
    /// </summary>
    internal string InstallCatalogueUri { get; }

    /// <summary>
    /// Gets the URI of the <c>vs_buildtools.exe</c> bootstrapper. It must belong to the same product line as
    /// <see cref="InstallCatalogueUri"/>, because a Dev17 bootstrapper cannot install a Dev18 channel.
    /// </summary>
    internal string BootstrapperUri { get; }

    private VisualStudioBuildToolsComponentVersion(
        string version,
        string installCatalogueUri,
        string bootstrapperUri,
        string channel = "Release" )
    {
        this.Version = version;
        this.InstallCatalogueUri = installCatalogueUri;
        this.BootstrapperUri = bootstrapperUri;
        this.Channel = channel;
    }

    // This is interpolated into VisualStudioBuildToolsComponent.Key, i.e. into the Docker layer cache key, so
    // it is what makes two versions resolve to two different images. Without it the default implementation
    // would return the type name and every version would share a single cached layer.
    public override string ToString() => this.Version;

    /// <summary>
    /// Gets the most recent Release channel build that this class carries, currently <see cref="v18_9"/>. A product
    /// that names this property moves to a new minor version, and therefore to a new base image, as soon as this
    /// class carries one. Name a line property instead where that move has to be a deliberate edit.
    /// </summary>
    public static VisualStudioBuildToolsComponentVersion LatestStable => v18_9;

    /// <summary>
    /// Gets the most recent Preview channel build that this class carries, currently <see cref="v18_11"/>. Microsoft
    /// markets that channel as Insiders. It serves pre-release builds, so this property is for testing a future
    /// Visual Studio, not for a production image.
    /// </summary>
    public static VisualStudioBuildToolsComponentVersion LatestPreview => v18_11;

    // The properties below are the supported way to select a version. Each names a line, i.e. the builds that share
    // a major and a minor version, and resolves to the build that this class currently carries for that line. A
    // product that names a line follows a re-pin within that line without any change of its own; a product that
    // names a build has to be edited every time the build stops being serviced. A line never crosses a minor
    // version, because two minor versions can differ by more than a fix, so moving a product from 18.9 to 18.11 stays
    // a deliberate edit. The pragma suppresses the obsolescence of the build-specific fields, which these properties
    // exist to hide.
#pragma warning disable PSENG0002
#pragma warning disable PSENG0003

    /// <summary>
    /// Gets the build of the Visual Studio 2022 (Dev17) 17.14 line that this class carries, currently
    /// <see cref="v17_14_39"/>. 17.14 is the servicing baseline of the Dev17 Release channel, and the only Dev17 line
    /// still served.
    /// </summary>
    // ReSharper disable once InconsistentNaming
    public static VisualStudioBuildToolsComponentVersion v17_14 => v17_14_39;

    /// <summary>
    /// Gets the build of the Visual Studio 2026 (Dev18) 18.9 line that this class carries, currently
    /// <see cref="v18_9_2"/>. 18.9 is the lowest Dev18 line that officially supports targeting <c>net10.0</c>. The
    /// Dev18 Release channel rolls forward rather than servicing a baseline, so a later line exists on it.
    /// </summary>
    // ReSharper disable once InconsistentNaming
    public static VisualStudioBuildToolsComponentVersion v18_9 => v18_9_2;

    /// <summary>
    /// Gets the build of the Visual Studio (Dev18) 18.11 line that this class carries, currently
    /// <see cref="v18_11_0"/>. 18.11 is served by the Preview channel, and is the pre-release of Visual Studio 2027.
    /// </summary>
    // ReSharper disable once InconsistentNaming
    public static VisualStudioBuildToolsComponentVersion v18_11 => v18_11_0;

#pragma warning restore PSENG0002
#pragma warning restore PSENG0003

    /// <summary>
    /// Visual Studio 2022 (Dev17) 17.14.15.
    /// </summary>
    [Obsolete(
        "Visual Studio services only the latest build of a servicing baseline, so 17.14.15 receives no further fix. Use v17_14.",
        DiagnosticId = UnservicedBuildDiagnosticId )]
    // ReSharper disable once InconsistentNaming
    public static readonly VisualStudioBuildToolsComponentVersion v17_14_15 = new(
        "17.14.15",
        "https://download.visualstudio.microsoft.com/download/pr/eb5f7427-d28f-4e06-95cc-093f6c2070c8/3480d7a528bad877857c92843bb1e9ce8ebd48a2bffcee366a98a7343f4d32fb/VisualStudio.vsman",
        _dev17Bootstrapper );

    /// <summary>
    /// Visual Studio 2022 (Dev17) 17.14.23.
    /// </summary>
    [Obsolete(
        "Visual Studio services only the latest build of a servicing baseline, so 17.14.23 receives no further fix. Use v17_14.",
        DiagnosticId = UnservicedBuildDiagnosticId )]
    // ReSharper disable once InconsistentNaming
    public static readonly VisualStudioBuildToolsComponentVersion v17_14_23 = new(
        "17.14.23",
        "https://download.visualstudio.microsoft.com/download/pr/a80deb24-6a28-4d30-b99f-13b6e89c9727/cd752233e77a8cf93a6b83ca3be9d3b8b78f030bfc4abc774c774f64284c8844/VisualStudio.vsman",
        _dev17Bootstrapper );

    /// <summary>
    /// Visual Studio 2022 (Dev17) 17.14.39.
    /// </summary>
    [Obsolete(
        "Use v17_14, which resolves to the build currently carried for the 17.14 baseline.",
        DiagnosticId = SpecificBuildDiagnosticId )]
    // ReSharper disable once InconsistentNaming
    public static readonly VisualStudioBuildToolsComponentVersion v17_14_39 = new(
        "17.14.39",
        "https://download.visualstudio.microsoft.com/download/pr/fa619120-9c0e-47e6-bfe0-3ee96fb671b2/bd98dd01efa4195cb1c11030da63b9e4a3bcec7bc406799a9db80339d6dabd79/VisualStudio.vsman",
        _dev17Bootstrapper );

    /// <summary>
    /// Visual Studio 2026 (Dev18) 18.0.0, the first Stable release of the Dev18 line. It is the version
    /// installed by the canonical Windows image of PostSharp 2026.0.
    /// </summary>
    /// <remarks>
    /// The Stable channel serves a later version, so <c>https://aka.ms/vs/18/stable/channel</c> no longer
    /// returns this channel manifest. The embedded copy is the only remaining source of it.
    /// </remarks>
    [Obsolete(
        "The Dev18 Stable channel no longer serves 18.0.0, and it predates the net10.0 targeting support of 18.9. Use v18_9.",
        DiagnosticId = UnservicedBuildDiagnosticId )]
    // ReSharper disable once InconsistentNaming
    public static readonly VisualStudioBuildToolsComponentVersion v18_0_0 = new(
        "18.0.0",
        "https://download.visualstudio.microsoft.com/download/pr/d3b4e0f6-4bc0-4ec0-ba9c-20b355d61cc4/ccd546b5752c6afac8992e5810260d4cbc52192108a1b70390604fa14b4329d1/VisualStudio.vsman",
        _dev18Bootstrapper );

    /// <summary>
    /// Visual Studio 2026 (Dev18) 18.9.2, the Stable release of 25 August 2026. This is the lowest line that
    /// officially supports targeting <c>net10.0</c>: MSBuild 17.14 accepts the .NET 10 SDK but warns and is
    /// unsupported for <c>net10.0</c>. It bundles the .NET 10.0.4xx SDK, the last .NET 10 feature band.
    /// </summary>
    /// <remarks>
    /// The Stable channel is the only one available: the 2026-LTSC channel is not published until November
    /// 2026. Re-pin to it once it exists.
    /// </remarks>
    [Obsolete(
        "Use v18_9, which resolves to the build currently carried for the 18.9 line, or LatestStable.",
        DiagnosticId = SpecificBuildDiagnosticId )]
    // ReSharper disable once InconsistentNaming
    public static readonly VisualStudioBuildToolsComponentVersion v18_9_2 = new(
        "18.9.2",
        "https://download.visualstudio.microsoft.com/download/pr/fe4fb3e6-ea32-4ae3-b154-72821a274f0d/29d05070615bd4bbe095bee9716d248be7661e516424fb9d06597ce4f3ab99ca/VisualStudio.vsman",
        _dev18Bootstrapper );

    /// <summary>
    /// Visual Studio (Dev18) 18.11.0, build 12202.211, from the Preview channel. Microsoft has announced Visual
    /// Studio 2027 as an in-place update of the Dev18 line rather than a new product line, so the pre-release builds
    /// of it are served by this channel; no Dev19 channel is published.
    /// </summary>
    /// <remarks>
    /// The channel advances every few weeks, so <c>https://aka.ms/vs/18/insiders/channel</c> returns a later channel
    /// manifest than the embedded copy. This entry exists to test against Visual Studio 2027 before it ships; move to
    /// a Release entry once it does, expected in November 2026.
    /// </remarks>
    [Obsolete(
        "Use v18_11, which resolves to the build currently carried for the 18.11 line, or LatestPreview.",
        DiagnosticId = SpecificBuildDiagnosticId )]
    // ReSharper disable once InconsistentNaming
    public static readonly VisualStudioBuildToolsComponentVersion v18_11_0 = new(
        "18.11.12202.211",
        "https://download.visualstudio.microsoft.com/download/pr/878be7d2-921c-416a-8caa-cfad80ef209b/ede547c4c0a713a4d0fc3e1943b82eb3e535cc789be77b8eb95b89a76d702bee/VisualStudioPreview.vsman",
        _dev18PreviewBootstrapper,
        "Preview" );
}