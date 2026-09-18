<#
.SYNOPSIS
    Integration test suite for the chained-Dockerfile resolver in DockerBuild.ps1.

.DESCRIPTION
    Exercises the chain resolver in the generated DockerBuild.ps1 against tiny, fast, chained fixture images
    (see fixtures/). The suite is self-contained: it fills the DockerBuild.ps1 template (from
    src/PostSharp.Engineering.BuildTools/Resources) with a test image prefix, stages the fixtures into a
    temporary eng/-shaped sandbox, and drives `DockerBuild.ps1 -BuildImage` while asserting on Docker state.

    Requires Docker. Auto-detects Windows- vs Linux-container mode and uses the matching fixtures
    (nanoserver / alpine). Cases that need a Linux-only registry (registry:2) are skipped in Windows-container
    mode; cases that need `docker run` of a PowerShell-7 capable image (runtime env/init, boot-image mount
    creation) are out of scope here - they are covered by real product builds, not by the lightweight fixtures.

    This suite is NOT part of `dotnet test` (it needs Docker). Run it explicitly:
        pwsh tests/dockerbuild/Run-DockerBuildTests.ps1

.PARAMETER KeepSandbox
    Do not delete the temporary sandbox/images on exit (for debugging).

.PARAMETER IncludeImageEviction
    Also exercise the path of the image-space cleanup that actually removes images. This removes unused
    Docker images from this machine, including images that have nothing to do with these tests. Run it on a
    build agent, not on a workstation. Without it, only the measuring and reporting of the cleanup is tested.
#>
[CmdletBinding()]
param(
    [switch]$KeepSandbox,
    [switch]$IncludeImageEviction
)

$ErrorActionPreference = 'Stop'

# ---- locations ------------------------------------------------------------------------------------------
$testRoot = $PSScriptRoot
$repoRoot = (Resolve-Path (Join-Path $testRoot '..' '..')).Path
$templatePath = Join-Path $repoRoot 'src/PostSharp.Engineering.BuildTools/Resources/DockerBuild.ps1'
$imagePrefix = 'dbtest'

if (-not (Test-Path $templatePath))
{
    Write-Error "DockerBuild.ps1 template not found at '$templatePath'."
    exit 1
}

# ---- docker availability + container OS ------------------------------------------------------------------
$dockerOs = $null
try { $dockerOs = (docker version --format '{{.Server.Os}}' 2>$null) } catch { }
if (-not $dockerOs)
{
    Write-Error "Docker is not available. This integration suite requires a running Docker engine."
    exit 1
}
Write-Host "Docker container OS: $dockerOs" -ForegroundColor Cyan
$fixtureSet = if ($dockerOs -eq 'windows') { 'windows' } else { 'linux' }
$fixturesDir = Join-Path $testRoot "fixtures/$fixtureSet"

# Command used to print a file from inside a built image (to verify per-image context).
$catCtxCmd = if ($dockerOs -eq 'windows') { @('cmd', '/c', 'type', 'C:\ctx.txt') } else { @('cat', '/ctx.txt') }

# ---- result tracking ------------------------------------------------------------------------------------
$script:failures = @()
$script:passes = 0
$script:skips = 0

function Test-Case([string]$name, [bool]$condition, [string]$detail = '')
{
    if ($condition)
    {
        Write-Host "  [PASS] $name" -ForegroundColor Green
        $script:passes++
    }
    else
    {
        Write-Host "  [FAIL] $name $detail" -ForegroundColor Red
        $script:failures += $name
    }
}

function Skip-Case([string]$name, [string]$reason)
{
    Write-Host "  [SKIP] $name - $reason" -ForegroundColor Yellow
    $script:skips++
}

# ---- docker helpers -------------------------------------------------------------------------------------
# Returns the set of "tag=id" strings for an image repository (one per existing tag).
function Get-ImageTags([string]$repo)
{
    $lines = docker images $repo --format '{{.Repository}}:{{.Tag}}={{.ID}}' 2>$null
    return @($lines | Where-Object { $_ -and $_.Trim() -ne '' })
}

function Test-ImageExists([string]$repo)
{
    return (Get-ImageTags $repo).Count -gt 0
}

# ---- sandbox staging ------------------------------------------------------------------------------------
$sandbox = Join-Path ([System.IO.Path]::GetTempPath()) "dockerbuild-tests-$([System.Guid]::NewGuid().ToString('N').Substring(0,8))"
Write-Host "Sandbox: $sandbox" -ForegroundColor Cyan

function Initialize-Sandbox
{
    if (Test-Path $sandbox) { Remove-Item $sandbox -Recurse -Force }
    New-Item -ItemType Directory -Path $sandbox -Force | Out-Null
    New-Item -ItemType Directory -Path (Join-Path $sandbox 'eng/docker') -Force | Out-Null
    New-Item -ItemType Directory -Path (Join-Path $sandbox 'eng/docker-context') -Force | Out-Null
    New-Item -ItemType Directory -Path (Join-Path $sandbox 'eng/.g') -Force | Out-Null

    # Fill the DockerBuild.ps1 template with test values.
    $script = (Get-Content $templatePath -Raw).
        Replace('<ENG_PATH>', 'eng').
        Replace('<ENVIRONMENT_VARIABLES>', '').
        Replace('<DOCKER_IMAGE_PREFIX>', $imagePrefix)
    Set-Content -Path (Join-Path $sandbox 'DockerBuild.ps1') -Value $script -Encoding UTF8

    # DockerBuild.ps1 requires DockerMounts.g.ps1 to exist (it is dot-sourced for extra mounts).
    Set-Content -Path (Join-Path $sandbox 'eng/DockerMounts.g.ps1') -Value '# (no extra mounts for tests)' -Encoding UTF8

    # Stage fixtures: Dockerfiles and per-image contexts.
    Copy-Item (Join-Path $fixturesDir 'docker/*') (Join-Path $sandbox 'eng/docker') -Recurse -Force
    Copy-Item (Join-Path $fixturesDir 'docker-context/*') (Join-Path $sandbox 'eng/docker-context') -Recurse -Force
}

# Runs the sandbox DockerBuild.ps1 with the given extra args. Returns @{ ExitCode; Output }.
# The directory in which DockerBuild.ps1 records image usage for the LRU eviction order. Resolved exactly the
# way the script resolves it, so that a change to one side fails these cases rather than passing silently.
function Get-UsageDirectory
{
    if ($IsWindows)
    {
        return (Join-Path $env:LOCALAPPDATA 'PostSharp.Engineering\docker-image-usage')
    }

    return (Join-Path $env:HOME '.local/share/PostSharp.Engineering/docker-image-usage')
}

# The path of the usage record of an image repository, or $null when there is none. The repository is resolved
# to an image identifier first, because the records are keyed by identifier and not by tag.
function Get-UsageRecord([string]$repo)
{
    $id = @(docker images $repo --no-trunc --format '{{.ID}}' 2>$null | Where-Object { $_ -and $_.Trim() -ne '' })[0]
    if (-not $id)
    {
        return $null
    }

    $path = Join-Path (Get-UsageDirectory) ("$id".Trim() -replace '^sha256:', '')
    if (Test-Path $path)
    {
        return $path
    }

    return $null
}

function Invoke-DockerBuild([string[]]$Arguments)
{
    $sandboxScript = Join-Path $sandbox 'DockerBuild.ps1'
    $allArgs = @('-NoProfile', '-File', $sandboxScript, '-BuildImage', '-NoNuGetCache', '-NoMcp') + $Arguments
    Push-Location $sandbox
    try
    {
        $output = & pwsh @allArgs 2>&1 | Out-String
        $exit = $LASTEXITCODE
    }
    finally { Pop-Location }
    return @{ ExitCode = $exit; Output = $output }
}

function Remove-TestImages
{
    # Match the test images whether tagged locally ("dbtest-...") or with a registry prefix
    # ("<registry>/dbtest-..."), so the registry case can prove a real pull after a local rmi.
    # Remove by reference and loop: a parent image cannot be removed while a child still references it, so
    # each pass untags one chain level (leaf -> root) until everything is gone (chain is <= 3 deep).
    for ($pass = 0; $pass -lt 5; $pass++)
    {
        $refs = docker images --format '{{.Repository}}:{{.Tag}}' 2>$null |
            Where-Object { $_ -match "(^|/)$imagePrefix-" }
        if (-not $refs) { break }
        foreach ($ref in $refs) { docker rmi -f $ref *> $null }
    }
}

# ---- run --------------------------------------------------------------------------------------------------
# The size, in bytes, that `docker system df` reports for the image store; 0 when it cannot be read.
# The suite measures the store the same way DockerBuild.ps1 does, so that a budget can be derived from it.
function Get-TestImageStoreSize
{
    foreach ($row in @(docker system df --format '{{.Type}}|{{.Size}}' 2>$null))
    {
        $fields = "$row" -split '\|', 2
        if ($fields.Count -eq 2 -and $fields[0].Trim() -eq 'Images' -and $fields[1] -match '^\s*(\d+(?:\.\d+)?)\s*([kKmMgGtTpP]?)(i?)B\s*$')
        {
            $exponent = switch ($Matches[2].ToUpperInvariant()) { 'K' { 1 } 'M' { 2 } 'G' { 3 } 'T' { 4 } 'P' { 5 } default { 0 } }
            return [long]([double]$Matches[1] * [Math]::Pow($( if ($Matches[3]) { 1024 } else { 1000 } ), $exponent))
        }
    }
    return [long]0
}

# Avoid the key-vault path and registry mode for the local-build cases.
$savedPostSharpOwned = $env:IS_POSTSHARP_OWNED
$savedRegistry = $env:DOCKER_REGISTRY
$env:IS_POSTSHARP_OWNED = ''
$env:DOCKER_REGISTRY = ''

# A machine-wide DOCKER_MAX_IMAGE_SPACE must not decide what these tests measure: every case that cares
# passes -MaxImageSpace explicitly, and the cases that do not must see the built-in default.
$savedMaxImageSpace = $env:DOCKER_MAX_IMAGE_SPACE
$env:DOCKER_MAX_IMAGE_SPACE = ''

try
{
    Initialize-Sandbox
    Remove-TestImages

    # === Case 10 (part 1) + Case 1: default selection builds the BUILD leaf + its ancestor, not the claude leaf.
    Write-Host "`n== Build leaf (default selection) ==" -ForegroundColor Magenta
    $r = Invoke-DockerBuild @()
    if ($r.ExitCode -ne 0) { Write-Host $r.Output }
    Test-Case "build leaf: DockerBuild.ps1 exits 0" ($r.ExitCode -eq 0)
    Test-Case "build leaf: root image '$imagePrefix-vs' built (parent-first)" (Test-ImageExists "$imagePrefix-vs")
    Test-Case "build leaf: '$imagePrefix-build' built" (Test-ImageExists "$imagePrefix-build")
    Test-Case "leaf selection: '$imagePrefix-claude' NOT built without -Claude" (-not (Test-ImageExists "$imagePrefix-claude"))

    # === Image-usage records: the last-used signal the cleanup evicts by. ===
    # The Docker engine exposes no last-used time, so the script records one itself. Without these records the
    # eviction silently falls back to the creation date, which is what it used to do - and what removed the
    # stable base images that every build reuses.
    Write-Host "`n== Image-usage records ==" -ForegroundColor Magenta

    $leafRecord = Get-UsageRecord "$imagePrefix-build"
    Test-Case "usage: the built leaf has a record" ($null -ne $leafRecord)
    Test-Case "usage: the chain root has a record" ($null -ne (Get-UsageRecord "$imagePrefix-vs"))

    if ($leafRecord)
    {
        $recorded = @(Get-Content $leafRecord)
        $parsed = [datetime]::MinValue
        Test-Case "usage: the record opens with a round-trip UTC timestamp" (
                $recorded.Count -ge 1 -and
                        [datetime]::TryParse($recorded[0], [cultureinfo]::InvariantCulture, [Globalization.DateTimeStyles]::RoundtripKind, [ref]$parsed))
        Test-Case "usage: the timestamp is recent" ($parsed -gt (Get-Date).ToUniversalTime().AddMinutes(-30))
        Test-Case "usage: the record names the image it belongs to" (
                (@($recorded | Select-Object -Skip 1) -join ' ') -match [regex]::Escape("$imagePrefix-build"))
    }

    Skip-Case "usage: the base OS image has a record" "the fixture roots declare no 'ARG OS_IMAGE'/'ARG OS_IMAGE_REPOSITORY', so Get-OsImageSpec returns nothing and the keep set - which is what gets recorded - holds no OS image; pinned instead by DockerImageLruTests.TheBaseOsImage_IsRecordedAsUsed and exercised by real product builds"

    # Recording is not part of the cleanup: a machine that disabled the budget must still leave records behind,
    # or it would never accumulate the history a later run with a budget needs.
    $usageBefore = @(Get-ChildItem (Get-UsageDirectory) -File -ErrorAction SilentlyContinue).Count
    $r = Invoke-DockerBuild @('-MaxImageSpace', '0')
    Test-Case "usage: -MaxImageSpace 0 exits 0" ($r.ExitCode -eq 0)
    Test-Case "usage: -MaxImageSpace 0 still records the images it used" (
            $usageBefore -gt 0 -and @(Get-ChildItem (Get-UsageDirectory) -File -ErrorAction SilentlyContinue).Count -ge $usageBefore)

    # === Case 2: content-hash caching - a second identical build is a no-op (image IDs unchanged).
    Write-Host "`n== Caching (rebuild is a no-op) ==" -ForegroundColor Magenta
    $vsBefore = Get-ImageTags "$imagePrefix-vs"
    $buildBefore = Get-ImageTags "$imagePrefix-build"
    $r = Invoke-DockerBuild @()
    Test-Case "cache: rebuild exits 0" ($r.ExitCode -eq 0)
    Test-Case "cache: '$imagePrefix-vs' tags/ids unchanged" (((Get-ImageTags "$imagePrefix-vs") -join '|') -eq ($vsBefore -join '|'))
    Test-Case "cache: '$imagePrefix-build' tags/ids unchanged" (((Get-ImageTags "$imagePrefix-build") -join '|') -eq ($buildBefore -join '|'))

    # === Case 4: line-ending invariance - rewriting a Dockerfile CRLF<->LF must not change the tag.
    Write-Host "`n== Line-ending invariance ==" -ForegroundColor Magenta
    $buildDf = Join-Path $sandbox 'eng/docker/build.Dockerfile'
    $tagsBefore = Get-ImageTags "$imagePrefix-build"
    $body = Get-Content $buildDf -Raw
    $crlf = ($body -replace "`r`n", "`n") -replace "`n", "`r`n"   # normalize to CRLF
    Set-Content -Path $buildDf -Value $crlf -NoNewline -Encoding ascii
    $r = Invoke-DockerBuild @()
    Test-Case "line-endings: rebuild exits 0" ($r.ExitCode -eq 0)
    Test-Case "line-endings: '$imagePrefix-build' tag unchanged after CRLF rewrite" (((Get-ImageTags "$imagePrefix-build") -join '|') -eq ($tagsBefore -join '|'))

    # === Case 10 (part 2) + Case 1 (full chain): -Claude builds the claude leaf on top of build + vs.
    Write-Host "`n== Claude leaf (full chain) ==" -ForegroundColor Magenta
    $r = Invoke-DockerBuild @('-Claude')
    if ($r.ExitCode -ne 0) { Write-Host $r.Output }
    Test-Case "claude leaf: exits 0" ($r.ExitCode -eq 0)
    Test-Case "leaf selection: '$imagePrefix-claude' built with -Claude" (Test-ImageExists "$imagePrefix-claude")

    # === Case 7: per-image context isolation - each image carries only its own context sentinel.
    Write-Host "`n== Per-image context isolation ==" -ForegroundColor Magenta
    foreach ($layer in 'vs', 'build', 'claude')
    {
        $tag = ((Get-ImageTags "$imagePrefix-$layer") | Select-Object -First 1) -split '=' | Select-Object -First 1
        if ($tag)
        {
            $content = (& docker run --rm $tag @catCtxCmd 2>&1 | Out-String).Trim()
            Test-Case "context: '$imagePrefix-$layer' has its own sentinel (context-$layer)" ($content -eq "context-$layer") "(got '$content')"
        }
        else
        {
            Test-Case "context: '$imagePrefix-$layer' image present" $false
        }
    }

    # === Case 3: base-hash fold - changing the ROOT Dockerfile changes the tag of the root AND all descendants.
    Write-Host "`n== Base-hash fold (parent change cascades) ==" -ForegroundColor Magenta
    $vsTagsBefore = Get-ImageTags "$imagePrefix-vs"
    $buildTagsBefore = Get-ImageTags "$imagePrefix-build"
    $claudeTagsBefore = Get-ImageTags "$imagePrefix-claude"
    $vsDf = Join-Path $sandbox 'eng/docker/vs.Dockerfile'
    Add-Content -Path $vsDf -Value "`n# cache-busting change for base-hash-fold test"
    $r = Invoke-DockerBuild @('-Claude')
    Test-Case "base-fold: rebuild exits 0" ($r.ExitCode -eq 0)
    $vsTagsAfter = Get-ImageTags "$imagePrefix-vs"
    $buildTagsAfter = Get-ImageTags "$imagePrefix-build"
    $claudeTagsAfter = Get-ImageTags "$imagePrefix-claude"
    Test-Case "base-fold: '$imagePrefix-vs' got a new tag" (($vsTagsAfter | Where-Object { $_ -notin $vsTagsBefore }).Count -gt 0)
    Test-Case "base-fold: '$imagePrefix-build' got a new tag (parent hash folded)" (($buildTagsAfter | Where-Object { $_ -notin $buildTagsBefore }).Count -gt 0)
    Test-Case "base-fold: '$imagePrefix-claude' got a new tag (cascaded)" (($claudeTagsAfter | Where-Object { $_ -notin $claudeTagsBefore }).Count -gt 0)

    # === Case 8: registry push/pull against the CONFIGURED registry ($env:DOCKER_REGISTRY, with optional
    # $env:DOCKER_USERNAME / $env:DOCKER_PASSWORD for auth). We do NOT deploy a throwaway registry. Skipped
    # when no registry is configured. Test images use the 'dbtest-' prefix and tiny content-hash tags.
    Write-Host "`n== Registry push/pull (configured registry) ==" -ForegroundColor Magenta
    if (-not $savedRegistry)
    {
        Skip-Case "registry push/pull" "no `$env:DOCKER_REGISTRY configured"
    }
    else
    {
        $env:DOCKER_REGISTRY = $savedRegistry
        try
        {
            Write-Host "Using configured registry: $savedRegistry" -ForegroundColor Cyan
            Remove-TestImages
            $r = Invoke-DockerBuild @('-Claude')
            if ($r.ExitCode -ne 0) { Write-Host "---- registry build output ----`n$($r.Output)`n----" -ForegroundColor DarkGray }
            Test-Case "registry: build+push exits 0" ($r.ExitCode -eq 0)
            # Drop local images, then rebuild: the ancestor chain (vs/build) must be satisfied by pulling from the
            # registry. The Claude LEAF is never pushed/pulled - it is always rebuilt locally (it bakes a daily
            # cache-buster + `@latest` installs), so after the rebuild it exists locally again because it was built,
            # not pulled.
            Remove-TestImages
            $r2 = Invoke-DockerBuild @('-Claude')
            Test-Case "registry: second build (after local rmi) exits 0" ($r2.ExitCode -eq 0)
            Test-Case "registry: '$imagePrefix-build' available again (pulled from registry)" (Test-ImageExists "$savedRegistry/$imagePrefix-build")
            Test-Case "registry: '$imagePrefix-claude' available again (rebuilt locally, never pulled)" (Test-ImageExists "$savedRegistry/$imagePrefix-claude")
        }
        finally
        {
            $env:DOCKER_REGISTRY = ''
        }
    }

    # === Image-space cleanup: measuring and reporting (removes nothing). ===
    # A budget nothing can exceed. This is the assertion that matters most, because it proves that
    # `docker system df` was found, that its Images row was parsed, and that the byte arithmetic survives a
    # budget of a million gigabytes. That parsing is what breaks when the version of Docker changes.
    Write-Host "`n== Image-space cleanup (under budget) ==" -ForegroundColor Magenta
    $r = Invoke-DockerBuild @('-MaxImageSpace', '1000000')
    if ($r.ExitCode -ne 0) { Write-Host $r.Output }
    Test-Case "cleanup: exits 0 when under budget" ($r.ExitCode -eq 0)
    Test-Case "cleanup: reports the measured store size" ($r.Output -match 'Docker image store: [\d.]+ GB of the 1000000 GB budget')
    Test-Case "cleanup: removes nothing when under budget" ($r.Output -notmatch 'removing up to')

    Write-Host "`n== Image-space cleanup (disabled and invalid) ==" -ForegroundColor Magenta
    $r = Invoke-DockerBuild @('-MaxImageSpace', '0')
    Test-Case "cleanup: -MaxImageSpace 0 exits 0" ($r.ExitCode -eq 0)
    Test-Case "cleanup: -MaxImageSpace 0 does not measure the store" ($r.Output -notmatch 'Docker image store')

    $r = Invoke-DockerBuild @('-MaxImageSpace', 'lots')
    Test-Case "cleanup: a non-numeric -MaxImageSpace fails the script" ($r.ExitCode -ne 0)
    Test-Case "cleanup: the failure names the parameter" ($r.Output -match 'MaxImageSpace must be a whole number')

    # === Image-space cleanup: the removal path and the grace window (destructive, opt-in). ===
    # Every case that puts the store over budget is destructive, whatever it then asserts: the run removes the
    # unused images of this machine, not only the fixtures. That includes the grace-window case, which is why
    # it lives here and not among the cases above.
    if (-not $IncludeImageEviction)
    {
        Skip-Case "image-space cleanup: removal and grace window" "destructive - going over budget removes unused images from this machine; pass -IncludeImageEviction to run it"
    }
    else
    {
        Write-Host "`n== Image-space cleanup (removal) ==" -ForegroundColor Magenta

        # A candidate that is genuinely old: `docker image ls` reports the build time recorded in an image's
        # manifest, not the time it was pulled or tagged. Tagging the base image of the fixture chain therefore
        # produces a candidate dated well outside the grace window.
        #
        # The base is read from the FROM of the fixture root, expanding the build arguments that the line
        # interpolates (the Windows root takes its tag from WINDOWS_VERSION). It is then pulled explicitly:
        # BuildKit keeps the base of a build in its own cache rather than in the image store, so building the
        # chain does not by itself make the base image appear in `docker image ls`.
        $rootDockerfile = Get-Content (Join-Path $sandbox 'eng/docker/vs.Dockerfile') -Raw
        $baseImage = ([regex]::Match($rootDockerfile, '(?m)^\s*FROM\s+(\S+)')).Groups[1].Value
        foreach ($argMatch in [regex]::Matches($rootDockerfile, '(?m)^\s*ARG\s+(\w+)=(\S+)'))
        {
            $baseImage = $baseImage.Replace("`${$( $argMatch.Groups[1].Value )}", $argMatch.Groups[2].Value)
        }

        docker pull $baseImage *> $null
        if ($LASTEXITCODE -eq 0) { docker tag $baseImage "$imagePrefix-stale:v1" *> $null }
        else { Write-Host "  (could not pull the fixture base image '$baseImage')" -ForegroundColor Yellow }
        $tagged = Test-ImageExists "$imagePrefix-stale"
        Test-Case "cleanup: a stale candidate could be staged" $tagged

        # A candidate that is genuinely new and is NOT in the keep set, which is what isolates the grace window
        # from the keep set.
        #
        # The COPY matters. An image only gets a current creation date when the build adds a filesystem layer:
        # a build that changes nothing but metadata, such as one holding only a FROM and a LABEL, keeps the
        # creation date of its base image and would be staged as an old image, not a new one.
        $freshStaged = $false
        if ($baseImage)
        {
            $freshCtx = Join-Path $sandbox '.fresh'
            New-Item -ItemType Directory -Path $freshCtx -Force | Out-Null
            Set-Content -Path (Join-Path $freshCtx 'fresh.txt') -Value 'fresh' -Encoding ascii
            "FROM $baseImage`nCOPY fresh.txt ./fresh.txt" | docker build -t "$imagePrefix-fresh:v1" -f - $freshCtx *> $null
            $freshStaged = Test-ImageExists "$imagePrefix-fresh"
        }
        Test-Case "cleanup: a fresh candidate could be staged" $freshStaged

        # A budget one gigabyte below the current store, so that the run is over budget by about a gigabyte and
        # the loop is satisfied after removing very little, instead of emptying the machine. The budget is a
        # whole number of gigabytes, so a store under 2 GB cannot be given a positive budget below itself.
        $budget = [int][Math]::Floor((Get-TestImageStoreSize) / 1e9) - 1

        if ($tagged -and $budget -lt 1)
        {
            Skip-Case "image-space cleanup: removal" "this machine holds under 2 GB of images, which is too little to set a positive budget below the store; run it on a build agent"
        }
        elseif ($tagged)
        {
            $r = Invoke-DockerBuild @('-MaxImageSpace', "$budget")
            if ($r.ExitCode -ne 0) { Write-Host $r.Output }
            Test-Case "cleanup: the removal pass exits 0" ($r.ExitCode -eq 0)
            Test-Case "cleanup: reports that it is over budget" ($r.Output -match "over the $budget GB budget")
            Test-Case "cleanup: the stale image was removed" (-not (Test-ImageExists "$imagePrefix-stale"))

            # The two invariants that a regression here would break. First, the cleanup must never take out the
            # chain that the run it precedes is about to use: that is the keep set. Second, it must never take
            # out an image built moments ago, which is what keeps a concurrent run on the same agent safe: that
            # is the grace window, and '$imagePrefix-fresh' tests it on its own, because it is new but is in no
            # keep set.
            Test-Case "cleanup: the current chain survived" ((Test-ImageExists "$imagePrefix-vs") -and (Test-ImageExists "$imagePrefix-build"))
            if ($freshStaged)
            {
                Test-Case "cleanup: an image outside the keep set survived the grace window" (Test-ImageExists "$imagePrefix-fresh")
            }
        }
    }

    # === Cases not covered by lightweight fixtures (documented). ===
    Write-Host "`n== Out-of-scope for lightweight fixtures ==" -ForegroundColor Magenta
    Skip-Case "OS build-arg fold (ltsc2025 vs ltsc2022)" "requires building on two Windows host editions; the WINDOWS_VERSION build-arg + hash fold is exercised, not asserted here"
    Skip-Case "image-space cleanup: the OS image is in the keep set" "the fixture roots declare no 'ARG OS_IMAGE'/'ARG OS_IMAGE_REPOSITORY', so Get-OsImageSpec returns nothing for them; the branch is exercised by real product builds, whose root declares OS_IMAGE_REPOSITORY"
    Skip-Case "boot image + MOUNTPOINTS creation" "requires 'docker run' of a PowerShell-7 image; covered by real product builds"
    Skip-Case "runtime env/init delivery" "requires 'docker run' of a PowerShell-7 image; covered by real product builds"
    Skip-Case "-NoBuildImage run step rebuilds the local-only Claude leaf" "the run step resolves the chain then 'docker run's a PowerShell-7 image; the local-only Claude leaf rebuild (cross-daemon CI case) is covered by real product builds, not the lightweight fixtures"
}
finally
{
    $env:IS_POSTSHARP_OWNED = $savedPostSharpOwned
    $env:DOCKER_REGISTRY = $savedRegistry
    $env:DOCKER_MAX_IMAGE_SPACE = $savedMaxImageSpace

    if ($KeepSandbox)
    {
        Write-Host "`nKeeping sandbox and images (-KeepSandbox): $sandbox" -ForegroundColor Yellow
    }
    else
    {
        Write-Host "`nCleaning up test images and sandbox..." -ForegroundColor Cyan
        Remove-TestImages
        if (Test-Path $sandbox) { Remove-Item $sandbox -Recurse -Force -ErrorAction SilentlyContinue }
    }
}

# ---- summary --------------------------------------------------------------------------------------------
Write-Host "`n=========================================================" -ForegroundColor Cyan
Write-Host "DockerBuild test suite: $($script:passes) passed, $($script:failures.Count) failed, $($script:skips) skipped" -ForegroundColor Cyan
if ($script:failures.Count -gt 0)
{
    Write-Host "Failed cases:" -ForegroundColor Red
    $script:failures | ForEach-Object { Write-Host "  - $_" -ForegroundColor Red }
    exit 1
}
Write-Host "All asserted cases passed." -ForegroundColor Green
exit 0
