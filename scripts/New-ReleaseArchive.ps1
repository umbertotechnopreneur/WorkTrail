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
Creates an unsigned release archive with its matching Microsoft framework packages.
.DESCRIPTION
Consumes a completed PackageMsix output. Does not build, install, sign, or trust certificates.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$PackageDirectory,
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
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $artifactRoot "releases/$Version/$Platform"
}
$outputRoot = [IO.Path]::GetFullPath($OutputDirectory)
if (-not $outputRoot.StartsWith($artifactRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Release output must be a directory inside this repository artifacts/ directory.'
}
if (Test-Path -LiteralPath $outputRoot) {
    throw "Release output already exists; use a fresh output directory: $outputRoot"
}
for ($ancestor = $outputRoot; $ancestor.Length -ge $artifactRoot.Length; $ancestor = Split-Path -Parent $ancestor) {
    if ((Test-Path -LiteralPath $ancestor) -and
        ((Get-Item -LiteralPath $ancestor -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) {
        throw "Release output cannot pass through a symbolic link or junction: $ancestor"
    }
}

Add-Type -AssemblyName System.IO.Compression.FileSystem
function Read-Package {
    param([Parameter(Mandatory)][string]$Path)
    $archive = [IO.Compression.ZipFile]::OpenRead($Path)
    try {
        $entry = $archive.GetEntry('AppxManifest.xml')
        if ($null -eq $entry) { throw "Package has no manifest: $Path" }
        $reader = [IO.StreamReader]::new($entry.Open())
        try { [xml]$manifest = $reader.ReadToEnd() } finally { $reader.Dispose() }
        $identity = $manifest.SelectSingleNode('/*[local-name()="Package"]/*[local-name()="Identity"]')
        if ($null -eq $identity) { throw "Package has no identity: $Path" }
        [pscustomobject]@{
            Path = $Path
            Name = $identity.GetAttribute('Name')
            Publisher = $identity.GetAttribute('Publisher')
            Version = [version]$identity.GetAttribute('Version')
            Architecture = $identity.GetAttribute('ProcessorArchitecture')
            Signed = $null -ne $archive.GetEntry('AppxSignature.p7x')
            Manifest = $manifest
        }
    }
    finally { $archive.Dispose() }
}

$packageRoot = (Resolve-Path -LiteralPath $PackageDirectory).Path
$packageFiles = @(Get-ChildItem -LiteralPath $packageRoot -Recurse -File |
    Where-Object { $_.Extension -in @('.msix', '.appx') -and $_.FullName -notmatch '[\\/]Dependencies[\\/]' })
if ($packageFiles.Count -ne 1) { throw 'Expected exactly one application package in PackageDirectory.' }
$package = Read-Package -Path $packageFiles[0].FullName
if ($package.Signed) { throw 'This preparation path requires an unsigned package; signing and publication are separate release steps.' }
if ($package.Architecture -ine $Platform -or $package.Version -ne [version]"$Version.0") {
    throw 'Application package architecture/version does not match the requested release.'
}

$buildArchive = [IO.Compression.ZipFile]::OpenRead($package.Path)
try {
    $buildInfoEntry = $buildArchive.GetEntry('BuildInfo.json')
    if ($null -eq $buildInfoEntry) { throw 'Package has no BuildInfo.json.' }
    $reader = [IO.StreamReader]::new($buildInfoEntry.Open())
    try { $buildInfo = $reader.ReadToEnd() | ConvertFrom-Json } finally { $reader.Dispose() }
    if ($buildInfo.semVer -cne $Version -or $buildInfo.packageVersion -cne "$Version.0" -or
        $buildInfo.platform -ine $Platform -or $buildInfo.gitCommit -cnotmatch '^[0-9a-f]{40}$') {
        throw 'Packaged build information does not match the release.'
    }
}
finally { $buildArchive.Dispose() }

$dependenciesRoot = Join-Path $packageFiles[0].DirectoryName 'Dependencies'
$candidates = @()
if (Test-Path -LiteralPath $dependenciesRoot -PathType Container) {
    # The SDK also emits other architectures. Select by manifest identity, never by newest file.
    $candidates = @(Get-ChildItem -LiteralPath $dependenciesRoot -Recurse -File |
        Where-Object { $_.Extension -in @('.msix', '.appx') } |
        ForEach-Object { Read-Package -Path $_.FullName } |
        Where-Object { $_.Architecture -ieq $Platform -or $_.Architecture -ieq 'neutral' })
}
$selectedDependencies = @()
foreach ($requirement in $package.Manifest.SelectNodes('/*[local-name()="Package"]/*[local-name()="Dependencies"]/*[local-name()="PackageDependency"]')) {
    $matches = @($candidates | Where-Object {
        $_.Name -ceq $requirement.GetAttribute('Name') -and
        $_.Publisher -ceq $requirement.GetAttribute('Publisher') -and
        $_.Version -ge [version]$requirement.GetAttribute('MinVersion')
    })
    if ($matches.Count -ne 1) { throw "Missing or ambiguous framework dependency: $($requirement.GetAttribute('Name'))" }
    $dependency = $matches[0]
    $signature = Get-AuthenticodeSignature -LiteralPath $dependency.Path
    if (-not $dependency.Signed -or $signature.Status -ne 'Valid') {
        throw "Framework dependency signature is not valid: $($dependency.Name)"
    }
    $framework = $dependency.Manifest.SelectSingleNode('/*[local-name()="Package"]/*[local-name()="Properties"]/*[local-name()="Framework"]')
    if ($null -eq $framework -or $framework.InnerText -cne 'true') {
        throw "Dependency is not a framework package: $($dependency.Name)"
    }
    if ($dependency.Manifest.SelectNodes('/*[local-name()="Package"]/*[local-name()="Dependencies"]/*[local-name()="PackageDependency"]').Count -ne 0) {
        throw "Nested framework dependencies require explicit packaging support: $($dependency.Name)"
    }
    $selectedDependencies += $dependency
}

$archiveName = "WorkTrail-$Version-$Platform-unsigned"
$stage = Join-Path $outputRoot $archiveName
[void][IO.Directory]::CreateDirectory((Join-Path $stage 'Dependencies'))
$packageName = "$archiveName.msix"
Copy-Item -LiteralPath $package.Path -Destination (Join-Path $stage $packageName)
$dependencyRecords = @(
    foreach ($dependency in $selectedDependencies) {
        $relativePath = "Dependencies/$($dependency.Name)_$($dependency.Version)_$($dependency.Architecture)$([IO.Path]::GetExtension($dependency.Path))"
        Copy-Item -LiteralPath $dependency.Path -Destination (Join-Path $stage $relativePath)
        [ordered]@{ file = $relativePath; sha256 = (Get-FileHash -LiteralPath $dependency.Path -Algorithm SHA256).Hash }
    }
)
$release = [ordered]@{
    schemaVersion = 1
    version = $Version
    packageVersion = "$Version.0"
    platform = $Platform
    signing = 'unsigned'
    package = [ordered]@{ file = $packageName; sha256 = (Get-FileHash -LiteralPath $package.Path -Algorithm SHA256).Hash }
    dependencies = $dependencyRecords
}
$utf8 = [Text.UTF8Encoding]::new($false)
[IO.File]::WriteAllText((Join-Path $stage 'release.json'), ($release | ConvertTo-Json -Depth 5) + "`n", $utf8)
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Install-WorkTrailRelease.ps1') -Destination (Join-Path $stage 'Install.ps1')
foreach ($notice in @('LICENSE', 'TRADEMARKS.md', 'THIRD_PARTY_NOTICES.md')) {
    Copy-Item -LiteralPath (Join-Path $repositoryRoot $notice) -Destination (Join-Path $stage $notice)
}
$instructions = @"
WorkTrail $Version - $Platform - UNSIGNED BUILD

This is a preparation artifact, not an installable public release.
The application package must be signed before installation. Install.ps1 refuses unsigned builds.
Do not import a development certificate or disable Windows signature checks to install this archive.
The matching Microsoft-signed framework packages are included in Dependencies/.

Source commit: $($buildInfo.gitCommit)
Source had local changes: $($buildInfo.gitDirty)
Package version: $Version.0

After the release has been signed and its metadata/checksums regenerated, install from PowerShell 7:
pwsh -NoProfile -File ./Install.ps1
Use -ForceApplicationShutdown only when ready to close an existing WorkTrail instance.
On x64, Install.ps1 also installs the included PawnIO advanced-sensor component if needed.
Accept the Windows administrator prompt. If setup requests a restart, restart Windows.
Direct MSIX users can install the same component from Sensors options.

SHA256SUMS.txt covers the payload files. The adjacent ZIP.sha256 covers this archive.
"@
[IO.File]::WriteAllText((Join-Path $stage 'README.txt'), $instructions + "`n", $utf8)
$hashLines = @(Get-ChildItem -LiteralPath $stage -Recurse -File | Sort-Object FullName | ForEach-Object {
    '{0}  {1}' -f (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash, [IO.Path]::GetRelativePath($stage, $_.FullName).Replace('\', '/')
})
[IO.File]::WriteAllText((Join-Path $stage 'SHA256SUMS.txt'), ($hashLines -join "`n") + "`n", $utf8)
$zipPath = Join-Path $outputRoot "$archiveName.zip"
[IO.Compression.ZipFile]::CreateFromDirectory($stage, $zipPath, [IO.Compression.CompressionLevel]::NoCompression, $false)
$zipHash = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash
[IO.File]::WriteAllText("$zipPath.sha256", "$zipHash  $archiveName.zip`n", $utf8)
Write-Host "Unsigned release archive ready: $zipPath"
