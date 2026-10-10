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
Restores the audited LibreHardwareMonitor source archive for compilation.
.DESCRIPTION
Only the pinned, SHA-256-verified archive is accepted. Downloads and extracted
sources remain under the dedicated project's obj directory; no driver is installed.
#>
[CmdletBinding()]
param([Parameter(Mandatory)][string]$Destination)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$revision = '3d331e3370efb858411f19511373eff65a218701'
$expectedHash = '5A83EE3F504A85EFB6AFEE4112447E60CACA1B7EC2E2D71F4651570B8B9B4230'
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$allowedRoot = [IO.Path]::GetFullPath((Join-Path $repositoryRoot 'WorkTrail.Hardware/LibreHardwareMonitor/obj'))
$resolvedDestination = [IO.Path]::GetFullPath($Destination)
if (-not $resolvedDestination.StartsWith($allowedRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'LibreHardwareMonitor sources must remain inside the dedicated project obj directory.'
}

$sourceRoot = Join-Path $resolvedDestination "LibreHardwareMonitor-$revision"
$markerPath = Join-Path $resolvedDestination 'verified-v5.sha256'
$sensorPath = Join-Path $sourceRoot 'LibreHardwareMonitorLib/Hardware/Sensor.cs'
if ((Test-Path -LiteralPath $markerPath) -and
    (Test-Path -LiteralPath (Join-Path $sourceRoot 'LibreHardwareMonitorLib/Hardware/Computer.cs')) -and
    (Test-Path -LiteralPath $sensorPath) -and
    ([IO.File]::ReadAllText($sensorPath).Contains('WorkTrailUpdateSequence')) -and
    ([IO.File]::ReadAllText($markerPath).Trim() -ceq $expectedHash)) {
    return
}

[void][IO.Directory]::CreateDirectory($resolvedDestination)
$archivePath = Join-Path $resolvedDestination 'source.zip'
if (-not (Test-Path -LiteralPath $archivePath)) {
    # A failed or interrupted download is never used as a source tree.
    $response = Invoke-WebRequest -Uri "https://codeload.github.com/LibreHardwareMonitor/LibreHardwareMonitor/zip/$revision"
    $bytes = [byte[]]$response.Content
    if ([Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($bytes)) -cne $expectedHash) {
        throw 'LibreHardwareMonitor source archive checksum mismatch.'
    }
    [IO.File]::WriteAllBytes($archivePath, $bytes)
}
if ((Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash -cne $expectedHash) {
    throw 'Cached LibreHardwareMonitor archive checksum mismatch; remove only this project obj directory before retrying.'
}

# Zip entries are validated before extraction to prevent escaping the dedicated cache.
$archive = [IO.Compression.ZipFile]::OpenRead($archivePath)
try {
    foreach ($entry in $archive.Entries) {
        $entryPath = [IO.Path]::GetFullPath((Join-Path $resolvedDestination $entry.FullName))
        if (-not $entryPath.StartsWith($resolvedDestination + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
            throw 'The source archive contains an invalid entry path.'
        }
    }
}
finally { $archive.Dispose() }
[IO.Compression.ZipFile]::ExtractToDirectory($archivePath, $resolvedDestination, $true)

$pawnIoPath = Join-Path $sourceRoot 'LibreHardwareMonitorLib/PawnIo/PawnIo.cs'
$pawnIoSource = [IO.File]::ReadAllText($pawnIoPath).Replace("`r`n", "`n")
$patches = @(
    @{ Before = "public class PawnIo`n{"; After = "public class PawnIo`n{`n    // WorkTrail: low-level access requires explicit advanced-mode opt-in.`n    public static bool IsDriverAccessEnabled { get; set; }" },
    @{ Before = '        SafeFileHandle handle = PInvoke.CreateFile'; After = "        if (!IsDriverAccessEnabled) return new PawnIo(null);`n        SafeFileHandle handle = PInvoke.CreateFile" },
    @{ Before = '        return new long[outLength];'; After = '        throw new InvalidOperationException("PawnIO module unavailable or hardware read failed.");' },
    @{ Before = "            returnSize = 0;`n            return 0;"; After = "            returnSize = 0;`n            return unchecked((int)0x80070006);" }
)
foreach ($patch in $patches) {
    if ($pawnIoSource.Split([string[]]@($patch.Before), [StringSplitOptions]::None).Count -ne 2) {
        throw "The audited PawnIO wrapper patch no longer matches the pinned source: $($patch.Before)"
    }
    $pawnIoSource = $pawnIoSource.Replace($patch.Before, $patch.After)
}
[IO.File]::WriteAllText($pawnIoPath, $pawnIoSource, [Text.UTF8Encoding]::new($false))

$sensorSource = [IO.File]::ReadAllText($sensorPath)
$sensorPatches = @(
    @{ Before = '    public virtual float? Value'; After = "    // WorkTrail: distinguish a new assignment from a cached value after failed polling.`n    public long WorkTrailUpdateSequence { get; private set; }`n`n    public virtual float? Value" },
    @{ Before = '            _currentValue = value;'; After = "            WorkTrailUpdateSequence++;`n            _currentValue = value;" }
)
foreach ($patch in $sensorPatches) {
    if ($sensorSource.Split([string[]]@($patch.Before), [StringSplitOptions]::None).Count -ne 2) {
        throw 'The audited sensor update-sequence patch no longer matches the pinned source.'
    }
    $sensorSource = $sensorSource.Replace($patch.Before, $patch.After)
}
[IO.File]::WriteAllText($sensorPath, $sensorSource, [Text.UTF8Encoding]::new($false))

$cpuGroupPath = Join-Path $sourceRoot 'LibreHardwareMonitorLib/Hardware/Cpu/CpuGroup.cs'
$cpuGroupSource = [IO.File]::ReadAllText($cpuGroupPath)
$cpuBefore = '            switch (threads[0].Vendor)'
$cpuAfter = @'
            // WorkTrail: AMD low-level constructors require PawnIO; retain library CPU load without it.
            if (threads[0].Vendor == Vendor.AMD && !LibreHardwareMonitor.PawnIo.PawnIo.IsDriverAccessEnabled)
            {
                _hardware.Add(new GenericCpu(index++, coreThreads, settings));
                continue;
            }
            switch (threads[0].Vendor)
'@
if ($cpuGroupSource.Split([string[]]@($cpuBefore), [StringSplitOptions]::None).Count -ne 2) {
    throw 'The audited optional-driver CPU patch no longer matches the pinned source.'
}
[IO.File]::WriteAllText($cpuGroupPath, $cpuGroupSource.Replace($cpuBefore, $cpuAfter), [Text.UTF8Encoding]::new($false))

$nvidiaPath = Join-Path $sourceRoot 'LibreHardwareMonitorLib/Hardware/Gpu/NvidiaGpu.cs'
$nvidiaSource = [IO.File]::ReadAllText($nvidiaPath).Replace("`r`n", "`n")
$nvidiaPatches = @(
    @{
        Before = '        // Power.'
        After = @'
        // WorkTrail: reserve every native utilization index, including domains absent on this GPU.
        // Memory, power and D3D loads must have distinct identifiers or the snapshot is invalid.
        int memoryLoadIndex = Enum.GetValues<NvApi.NvUtilizationDomain>().Max(domain => (int)domain) + 1;
        int powerLoadIndex = memoryLoadIndex + 1;

        // Power.
'@
    },
    @{ Before = 'i + (_loads?.Length ?? 0), SensorType.Load'; After = 'i + powerLoadIndex, SensorType.Load' },
    @{
        Before = "                                        int sensorCount = (_loads?.Length ?? 0) + (_powers?.Length ?? 0);`n                                        int loadSensorIndex = sensorCount > 0 ? sensorCount + 1 : 0;"
        After = '                                        int loadSensorIndex = powerLoadIndex + (_powers?.Length ?? 0);'
    },
    @{ Before = 'new Sensor("GPU Memory", 3, SensorType.Load'; After = 'new Sensor("GPU Memory", memoryLoadIndex, SensorType.Load' }
)
foreach ($patch in $nvidiaPatches) {
    if ($nvidiaSource.Split([string[]]@($patch.Before), [StringSplitOptions]::None).Count -ne 2) {
        # Reject upstream drift instead of publishing an incompletely patched collector.
        throw 'The audited NVIDIA sensor identity patch no longer matches the pinned source.'
    }
    $nvidiaSource = $nvidiaSource.Replace($patch.Before, $patch.After)
}
[IO.File]::WriteAllText($nvidiaPath, $nvidiaSource, [Text.UTF8Encoding]::new($false))
[IO.File]::WriteAllText($markerPath, $expectedHash)
