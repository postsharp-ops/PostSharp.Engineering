# StartMcp.ps1 - Builds and runs the MCP Approval Server from a temp folder to avoid file locks

param(
    [switch]$NoBuild,
    [switch]$Debug
)

$ErrorActionPreference = "Stop"

Push-Location $PSScriptRoot

$projectPath = "$PSScriptRoot\src\PostSharp.Engineering.McpApprovalServer\PostSharp.Engineering.McpApprovalServer.csproj"
$outputPath = "$PSScriptRoot\src\PostSharp.Engineering.McpApprovalServer\bin\Debug\net8.0-windows"
$tempPath = "$env:LOCALAPPDATA\PostSharp\McpApprovalServer\bin"
$exeName = "PostSharp.Engineering.McpApprovalServer.exe"

# Kill any running instance
$existingProcess = Get-Process -Name "PostSharp.Engineering.McpApprovalServer" -ErrorAction SilentlyContinue
if ($existingProcess) {
    Write-Host "Stopping existing MCP server process..." -ForegroundColor Yellow
    $existingProcess | Stop-Process -Force
    Start-Sleep -Milliseconds 500
}

# Build unless -NoBuild is specified
if (-not $NoBuild) {
    Write-Host "Building MCP Approval Server..." -ForegroundColor Cyan
    $buildArgs = @("build", $projectPath, "-c", "Debug")
    if (-not $Debug) {
        $buildArgs += "-v:q"
    }
    & dotnet @buildArgs
    if ($LASTEXITCODE -ne 0) {
        Write-Host "Build failed!" -ForegroundColor Red
        exit 1
    }
    Write-Host "Build succeeded." -ForegroundColor Green
}

# Ensure temp directory exists
if (-not (Test-Path $tempPath)) {
    New-Item -ItemType Directory -Path $tempPath -Force | Out-Null
}

# Copy all files to temp folder
Write-Host "Copying to $tempPath..." -ForegroundColor Cyan
Copy-Item -Path "$outputPath\*" -Destination $tempPath -Recurse -Force

# Start the server
$exePath = Join-Path $tempPath $exeName
Write-Host "Starting MCP server from $exePath..." -ForegroundColor Cyan
Start-Process -FilePath $exePath

Write-Host "MCP Approval Server started." -ForegroundColor Green
Write-Host "Logs: $env:LOCALAPPDATA\PostSharp\McpApprovalServer\audit\" -ForegroundColor Gray

Pop-Location
