// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using PostSharp.Engineering.BuildTools.Build;
using PostSharp.Engineering.BuildTools.Utilities;
using System.IO;
using System.Linq;

namespace PostSharp.Engineering.BuildTools.Docker;

public class VisualStudioBuildToolsComponent : ContainerComponent
{
    private readonly string[] _vsComponents;
    private const string _channelManifestFilename = "VisualStudio.17.Release.chman";

    private const string _installCatalogueUri =
        "https://download.visualstudio.microsoft.com/download/pr/eb5f7427-d28f-4e06-95cc-093f6c2070c8/3480d7a528bad877857c92843bb1e9ce8ebd48a2bffcee366a98a7343f4d32fb/VisualStudio.vsman";

    public override string Name => "Install VS Build Tools";

    public override ContainerComponentKind Kind => ContainerComponentKind.VsBuildTools;

    public VisualStudioBuildToolsComponent( string[] vsComponents )
    {
        this._vsComponents = vsComponents;
    }

    public override void WriteDockerfile( TextWriter writer )
    {
        var components = string.Join( ", ", this._vsComponents.Select( x => $"\"--add\", \"{x}\"" ) );

        writer.WriteLine(
            $$"""
              COPY {{_channelManifestFilename}} /{{_channelManifestFilename}}
              RUN Invoke-WebRequest -Uri https://aka.ms/vs/17/release/vs_buildtools.exe -OutFile vs_buildtools.exe; `
                  $process = Start-Process .\vs_buildtools.exe -NoNewWindow -Wait -PassThru `
                      -ArgumentList  "--quiet", "--wait", "--norestart", "--nocache",  "--installPath", "C:\BuildTools", "--installChannelUri", "c:\{{_channelManifestFilename}}", "--installCatalogUri", "{{_installCatalogueUri}}", "--productId", "Microsoft.VisualStudio.Product.BuildTools", {{components}}; `        
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
    }


    public override void PopulateContextDirectory( BuildContext context, string directory )
    {
        EmbeddedResourceHelper.ExtractResource( context, _channelManifestFilename, directory );
    }
}