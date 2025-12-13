# The original of this file is in the PostSharp.Engineering repo.
# You can generate this file using `./Build.ps1 generate-scripts`.

param(
    [string]$Prompt
)

$ErrorActionPreference = "Stop"

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
