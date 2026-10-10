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
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using WorkTrail.Services;
using Xunit;

namespace WorkTrail.Core.Tests;

public sealed class HardwareTelemetryServiceTests
{
    [Fact]
    public async Task ConcurrentConsumers_ShareOneFrozenRecentReading()
    {
        var clock = new TestClock();
        var reads = 0;
        var sensors = new List<HardwareSensorSnapshot> { new("/cpu/load/0", "CPU Total", "Load", "%", 12) };
        var devices = new List<HardwareDeviceSnapshot> { new("/cpu", "CPU", "Cpu", clock.GetUtcNow(), sensors) };
        await using var service = Create(clock, _ =>
        {
            reads++;
            return ValueTask.FromResult(new SystemSnapshot(clock.GetUtcNow(), "partial", devices));
        });

        var results = await Task.WhenAll(service.CaptureAsync(CancellationToken.None).AsTask(), service.CaptureAsync(CancellationToken.None).AsTask());
        sensors.Clear();
        devices.Clear();

        Assert.Equal(1, reads);
        Assert.Same(results[0], results[1]);
        Assert.Equal(12, results[0].Devices[0].Sensors[0].Value);
        Assert.Throws<NotSupportedException>(() => ((IList<HardwareDeviceSnapshot>)results[0].Devices).Clear());
    }

    [Fact]
    public async Task FailedReading_IsExplicitAndRetriedAfterCooldown()
    {
        var clock = new TestClock();
        var attempts = 0;
        await using var service = Create(clock, _ =>
        {
            attempts++;
            if (attempts == 1) throw new IOException("private machine diagnostic must not enter the snapshot");
            return ValueTask.FromResult(new SystemSnapshot(clock.GetUtcNow(), "ready", []));
        });

        var failed = await service.CaptureAsync(CancellationToken.None);
        Assert.Equal("unavailable", failed.Status);
        Assert.Equal("collector-failed", failed.ErrorCode);
        Assert.Empty(failed.Devices);
        clock.Advance(TimeSpan.FromSeconds(3));
        Assert.Same(failed, await service.CaptureAsync(CancellationToken.None));
        clock.Advance(TimeSpan.FromSeconds(8));
        Assert.Equal("ready", (await service.CaptureAsync(CancellationToken.None)).Status);
        Assert.Equal(2, attempts);
    }

    [Fact]
    public async Task InvalidProviderData_IsNotSavedAsAValidReading()
    {
        var clock = new TestClock();
        await using var service = Create(clock, _ => ValueTask.FromResult(new SystemSnapshot(clock.GetUtcNow(), "ready",
            [new("/cpu", "CPU", "Cpu", clock.GetUtcNow(), [new("/cpu/temp/0", "CPU Package", "Temperature", "°C", double.NaN)])])));

        var result = await service.CaptureAsync(CancellationToken.None);

        Assert.Equal("unavailable", result.Status);
        Assert.Empty(result.Devices);
    }

    [Fact]
    public async Task FatalCollectorError_RemainsExplicitUntilRetryCooldownExpires()
    {
        var clock = new TestClock();
        var attempts = 0;
        await using var service = Create(clock, _ =>
        {
            attempts++;
            return ValueTask.FromResult(attempts == 1
                ? new SystemSnapshot(clock.GetUtcNow(), "error", [], "blocked", "sensor-read-failed")
                : new SystemSnapshot(clock.GetUtcNow(), "partial", [], "available"));
        });

        var failed = await service.CaptureAsync(CancellationToken.None);
        Assert.Equal("error", failed.Status);
        Assert.Equal("sensor-read-failed", failed.ErrorCode);
        Assert.Equal("blocked", failed.DriverStatus);
        clock.Advance(TimeSpan.FromSeconds(9));
        Assert.Same(failed, await service.CaptureAsync(CancellationToken.None));
        Assert.Equal(1, attempts);

        clock.Advance(TimeSpan.FromSeconds(1));
        var retried = await service.CaptureAsync(CancellationToken.None);
        Assert.Equal("partial", retried.Status);
        Assert.Equal("available", retried.DriverStatus);
        Assert.Null(retried.ErrorCode);
        Assert.Equal(2, attempts);
    }

    [Fact]
    public async Task Cancellation_DoesNotBecomeAnUnavailableMeasurement()
    {
        var clock = new TestClock();
        await using var service = Create(clock, async token =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            throw new InvalidOperationException();
        });
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.CaptureAsync(cancellation.Token).AsTask());
    }

    [Fact]
    public async Task DisabledCollection_DoesNotReadAndRetainsTrackingIntent()
    {
        var clock = new TestClock();
        var reads = 0;
        await using var service = Create(clock, _ =>
        {
            reads++;
            return ValueTask.FromResult(new SystemSnapshot(clock.GetUtcNow(), "partial", []));
        });
        await service.ConfigureAsync(new(Enabled: false), CancellationToken.None);
        await service.SetTrackingAsync(true, CancellationToken.None);

        var disabled = await service.CaptureAsync(CancellationToken.None);
        SystemSnapshotValidator.Validate(disabled);
        Assert.Equal("disabled", disabled.Status);
        Assert.Equal("disabled", disabled.DriverStatus);
        Assert.Empty(disabled.Devices);
        Assert.Equal(0, reads);

        await service.ConfigureAsync(new(), CancellationToken.None);
        Assert.Equal(1, reads);
        await service.ConfigureAsync(new(Enabled: false), CancellationToken.None);
        Assert.Equal("disabled", (await service.CaptureAsync(CancellationToken.None)).Status);
        Assert.Equal(1, reads);
    }

    [Fact]
    public async Task ProfileChange_UsesNewSharedCacheInterval()
    {
        var clock = new TestClock();
        var reads = 0;
        await using var service = Create(clock, _ =>
        {
            reads++;
            return ValueTask.FromResult(new SystemSnapshot(clock.GetUtcNow(), "partial", [], "blocked", "device-read-failed"));
        });
        await service.ConfigureAsync(new(SamplingProfile: "slow"), CancellationToken.None);
        var slow = await service.CaptureAsync(CancellationToken.None);
        clock.Advance(TimeSpan.FromSeconds(3));
        Assert.Same(slow, await service.CaptureAsync(CancellationToken.None));

        await service.ConfigureAsync(new(SamplingProfile: "fastest"), CancellationToken.None);
        var fastest = await service.CaptureAsync(CancellationToken.None);
        Assert.Equal(2, reads);
        Assert.Equal("blocked", fastest.DriverStatus);
        Assert.Equal("device-read-failed", fastest.ErrorCode);
        clock.Advance(TimeSpan.FromMilliseconds(499));
        Assert.Same(fastest, await service.CaptureAsync(CancellationToken.None));
        clock.Advance(TimeSpan.FromMilliseconds(1));
        await service.CaptureAsync(CancellationToken.None);
        Assert.Equal(3, reads);
    }

    [Fact]
    public async Task ProfileChange_DoesNotCancelAnInFlightCollectorRead()
    {
        var clock = new TestClock();
        var pending = new TaskCompletionSource<SystemSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
        var reads = 0;
        CancellationToken readerToken = default;
        await using var service = Create(clock, token =>
        {
            readerToken = token;
            return ++reads == 1 ? new ValueTask<SystemSnapshot>(pending.Task)
                : ValueTask.FromResult(new SystemSnapshot(clock.GetUtcNow(), "partial", []));
        });
        await service.SetTrackingAsync(true, CancellationToken.None);

        var changing = service.ConfigureAsync(new(SamplingProfile: "fastest"), CancellationToken.None).AsTask();
        Assert.False(changing.IsCompleted);
        Assert.False(readerToken.IsCancellationRequested);
        pending.SetResult(new SystemSnapshot(clock.GetUtcNow(), "partial", []));
        await changing;
        Assert.Equal(2, reads);
    }

    [Fact]
    public async Task MissingDriver_LeavesAdvancedActivationExplicitWithoutCollecting()
    {
        var clock = new TestClock();
        var reads = 0;
        await using var service = Create(clock, _ =>
        {
            reads++;
            return ValueTask.FromResult(new SystemSnapshot(clock.GetUtcNow(), "partial", [], "available"));
        });
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.EnableAdvancedAsync(CancellationToken.None));
        await service.ConfigureAsync(new(UseAdvancedSensors: true), CancellationToken.None);
        Assert.Equal(0, reads);
        Assert.Equal("available", (await service.CaptureAsync(CancellationToken.None)).DriverStatus);
        await service.ConfigureAsync(new(Enabled: false, UseAdvancedSensors: true), CancellationToken.None);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.EnableAdvancedAsync(CancellationToken.None));
        Assert.Equal(1, reads);
    }

    [Theory]
    [InlineData("2.2.0")]
    [InlineData("2.2.0.0")]
    [InlineData("3.0.0")]
    public async Task InstalledDriver_RestoresSavedAdvancedPreferenceOnceWithoutSetup(string version)
    {
        var clock = new TestClock();
        var launches = 0;
        var reads = 0;
        await using var service = Create(clock, _ =>
        {
            reads++;
            return ValueTask.FromResult(new SystemSnapshot(clock.GetUtcNow(), "partial", [], "active"));
        }, Version.Parse(version), _ => { launches++; return Task.CompletedTask; });

        await service.ConfigureAsync(new(UseAdvancedSensors: true), CancellationToken.None);
        Assert.Equal(1, launches);
        Assert.Equal(0, reads);
        await service.ConfigureAsync(new(UseAdvancedSensors: true), CancellationToken.None);
        await service.ConfigureAsync(new(UseAdvancedSensors: true, SamplingProfile: "fast"), CancellationToken.None);
        Assert.Equal("active", (await service.CaptureAsync(CancellationToken.None)).DriverStatus);
        Assert.Equal(1, launches);
    }

    [Theory]
    [InlineData(false, true, "2.2.0")]
    [InlineData(true, false, "2.2.0")]
    [InlineData(true, true, null)]
    [InlineData(true, true, "2.1.0")]
    public async Task Startup_DoesNotLaunchForDisabledPreferencesOrMissingPrerequisites(bool enabled, bool advanced, string? version)
    {
        var clock = new TestClock();
        await using var service = Create(clock, _ => throw new InvalidOperationException("Startup must not sample hardware."),
            version is null ? null : Version.Parse(version));

        await service.ConfigureAsync(new(Enabled: enabled, UseAdvancedSensors: advanced), CancellationToken.None);
    }

    [Fact]
    public async Task ConcurrentInitialConfiguration_SharesOneConsentRequest()
    {
        var clock = new TestClock();
        var consent = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var launches = 0;
        await using var service = Create(clock, _ => throw new InvalidOperationException(), new Version(2, 2, 0), _ =>
        {
            launches++;
            return consent.Task;
        });
        var first = service.ConfigureAsync(new(UseAdvancedSensors: true), CancellationToken.None).AsTask();
        var second = service.ConfigureAsync(new(UseAdvancedSensors: true), CancellationToken.None).AsTask();
        Assert.Equal(1, launches);
        Assert.False(second.IsCompleted);
        consent.SetResult();
        await Task.WhenAll(first, second);
        Assert.Equal(1, launches);
    }

    [Theory]
    [InlineData(true, "advanced-consent-cancelled")]
    [InlineData(false, "collector-timeout")]
    public async Task FailedStartup_ReportsFailureThenUsesBasicReadingsWithoutRepeatingConsent(bool declined, string expectedCode)
    {
        var clock = new TestClock();
        var launches = 0;
        var reads = 0;
        await using var service = Create(clock, _ =>
        {
            reads++;
            return ValueTask.FromResult(new SystemSnapshot(clock.GetUtcNow(), "partial", [], "available"));
        }, new Version(2, 2, 0), _ =>
        {
            launches++;
            return Task.FromException(declined ? new Win32Exception(1223) : new OperationCanceledException());
        });

        await service.ConfigureAsync(new(UseAdvancedSensors: true), CancellationToken.None);
        var failed = await service.CaptureAsync(CancellationToken.None);
        Assert.Equal("unavailable", failed.Status);
        Assert.Equal(expectedCode, failed.ErrorCode);
        Assert.Equal(0, reads);
        clock.Advance(TimeSpan.FromSeconds(11));
        Assert.Equal("partial", (await service.CaptureAsync(CancellationToken.None)).Status);
        await service.ConfigureAsync(new(UseAdvancedSensors: true, SamplingProfile: "fast"), CancellationToken.None);
        await service.ConfigureAsync(new(UseAdvancedSensors: false), CancellationToken.None);
        await service.ConfigureAsync(new(UseAdvancedSensors: true), CancellationToken.None);
        Assert.Equal(1, launches);

        // A retry remains available through the existing explicit action.
        if (declined) await Assert.ThrowsAsync<Win32Exception>(() => service.EnableAdvancedAsync(CancellationToken.None));
        else await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.EnableAdvancedAsync(CancellationToken.None));
        Assert.Equal(2, launches);
    }

    [Fact]
    public async Task SettingsOptInAfterStartup_DoesNotRequestUnexpectedConsent()
    {
        await using var service = Create(new TestClock(), _ => throw new InvalidOperationException(), new Version(2, 2, 0));
        await service.ConfigureAsync(new(), CancellationToken.None);
        await service.ConfigureAsync(new(UseAdvancedSensors: true), CancellationToken.None);
    }

    [Fact]
    public async Task InvalidInstalledVersion_IsReportedWithoutPreventingStartup()
    {
        var clock = new TestClock();
        var installer = new PawnIoInstaller(Path.Combine(Path.GetTempPath(), "PawnIO.UnitTests.exe"),
            () => throw new InvalidDataException("Invalid installation metadata."),
            _ => throw new InvalidOperationException("Startup must not run setup."));
        await using var service = new HardwareTelemetryService(Path.Combine(Path.GetTempPath(), "WorkTrail.Hardware.UnitTests.exe"),
            clock, installer: installer, startCollector: _ => throw new InvalidOperationException("No launch was expected."));

        await service.ConfigureAsync(new(UseAdvancedSensors: true), CancellationToken.None);
        var failed = await service.CaptureAsync(CancellationToken.None);
        Assert.Equal("unavailable", failed.Status);
        Assert.Equal("advanced-start-failed", failed.ErrorCode);
        Assert.Equal("blocked", failed.DriverStatus);
    }

    [Fact]
    public async Task CancelledInitialization_PropagatesCancellationWithoutAnAutomaticRetry()
    {
        var clock = new TestClock();
        var launches = 0;
        using var cancellation = new CancellationTokenSource();
        await using var service = Create(clock, _ => throw new InvalidOperationException(), new Version(2, 2, 0), token =>
        {
            launches++;
            cancellation.Cancel();
            return Task.FromCanceled(token);
        });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            service.ConfigureAsync(new(UseAdvancedSensors: true), cancellation.Token).AsTask());
        await service.ConfigureAsync(new(UseAdvancedSensors: true), CancellationToken.None);
        Assert.Equal(1, launches);
    }

    [Fact]
    public async Task InvalidConfiguration_IsRejectedWithoutChangingTheService()
    {
        var clock = new TestClock();
        await using var service = Create(clock, _ => ValueTask.FromResult(new SystemSnapshot(clock.GetUtcNow(), "partial", [])));
        await service.ConfigureAsync(new(Enabled: false), CancellationToken.None);
        await Assert.ThrowsAsync<ArgumentException>(() => service.ConfigureAsync(new(SamplingProfile: "invalid"), CancellationToken.None).AsTask());
        Assert.Equal("disabled", (await service.CaptureAsync(CancellationToken.None)).Status);
    }

    private static HardwareTelemetryService Create(TestClock clock, Func<CancellationToken, ValueTask<SystemSnapshot>> read,
        Version? installedVersion = null, Func<CancellationToken, Task>? startCollector = null) =>
        new(Path.Combine(Path.GetTempPath(), "WorkTrail.Hardware.UnitTests.exe"), clock, reader: read,
            installer: new PawnIoInstaller(Path.Combine(Path.GetTempPath(), "PawnIO.UnitTests.exe"), () => installedVersion,
                _ => throw new InvalidOperationException("Automatic startup must never run driver setup.")),
            startCollector: startCollector ?? (_ => throw new InvalidOperationException("No collector launch was expected.")));

    private sealed class TestClock : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 9, 17, 0, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => _now;
        internal void Advance(TimeSpan duration) => _now += duration;
    }
}
