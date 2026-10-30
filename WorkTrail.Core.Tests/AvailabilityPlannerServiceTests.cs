// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using WorkTrail.Application;
using Xunit;

namespace WorkTrail.Core.Tests;

/// <summary>Checks real-time alignment, complete-slot availability and holiday precedence.</summary>
public sealed class AvailabilityPlannerServiceTests
{
    /// <summary>Five civil days retain the DST-short or DST-long day instead of inventing 120 hours.</summary>
    [Theory]
    [InlineData(2026, 3, 29, 119)]
    [InlineData(2026, 10, 25, 121)]
    public void FiveDaysRespectDst(int year, int month, int day, int hours)
    {
        var instant = new DateTimeOffset(year, month, day, 12, 0, 0, TimeSpan.Zero);
        var snapshot = Build(instant, Schedule());
        Assert.Equal(hours * 2, snapshot.Rows.Count);
        Assert.All(snapshot.Rows.Zip(snapshot.Rows.Skip(1)), pair => Assert.Equal(pair.First.EndUtc, pair.Second.StartUtc));
        Assert.All(snapshot.Rows, row => Assert.Equal(row.StartUtc, row.Cells[0].LocalStart.ToUniversalTime()));
    }

    /// <summary>An interval overlapping the end of work is not marked fully available.</summary>
    [Fact]
    public void SlotChecksWholeDuration()
    {
        var instant = new DateTimeOffset(2026, 9, 24, 15, 0, 0, TimeSpan.Zero);
        var snapshot = Build(instant, Schedule() with { End = new TimeOnly(17, 10) });
        var row = snapshot.Rows.Single(r => r.StartUtc == instant);
        Assert.Equal(AvailabilityState.Reserve, row.Cells[0].State);
    }

    /// <summary>Personal overrides take precedence over both holidays and rest days.</summary>
    [Fact]
    public void PersonalExceptionOverridesChristmas()
    {
        var instant = new DateTimeOffset(2026, 12, 25, 12, 0, 0, TimeSpan.Zero);
        var normal = Build(instant, Schedule());
        Assert.Equal("Planner.Holiday", normal.Rows.Single(r => r.StartUtc == instant).Cells[0].ReasonKey);
        var custom = Build(instant, Schedule() with { Exceptions = [new(new DateOnly(2026, 12, 25), AvailabilityState.Available)] });
        Assert.Equal(AvailabilityState.Available, custom.Rows.Single(r => r.StartUtc == instant).Cells[0].State);
    }

    /// <summary>Missing holiday coverage is visible instead of granting availability.</summary>
    [Fact]
    public void UnknownCalendarDoesNotClaimAvailability()
    {
        var instant = new DateTimeOffset(2030, 9, 24, 12, 0, 0, TimeSpan.Zero);
        var snapshot = Build(instant, Schedule());
        Assert.Equal(AvailabilityState.Unknown, snapshot.Rows.Single(r => r.StartUtc == instant).Cells[0].State);
    }

    /// <summary>Night shifts continue through midnight without losing their active interval.</summary>
    [Fact]
    public void OvernightShiftContinuesIntoNextDay()
    {
        var instant = new DateTimeOffset(2026, 9, 24, 23, 0, 0, TimeSpan.Zero);
        var snapshot = Build(instant, Schedule() with { Start = new TimeOnly(22, 0), End = new TimeOnly(6, 0) });
        Assert.Equal(AvailabilityState.Available, snapshot.Rows.Single(r => r.StartUtc == instant).Cells[0].State);
    }

    /// <summary>A half-hour offset remains aligned with the same UTC instants.</summary>
    [Fact]
    public void FractionalOffsetAndCurrentWeatherArePreserved()
    {
        var instant = new DateTimeOffset(2026, 9, 24, 9, 0, 0, TimeSpan.Zero);
        var projection = Snapshot(instant);
        var current = Snapshot(instant.AddDays(2));
        var observation = new WorldClockWeather(28, "rain", current.InstantUtc, true);
        var india = projection.Clocks[0] with { CityId = "mumbai", CountryCode = "IN", TimeZoneId = "India Standard Time" };
        projection = projection with { Clocks = [projection.Clocks[0], india] };
        current = current with { Clocks = [current.Clocks[0] with { Weather = observation }, india with { Weather = observation }] };
        var result = AvailabilityPlannerService.Build(projection, current, "rome", 30, new AppSettings(), CancellationToken.None);
        var row = result.Rows.Single(r => r.StartUtc == instant);
        Assert.Equal(new TimeSpan(14, 30, 0), row.Cells[1].LocalStart.TimeOfDay);
        Assert.Equal(observation, result.Cities[0].Clock.Weather);
        Assert.NotEqual(result.InstantUtc, result.Cities[0].Clock.Weather!.ObservedAtUtc);
    }

    /// <summary>Invalid preferences cannot be persisted as valid working schedules.</summary>
    [Fact]
    public void RejectsInvalidSchedule()
    {
        Assert.False(AvailabilityPlannerService.IsValid(Schedule() with { End = new TimeOnly(9, 0) }));
        Assert.False(AvailabilityPlannerService.IsValid(Schedule() with { ReserveBeforeMinutes = -1 }));
        Assert.False(AvailabilityPlannerService.IsValid(Schedule() with { Exceptions = [new(new DateOnly(2026, 1, 1), AvailabilityState.Unknown)] }));
    }

    private static AvailabilitySchedule Schedule() => new("rome", new TimeOnly(9, 0), new TimeOnly(18, 0), []);

    private static AvailabilityPlannerSnapshot Build(DateTimeOffset instant, AvailabilitySchedule schedule)
    {
        var snapshot = Snapshot(instant);
        return AvailabilityPlannerService.Build(snapshot, snapshot, "rome", 30,
            new AppSettings(AvailabilitySchedules: new Dictionary<string, AvailabilitySchedule> { ["rome"] = schedule }), CancellationToken.None);
    }

    private static WorldClockSnapshot Snapshot(DateTimeOffset instant)
    {
        var local = TimeZoneInfo.ConvertTime(instant, TimeZoneInfo.FindSystemTimeZoneById("W. Europe Standard Time"));
        var city = new WorldClockItem("rome", "Rome", "IT", "W. Europe Standard Time", local, false, null, true,
            null, null, 90, "Assets/WorldClocks/Skylines/rome-summer.png", "summer", new("day", [], []), null);
        return new(instant, [city], 12, new("openweather", "configuration-required", "missing-api-key", 1, 0, false),
            new(new(0, 0), new(0, 0), 90, []));
    }
}
