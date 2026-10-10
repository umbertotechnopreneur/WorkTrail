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
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using WorkTrail.Application;
using WorkTrail.Runtime;
using WorkTrail.Services;
using Xunit;

namespace WorkTrail.Core.Tests;

public sealed class HardwareTelemetryRuntimeTests
{
    /// <summary>A live sensor preference can drain hardware work instead of using the short general-settings deadline.</summary>
    [Fact]
    public async Task SensorSettings_UseHardwareAwareTimeout()
    {
        var application = DispatchProxy.Create<IWorkTrailApplication, HardwareRuntimeProxy>();
        var installation = $"hardware-settings-test-{Guid.NewGuid():N}";
        await using var host = new RuntimeHost(application, installation);
        Assert.True(host.TryStart());
        await using var client = new RuntimeClient(installation, TimeSpan.Zero);

        var result = await client.PatchSettingsAsync(new SettingsPatch(new Dictionary<string, string?>
        {
            ["sensors.sampling_profile"] = "fast"
        }), CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal("fast", result.Value!.HardwareSamplingProfile);
    }

    /// <summary>Allows hardware collection and explicit consent to outlive the short default IPC timeout.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task HardwareOperations_UseDedicatedTimeoutAndRoundTripTheSnapshot(bool advanced)
    {
        var application = DispatchProxy.Create<IWorkTrailApplication, HardwareRuntimeProxy>();
        var installation = $"hardware-timeout-test-{Guid.NewGuid():N}";
        await using var host = new RuntimeHost(application, installation);
        Assert.True(host.TryStart());
        await using var client = new RuntimeClient(installation, TimeSpan.Zero);

        var result = advanced
            ? await client.EnableAdvancedHardwareTelemetryAsync(CancellationToken.None)
            : await client.CaptureSystemSnapshotAsync(CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal("ready", result.Value!.Status);
        Assert.Equal(25, HardwareUsageProjection.Read(result.Value).Cpu);
        Assert.Equal(advanced ? nameof(IWorkTrailApplication.EnableAdvancedHardwareTelemetryAsync)
            : nameof(IWorkTrailApplication.CaptureSystemSnapshotAsync), ((HardwareRuntimeProxy)application).Operation);
        Assert.True(RuntimeClient.HardwareAdvancedTimeout > TimeSpan.FromMinutes(6));
        Assert.True(RuntimeClient.HardwareSnapshotTimeout > TimeSpan.FromSeconds(8));
        Assert.True(RuntimeClient.ScreenshotCaptureTimeout > RuntimeClient.HardwareSnapshotTimeout);
    }

    /// <summary>The live monitor uses its hardware-only IPC operation rather than system context capture.</summary>
    [Fact]
    public async Task LiveMonitor_UsesSharedHardwareOnlyOperation()
    {
        var application = DispatchProxy.Create<IWorkTrailApplication, HardwareRuntimeProxy>();
        var installation = $"sensor-monitor-test-{Guid.NewGuid():N}";
        await using var host = new RuntimeHost(application, installation);
        Assert.True(host.TryStart());
        await using var client = new RuntimeClient(installation, TimeSpan.Zero);
        var result = await client.CaptureHardwareSnapshotAsync(CancellationToken.None);
        Assert.True(result.Succeeded);
        Assert.Equal(nameof(IWorkTrailApplication.CaptureHardwareSnapshotAsync), ((HardwareRuntimeProxy)application).Operation);
        Assert.Null(result.Value!.DeviceContext);
        Assert.Equal(new WindowMinimumSize(400, 360), WindowStateService.GetMinimumSize(WindowStateKeys.Sensors));
    }

    public class HardwareRuntimeProxy : DispatchProxy
    {
        public string? Operation { get; private set; }

        /// <summary>Returns inert telemetry without starting a collector or asking Windows for consent.</summary>
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod?.Name == nameof(IWorkTrailApplication.PatchSettingsAsync))
                return Task.FromResult(SettingsCatalog.Apply(new AppSettings(), (SettingsPatch)args![0]!));
            if (targetMethod?.Name is nameof(IWorkTrailApplication.CaptureSystemSnapshotAsync)
                or nameof(IWorkTrailApplication.CaptureHardwareSnapshotAsync)
                or nameof(IWorkTrailApplication.EnableAdvancedHardwareTelemetryAsync))
            {
                Operation = targetMethod.Name;
                return Task.FromResult(OperationResult<SystemSnapshot>.Success(
                    "system.snapshot.captured", "SystemSnapshotCaptured", HardwareTestData.Snapshot(DateTimeOffset.UtcNow)));
            }
            if (targetMethod?.Name == nameof(IAsyncDisposable.DisposeAsync)) return ValueTask.CompletedTask;
            if (targetMethod?.Name is "add_RuntimeStateChanged" or "remove_RuntimeStateChanged") return null;
            throw new NotSupportedException(targetMethod?.Name);
        }
    }
}
