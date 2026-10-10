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
using System.Linq;
using WorkTrail.Services;
using Xunit;

namespace WorkTrail.Presentation.Tests;

public sealed class SensorMonitorProjectionTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 18, 10, 0, 0, TimeSpan.Zero);
    private static readonly LocalizationService Strings = new("it-IT");

    [Fact]
    public void Cpu_UsesPackageTemperatureAndPowerWithoutAddingOverlappingCores()
    {
        var row = Project("Cpu",
            new("load", "CPU Total", "Load", "%", 24),
            new("package-temp", "CPU Package", "Temperature", "°C", 61),
            new("core-temp", "CPU Core #1", "Temperature", "°C", 70),
            new("package-power", "CPU Package", "Power", "W", 25.5),
            new("core-power", "CPU Cores", "Power", "W", 18));
        Assert.Equal(24, row.Percent);
        Assert.Equal("Temperatura: 61 °C", row.Temperature);
        Assert.Equal("61 °C", row.TemperatureValue);
        Assert.True(row.HasTemperature);
        Assert.Contains("26 W", row.Details);
        Assert.DoesNotContain("43,5", row.Details);
        Assert.DoesNotContain("18 W", row.Details);
    }

    [Fact]
    public void Storage_DoesNotMistakeOccupiedSpaceForDiskActivity()
    {
        var row = Project("Storage", new("space", "Used Space", "Load", "%", 62),
            new("temperature", "Temperature", "Temperature", "°C", 41));
        Assert.Null(row.Percent);
        Assert.Equal(Strings.Translate("Common.NotAvailable"), row.Value);
        Assert.Contains("62 %", row.Details);
        Assert.Equal($"{Strings.Translate("Common.NotAvailable")} liberi / {Strings.Translate("Common.NotAvailable")} totali", row.CapacityText);
        Assert.Contains("41 °C", row.Temperature);
    }

    [Fact]
    public void MissingPackageTemperature_UsesHottestAvailableTemperatureAndExposesItsSource()
    {
        var row = Project("Cpu", new("a", "CPU Core #1", "Temperature", "°C", 42),
            new("b", "CPU Core #2", "Temperature", "°C", 51));
        Assert.Equal("Temperatura: 51 °C", row.Temperature);
        Assert.Contains("CPU Core #2", row.Source);
    }

    [Fact]
    public void MultipleDevices_RemainSeparateAndAbsentBatteryDoesNotCreateARow()
    {
        var snapshot = new SystemSnapshot(Now, "ready", [
            new("gpu1", "GPU 1", "GpuIntel", Now, []),
            new("gpu2", "GPU 2", "GpuNvidia", Now, [])]);
        var rows = SensorMonitorProjection.Create(snapshot, Strings.Culture, Strings.Translate);
        Assert.Equal(2, rows.Count);
        Assert.All(rows, row => Assert.Equal("Gpu", row.Category));
        Assert.All(rows, row => Assert.False(row.HasTemperature));
        Assert.All(rows, row => Assert.Empty(row.Temperature));
    }

    [Fact]
    public void IntegratedGpu_UsesBusiestD3DEngineAndKeepsHistoryWhenThatEngineChanges()
    {
        var first = Project("GpuIntel", new("3d", "D3D 3D", "Load", "%", 24),
            new("decode", "D3D Video Decode", "Load", "%", 37),
            new("shared", "D3D Shared Memory Used", "SmallData", "MiB", 2880),
            new("total", "D3D Shared Memory Total", "SmallData", "MiB", 16384));
        var second = Project("GpuIntel", new("3d", "D3D 3D", "Load", "%", 42),
            new("decode", "D3D Video Decode", "Load", "%", 10)) with
        { SampledAt = Now.AddSeconds(2) };
        Assert.Equal(37, first.Percent);
        Assert.Contains("D3D Video Decode", first.Source);
        Assert.Equal(42, second.Percent);
        Assert.Contains("D3D 3D", second.Source);
        Assert.Equal(first.SensorId, second.SensorId);
        Assert.Equal("3 / 16 GiB", first.CapacityText);
        var history = new SensorTraceHistory();
        history.Record(first, Now);
        history.Record(second, Now.AddSeconds(2));
        Assert.Equal(2, history.GetPoints(second.Id).Count);
    }

    [Fact]
    public void PhysicalMemory_ShowsUsedCapacityAndDoesNotDuplicateVirtualMemory()
    {
        var snapshot = new SystemSnapshot(Now, "ready", [
            new("/ram", "Total Memory", "Memory", Now, [
                new("ram-load", "Memory", "Load", "%", 62.5),
                new("ram-used", "Memory Used", "Data", "GiB", 20),
                new("ram-free", "Memory Available", "Data", "GiB", 12)]),
            new("/vram", "Virtual Memory", "Memory", Now, [
                new("virtual-load", "Memory", "Load", "%", 40)])]);
        var row = Assert.Single(SensorMonitorProjection.Create(snapshot, Strings.Culture, Strings.Translate));
        Assert.Equal("/ram", row.Id);
        Assert.Equal("20 / 32 GiB", row.CapacityText);
        Assert.Equal(62.5, row.Percent);
    }

    [Fact]
    public void SensorValues_RoundDisplayedMeasurementsToWholeUnits()
    {
        var row = Project("Cpu",
            new("load", "CPU Total", "Load", "%", 13.6),
            new("temperature", "CPU Package", "Temperature", "°C", 61.5),
            new("power", "CPU Package", "Power", "W", 25.5),
            new("clock", "CPU Core #1", "Clock", "MHz", 3150));

        Assert.Equal("14 %", row.Value);
        Assert.Equal("Temperatura: 62 °C", row.Temperature);
        Assert.Contains("26 W", row.SecondaryValue);
        Assert.Contains("3 GHz", row.SecondaryValue);
    }

    [Fact]
    public void Battery_PreservesChargeAndDischargeDetailsWithoutUnsupportedTemperature()
    {
        var row = Project("Battery", new("level", "Charge Level", "Level", "%", 75),
            new("power", "Discharge Rate", "Power", "W", 26),
            new("time", "Remaining Time (Estimated)", "TimeSpan", "s", 8520),
            new("temp", "Temperature", "Temperature", "°C", null));
        Assert.Equal(75, row.Percent);
        Assert.Equal("In scarica 26 W · 142 min", row.SecondaryValue);
        Assert.False(row.HasTemperature);
        Assert.Empty(row.Temperature);
    }

    [Fact]
    public void Storage_ShowsCapacitySeparateFromActivityAndIncludesTransferRates()
    {
        var row = Project("Storage", new("activity", "Total Activity", "Load", "%", 7),
            new("total", "Total Space", "Data", "GiB", 1000),
            new("free", "Free Space", "Data", "GiB", 380),
            new("read", "Read Rate", "Throughput", "B/s", 2097152));
        Assert.Equal(7, row.Percent);
        Assert.Equal("380 GiB liberi / 1000 GiB totali", row.CapacityText);
        Assert.Equal("Lettura 2 MiB/s", row.SecondaryValue);
    }

    [Fact]
    public void NvmeTemperature_UsesCompositeMeasurementAndNeverThermalWarningThresholds()
    {
        HardwareSensorSnapshot[] thresholds = [
            new("warning", "Warning Temperature", "Temperature", "°C", 80),
            new("critical", "Critical Temperature", "Temperature", "°C", 95)];
        var measured = Project("Storage", [.. thresholds,
            new("composite", "Composite Temperature", "Temperature", "°C", 41),
            new("controller", "Temperature #1", "Temperature", "°C", 53)]);
        Assert.Equal("41 °C", measured.TemperatureValue);
        Assert.Contains("Composite Temperature", measured.Source);
        var alternate = Project("Storage", [.. thresholds,
            new("composite", "Composite Temperature", "Temperature", "°C", null),
            new("controller", "Temperature #1", "Temperature", "°C", 53)]);
        Assert.Equal("53 °C", alternate.TemperatureValue);
        Assert.False(Project("Storage", thresholds).HasTemperature);
    }

    [Fact]
    public void StaleSnapshot_DoesNotPresentOldValuesAsLive()
    {
        var snapshot = new SystemSnapshot(Now, "stale", [new("cpu", "CPU", "Cpu", Now.AddMinutes(-5),
            [new("load", "CPU Total", "Load", "%", 95), new("temp", "CPU Package", "Temperature", "°C", 91)])]);
        var row = Assert.Single(SensorMonitorProjection.Create(snapshot, Strings.Culture, Strings.Translate));
        Assert.Null(row.Percent);
        Assert.Equal(Strings.Translate("Common.NotAvailable"), row.Value);
        Assert.DoesNotContain("91", row.Temperature);
    }

    [Fact]
    public void History_DeduplicatesCachedSamplesEvictsOldPointsAndResetsChangedSensors()
    {
        var history = new SensorTraceHistory();
        var row = Project("Cpu", new HardwareSensorSnapshot("load", "CPU Total", "Load", "%", 24));
        history.Record(row, Now);
        Assert.Single(history.GetPoints(row.Id));
        history.Record(row, Now.AddSeconds(1));
        Assert.Single(history.GetPoints(row.Id));
        history.Record(row with { SampledAt = Now.AddSeconds(2), Percent = 30 }, Now.AddSeconds(2));
        Assert.Equal(2, history.GetPoints(row.Id).Count);
        history.Record(row, Now.AddMinutes(3));
        Assert.Empty(history.GetPoints(row.Id));
        history.Record(row with { SensorId = "new", SampledAt = Now.AddMinutes(3) }, Now.AddMinutes(3));
        Assert.Single(history.GetPoints(row.Id));
        history.Retain([]);
        Assert.Empty(history.GetPoints(row.Id));
        history.Record(row, Now);
        Assert.Single(history.GetPoints(row.Id));
    }

    [Fact]
    public void History_RecordsHiddenDevicesWithoutCopyingTheirTracesUntilRequested()
    {
        var history = new SensorTraceHistory();
        var row = Project("Cpu", new HardwareSensorSnapshot("load", "CPU Total", "Load", "%", 0));
        var hidden = row with { Id = "hidden-device" };
        history.Retain([row, hidden]);
        history.Record(row, Now);
        history.Record(hidden, Now);
        var visiblePoints = history.GetPoints(row.Id);
        history.Record(hidden with { SampledAt = Now.AddSeconds(2), Percent = null }, Now.AddSeconds(2));

        Assert.Single(visiblePoints);
        var hiddenPoints = history.GetPoints(hidden.Id);
        Assert.Equal(2, hiddenPoints.Count);
        Assert.Equal(0, hiddenPoints[0].Value);
        Assert.Null(hiddenPoints[1].Value);
        history.Retain([hidden]);
        Assert.Empty(history.GetPoints(row.Id));
        Assert.Equal(2, history.GetPoints(hidden.Id).Count);
    }

    private static SensorMonitorRow Project(string kind, params HardwareSensorSnapshot[] sensors) =>
        Assert.Single(SensorMonitorProjection.Create(new SystemSnapshot(Now, "ready", [new("device", "Device", kind, Now, sensors)]), Strings.Culture, Strings.Translate));
}
