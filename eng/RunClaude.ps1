# The original of this file is in the PostSharp.Engineering repo.
# You can generate this file using `./Build.ps1 generate-scripts`.

param(
    [string]$Prompt
)

$ErrorActionPreference = "Stop"

if ($env:RUNNING_IN_DOCKER -ne "true")
{
    Write-Error "This script must be run inside a Docker container. Set RUNNING_IN_DOCKER=true to override."
    exit 1
}

Write-Host "Starting Claude CLI..." -ForegroundColor Green

# Run Claude
if ($Prompt)
{
    Write-Host "Running Claude with prompt: $Prompt" -ForegroundColor Cyan
    claude --dangerously-skip-permissions -p $Prompt
}
else
{
    Write-Host "Running Claude in interactive mode" -ForegroundColor Cyan
    claude --dangerously-skip-permissions
}

exit $LASTEXITCODE
