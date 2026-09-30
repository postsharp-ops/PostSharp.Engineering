// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using System.Collections.Immutable;

namespace PostSharp.Engineering.BuildTools.Build.Testing;

/// <summary>
/// One Microsoft.Testing.Platform test application: one target framework, and optionally one runtime identifier, of a test
/// project, as <see cref="TestApplicationDiscovery"/> reads it from the evaluation of the project.
/// </summary>
/// <param name="ProjectPath">The full path of the project.</param>
/// <param name="AssemblyName">The name of the assembly of the application.</param>
/// <param name="TargetFramework">The target framework of the application.</param>
/// <param name="RuntimeIdentifier">The runtime identifier that the project sets, or an empty string.</param>
/// <param name="Platforms">The platforms that the application runs on (<c>TestApplicationPlatforms</c>).</param>
/// <param name="Tags">The tags of the application (<c>TestApplicationTag</c>).</param>
/// <param name="RunAlone">Whether the application must not run at the same time as another one (<c>TestApplicationRunAlone</c>).</param>
/// <param name="Skip">A reason not to run the application (<c>TestApplicationSkip</c>), or <c>null</c>.</param>
/// <param name="Artifacts">The build artifacts that the prepare script of the application reads (<c>TestApplicationArtifacts</c>), as
/// paths relative to the repository, with forward slashes.</param>
internal sealed record TestApplication(
    string ProjectPath,
    string AssemblyName,
    string TargetFramework,
    string RuntimeIdentifier,
    ImmutableArray<string> Platforms,
    ImmutableArray<string> Tags,
    bool RunAlone,
    string? Skip,
    ImmutableArray<string> Artifacts )
{
    /// <summary>
    /// Gets the name of the archive of the application, without the extension, as <c>TestArchive.targets</c> writes it.
    /// </summary>
    public string ArchiveName => this.RuntimeIdentifier.Length == 0 ? $"{this.AssemblyName}.{this.TargetFramework}" : $"{this.AssemblyName}.{this.TargetFramework}.{this.RuntimeIdentifier}";

    /// <summary>
    /// Gets the full path of the <c>project.assets.json</c> file of the project, or an empty string for an archive of the
    /// ps1 kind, which publishes nothing.
    /// </summary>
    public string ProjectAssetsFile { get; init; } = "";

    /// <summary>
    /// Gets the moniker of the target framework of the application (<c>TargetFrameworkMoniker</c>), for instance
    /// <c>.NETFramework,Version=v4.8</c>, or an empty string when it is not known.
    /// </summary>
    /// <remarks>
    /// The restore graph (<c>project.assets.json</c>) names the target of a .NET Framework application by this moniker and
    /// not by <see cref="TargetFramework"/>, whereas it names the target of a .NET 5 or later application by the short name
    /// of the framework, which is <see cref="TargetFramework"/>. See <see cref="TestArchivePackages"/>.
    /// </remarks>
    public string TargetFrameworkMoniker { get; init; } = "";

    /// <summary>
    /// Gets the packages of the application that the build configurations download from the builds that publish them, as
    /// <see cref="TestArchivePackages.Package.Reference"/> gives them. <c>generate-scripts</c> sets them after a restore.
    /// </summary>
    public ImmutableArray<string> Packages { get; init; } = [];

    /// <summary>
    /// Gets the directory of an artifact, which the build configurations download it to.
    /// </summary>
    public static string GetArtifactDirectory( string artifact )
    {
        var separator = artifact.LastIndexOf( '/' );

        return separator < 0 ? "" : artifact[..separator];
    }
}
