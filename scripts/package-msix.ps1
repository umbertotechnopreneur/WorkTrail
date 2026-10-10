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
Builds one signed or explicitly unsigned WorkTrail MSIX package.

.DESCRIPTION
Uses the shared MeUp packaging contract without sharing implementation files:
artifacts/msix/<debug|store>/<version>/<architecture>. A signed package requires
an explicit certificate thumbprint; the delegated build rejects a certificate
whose subject differs from the tracked MSIX manifest publisher.

.EXAMPLE
pwsh -NoProfile -File .\scripts\package-msix.ps1 -CertificateThumbprint <thumbprint>

.EXAMPLE
pwsh -NoProfile -File .\scripts\package-msix.ps1 -Channel Store -Architecture arm64 -Version 1.2.3 -CertificateThumbprint <thumbprint>
#>
[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Store')]
    [string]$Channel = 'Debug',

    [ValidateSet('x64', 'arm64')]
    [string]$Architecture = 'x64',

    [string]$Version,

    [string]$CertificateThumbprint,

    [switch]$Unsigned
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if ($Unsigned -and -not [string]::IsNullOrWhiteSpace($CertificateThumbprint)) {
    throw 'Unsigned packaging cannot use CertificateThumbprint.'
}

if (-not $Unsigned -and [string]::IsNullOrWhiteSpace($CertificateThumbprint)) {
    throw 'Signed MSIX packaging requires CertificateThumbprint. It must identify a certificate whose Subject matches WorkTrail\Package.appxmanifest.'
}

$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$configuration = if ($Channel -eq 'Debug') { 'Debug' } else { 'Release' }
$platform = if ($Architecture -eq 'arm64') { 'ARM64' } else { 'x64' }
$versionDirectory = if ([string]::IsNullOrWhiteSpace($Version)) { 'local' } else { $Version }
$packageOutputPath = Join-Path $repositoryRoot "artifacts\msix\$($Channel.ToLowerInvariant())\$versionDirectory\$Architecture"

$arguments = @(
    '-NoProfile',
    '-NonInteractive',
    '-File',
    (Join-Path $PSScriptRoot 'WorkTrail.ps1'),
    '-Action',
    'PackageMsix',
    '-Platform',
    $platform,
    '-Configuration',
    $configuration,
    '-PackageOutputPath',
    $packageOutputPath
)

if (-not [string]::IsNullOrWhiteSpace($Version)) {
    $arguments += @('-ReleaseVersion', $Version)
}

if ($Unsigned) {
    $arguments += '-Unsigned'
}
else {
    $arguments += @('-PackageCertificateThumbprint', $CertificateThumbprint)
}

& pwsh @arguments
if ($LASTEXITCODE -ne 0) {
    throw "MSIX packaging failed with exit code $LASTEXITCODE."
}
