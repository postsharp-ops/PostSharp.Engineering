# The original of this file is in the PostSharp.Engineering repo.
# You can generate this file using `./Build.ps1 generate-scripts`.

[CmdletBinding(PositionalBinding = $false)]
param(
    [switch]$Interactive, # Opens an interactive PowerShell session
    [switch]$BuildImage, # Only builds the image, but does not build the product.
    [switch]$NoBuildImage, # Does not build the image.
    [switch]$NoClean, # Does not clean up.
    [switch]$NoNuGetCache, # Does not mount the host nuget cache in the container.
    [switch]$KeepEnv, # Does not override the env.g.json file.
    [switch]$Claude, # Run Claude CLI instead of Build.ps1. Use -Claude for interactive, -Claude "prompt" for non-interactive.
    [string]$ImageName, # Image name (defaults to a name based on the directory).
    [string]$BuildAgentPath = 'C:\BuildAgent',
    [switch]$LoadEnvFromKeyVault, # Forces loading environment variables form the key vault.
    [switch]$StartVsmon, # Enable the remote debugger.
    [string]$Script = 'Build.ps1', # The build script to be executed inside Docker.
    [Parameter(ValueFromRemainingArguments)]
    [string[]]$BuildArgs   # Arguments passed to `Build.ps1` within the container (or Claude prompt if -Claude is specified).
)

####
# These settings are replaced by the generate-scripts command.
$EngPath = '<ENG_PATH>'
$EnvironmentVariables = '<ENVIRONMENT_VARIABLES>'
####

$ErrorActionPreference = "Stop"
$dockerContextDirectory = "$EngPath/docker-context"

Set-Location $PSScriptRoot

# Function to create secrets JSON file
function New-EnvJson
{
    param(
        [string]$EnvironmentVariableList
    )

    # Parse comma-separated environment variable names
    $envVarNames = $EnvironmentVariableList -split ',' | ForEach-Object { $_.Trim() } | Where-Object { $_ -ne '' }

    # Build hashtable with environment variable values
    $envVariables = @{ }
    foreach ($envVarName in $envVarNames)
    {
        $value = [Environment]::GetEnvironmentVariable($envVarName)
        if (-not [string]::IsNullOrEmpty($value))
        {
            $envVariables[$envVarName] = $value
        }
    }

    # Add secrets from the PostSharpBuildEnv key vault, on our development machines.
    # On CI agents, these environment variables are supposed to be set by the host.
    if ($LoadEnvFromKeyVault -or ($env:IS_POSTSHARP_OWNED -and -not $env:IS_TEAMCITY_AGENT))
    {
        $moduleName = "Az.KeyVault"

        if (-not (Get-Module -ListAvailable -Name $moduleName))
        {
            Write-Error "The required module '$moduleName' is not installed. Please install it with: Install-Module -Name $moduleName"
            exit 1
        }

        Import-Module $moduleName
        foreach ($secret in Get-AzKeyVaultSecret -VaultName "PostSharpBuildEnv")
        {
            $secretWithValue = Get-AzKeyVaultSecret -VaultName "PostSharpBuildEnv" -Name $secret.Name
            $envName = $secretWithValue.Name -Replace "-", "_"
            $envValue = (ConvertFrom-SecureString $secretWithValue.SecretValue -AsPlainText)
            $envVariables[$envName] = $envValue
        }
    }

    # Convert to JSON and save
    $jsonPath = Join-Path $dockerContextDirectory "env.g.json"

    # Write a test JSON file with GUID first
    @{ guid = [System.Guid]::NewGuid().ToString() } | ConvertTo-Json | Set-Content -Path $jsonPath -Encoding UTF8

    # Check if secrets file is tracked by git
    $gitStatus = git status --porcelain $jsonPath 2> $null
    if ($LASTEXITCODE -eq 0 -and -not [string]::IsNullOrWhiteSpace($gitStatus))
    {
        Write-Error "Secrets file '$jsonPath' is tracked by git. Please add it to .gitignore first."
        exit 1
    }

    $envVariables | ConvertTo-Json -Depth 10 | Set-Content -Path $jsonPath -Encoding UTF8
    Write-Host "Created secrets file: $jsonPath" -ForegroundColor Cyan


    return $jsonPath
}

# Function to create Claude-specific env.g.json with filtered/renamed variables
function New-ClaudeEnvJson
{
    $claudeEnv = @{ }

    # CLAUDE_GITHUB_TOKEN -> GITHUB_TOKEN (renamed)
    if ($env:CLAUDE_GITHUB_TOKEN)
    {
        $claudeEnv["GITHUB_TOKEN"] = $env:CLAUDE_GITHUB_TOKEN
    }

    # Preserved variables
    if ($env:ANTHROPIC_API_KEY)
    {
        $claudeEnv["ANTHROPIC_API_KEY"] = $env:ANTHROPIC_API_KEY
    }
    if ($env:IS_POSTSHARP_OWNED)
    {
        $claudeEnv["IS_POSTSHARP_OWNED"] = $env:IS_POSTSHARP_OWNED
    }
    if ($env:IS_TEAMCITY_AGENT)
    {
        $claudeEnv["IS_TEAMCITY_AGENT"] = $env:IS_TEAMCITY_AGENT
    }

    # Git identity - read from host git config if not set in environment
    $gitUserName = $env:GIT_USER_NAME
    $gitUserEmail = $env:GIT_USER_EMAIL
    if (-not $gitUserName)
    {
        $gitUserName = git config --global user.name
    }
    if (-not $gitUserEmail)
    {
        $gitUserEmail = git config --global user.email
    }
    if ($gitUserName)
    {
        $claudeEnv["GIT_USER_NAME"] = $gitUserName
    }
    if ($gitUserEmail)
    {
        $claudeEnv["GIT_USER_EMAIL"] = $gitUserEmail
    }

    # Convert to JSON and save
    $jsonPath = Join-Path $dockerContextDirectory "env.g.json"

    # Write a test JSON file with GUID first
    @{ guid = [System.Guid]::NewGuid().ToString() } | ConvertTo-Json | Set-Content -Path $jsonPath -Encoding UTF8

    # Check if secrets file is tracked by git
    $gitStatus = git status --porcelain $jsonPath 2> $null
    if ($LASTEXITCODE -eq 0 -and -not [string]::IsNullOrWhiteSpace($gitStatus))
    {
        Write-Error "Secrets file '$jsonPath' is tracked by git. Please add it to .gitignore first."
        exit 1
    }

    $claudeEnv | ConvertTo-Json -Depth 10 | Set-Content -Path $jsonPath -Encoding UTF8
    Write-Host "Created Claude secrets file: $jsonPath" -ForegroundColor Cyan

    return $jsonPath
}

if ($env:RUNNING_IN_DOCKER)
{
    Write-Error "Already running in Docker."
    exit 1
}

# Generate ImageName from script directory if not provided
if ( [string]::IsNullOrEmpty($ImageName))
{
    # Get full path without drive name (e.g., "C:\src\Metalama.Compiler" becomes "src\Metalama.Compiler")
    $fullPath = $PSScriptRoot -replace '^[A-Za-z]:\\', ''
    # Sanitize path to valid Docker image name (lowercase alphanumeric and hyphens only)
    $ImageTag = $fullPath.ToLower() -replace '[^a-z0-9\-]', '-' -replace '-+', '-' -replace '^-|-$', ''
    # Ensure it doesn't start with a hyphen and has at least one character
    if ([string]::IsNullOrEmpty($ImageTag) -or $ImageTag -match '^-')
    {
        $ImageTag = "docker-build-image"
    }
    Write-Host "Generated image name from directory: $ImageTag" -ForegroundColor Cyan
}
else
{
    # Generate a hash of the repo directory tagging (4 bytes, 8 hex chars)
    $hashBytes = (New-Object -TypeName System.Security.Cryptography.SHA256Managed).ComputeHash([System.Text.Encoding]::UTF8.GetBytes($PSScriptRoot))
    $directoryHash = [System.BitConverter]::ToString($hashBytes, 0, 4).Replace("-", "").ToLower()
    $ImageTag = "$ImageName`:$directoryHash"
    Write-Host "Image will be tagged as: $ImageTag" -ForegroundColor Cyan
}

# When building locally (as opposed as on the build agent), we must do a complete cleanup because 
# obj files may point to the host filesystem.
if (-not $env:IS_TEAMCITY_AGENT -and -not $NoClean)
{
    Write-Host "Cleaning up." -ForegroundColor Green
    if (Test-Path "artifacts")
    {
        Remove-Item artifacts -Force -Recurse  -ErrorAction SilentlyContinue
    }
    Get-ChildItem "bin" -Recurse | Remove-Item -Force -Recurse -ErrorAction SilentlyContinue
    Get-ChildItem "obj" -Recurse | Remove-Item -Force -Recurse -ErrorAction SilentlyContinue
}

Write-Host "Preparing context and mounts." -ForegroundColor Green
# Create secrets JSON file.
if (-not $KeepEnv)
{
    if ($Claude)
    {
        # Use Claude-specific environment variables (filtered and renamed)
        New-ClaudeEnvJson
    }
    else
    {
        # Use standard build environment variables
        if (-not $env:ENG_USERNAME)
        {
            $env:ENG_USERNAME = $env:USERNAME
        }

        # Add git identity to environment
        if ($env:IS_TEAMCITY_AGENT)
        {
            # On TeamCity agents, check if the environment variables are set.
            if (-not $env:GIT_USER_EMAIL -or -not $env:GIT_USER_NAME)
            {
                Write-Error "On TeamCity agents, the GIT_USER_EMAIL and GIT_USER_NAME environment variables must be set."
                exit 1
            }
        }
        else
        {
            # On developer machines, use the current git user.
            $env:GIT_USER_EMAIL = git config --global user.email
            $env:GIT_USER_NAME = git config --global user.name
        }

        New-EnvJson -EnvironmentVariableList $EnvironmentVariables
    }
}

# Get the source directory name from $PSScriptRoot
$SourceDirName = $PSScriptRoot

# Start timing the entire process except cleaning
$stopwatch = [System.Diagnostics.Stopwatch]::StartNew()

# Ensure docker context directory exists and contains at least one file
if (-not (Test-Path $dockerContextDirectory))
{
    Write-Error "Docker context directory '$dockerContextDirectory' does not exist."
    exit 1
}


# Prepare volume mappings
$VolumeMappings = @("-v", "${SourceDirName}:${SourceDirName}")
$MountPoints = @($SourceDirName, "c:\packages")
$GitDirectories = @($SourceDirName)

# Define static Git system directory for mapping. This used by Teamcity as an LFS parent repo.
$gitSystemDir = "$BuildAgentPath\system\git"

if (Test-Path $gitSystemDir)
{
    $VolumeMappings += @("-v", "${gitSystemDir}:${gitSystemDir}:ro")
    $MountPoints += $gitSystemDir
}

# Mount the host NuGet cache in the container.
if (-not $NoNuGetCache)
{
    $nugetCacheDir = Join-Path $env:USERPROFILE ".nuget\packages"
    Write-Host "NuGet cache directory: $nugetCacheDir" -ForegroundColor Cyan
    if (-not (Test-Path $nugetCacheDir))
    {
        Write-Host "Creating NuGet cache directory on host: $nugetCacheDir"
        New-Item -ItemType Directory -Force -Path $nugetCacheDir | Out-Null
    }

    $VolumeMappings += @("-v", "${nugetCacheDir}:c:\packages")
}

# Mount VS Remote Debugger
if ($StartVsmon)
{
    if (-not $env:DevEnvDir)
    {
        Write-Host "Environment variable 'DevEnvDir' is not defined." -ForegroundColor Red
        exit 1
    }

    $remoteDebuggerHostDir = "$( $env:DevEnvDir )Remote Debugger\x64"
    if (-not (Test-Path $remoteDebuggerHostDir))
    {
        Write-Host "Directory '$remoteDebuggerHostDir' does not exist." -ForegroundColor Red
        exit 1
    }

    $remoteDebuggerContainerDir = "C:\msvsmon"
    $VolumeMappings += @("-v", "${remoteDebuggerHostDir}:${remoteDebuggerContainerDir}:ro")
    $MountPoints += $remoteDebuggerContainerDir

}

# Discover symbolic links in source-dependencies and add their targets to mount points
$sourceDependenciesDir = Join-Path $SourceDirName "source-dependencies"
if (Test-Path $sourceDependenciesDir)
{
    $symbolicLinks = Get-ChildItem -Path $sourceDependenciesDir -Force | Where-Object { $_.LinkType -eq 'SymbolicLink' }

    foreach ($link in $symbolicLinks)
    {
        $targetPath = $link.Target
        if (-not [string]::IsNullOrEmpty($targetPath) -and (Test-Path $targetPath))
        {
            Write-Host "Found symbolic link '$( $link.Name )' -> '$targetPath'" -ForegroundColor Cyan
            $VolumeMappings += @("-v", "${targetPath}:${targetPath}:ro")
            $MountPoints += $targetPath
            $GitDirectories += $targetPath
        }
        else
        {
            Write-Host "Warning: Symbolic link '$( $link.Name )' target '$targetPath' does not exist or is invalid" -ForegroundColor Yellow
        }
    }

    $sourceDirectories = Get-ChildItem -Path $sourceDependenciesDir -Force | Where-Object { $_.LinkType -eq $null }
    foreach ($sourceDirectory in $sourceDirectories)
    {
        $GitDirectories += $sourceDirectory
    }
}

# Execute auto-generated DockerMounts.g.ps1 script to add more directory mounts.
$dockerMountsScript = Join-Path $EngPath 'DockerMounts.g.ps1'
if (Test-Path $dockerMountsScript)
{
    Write-Host "Importing Docker mount points from $dockerMountsScript" -ForegroundColor Cyan
    . $dockerMountsScript
}

# Handle non-C: drive letters for Docker (Windows containers only have C: by default)
# We mount X:\foo to C:\X\foo in the container, then use subst to create the X: drive
$driveLetters = @{}

function Get-ContainerPath($hostPath)
{
    if ($hostPath -match '^([A-Za-z]):(.*)$')
    {
        $driveLetter = $Matches[1].ToUpper()
        $pathWithoutDrive = $Matches[2]
        if ($driveLetter -ne 'C')
        {
            $driveLetters[$driveLetter] = $true
            return "C:\$driveLetter$pathWithoutDrive"
        }
    }
    return $hostPath
}

# Transform all volume mappings to use container paths
$transformedVolumeMappings = @()
for ($i = 0; $i -lt $VolumeMappings.Count; $i += 2)
{
    $flag = $VolumeMappings[$i]
    $mapping = $VolumeMappings[$i + 1]

    # Parse volume mapping: hostPath:containerPath[:options]
    if ($mapping -match '^([A-Za-z]:\\[^:]*):([A-Za-z]:\\[^:]*)(:.+)?$')
    {
        $hostPath = $Matches[1]
        $containerPath = $Matches[2]
        $options = $Matches[3]
        $newContainerPath = Get-ContainerPath $containerPath
        $transformedVolumeMappings += @($flag, "${hostPath}:${newContainerPath}${options}")
    }
    else
    {
        $transformedVolumeMappings += @($flag, $mapping)
    }
}
$VolumeMappings = $transformedVolumeMappings

# Transform MountPoints, GitDirectories, and SourceDirName for the container
$MountPoints = $MountPoints | ForEach-Object { Get-ContainerPath $_ }
$GitDirectories = $GitDirectories | ForEach-Object { Get-ContainerPath $_ }
$ContainerSourceDir = Get-ContainerPath $SourceDirName

# Add both the unmapped (C:\X\...) and mapped (X:\...) paths to GitDirectories for safe.directory
# Git may resolve paths differently depending on how it's invoked
$expandedGitDirectories = @()
foreach ($dir in $GitDirectories)
{
    $expandedGitDirectories += $dir
    # If path is C:\<letter>\... (unmapped subst path), also add <letter>:\... (mapped path)
    if ($dir -match '^C:\\([A-Za-z])\\(.*)$')
    {
        $letter = $Matches[1].ToUpper()
        $rest = $Matches[2]
        $expandedGitDirectories += "${letter}:\$rest"
    }
}
$GitDirectories = $expandedGitDirectories

# Build subst commands string for inline execution in docker run
$substCommandsInline = ""
foreach ($letter in $driveLetters.Keys | Sort-Object)
{
    $substCommandsInline += "C:\Windows\System32\subst.exe ${letter}: C:\$letter; "
}
if ($driveLetters.Count -gt 0)
{
    Write-Host "Drive letter mappings for container: $($driveLetters.Keys -join ', ')" -ForegroundColor Cyan
}

# Create Init.g.ps1 with git configuration (safe.directory and user identity)
$initScript = Join-Path $dockerContextDirectory "Init.g.ps1"
$initScriptContent = @"
# Auto-generated initialization script for container startup

# Configure git user identity from Machine environment variables
`$gitUserName = [Environment]::GetEnvironmentVariable('GIT_USER_NAME', 'Machine')
`$gitUserEmail = [Environment]::GetEnvironmentVariable('GIT_USER_EMAIL', 'Machine')
if (`$gitUserName) {
    git config --global user.name `$gitUserName
}
if (`$gitUserEmail) {
    git config --global user.email `$gitUserEmail
}

# Configure git safe.directory for all mounted directories
`$gitDirectories = @(
$(($GitDirectories | ForEach-Object { "    '$_'" }) -join ",`n")
)

foreach (`$dir in `$gitDirectories) {
    if (`$dir) {
        `$normalizedDir = (`$dir -replace '\\\\', '/').TrimEnd('/') + '/'
        git config --global --add safe.directory `$normalizedDir
    }
}
"@
$initScriptContent | Set-Content -Path $initScript -Encoding UTF8

$mountPointsAsString = $MountPoints -Join ";"
$gitDirectoriesAsString = $GitDirectories -Join ";"

Write-Host "Volume mappings: " @VolumeMappings -ForegroundColor Gray
Write-Host "Mount points: " $mountPointsAsString -ForegroundColor Gray
Write-Host "Git directories: " $gitDirectoriesAsString -ForegroundColor Gray

# Kill all containers
docker ps -q --filter "ancestor=$ImageTag" | ForEach-Object {
    Write-Host "Killing container $_"
    docker kill $_
}

# Building the image.
if (-not $NoBuildImage)
{
    if ($Claude)
    {
        # Build Claude image directly from standalone Dockerfile.claude
        $ImageTag = "$ImageTag-claude"
        Write-Host "Building the Claude image with tag: $ImageTag" -ForegroundColor Green

        if (-not (Test-Path "Dockerfile.claude"))
        {
            Write-Error "Dockerfile.claude not found. Make sure generate-scripts was run with Claude support."
            exit 1
        }

        Get-Content -Raw Dockerfile.claude | docker build -t $ImageTag --build-arg MOUNTPOINTS="$mountPointsAsString" -f - $dockerContextDirectory
        if ($LASTEXITCODE -ne 0)
        {
            Write-Host "Docker build (Claude) failed with exit code $LASTEXITCODE" -ForegroundColor Red
            exit $LASTEXITCODE
        }
    }
    else
    {
        # Build base image
        Write-Host "Building the base image with tag: $ImageTag" -ForegroundColor Green
        Get-Content -Raw Dockerfile | docker build -t $ImageTag --build-arg MOUNTPOINTS="$mountPointsAsString" -f - $dockerContextDirectory
        if ($LASTEXITCODE -ne 0)
        {
            Write-Host "Docker build failed with exit code $LASTEXITCODE" -ForegroundColor Red
            exit $LASTEXITCODE
        }
    }
}
else
{
    Write-Host "Skipping image build (-NoBuildImage specified)." -ForegroundColor Yellow

    # If Claude mode and skipping build, use the Claude image tag
    if ($Claude)
    {
        $ImageTag = "$ImageTag-claude"
    }
}


# Run the build within the container
if (-not $BuildImage)
{
    if ($Claude)
    {
        # Run Claude mode
        Write-Host "Running Claude in the container." -ForegroundColor Green

        # Add Claude-specific volume mounts for auth and settings
        $hostUserProfile = $env:USERPROFILE
        $containerUserProfile = "C:\Users\ContainerUser"

        # Mount .claude directory (settings and credentials)
        if (Test-Path "$hostUserProfile\.claude")
        {
            $VolumeMappings += @("-v", "${hostUserProfile}\.claude:${containerUserProfile}\.claude")
        }

        # Copy .claude.json to docker-context (cannot mount files on Windows Docker)
        # Also fix installMethod to match container's npm installation
        $claudeJsonSource = "$hostUserProfile\.claude.json"
        $claudeJsonDest = Join-Path $dockerContextDirectory "claude.json"
        $copyClaudeJsonScript = ""
        if (Test-Path $claudeJsonSource)
        {
            $claudeConfig = Get-Content $claudeJsonSource -Raw | ConvertFrom-Json
            # Change installMethod to npm since that's how Claude is installed in container
            if ($claudeConfig.installMethod)
            {
                $claudeConfig.installMethod = "npm"
            }
            $claudeConfig | ConvertTo-Json -Depth 10 | Set-Content $claudeJsonDest -Encoding UTF8
            # Will copy from mounted source dir to user profile in container
            $copyClaudeJsonScript = "Copy-Item '$ContainerSourceDir\eng\docker-context\claude.json' '$containerUserProfile\.claude.json' -Force; "
        }

        # Mount .cache\claude (cache)
        if (Test-Path "$hostUserProfile\.cache\claude")
        {
            $VolumeMappings += @("-v", "${hostUserProfile}\.cache\claude:${containerUserProfile}\.cache\claude")
        }

        $VolumeMappingsAsString = $VolumeMappings -join " "

        # Extract Claude prompt from remaining arguments if present
        # Usage: -Claude for interactive, -Claude "prompt" for non-interactive
        $ClaudePrompt = $null
        if ($BuildArgs -and $BuildArgs.Count -gt 0 -and $BuildArgs[0] -and -not $BuildArgs[0].StartsWith('-'))
        {
            $ClaudePrompt = $BuildArgs[0]
        }

        # Build inline script: subst drives, copy claude.json, cd to source, run Claude
        if ($ClaudePrompt)
        {
            # Non-interactive mode with prompt - no -it flags
            $dockerArgs = @()
            $inlineScript = "${substCommandsInline}& c:\Init.g.ps1; ${copyClaudeJsonScript}cd '$SourceDirName'; & .\eng\RunClaude.g.ps1 -Prompt `"$ClaudePrompt`""
        }
        else
        {
            # Interactive mode - requires TTY
            $dockerArgs = @("-it")
            $inlineScript = "${substCommandsInline}& c:\Init.g.ps1; ${copyClaudeJsonScript}cd '$SourceDirName'; & .\eng\RunClaude.g.ps1"
        }

        $dockerArgsAsString = $dockerArgs -join " "
        $pwshPath = 'C:\Program Files\PowerShell\7\pwsh.exe'

        # Set HOME/USERPROFILE so Claude finds its config in the mounted location
        $envArgs = @("-e", "HOME=$containerUserProfile", "-e", "USERPROFILE=$containerUserProfile")

        Write-Host "Executing: ``docker run --rm --memory=12g $dockerArgsAsString $VolumeMappingsAsString -e HOME=$containerUserProfile -e USERPROFILE=$containerUserProfile -w $ContainerSourceDir $ImageTag `"$pwshPath`" -Command `"$inlineScript`"" -ForegroundColor Cyan
        docker run --rm --memory=12g $dockerArgs @VolumeMappings @envArgs -w $ContainerSourceDir $ImageTag $pwshPath -Command $inlineScript

        if ($LASTEXITCODE -ne 0)
        {
            Write-Host "Docker run (Claude) failed with exit code $LASTEXITCODE" -ForegroundColor Red
            exit $LASTEXITCODE
        }
    }
    else
    {
        # Run standard build mode
        # Delete now and not in the container because it's much faster and lock error messages are more relevant.
        Write-Host "Building the product in the container." -ForegroundColor Green

        # Prepare Build.ps1 arguments
        if ($StartVsmon)
        {
            $BuildArgs = @("-StartVsmon") + $BuildArgs
        }

        if ($Interactive)
        {
            $pwshArgs = "-NoExit"
            $BuildArgs = @("-Interactive") + $BuildArgs
            $dockerArgs = @("-it")
            $pwshExitCommand = ""
        }
        else
        {
            $pwshArgs = "-NonInteractive"
            $dockerArgs = @()
            $pwshExitCommand = "exit `$LASTEXITCODE`;"
        }

        $buildArgsString = $BuildArgs -join " "
        $VolumeMappingsAsString = $VolumeMappings -join " "
        $dockerArgsAsString = $dockerArgs -join " "

        # Build inline script: subst drives, run init, cd to source, run build
        $inlineScript = "${substCommandsInline}& c:\Init.g.ps1; cd '$SourceDirName'; & .\$Script $buildArgsString; $pwshExitCommand"

        $pwshPath = 'C:\Program Files\PowerShell\7\pwsh.exe'
        Write-Host "Executing: ``docker run --rm --memory=12g $dockerArgsAsString $VolumeMappingsAsString -w $ContainerSourceDir $ImageTag `"$pwshPath`" $pwshArgs -Command `"$inlineScript`"" -ForegroundColor Cyan

        docker run --rm --memory=12g $dockerArgs @VolumeMappings -w $ContainerSourceDir $ImageTag $pwshPath $pwshArgs -Command $inlineScript
        if ($LASTEXITCODE -ne 0)
        {
            Write-Host "Docker run (build) failed with exit code $LASTEXITCODE" -ForegroundColor Red
            exit $LASTEXITCODE
        }
    }
}
else
{
    Write-Host "Skipping container run (BuildImage specified)." -ForegroundColor Yellow
}

# Stop timing and display results
$elapsed = $stopwatch.Elapsed
Write-Host ""
Write-Host "Total build time: $($elapsed.ToString('hh\:mm\:ss\.fff') )" -ForegroundColor Cyan
Write-Host "Build completed at: $( Get-Date -Format 'yyyy-MM-dd HH:mm:ss' )" -ForegroundColor Cyan
