// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PostSharp.Engineering.BuildTools.Docker;

/// <summary>
/// Installs the Claude CLI. This component is placed before the timestamp for caching.
/// Plugin installation is handled by <see cref="ClaudeAddInsComponent"/> which runs after the timestamp.
/// </summary>
public class ClaudeComponent : ContainerComponent
{
    private const string _minNodeVersion = "22.0.0";

    public override string Name => "Install Claude CLI";

    public override ContainerComponentKind Kind => ContainerComponentKind.Claude;

    public override void WriteDockerfile( TextWriter writer )
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
        writer.WriteLine( "RUN C:\\nodejs\\npm.cmd install --global @anthropic-ai/claude-code@2.1.27" );
        writer.Write( "RUN mkdir C:\\Users\\ContainerAdministrator\\.claude" );
        writer.Write( " && echo {\"hasCompletedOnboarding\": true} > C:\\Users\\ContainerAdministrator\\.claude.json" );
        writer.Write( " && echo {\"alwaysThinkingEnabled\": true} > C:\\Users\\ContainerAdministrator\\.claude\\settings.json" );
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
            // Auto-add NodeJsComponent with minimum required version
            add( new NodeJsComponent( _minNodeVersion ) );
        }
        else if ( Version.Parse( existingNodeJs.Version ) < Version.Parse( _minNodeVersion ) )
        {
            throw new InvalidOperationException( $"Claude CLI requires Node.js >= {_minNodeVersion}, but {existingNodeJs.Version} is configured." );
        }

        // We don't add GitHub CLI because we don't have pass the token anyway.
    }
}