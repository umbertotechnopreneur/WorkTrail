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
Smoke-tests the packaged sensor helper using ordinary privileges and bounded IPC.
.DESCRIPTION
Does not install, activate or request elevation for PawnIO. It prints aggregate
counts only, never sensor names, device identifiers, serial numbers or raw reports.
#>
[CmdletBinding()]
param([Parameter(Mandatory)][string]$HelperPath)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$resolvedHelperPath = [IO.Path]::GetFullPath($HelperPath)
if (-not (Test-Path -LiteralPath $resolvedHelperPath -PathType Leaf)) { throw 'Hardware helper executable is missing.' }
$pipeName = 'WorkTrail.Hardware.' + [Guid]::NewGuid().ToString('N')
$pipe = [IO.Pipes.NamedPipeServerStream]::new($pipeName, [IO.Pipes.PipeDirection]::InOut, 1,
    [IO.Pipes.PipeTransmissionMode]::Byte, [IO.Pipes.PipeOptions]::Asynchronous -bor [IO.Pipes.PipeOptions]::CurrentUserOnly)
$child = $null

function Read-HardwareFrame {
    $deadline = [Threading.CancellationTokenSource]::new([TimeSpan]::FromSeconds(12))
    try {
        $header = [byte[]]::new(4)
        $pipe.ReadExactlyAsync([Memory[byte]]::new($header), $deadline.Token).AsTask().GetAwaiter().GetResult()
        $length = [BitConverter]::ToInt32($header, 0)
        if ($length -le 0 -or $length -gt 524288) { throw 'Invalid hardware frame length.' }
        $payload = [byte[]]::new($length)
        $pipe.ReadExactlyAsync([Memory[byte]]::new($payload), $deadline.Token).AsTask().GetAwaiter().GetResult()
        return ([Text.Encoding]::UTF8.GetString($payload) | ConvertFrom-Json -Depth 32)
    }
    finally { $deadline.Dispose() }
}

try {
    $startInfo = [Diagnostics.ProcessStartInfo]::new($resolvedHelperPath)
    $startInfo.WorkingDirectory = [IO.Path]::GetDirectoryName($resolvedHelperPath)
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true
    $startInfo.WindowStyle = [Diagnostics.ProcessWindowStyle]::Hidden
    foreach ($argument in @('--pipe', $pipeName, '--parent', [string]$PID)) { $startInfo.ArgumentList.Add($argument) }
    $child = [Diagnostics.Process]::Start($startInfo)
    $connectionDeadline = [Threading.CancellationTokenSource]::new([TimeSpan]::FromSeconds(12))
    try { $pipe.WaitForConnectionAsync($connectionDeadline.Token).GetAwaiter().GetResult() }
    finally { $connectionDeadline.Dispose() }
    $hello = Read-HardwareFrame
    if ($hello.Version -ne 2) { throw 'Hardware helper protocol version mismatch.' }

    $previousSampleTimes = @{}
    $profileIntervals = @{
        normal = @{ Cpu = 2; GpuIntel = 2; GpuNvidia = 2; GpuAmd = 2; Memory = 10; Battery = 30; Storage = 60; Network = 2 }
        fastest = @{ Cpu = 0.5; GpuIntel = 0.5; GpuNvidia = 0.5; GpuAmd = 0.5; Memory = 2; Battery = 5; Storage = 30; Network = 0.5 }
    }
    foreach ($sampleNumber in 1..3) {
        $samplingProfile = if ($sampleNumber -lt 3) { 'normal' } else { 'fastest' }
        $request = @{ Version = 2; Command = 'sample'; SamplingProfile = $samplingProfile } | ConvertTo-Json -Compress
        $payload = [Text.Encoding]::UTF8.GetBytes($request)
        $header = [BitConverter]::GetBytes([int]$payload.Length)
        $pipe.Write($header)
        $pipe.Write($payload)
        $pipe.Flush()
        $snapshot = Read-HardwareFrame
        if ($snapshot.Status -notin @('ready', 'partial')) { throw "Hardware smoke failed: $($snapshot.Status), $($snapshot.ErrorCode)." }
        if ($snapshot.DriverStatus -notin @('available', 'not-installed')) { throw 'Standard-mode smoke unexpectedly activated low-level access.' }
        $devices = @($snapshot.Devices)
        $sensors = @($devices | ForEach-Object { $_.Sensors })
        $validCount = @($sensors | Where-Object { $null -ne $_.Value }).Count
        if ($devices.Count -gt 64 -or $sensors.Count -gt 4096 -or $validCount -eq 0) { throw 'Hardware smoke returned invalid sensor cardinality.' }
        foreach ($device in $devices) {
            if (-not $profileIntervals[$samplingProfile].ContainsKey($device.Kind)) { throw 'Unexpected hardware sampling category.' }
            if ($previousSampleTimes.ContainsKey($device.Id)) {
                $previousTime = [DateTimeOffset]$previousSampleTimes[$device.Id]
                $currentTime = [DateTimeOffset]$device.SampledAt
                $due = $previousTime.AddSeconds($profileIntervals[$samplingProfile][$device.Kind])
                if ([DateTimeOffset]$snapshot.CollectionStartedAt -ge $due -and $currentTime -eq $previousTime) {
                    throw "Due category was not refreshed: $($device.Kind)."
                }
                if ([DateTimeOffset]$snapshot.Timestamp -lt $due -and $currentTime -ne $previousTime) {
                    throw "Category was polled before its configured interval: $($device.Kind)."
                }
            }
            $previousSampleTimes[$device.Id] = $device.SampledAt
        }
        [pscustomobject]@{
            Sample = $sampleNumber
            Profile = $samplingProfile
            Status = $snapshot.Status
            DriverStatus = $snapshot.DriverStatus
            Devices = $devices.Count
            Sensors = $sensors.Count
            UsableSensors = $validCount
            UsableBatterySensors = @($devices | Where-Object Kind -eq 'Battery' | ForEach-Object { $_.Sensors } | Where-Object { $null -ne $_.Value }).Count
            DeviceKinds = (@($devices.Kind | Sort-Object -Unique) -join ', ')
        }
        if ($sampleNumber -eq 1) { Start-Sleep -Milliseconds 2100 }
        elseif ($sampleNumber -eq 2) { Start-Sleep -Milliseconds 600 }
    }
}
finally {
    $pipe.Dispose()
    if ($null -ne $child) {
        try {
            # Only this exact, unelevated test child is eligible for cleanup.
            if (-not $child.WaitForExit(1500)) { $child.Kill() }
        }
        finally { $child.Dispose() }
    }
}
