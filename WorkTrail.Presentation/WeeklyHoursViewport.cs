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


namespace WorkTrail.Presentation;

/// <summary>Projects one half of the day without changing the full-day selection.</summary>
public sealed class WeeklyHoursViewport
{
    /// <summary>Gets the number of quarter-hour rows in either half of the day.</summary>
    public const int SlotCount = WeeklyHoursGridProjection.SlotsPerDay / 2;

    /// <summary>Creates a morning (0) or evening (1) viewport; other values are unsupported.</summary>
    public WeeklyHoursViewport(int half)
    {
        if (half is not (0 or 1))
        {
            // Unsupported views are rejected rather than mapped onto another part of the day.
            throw new ArgumentOutOfRangeException(nameof(half));
        }

        FirstSlot = half * SlotCount;
    }

    /// <summary>Gets the inclusive first slot in the full-day selection.</summary>
    public int FirstSlot { get; }

    /// <summary>Gets the exclusive end boundary, including 24:00 for evening.</summary>
    public int EndSlot => FirstSlot + SlotCount;

    /// <summary>Maps a visible row to its authoritative full-day slot.</summary>
    public int GetSlotIndex(int row)
    {
        if (row < 0 || row >= SlotCount)
        {
            throw new ArgumentOutOfRangeException(nameof(row));
        }

        return FirstSlot + row;
    }

    /// <summary>Returns visible contiguous bands with their original, unclipped time boundaries.</summary>
    public IReadOnlyList<WeeklyHoursBand> GetBands(IReadOnlyList<bool> slots)
    {
        ArgumentNullException.ThrowIfNull(slots);
        if (slots.Count != WeeklyHoursGridProjection.SlotsPerDay)
        {
            // A half-day buffer would discard hidden selections and is never accepted.
            throw new ArgumentException("Expected a complete day of quarter-hour slots.", nameof(slots));
        }

        var bands = new List<WeeklyHoursBand>();
        var slot = 0;
        while (slot < slots.Count)
        {
            if (!slots[slot])
            {
                slot++;
                continue;
            }

            var start = slot;
            while (slot < slots.Count && slots[slot])
            {
                slot++;
            }

            if (start < EndSlot && slot > FirstSlot)
            {
                bands.Add(new WeeklyHoursBand(start, slot, Math.Max(start, FirstSlot), Math.Min(slot, EndSlot)));
            }
        }

        return bands;
    }
}

/// <summary>Describes a selected range and the portion rendered in the current half of the day.</summary>
public sealed record WeeklyHoursBand(int StartSlot, int EndSlot, int VisibleStartSlot, int VisibleEndSlot);
