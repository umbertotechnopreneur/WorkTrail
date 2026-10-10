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


using System.Globalization;

namespace WorkTrail.Services;

/// <summary>Normalizes the optional, informational weekly schedule shared by settings, UI, and AI prompts.</summary>
public static class ActiveHoursSchedule
{
    /// <summary>Gets the required granularity for every active-hours boundary.</summary>
    public const int BoundaryMinutes = 15;

    /// <summary>Gets the canonical lowercase English weekday identifiers in display order.</summary>
    public static IReadOnlyList<string> Days { get; } = Array.AsReadOnly(
        new[] { "monday", "tuesday", "wednesday", "thursday", "friday", "saturday", "sunday" });

    private static readonly IReadOnlyList<ActiveHoursDay> DefaultSchedule =
        Days.Select(static day => new ActiveHoursDay(day, "00:00-24:00")).ToArray();

    internal static IReadOnlyList<ActiveHoursDay> Normalize(IReadOnlyList<ActiveHoursDay>? configuredDays)
    {
        var source = configuredDays ?? DefaultSchedule;
        return Days.Select(day =>
        {
            var configured = source.LastOrDefault(candidate =>
                string.Equals(candidate.Day, day, StringComparison.OrdinalIgnoreCase));
            if (!TryNormalizeActivePeriod(configured?.ActivePeriod, out var active)
                || !TryNormalizeBreakPeriods(configured?.BreakPeriods, out var breaks))
            {
                throw new InvalidDataException(
                    $"Active-hours boundaries for '{day}' must be valid {BoundaryMinutes}-minute increments.");
            }

            if (!string.IsNullOrEmpty(breaks)
                && (string.IsNullOrEmpty(active) || !BreaksFitActivePeriod(active, breaks)))
            {
                throw new InvalidDataException($"Active-hours breaks for '{day}' must fit its active period.");
            }

            return new ActiveHoursDay(day, active, breaks);
        }).ToArray();
    }

    internal static IReadOnlyList<ActiveHoursDay> Update(
        IReadOnlyList<ActiveHoursDay>? configuredDays,
        string day,
        bool breaks,
        string value)
    {
        var normalized = Normalize(configuredDays).ToArray();
        var index = Array.FindIndex(normalized, entry => string.Equals(entry.Day, day, StringComparison.Ordinal));
        if (index < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(day));
        }

        normalized[index] = breaks
            ? normalized[index] with { BreakPeriods = value }
            : normalized[index] with { ActivePeriod = value };
        return normalized;
    }

    internal static bool TryNormalizeActivePeriod(string? value, out string normalized) =>
        TryNormalizeRanges(value, allowMultiple: false, out normalized);

    internal static bool TryNormalizeBreakPeriods(string? value, out string normalized) =>
        TryNormalizeRanges(value, allowMultiple: true, out normalized);

    internal static bool IsValid(IReadOnlyList<ActiveHoursDay>? configuredDays)
    {
        foreach (var dayName in Days)
        {
            var configured = configuredDays?.LastOrDefault(candidate =>
                string.Equals(candidate.Day, dayName, StringComparison.OrdinalIgnoreCase));
            if (!TryNormalizeActivePeriod(configured?.ActivePeriod, out var active)
                || !TryNormalizeBreakPeriods(configured?.BreakPeriods, out var breaks)
                || (!string.IsNullOrEmpty(breaks)
                    && (string.IsNullOrEmpty(active) || !BreaksFitActivePeriod(active, breaks))))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Determines whether at least one day contains an eligible scheduled-snapshot period.</summary>
    internal static bool HasAnyActivePeriod(IReadOnlyList<ActiveHoursDay>? configuredDays) =>
        Normalize(configuredDays).Any(day => !string.IsNullOrEmpty(day.ActivePeriod));

    /// <summary>Builds the current-day note that is sent only as non-enforcing AI context.</summary>
    /// <param name="configuredDays">The configured weekly schedule.</param>
    /// <param name="timestamp">The instant represented by the snapshot.</param>
    /// <param name="timeZone">Optional device time zone, primarily used for deterministic tests.</param>
    internal static string? BuildInformationalNote(
        IReadOnlyList<ActiveHoursDay>? configuredDays,
        DateTimeOffset timestamp,
        TimeZoneInfo? timeZone = null)
    {
        // Snapshot timestamps are UTC, but a weekday schedule is always interpreted in the device's current local time zone.
        var localTimestamp = TimeZoneInfo.ConvertTime(timestamp, timeZone ?? TimeZoneInfo.Local);
        var day = localTimestamp.ToString("dddd", CultureInfo.InvariantCulture).ToLowerInvariant();
        var entry = Normalize(configuredDays).Single(candidate => candidate.Day == day);
        if (string.IsNullOrEmpty(entry.ActivePeriod))
        {
            return null;
        }

        var label = CultureInfo.InvariantCulture.TextInfo.ToTitleCase(entry.Day);
        return string.IsNullOrEmpty(entry.BreakPeriods)
            ? $"{label}: planned active hours {entry.ActivePeriod}. This is informational only."
            : $"{label}: planned active hours {entry.ActivePeriod}; planned breaks {entry.BreakPeriods}. This is informational only.";
    }

    /// <summary>Determines whether a local timestamp falls within a configured working period and outside its breaks.</summary>
    internal static bool IsWithinActiveHours(IReadOnlyList<ActiveHoursDay>? configuredDays, DateTimeOffset timestamp)
    {
        var day = timestamp.DayOfWeek.ToString().ToLowerInvariant();
        var entry = Normalize(configuredDays).Single(candidate => candidate.Day == day);
        var localMinutes = (timestamp.Hour * 60) + timestamp.Minute;
        return TryParseRange(entry.ActivePeriod, out var activeStart, out var activeEnd)
            && localMinutes >= activeStart
            && localMinutes < activeEnd
            && !entry.BreakPeriods.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                .Any(range => TryParseRange(range, out var breakStart, out var breakEnd)
                    && localMinutes >= breakStart
                    && localMinutes < breakEnd);
    }

    private static bool TryNormalizeRanges(string? value, bool allowMultiple, out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        var ranges = value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (ranges.Length == 0 || (!allowMultiple && ranges.Length != 1))
        {
            return false;
        }

        var normalizedRanges = new List<string>(ranges.Length);
        foreach (var range in ranges)
        {
            if (!TryParseRange(range, out var start, out var end)
                || end <= start
                || start % BoundaryMinutes != 0
                || end % BoundaryMinutes != 0)
            {
                return false;
            }

            normalizedRanges.Add($"{FormatBoundary(start)}-{FormatBoundary(end)}");
        }

        normalized = string.Join(", ", normalizedRanges);
        return true;
    }

    private static bool BreaksFitActivePeriod(string activePeriod, string breakPeriods)
    {
        if (string.IsNullOrEmpty(breakPeriods))
        {
            return true;
        }

        if (!TryParseRange(activePeriod, out var activeStart, out var activeEnd))
        {
            return false;
        }

        return breakPeriods.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .All(range => TryParseRange(range, out var breakStart, out var breakEnd)
                && breakStart >= activeStart
                && breakEnd <= activeEnd);
    }

    /// <summary>Parses an <c>HH:mm-HH:mm</c> range into minute boundaries.</summary>
    /// <param name="value">Range to parse.</param>
    /// <param name="start">Start boundary in minutes after midnight.</param>
    /// <param name="end">End boundary in minutes after midnight; <c>24:00</c> is supported.</param>
    /// <returns><see langword="true"/> when both boundaries are valid.</returns>
    public static bool TryParseRange(string value, out int start, out int end)
    {
        start = default;
        end = default;
        var parts = value.Split('-', StringSplitOptions.TrimEntries);
        return parts.Length == 2
            && TryParseBoundary(parts[0], allowEndOfDay: false, out start)
            && TryParseBoundary(parts[1], allowEndOfDay: true, out end);
    }

    private static bool TryParseBoundary(string value, bool allowEndOfDay, out int minutes)
    {
        if (allowEndOfDay && string.Equals(value, "24:00", StringComparison.Ordinal))
        {
            minutes = 24 * 60;
            return true;
        }

        if (TimeOnly.TryParseExact(value, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var time))
        {
            minutes = (time.Hour * 60) + time.Minute;
            return true;
        }

        minutes = default;
        return false;
    }

    private static string FormatBoundary(int minutes) =>
        minutes == 24 * 60
            ? "24:00"
            : $"{minutes / 60:00}:{minutes % 60:00}";
}
