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


using WorkTrail.Application;

namespace WorkTrail.Presentation;

/// <summary>Describes one or more simultaneous retained captures within the visible time window.</summary>
public sealed record ScreenshotDayMarker(
    int Index,
    DateTimeOffset CapturedAt,
    double NormalizedPosition,
    double NormalizedActivity,
    bool IsSelected,
    int CaptureCount);

/// <summary>Contains the passive adaptive-time projection rendered above the screenshot inspector.</summary>
public sealed record ScreenshotDayTimelineState(
    IReadOnlyList<ScreenshotDayMarker> Markers,
    TimeSpan WindowStart,
    TimeSpan WindowEnd,
    IReadOnlyList<TimeSpan> Ticks,
    TimeSpan SelectionStart,
    TimeSpan SelectionEnd)
{
    /// <summary>Gets an empty timeline when the selected day has no retained screenshots.</summary>
    public static ScreenshotDayTimelineState Empty { get; } = new(
        Array.Empty<ScreenshotDayMarker>(),
        TimeSpan.Zero,
        TimeSpan.Zero,
        Array.Empty<TimeSpan>(),
        TimeSpan.Zero,
        TimeSpan.Zero);
}

/// <summary>Projects screenshot timestamps and interval activity into a bounded adaptive local-time window.</summary>
public static class ScreenshotDayTimelineProjection
{
    private static readonly TimeSpan DayDuration = TimeSpan.FromDays(1);
    private static readonly TimeSpan SelectionRadius = TimeSpan.FromMinutes(15);
    private static readonly int[] WindowHourSteps = [4, 8, 12, 24];

    /// <summary>Builds a deterministic adaptive marker projection for the selected screenshot.</summary>
    public static ScreenshotDayTimelineState Create(
        IReadOnlyList<ScreenshotGalleryItem> items,
        int selectedIndex)
    {
        ArgumentNullException.ThrowIfNull(items);
        if (items.Count == 0)
        {
            if (selectedIndex != -1)
            {
                throw new ArgumentOutOfRangeException(nameof(selectedIndex), selectedIndex, "An empty day timeline must use selection index -1.");
            }

            return ScreenshotDayTimelineState.Empty;
        }

        if (selectedIndex < 0 || selectedIndex >= items.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(selectedIndex), selectedIndex, "The selected screenshot must exist in the day timeline.");
        }

        var selectedLocalTime = items[selectedIndex].CapturedAt.ToLocalTime().TimeOfDay;
        var selectionStart = selectedLocalTime - SelectionRadius;
        var selectionEnd = selectedLocalTime + SelectionRadius;
        selectionStart = selectionStart < TimeSpan.Zero ? TimeSpan.Zero : selectionStart;
        selectionEnd = selectionEnd > DayDuration ? DayDuration : selectionEnd;

        var indexedItems = items
            .Select((item, index) => (Item: item, Index: index, LocalTime: item.CapturedAt.ToLocalTime()))
            .ToArray();
        var earliestCapture = indexedItems.Min(static entry => entry.LocalTime.TimeOfDay);
        var latestCapture = indexedItems.Max(static entry => entry.LocalTime.TimeOfDay);
        var requiredStart = earliestCapture < selectionStart ? earliestCapture : selectionStart;
        var requiredEnd = latestCapture > selectionEnd ? latestCapture : selectionEnd;
        var roundedStartHour = Math.Floor(requiredStart.TotalHours);
        var roundedEndHour = Math.Ceiling(requiredEnd.TotalHours);
        if (roundedEndHour <= roundedStartHour)
        {
            roundedEndHour = roundedStartHour + 1d;
        }

        var requiredHours = roundedEndHour - roundedStartHour;
        var windowHours = WindowHourSteps.First(hours => hours >= requiredHours);
        var spareHours = windowHours - requiredHours;
        var windowStartHour = roundedStartHour - Math.Floor(spareHours / 2d);
        windowStartHour = Math.Clamp(windowStartHour, 0d, DayDuration.TotalHours - windowHours);
        var windowStart = TimeSpan.FromHours(windowStartHour);
        var windowEnd = windowStart + TimeSpan.FromHours(windowHours);
        var windowDuration = windowEnd - windowStart;
        var ticks = Enumerable.Range(0, 5)
            .Select(index => windowStart + TimeSpan.FromTicks(windowDuration.Ticks * index / 4))
            .ToArray();

        var markers = indexedItems
            .GroupBy(static entry => entry.LocalTime)
            .Select(group =>
            {
                var entries = group.ToArray();
                var isSelected = entries.Any(entry => entry.Index == selectedIndex);
                var representative = isSelected
                    ? entries.Single(entry => entry.Index == selectedIndex)
                    : entries[0];
                var normalizedPosition = Math.Clamp(
                    (representative.LocalTime.TimeOfDay - windowStart).TotalSeconds / windowDuration.TotalSeconds,
                    0d,
                    1d);
                var normalizedActivity = entries.Max(entry => Math.Clamp(entry.Item.ActivityIndex ?? 0, 0, 100)) / 100d;
                return new ScreenshotDayMarker(
                    representative.Index,
                    representative.LocalTime,
                    normalizedPosition,
                    normalizedActivity,
                    isSelected,
                    entries.Length);
            })
            .ToArray();

        return new ScreenshotDayTimelineState(
            markers,
            windowStart,
            windowEnd,
            ticks,
            selectionStart,
            selectionEnd);
    }
}
