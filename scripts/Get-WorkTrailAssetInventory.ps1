# VBWR B
#
# Project: WorkTrail
# Repository: https://github.com/umbertotechnopreneur/WorkTrail
# Creator: Umberto Giacobbi | https://umbertogiacobbi.biz
#
# VibeWare: Human intent, AI execution, and plenty of tokens
# Manifesto: https://umbertogiacobbi.biz/vibeware/manifesto
#
# Modified with AI: OpenAI Codex; added this header on 2026-10-10.
# Human guidance: Umberto Giacobbi; requested VibeWare branding.
#
# Copyright (c) 2026 Umberto Giacobbi
# License: MIT - see LICENSE
# SPDX-License-Identifier: MIT
#
# VBWR E

[CmdletBinding()]
param(
    [string]$OutputPath = (Join-Path $PSScriptRoot '..\.github\tasks\asset-inventory.csv')
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$assetRoot = Join-Path $repositoryRoot 'WorkTrail\Assets'
$referenceRoots = @(
    (Join-Path $repositoryRoot 'WorkTrail'),
    (Join-Path $repositoryRoot 'scripts')
)
$referenceFiles = Get-ChildItem -LiteralPath $referenceRoots -Recurse -File |
    Where-Object { $_.Extension -in '.cs', '.xaml', '.csproj', '.xml', '.ps1' }

$assets = Get-ChildItem -LiteralPath $assetRoot -File | Sort-Object Name
$hashCounts = $assets |
    ForEach-Object { (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash } |
    Group-Object -NoElement |
    ForEach-Object -Begin { $counts = @{} } -Process { $counts[$_.Name] = $_.Count } -End { $counts }

$inventory = foreach ($asset in $assets) {
    $hash = (Get-FileHash -LiteralPath $asset.FullName -Algorithm SHA256).Hash
    $width = $null
    $height = $null
    if ($asset.Extension -in '.png', '.ico') {
        $image = [System.Drawing.Image]::FromFile($asset.FullName)
        try {
            $width = $image.Width
            $height = $image.Height
        }
        finally {
            $image.Dispose()
        }
    }

    $matches = @($referenceFiles |
        Select-String -SimpleMatch -Pattern $asset.Name |
        ForEach-Object { [System.IO.Path]::GetRelativePath($repositoryRoot, $_.Path).Replace('\', '/') } |
        Sort-Object -Unique)
    $isWorkTrailGenerated = $asset.Name -like 'WorkTrail*.png'
    $isExplicitPackageAsset = $asset.Name -in @(
        'SplashScreen.scale-200.png',
        'LockScreenLogo.scale-200.png',
        'Square150x150Logo.scale-200.png',
        'Square44x44Logo.scale-200.png',
        'Square44x44Logo.targetsize-24_altform-unplated.png',
        'StoreLogo.png',
        'Wide310x150Logo.scale-200.png',
        'WorkTrailIcon.ico'
    )
    $role = if ($matches.Count -gt 0) {
        'direct-reference'
    }
    elseif ($isWorkTrailGenerated -and $asset.Name -match '(scale-|targetsize-|altform-)') {
        'package-platform-variant'
    }
    elseif ($isWorkTrailGenerated) {
        'package-base-asset'
    }
    elseif ($isExplicitPackageAsset) {
        'legacy-package-asset'
    }
    else {
        'unclassified'
    }

    [pscustomobject]@{
        Path = [System.IO.Path]::GetRelativePath($repositoryRoot, $asset.FullName).Replace('\', '/')
        Width = $width
        Height = $height
        Bytes = $asset.Length
        Sha256 = $hash
        DuplicateCount = $hashCounts[$hash]
        MsBuildIncluded = $isWorkTrailGenerated -or $isExplicitPackageAsset
        Classification = $role
        References = $matches -join ';'
    }
}

$resolvedOutput = [System.IO.Path]::GetFullPath($OutputPath)
$outputDirectory = Split-Path -Parent $resolvedOutput
[System.IO.Directory]::CreateDirectory($outputDirectory) | Out-Null
$inventory | Export-Csv -LiteralPath $resolvedOutput -NoTypeInformation -Encoding utf8
Write-Output "Wrote $($inventory.Count) asset records to $resolvedOutput"
