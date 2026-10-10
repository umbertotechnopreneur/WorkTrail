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

/// <summary>Pairs one reported hour with its visible local date and selection availability.</summary>
public sealed record ActivityWeekCell(DateOnly Date, int Hour, ReportHourCell Activity, bool IsAvailable);

/// <summary>Projects a bounded weekly report into a Monday-first 24 by 7 grid.</summary>
public static class ActivityWeekProjection
{
    /// <summary>Returns the Monday containing the supplied date.</summary>
    public static DateOnly MondayOf(DateOnly date) => date.AddDays(-(((int)date.DayOfWeek + 6) % 7));

    /// <summary>Validates and projects a single week; incomplete or incompatible reports fail before rendering.</summary>
    public static IReadOnlyList<ActivityWeekCell> Create(ReportSnapshot snapshot, DateOnly monday, DateOnly firstDate, DateOnly lastDate)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (monday.DayOfWeek != DayOfWeek.Monday || firstDate > lastDate
            || firstDate < monday || lastDate > monday.AddDays(6)
            || snapshot.ContractVersion != 6 || snapshot.Range.From != firstDate
            || snapshot.Range.ToInclusive != lastDate || snapshot.HourOfWeek.Count != 168)
        {
            throw new InvalidDataException("Expected a complete single-week report with contract version 6.");
        }

        var hours = new Dictionary<(int Day, int Hour), ReportHourCell>();
        foreach (var cell in snapshot.HourOfWeek)
        {
            if (cell.DayOfWeek is < 0 or > 6 || cell.Hour is < 0 or > 23
                || cell.ActiveSeconds < 0 || cell.IdleSeconds < 0 || cell.TrackedSeconds != cell.ActiveSeconds + cell.IdleSeconds
                || cell.KeyPresses < 0 || cell.MouseClicks < 0 || cell.SampleCount < 0
                || cell.Installations is not { } installations
                || installations.Any(profile => !IsValidInstallation(profile))
                || installations.Select(profile => profile.InstallationId).Distinct(StringComparer.Ordinal).Count() != installations.Count
                || (cell.HasData
                    ? cell.ObservationDays != 1 || cell.SampleCount == 0 || cell.ActivityScore is not (>= 0 and <= 100) || installations.Count == 0
                    : cell.ObservationDays != 0 || cell.SampleCount != 0 || cell.ActivityScore is not null
                        || cell.TrackedSeconds != 0 || cell.KeyPresses != 0 || cell.MouseClicks != 0 || installations.Count != 0)
                || !hours.TryAdd((cell.DayOfWeek, cell.Hour), cell))
            {
                // Do not render missing, duplicate or multi-week buckets as valid hourly measurements.
                throw new InvalidDataException("Invalid weekly activity bucket.");
            }
        }

        return Enumerable.Range(0, 24).SelectMany(hour => Enumerable.Range(0, 7).Select(day =>
        {
            var date = monday.AddDays(day);
            var cell = hours[((int)date.DayOfWeek, hour)];
            var available = date >= firstDate && date <= lastDate;
            if (!available && cell.HasData)
            {
                throw new InvalidDataException("Activity was returned outside the requested week range.");
            }

            return new ActivityWeekCell(date, hour, cell, available);
        })).ToArray();
    }

    private static bool IsValidInstallation(InstallationProfile profile) =>
        profile is not null && Guid.TryParseExact(profile.InstallationId, "N", out _)
        && profile.MachineName is { Length: >= 1 and <= 128 } && profile.MachineName == profile.MachineName.Trim()
        && profile.FriendlyName is { Length: >= 1 and <= 64 } && profile.FriendlyName == profile.FriendlyName.Trim()
        && InstallationProfileCatalog.Colors.Contains(profile.Color, StringComparer.Ordinal)
        && InstallationProfileCatalog.Icons.Contains(profile.Icon, StringComparer.Ordinal)
        && profile.FirstSeenAt.Offset == TimeSpan.Zero && profile.UpdatedAt.Offset == TimeSpan.Zero
        && profile.UpdatedAt >= profile.FirstSeenAt && profile.Revision >= 1;
}
