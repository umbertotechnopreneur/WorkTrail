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
using System.IO;
using WorkTrail.Presentation;
using WorkTrail.Services;
using Xunit;

namespace WorkTrail.Presentation.Tests;

/// <summary>Verifies lossless projection between the schedule contract and its quarter-hour editor.</summary>
public sealed class WeeklyHoursGridProjectionTests
{
    /// <summary>Ensures supported quarter-hour boundaries and breaks survive a complete round trip.</summary>
    [Fact]
    public void QuarterHourBoundariesRoundTripWithoutShifting()
    {
        var configured = new ActiveHoursDay("monday", "09:15-18:45", "13:15-14:00, 16:30-16:45");

        var slots = WeeklyHoursGridProjection.ToSlots(configured);
        var roundTrip = WeeklyHoursGridProjection.FromSlots(configured.Day, slots);

        Assert.Equal(configured, roundTrip);
    }

    /// <summary>Ensures unsupported persisted boundaries are rejected instead of shifted silently.</summary>
    [Fact]
    public void UnsupportedBoundaryFailsInsteadOfRounding()
    {
        var configured = new ActiveHoursDay("monday", "09:10-18:45");

        var exception = Assert.Throws<InvalidDataException>(() => WeeklyHoursGridProjection.ToSlots(configured));

        Assert.Contains("15-minute increments", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>Ensures a break outside the active range is rejected instead of ignored by projection.</summary>
    [Fact]
    public void BreakOutsideActivePeriodFailsInsteadOfBeingIgnored()
    {
        var configured = new ActiveHoursDay("monday", "09:00-17:00", "08:00-08:15");

        var exception = Assert.Throws<InvalidDataException>(() => WeeklyHoursGridProjection.ToSlots(configured));

        Assert.Contains("must fit its active period", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>Ensures clearing every editor cell produces a canonical inactive day.</summary>
    [Fact]
    public void EmptySelectionProducesAnInactiveDay()
    {
        var result = WeeklyHoursGridProjection.FromSlots(
            "sunday",
            new bool[WeeklyHoursGridProjection.SlotsPerDay]);

        Assert.Equal(new ActiveHoursDay("sunday"), result);
    }
}
