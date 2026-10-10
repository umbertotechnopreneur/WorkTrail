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
using Xunit;

namespace WorkTrail.Core.Tests;

public sealed class HardwareTelemetryModelTests
{
    [Fact]
    public void Validation_PreservesZeroAndMissingValuesIncludingBattery()
    {
        var now = DateTimeOffset.UtcNow;
        var snapshot = new SystemSnapshot(now, "partial",
        [
            new("/battery/0", "Battery", "Battery", now,
            [
                new("/battery/0/level/0", "Charge Level", "Level", "%", 0),
                new("/battery/0/power/0", "Discharge Rate", "Power", "W", null)
            ])
        ]);

        SystemSnapshotValidator.Validate(snapshot);
        Assert.Equal(0, snapshot.Devices[0].Sensors[0].Value);
        Assert.Null(snapshot.Devices[0].Sensors[1].Value);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void Validation_RejectsNonFiniteReadings(double value)
    {
        var snapshot = HardwareTestData.Snapshot(DateTimeOffset.UtcNow, cpu: value);
        Assert.Throws<InvalidDataException>(() => SystemSnapshotValidator.Validate(snapshot));
    }

    [Fact]
    public void Validation_RejectsDuplicateDeviceIdentityAndInvalidTime()
    {
        var snapshot = HardwareTestData.Snapshot(DateTimeOffset.UtcNow);
        Assert.Throws<InvalidDataException>(() => SystemSnapshotValidator.Validate(
            snapshot with { Devices = [snapshot.Devices[0], snapshot.Devices[0]] }));
        Assert.Throws<InvalidDataException>(() => SystemSnapshotValidator.Validate(
            snapshot with { CollectionStartedAt = snapshot.Timestamp.AddSeconds(1) }));
    }

    [Fact]
    public void UsageProjection_DoesNotSumOverlappingGpuEnginesOrUseStaleSamples()
    {
        var now = DateTimeOffset.UtcNow;
        var snapshot = new SystemSnapshot(now, "ready",
        [
            new("/gpu/0", "GPU 1", "GpuNvidia", now,
            [
                new("/gpu/0/load/0", "GPU Core", "Load", "%", 60),
                new("/gpu/0/load/1", "GPU Memory Controller", "Load", "%", 90)
            ]),
            new("/gpu/1", "GPU 2", "GpuAmd", now,
                [new("/gpu/1/load/0", "GPU Core", "Load", "%", 40)])
        ]);

        Assert.Equal(60, HardwareUsageProjection.Read(snapshot).Gpu);
        Assert.Null(HardwareUsageProjection.Read(snapshot with { Status = "stale" }).Gpu);
    }

    [Fact]
    public void UsageProjection_UsesBusiestIntegratedD3DEngineWithoutAddingConcurrentLoads()
    {
        var now = DateTimeOffset.UtcNow;
        var snapshot = new SystemSnapshot(now, "partial", [new("/gpu/0", "Intel Graphics", "GpuIntel", now, [
            new("/gpu/load/0", "D3D 3D", "Load", "%", 20),
            new("/gpu/load/1", "D3D Video Decode", "Load", "%", 45),
            new("/gpu/load/2", "GPU Memory", "Load", "%", 90),
            new("/gpu/load/3", "D3D Copy", "Load", "%", null)])]);
        Assert.Equal(45, HardwareUsageProjection.Read(snapshot).Gpu);
    }

    [Fact]
    public void GpuUtilization_PrefersCoreLoadAndDoesNotTurnMissingOrInvalidValuesIntoZero()
    {
        HardwareSensorSnapshot[] sensors = [
            new("core", "GPU Core", "Load", "%", 10),
            new("3d", "D3D 3D", "Load", "%", 30),
            new("copy", "D3D Copy", "Load", "%", 150)];
        Assert.Equal("core", HardwareUsageProjection.SelectGpuUtilization(sensors)?.Id);
        Assert.Equal("3d", HardwareUsageProjection.SelectGpuUtilization([sensors[0] with { Value = null }, sensors[1], sensors[2]])?.Id);
        Assert.Null(HardwareUsageProjection.SelectGpuUtilization([sensors[0] with { Value = null }, sensors[2]]));
        Assert.Equal(0, HardwareUsageProjection.SelectGpuUtilization([sensors[1] with { Value = 0 }])?.Value);
    }
}
