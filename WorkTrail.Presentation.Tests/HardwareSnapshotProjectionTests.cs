// SPDX-License-Identifier: MIT

using System;
using System.Linq;
using WorkTrail.Application;
using WorkTrail.Services;
using Xunit;

namespace WorkTrail.Presentation.Tests;

public sealed class HardwareSnapshotProjectionTests
{
    [Fact]
    public void CaptureProjection_PreservesBatteryUnitsAndHistoricalSamplingTime()
    {
        var capturedAt = new DateTimeOffset(2026, 9, 12, 10, 30, 0, TimeSpan.Zero);
        var sampledAt = capturedAt.AddSeconds(-30);
        var snapshot = new SystemSnapshot(capturedAt, "partial",
        [
            new("/battery/0", "Portable battery", "Battery", sampledAt,
            [
                new("/battery/0/level/0", "Charge Level", "Level", "%", 72.5),
                new("/battery/0/power/0", "Discharge Rate", "Power", "W", 8.4),
                new("/battery/0/energy/0", "Remaining Capacity", "Energy", "mWh", 43000),
                new("/battery/0/temperature/0", "Temperature", "Temperature", "°C", null)
            ])
        ]);
        var strings = new LocalizationService("it-IT");
        var item = new ScreenshotGalleryItem(capturedAt, "frame.webp", "Editor", "monitor", "manual", HardwareSnapshot: snapshot);

        var state = ScreenshotDetailsProjection.Create(item, strings.Culture, "Schermo", "Manuale", "--", strings.Translate);

        var battery = Assert.Single(state.Hardware.Summary, row => row.Label == "Batteria");
        Assert.Contains("72,5 %", battery.Value, StringComparison.Ordinal);
        Assert.Contains("8,4 W", battery.Value, StringComparison.Ordinal);
        Assert.Contains("43000 mWh", battery.Value, StringComparison.Ordinal);
        Assert.DoesNotContain("Temperature", battery.Value, StringComparison.Ordinal);
        var device = Assert.Single(state.Hardware.Details);
        Assert.Equal("/battery/0", device.Id);
        Assert.Contains(sampledAt.ToLocalTime().ToString("G", strings.Culture), device.UpdatedAt, StringComparison.Ordinal);
        Assert.Contains(device.Readings, line => line.Contains("Temperature", StringComparison.Ordinal)
            && line.Contains(strings.Translate("Common.NotAvailable"), StringComparison.Ordinal));
        Assert.Equal(strings.Translate("Hardware.Status.Partial"), state.Hardware.Status);
        Assert.Equal("--", state.CpuUsage);
    }

    [Fact]
    public void Projection_HidesHardwareWhenNoMeasurementsExist()
    {
        var strings = new LocalizationService("en-US");
        var collected = HardwareSnapshotProjection.Create(new SystemSnapshot(DateTimeOffset.UtcNow, "ready", []), strings.Culture, strings.Translate);
        var historical = HardwareSnapshotProjection.Create(null, strings.Culture, strings.Translate);

        Assert.False(collected.HasData);
        Assert.False(historical.HasData);
        Assert.Empty(collected.Summary);
        Assert.Empty(historical.Summary);
        Assert.Empty(historical.Details);
    }

    [Fact]
    public void Projection_DoesNotSumOverlappingPowerSensorsAndShowsStaleStatus()
    {
        var sampledAt = new DateTimeOffset(2026, 9, 12, 10, 30, 0, TimeSpan.Zero);
        var strings = new LocalizationService("en-US");
        var snapshot = new SystemSnapshot(sampledAt.AddMinutes(5), "stale",
        [
            new("/cpu/0", "CPU", "Cpu", sampledAt,
            [
                new("/cpu/0/power/0", "CPU Package", "Power", "W", 20),
                new("/cpu/0/power/1", "CPU Cores", "Power", "W", 15)
            ])
        ]);

        var result = HardwareSnapshotProjection.Create(snapshot, strings.Culture, strings.Translate);

        Assert.Equal(strings.Translate("Hardware.Status.Stale"), result.Status);
        Assert.Contains(strings.Translate("Common.NotAvailable"), result.Summary[0].Value, StringComparison.Ordinal);
        Assert.DoesNotContain("35 W", result.Summary[0].Value, StringComparison.Ordinal);
        var device = Assert.Single(result.Details);
        Assert.Contains(device.Readings, line => line.Contains("CPU Package", StringComparison.Ordinal));
        Assert.Contains(device.Readings, line => line.Contains("CPU Cores", StringComparison.Ordinal));
    }

    [Fact]
    public void Projection_ShowsEssentialRoundedMeasurementsAndKeepsRawDetails()
    {
        var timestamp = new DateTimeOffset(2026, 10, 2, 10, 0, 0, TimeSpan.Zero);
        var strings = new LocalizationService("en-US");
        var snapshot = new SystemSnapshot(timestamp, "ready",
        [
            new("/cpu", "CPU", "Cpu", timestamp,
                [new("/cpu/core", "CPU Core #1", "Load", "%", 99.26), new("/cpu/total", "CPU Total", "Load", "%", 16.46)]),
            new("/gpu", "GPU", "GpuNvidia", timestamp,
                [new("/gpu/load", "GPU Core", "Load", "%", 33.12), new("/gpu/temp", "GPU Core", "Temperature", "°C", 51.2),
                 new("/gpu/clock", "GPU Core", "Clock", "MHz", 210)]),
            new("/vram", "Virtual Memory", "Memory", timestamp,
                [new("/vram/used", "Memory Used", "Data", "GiB", 25.4)]),
            new("/ram", "Total Memory", "Memory", timestamp,
                [new("/ram/used", "Memory Used", "Data", "GiB", 21.43), new("/ram/free", "Memory Available", "Data", "GiB", 10.5)]),
            new("/disk", "C:", "Storage", timestamp,
                [new("/disk/free", "Free Space", "Data", "GiB", 192.32), new("/disk/total", "Total Space", "Data", "GiB", 418.21),
                 new("/disk/read", "Read Rate", "Throughput", "B/s", 0), new("/disk/write", "Write Rate", "Throughput", "B/s", 15930)]),
            new("/net", "Ethernet", "Network", timestamp,
                [new("/net/up", "Upload Speed", "Throughput", "B/s", 4397.17), new("/net/down", "Download Speed", "Throughput", "B/s", 18886.8),
                 new("/net/load", "Network Utilization", "Load", "%", 0.19)])
        ]);

        var result = HardwareSnapshotProjection.Create(snapshot, strings.Culture, strings.Translate);

        Assert.Equal("CPU: 16 %", Assert.Single(result.Summary, row => row.Label == "CPU").Value);
        Assert.Equal("GPU: 33 % · 51 °C", Assert.Single(result.Summary, row => row.Label == "GPU").Value);
        Assert.Equal("Total Memory: 21.4 / 31.9 GiB", Assert.Single(result.Summary, row => row.Label == "Memory").Value);
        Assert.Equal("C: 192.3 GiB free / 418.2 GiB total · Read 0 B/s · Write 15.6 KiB/s",
            Assert.Single(result.Summary, row => row.Label == "Storage").Value);
        Assert.Equal("Ethernet: ↓ 18.4 KiB/s · ↑ 4.3 KiB/s", Assert.Single(result.Summary, row => row.Label == "Network").Value);
        Assert.Equal(snapshot.Devices.Select(device => device.Id), result.Details.Select(device => device.Id));
        Assert.Contains(result.Details, device => device.Name == "Virtual Memory");
        var readings = result.Details.SelectMany(device => device.Readings).ToArray();
        Assert.Contains(readings, line => line.Contains("99.26 %", StringComparison.Ordinal));
        Assert.Contains(readings, line => line.Contains("210 MHz", StringComparison.Ordinal));
        Assert.Contains(readings, line => line.Contains("0.19 %", StringComparison.Ordinal));
        Assert.Equal(16.46, snapshot.Devices[0].Sensors[1].Value);
    }

    [Fact]
    public void Projection_KeepsZeroTotalsAndDoesNotInventMissingPhysicalMemoryOrCpuTotals()
    {
        var timestamp = new DateTimeOffset(2026, 10, 2, 10, 0, 0, TimeSpan.Zero);
        var strings = new LocalizationService("it-IT");
        var snapshot = new SystemSnapshot(timestamp, "partial",
        [
            new("/cpu/0", "Zero CPU", "Cpu", timestamp, [new("/cpu/0/total", "CPU Total", "Load", "%", 0)]),
            new("/cpu/1", "Core-only CPU", "Cpu", timestamp, [new("/cpu/1/core", "CPU Core #1", "Load", "%", 90)]),
            new("/vram", "Virtual Memory", "Memory", timestamp, [new("/vram/load", "Memory", "Load", "%", 74.88)])
        ]);

        var result = HardwareSnapshotProjection.Create(snapshot, strings.Culture, strings.Translate);

        var cpu = Assert.Single(result.Summary);
        Assert.Contains("Zero CPU: 0 %", cpu.Value, StringComparison.Ordinal);
        Assert.Contains("Core-only CPU: " + strings.Translate("Common.NotAvailable"), cpu.Value, StringComparison.Ordinal);
        Assert.Contains(result.Details.Single(device => device.Id == "/vram").Readings,
            line => line.Contains("74,88 %", StringComparison.Ordinal));
    }

    [Fact]
    public void Projection_GroupsDuplicateDeviceNamesAndPreservesZeroAndMissingMeasurements()
    {
        var timestamp = DateTimeOffset.UtcNow;
        var strings = new LocalizationService("en-US");
        var devices = new HardwareDeviceSnapshot[]
        {
            new("/gpu/0", "GPU", "GpuNvidia", timestamp,
                [new("/gpu/0/load", "GPU Core", "Load", "%", 0), new("/gpu/0/temp", "GPU Core", "Temperature", "°C", null)]),
            new("/gpu/1", "GPU", "GpuIntel", timestamp.AddSeconds(-5),
                [new("/gpu/1/temp", "GPU Core", "Temperature", "°C", 40.25)])
        };
        var result = HardwareSnapshotProjection.Create(new SystemSnapshot(timestamp, "partial", devices), strings.Culture, strings.Translate);

        Assert.Equal(2, result.Details.Count);
        Assert.Equal(2, result.Details[0].Readings.Count);
        Assert.Contains("0 %", result.Details[0].Readings[0], StringComparison.Ordinal);
        Assert.Contains(strings.Translate("Common.NotAvailable"), result.Details[0].Readings[1], StringComparison.Ordinal);
        Assert.Equal("/gpu/1", result.Details[1].Id);
        Assert.NotEqual(result.Details[0].UpdatedAt, result.Details[1].UpdatedAt);
        Assert.Null(devices[0].Sensors[1].Value);
        Assert.Equal(40.25, devices[1].Sensors[0].Value);
    }
}
