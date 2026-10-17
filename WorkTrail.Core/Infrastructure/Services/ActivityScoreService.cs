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
    private const double MaximumDurableActivityContribution = MaximumInputContribution + MaximumActiveContribution;
    private readonly object _gate = new();
    private readonly Dictionary<DateTimeOffset, MinuteAggregate> _minutes = new();

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

    /// <summary>Builds the persisted activity boundaries for one screenshot interval.</summary>
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

        return new ScreenshotIntervalTelemetry(fromUtc, toUtc);
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
                    aggregate.ActiveSeconds));
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
    /// Calculates a durable interval activity index using the same input and active-time weights as the live score.
    /// </summary>
    internal static int CalculateIntervalActivityIndex(
        IEnumerable<ActivitySample> samples,
        double intervalMinutes)
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
            Math.Clamp(activeSeconds / (double)maximumActiveSeconds, 0d, 1d));
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

        // Normalize the durable input and active-time contributions to the displayed 0-100 range.
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
        aggregate.ActiveSeconds > 0 ? 1d : 0d);

    private static int CalculateScore(
        double keyPresses,
        double mouseClicks,
        double activeRatio)
    {
        // Input is deliberately dominant; normalize the same durable weights used by historical scores.
        var inputContribution = Math.Min(MaximumInputContribution, (keyPresses * 0.55d) + (mouseClicks * 3.5d));
        var activeContribution = Math.Clamp(activeRatio, 0d, 1d) * MaximumActiveContribution;
        return (int)Math.Round(Math.Clamp((inputContribution + activeContribution) * 100d / MaximumDurableActivityContribution, 0d, 100d));
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
    }

}
