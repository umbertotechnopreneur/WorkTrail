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
    [string] $OutputRoot = (Join-Path $PSScriptRoot '..\design\world-clocks\watercolor\runtime-v4'),

    [Parameter()]
    [string] $ManifestPath = (Join-Path $PSScriptRoot '..\design\world-clocks\watercolor\generation-manifest-v1.json'),

    [Parameter()]
    [switch] $RequireComplete
)

$ErrorActionPreference = 'Stop'

$expectedSchemaVersion = 1
$expectedStyleId = 'urban-wash-v1'
$cityIdPattern = '^[a-z0-9]+(?:-[a-z0-9]+)*$'
$sha256Pattern = '^[0-9a-f]{64}$'

function Assert-DirectChildPath {
    param(
        [Parameter(Mandatory)]
        [string] $Parent,

        [Parameter(Mandatory)]
        [string] $Child
    )

    $parentPath = [IO.Path]::TrimEndingDirectorySeparator([IO.Path]::GetFullPath($Parent))
    $childPath = [IO.Path]::GetFullPath($Child)
    if (-not [string]::Equals(
        [IO.Path]::GetDirectoryName($childPath),
        $parentPath,
        [StringComparison]::OrdinalIgnoreCase)) {
        throw "Path must be a direct child of $parentPath`: $childPath"
    }
}

$resolvedMasterRoot = (Resolve-Path -LiteralPath $MasterRoot).Path
$resolvedManifestPath = (Resolve-Path -LiteralPath $ManifestPath).Path
$manifest = Get-Content -LiteralPath $resolvedManifestPath -Raw | ConvertFrom-Json
$cities = @($manifest.cities)
$expectedCityCount = $cities.Count
$expectedAssetCount = $expectedCityCount * 2
if ([int] $manifest.schemaVersion -ne $expectedSchemaVersion -or [string] $manifest.styleId -ne $expectedStyleId) {
    throw "Unsupported generation manifest schema/style: $($manifest.schemaVersion)/$($manifest.styleId)."
}
if ($expectedCityCount -eq 0 -or [int] $manifest.assetCountExpected -ne $expectedAssetCount) {
    throw "The generation manifest asset count must match its declared city list; found $($cities.Count)/$($manifest.assetCountExpected)."
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
$reviewedMasterByKey = [Collections.Generic.Dictionary[string, object]]::new([StringComparer]::Ordinal)
foreach ($reviewedMaster in $reviewedMasters) {
    $cityId = [string] $reviewedMaster.cityId
    $season = [string] $reviewedMaster.season
    $key = "$cityId/$season"
    $expectedFileName = "$cityId-$season.png"
    if ($cityId -cnotin $cityIds `
        -or $season -cnotin @('summer', 'winter') `
        -or [string] $reviewedMaster.fileName -cne $expectedFileName `
        -or [string] $reviewedMaster.sha256 -cnotmatch $sha256Pattern) {
        throw "The generation manifest contains an invalid reviewed-master binding: $key."
    }
    if ($reviewedMasterByKey.ContainsKey($key)) {
        throw "The generation manifest contains a duplicate reviewed-master binding: $key."
    }
    $reviewedMasterByKey.Add($key, $reviewedMaster)
}

$expectedBindingKeys = @($cities | ForEach-Object {
    "$($_.cityId)/summer"
    "$($_.cityId)/winter"
} | Sort-Object)
if (Compare-Object -ReferenceObject $expectedBindingKeys -DifferenceObject @($reviewedMasterByKey.Keys | Sort-Object)) {
    throw 'The generation manifest reviewed-master bindings do not exactly cover every city and season.'
}

$ffmpeg = Get-Command ffmpeg -ErrorAction Stop
$ffprobe = Get-Command ffprobe -ErrorAction Stop
$validatorPath = Join-Path $PSScriptRoot 'Test-WorldClockWatercolorAssets.ps1'
$qualityGateResults = if ($RequireComplete) {
    @(& $validatorPath -MasterRoot $resolvedMasterRoot -ManifestPath $resolvedManifestPath -RequireComplete)
} else {
    @(& $validatorPath -MasterRoot $resolvedMasterRoot -ManifestPath $resolvedManifestPath)
}
if ($qualityGateResults.Count -eq 0) {
    throw 'The master quality gate did not validate any source assets.'
}

$resolvedOutputRoot = [IO.Path]::GetFullPath($OutputRoot)
$outputParent = [IO.Path]::GetDirectoryName($resolvedOutputRoot)
if (-not (Test-Path -LiteralPath $outputParent -PathType Container)) {
    throw "Output parent directory does not exist: $outputParent."
}
$resolvedOutputParent = (Resolve-Path -LiteralPath $outputParent).Path
Assert-DirectChildPath -Parent $resolvedOutputParent -Child $resolvedOutputRoot

if (Test-Path -LiteralPath $resolvedOutputRoot) {
    $outputItem = Get-Item -LiteralPath $resolvedOutputRoot -Force
    if (-not $outputItem.PSIsContainer -or ($outputItem.Attributes -band [IO.FileAttributes]::ReparsePoint)) {
        throw "Output path must be a normal directory: $resolvedOutputRoot."
    }
    $existingItems = @(Get-ChildItem -LiteralPath $resolvedOutputRoot -Force)
    if ($existingItems.Count -gt 0) {
        throw "Output directory must be empty to prevent overwrites: $resolvedOutputRoot."
    }
}

$stagingRoot = Join-Path $resolvedOutputParent ".$(Split-Path -Leaf $resolvedOutputRoot).staging-$([Guid]::NewGuid().ToString('N'))"
Assert-DirectChildPath -Parent $resolvedOutputParent -Child $stagingRoot
New-Item -ItemType Directory -Path $stagingRoot | Out-Null
$completed = $false

$expected = foreach ($city in $cities) {
    foreach ($season in 'summer', 'winter') {
        $cityId = [string] $city.cityId
        $reviewedMaster = $reviewedMasterByKey["$cityId/$season"]
        [pscustomobject]@{
            CityId = $cityId
            Season = $season
            SourceMasterFileName = [string] $reviewedMaster.fileName
            SourceMasterSha256 = [string] $reviewedMaster.sha256
            Source = Join-Path $resolvedMasterRoot ([string] $reviewedMaster.fileName)
            Target = Join-Path $stagingRoot "$cityId-$season.webp"
        }
    }
}

$missing = @($expected | Where-Object { -not (Test-Path -LiteralPath $_.Source) })
$expectedMasterNames = @($expected | ForEach-Object { [IO.Path]::GetFileName($_.Source) } | Sort-Object)
$actualMasterNames = @(Get-ChildItem -LiteralPath $resolvedMasterRoot -File -Filter '*.png' | ForEach-Object Name | Sort-Object)
$unexpectedMasters = @($actualMasterNames | Where-Object { $_ -notin $expectedMasterNames })
if ($RequireComplete -and ($missing.Count -gt 0 -or $unexpectedMasters.Count -gt 0)) {
    throw "The master set is not exact; missing=$($missing.Count), unexpected=$($unexpectedMasters.Count)."
}

$available = @($expected | Where-Object { Test-Path -LiteralPath $_.Source })
if ($available.Count -eq 0) {
    throw "No generated masters are available under $resolvedMasterRoot."
}
if ($qualityGateResults.Count -ne $available.Count) {
    throw "The shared master quality gate validated $($qualityGateResults.Count) files, but conversion selected $($available.Count)."
}

try {
    $ffmpegPath = $ffmpeg.Source
    $ffprobePath = $ffprobe.Source
    $assets = @($available | ForEach-Object -ThrottleLimit 4 -Parallel {
        $ErrorActionPreference = 'Stop'
        $item = $_
        $actualSourceHash = (Get-FileHash -LiteralPath $item.Source -Algorithm SHA256).Hash.ToLowerInvariant()
        if ($actualSourceHash -cne $item.SourceMasterSha256) {
            throw "Source master changed after the quality gate: $($item.Source)."
        }

        & $using:ffmpegPath -hide_banner -loglevel error -n -i $item.Source `
            -vf 'scale=1280:720:force_original_aspect_ratio=increase,crop=1280:720' `
            -frames:v 1 -c:v libwebp -quality 82 -compression_level 4 -pix_fmt yuva420p $item.Target
        if ($LASTEXITCODE -ne 0) {
            throw "WebP conversion failed for $($item.Source)."
        }

        $probeOutput = & $using:ffprobePath -v error -select_streams v:0 `
            -show_entries stream=codec_name,width,height,pix_fmt -of json $item.Target
        if ($LASTEXITCODE -ne 0) {
            throw "ffprobe failed for $($item.Target)."
        }

        $stream = @(($probeOutput | ConvertFrom-Json).streams)[0]
        if ($null -eq $stream -or [string] $stream.codec_name -ne 'webp' `
            -or [int] $stream.width -ne 1280 -or [int] $stream.height -ne 720) {
            throw "Unexpected runtime codec or dimensions for $($item.Target)."
        }

        if ([string] $stream.pix_fmt -notmatch '^yuva') {
            throw "Runtime asset $($item.Target) lost its alpha-capable pixel format: $($stream.pix_fmt)."
        }

        $alphaOutput = (& $using:ffmpegPath -hide_banner -loglevel error -i $item.Target `
            -vf 'alphaextract,signalstats,metadata=print:file=-' -frames:v 1 -f null NUL 2>&1) -join "`n"
        if ($LASTEXITCODE -ne 0) {
            throw "Alpha validation failed for $($item.Target)."
        }

        $minimumMatch = [regex]::Match($alphaOutput, 'lavfi\.signalstats\.YMIN=(\d+)')
        $maximumMatch = [regex]::Match($alphaOutput, 'lavfi\.signalstats\.YMAX=(\d+)')
        if (-not $minimumMatch.Success -or -not $maximumMatch.Success `
            -or [int] $minimumMatch.Groups[1].Value -ne 0 `
            -or [int] $maximumMatch.Groups[1].Value -ne 255) {
            throw "Runtime asset $($item.Target) does not preserve the required 0-255 alpha range."
        }

        [ordered]@{
            cityId = $item.CityId
            season = $item.Season
            fileName = [IO.Path]::GetFileName($item.Target)
            sourceMasterFileName = $item.SourceMasterFileName
            sourceMasterSha256 = $item.SourceMasterSha256
            width = 1280
            height = 720
            pixelFormat = [string] $stream.pix_fmt
            bytes = (Get-Item -LiteralPath $item.Target).Length
            sha256 = (Get-FileHash -LiteralPath $item.Target -Algorithm SHA256).Hash.ToLowerInvariant()
        }
    })
    $assets = @($assets | Sort-Object { $_.cityId }, { $_.season })

    $ffmpegVersion = (& $ffmpeg.Source -version | Select-Object -First 1)
    $libwebpDescription = (& $ffmpeg.Source -hide_banner -h encoder=libwebp 2>&1 | Select-Object -First 1)

    $runtimeManifest = [ordered]@{
        schemaVersion = 1
        sourceManifest = [IO.Path]::GetFileName($resolvedManifestPath)
        sourceManifestSha256 = (Get-FileHash -LiteralPath $resolvedManifestPath -Algorithm SHA256).Hash.ToLowerInvariant()
        sourceManifestSchemaVersion = [int] $manifest.schemaVersion
        sourceMasterBinding = 'generation-manifest-reviewed-sha256'
        styleId = [string] $manifest.styleId
        generatedAt = [DateTimeOffset]::UtcNow.ToString('O')
        transformation = 'Scaled and center-cropped to 1280x720 alpha WebP with FFmpeg/libwebp quality 82, compression level 4'
        toolchain = [ordered]@{
            ffmpeg = [string] $ffmpegVersion
            encoder = 'libwebp'
            encoderDescription = [string] $libwebpDescription
        }
        expectedAssetCount = $expectedAssetCount
        generatedAssetCount = $assets.Count
        complete = $assets.Count -eq $expectedAssetCount
        assets = @($assets)
    }

    $stagedManifestPath = Join-Path $stagingRoot 'runtime-asset-manifest.json'
    $runtimeManifestJson = (($runtimeManifest | ConvertTo-Json -Depth 8) -replace "`r`n", "`n") + "`n"
    [System.IO.File]::WriteAllText(
        $stagedManifestPath,
        $runtimeManifestJson,
        [System.Text.UTF8Encoding]::new($false))

    $stagedItems = @(Get-ChildItem -LiteralPath $stagingRoot -File -Force)
    if ($stagedItems.Count -ne ($assets.Count + 1)) {
        throw "Staged runtime output contains unexpected files."
    }

    if (Test-Path -LiteralPath $resolvedOutputRoot) {
        if (@(Get-ChildItem -LiteralPath $resolvedOutputRoot -Force).Count -ne 0) {
            throw "Output directory changed during conversion: $resolvedOutputRoot."
        }
        Remove-Item -LiteralPath $resolvedOutputRoot -Force
    }
    Move-Item -LiteralPath $stagingRoot -Destination $resolvedOutputRoot
    $completed = $true

    $runtimeManifestPath = Join-Path $resolvedOutputRoot 'runtime-asset-manifest.json'

    [pscustomobject]@{
        OutputRoot = $resolvedOutputRoot
        GeneratedAssetCount = $assets.Count
        MissingMasterCount = $missing.Count
        Complete = $assets.Count -eq $expectedAssetCount
        ManifestPath = $runtimeManifestPath
    }
} finally {
    if (-not $completed -and (Test-Path -LiteralPath $stagingRoot)) {
        Assert-DirectChildPath -Parent $resolvedOutputParent -Child $stagingRoot
        Remove-Item -LiteralPath $stagingRoot -Recurse -Force
    }
}
