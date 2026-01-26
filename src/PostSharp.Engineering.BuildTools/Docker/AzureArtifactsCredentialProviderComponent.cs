// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using JetBrains.Annotations;
using System.IO;

namespace PostSharp.Engineering.BuildTools.Docker;

[PublicAPI]
public sealed class AzureArtifactsCredentialProviderComponent : ContainerComponent
{
    public override string Name => "Install Azure Artifacts Credential Provider";

    public override ContainerComponentKind Kind => ContainerComponentKind.AzureArtifactsCredentialProvider;

    public override void WriteDockerfile( TextWriter writer )
    {
        writer.WriteLine(
            """
            RUN "Invoke-Expression (Invoke-RestMethod -Uri 'https://aka.ms/install-artifacts-credprovider.ps1')"

            ENV NUGET_CREDENTIALPROVIDER_SESSIONTOKENCACHE_ENABLED=true
            """ );
    }
}