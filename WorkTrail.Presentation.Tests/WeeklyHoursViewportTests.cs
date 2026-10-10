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
using WorkTrail.Presentation;
using WorkTrail.Services;
using Xunit;

namespace WorkTrail.Presentation.Tests;

/// <summary>Guards noon, midnight and hidden selections in the half-day schedule projection.</summary>
public sealed class WeeklyHoursViewportTests
{
    /// <summary>Ensures each half maps 48 visible rows to distinct full-day slots.</summary>
    [Theory]
    [InlineData(0, 0, 47, "12:00")]
    [InlineData(1, 48, 95, "24:00")]
    public void HalfDayRowsHaveUnambiguousBoundaries(int half, int first, int last, string end)
    {
        var view = new WeeklyHoursViewport(half);

        Assert.Equal(first, view.GetSlotIndex(0));
        Assert.Equal(last, view.GetSlotIndex(47));
        Assert.Equal(end, WeeklyHoursGridProjection.FormatBoundary(view.EndSlot));
        Assert.Throws<ArgumentOutOfRangeException>(() => view.GetSlotIndex(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => view.GetSlotIndex(48));
    }

    /// <summary>Rejects unsupported view states instead of choosing a different half silently.</summary>
    [Theory]
    [InlineData(-1)]
    [InlineData(2)]
    public void UnsupportedHalfFails(int half) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new WeeklyHoursViewport(half));

    /// <summary>Ensures a band crossing noon retains its complete label and does not mutate the schedule.</summary>
    [Fact]
    public void NoonCrossingRetainsBothHalvesAndOriginalBandBoundaries()
    {
        var configured = new ActiveHoursDay("monday", "09:00-18:00", "13:00-14:00");
        var slots = WeeklyHoursGridProjection.ToSlots(configured);
        var snapshot = slots.ToArray();

        var morning = new WeeklyHoursViewport(0).GetBands(slots);
        var evening = new WeeklyHoursViewport(1).GetBands(slots);

        Assert.Equal(new WeeklyHoursBand(36, 52, 36, 48), Assert.Single(morning));
        Assert.Equal(new WeeklyHoursBand(36, 52, 48, 52), evening[0]);
        Assert.Equal(new WeeklyHoursBand(56, 72, 56, 72), evening[1]);
        Assert.Equal(2, evening.Count);
        Assert.Equal(snapshot, slots);
        Assert.Equal(configured, WeeklyHoursGridProjection.FromSlots("monday", slots));
    }

    /// <summary>Ensures a break spanning noon stays empty in both views.</summary>
    [Fact]
    public void NoonBreakIsNotFilledByClipping()
    {
        var slots = WeeklyHoursGridProjection.ToSlots(new ActiveHoursDay("tuesday", "09:00-14:00", "11:45-12:15"));

        Assert.Equal(new WeeklyHoursBand(36, 47, 36, 47), Assert.Single(new WeeklyHoursViewport(0).GetBands(slots)));
        Assert.Equal(new WeeklyHoursBand(49, 56, 49, 56), Assert.Single(new WeeklyHoursViewport(1).GetBands(slots)));
    }

    /// <summary>Ensures the last evening quarter remains selectable while a hidden morning slot is preserved.</summary>
    [Fact]
    public void EditingEveningPreservesMorningAndIncludesTheLastQuarter()
    {
        var slots = new bool[WeeklyHoursGridProjection.SlotsPerDay];
        slots[new WeeklyHoursViewport(0).GetSlotIndex(0)] = true;
        slots[new WeeklyHoursViewport(1).GetSlotIndex(47)] = true;

        Assert.Equal(new WeeklyHoursBand(0, 1, 0, 1), Assert.Single(new WeeklyHoursViewport(0).GetBands(slots)));
        Assert.Equal(new WeeklyHoursBand(95, 96, 95, 96), Assert.Single(new WeeklyHoursViewport(1).GetBands(slots)));
        var saved = WeeklyHoursGridProjection.FromSlots("sunday", slots);
        Assert.Equal("00:00-24:00", saved.ActivePeriod);
        Assert.Equal("00:15-23:45", saved.BreakPeriods);
        Assert.Equal(slots, WeeklyHoursGridProjection.ToSlots(saved));
    }

    /// <summary>Ensures a band ending at noon does not appear in evening and one starting there does not appear in morning.</summary>
    [Theory]
    [InlineData("11:45-12:00", 0)]
    [InlineData("12:00-12:15", 1)]
    public void NoonBoundaryBelongsToExactlyOneView(string range, int half)
    {
        var slots = WeeklyHoursGridProjection.ToSlots(new ActiveHoursDay("wednesday", range));

        Assert.Single(new WeeklyHoursViewport(half).GetBands(slots));
        Assert.Empty(new WeeklyHoursViewport(1 - half).GetBands(slots));
    }

    /// <summary>Rejects incomplete day buffers that could lose selections in the hidden half.</summary>
    [Fact]
    public void ProjectionRequiresTheCompleteDay() =>
        Assert.Throws<ArgumentException>(() => new WeeklyHoursViewport(0).GetBands(new bool[48]));
}
