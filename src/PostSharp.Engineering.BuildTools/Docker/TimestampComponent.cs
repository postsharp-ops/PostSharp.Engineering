// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using PostSharp.Engineering.BuildTools.Build;
using System;
using System.IO;

namespace PostSharp.Engineering.BuildTools.Docker;

/// <summary>
/// Docker component that invalidates cache layers to force Claude CLI and plugin updates.
/// Copies a timestamp file that changes when -Update is specified in DockerBuild.ps1.
/// </summary>
public sealed class TimestampComponent : ContainerComponent
{
    private const string TimestampFileName = "update.timestamp";

    public override string Name => "Timestamp";

    public override ContainerComponentKind Kind => ContainerComponentKind.Timestamp;

    // The timestamp is the Claude image's daily cache-buster, so it lives on the Claude layer: a daily change
    // must rebuild only the (cheap) Claude npm layer, not the heavy build and Visual Studio base images.
    public override string Layer => ContainerLayers.Claude;

    public override void WriteDockerfile( TextWriter writer, ContainerOperatingSystem operatingSystem )
    {
        writer.WriteLine(
            $$"""
            # Cache invalidation layer - changes when -Update is used
            COPY .g/{{TimestampFileName}} C:\docker-context\{{TimestampFileName}}
            RUN Write-Host "PostSharp.Engineering build timestamp: $(Get-Content C:\docker-context\{{TimestampFileName}})"
            """ );
    }
}
