$ErrorActionPreference = 'Stop'

# Get the script's directory
$scriptDir = $PSScriptRoot
$lastExecutionFile = Join-Path $scriptDir "last-execution.txt"

# Check if the script has been executed in the last 24 hours
$shouldExecute = $true
if (Test-Path $lastExecutionFile) {
    $lastExecutionTime = (Get-Item $lastExecutionFile).LastWriteTime
    $timeSinceLastExecution = (Get-Date) - $lastExecutionTime
    
    if ($timeSinceLastExecution.TotalHours -lt 24) {
        Write-Host "Daily maintenance was already executed $([math]::Round($timeSinceLastExecution.TotalHours, 2)) hours ago. Skipping execution."
        $shouldExecute = $false
    }
}

if (-not $shouldExecute) {
    exit 0
}

Write-Host "Executing daily maintenance..."

# Update the last execution timestamp by touching the file
"Last maintenance executed at $(Get-Date)" | Out-File $lastExecutionFile -Encoding UTF8

# Remove all Docker images that have not been used for 7 days.
docker image prune -a --filter "until=168h" --force

# Pull this repo.
git pull

Write-Host "Daily maintenance completed successfully."
