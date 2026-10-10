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


using LibreHardwareMonitor.Hardware;
using LibreHardwareMonitor.PawnIo;
using System.Management;
using System.Runtime.InteropServices;
using WorkTrail.Services;

namespace WorkTrail.Hardware;

internal sealed class LibreHardwareTelemetryCollector : IDisposable
{
    private readonly bool _advanced;
    private readonly Dictionary<IHardware, CachedDeviceReading> _deviceCache = new();
    private readonly WindowsStorageTelemetryCollector _windowsStorage = new();
    private Computer? _computer;

    internal LibreHardwareTelemetryCollector(bool advanced)
    {
        _advanced = advanced;
        DriverStatus = ReadDriverStatus(advanced);
        PawnIo.IsDriverAccessEnabled = advanced && DriverStatus == "active";
    }

    internal string DriverStatus { get; }

    internal async Task<SystemSnapshot> SampleAsync(HardwareSamplingProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        var started = DateTimeOffset.UtcNow;
        if (_computer is null)
        {
            _computer = new Computer
            {
                IsCpuEnabled = true,
                IsGpuEnabled = true,
                IsMemoryEnabled = true,
                IsStorageEnabled = true,
                IsNetworkEnabled = true,
                IsBatteryEnabled = true,
                IsMotherboardEnabled = false,
                IsControllerEnabled = false,
                IsPsuEnabled = false,
                IsPowerMonitorEnabled = false
            };
            _computer.Open();
            // Delta-based load and throughput sensors need a baseline before the first snapshot.
            foreach (var hardware in _computer.Hardware.Where(hardware => hardware.HardwareType != HardwareType.Storage))
            {
                try { Update(hardware); }
                catch { /* The actual sample reports device failures independently after warm-up. */ }
            }
            await Task.Delay(TimeSpan.FromMilliseconds(250)).ConfigureAwait(false);
        }

        var devices = new List<HardwareDeviceSnapshot>();
        var hadError = false;
        var physicalNetworkAdapterIds = ReadPhysicalNetworkAdapterIds();
        foreach (var hardware in _computer.Hardware)
        {
            if (hardware.HardwareType == HardwareType.Network
                && !IsPhysicalNetworkAdapter(hardware.Identifier.ToString(), physicalNetworkAdapterIds))
            {
                // LibreHardwareMonitor exposes Windows filter and virtual adapters as network hardware.
                // Keep only adapters confirmed by Windows as physical, and hide all network entries if
                // that confirmation is unavailable rather than presenting virtual hardware as genuine.
                hadError |= physicalNetworkAdapterIds is null;
                continue;
            }
            if (_deviceCache.TryGetValue(hardware, out var cached)
                && !profile.IsSampleDue(hardware.HardwareType.ToString(), cached.SampledAt, DateTimeOffset.UtcNow))
            {
                // A profile change reuses device readings until due under the new rate, including failures.
                // Cached values retain their original timestamps; reusing them never suggests a fresh read.
                devices.AddRange(cached.Devices);
                hadError |= cached.HadError;
                continue;
            }
            var versions = WorkTrailSensorAccess.BeginUpdate(hardware);
            var deviceError = false;
            try { Update(hardware); }
            catch { deviceError = true; /* This device remains explicitly null/partial; other devices are independent. */ }
            var current = new List<HardwareDeviceSnapshot>();
            var sampledAt = DateTimeOffset.UtcNow;
            AppendDevice(hardware, current, versions, sampledAt);
            devices.AddRange(current);
            _deviceCache[hardware] = new CachedDeviceReading(sampledAt, current.AsReadOnly(), deviceError);
            hadError |= deviceError;
        }
        var basicStorageError = false;
        if (!devices.Any(device => device.Kind == "Storage"))
        {
            // Basic mode cannot always open raw physical disks. Its documented Windows volume source stays
            // within this collector and cadence; it is never added beside the library's physical-disk readings.
            var basicStorage = _windowsStorage.Sample(profile);
            devices.AddRange(basicStorage.Devices);
            basicStorageError = basicStorage.HasError;
        }
        var usable = devices.Sum(device => device.Sensors.Count(sensor => sensor.Value is not null));
        var missing = devices.Any(device => device.Sensors.Any(sensor => sensor.Value is null));
        var status = usable == 0 ? "unavailable" : hadError || basicStorageError || missing || DriverStatus != "active" ? "partial" : "ready";
        var snapshot = new SystemSnapshot(DateTimeOffset.UtcNow, status, Array.AsReadOnly(devices.ToArray()),
            DriverStatus, basicStorageError ? "windows-storage-read-failed" : hadError ? "device-read-failed" : null, started);
        SystemSnapshotValidator.Validate(snapshot);
        return snapshot;
    }

    private void AppendDevice(IHardware hardware, List<HardwareDeviceSnapshot> devices, IReadOnlyDictionary<ISensor, long> versions, DateTimeOffset sampledAt)
    {
        var readings = new List<HardwareSensorSnapshot>();
        foreach (var sensor in hardware.Sensors)
        {
            var staticMetadata = hardware.HardwareType == HardwareType.Battery
                && (sensor.SensorType == SensorType.Energy && sensor.Index is 0 or 1 || sensor.SensorType == SensorType.Level && sensor.Index == 1)
                || hardware.HardwareType == HardwareType.GpuAmd && sensor.SensorType == SensorType.SmallData && sensor.Name == "GPU Memory Total"
                // The library assigns disk capacity only at discovery. Free/used space, activity and temperature
                // remain poll-dependent, so failed updates cannot replay those cached measurements as fresh.
                || hardware.HardwareType == HardwareType.Storage && sensor.SensorType == SensorType.Data && sensor.Name == "Total Space";
            var raw = WorkTrailSensorAccess.ReadUpdatedValue(sensor, versions, staticMetadata);
            double? value = raw is { } number && float.IsFinite(number) ? number : null;
            if (hardware.HardwareType == HardwareType.Cpu && (!_advanced || DriverStatus != "active") && sensor.SensorType != SensorType.Load)
                value = null;
            readings.Add(new HardwareSensorSnapshot(sensor.Identifier.ToString(), sensor.Name,
                sensor.SensorType.ToString(), Unit(sensor.SensorType), value));
        }
        devices.Add(new HardwareDeviceSnapshot(hardware.Identifier.ToString(), hardware.Name, hardware.HardwareType.ToString(),
            sampledAt, Array.AsReadOnly(readings.ToArray())));
        foreach (var child in hardware.SubHardware) AppendDevice(child, devices, versions, sampledAt);
    }

    private static void Update(IHardware hardware)
    {
        hardware.Update();
        foreach (var child in hardware.SubHardware) Update(child);
    }

    /// <summary>Reads the GUIDs of adapters Windows identifies as physical, excluding NDIS filters and virtual adapters.</summary>
    private static HashSet<string>? ReadPhysicalNetworkAdapterIds()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(new ManagementScope(@"\\.\root\StandardCimv2"),
                new ObjectQuery("SELECT InterfaceGuid FROM MSFT_NetAdapter WHERE HardwareInterface = TRUE AND Virtual = FALSE"),
                new System.Management.EnumerationOptions { ReturnImmediately = true, Rewindable = false, Timeout = TimeSpan.FromSeconds(3) });
            using var results = searcher.Get();
            var adapterIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (ManagementBaseObject row in results)
            {
                using (row)
                {
                    if (Guid.TryParse(row["InterfaceGuid"] as string, out var adapterId)) adapterIds.Add(adapterId.ToString("D"));
                }
            }
            return adapterIds;
        }
        catch (Exception exception) when (exception is ManagementException or COMException or UnauthorizedAccessException or TimeoutException)
        {
            return null;
        }
    }

    /// <summary>Matches a LibreHardwareMonitor network identifier only when Windows confirmed its physical adapter GUID.</summary>
    internal static bool IsPhysicalNetworkAdapter(string hardwareIdentifier, ISet<string>? physicalAdapterIds)
    {
        if (physicalAdapterIds is null) return false;
        const string networkIdentifierPrefix = "/nic/";
        if (!hardwareIdentifier.StartsWith(networkIdentifierPrefix, StringComparison.OrdinalIgnoreCase)) return false;
        var encodedAdapterId = hardwareIdentifier[networkIdentifierPrefix.Length..];
        return Guid.TryParse(Uri.UnescapeDataString(encodedAdapterId), out var adapterId)
            && physicalAdapterIds.Contains(adapterId.ToString("D"));
    }

    private static string Unit(SensorType type) => type switch
    {
        SensorType.Voltage => "V",
        SensorType.Current => "A",
        SensorType.Power => "W",
        SensorType.Clock => "MHz",
        SensorType.Temperature => "°C",
        SensorType.Load or SensorType.Control or SensorType.Level or SensorType.Humidity => "%",
        SensorType.Frequency => "Hz",
        SensorType.Fan => "RPM",
        SensorType.Flow => "L/h",
        SensorType.Factor => "1",
        SensorType.Data => "GiB",
        SensorType.SmallData => "MiB",
        SensorType.Throughput => "B/s",
        SensorType.TimeSpan => "s",
        SensorType.Timing => "ns",
        SensorType.Energy => "mWh",
        SensorType.Noise => "dBA",
        SensorType.Conductivity => "µS/cm",
        _ => throw new InvalidDataException("Unsupported sensor unit.")
    };

    private static string ReadDriverStatus(bool advanced)
    {
        if (!PawnIo.IsInstalled) return "not-installed";
        if (!advanced) return "available";
        try
        {
            using var handle = File.OpenHandle(@"\\?\GLOBALROOT\Device\PawnIO", FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite);
            return "active";
        }
        catch (UnauthorizedAccessException) { return "access-denied"; }
        catch (IOException) { return "blocked"; }
    }

    /// <summary>Closes library device handles without changing any hardware controls.</summary>
    public void Dispose()
    {
        Program.BeginNativeOperation();
        _computer?.Close();
    }

    private sealed record CachedDeviceReading(DateTimeOffset SampledAt, IReadOnlyList<HardwareDeviceSnapshot> Devices, bool HadError);
}
