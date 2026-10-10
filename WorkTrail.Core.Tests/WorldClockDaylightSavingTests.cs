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
using System.Globalization;
using WorkTrail.Services;
using Xunit;

namespace WorkTrail.Core.Tests;

public sealed class WorldClockDaylightSavingTests
{
    /// <summary>Checks northern/southern seasons, fractional shifts, midnight changes, and the exact boundary.</summary>
    [Theory]
    [InlineData("W. Europe Standard Time", "2026-09-05T12:00:00Z", "2026-10-25T01:00:00Z")]
    [InlineData("Eastern Standard Time", "2026-09-05T12:00:00Z", "2026-11-01T06:00:00Z")]
    [InlineData("AUS Eastern Standard Time", "2026-12-15T12:00:00Z", "2027-04-03T16:00:00Z")]
    [InlineData("Lord Howe Standard Time", "2026-12-15T12:00:00Z", "2027-04-03T15:00:00Z")]
    [InlineData("Cuba Standard Time", "2026-09-05T12:00:00Z", "2026-11-01T05:00:00Z")]
    public void ActiveSeason_EndsAtTheFirstStandardTimeInstant(string zoneId, string now, string expectedEnd)
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById(zoneId);
        var expected = DateTimeOffset.Parse(expectedEnd, CultureInfo.InvariantCulture);

        var end = Assert.IsType<DateTimeOffset>(WorldClockDaylightSaving.FindEnd(
            zone, DateTimeOffset.Parse(now, CultureInfo.InvariantCulture)));

        Assert.Equal(expected, end.ToUniversalTime());
        Assert.Equal(zone.GetUtcOffset(expected), end.Offset);
        Assert.Equal(end, WorldClockDaylightSaving.FindEnd(zone, expected.AddTicks(-1)));
        Assert.Null(WorldClockDaylightSaving.FindEnd(zone, expected));
        Assert.Null(WorldClockDaylightSaving.FindEnd(zone, expected.AddTicks(1)));
    }

    /// <summary>Does not report a future DST end when the projected clock is in standard time.</summary>
    [Theory]
    [InlineData("SE Asia Standard Time")]
    [InlineData("GMT Standard Time")]
    public void InactiveSeason_HasNoEnd(string zoneId)
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById(zoneId);

        Assert.Null(WorldClockDaylightSaving.FindEnd(
            zone, new DateTimeOffset(2026, 1, 15, 12, 0, 0, TimeSpan.Zero)));
    }

    /// <summary>Uses the effective standard offset and daylight delta for fixed-date rules, including negative DST.</summary>
    [Theory]
    [InlineData(30, 0)]
    [InlineData(-30, 1)]
    public void FixedDateRule_UsesBaseOffsetDeltaAndSignedDaylightDelta(int daylightMinutes, int expectedUtcHour)
    {
        var rule = TimeZoneInfo.AdjustmentRule.CreateAdjustmentRule(
            new DateTime(2026, 1, 1), new DateTime(2026, 12, 31), TimeSpan.FromMinutes(daylightMinutes),
            FixedTransition(3, 1), FixedTransition(9, 5), TimeSpan.FromMinutes(30));
        var zone = TimeZoneInfo.CreateCustomTimeZone(
            "test-fixed-dst", TimeSpan.FromHours(2), "Test", "Standard", "Daylight", [rule]);

        var end = WorldClockDaylightSaving.FindEnd(
            zone, new DateTimeOffset(2026, 9, 1, 12, 0, 0, TimeSpan.Zero));

        Assert.Equal(new DateTimeOffset(2026, 9, 5, expectedUtcHour, 0, 0, TimeSpan.Zero), end);
    }

    /// <summary>A southern season can terminate when its adjustment rule expires at the year boundary.</summary>
    [Fact]
    public void ExpiringRule_EndsActiveSeasonAtTheRuleBoundary()
    {
        var rule = TimeZoneInfo.AdjustmentRule.CreateAdjustmentRule(
            new DateTime(2026, 1, 1), new DateTime(2026, 12, 31), TimeSpan.FromHours(1),
            FixedTransition(10, 1), FixedTransition(3, 1));
        var zone = TimeZoneInfo.CreateCustomTimeZone(
            "test-expiring-dst", TimeSpan.FromHours(2), "Test", "Standard", "Daylight", [rule]);

        var end = Assert.IsType<DateTimeOffset>(WorldClockDaylightSaving.FindEnd(
            zone, new DateTimeOffset(2026, 12, 15, 12, 0, 0, TimeSpan.Zero)));

        Assert.Equal(new DateTimeOffset(2026, 12, 31, 22, 0, 0, TimeSpan.Zero), end);
        Assert.True(zone.IsDaylightSavingTime(end.AddTicks(-1)));
        Assert.False(zone.IsDaylightSavingTime(end));
    }

    private static TimeZoneInfo.TransitionTime FixedTransition(int month, int day) =>
        TimeZoneInfo.TransitionTime.CreateFixedDateRule(new DateTime(1, 1, 1, 3, 0, 0), month, day);
}
