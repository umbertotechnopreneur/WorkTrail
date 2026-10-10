#requires -Version 7.0
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

<#
.SYNOPSIS
Creates an unsigned portable ZIP from a completed self-contained publish.
.DESCRIPTION
Consumes Release-Unpackaged output. Does not build, install, sign, or change trust.
Application data continues to use the normal Windows user profile.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$PublishDirectory,
    [Parameter(Mandatory)][string]$Version,
    [Parameter(Mandatory)][ValidateSet('x64', 'ARM64')][string]$Platform,
    [string]$OutputDirectory
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if ($Version -cnotmatch '^[1-9][0-9]{0,4}\.(0|[1-9][0-9]{0,4})\.(0|[1-9][0-9]{0,4})$' -or
    @($Version.Split('.') | Where-Object { [int]$_ -gt 65534 }).Count -gt 0) {
    throw 'Version must be X.Y.Z with major 1..65534 and minor/patch 0..65534.'
}

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$artifactRoot = [IO.Path]::GetFullPath((Join-Path $repositoryRoot 'artifacts'))
$Platform = if ($Platform -ieq 'x64') { 'x64' } else { 'ARM64' }
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $artifactRoot "releases/$Version/$Platform/portable"
}
$outputRoot = [IO.Path]::TrimEndingDirectorySeparator([IO.Path]::GetFullPath($OutputDirectory))
$publishRoot = [IO.Path]::TrimEndingDirectorySeparator([IO.Path]::GetFullPath($PublishDirectory))
$separator = [IO.Path]::DirectorySeparatorChar
if (-not $outputRoot.StartsWith($artifactRoot + $separator, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Portable release output must be a directory inside this repository artifacts/ directory.'
}
if ($outputRoot.Equals($publishRoot, [StringComparison]::OrdinalIgnoreCase) -or
    $outputRoot.StartsWith($publishRoot + $separator, [StringComparison]::OrdinalIgnoreCase) -or
    $publishRoot.StartsWith($outputRoot + $separator, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Publish and portable release output directories must not overlap.'
}
if (Test-Path -LiteralPath $outputRoot) {
    throw "Portable release output already exists; use a fresh output directory: $outputRoot"
}
foreach ($path in @($publishRoot, $outputRoot)) {
    for ($ancestor = $path; -not [string]::IsNullOrEmpty($ancestor); $ancestor = Split-Path -Parent $ancestor) {
        if ((Test-Path -LiteralPath $ancestor) -and
            ((Get-Item -LiteralPath $ancestor -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) {
            throw "Portable release paths cannot pass through a symbolic link or junction: $ancestor"
        }
    }
}
if (-not (Test-Path -LiteralPath $publishRoot -PathType Container)) {
    throw 'PublishDirectory must be an existing self-contained publish directory.'
}
$publishItems = @(Get-ChildItem -LiteralPath $publishRoot -Recurse -Force)
if (@($publishItems | Where-Object { $_.Attributes -band [IO.FileAttributes]::ReparsePoint }).Count -gt 0) {
    throw 'PublishDirectory cannot contain symbolic links or junctions.'
}
$publishFiles = @($publishItems | Where-Object { -not $_.PSIsContainer } | Sort-Object FullName)
$generatedNames = @('README.txt', 'release.json', 'SHA256SUMS.txt', 'LICENSE', 'TRADEMARKS.md', 'THIRD_PARTY_NOTICES.md')
foreach ($name in $generatedNames) {
    if (Test-Path -LiteralPath (Join-Path $publishRoot $name)) {
        throw "PublishDirectory contains a reserved release filename: $name"
    }
}
foreach ($requiredFile in @(
    'WorkTrail.exe', 'WorkTrail.dll', 'WorkTrail.deps.json', 'WorkTrail.runtimeconfig.json', 'BuildInfo.json',
    'hostfxr.dll', 'hostpolicy.dll', 'coreclr.dll', 'System.Private.CoreLib.dll',
    'Microsoft.ui.xaml.dll', 'Microsoft.WindowsAppRuntime.dll', 'Microsoft.Windows.ApplicationModel.Resources.dll',
    'WorkTrail.pri'
)) {
    $requiredPath = Join-Path $publishRoot $requiredFile
    if (-not (Test-Path -LiteralPath $requiredPath -PathType Leaf) -or (Get-Item -LiteralPath $requiredPath).Length -eq 0) {
        throw "Missing or empty required portable publish file: $requiredFile"
    }
}
foreach ($notice in @('LICENSE', 'TRADEMARKS.md', 'THIRD_PARTY_NOTICES.md')) {
    $noticePath = Join-Path $repositoryRoot $notice
    $noticeItem = Get-Item -LiteralPath $noticePath -Force
    if ($noticeItem.PSIsContainer -or $noticeItem.Length -eq 0 -or ($noticeItem.Attributes -band [IO.FileAttributes]::ReparsePoint)) {
        throw "Invalid repository release notice: $notice"
    }
}

$buildInfo = Get-Content -LiteralPath (Join-Path $publishRoot 'BuildInfo.json') -Raw | ConvertFrom-Json -AsHashtable
if ($buildInfo.semVer -cne $Version -or $buildInfo.packageVersion -cne "$Version.0" -or
    $buildInfo.platform -cne $Platform -or $buildInfo.configuration -cne 'Release-Unpackaged' -or
    $buildInfo.runtimeIdentifier -cne "win-$($Platform.ToLowerInvariant())" -or
    $buildInfo.gitCommit -cnotmatch '^[0-9a-f]{40}$' -or $buildInfo.gitDirty -isnot [bool]) {
    throw 'Portable build information does not match the release version, platform, configuration, or source commit.'
}
$runtimeConfig = Get-Content -LiteralPath (Join-Path $publishRoot 'WorkTrail.runtimeconfig.json') -Raw | ConvertFrom-Json -AsHashtable
$runtimeOptions = $runtimeConfig.runtimeOptions
if ($null -eq $runtimeOptions -or $runtimeOptions.ContainsKey('framework') -or $runtimeOptions.ContainsKey('frameworks') -or
    -not $runtimeOptions.ContainsKey('includedFrameworks') -or
    'Microsoft.NETCore.App' -notin @($runtimeOptions.includedFrameworks | ForEach-Object { $_.name })) {
    throw 'Portable runtime configuration must describe a self-contained .NET application.'
}

function Assert-PortableArchitecture {
    param([string]$FileName)

    $reader = [IO.BinaryReader]::new([IO.File]::OpenRead((Join-Path $publishRoot $FileName)))
    try {
        if ($reader.BaseStream.Length -lt 64 -or $reader.ReadUInt16() -ne 0x5A4D) { throw "Invalid PE file: $FileName" }
        $reader.BaseStream.Position = 0x3C
        $headerOffset = $reader.ReadUInt32()
        if ($headerOffset -lt 64 -or $headerOffset -gt ($reader.BaseStream.Length - 6)) { throw "Invalid PE header: $FileName" }
        $reader.BaseStream.Position = $headerOffset
        if ($reader.ReadUInt32() -ne 0x00004550) { throw "Invalid PE signature: $FileName" }
        $expectedMachine = if ($Platform -ceq 'x64') { 0x8664 } else { 0xAA64 }
        if ($reader.ReadUInt16() -ne $expectedMachine) {
            throw "Portable binary architecture does not match ${Platform}: $FileName"
        }
    }
    finally { $reader.Dispose() }
}
foreach ($binary in @('WorkTrail.exe', 'hostfxr.dll', 'coreclr.dll')) { Assert-PortableArchitecture -FileName $binary }

$archiveName = "WorkTrail-$Version-$Platform-portable-unsigned.zip"
$zipPath = Join-Path $outputRoot $archiveName
$utf8 = [Text.UTF8Encoding]::new($false)
$hashLines = [Collections.Generic.List[string]]::new()
$release = [ordered]@{
    schemaVersion = 1
    format = 'portable'
    version = $Version
    packageVersion = "$Version.0"
    platform = $Platform
    runtimeIdentifier = $buildInfo.runtimeIdentifier
    configuration = $buildInfo.configuration
    signing = 'unsigned'
    entryPoint = 'WorkTrail.exe'
    gitCommit = $buildInfo.gitCommit
    gitDirty = $buildInfo.gitDirty
}
$instructions = @"
WorkTrail $Version - $Platform - PORTABLE UNSIGNED BUILD

Extract the entire ZIP into a folder and run WorkTrail.exe from that folder.
Keep all DLLs, resources, and subfolders beside the executable; do not copy only the EXE.
Use the archive matching your Windows architecture ($Platform).
Basic startup needs no MSIX installation, administrator access, or certificate import.
No CLI alias is registered. On x64, the included PawnIO driver can be installed offline
from Sensors options with Windows administrator consent for advanced sensor access.
The shared driver remains installed after this portable folder is removed.
This unsigned prerelease preparation artifact may trigger Windows security prompts.
Follow your organization's policy for unsigned software; do not disable security checks.

The .NET and Windows App SDK runtimes are included.
On-device screenshot OCR requires the MSIX edition; it is unavailable in this portable build.
OCR is initialized on extraction, not basic startup. An OCR failure is recorded on
the capture without discarding the screenshot or terminating the application.
This feature limitation does not by itself prevent basic application startup.

Portable describes application deployment, not a portable user profile.
Settings, screenshots, history, and other application data use the normal LocalAppData
storage (or the data location already configured in WorkTrail), outside this folder.
Close WorkTrail before moving or deleting the extracted folder. Deleting it does not
delete application data. Startup integration remains an explicit application setting.

Source commit: $($buildInfo.gitCommit)
Source had local changes: $($buildInfo.gitDirty)
Version: $Version

SHA256SUMS.txt covers every payload file except the checksum list itself.
The adjacent ZIP.sha256 covers the archive. No files are signed by this script.
"@

# Validate everything before writing. A packaging I/O failure is fatal and leaves its
# partial output for inspection; reruns require a fresh directory instead of overwriting.
[void][IO.Directory]::CreateDirectory($outputRoot)
$archive = [IO.Compression.ZipFile]::Open($zipPath, [IO.Compression.ZipArchiveMode]::Create)
try {
    foreach ($file in $publishFiles) {
        $relativePath = [IO.Path]::GetRelativePath($publishRoot, $file.FullName).Replace('\', '/')
        $source = [IO.File]::OpenRead($file.FullName)
        try {
            $destination = $archive.CreateEntry($relativePath, [IO.Compression.CompressionLevel]::Optimal).Open()
            try { $source.CopyTo($destination) } finally { $destination.Dispose() }
            $source.Position = 0
            $hash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($source))
            $hashLines.Add("$hash  $relativePath")
        }
        finally { $source.Dispose() }
    }
    foreach ($directory in @($publishItems | Where-Object { $_.PSIsContainer })) {
        if (@(Get-ChildItem -LiteralPath $directory.FullName -Force).Count -eq 0) {
            [void]$archive.CreateEntry([IO.Path]::GetRelativePath($publishRoot, $directory.FullName).Replace('\', '/') + '/')
        }
    }
    $documents = @{
        'README.txt' = $utf8.GetBytes($instructions + "`n")
        'release.json' = $utf8.GetBytes(($release | ConvertTo-Json -Depth 5) + "`n")
    }
    foreach ($notice in @('LICENSE', 'TRADEMARKS.md', 'THIRD_PARTY_NOTICES.md')) {
        $documents[$notice] = [IO.File]::ReadAllBytes((Join-Path $repositoryRoot $notice))
    }
    foreach ($name in @($documents.Keys | Sort-Object)) {
        $bytes = $documents[$name]
        $destination = $archive.CreateEntry($name, [IO.Compression.CompressionLevel]::Optimal).Open()
        try { $destination.Write($bytes, 0, $bytes.Length) } finally { $destination.Dispose() }
        $hash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($bytes))
        $hashLines.Add("$hash  $name")
    }
    $checksums = $utf8.GetBytes(($hashLines -join "`n") + "`n")
    $destination = $archive.CreateEntry('SHA256SUMS.txt').Open()
    try { $destination.Write($checksums, 0, $checksums.Length) } finally { $destination.Dispose() }
}
finally { $archive.Dispose() }
$zipHash = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash
[IO.File]::WriteAllText("$zipPath.sha256", "$zipHash  $archiveName`n", $utf8)
Write-Host "Unsigned portable release archive ready: $zipPath"
