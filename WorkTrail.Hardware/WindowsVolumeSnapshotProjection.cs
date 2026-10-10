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


using System;
using System.IO;
using WorkTrail.Services;

namespace WorkTrail.Hardware;

/// <summary>Maps basic Windows logical-volume readings without inventing physical-device or SMART data.</summary>
internal static class WindowsVolumeSnapshotProjection
{
    /// <summary>Preserves valid capacity and counters independently; unavailable or invalid readings remain null and report an error.</summary>
    internal static (HardwareDeviceSnapshot Device, bool HasError) Create(string volumeName, long? totalBytes,
        long? freeBytes, double? idlePercent, double? readBytesPerSecond, double? writeBytesPerSecond, DateTimeOffset sampledAt)
    {
        if (volumeName.Length != 2 || volumeName[0] is < 'A' or > 'Z' || volumeName[1] != ':' || sampledAt == default)
            throw new InvalidDataException("The Windows logical-volume identity or sample time is invalid.");

        var id = "/windows/volume/" + char.ToLowerInvariant(volumeName[0]);
        var validCapacity = totalBytes is > 0 && freeBytes is >= 0 && freeBytes <= totalBytes;
        var activity = idlePercent is >= 0 and <= 100 ? 100 - idlePercent : null;
        var read = readBytesPerSecond is >= 0 && double.IsFinite(readBytesPerSecond.Value) ? readBytesPerSecond : null;
        var write = writeBytesPerSecond is >= 0 && double.IsFinite(writeBytesPerSecond.Value) ? writeBytesPerSecond : null;
        const double bytesPerGibibyte = 1073741824;
        var sensors = new HardwareSensorSnapshot[]
        {
            new(id + "/load/activity", "Total Activity", "Load", "%", activity),
            new(id + "/load/used", "Used Space", "Load", "%", validCapacity ? (double)(totalBytes!.Value - freeBytes!.Value) / totalBytes.Value * 100 : null),
            new(id + "/data/total", "Total Space", "Data", "GiB", validCapacity ? totalBytes / bytesPerGibibyte : null),
            new(id + "/data/free", "Free Space", "Data", "GiB", validCapacity ? freeBytes / bytesPerGibibyte : null),
            new(id + "/throughput/read", "Read Rate", "Throughput", "B/s", read),
            new(id + "/throughput/write", "Write Rate", "Throughput", "B/s", write)
        };
        // The drive letter is an OS volume identity, not a physical disk model. Missing telemetry stays
        // explicitly partial; invalid occupancy/counter values are never clamped or replaced with zero.
        return (new HardwareDeviceSnapshot(id, volumeName, "Storage", sampledAt, Array.AsReadOnly(sensors)),
            !validCapacity || activity is null || read is null || write is null);
    }
}
