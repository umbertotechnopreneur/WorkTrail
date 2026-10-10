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


using System.Collections.Generic;
using WorkTrail.Application;

namespace WorkTrail.Services;

/// <summary>
/// Aggregates the latest thirty one-minute activity buckets for the live player score.
/// </summary>
public sealed class ActivityScoreService
{
    /// <summary>Gets the fixed number of one-minute bars retained by the compact player.</summary>
    public const int WindowMinutes = 30;

    private const int MaximumSnapshotIntervalMinutes = WindowMinutes / 2;
    private const double MaximumInputContribution = 86d;
    private const double MaximumActiveContribution = 8d;
    private const double MaximumCpuContribution = 4d;
    private const double MaximumGpuContribution = 2d;
    private const double MaximumDurableActivityContribution = MaximumInputContribution + MaximumActiveContribution;
    private readonly object _gate = new();
    private readonly Dictionary<DateTimeOffset, MinuteAggregate> _minutes = new();
    private readonly Dictionary<DateTimeOffset, SystemTelemetryPoint> _telemetryPoints = new();

    /// <summary>Records keyboard, click, and active-time contributions from one durable activity sample.</summary>
    public void RecordSample(ActivitySample sample)
    {
        ArgumentNullException.ThrowIfNull(sample);

        lock (_gate)
        {
            var aggregate = GetOrCreate(TruncateToMinute(sample.Timestamp));
            aggregate.KeyPresses += sample.KeyPresses;
            aggregate.MouseClicks += sample.MouseClicks;
            if (string.Equals(sample.State, "active", StringComparison.OrdinalIgnoreCase))
            {
                aggregate.ActiveSeconds = Math.Min(60, aggregate.ActiveSeconds + sample.DurationSeconds);
            }
        }
    }

    /// <summary>Records the low-weight system telemetry captured by the application facade.</summary>
    public void RecordSystemSnapshot(SystemSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var usage = HardwareUsageProjection.Read(snapshot);

        lock (_gate)
        {
            var aggregate = GetOrCreate(TruncateToMinute(snapshot.Timestamp));
            aggregate.CpuUsagePercent = usage.Cpu;
            aggregate.GpuUsagePercent = usage.Gpu;
            if (usage.Cpu is not null || usage.Gpu is not null)
            {
                var timestamp = snapshot.Timestamp.ToUniversalTime();
                // The shared collector can return one cached reading to several callers.
                // Count that measured instant once while continuing to refresh the minute display.
                _telemetryPoints[timestamp] = new SystemTelemetryPoint(
                    timestamp,
                    aggregate.CpuUsagePercent,
                    aggregate.GpuUsagePercent);
            }
        }
    }

    /// <summary>Builds the persisted CPU/GPU averages for one screenshot interval.</summary>
    public ScreenshotIntervalTelemetry BuildScreenshotIntervalTelemetry(
        DateTimeOffset intervalStartedAt,
        DateTimeOffset capturedAt)
    {
        var fromUtc = intervalStartedAt.ToUniversalTime();
        var toUtc = capturedAt.ToUniversalTime();
        if (fromUtc >= toUtc)
        {
            throw new ArgumentOutOfRangeException(nameof(intervalStartedAt), "Screenshot telemetry interval must end after it starts.");
        }

        lock (_gate)
        {
            Trim(toUtc.AddMinutes(-WindowMinutes));
            var points = _telemetryPoints.Values
                .Where(point => point.TimestampUtc > fromUtc && point.TimestampUtc <= toUtc)
                .ToArray();
            var cpuPoints = points.Where(point => point.CpuUsagePercent is not null).ToArray();
            var gpuPoints = points.Where(point => point.GpuUsagePercent is not null).ToArray();
            return new ScreenshotIntervalTelemetry(
                fromUtc,
                toUtc,
                cpuPoints.Length == 0 ? null : (int)Math.Round(cpuPoints.Average(point => point.CpuUsagePercent!.Value)),
                gpuPoints.Length == 0 ? null : (int)Math.Round(gpuPoints.Average(point => point.GpuUsagePercent!.Value)));
        }
    }

    /// <summary>Builds a trailing thirty-minute immutable rendering state at one-minute resolution.</summary>
    public ActivityScoreState GetState(int screenshotIntervalMinutes, DateTimeOffset? windowEndUtc = null)
    {
        var windowEnd = TruncateToMinute((windowEndUtc ?? DateTimeOffset.UtcNow).ToUniversalTime());
        var windowStart = windowEnd.AddMinutes(-(WindowMinutes - 1));
        var intervalMinutes = Math.Clamp(screenshotIntervalMinutes, 1, MaximumSnapshotIntervalMinutes);

        lock (_gate)
        {
            Trim(windowStart);
            var minutes = new List<ActivityScoreMinute>(WindowMinutes);
            for (var index = 0; index < WindowMinutes; index++)
            {
                var minute = windowStart.AddMinutes(index);
                var aggregate = GetOrCreate(minute);
                minutes.Add(new ActivityScoreMinute(
                    minute,
                    CalculateScore(aggregate),
                    aggregate.KeyPresses,
                    aggregate.MouseClicks,
                    aggregate.ActiveSeconds,
                    aggregate.CpuUsagePercent,
                    aggregate.GpuUsagePercent));
            }

            var latestStart = windowEnd.AddMinutes(-(intervalMinutes - 1));
            var previousStart = latestStart.AddMinutes(-intervalMinutes);
            return new ActivityScoreState(
                windowStart,
                windowEnd,
                intervalMinutes,
                minutes,
                minutes[^1].Score,
                BuildInterval(minutes, previousStart, latestStart.AddMinutes(-1)),
                BuildInterval(minutes, latestStart, windowEnd));
        }
    }

    /// <summary>
    /// Calculates a durable interval activity index using the same input, active-time, CPU, and GPU weights as the live score.
    /// </summary>
    internal static int CalculateIntervalActivityIndex(
        IEnumerable<ActivitySample> samples,
        double intervalMinutes,
        int? cpuUsagePercent,
        int? gpuUsagePercent)
    {
        ArgumentNullException.ThrowIfNull(samples);
        var normalizedIntervalMinutes = Math.Max(1d, intervalMinutes);
        var keyPresses = 0L;
        var mouseClicks = 0L;
        var activeSeconds = 0L;
        foreach (var sample in samples)
        {
            keyPresses = checked(keyPresses + sample.KeyPresses);
            mouseClicks = checked(mouseClicks + sample.MouseClicks);
            if (string.Equals(sample.State, "active", StringComparison.OrdinalIgnoreCase))
            {
                activeSeconds = checked(activeSeconds + sample.DurationSeconds);
            }
        }

        var maximumActiveSeconds = checked(normalizedIntervalMinutes * 60L);
        return CalculateScore(
            keyPresses / (double)normalizedIntervalMinutes,
            mouseClicks / (double)normalizedIntervalMinutes,
            Math.Clamp(activeSeconds / (double)maximumActiveSeconds, 0d, 1d),
            cpuUsagePercent,
            gpuUsagePercent);
    }

    /// <summary>
    /// Calculates a normalized 0-100 score for a historical time bucket using only durable activity-history counters.
    /// </summary>
    internal static int CalculateHistoricalActivityScore(
        long keyPresses,
        long mouseClicks,
        double activeSeconds,
        double trackedSeconds)
    {
        if (keyPresses < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(keyPresses));
        }

        if (mouseClicks < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(mouseClicks));
        }

        if (!double.IsFinite(activeSeconds) || activeSeconds < 0d)
        {
            throw new ArgumentOutOfRangeException(nameof(activeSeconds));
        }

        if (!double.IsFinite(trackedSeconds) || trackedSeconds < 0d)
        {
            throw new ArgumentOutOfRangeException(nameof(trackedSeconds));
        }

        var trackedMinutes = Math.Max(1d, trackedSeconds / 60d);
        var inputContribution = Math.Min(
            MaximumInputContribution,
            (keyPresses / trackedMinutes * 0.55d) + (mouseClicks / trackedMinutes * 3.5d));
        var activeRatio = trackedSeconds <= 0d
            ? 0d
            : Math.Clamp(activeSeconds / trackedSeconds, 0d, 1d);
        var activeContribution = activeRatio * MaximumActiveContribution;

        // Daily history has no complete CPU/GPU series, so normalize the durable 94-point budget to 0-100.
        var normalizedScore = (inputContribution + activeContribution) * 100d / MaximumDurableActivityContribution;
        return (int)Math.Round(Math.Clamp(normalizedScore, 0d, 100d));
    }

    private MinuteAggregate GetOrCreate(DateTimeOffset minuteUtc)
    {
        if (_minutes.TryGetValue(minuteUtc, out var aggregate))
        {
            return aggregate;
        }

        aggregate = new MinuteAggregate();
        _minutes.Add(minuteUtc, aggregate);
        return aggregate;
    }

    private void Trim(DateTimeOffset oldestMinute)
    {
        var stale = _minutes.Keys.Where(minute => minute < oldestMinute).ToArray();
        foreach (var minute in stale)
        {
            _minutes.Remove(minute);
        }

        foreach (var timestamp in _telemetryPoints.Keys.Where(timestamp => timestamp < oldestMinute).ToArray())
        {
            _telemetryPoints.Remove(timestamp);
        }
    }

    private static ActivityScoreInterval BuildInterval(
        IEnumerable<ActivityScoreMinute> minutes,
        DateTimeOffset fromInclusive,
        DateTimeOffset toInclusive)
    {
        var keys = 0L;
        var clicks = 0L;
        foreach (var minute in minutes)
        {
            if (minute.MinuteUtc < fromInclusive || minute.MinuteUtc > toInclusive)
            {
                continue;
            }

            keys += minute.KeyPresses;
            clicks += minute.MouseClicks;
        }

        return new ActivityScoreInterval(keys, clicks);
    }

    private static int CalculateScore(MinuteAggregate aggregate) => CalculateScore(
        aggregate.KeyPresses,
        aggregate.MouseClicks,
        aggregate.ActiveSeconds > 0 ? 1d : 0d,
        aggregate.CpuUsagePercent,
        aggregate.GpuUsagePercent);

    private static int CalculateScore(
        double keyPresses,
        double mouseClicks,
        double activeRatio,
        int? cpuUsagePercent,
        int? gpuUsagePercent)
    {
        // Input is deliberately dominant; CPU and GPU together can add at most six points.
        var inputContribution = Math.Min(MaximumInputContribution, (keyPresses * 0.55d) + (mouseClicks * 3.5d));
        var activeContribution = Math.Clamp(activeRatio, 0d, 1d) * MaximumActiveContribution;
        var cpuContribution = Math.Clamp(cpuUsagePercent ?? 0, 0, 100) * MaximumCpuContribution / 100d;
        var gpuContribution = Math.Clamp(gpuUsagePercent ?? 0, 0, 100) * MaximumGpuContribution / 100d;
        return (int)Math.Round(Math.Clamp(inputContribution + activeContribution + cpuContribution + gpuContribution, 0d, 100d));
    }

    private static DateTimeOffset TruncateToMinute(DateTimeOffset value)
    {
        var utc = value.ToUniversalTime();
        return new DateTimeOffset(utc.Year, utc.Month, utc.Day, utc.Hour, utc.Minute, 0, TimeSpan.Zero);
    }

    private sealed class MinuteAggregate
    {
        public long KeyPresses { get; set; }
        public long MouseClicks { get; set; }
        public int ActiveSeconds { get; set; }
        public int? CpuUsagePercent { get; set; }
        public int? GpuUsagePercent { get; set; }
    }

    private sealed record SystemTelemetryPoint(DateTimeOffset TimestampUtc, int? CpuUsagePercent, int? GpuUsagePercent);
}
