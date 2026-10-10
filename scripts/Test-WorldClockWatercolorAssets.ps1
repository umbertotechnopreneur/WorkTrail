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
    [Parameter()]
    [string] $MasterRoot = (Join-Path $PSScriptRoot '..\design\world-clocks\watercolor\masters-v1'),

    [Parameter()]
    [string] $ManifestPath,

    [Parameter()]
    [switch] $RequireComplete
)

$ErrorActionPreference = 'Stop'

$resolvedRoot = (Resolve-Path -LiteralPath $MasterRoot).Path
$ffprobe = Get-Command ffprobe -ErrorAction Stop
$ffmpeg = Get-Command ffmpeg -ErrorAction Stop
$expectedRatio = 16.0 / 9.0
$ratioTolerance = 0.01
$cityIdPattern = '^[a-z0-9]+(?:-[a-z0-9]+)*$'
$sha256Pattern = '^[0-9a-f]{64}$'

$expectedNames = @()
$reviewedHashByName = [Collections.Generic.Dictionary[string, string]]::new([StringComparer]::Ordinal)
if (-not [string]::IsNullOrWhiteSpace($ManifestPath)) {
    $resolvedManifest = (Resolve-Path -LiteralPath $ManifestPath).Path
    $manifest = Get-Content -LiteralPath $resolvedManifest -Raw | ConvertFrom-Json
    $cities = @($manifest.cities)
    $expectedCityCount = $cities.Count
    $expectedAssetCount = $expectedCityCount * 2
    if ([int] $manifest.schemaVersion -ne 1 -or [string] $manifest.styleId -ne 'urban-wash-v1' `
        -or [int] $manifest.assetCountExpected -ne $expectedAssetCount) {
        throw "Unsupported generation manifest schema, style, or asset count."
    }

    if ($expectedCityCount -eq 0) {
        throw 'The generation manifest must contain at least one city.'
    }

    $invalidIds = @($cities | Where-Object {
        [string]::IsNullOrWhiteSpace([string] $_.cityId) -or [string] $_.cityId -cnotmatch $cityIdPattern
    })
    if ($invalidIds.Count -gt 0) {
        throw "The generation manifest contains invalid city ids."
    }

    $duplicateIds = @($cities | Group-Object cityId | Where-Object Count -ne 1)
    if ($duplicateIds.Count -gt 0) {
        throw "The generation manifest contains duplicate city ids: $($duplicateIds.Name -join ', ')."
    }

    $reviewedMasters = @($manifest.reviewedMasters)
    if ($reviewedMasters.Count -ne $expectedAssetCount) {
        throw "The generation manifest must bind exactly $expectedAssetCount reviewed masters; found $($reviewedMasters.Count)."
    }

    $cityIds = @($cities.cityId)
    $invalidReviewedMasters = @($reviewedMasters | Where-Object {
        $cityId = [string] $_.cityId
        $season = [string] $_.season
        $expectedFileName = "$cityId-$season.png"
        $cityId -cnotin $cityIds `
            -or $season -cnotin @('summer', 'winter') `
            -or [string] $_.fileName -cne $expectedFileName `
            -or [string] $_.sha256 -cnotmatch $sha256Pattern
    })
    if ($invalidReviewedMasters.Count -gt 0) {
        throw 'The generation manifest contains an invalid reviewed-master binding.'
    }

    $duplicateReviewedMasters = @($reviewedMasters |
        Group-Object { "$($_.cityId)/$($_.season)" } |
        Where-Object Count -ne 1)
    if ($duplicateReviewedMasters.Count -gt 0) {
        throw "The generation manifest contains duplicate reviewed-master bindings: $($duplicateReviewedMasters.Name -join ', ')."
    }

    $expectedBindingKeys = @($cities | ForEach-Object {
        "$($_.cityId)/summer"
        "$($_.cityId)/winter"
    } | Sort-Object)
    $actualBindingKeys = @($reviewedMasters | ForEach-Object { "$($_.cityId)/$($_.season)" } | Sort-Object)
    if (Compare-Object -ReferenceObject $expectedBindingKeys -DifferenceObject $actualBindingKeys) {
        throw 'The generation manifest reviewed-master bindings do not exactly cover every city and season.'
    }

    foreach ($reviewedMaster in $reviewedMasters) {
        $reviewedHashByName.Add(
            [string] $reviewedMaster.fileName,
            [string] $reviewedMaster.sha256)
    }
    $expectedNames = @($reviewedMasters.fileName | Sort-Object)
}

$rootChildren = @(Get-ChildItem -LiteralPath $resolvedRoot -Force)
$files = @($rootChildren | Where-Object { -not $_.PSIsContainer -and $_.Name -like '*.png' } | Sort-Object Name)
if ($files.Count -eq 0) {
    throw "No PNG masters were found under $resolvedRoot."
}

if ($RequireComplete) {
    if ($expectedNames.Count -eq 0) {
        throw '-RequireComplete requires -ManifestPath.'
    }

    $actualNames = @($rootChildren | ForEach-Object Name | Sort-Object)
    $comparison = @(Compare-Object -ReferenceObject $expectedNames -DifferenceObject $actualNames -CaseSensitive)
    $missing = @($comparison | Where-Object SideIndicator -eq '<=')
    $unexpected = @($comparison | Where-Object SideIndicator -eq '=>')
    if ($missing.Count -gt 0 -or $unexpected.Count -gt 0) {
        throw "Asset set is incomplete. Missing=$($missing.Count); unexpected=$($unexpected.Count)."
    }
}

$results = foreach ($file in $files) {
    $probeOutput = & $ffprobe.Source -v error -select_streams v:0 `
        -show_entries stream=codec_name,width,height,pix_fmt -of json $file.FullName
    if ($LASTEXITCODE -ne 0) {
        throw "ffprobe failed for $($file.FullName)."
    }

    $probe = $probeOutput | ConvertFrom-Json
    $streams = @($probe.streams)
    if ($streams.Count -ne 1) {
        throw "Expected exactly one image stream in $($file.FullName); found $($streams.Count)."
    }
    $stream = $streams[0]

    if ([string] $stream.codec_name -cne 'png') {
        throw "Master $($file.Name) must decode as PNG; found codec $($stream.codec_name)."
    }

    $width = [int] $stream.width
    $height = [int] $stream.height
    $ratio = $width / [double] $height
    if ($width -lt 1500 -or $height -lt 840 -or [Math]::Abs($ratio - $expectedRatio) -gt $ratioTolerance) {
        throw "Unexpected master dimensions for $($file.Name): ${width}x${height}."
    }

    if ([string] $stream.pix_fmt -cne 'rgba') {
        throw "Master $($file.Name) must decode as RGBA; found pixel format $($stream.pix_fmt)."
    }

    $alphaOutput = (& $ffmpeg.Source -hide_banner -loglevel error -i $file.FullName `
        -vf 'alphaextract,signalstats,metadata=print:file=-' -frames:v 1 -f null NUL 2>&1) -join "`n"
    if ($LASTEXITCODE -ne 0) {
        throw "FFmpeg alpha validation failed for $($file.FullName)."
    }

    $minimumMatch = [regex]::Match($alphaOutput, 'lavfi\.signalstats\.YMIN=(\d+)')
    $maximumMatch = [regex]::Match($alphaOutput, 'lavfi\.signalstats\.YMAX=(\d+)')
    if (-not $minimumMatch.Success -or -not $maximumMatch.Success) {
        throw "Alpha statistics were not reported for $($file.FullName)."
    }

    $alphaMinimum = [int] $minimumMatch.Groups[1].Value
    $alphaMaximum = [int] $maximumMatch.Groups[1].Value
    if ($alphaMinimum -ne 0 -or $alphaMaximum -ne 255) {
        throw "Master $($file.Name) must contain both fully transparent and fully opaque pixels; found $alphaMinimum-$alphaMaximum."
    }

    $actualSha256 = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($reviewedHashByName.Count -gt 0) {
        if (-not $reviewedHashByName.ContainsKey($file.Name)) {
            throw "Master $($file.Name) has no reviewed checksum binding in the generation manifest."
        }
        if ($actualSha256 -cne $reviewedHashByName[$file.Name]) {
            throw "Master checksum does not match the reviewed generation-manifest binding: $($file.Name)."
        }
    }

    [pscustomobject]@{
        Name = $file.Name
        Codec = [string] $stream.codec_name
        Width = $width
        Height = $height
        PixelFormat = [string] $stream.pix_fmt
        AlphaMinimum = $alphaMinimum
        AlphaMaximum = $alphaMaximum
        Bytes = $file.Length
        Sha256 = $actualSha256
    }
}

$results
