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
#
# VBWR E

[CmdletBinding(SupportsShouldProcess)]
param(
    [switch]$Fix
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$expectedHeader = '// SPDX-License-Identifier: MIT'
$protectedThirdPartyHeaders = @{
    'WorkTrail.Hardware/LibreHardwareMonitor/MemoryGroup.cs' = '// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.'
}
$repositoryRoot = (& git rev-parse --show-toplevel).Trim()

if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($repositoryRoot)) {
    throw 'Unable to resolve the Git repository root.'
}

$trackedPaths = @(& git -C $repositoryRoot ls-files -- '*.cs')
if ($LASTEXITCODE -ne 0) {
    throw 'Unable to enumerate tracked C# source files.'
}

$untrackedPaths = @(& git -C $repositoryRoot ls-files --others --exclude-standard -- '*.cs')
if ($LASTEXITCODE -ne 0) {
    throw 'Unable to enumerate untracked C# source files.'
}

$sourcePaths = @($trackedPaths + $untrackedPaths) |
    Where-Object { $_ -and $_ -notmatch '(^|/)(bin|obj|artifacts|\.vs)/' } |
    Sort-Object -Unique

$missingHeaders = [System.Collections.Generic.List[string]]::new()
$updatedFiles = [System.Collections.Generic.List[string]]::new()
$existingFileCount = 0

foreach ($relativePath in $sourcePaths) {
    $fullPath = Join-Path $repositoryRoot ($relativePath -replace '/', [System.IO.Path]::DirectorySeparatorChar)
    if (-not (Test-Path -LiteralPath $fullPath -PathType Leaf)) {
        continue
    }

    $existingFileCount++

    $bytes = [System.IO.File]::ReadAllBytes($fullPath)
    $bomLength = if (
        $bytes.Length -ge 3 -and
        $bytes[0] -eq 0xEF -and
        $bytes[1] -eq 0xBB -and
        $bytes[2] -eq 0xBF
    ) {
        3
    }
    else {
        0
    }

    $probeLength = [Math]::Min(512, $bytes.Length - $bomLength)
    $probe = if ($probeLength -gt 0) {
        [System.Text.Encoding]::UTF8.GetString($bytes, $bomLength, $probeLength)
    }
    else {
        ''
    }

    if ($protectedThirdPartyHeaders.ContainsKey($relativePath)) {
        if (-not $probe.StartsWith($protectedThirdPartyHeaders[$relativePath], [System.StringComparison]::Ordinal)) {
            # Never relicense adapted upstream code, even when the caller explicitly requests -Fix.
            throw "Protected third-party license header is invalid: $relativePath. Restore its original MPL notice manually."
        }
        continue
    }

    if ($probe.StartsWith($expectedHeader, [System.StringComparison]::Ordinal)) {
        continue
    }

    $missingHeaders.Add($relativePath)
    if (-not $Fix) {
        continue
    }

    $newLine = if ($probe.Contains("`r`n", [System.StringComparison]::Ordinal)) {
        "`r`n"
    }
    else {
        "`n"
    }

    if (-not $PSCmdlet.ShouldProcess($fullPath, 'Add SPDX MIT source header')) {
        continue
    }

    $headerBytes = [System.Text.Encoding]::UTF8.GetBytes($expectedHeader + $newLine + $newLine)
    $updatedBytes = [byte[]]::new($bytes.Length + $headerBytes.Length)

    if ($bomLength -gt 0) {
        [System.Buffer]::BlockCopy($bytes, 0, $updatedBytes, 0, $bomLength)
    }

    [System.Buffer]::BlockCopy($headerBytes, 0, $updatedBytes, $bomLength, $headerBytes.Length)
    [System.Buffer]::BlockCopy(
        $bytes,
        $bomLength,
        $updatedBytes,
        $bomLength + $headerBytes.Length,
        $bytes.Length - $bomLength)

    [System.IO.File]::WriteAllBytes($fullPath, $updatedBytes)
    $updatedFiles.Add($relativePath)
}

if ($Fix) {
    Write-Output "Added the SPDX MIT header to $($updatedFiles.Count) C# source files."
    exit 0
}

if ($missingHeaders.Count -gt 0) {
    $missingHeaders | ForEach-Object { Write-Error "Missing SPDX MIT header: $_" -ErrorAction Continue }
    throw "$($missingHeaders.Count) C# source files are missing the SPDX MIT header. Run this script with -Fix."
}

Write-Output "Verified the SPDX MIT header in $existingFileCount C# source files."
