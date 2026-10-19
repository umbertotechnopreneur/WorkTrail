# SPDX-License-Identifier: MIT
#requires -Version 7.0
<#
.SYNOPSIS
Cleans this job's generated output after preserving its dependency scan records.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateSet('Debug', 'Release-Unpackaged')]
    [string]$Configuration,
    [Parameter(Mandatory)]
    [ValidateSet('x64', 'ARM64')]
    [string]$Platform
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if ($env:GITHUB_ACTIONS -ne 'true' -or $env:RUNNER_ENVIRONMENT -ne 'github-hosted') {
    throw 'This cleanup is restricted to disposable GitHub-hosted CI jobs.'
}
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$artifactsRoot = [IO.Path]::GetFullPath((Join-Path $repositoryRoot 'artifacts'))
$securityRoot = Join-Path $artifactsRoot 'ci/security'
$clearedStages = @{}
foreach ($stage in @('Archive', 'DotNet', 'NuGet')) {
    $recordPath = Join-Path $securityRoot "$stage.json"
    $clearedStages[$stage] = $false
    if (Test-Path -LiteralPath $recordPath) {
        $recordText = Get-Content -LiteralPath $recordPath -Raw
        $clearedStages[$stage] = ($recordText | ConvertFrom-Json).CompletedWithoutDetections
        # Preserve the small audit record in the job summary before deleting local output.
        if ($env:GITHUB_STEP_SUMMARY) {
            @("### $stage dependency scan", '```json', $recordText, '```') |
                Add-Content -LiteralPath $env:GITHUB_STEP_SUMMARY -Encoding utf8NoBOM
        }
    }
}

$cleanFailed = $false
if ($clearedStages.Archive -and $clearedStages.DotNet -and $clearedStages.NuGet) {
    # Never invoke MSBuild targets from dependencies that failed their security gate.
    $cleanTargets = @((Join-Path $repositoryRoot 'WorkTrail.slnx'))
    if ($Configuration -eq 'Debug') {
        # The lint job builds test projects directly; the solution has no Debug|x64 configuration.
        $cleanTargets = @(git -C $repositoryRoot ls-files -- '*.Tests.csproj')
        if ($LASTEXITCODE -ne 0 -or $cleanTargets.Count -eq 0) {
            $cleanFailed = $true
            $cleanTargets = @()
        }
    }
    foreach ($projectPath in $cleanTargets) {
        dotnet clean $projectPath --configuration $Configuration "-p:Platform=$Platform"
        $cleanFailed = $cleanFailed -or $LASTEXITCODE -ne 0
    }
    dotnet build-server shutdown
    $cleanFailed = $cleanFailed -or $LASTEXITCODE -ne 0
}
foreach ($relative in @('ci', 'build-metadata', 'build-state')) {
    $target = [IO.Path]::GetFullPath((Join-Path $artifactsRoot $relative))
    if (-not $target.StartsWith($artifactsRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'A CI cleanup target escaped artifacts/.'
    }
    if (-not (Test-Path -LiteralPath $target)) { continue }
    $resolved = (Resolve-Path -LiteralPath $target).Path
    if ($resolved -ne $target -or ((Get-Item -LiteralPath $target).Attributes -band [IO.FileAttributes]::ReparsePoint)) {
        throw "A CI cleanup target is linked elsewhere: $target"
    }
    $links = @(Get-ChildItem -LiteralPath $target -Recurse -Force |
        Where-Object { $_.Attributes -band [IO.FileAttributes]::ReparsePoint } | Select-Object -First 1)
    if ($links.Count -gt 0) { throw 'CI output contains a link; inspect it before cleanup.' }
    Remove-Item -LiteralPath $target -Recurse -Force
    if (Test-Path -LiteralPath $target) { throw "CI output remains after cleanup: $target" }
}
if ($cleanFailed) { throw 'dotnet clean failed; task-owned files were removed, but the clean target did not succeed.' }
Write-Output 'CI intermediates, package cache and test output removed; dependency scan records remain in the job summary.'
