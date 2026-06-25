// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PostSharp.Engineering.BuildTools.Docker;

/// <summary>
/// Installs the Claude CLI. This component is placed after the timestamp so that the latest version
/// is installed whenever the Docker image is rebuilt. Plugin installation is handled by <see cref="ClaudeAddInsComponent"/>.
/// </summary>
public class ClaudeComponent : ContainerComponent
{
    private const string _minNodeVersion = "22.0.0";
    public override string Name => "Install Claude CLI";

    public override ContainerComponentKind Kind => ContainerComponentKind.Claude;

    public override string Layer => ContainerLayers.Claude;

    public override void WriteDockerfile( TextWriter writer, ContainerOperatingSystem operatingSystem )
    {
        // We don't use the native installer because it's very slow to download.
        // At least the NPM version is stored on fast CDNs.

        writer.WriteLine(
            """
            # Set HOME/USERPROFILE so Claude CLI finds credentials during build
            ENV HOME=C:\\Users\\ContainerAdministrator
            ENV USERPROFILE=C:\\Users\\ContainerAdministrator

            # Install Claude CLI and configure using cmd shell to avoid HCS issues with PowerShell
            SHELL ["cmd", "/S", "/C"]
            """ );

        // Build a single multi-line RUN command for all operations
        writer.WriteLine( "RUN C:\\nodejs\\npm.cmd install --global @anthropic-ai/claude-code@latest" );
        writer.Write( "RUN mkdir C:\\Users\\ContainerAdministrator\\.claude" );
        writer.Write( " && echo {\"hasCompletedOnboarding\": true} > C:\\Users\\ContainerAdministrator\\.claude.json" );
        writer.Write( " && echo {\"alwaysThinkingEnabled\": true, \"spinnerTipsEnabled\": false} > C:\\Users\\ContainerAdministrator\\.claude\\settings.json" );
        writer.WriteLine();

        writer.WriteLine(
            """

            # Restore PowerShell shell using full path
            SHELL ["C:\\Windows\\System32\\WindowsPowerShell\\v1.0\\powershell.exe", "-Command"]
            """ );
    }

    public override void AddRequirements( IReadOnlyList<ContainerComponent> components, Action<ContainerComponent> add )
    {
        base.AddRequirements( components, add );

        var existingNodeJs = components.OfType<NodeJsComponent>().FirstOrDefault();

        if ( existingNodeJs == null )
        {
            // Node.js is required by Claude only (the product did not add it), so install it on the dedicated
            // claude-pre layer - a child of the build image. CI builds the build leaf and thus never builds Node.js;
            // only the Claude dev image (downstream of claude-pre) picks it up.
            add( new NodeJsComponent( _minNodeVersion, ContainerLayers.ClaudePre ) );
        }
        else if ( Version.Parse( existingNodeJs.Version ) < Version.Parse( _minNodeVersion ) )
        {
            throw new InvalidOperationException( $"Claude CLI requires Node.js >= {_minNodeVersion}, but {existingNodeJs.Version} is configured." );
        }

        // Require timestamp component for cache invalidation so @latest resolves on each daily build.
        if ( !components.OfType<TimestampComponent>().Any() )
        {
            add( new TimestampComponent() );
        }
    }
}