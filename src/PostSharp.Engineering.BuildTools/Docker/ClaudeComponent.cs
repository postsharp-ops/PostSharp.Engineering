// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PostSharp.Engineering.BuildTools.Docker;

public class ClaudeComponent : ContainerComponent
{
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
            ENV HOME=C:\Users\ContainerAdministrator
            ENV USERPROFILE=C:\Users\ContainerAdministrator
            ENV CLAUDE_CODE_SHELL=pwsh
            ENV PATH=$PATH;C:\Users\ContainerAdministrator\.local\bin

            """ );

        // Build a single multi-line RUN command for all operations
        writer.WriteLine(
                """
                RUN irm https://claude.ai/install.ps1 | iex; `
                    $claudeJsonPath = 'C:\Users\ContainerAdministrator\.claude.json'; `
                    if (Test-Path $claudeJsonPath) { `
                        $claudeConfig = Get-Content $claudeJsonPath -Raw | ConvertFrom-Json; `
                        $claudeConfig | Add-Member -NotePropertyName 'hasCompletedOnboarding' -NotePropertyValue $true -Force; `
                        $claudeConfig | ConvertTo-Json -Depth 10 | Set-Content $claudeJsonPath; `
                    } else { `
                        '{"hasCompletedOnboarding": true}' | Set-Content $claudeJsonPath; `
                    }; `
                """ );
        // Add marketplaces if any are specified
        foreach ( var marketplace in this.Marketplaces )
        {
            writer.WriteLine( $"    claude plugin marketplace add {marketplace}; `" );
        }

        // Install plugins from the added marketplaces
        foreach ( var plugin in this.Plugins )
        {
            writer.WriteLine( $"    claude plugin install {plugin}; `" );
        }

        writer.WriteLine("  echo 'Claude CLI installation completed.'");
        writer.WriteLine();
    }

    public override void AddRequirements( IReadOnlyList<ContainerComponent> components, Action<ContainerComponent> add )
    {
        base.AddRequirements( components, add );

        // Auto-add GitHubCliComponent if not already present
        if ( !components.OfType<GitHubCliComponent>().Any() )
        {
            add( new GitHubCliComponent() );
        }

        // Auto-add TimestampComponent for cache invalidation
        if ( !components.OfType<TimestampComponent>().Any() )
        {
            add( new TimestampComponent() );
        }
    }
}