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


using System.Threading;
using System.Threading.Tasks;
using WorkTrail.Services;
using Xunit;

namespace WorkTrail.Core.Tests;

public sealed class DeviceContextServiceTests
{
    [Fact]
    public async Task CaptureAsync_UsesThePlatformValuesAndKeepsWindowsLocationProvenance()
    {
        var platform = new FakeDeviceContextPlatform(
            new DeviceContextValue("Asia/Ho_Chi_Minh", "windows-time-zone", "available"),
            new DeviceContextValue("en-US", "windows-user-ui-language", "available"),
            new DeviceContextValue("it-IT", "windows-foreground-keyboard-layout", "available"),
            new DeviceLocationSnapshot(10.7769, 106.7009, 25, "windows-geolocator", "available"));
        var service = new DeviceContextService(platform);

        var snapshot = await service.CaptureAsync();

        Assert.Equal("Asia/Ho_Chi_Minh", snapshot.TimeZone.Value);
        Assert.Equal("en-US", snapshot.WindowsUiLanguage.Value);
        Assert.Equal("it-IT", snapshot.InputLanguage.Value);
        Assert.Equal(10.7769, snapshot.Location.Latitude);
        Assert.Equal(106.7009, snapshot.Location.Longitude);
        Assert.Equal("windows-geolocator", snapshot.Location.Source);
        Assert.Equal("available", snapshot.Location.Status);
    }

    [Fact]
    public async Task CaptureAsync_RemovesInvalidCoordinatesWithoutAWebFallback()
    {
        var platform = new FakeDeviceContextPlatform(
            new DeviceContextValue("UTC", "windows-time-zone", "available"),
            new DeviceContextValue("en-US", "windows-user-ui-language", "available"),
            new DeviceContextValue("en-US", "windows-foreground-keyboard-layout", "available"),
            new DeviceLocationSnapshot(91, 10, -1, "windows-geolocator", "available"));
        var service = new DeviceContextService(platform);

        var snapshot = await service.CaptureAsync();

        Assert.Null(snapshot.Location.Latitude);
        Assert.Null(snapshot.Location.Longitude);
        Assert.Null(snapshot.Location.AccuracyMeters);
        Assert.Equal("windows-geolocator", snapshot.Location.Source);
        Assert.Equal("invalid_coordinates", snapshot.Location.Status);
    }

    private sealed class FakeDeviceContextPlatform(
        DeviceContextValue timeZone,
        DeviceContextValue windowsUiLanguage,
        DeviceContextValue inputLanguage,
        DeviceLocationSnapshot location) : IDeviceContextPlatform
    {
        private readonly DeviceContextValue _timeZone = timeZone;
        private readonly DeviceContextValue _windowsUiLanguage = windowsUiLanguage;
        private readonly DeviceContextValue _inputLanguage = inputLanguage;
        private readonly DeviceLocationSnapshot _location = location;

        public DeviceContextValue GetTimeZone() => _timeZone;

        public DeviceContextValue GetWindowsUiLanguage() => _windowsUiLanguage;

        public DeviceContextValue GetActiveInputLanguage() => _inputLanguage;

        public Task<DeviceLocationSnapshot> GetCurrentLocationAsync(CancellationToken cancellationToken)
            => Task.FromResult(_location);

        public Task<DeviceLocationAccessResult> RequestLocationAccessAsync(CancellationToken cancellationToken)
            => Task.FromResult(new DeviceLocationAccessResult("windows-geolocator", "allowed"));
    }
}
