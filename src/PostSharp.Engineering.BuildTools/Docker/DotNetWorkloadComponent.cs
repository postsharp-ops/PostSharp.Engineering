// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using JetBrains.Annotations;
using System.IO;

namespace PostSharp.Engineering.BuildTools.Docker;

/// <summary>
/// Installs a .NET workload, for instance <c>maui-windows</c>, by running <c>dotnet workload install</c>.
/// </summary>
/// <remarks>
/// The image must contain a .NET SDK, which a <see cref="DotNetComponent"/> with
/// <see cref="DotNetComponentKind.Sdk"/> installs. This component does not add that component itself, because
/// it cannot choose the version that the product builds with.
/// </remarks>
[PublicAPI]
public sealed class DotNetWorkloadComponent : ContainerComponent
{
    /// <summary>
    /// Gets the identifier of the workload, for instance <c>maui-windows</c>.
    /// </summary>
    public string Workload { get; }

    /// <summary>
    /// Gets a value indicating whether prerelease workload manifests are accepted, that is, whether
    /// <c>--include-previews</c> is passed to <c>dotnet workload install</c>.
    /// </summary>
    public bool IncludePreviews { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="DotNetWorkloadComponent"/> class.
    /// </summary>
    /// <param name="workload">The identifier of the workload, for instance <c>maui-windows</c>.</param>
    /// <param name="includePreviews">
    /// Set this parameter to <c>true</c> when the main SDK of the product is a prerelease. A feature band
    /// publishes its workload manifests under an identifier that carries the prerelease label of the band, for
    /// instance <c>Microsoft.NET.Sdk.Maui.Manifest-11.0.100-rc.2</c>, and the identifier without that label does
    /// not exist until the band ships. Without this option, the command then finds no manifest.
    /// </param>
    public DotNetWorkloadComponent( string workload, bool includePreviews = false )
    {
        this.Workload = workload;
        this.IncludePreviews = includePreviews;
    }

    public override string Name => $"Install .NET workload {this.Workload}";

    public override string Key => $"{nameof(DotNetWorkloadComponent)}:{this.Workload}";

    public override ContainerComponentKind Kind => ContainerComponentKind.DotNetWorkload;

    public override void WriteDockerfile( TextWriter writer, ContainerOperatingSystem operatingSystem )
    {
        // The command is the same on Linux and on Windows, so, unlike DotNetComponent, this component needs no
        // branch per operating system.
        //
        // The instruction must contain no double quote. A generated Windows Dockerfile sets SHELL to
        // powershell.exe -Command, and Docker passes the instruction to that shell in a form that loses double
        // quotes before PowerShell parses the text. An earlier version of this step wrote a JSON file with
        // Set-Content -Value '{ "sdk": ... }', the file came out as { sdk: ... }, and the .NET SDK rejected it
        // with the error 's' is an invalid start of a property name. If a later change needs JSON, build it from
        // a hash table and ConvertTo-Json instead of writing the text literally.
        //
        // The command installs the workload for the feature band of the SDK that runs it, which in a generated
        // image is the highest installed SDK, therefore the main SDK of the product. No global.json is needed,
        // and pinning the band with one is harmful, because the workload is then installed for a band that the
        // product does not build with.
        //
        // The command has no --skip-sign-check option in .NET 10, so a signature check cannot be skipped here.
        var options = this.IncludePreviews ? " --include-previews" : "";

        writer.WriteLine( $"RUN dotnet workload install {this.Workload}{options}" );
    }

    public override string ToString() => $"{this.Kind} {this.Workload}";
}
