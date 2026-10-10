// SPDX-License-Identifier: MIT
/* VBWR B
 *
 * Project: WorkTrail
 * Repository: https://github.com/umbertotechnopreneur/WorkTrail
 * Creator: Umberto Giacobbi | https://umbertogiacobbi.biz
 *
 * VibeWare: Human intent, AI execution, and plenty of tokens
 * Manifesto: https://umbertogiacobbi.biz/vibeware/manifesto
 *
 * Modified with AI: OpenAI Codex; added this header on 2026-10-10.
 * Human guidance: Umberto Giacobbi; requested VibeWare branding.
 *
 * Copyright (c) 2026 Umberto Giacobbi
 * License: MIT - see LICENSE
 *
 * VBWR E */


using System.Globalization;
using System.Management;
using System.Runtime.InteropServices;
using WorkTrail.Services;

namespace WorkTrail.Hardware;

/// <summary>Collects documented basic logical-volume metrics when the library cannot discover physical disks.</summary>
internal sealed class WindowsStorageTelemetryCollector
{
    private WindowsStorageSample? _cached;

    /// <summary>Reads fixed ready volumes at the configured storage cadence, retaining the actual cached timestamps.</summary>
    internal WindowsStorageSample Sample(HardwareSamplingProfile profile)
    {
        if (_cached is not null && !profile.IsSampleDue("Storage", _cached.SampledAt, DateTimeOffset.UtcNow))
            return _cached;

        var volumes = new List<(string Name, long? Total, long? Free)>();
        var error = false;
        try
        {
            foreach (var drive in DriveInfo.GetDrives().OrderBy(drive => drive.Name, StringComparer.Ordinal))
            {
                try
                {
                    if (drive.DriveType != DriveType.Fixed || !drive.IsReady) continue;
                    var name = drive.Name.TrimEnd('\\');
                    try { volumes.Add((name, drive.TotalSize, drive.TotalFreeSpace)); }
                    catch (Exception exception) when (IsVolumeFailure(exception))
                    {
                        // Keep the known ready volume even when capacity access fails; counters can still be valid.
                        volumes.Add((name, null, null));
                        error = true;
                    }
                }
                catch (Exception exception) when (IsVolumeFailure(exception))
                {
                    // An unreadable volume is a reported partial collection, never an invented empty drive.
                    error = true;
                }
            }
        }
        catch (Exception exception) when (IsVolumeFailure(exception))
        {
            // Enumeration failure is exposed in the snapshot diagnostic; no cached capacities are relabeled as fresh.
            error = true;
        }

        var performance = new Dictionary<string, VolumePerformance>(StringComparer.OrdinalIgnoreCase);
        if (volumes.Count > 0)
        {
            try { performance = ReadPerformance(); }
            catch (Exception exception) when (exception is ManagementException or COMException or UnauthorizedAccessException
                or TimeoutException or InvalidDataException or FormatException or OverflowException)
            {
                // WMI can be disabled or inaccessible: capacity remains usable, while activity/rates stay null.
                error = true;
            }
        }

        var sampledAt = DateTimeOffset.UtcNow;
        var devices = new List<HardwareDeviceSnapshot>();
        foreach (var volume in volumes)
        {
            performance.TryGetValue(volume.Name, out var counters);
            var projected = WindowsVolumeSnapshotProjection.Create(volume.Name, volume.Total, volume.Free,
                counters?.Idle, counters?.Read, counters?.Write, sampledAt);
            devices.Add(projected.Device);
            error |= projected.HasError;
        }
        _cached = new WindowsStorageSample(sampledAt, devices.AsReadOnly(), error);
        return _cached;
    }

    private static Dictionary<string, VolumePerformance> ReadPerformance()
    {
        // Query only volume names and aggregate performance counters; no filenames, labels or user data are read.
        // The WMI timeout bounds each enumeration wait inside the existing helper's operation deadline.
        using var searcher = new ManagementObjectSearcher(new ManagementScope(@"\\.\root\cimv2"),
            new ObjectQuery("SELECT Name, PercentIdleTime, DiskReadBytesPerSec, DiskWriteBytesPerSec FROM Win32_PerfFormattedData_PerfDisk_LogicalDisk"),
            new System.Management.EnumerationOptions { ReturnImmediately = true, Rewindable = false, Timeout = TimeSpan.FromSeconds(3) });
        using var results = searcher.Get();
        var counters = new Dictionary<string, VolumePerformance>(StringComparer.OrdinalIgnoreCase);
        foreach (ManagementBaseObject row in results)
        {
            using (row)
            {
                var name = row["Name"] as string;
                if (name is null || name.Length != 2 || name[1] != ':') continue;
                if (!counters.TryAdd(name, new VolumePerformance(Number(row["PercentIdleTime"]),
                    Number(row["DiskReadBytesPerSec"]), Number(row["DiskWriteBytesPerSec"]))))
                    throw new InvalidDataException("Windows returned duplicate logical-volume counters.");
            }
        }
        return counters;
    }

    private static double? Number(object? value) => value is null or DBNull ? null : Convert.ToDouble(value, CultureInfo.InvariantCulture);

    private static bool IsVolumeFailure(Exception exception) => exception is IOException or UnauthorizedAccessException;

    private sealed record VolumePerformance(double? Idle, double? Read, double? Write);
}

/// <summary>Retains basic storage samples and their failure state without changing timestamps between polls.</summary>
internal sealed record WindowsStorageSample(DateTimeOffset SampledAt, IReadOnlyList<HardwareDeviceSnapshot> Devices, bool HasError);
