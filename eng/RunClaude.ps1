# The original of this file is in the PostSharp.Engineering repo.
# You can generate this file using `./Build.ps1 generate-scripts`.

param(
    [string]$Prompt,
    [int]$McpPort
)

$ErrorActionPreference = "Stop"

if ($env:RUNNING_IN_DOCKER -ne "true")
{
    Write-Error "This script must be run inside a Docker container. Set RUNNING_IN_DOCKER=true to override."
    exit 1
}

# Configure MCP approval server if port is specified
$mcpConfigArg = ""
if ($McpPort -gt 0) {
    $sseUrl = "http://host.docker.internal:$McpPort/sse"
    Write-Host "Configuring MCP approval server: $sseUrl" -ForegroundColor Cyan

    # Create temporary MCP config file
    $mcpConfigPath = "$env:TEMP\mcp-config.json"
    $mcpConfig = @{
        'mcpServers' = @{
            'host-approval' = @{
                'type' = 'sse'
                'url' = $sseUrl
            }
        }
    }
    $mcpConfig | ConvertTo-Json -Depth 10 | Set-Content $mcpConfigPath -Encoding UTF8
    $mcpConfigArg = "--mcp-config `"$mcpConfigPath`""
    Write-Host "MCP config file created: $mcpConfigPath" -ForegroundColor Green
}

Write-Host "Starting Claude CLI..." -ForegroundColor Green

# Run Claude
if ($Prompt)
{
    Write-Host "Running Claude with prompt: $Prompt" -ForegroundColor Cyan
    $cmd = "claude --dangerously-skip-permissions $mcpConfigArg -p `"$Prompt`""
    Invoke-Expression $cmd
}
else
{
    Write-Host "Running Claude in interactive mode" -ForegroundColor Cyan
    $cmd = "claude --dangerously-skip-permissions $mcpConfigArg"
    Invoke-Expression $cmd
}

exit $LASTEXITCODE
