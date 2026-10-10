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


using WorkTrail.Services;

namespace WorkTrail.Presentation;

/// <summary>Projects active-hour ranges onto the fixed-resolution weekly editor grid.</summary>
public static class WeeklyHoursGridProjection
{
    /// <summary>Gets the explicit editor resolution in minutes.</summary>
    public const int MinutesPerSlot = ActiveHoursSchedule.BoundaryMinutes;

    /// <summary>Gets the number of selectable slots in one civil day.</summary>
    public const int SlotsPerDay = (24 * 60) / MinutesPerSlot;

    /// <summary>Projects one configured day onto the editor slots without rounding boundaries.</summary>
    public static bool[] ToSlots(ActiveHoursDay day)
    {
        ArgumentNullException.ThrowIfNull(day);
        var slots = new bool[SlotsPerDay];
        if (string.IsNullOrWhiteSpace(day.ActivePeriod))
        {
            return slots;
        }

        if (!ActiveHoursSchedule.TryParseRange(day.ActivePeriod, out var activeStart, out var activeEnd))
        {
            throw new InvalidDataException($"Invalid active-hours range for '{day.Day}'.");
        }

        SetRange(slots, day.Day, activeStart, activeEnd, value: true);
        foreach (var period in day.BreakPeriods.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            if (!ActiveHoursSchedule.TryParseRange(period, out var breakStart, out var breakEnd))
            {
                throw new InvalidDataException($"Invalid active-hours break for '{day.Day}'.");
            }

            if (breakStart < activeStart || breakEnd > activeEnd)
            {
                throw new InvalidDataException(
                    $"Active-hours break for '{day.Day}' must fit its active period.");
            }

            SetRange(slots, day.Day, breakStart, breakEnd, value: false);
        }

        return slots;
    }

    /// <summary>Serializes one day of editor slots into the normalized active-hours contract.</summary>
    public static ActiveHoursDay FromSlots(string day, IReadOnlyList<bool> slots)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(day);
        ArgumentNullException.ThrowIfNull(slots);
        if (slots.Count != SlotsPerDay)
        {
            throw new ArgumentException($"Expected exactly {SlotsPerDay} active-hours slots.", nameof(slots));
        }

        var selected = slots.ToArray();
        var firstSelected = Array.FindIndex(selected, static value => value);
        if (firstSelected < 0)
        {
            return new ActiveHoursDay(day);
        }

        var lastSelected = Array.FindLastIndex(selected, static value => value);
        var breaks = new List<string>();
        var slot = firstSelected;
        while (slot <= lastSelected)
        {
            if (selected[slot])
            {
                slot++;
                continue;
            }

            var breakStart = slot;
            while (slot <= lastSelected && !selected[slot])
            {
                slot++;
            }

            breaks.Add($"{FormatBoundary(breakStart)}-{FormatBoundary(slot)}");
        }

        return new ActiveHoursDay(
            day,
            $"{FormatBoundary(firstSelected)}-{FormatBoundary(lastSelected + 1)}",
            string.Join(", ", breaks));
    }

    /// <summary>Formats one editor slot boundary using the active-hours time contract.</summary>
    public static string FormatBoundary(int slot)
    {
        if (slot < 0 || slot > SlotsPerDay)
        {
            throw new ArgumentOutOfRangeException(nameof(slot));
        }

        var minutes = slot * MinutesPerSlot;
        return minutes == 24 * 60
            ? "24:00"
            : $"{minutes / 60:00}:{minutes % 60:00}";
    }

    private static void SetRange(bool[] slots, string day, int startMinutes, int endMinutes, bool value)
    {
        if (startMinutes % MinutesPerSlot != 0 || endMinutes % MinutesPerSlot != 0)
        {
            // The editor fails explicitly instead of silently shifting a persisted boundary.
            throw new InvalidDataException(
                $"Active-hours boundaries for '{day}' must use {MinutesPerSlot}-minute increments.");
        }

        var startSlot = startMinutes / MinutesPerSlot;
        var endSlot = endMinutes / MinutesPerSlot;
        if (startSlot < 0 || endSlot > SlotsPerDay || endSlot <= startSlot)
        {
            throw new InvalidDataException($"Active-hours range for '{day}' is outside one civil day.");
        }

        for (var slot = startSlot; slot < endSlot; slot++)
        {
            slots[slot] = value;
        }
    }
}
