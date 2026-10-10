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


namespace WorkTrail.Services;

/// <summary>Configures optional collection and the saved preference for requesting advanced access at startup.</summary>
public sealed record HardwareTelemetryConfiguration(bool Enabled = true, bool UseAdvancedSensors = false, string SamplingProfile = "normal");

/// <summary>Contains one immutable library reading; a null value means no usable measurement.</summary>
public sealed record HardwareSensorSnapshot(string Id, string Name, string Kind, string Unit, double? Value);

/// <summary>Groups sensor readings by device and preserves the time of that device's collection.</summary>
public sealed record HardwareDeviceSnapshot(
    string Id,
    string Name,
    string Kind,
    DateTimeOffset SampledAt,
    IReadOnlyList<HardwareSensorSnapshot> Sensors);

/// <summary>Contains the single hardware telemetry contract shared by capture, storage, IPC and presentation.</summary>
/// <remarks>Collection timestamps describe library polling, not guaranteed hardware conversion times. Watts are component power, not whole-PC energy.</remarks>
public sealed record SystemSnapshot(
    DateTimeOffset Timestamp,
    string Status,
    IReadOnlyList<HardwareDeviceSnapshot> Devices,
    string DriverStatus = "not-installed",
    string? ErrorCode = null,
    DateTimeOffset? CollectionStartedAt = null,
    DeviceContextSnapshot? DeviceContext = null,
    string? InformationalSchedule = null,
    string LibraryVersion = "0.9.6");

/// <summary>Owns the sole sensor collector and its optional explicitly elevated mode.</summary>
public interface IHardwareTelemetryService : IAsyncDisposable
{
    /// <summary>Applies validated settings; initial opted-in configuration restores installed advanced sensors with Windows consent.</summary>
    ValueTask ConfigureAsync(HardwareTelemetryConfiguration configuration, CancellationToken cancellationToken);

    /// <summary>Gets a bounded immutable snapshot, including explicit unavailable or failed states.</summary>
    ValueTask<SystemSnapshot> CaptureAsync(CancellationToken cancellationToken);

    /// <summary>Enables background polling only while tracking is active.</summary>
    ValueTask SetTrackingAsync(bool isTracking, CancellationToken cancellationToken);

    /// <summary>Installs the bundled driver when needed and requests the advanced collector, with explicit Windows consent.</summary>
    Task EnableAdvancedAsync(CancellationToken cancellationToken);
}

/// <summary>Validates the bounded durable and cross-process hardware contract.</summary>
public static class SystemSnapshotValidator
{
    /// <summary>Rejects malformed snapshots rather than normalizing corrupted data.</summary>
    public static void Validate(SystemSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (snapshot.Timestamp == default || snapshot.Status is not ("ready" or "partial" or "unavailable" or "unsupported" or "error" or "starting" or "stale" or "disabled")
            || snapshot.Devices is null || snapshot.Devices.Count > 64
            || snapshot.Status == "disabled" && snapshot.Devices.Count != 0
            || !ValidText(snapshot.DriverStatus, 80)
            || !ValidText(snapshot.LibraryVersion, 80)
            || snapshot.ErrorCode is { } error && !ValidText(error, 160)
            || snapshot.CollectionStartedAt > snapshot.Timestamp)
        {
            throw new InvalidDataException("The hardware snapshot metadata is invalid.");
        }

        var deviceIds = new HashSet<string>(StringComparer.Ordinal);
        var sensorIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var device in snapshot.Devices)
        {
            if (device is null || !ValidText(device.Id, 256) || !deviceIds.Add(device.Id)
                || !ValidText(device.Name, 256) || !ValidText(device.Kind, 64)
                || device.SampledAt == default || device.SampledAt > snapshot.Timestamp
                || device.Sensors is null || device.Sensors.Count > 512)
            {
                throw new InvalidDataException("The hardware device metadata is invalid.");
            }

            foreach (var sensor in device.Sensors)
            {
                if (sensor is null || !ValidText(sensor.Id, 256) || !sensorIds.Add(sensor.Id)
                    || !ValidText(sensor.Name, 256) || !ValidText(sensor.Kind, 64)
                    || !ValidText(sensor.Unit, 32) || sensor.Value is { } value && !double.IsFinite(value)
                    || sensorIds.Count > 4096)
                {
                    throw new InvalidDataException("The hardware sensor reading is invalid.");
                }
            }
        }
    }

    private static bool ValidText(string? value, int maximumLength) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= maximumLength && !value.Any(char.IsControl);
}

/// <summary>Derives low-weight activity usage from the same library readings used by screenshots.</summary>
public static class HardwareUsageProjection
{
    /// <summary>Projects CPU package average and GPU utilization without summing overlapping GPU engines.</summary>
    public static (int? Cpu, int? Gpu) Read(SystemSnapshot snapshot)
    {
        if (snapshot.Status is not ("ready" or "partial"))
        {
            return (null, null);
        }

        var cpu = ReadValues(snapshot, "Cpu", "CPU Total");
        var gpu = snapshot.Devices.Where(device => device.Kind.StartsWith("Gpu", StringComparison.Ordinal))
            .Select(device => SelectGpuUtilization(device.Sensors))
            .Where(sensor => sensor is not null)
            .Select(sensor => sensor!.Value!.Value).ToArray();
        return (cpu.Length == 0 ? null : (int)Math.Round(cpu.Average()),
            gpu.Length == 0 ? null : (int)Math.Round(gpu.Max()));
    }

    /// <summary>Selects the driver's core utilization, or its busiest D3D engine when no core reading is available.</summary>
    public static HardwareSensorSnapshot? SelectGpuUtilization(IEnumerable<HardwareSensorSnapshot> sensors)
    {
        ArgumentNullException.ThrowIfNull(sensors);
        var loads = sensors.Where(sensor => sensor.Kind == "Load" && sensor.Unit == "%" && sensor.Value is >= 0 and <= 100).ToArray();
        // Integrated Intel adapters expose D3D engines instead of GPU Core. Their concurrent loads overlap,
        // so the busiest engine is representative; absent/invalid readings remain unavailable, never zero.
        return loads.FirstOrDefault(sensor => sensor.Name == "GPU Core")
            ?? loads.Where(sensor => sensor.Name.StartsWith("D3D ", StringComparison.Ordinal))
                .OrderByDescending(sensor => sensor.Value).ThenBy(sensor => sensor.Id, StringComparer.Ordinal).FirstOrDefault();
    }

    private static double[] ReadValues(SystemSnapshot snapshot, string kind, string name) =>
        snapshot.Devices.Where(device => device.Kind == kind).SelectMany(device => device.Sensors)
            .Where(sensor => sensor.Kind == "Load" && sensor.Name == name && sensor.Value is >= 0 and <= 100)
            .Select(sensor => sensor.Value!.Value).ToArray();
}
