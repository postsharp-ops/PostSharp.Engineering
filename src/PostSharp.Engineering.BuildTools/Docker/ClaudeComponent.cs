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

        writer.WriteLine(
            """
            RUN $ErrorActionPreference = 'Stop'; `
                $version = '2.1.27'; `
                $url = \"https://storage.googleapis.com/claude-code-dist-86c565f3-f756-42ad-8dfa-d59b1c096819/claude-code-releases/2.1.27/win32-x64/claude.exe\"; `
                New-Item -ItemType Directory -Path \"$env:USERPROFILE\.local\bin\" -Force | Out-Null; `
                echo \"Downloading Claude CLI from $url...\"; `
                Invoke-WebRequest -Uri $url -OutFile \"$env:USERPROFILE\.local\bin\claude.exe\"; `
                echo 'Claude CLI $version installed.'; `
                echo \"Configuring Claude CLI...\"; `
                if (Test-Path $claudeJsonPath) { `
                    $claudeConfig = Get-Content $claudeJsonPath -Raw | ConvertFrom-Json; `
                    $claudeConfig | Add-Member -NotePropertyName 'hasCompletedOnboarding' -NotePropertyValue $true -Force; `
                    $claudeConfig | ConvertTo-Json -Depth 10 | Set-Content $claudeJsonPath; `
                } else { `
                    '{"hasCompletedOnboarding": true}' | Set-Content $claudeJsonPath; `
                }; `
                echo 'Configuring Claude CLI plugins.';`
            
            """ );

        foreach ( var marketplace in this.Marketplaces )
        {
            writer.WriteLine( $"    claude plugin marketplace add {marketplace}; `" );
        }

        // Install plugins from the added marketplaces
        foreach ( var plugin in this.Plugins )
        {
            writer.WriteLine( $"    claude plugin install {plugin}; `" );
        }

        writer.WriteLine( $"    echo \"Claude $version installed\"" );
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
    }
}