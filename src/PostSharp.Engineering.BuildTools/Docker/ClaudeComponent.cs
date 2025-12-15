// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PostSharp.Engineering.BuildTools.Docker;

public class ClaudeComponent : ContainerComponent
{
    private const string _minNodeVersion = "22.0.0";

    public override string Name => "Install Claude CLI";

    public override ContainerComponentKind Kind => ContainerComponentKind.Claude;

    /// <summary>
    /// Gets the list of marketplace URLs to add in the container.
    /// These should be GitHub repository URLs (e.g., https://github.com/org/repo).
    /// Marketplaces are added first, then plugins can be installed from them.
    /// </summary>
    public string[] Marketplaces { get; init; } =
    [
        "https://github.com/metalama/Metalama.AI.Skills",
        "https://github.com/postsharp/PostSharp.Engineering.AISkills"
    ];

    /// <summary>
    /// Gets the list of plugin names to install from the added marketplaces.
    /// </summary>
    public string[] Plugins { get; init; } =
    [
        "metalama",
        "metalama-dev",
        "eng"
    ];

    public override void WriteDockerfile( TextWriter writer )
    {
        writer.WriteLine(
            """
            # Configure npm global directory to avoid Windows container path issues
            ENV NPM_CONFIG_PREFIX=C:\\npm
            ENV PATH="C:\\npm;${PATH}"

            # Install Claude CLI using cmd shell to avoid HCS issues with PowerShell
            SHELL ["cmd", "/S", "/C"]
            RUN C:\\nodejs\\npm.cmd install --global @anthropic-ai/claude-code

            # Set HOME/USERPROFILE so Claude CLI finds credentials during build
            ENV HOME=C:\\Users\\ContainerUser
            ENV USERPROFILE=C:\\Users\\ContainerUser

            # Create Claude config directory (credentials are mounted at runtime)
            RUN mkdir C:\Users\ContainerUser\.claude
            """ );

/*
        // Add marketplaces if any are specified
        foreach ( var marketplace in this.Marketplaces )
        {
            writer.WriteLine( $"RUN C:\\npm\\claude.cmd plugin marketplace add {marketplace}" );
        }

        // Install plugins from the added marketplaces
        foreach ( var plugin in this.Plugins )
        {
            writer.WriteLine( $"RUN C:\\npm\\claude.cmd plugin install {plugin}" );
        }
*/
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
            throw new InvalidOperationException(
                $"Claude CLI requires Node.js >= {_minNodeVersion}, but {existingNodeJs.Version} is configured." );
        }

        // Auto-add GitHubCliComponent if not already present
        if ( !components.OfType<GitHubCliComponent>().Any() )
        {
            add( new GitHubCliComponent() );
        }
    }
}