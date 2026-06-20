// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

namespace PostSharp.Engineering.BuildTools.Docker;

public record DockerSpec( string ImageName, int? Memory = null, string? Dockerfile = null )
{
    // Dockerfile names are prefix-free (just the layer); the image tag carries the prefix. The main chain's
    // Claude leaf is docker/claude.Dockerfile.
    public DockerSpec WithClaudeDockerfile( string engineeringDirectory ) => this with { Dockerfile = $"{engineeringDirectory}/docker/claude.Dockerfile" };
}