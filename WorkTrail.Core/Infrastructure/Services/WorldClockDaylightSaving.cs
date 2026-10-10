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


namespace WorkTrail.Services;

/// <summary>Resolves the end of the active DST period using the installed time-zone rules.</summary>
internal static class WorldClockDaylightSaving
{
    /// <summary>Returns the first standard-time instant, or null when DST is inactive or no end is defined.</summary>
    internal static DateTimeOffset? FindEnd(TimeZoneInfo timeZone, DateTimeOffset instant)
    {
        ArgumentNullException.ThrowIfNull(timeZone);
        if (!timeZone.IsDaylightSavingTime(instant))
        {
            return null;
        }

        var localYear = TimeZoneInfo.ConvertTime(instant, timeZone).Year;
        DateTimeOffset? nextEnd = null;
        // Use installed OS rules, including southern seasons and non-hour deltas. A missing
        // future end is explicit in the DTO/UI; time-zone failures propagate without a guessed date.
        foreach (var rule in timeZone.GetAdjustmentRules())
        {
            if (rule.DateEnd.Year < localYear || rule.DaylightDelta == TimeSpan.Zero)
            {
                continue;
            }

            var daylightOffset = timeZone.BaseUtcOffset + rule.BaseUtcOffsetDelta + rule.DaylightDelta;
            for (var year = Math.Max(localYear, rule.DateStart.Year); year <= rule.DateEnd.Year; year++)
            {
                var localEnd = TransitionDate(year, rule.DaylightTransitionEnd);
                if (localEnd.Date >= rule.DateStart.Date && localEnd.Date <= rule.DateEnd.Date
                    && Consider(localEnd, daylightOffset))
                {
                    break;
                }
            }

            // Rule expiry is evaluated on standard civil dates by TimeZoneInfo. Validate both
            // sides' offsets so base-offset changes cannot turn a date boundary into a guessed end.
            if (rule.DateEnd.Date < DateTime.MaxValue.Date)
            {
                var boundary = rule.DateEnd.Date.AddDays(1);
                Consider(boundary, daylightOffset);
                Consider(boundary, timeZone.BaseUtcOffset + rule.BaseUtcOffsetDelta);
                Consider(boundary, timeZone.BaseUtcOffset);
            }
        }

        return nextEnd is { } end ? TimeZoneInfo.ConvertTime(end, timeZone) : null;

        bool Consider(DateTime localEnd, TimeSpan daylightOffset)
        {
            var utcTicks = localEnd.Ticks - daylightOffset.Ticks;
            if (utcTicks <= instant.UtcTicks || utcTicks > DateTime.MaxValue.Ticks)
            {
                return false;
            }

            var candidate = new DateTimeOffset(utcTicks, TimeSpan.Zero);
            if (!timeZone.IsDaylightSavingTime(candidate.AddTicks(-1))
                || timeZone.IsDaylightSavingTime(candidate))
            {
                return false;
            }

            if (nextEnd is null || candidate < nextEnd)
            {
                nextEnd = candidate;
            }

            return true;
        }
    }

    private static DateTime TransitionDate(int year, TimeZoneInfo.TransitionTime transition)
    {
        var daysInMonth = DateTime.DaysInMonth(year, transition.Month);
        var day = Math.Min(transition.Day, daysInMonth);
        if (!transition.IsFixedDateRule)
        {
            var firstDay = new DateTime(year, transition.Month, 1).DayOfWeek;
            day = 1 + ((int)transition.DayOfWeek - (int)firstDay + 7) % 7 + 7 * (transition.Week - 1);
            if (day > daysInMonth)
            {
                day -= 7;
            }
        }

        return new DateTime(year, transition.Month, day, 0, 0, 0, DateTimeKind.Unspecified)
            .Add(transition.TimeOfDay.TimeOfDay);
    }
}
