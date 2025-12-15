# The original of this file is in the PostSharp.Engineering repo.
# You can generate this file using `./Build.ps1 generate-scripts`.

[CmdletBinding(PositionalBinding = $false)]
param(
    [switch]$Interactive, # Opens an interactive PowerShell session
    [switch]$StartVsmon, # Enable the remote debugger.
    [Parameter(ValueFromRemainingArguments)]
    [string[]]$BuildArgs   # Arguments passed to `Build.ps1` within the container.
)

####
# These settings are replaced by the generate-scripts command.
$EngPath = 'eng'
$ProductName = 'PostSharpEngineering'
####

if ($StartVsmon)
{
    $vsmonport = 4024
    Write-Host "Starting Visual Studio Remote Debugger, listening at port $vsmonport." -ForegroundColor Cyan
    $vsmonProcess = Start-Process -FilePath "C:\msvsmon\msvsmon.exe" `
        -ArgumentList "/noauth","/anyuser","/silent","/port:$vsmonport","/timeout:2147483647" `
        -NoNewWindow -PassThru
}

# Change the prompt and window title in Docker.
if ($env:RUNNING_IN_DOCKER)
{
    function global:prompt
    {
        $host.UI.RawUI.WindowTitle = "[docker] " + (Get-Location).Path
        "[docker] $( Get-Location )> "
    }
}


if (-not $Interactive -or $BuildArgs)
{
    # Change the working directory so we can use a global.json that is specific to eng.
    $previousLocation = Get-Location

    Set-Location (Join-Path $PSScriptRoot $EngPath "src")

    try
    {
        # Build caching: check if we need to rebuild
        $projectPath = Join-Path $PSScriptRoot $EngPath "src" "Build$ProductName.csproj"

        # Find the output DLL by looking for the first DLL in bin/Debug/*/
        $binDebugPath = Join-Path $PSScriptRoot $EngPath "src" "bin" "Debug"
        $outputDll = $null
        if (Test-Path $binDebugPath)
        {
            $outputDll = Get-ChildItem -Path $binDebugPath -Filter "Build$ProductName.dll" -Recurse -ErrorAction SilentlyContinue | Select-Object -First 1 | ForEach-Object { $_.FullName }
        }

        $needsBuild = $false

        if (-not $outputDll -or -not (Test-Path $outputDll))
        {
            # DLL doesn't exist, need to build
            $needsBuild = $true
        }
        else
        {
            # Get the DLL's last write time
            $dllTime = (Get-Item $outputDll).LastWriteTime

            # Check files in $EngPath/src (non-recursive)
            $engSrcFiles = Get-ChildItem -Path (Join-Path $PSScriptRoot $EngPath "src") -File -ErrorAction SilentlyContinue

            # Check files in $EngPath (non-recursive)
            $engFiles = Get-ChildItem -Path (Join-Path $PSScriptRoot $EngPath) -File -ErrorAction SilentlyContinue

            # Check files in $ScriptRoot (non-recursive)
            $rootFiles = Get-ChildItem -Path $PSScriptRoot -File -ErrorAction SilentlyContinue

            # Combine all files and check if any are newer than the DLL
            $allFiles = @($engSrcFiles) + @($engFiles) + @($rootFiles)
            foreach ($file in $allFiles)
            {
                if ($file.LastWriteTime -gt $dllTime)
                {
                    $needsBuild = $true
                    break
                }
            }
        }

        if ($needsBuild)
        {
            # Build is needed
            Write-Host "Build cache invalid, rebuilding..." -ForegroundColor Cyan
            & dotnet build $projectPath
            if ($LASTEXITCODE -ne 0)
            {
                throw "Build failed with exit code $LASTEXITCODE"
            }

            # Update the DLL timestamp to mark the cache as valid
            if ($outputDll -and (Test-Path $outputDll))
            {
                (Get-Item $outputDll).LastWriteTime = Get-Date
            }
        }
        else
        {
            Write-Host "Build cache valid, skipping build." -ForegroundColor Green
        }

        # Run the project using dotnet exec (faster than dotnet run)
        if (-not $outputDll -or -not (Test-Path $outputDll))
        {
            throw "Output DLL not found at: $outputDll"
        }

        & dotnet exec $outputDll $BuildArgs

        if ($StartVsmon)
        {
            Write-Host ""
            Write-Host "Killing vsmon.exe."
            $vsmonProcess.Kill()
        }
    }
    finally
    {
        Set-Location $previousLocation
    }
}

if ( $Interactive ) {
    Write-Host "Entering interactive PowerShell." -ForegroundColor Green
}
