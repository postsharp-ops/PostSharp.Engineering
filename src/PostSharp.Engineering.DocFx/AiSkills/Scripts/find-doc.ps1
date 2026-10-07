<#
.SYNOPSIS
Searches the conceptual documentation index of the skill by keyword.

.DESCRIPTION
PowerShell fallback for find-doc.py, for environments without Python.
Compatible with Windows PowerShell 5.1 and PowerShell 7+.

Run from the skill root directory (the directory containing index.yml), or from
anywhere as long as this script stays in the skill's scripts/ directory.

Searches article titles, summaries, keywords, and paths in index.yml and prints
the matching articles with their file paths, ready to be read. All keywords must
match (AND semantics). A single keyword that equals the uid of an article returns
only that article, which resolves an <xref:uid> link.

.EXAMPLE
powershell -File scripts/find-doc.ps1 cache invalidation

.EXAMPLE
pwsh -File scripts/find-doc.ps1 "getting started"
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true, Position = 0, ValueFromRemainingArguments = $true)]
    [string[]] $Keywords,

    [int] $MaxResults = 25
)

$ErrorActionPreference = 'Stop'

function Find-SkillRoot {
    $candidates = @()

    if ($PSScriptRoot) {
        $candidates += (Split-Path -Parent $PSScriptRoot)
    }

    $candidates += (Get-Location).Path

    foreach ($candidate in $candidates) {
        if (Test-Path (Join-Path $candidate 'index.yml')) {
            return $candidate
        }
    }

    Write-Output 'Cannot find index.yml. Run this script from the skill root directory.'
    exit 2
}

$root = Find-SkillRoot
$indexText = [System.IO.File]::ReadAllText((Join-Path $root 'index.yml'))

# Light-weight parse of index.yml: each item starts with a 'name:' key.
$records = New-Object System.Collections.Generic.List[hashtable]
$current = $null

foreach ($rawLine in ($indexText -split "`n")) {
    $line = $rawLine.TrimEnd("`r")

    if ($line -match '^\s*-?\s*(name|uid|path|summary|keywords):\s*(.*)$') {
        $key = $Matches[1]
        $value = $Matches[2].Trim().Trim('"').Trim("'")

        if ($key -eq 'name') {
            if ($null -ne $current) {
                $records.Add($current)
            }

            $current = @{}
        }

        if ($null -ne $current) {
            $current[$key] = $value
        }
    }
}

if ($null -ne $current) {
    $records.Add($current)
}

$terms = @($Keywords | ForEach-Object { $_.ToLowerInvariant() })

$hits = @()

if ($terms.Count -eq 1) {
    $hits = @($records | Where-Object { $_['uid'] -and $_['uid'].ToLowerInvariant() -eq $terms[0] })
}

if ($hits.Count -eq 0) {
    $hits = @($records | Where-Object {
        $record = $_
        $haystack = (@('name', 'uid', 'path', 'summary', 'keywords') | ForEach-Object { $record[$_] }) -join ' '
        $haystack = $haystack.ToLowerInvariant()
        $allMatch = $true

        foreach ($term in $terms) {
            if (-not $haystack.Contains($term)) {
                $allMatch = $false
                break
            }
        }

        $allMatch
    })
}

foreach ($hit in ($hits | Select-Object -First $MaxResults)) {
    Write-Output "- $($hit['name'])"

    if ($hit['uid']) {
        Write-Output "  uid: $($hit['uid'])"
    }

    if ($hit['path']) {
        Write-Output "  path: $($hit['path'])"
    }

    if ($hit['summary']) {
        $summary = $hit['summary']

        if ($summary.Length -gt 220) {
            $summary = $summary.Substring(0, 220)
        }

        Write-Output "  $summary"
    }
}

if ($hits.Count -gt $MaxResults) {
    Write-Output "... and $($hits.Count - $MaxResults) more. Add keywords to narrow down."
}

Write-Output ''
Write-Output "$($hits.Count) match(es)."

if ($hits.Count -gt 0) {
    exit 0
}
else {
    exit 1
}
