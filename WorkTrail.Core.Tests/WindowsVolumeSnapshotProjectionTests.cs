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
using WorkTrail.Hardware;
using WorkTrail.Services;
using Xunit;

namespace WorkTrail.Core.Tests;

public sealed class WindowsVolumeSnapshotProjectionTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 18, 10, 0, 0, TimeSpan.Zero);
    private const long Gibibyte = 1073741824;

    [Fact]
    public void BasicVolume_PreservesCapacityUnitsAndSeparatesOccupancyFromActivity()
    {
        var result = WindowsVolumeSnapshotProjection.Create("C:", 100 * Gibibyte, 60 * Gibibyte, 93, 2048, 4096, Now);
        Assert.False(result.HasError);
        Assert.Equal("/windows/volume/c", result.Device.Id);
        Assert.Equal("C:", result.Device.Name);
        Assert.Equal("Storage", result.Device.Kind);
        Assert.Equal(Now, result.Device.SampledAt);
        Assert.Equal(7, Sensor(result.Device, "Total Activity").Value);
        Assert.Equal(40, Sensor(result.Device, "Used Space").Value);
        Assert.Equal(100, Sensor(result.Device, "Total Space").Value);
        Assert.Equal("GiB", Sensor(result.Device, "Total Space").Unit);
        Assert.Equal(60, Sensor(result.Device, "Free Space").Value);
        Assert.Equal(2048, Sensor(result.Device, "Read Rate").Value);
        Assert.Equal("B/s", Sensor(result.Device, "Write Rate").Unit);
        Assert.DoesNotContain(result.Device.Sensors, sensor => sensor.Kind == "Temperature");
        SystemSnapshotValidator.Validate(new SystemSnapshot(Now, "partial", [result.Device]));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(101)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void InvalidIdleCounter_IsUnavailableWithoutDiscardingValidCapacity(double idle)
    {
        var result = WindowsVolumeSnapshotProjection.Create("C:", 100 * Gibibyte, 60 * Gibibyte, idle, 10, 20, Now);
        Assert.True(result.HasError);
        Assert.Null(Sensor(result.Device, "Total Activity").Value);
        Assert.Equal(100, Sensor(result.Device, "Total Space").Value);
        Assert.Equal(40, Sensor(result.Device, "Used Space").Value);
    }

    [Fact]
    public void MissingWindowsCounters_KeepCapacityAndDoNotPretendTheDiskIsIdle()
    {
        var result = WindowsVolumeSnapshotProjection.Create("C:", 100 * Gibibyte, 60 * Gibibyte, null, null, null, Now);
        Assert.True(result.HasError);
        Assert.Null(Sensor(result.Device, "Total Activity").Value);
        Assert.Null(Sensor(result.Device, "Read Rate").Value);
        Assert.Null(Sensor(result.Device, "Write Rate").Value);
        Assert.Equal(100, Sensor(result.Device, "Total Space").Value);
    }

    [Fact]
    public void InvalidCapacityAndRates_RemainNullWhileValidActivitySurvives()
    {
        var result = WindowsVolumeSnapshotProjection.Create("D:", 100, 101, 80, -1, double.PositiveInfinity, Now);
        Assert.True(result.HasError);
        Assert.Equal("/windows/volume/d", result.Device.Id);
        Assert.Equal(20, Sensor(result.Device, "Total Activity").Value);
        Assert.Null(Sensor(result.Device, "Used Space").Value);
        Assert.Null(Sensor(result.Device, "Total Space").Value);
        Assert.Null(Sensor(result.Device, "Free Space").Value);
        Assert.Null(Sensor(result.Device, "Read Rate").Value);
        Assert.Null(Sensor(result.Device, "Write Rate").Value);
    }

    [Fact]
    public void ValidZeroReadings_ArePreserved()
    {
        var result = WindowsVolumeSnapshotProjection.Create("C:", Gibibyte, Gibibyte, 100, 0, 0, Now);
        Assert.False(result.HasError);
        Assert.Equal(0, Sensor(result.Device, "Total Activity").Value);
        Assert.Equal(0, Sensor(result.Device, "Used Space").Value);
        Assert.Equal(0, Sensor(result.Device, "Read Rate").Value);
    }

    [Theory]
    [InlineData("_Total")]
    [InlineData("C:\\")]
    [InlineData("c:")]
    public void UnknownVolumeIdentity_IsRejected(string volume) => Assert.Throws<InvalidDataException>(() =>
        WindowsVolumeSnapshotProjection.Create(volume, Gibibyte, 0, 100, 0, 0, Now));

    private static HardwareSensorSnapshot Sensor(HardwareDeviceSnapshot device, string name) =>
        Assert.Single(device.Sensors, sensor => sensor.Name == name);
}
