// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using JetBrains.Annotations;
using System.IO;

namespace PostSharp.Engineering.BuildTools.Docker;

[PublicAPI]
public sealed class AzureArtifactsCredentialProviderComponent : ContainerComponent
{
    public override string Name => "AzureArtifactsCredentialProvider";

    public override ContainerComponentKind Kind => ContainerComponentKind.AzureArtifactsCredentialProvider;

    public override void WriteDockerfile( TextWriter writer )
    {
        writer.WriteLine(
            """
            RUN $credProviderUrl = 'https://github.com/microsoft/artifacts-credprovider/releases/download/v1.1.2/Microsoft.NuGet.CredentialProvider.zip'; `
                $credProviderZip = 'Microsoft.NuGet.CredentialProvider.zip'; `
                $pluginsDir = 'C:\ProgramData\NuGet\plugins\netfx\CredentialProvider.Microsoft'; `
                Invoke-WebRequest -Uri $credProviderUrl -OutFile $credProviderZip; `
                New-Item -ItemType Directory -Force -Path $pluginsDir | Out-Null; `
                Expand-Archive -Path $credProviderZip -DestinationPath $pluginsDir -Force; `
                Move-Item -Path "$pluginsDir\plugins\netfx\CredentialProvider.Microsoft\*" -Destination $pluginsDir -Force; `
                Remove-Item -Path "$pluginsDir\plugins" -Recurse -Force; `
                Remove-Item $credProviderZip

            ENV NUGET_CREDENTIALPROVIDER_SESSIONTOKENCACHE_ENABLED=true
            """ );
    }
}