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

namespace WorkTrail.Core.Tests;

public sealed class ActivityScoreServiceTests
{
    [Fact]
    public void GetState_UsesThirtyMinuteBucketsAndKeepsInputDominant()
    {
        var service = new ActivityScoreService();
        var windowEnd = new DateTimeOffset(2026, 8, 9, 10, 30, 0, TimeSpan.Zero);
        var windowStart = windowEnd.AddMinutes(-(ActivityScoreService.WindowMinutes - 1));

        for (var index = 0; index < ActivityScoreService.WindowMinutes; index++)
        {
            service.RecordSample(Sample(windowStart.AddMinutes(index), keys: 1, clicks: 2));
        }

        service.RecordSystemSnapshot(Snapshot(windowEnd, cpu: 100, gpu: 100));
        var state = service.GetState(15, windowEnd);

        Assert.Equal(ActivityScoreService.WindowMinutes, state.Minutes.Count);
        Assert.Equal(15, state.SnapshotIntervalMinutes);
        Assert.Equal(15, state.PreviousSnapshotInterval.KeyPresses);
        Assert.Equal(30, state.PreviousSnapshotInterval.MouseClicks);
        Assert.Equal(15, state.LatestSnapshotInterval.KeyPresses);
        Assert.Equal(30, state.LatestSnapshotInterval.MouseClicks);
        Assert.True(state.CurrentScore > 20);
    }

    [Fact]
    public void GetState_CpuAndGpuContributeMuchLessThanInput()
    {
        var windowEnd = new DateTimeOffset(2026, 8, 9, 10, 30, 0, TimeSpan.Zero);
        var telemetryOnly = new ActivityScoreService();
        telemetryOnly.RecordSystemSnapshot(Snapshot(windowEnd, cpu: 100, gpu: 100));

        var inputDriven = new ActivityScoreService();
        inputDriven.RecordSample(Sample(windowEnd, keys: 40, clicks: 8));

        Assert.True(inputDriven.GetState(15, windowEnd).CurrentScore > telemetryOnly.GetState(15, windowEnd).CurrentScore);
    }

    [Fact]
    public void CalculateIntervalActivityIndex_UsesPersistedTelemetryWithSharedWeights()
    {
        var intervalEnd = new DateTimeOffset(2026, 8, 9, 10, 30, 0, TimeSpan.Zero);
        var samples = Enumerable.Range(0, 15)
            .Select(index => Sample(intervalEnd.AddMinutes(-index), keys: 10, clicks: 2))
            .ToArray();

        var index = ActivityScoreService.CalculateIntervalActivityIndex(
            samples,
            intervalMinutes: 15,
            cpuUsagePercent: 50,
            gpuUsagePercent: 50);

        // Average input contributes 12.5 points, active time 0.67, CPU 2, and GPU 1.
        Assert.Equal(16, index);
    }

    [Fact]
    public void CalculateHistoricalActivityScore_NormalizesDurableHistoryToOneHundredPoints()
    {
        var score = ActivityScoreService.CalculateHistoricalActivityScore(
            keyPresses: 40,
            mouseClicks: 8,
            activeSeconds: 60,
            trackedSeconds: 60);

        // Input contributes 50 of 86 points and active time contributes 8 of 8; 58/94 normalizes to 62/100.
        Assert.Equal(62, score);
    }

    [Fact]
    public void CalculateHistoricalActivityScore_UsesOneMinuteFloorAndSaturatesAtOneHundred()
    {
        var shortObservedInterval = ActivityScoreService.CalculateHistoricalActivityScore(
            keyPresses: 40,
            mouseClicks: 8,
            activeSeconds: 1,
            trackedSeconds: 1);
        var saturated = ActivityScoreService.CalculateHistoricalActivityScore(
            keyPresses: 10_000,
            mouseClicks: 10_000,
            activeSeconds: 60,
            trackedSeconds: 60);

        Assert.Equal(62, shortObservedInterval);
        Assert.Equal(100, saturated);
    }

    [Fact]
    public void CalculateHistoricalActivityScore_RecordedIdleWithoutInputIsZero()
    {
        var score = ActivityScoreService.CalculateHistoricalActivityScore(
            keyPresses: 0,
            mouseClicks: 0,
            activeSeconds: 0,
            trackedSeconds: 60);

        Assert.Equal(0, score);
    }

    [Fact]
    public void BuildScreenshotIntervalTelemetry_AveragesOnlyPointsAfterPreviousCapture()
    {
        var service = new ActivityScoreService();
        var previousCapture = new DateTimeOffset(2026, 8, 9, 10, 0, 30, TimeSpan.Zero);
        service.RecordSystemSnapshot(Snapshot(previousCapture, cpu: 90, gpu: 90));
        service.RecordSystemSnapshot(Snapshot(previousCapture.AddMinutes(1), cpu: 20, gpu: 40));
        service.RecordSystemSnapshot(Snapshot(previousCapture.AddMinutes(2), cpu: 40, gpu: 60));

        var telemetry = service.BuildScreenshotIntervalTelemetry(previousCapture, previousCapture.AddMinutes(3));

        Assert.Equal(30, telemetry.CpuUsagePercent);
        Assert.Equal(50, telemetry.GpuUsagePercent);
    }

    /// <summary>Repeated consumers of one cached sensor instant cannot give it extra averaging weight.</summary>
    [Fact]
    public void BuildScreenshotIntervalTelemetry_CountsCachedTimestampOnceAndKeepsLatestMinute()
    {
        var service = new ActivityScoreService();
        var intervalStart = new DateTimeOffset(2026, 8, 9, 10, 0, 0, TimeSpan.Zero);
        var cached = Snapshot(intervalStart.AddSeconds(10), cpu: 20, gpu: 40);
        service.RecordSystemSnapshot(cached);
        service.RecordSystemSnapshot(cached);
        service.RecordSystemSnapshot(cached with { Timestamp = cached.Timestamp.ToOffset(TimeSpan.FromHours(7)) });
        service.RecordSystemSnapshot(Snapshot(intervalStart.AddSeconds(12), cpu: 80, gpu: 60));

        var telemetry = service.BuildScreenshotIntervalTelemetry(intervalStart, intervalStart.AddSeconds(15));
        var minute = service.GetState(5, intervalStart).Minutes[^1];

        Assert.Equal(50, telemetry.CpuUsagePercent);
        Assert.Equal(50, telemetry.GpuUsagePercent);
        Assert.Equal(80, minute.CpuUsagePercent);
        Assert.Equal(60, minute.GpuUsagePercent);
    }

    private static ActivitySample Sample(DateTimeOffset timestamp, long keys, long clicks) => new(
        timestamp, 5, "active", "test", "Test", "Test", "Test", "installation", keys, clicks);

    private static SystemSnapshot Snapshot(DateTimeOffset timestamp, int cpu, int gpu) =>
        HardwareTestData.Snapshot(timestamp, cpu, gpu);
}
