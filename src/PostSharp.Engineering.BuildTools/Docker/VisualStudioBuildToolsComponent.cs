// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using JetBrains.Annotations;
using PostSharp.Engineering.BuildTools.Build;
using PostSharp.Engineering.BuildTools.Utilities;
using System;
using System.IO;
using System.Linq;

namespace PostSharp.Engineering.BuildTools.Docker;

[PublicAPI]
public sealed class VisualStudioBuildToolsComponent : ContainerComponent
{
    private const string _installPath = @"C:\BuildTools";
    private const string _bootstrapperPath = @"C:\vs_buildtools.exe";
    private const string _productId = "Microsoft.VisualStudio.Product.BuildTools";

    /// <summary>
    /// The number of VS components installed by a single invocation of the installer, i.e. by a single Docker
    /// layer.
    /// </summary>
    private const int _componentsPerLayer = 3;

    private readonly VisualStudioBuildToolsComponentVersion _version;

    public string[] Components { get; }

    public override string Name => "Install VS Build Tools";

    public override string Key => $"{nameof(VisualStudioBuildToolsComponent)}:{this._version}:{string.Join( ",", this.Components.OrderBy( x => x ) )}";

    public override ContainerComponentKind Kind => ContainerComponentKind.VsBuildTools;

    // The heavy, rarely-changing VS Build Tools form the base of the chain so the toolchain above can be
    // rebuilt without reinstalling VS. The layer depends on the major version, so that moving a product from
    // Dev17 to Dev18 builds a new base image instead of layering one product line over the other.
    public override string Layer => ContainerLayers.VisualStudio( this._version.MajorVersion );

    [Obsolete( "Specify the VisualStudioBuildToolsComponentVersion/" )]
    public VisualStudioBuildToolsComponent( string[] vsComponents ) : this( VisualStudioBuildToolsComponentVersion.v17_14_15, vsComponents ) { }

    public VisualStudioBuildToolsComponent( VisualStudioBuildToolsComponentVersion version, string[] vsComponents )
    {
        this._version = version;
        this.Components = vsComponents;
    }

    public override void WriteDockerfile( TextWriter writer, ContainerOperatingSystem operatingSystem )
    {
        writer.WriteLine( $"COPY {this._version.ManifestFilename} /{this._version.ManifestFilename}" );

        // First layer: download the bootstrapper and let it lay down the VS Installer and a bare Build Tools
        // instance, without any component. The bootstrapper and the channel manifest are deliberately left in
        // the image, because every batch below is a separate process that needs both; a few megabytes against
        // the tens of gigabytes of the Build Tools themselves.
        writer.WriteLine( $"RUN Invoke-WebRequest -Uri {this._version.BootstrapperUri} -OutFile {_bootstrapperPath}" );

        WriteInstallerRun(
            writer,
            $"""
             "--quiet", "--wait", "--norestart", "--nocache", "--installPath", "{_installPath}", "--installChannelUri", "C:\{this._version.ManifestFilename}", "--installCatalogUri", "{this._version.InstallCatalogueUri}", "--productId", "{_productId}"
             """ );

        // Then one layer per batch of components. Installing everything in a single invocation produced one
        // ~20 GB layer, which is slow to push and pull and lost in full whenever the installer fails.
        // Batching alphabetically keeps the split independent of the order in which the product definition
        // happens to list the components, so the same set of components always yields the same layers.
        foreach ( var batch in this.Components.OrderBy( x => x, StringComparer.OrdinalIgnoreCase ).Chunk( _componentsPerLayer ) )
        {
            var components = string.Join( ", ", batch.Select( x => $"\"--add\", \"{x}\"" ) );

            WriteInstallerRun(
                writer,
                $"""
                 "modify", "--quiet", "--wait", "--norestart", "--nocache", "--installPath", "{_installPath}", "--channelId", "{this._version.ChannelId}", "--productId", "{_productId}", {components}
                 """ );
        }

        // Define VSINSTALLDIR
        writer.WriteLine( "ENV VSINSTALLDIR=C:\\BuildTools" );

        // Define VSSDKINSTALLDIR
        if ( this.Components.Contains( "Microsoft.VisualStudio.Component.VSSDKBuildTools" ) )
        {
            writer.WriteLine( "ENV VSSDKINSTALL=C:\\BuildTools\\VSSDK" );
        }

        // We must always create "C:\Program Files (x86)\Microsoft Visual Studio\Shared\NuGetPackages" or MSBuild might complain.
        writer.WriteLine(
            """
            RUN New-Item -ItemType Directory -Path 'C:\Program Files (x86)\Microsoft Visual Studio\Shared\NuGetPackages' -Force | Out-Null"; `
                New-Item -ItemType Directory -Path 'C:\Program Files\dotnet\sdk\NuGetFallbackFolder' -Force | Out-Null
            """ );
    }

    // A single invocation of the installer, with the setup logs dumped into the build output when it fails:
    // in quiet mode the installer itself reports nothing but an exit code.
    private static void WriteInstallerRun( TextWriter writer, string arguments )
    {
        writer.WriteLine(
            $$"""
              RUN $process = Start-Process {{_bootstrapperPath}} -NoNewWindow -Wait -PassThru `
                      -ArgumentList {{arguments}}; `
                  if ($process.ExitCode -ne 0) { `
                      Get-ChildItem "$env:TEMP\dd_*.log" -ErrorAction SilentlyContinue | ForEach-Object { `
                          Write-Host "=== Contents of $($_.Name) ==="; `
                          Get-Content $_.FullName; `
                          Write-Host "=== End of $($_.Name) ===" `
                      }; `
                      exit $process.ExitCode; `
                  }
              """ );
    }

    public override void PopulateContextDirectory( BuildContext context, string directory )
    {
        EmbeddedResourceHelper.ExtractResource( context, this._version.ManifestFilename, directory );
    }

    // ReSharper disable once InconsistentNaming
    public bool RequireVSComponent( BuildContext context, string component )
    {
        if ( !this.Components.Contains( component ) )
        {
            context.Console.WriteError( $"The VS component {component} is required." );

            return false;
        }

        return true;
    }
}