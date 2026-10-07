<#
.SYNOPSIS
Looks up the API documentation of the skill by type or member name.

.DESCRIPTION
PowerShell fallback for find-api.py, for environments without Python.
Compatible with Windows PowerShell 5.1 and PowerShell 7+.

Run from the skill root directory (the directory containing api/), or from
anywhere as long as this script stays in the skill's scripts/ directory.

Prints the matching UIDs and, for the most specific matches, the full
documentation block (summary, syntax, parameters, remarks) extracted from the
DocFx YML — so you don't need to read the whole YML file.

.EXAMPLE
powershell -File scripts/find-api.ps1 MyType

.EXAMPLE
pwsh -File scripts/find-api.ps1 MyNamespace.MyType.MyMethod
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true, Position = 0)]
    [string] $Query,

    [int] $MaxListed = 25,
    [int] $MaxBlocks = 3
)

$ErrorActionPreference = 'Stop'

function Find-SkillRoot {
    $candidates = @()

    if ($PSScriptRoot) {
        $candidates += (Split-Path -Parent $PSScriptRoot)
    }

    $candidates += (Get-Location).Path

    foreach ($candidate in $candidates) {
        if (Test-Path (Join-Path (Join-Path $candidate 'api') '.manifest')) {
            return $candidate
        }
    }

    Write-Output 'Cannot find api/.manifest. Run this script from the skill root directory.'
    exit 2
}

function Get-ItemBlock {
    param(
        [string] $YmlText,
        [string] $Uid
    )

    $output = New-Object System.Collections.Generic.List[string]
    $capture = $false

    foreach ($rawLine in ($YmlText -split "`n")) {
        $line = $rawLine.TrimEnd("`r")

        if ($line -ceq ('- uid: ' + $Uid)) {
            $capture = $true
        }
        elseif ($capture -and ($line.StartsWith('- uid: ') -or $line -match '^[A-Za-z][A-Za-z0-9]*:')) {
            break
        }

        if ($capture) {
            $output.Add($line)
        }
    }

    return ($output -join "`n")
}

$root = Find-SkillRoot
$manifestPath = Join-Path (Join-Path $root 'api') '.manifest'
$manifest = [System.IO.File]::ReadAllText($manifestPath) | ConvertFrom-Json
$entries = @($manifest.PSObject.Properties)

# A trailing '*' denotes all the overloads in a docfx xref; the manifest has no entry for it.
$Query = $Query.TrimEnd('*')

# Exact UID match first; otherwise case-insensitive substring match.
$found = @($entries | Where-Object { $_.Name -ceq $Query })

if ($found.Count -eq 0) {
    $found = @($entries | Where-Object { $_.Name.IndexOf($Query, [System.StringComparison]::OrdinalIgnoreCase) -ge 0 })
}

if ($found.Count -eq 0) {
    Write-Output "No API matching '$Query'. Try a shorter substring."
    exit 1
}

Write-Output "$($found.Count) match(es) for '$Query':"

$found | Select-Object -First $MaxListed | ForEach-Object {
    Write-Output "  $($_.Name)  ->  api/$($_.Value)"
}

if ($found.Count -gt $MaxListed) {
    Write-Output "  ... and $($found.Count - $MaxListed) more. Refine the query."
}

# Print full documentation for the most specific (shortest-UID) matches.
$best = $found | Sort-Object { $_.Name.Length } | Select-Object -First $MaxBlocks

foreach ($entry in $best) {
    $ymlPath = Join-Path (Join-Path $root 'api') ($entry.Value -replace '/', [System.IO.Path]::DirectorySeparatorChar)

    if (-not (Test-Path $ymlPath)) {
        continue
    }

    $block = Get-ItemBlock -YmlText ([System.IO.File]::ReadAllText($ymlPath)) -Uid $entry.Name

    if ($block) {
        Write-Output ''
        Write-Output ('=' * 72)
        Write-Output $block
    }
}

exit 0
