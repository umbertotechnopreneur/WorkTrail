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
using System.Threading;
using System.Threading.Tasks;
using WorkTrail.Services;

namespace WorkTrail.Core.Tests;

internal static class HardwareTestData
{
    internal static SystemSnapshot Snapshot(DateTimeOffset timestamp, double? cpu = 25, double? gpu = 15) => new(
        timestamp,
        "ready",
        [
            new("/cpu/0", "Test CPU", "Cpu", timestamp,
                [new("/cpu/0/load/0", "CPU Total", "Load", "%", cpu)]),
            new("/gpu/0", "Test GPU", "GpuNvidia", timestamp,
                [new("/gpu/0/load/0", "GPU Core", "Load", "%", gpu)])
        ]);
}

internal sealed class FakeHardwareTelemetryService : IHardwareTelemetryService
{
    internal SystemSnapshot? Snapshot { get; set; }
    internal int CaptureCount { get; private set; }
    internal bool IsTracking { get; private set; }
    internal bool AdvancedEnabled { get; private set; }
    internal HardwareTelemetryConfiguration Configuration { get; private set; } = new();

    public ValueTask ConfigureAsync(HardwareTelemetryConfiguration configuration, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _ = HardwareSamplingProfiles.Get(configuration.SamplingProfile);
        Configuration = configuration;
        if (!configuration.Enabled || !configuration.UseAdvancedSensors) AdvancedEnabled = false;
        return ValueTask.CompletedTask;
    }

    public ValueTask<SystemSnapshot> CaptureAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!Configuration.Enabled)
            return ValueTask.FromResult(new SystemSnapshot(DateTimeOffset.UtcNow, "disabled", [], "disabled"));
        CaptureCount++;
        return ValueTask.FromResult(Snapshot ?? HardwareTestData.Snapshot(DateTimeOffset.UtcNow));
    }

    public ValueTask SetTrackingAsync(bool isTracking, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IsTracking = isTracking;
        return ValueTask.CompletedTask;
    }

    public Task EnableAdvancedAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!Configuration.Enabled || !Configuration.UseAdvancedSensors) throw new InvalidOperationException("Advanced sensors are not enabled.");
        AdvancedEnabled = true;
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
