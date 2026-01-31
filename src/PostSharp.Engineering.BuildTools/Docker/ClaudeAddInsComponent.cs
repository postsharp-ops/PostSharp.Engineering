// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PostSharp.Engineering.BuildTools.Docker;

/// <summary>
/// Installs Claude CLI plugins and marketplaces. This component is placed after the timestamp
/// so that plugin updates cause a cache invalidation while the Claude CLI installation remains cached.
/// </summary>
public class ClaudeAddInsComponent : ContainerComponent
{
    public override string Name => "Install Claude CLI Add-ins";

    public override ContainerComponentKind Kind => ContainerComponentKind.ClaudeAddIns;

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
        // Use cmd shell to avoid HCS issues with PowerShell
        writer.WriteLine(
            """
            # Install Claude plugins using cmd shell to avoid HCS issues with PowerShell
            SHELL ["cmd", "/S", "/C"]
            """ );

        writer.Write( "RUN echo Installing Claude plugins" );

        // Add marketplaces if any are specified
        foreach ( var marketplace in this.Marketplaces )
        {
            writer.Write( $" && C:\\npm\\claude plugin marketplace add {marketplace}" );
        }

        // Install plugins from the added marketplaces
        foreach ( var plugin in this.Plugins )
        {
            writer.Write( $" && C:\\npm\\claude plugin install {plugin}" );
        }

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

        // Require Claude CLI to be installed first
        var existingClaude = components.OfType<ClaudeComponent>().FirstOrDefault();

        if ( existingClaude == null )
        {
            add( new ClaudeComponent() );
        }
    }
}
