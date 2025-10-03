param(
    [switch]$Inner,
    [switch]$Force
)

$ErrorActionPreference = 'Stop'

$scriptDir = $PSScriptRoot
$lastExecutionFile = Join-Path $scriptDir "last-execution.txt"
$executionLogFile = Join-Path $scriptDir "Daily-Maintenance.log"

# If -Inner is not specified, call this script with -Inner and use Tee-Object for logging
if (-not $Inner) {
    $scriptArgs = "-Inner"
    if ($Force) { $scriptArgs += " -Force" }
    Write-Host "Calling $PSCommandPath $scriptArgs for logging."
    Invoke-Expression "& '$PSCommandPath' $scriptArgs 2>&1" | Tee-Object -FilePath $executionLogFile -Append
    exit $LASTEXITCODE
}

Set-Location $scriptDir

$timestamp = Get-Date -Format "yyyy-MM-dd HH:mm:ss"
Write-Host "[$timestamp] === Daily Maintenance Script Started ==="


# Check if the script has been executed in the last 24 hours
$shouldExecute = $true
if (-not $Force -and (Test-Path $lastExecutionFile)) {
    $lastExecutionTime = (Get-Item $lastExecutionFile).LastWriteTime
    $timeSinceLastExecution = (Get-Date) - $lastExecutionTime
    
    if ($timeSinceLastExecution.TotalHours -lt 24) {
        Write-Host "Daily maintenance was already executed $([math]::Round($timeSinceLastExecution.TotalHours, 2)) hours ago. Skipping execution. Use -Force to execute the script anyway."
        Write-Host "Use -Force parameter to override this behavior."
        $shouldExecute = $false
    }
}

if (-not $shouldExecute) {
    exit 0
}

Write-Host "Executing daily maintenance..."

# Update the last execution timestamp by touching the file
"Last maintenance executed at $(Get-Date)" | Out-File $lastExecutionFile -Encoding UTF8

# Progressive Docker image pruning based on available disk space
$minFreeSpaceGB = 25
$daysToTry = 7  # Start with 7 days
$minDays = 1    # Don't go below 1 day

Write-Host "Starting progressive Docker image pruning..." -ForegroundColor Cyan

do {
    $hoursFilter = $daysToTry * 24
    Write-Host "Pruning Docker images unused for $daysToTry days (${hoursFilter}h)..."
    docker image prune -a --filter "until=${hoursFilter}h" --force
    
    # Check available free space on C: drive
    $drive = Get-PSDrive -Name C
    $freeSpaceGB = [math]::Round($drive.Free / 1GB, 2)
    Write-Host "Free space on C: drive: $freeSpaceGB GB"
    
    if ($freeSpaceGB -ge $minFreeSpaceGB) {
        Write-Host "Sufficient free space available ($freeSpaceGB GB >= $minFreeSpaceGB GB). Docker pruning completed." -ForegroundColor Green
        break
    }
    
    if ($daysToTry -gt $minDays) {
        $daysToTry--
        Write-Host "Insufficient free space ($freeSpaceGB GB < $minFreeSpaceGB GB). Trying more aggressive pruning..."  -ForegroundColor Yellow
    } else {
        Write-Host "Reached minimum pruning threshold (1 day). Current free space: $freeSpaceGB GB"  -ForegroundColor Red
        break
    }
    
} while ($daysToTry -ge $minDays)

# Remove PostSharp and Metalama from the NuGet package cache
# We assume that TeamCity runs as SYSTEM.
Write-Host "Removing PostSharp and Metalama from the NuGet cache..." -ForegroundColor Cyan
$packageDir = "C:\Windows\system32\config\systemprofile\.nuget\packages"
Remove-Item $packageDir/Metalama* -Force -Recurse -ErrorAction SilentlyContinue
Remove-Item $packageDir/PostSharp* -Force -Recurse -ErrorAction SilentlyContinue

# Pull this repo.
Write-Host "Pulling scripts from Git..." -ForegroundColor Cyan
git pull

Write-Host "Daily maintenance completed successfully."  -ForegroundColor Green
