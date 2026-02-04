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
    private readonly VisualStudioBuildToolsComponentVersion _version;

    public string[] Components { get; }

    public override string Name => "Install VS Build Tools";

    public override ContainerComponentKind Kind => ContainerComponentKind.VsBuildTools;

    [Obsolete( "Specify the VisualStudioBuildToolsComponentVersion/" )]
    public VisualStudioBuildToolsComponent( string[] vsComponents ) : this( VisualStudioBuildToolsComponentVersion.v17_14_15, vsComponents ) { }

    public VisualStudioBuildToolsComponent( VisualStudioBuildToolsComponentVersion version, string[] vsComponents )
    {
        this._version = version;
        this.Components = vsComponents;
    }

    public override void WriteDockerfile( TextWriter writer, ContainerOperatingSystem operatingSystem )
    {
        var components = string.Join( ", ", this.Components.Select( x => $"\"--add\", \"{x}\"" ) );

        writer.WriteLine(
            $$"""
              COPY {{this._version.ManifestFilename}} /{{this._version.ManifestFilename}}
              RUN Invoke-WebRequest -Uri https://aka.ms/vs/17/release/vs_buildtools.exe -OutFile vs_buildtools.exe; `
                  $process = Start-Process .\vs_buildtools.exe -NoNewWindow -Wait -PassThru `
                      -ArgumentList  "--quiet", "--wait", "--norestart", "--nocache",  "--installPath", "C:\BuildTools", "--installChannelUri", "c:\{{this._version.ManifestFilename}}", "--installCatalogUri", "{{this._version.InstallCatalogueUri}}", "--productId", "Microsoft.VisualStudio.Product.BuildTools", {{components}}; `        
                  if ($process.ExitCode -ne 0) { `
                   Get-ChildItem "$env:TEMP\dd_*.log" -ErrorAction SilentlyContinue | ForEach-Object { `
                      Write-Host "=== Contents of $($_.Name) ==="; `
                      Get-Content $_.FullName; `
                      Write-Host "=== End of $($_.Name) ===" `
                      }; `
                   exit $process.ExitCode; `
                   }; `
                  Remove-Item C:\\vs_buildtools.exe;
              """ );

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