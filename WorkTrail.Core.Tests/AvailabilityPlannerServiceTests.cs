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

    /// <summary>Grouping retains each calendar and combines a slot using the most restrictive state.</summary>
    [Fact]
    public void GroupedCitiesKeepCalendarsAndUseRestrictiveAvailability()
    {
        var instant = new DateTimeOffset(2026, 11, 4, 2, 0, 0, TimeSpan.Zero);
        var template = Snapshot(instant);
        var hongKong = template.Clocks[0] with { CityId = "hk", CityName = "Hong Kong", CountryCode = "HK", TimeZoneId = "China Standard Time", LocalTime = TimeZoneInfo.ConvertTime(instant, TimeZoneInfo.FindSystemTimeZoneById("China Standard Time")) };
        var kualaLumpur = template.Clocks[0] with { CityId = "kl", CityName = "Kuala Lumpur", CountryCode = "MY", TimeZoneId = "Singapore Standard Time", LocalTime = TimeZoneInfo.ConvertTime(instant, TimeZoneInfo.FindSystemTimeZoneById("Singapore Standard Time")) };
        var projection = template with { Clocks = [hongKong, kualaLumpur] };
        var day = new DateOnly(2026, 11, 4);
        var schedules = new Dictionary<string, AvailabilitySchedule>
        {
            ["hk"] = Schedule() with { CityId = "hk", Exceptions = [new(day, AvailabilityState.Available)] },
            ["kl"] = Schedule() with { CityId = "kl", Exceptions = [new(day, AvailabilityState.Unavailable)] }
        };
        var result = AvailabilityPlannerService.Build(projection, projection, "hk", 30,
            new AppSettings(WorldClockAutoGroupCities: true, AvailabilitySchedules: schedules), CancellationToken.None);
        var group = Assert.Single(result.DisplayGroups!);
        Assert.Equal(new[] { 0, 1 }, group.CityIndices);
        var row = result.Rows.Single(item => item.StartUtc == instant);
        Assert.Equal(AvailabilityState.Available, row.Cells[0].State);
        Assert.Equal(AvailabilityState.Unavailable, row.Cells[1].State);
        Assert.Equal(AvailabilityState.Unavailable, Assert.Single(row.GroupedCells!).State);
        Assert.Equal("kl", result.Cities[1].Schedule.CityId);
    }

    /// <summary>Cities are not grouped when their time zones diverge during the displayed interval.</summary>
    [Fact]
    public void GroupingKeepsDstDivergenceSeparate()
    {
        var instant = new DateTimeOffset(2026, 10, 30, 12, 0, 0, TimeSpan.Zero);
        var template = Snapshot(instant);
        var newYorkZone = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");
        var caracasZone = TimeZoneInfo.FindSystemTimeZoneById("SA Western Standard Time");
        var newYork = template.Clocks[0] with { CityId = "ny", TimeZoneId = newYorkZone.Id, LocalTime = TimeZoneInfo.ConvertTime(instant, newYorkZone) };
        var caracas = template.Clocks[0] with { CityId = "ccs", TimeZoneId = caracasZone.Id, LocalTime = TimeZoneInfo.ConvertTime(instant, caracasZone) };
        var projection = template with { Clocks = [newYork, caracas] };
        var result = AvailabilityPlannerService.Build(projection, projection, "ny", 30,
            new AppSettings(WorldClockAutoGroupCities: true), CancellationToken.None);
        Assert.Equal(newYork.LocalTime.Offset, caracas.LocalTime.Offset);
        Assert.Equal(2, result.DisplayGroups!.Count);
        Assert.All(result.DisplayGroups, group => Assert.Single(group.CityIndices));
    }

    /// <summary>Invalid preferences cannot be persisted as valid working schedules.</summary>
    [Fact]
    public void RejectsInvalidSchedule()
    {
        Assert.False(AvailabilityPlannerService.IsValid(Schedule() with { End = new TimeOnly(9, 0) }));
        Assert.False(AvailabilityPlannerService.IsValid(Schedule() with { ReserveBeforeMinutes = -1 }));
        Assert.False(AvailabilityPlannerService.IsValid(Schedule() with { Exceptions = [new(new DateOnly(2026, 1, 1), AvailabilityState.Unknown)] }));
    }

    /// <summary>Projects day, sunset and night at each slot for grouped cities with different locations.</summary>
    [Fact]
    public void SolarPhasesFollowEachCityAndSlot()
    {
        var instant = new DateTimeOffset(2026, 11, 10, 4, 0, 0, TimeSpan.Zero);
        var template = Snapshot(instant);
        var zone = TimeZoneInfo.FindSystemTimeZoneById("China Standard Time");
        var hongKong = template.Clocks[0] with
        {
            CityId = "hk",
            CityName = "Hong Kong",
            CountryCode = "HK",
            TimeZoneId = zone.Id,
            LocalTime = TimeZoneInfo.ConvertTime(instant, zone)
        };
        var denpasar = hongKong with
        {
            CityId = "denpasar",
            CityName = "Denpasar",
            CountryCode = "ID",
            TimeZoneId = "Singapore Standard Time"
        };
        var projection = template with
        {
            Clocks = [hongKong, denpasar],
            Map = template.Map with { Cities = [new("hk", "Hong Kong", 22.3193, 114.1694), new("denpasar", "Denpasar", -8.65, 115.2167)] }
        };
        var snapshot = AvailabilityPlannerService.Build(projection, projection, "hk", 30,
            new AppSettings(HomeCityId: "hk", WorldClockAutoGroupCities: true), CancellationToken.None);
        Assert.Single(snapshot.DisplayGroups!);
        Assert.Equal(AvailabilitySolarPhase.Day, snapshot.Rows.Single(row => row.StartUtc == instant).Cells[0].SolarPhase);
        Assert.Equal(AvailabilitySolarPhase.Sunset, snapshot.Rows.Single(row => row.StartUtc == instant.AddHours(5.5)).Cells[0].SolarPhase);
        Assert.Equal(AvailabilitySolarPhase.Night, snapshot.Rows.Single(row => row.StartUtc == instant.AddHours(12)).Cells[0].SolarPhase);
        Assert.Contains(snapshot.Rows, row => row.Cells.Any(cell => cell.SolarPhase == AvailabilitySolarPhase.Sunset)
            && row.Cells.Any(cell => cell.SolarPhase == AvailabilitySolarPhase.Night));
    }
    private static AvailabilitySchedule Schedule() => new("rome", new TimeOnly(9, 0), new TimeOnly(18, 0), []);

    /// <summary>The reported five-city case keeps the selected day distinct from later scrolled days.</summary>
    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    public void NovemberCityColumnsKeepTheirTimesDatesAndHolidayCoverage(int visibleDay)
    {
        var reference = new DateTimeOffset(2026, 11, 1, 9, 45, 0, TimeSpan.Zero);
        var definitions = new[]
        {
            (Id: "ho-chi-minh-city", Country: "VN", Zone: "SE Asia Standard Time", Hour: 16),
            (Id: "denpasar", Country: "ID", Zone: "Singapore Standard Time", Hour: 17),
            (Id: "domegge-di-cadore", Country: "IT", Zone: "W. Europe Standard Time", Hour: 10),
            (Id: "new-york", Country: "US", Zone: "Eastern Standard Time", Hour: 4),
            (Id: "hong-kong", Country: "HK", Zone: "China Standard Time", Hour: 17)
        };
        var template = Snapshot(reference);
        var clocks = definitions.Select(city => template.Clocks[0] with
        {
            CityId = city.Id,
            CountryCode = city.Country,
            TimeZoneId = city.Zone,
            LocalTime = TimeZoneInfo.ConvertTime(reference, TimeZoneInfo.FindSystemTimeZoneById(city.Zone))
        }).ToArray();
        var projection = template with { Clocks = clocks };
        var settings = new AppSettings(HomeCityId: definitions[0].Id, AvailabilitySchedules: definitions.ToDictionary(
            city => city.Id, city => Schedule() with { CityId = city.Id }));
        var result = AvailabilityPlannerService.Build(projection, projection, definitions[0].Id, 30, settings, CancellationToken.None);
        var visible = new DateTimeOffset(2026, 11, visibleDay, 9, 0, 0, TimeSpan.Zero);
        var row = result.Rows.Single(item => item.StartUtc == visible);

        Assert.Equal(new DateTime(2026, 11, 1), result.ReferenceLocalTime.Date);
        Assert.Equal(definitions.Select(city => city.Id), result.Cities.Select(city => city.Clock.CityId));
        for (var index = 0; index < definitions.Length; index++)
        {
            Assert.Equal(definitions[index].Hour, row.Cells[index].LocalStart.Hour);
            Assert.Equal(new DateTime(2026, 11, visibleDay), row.Cells[index].LocalStart.Date);
            Assert.Equal(visible, row.Cells[index].LocalStart.ToUniversalTime());
        }
        foreach (var index in new[] { 1, 4 })
        {
            // The national calendars now cover both cities; this fixture explicitly has no rest days.
            Assert.Equal(AvailabilityState.Available, row.Cells[index].State);
            Assert.Equal("Planner.WorkingHours", row.Cells[index].ReasonKey);
            var midnight = result.Rows.Single(item => item.StartUtc == visible.AddHours(7)).Cells[index];
            Assert.Equal(new DateTime(2026, 11, visibleDay + 1), midnight.LocalStart.Date);
            Assert.Equal(TimeSpan.Zero, midnight.LocalStart.TimeOfDay);
        }
    }

    /// <summary>Official calendars stop granting verified availability after their published coverage ends.</summary>
    [Theory]
    [InlineData("ID")]
    [InlineData("HK")]
    public void OfficialCalendarDoesNotExtendIntoUnpublishedYear(string countryCode)
    {
        var instant = new DateTimeOffset(2028, 1, 10, 9, 0, 0, TimeSpan.Zero);
        var projection = Snapshot(instant);
        var zone = TimeZoneInfo.FindSystemTimeZoneById("Singapore Standard Time");
        projection = projection with
        {
            Clocks = [projection.Clocks[0] with
        {
            CountryCode = countryCode, TimeZoneId = zone.Id, LocalTime = TimeZoneInfo.ConvertTime(instant, zone)
        }]
        };
        var settings = new AppSettings(AvailabilitySchedules: new Dictionary<string, AvailabilitySchedule> { ["rome"] = Schedule() });
        var result = AvailabilityPlannerService.Build(projection, projection, "rome", 30, settings, CancellationToken.None);
        var cell = result.Rows.Single(row => row.StartUtc == instant).Cells[0];
        Assert.Equal(AvailabilityState.Unknown, cell.State);
        Assert.Equal("Planner.CalendarUnknown", cell.ReasonKey);
    }

    /// <summary>The two New York 01:00 slots on fall-back day are separate instants with distinct offsets.</summary>
    [Fact]
    public void NewYorkRepeatedHourKeepsBothUtcInstants()
    {
        var instant = new DateTimeOffset(2026, 11, 1, 5, 0, 0, TimeSpan.Zero);
        var projection = Snapshot(instant);
        var clock = projection.Clocks[0] with
        {
            CityId = "new-york",
            CountryCode = "US",
            TimeZoneId = "Eastern Standard Time",
            LocalTime = TimeZoneInfo.ConvertTime(instant, TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time"))
        };
        projection = projection with { Clocks = [clock] };
        var result = AvailabilityPlannerService.Build(projection, projection, clock.CityId, 30, new AppSettings(), CancellationToken.None);
        var first = result.Rows.Single(row => row.StartUtc == instant).Cells[0].LocalStart;
        var second = result.Rows.Single(row => row.StartUtc == instant.AddHours(1)).Cells[0].LocalStart;

        Assert.Equal(first.DateTime, second.DateTime);
        Assert.Equal(1, first.Hour);
        Assert.Equal(TimeSpan.FromHours(-4), first.Offset);
        Assert.Equal(TimeSpan.FromHours(-5), second.Offset);
    }

    /// <summary>Day separators follow the selected reference even when it is not the first city or a whole-hour offset.</summary>
    [Theory]
    [InlineData(15)]
    [InlineData(30)]
    [InlineData(60)]
    public void DayBoundariesUseSelectedQuarterHourReference(int stepMinutes)
    {
        var instant = new DateTimeOffset(2026, 11, 4, 12, 0, 0, TimeSpan.Zero);
        var snapshot = BuildForReference(instant, "Nepal Standard Time", stepMinutes);
        var rows = snapshot.Rows.Where(row => row.ReferenceDayBoundary is not null).ToArray();
        var zone = TimeZoneInfo.FindSystemTimeZoneById("Nepal Standard Time");

        Assert.Equal("rome", snapshot.Cities[0].Clock.CityId);
        Assert.Equal("reference", snapshot.ReferenceCityId);
        Assert.Equal(5, rows.Length);
        Assert.All(rows, row =>
        {
            var boundary = row.ReferenceDayBoundary!;
            var local = TimeZoneInfo.ConvertTime(boundary.InstantUtc, zone);
            Assert.Equal(TimeSpan.Zero, local.TimeOfDay);
            Assert.Equal(DateOnly.FromDateTime(local.DateTime), boundary.LocalDate);
            Assert.Equal(18, boundary.InstantUtc.Hour);
            Assert.Equal(15, boundary.InstantUtc.Minute);
            Assert.NotEqual(TimeSpan.Zero, row.Cells[0].LocalStart.TimeOfDay);
            Assert.Equal(row.StartUtc, boundary.InstantUtc);
        });
    }

    /// <summary>Ordinary DST changes alter the distance between civil starts without adding transition lines.</summary>
    [Theory]
    [InlineData(2026, 3, 29, 23)]
    [InlineData(2026, 10, 25, 25)]
    public void DayBoundaryDistancesRespectShortAndLongCivilDays(int year, int month, int day, int expectedHours)
    {
        var snapshot = Build(new DateTimeOffset(year, month, day, 12, 0, 0, TimeSpan.Zero), Schedule());
        var boundaries = snapshot.Rows.Select(row => row.ReferenceDayBoundary).OfType<AvailabilityDayBoundary>().ToArray();
        var date = new DateOnly(year, month, day);
        var start = boundaries.Single(boundary => boundary.LocalDate == date);
        var end = boundaries.Single(boundary => boundary.LocalDate == date.AddDays(1));

        Assert.Equal(5, boundaries.Length);
        Assert.Equal(expectedHours, (end.InstantUtc - start.InstantUtc).TotalHours);
        Assert.All(boundaries, boundary => Assert.Equal(TimeSpan.Zero,
            TimeZoneInfo.ConvertTime(boundary.InstantUtc, TimeZoneInfo.FindSystemTimeZoneById("W. Europe Standard Time")).TimeOfDay));
    }

    /// <summary>Midnight DST uses the first valid instant and the first occurrence of a repeated midnight.</summary>
    [Theory]
    [InlineData(2025, 3, 9, 1, 5, 23)]
    [InlineData(2025, 11, 2, 0, 4, 25)]
    public void MidnightTransitionsHaveOneBoundaryPerCivilDay(int year, int month, int day, int localHour, int utcHour, int expectedHours)
    {
        // Historical Cuba rules: https://data.iana.org/time-zones/tzdb/northamerica
        var snapshot = BuildForReference(new DateTimeOffset(year, month, day, 12, 0, 0, TimeSpan.Zero), "Cuba Standard Time", 30);
        var boundaries = snapshot.Rows.Select(row => row.ReferenceDayBoundary).OfType<AvailabilityDayBoundary>().ToArray();
        var date = new DateOnly(year, month, day);
        var start = boundaries.Single(boundary => boundary.LocalDate == date);
        var end = boundaries.Single(boundary => boundary.LocalDate == date.AddDays(1));

        Assert.Equal(5, boundaries.Length);
        Assert.Equal(5, boundaries.Select(boundary => boundary.LocalDate).Distinct().Count());
        Assert.Equal(new DateTimeOffset(year, month, day, utcHour, 0, 0, TimeSpan.Zero), start.InstantUtc);
        Assert.Equal(localHour, TimeZoneInfo.ConvertTime(start.InstantUtc, TimeZoneInfo.FindSystemTimeZoneById("Cuba Standard Time")).Hour);
        Assert.Equal(expectedHours, (end.InstantUtc - start.InstantUtc).TotalHours);
    }

    /// <summary>A half-hour DST change places the following midnight inside its elapsed-hour row.</summary>
    [Fact]
    public void ReferenceBoundaryRetainsItsFractionalPositionInsideHourlyRow()
    {
        // Lord Howe's thirty-minute transition: https://data.iana.org/time-zones/tzdb/australasia
        var snapshot = BuildForReference(new DateTimeOffset(2025, 10, 5, 0, 0, 0, TimeSpan.Zero), "Lord Howe Standard Time", 60);
        var row = snapshot.Rows.Single(value => value.ReferenceDayBoundary?.LocalDate == new DateOnly(2025, 10, 6));
        var boundary = row.ReferenceDayBoundary!;

        Assert.Equal(new DateTimeOffset(2025, 10, 5, 13, 0, 0, TimeSpan.Zero), boundary.InstantUtc);
        Assert.Equal(TimeSpan.FromMinutes(30), boundary.InstantUtc - row.StartUtc);
        Assert.Equal(TimeSpan.FromMinutes(30), row.EndUtc - boundary.InstantUtc);
        Assert.Equal(0.5, (boundary.InstantUtc - row.StartUtc).TotalMinutes / (row.EndUtc - row.StartUtc).TotalMinutes);
        Assert.Equal(new TimeSpan(23, 30, 0), row.Cells[1].LocalStart.TimeOfDay);
        Assert.Equal(new TimeSpan(0, 30, 0), row.Cells[1].LocalEnd.TimeOfDay);
    }

    /// <summary>A skipped date produces no fictitious or duplicate line at the date-line transition.</summary>
    [Fact]
    public void SkippedCivilDateDoesNotDuplicateBoundary()
    {
        // Samoa skipped 2011-12-30: https://data.iana.org/time-zones/tzdb/australasia
        var snapshot = BuildForReference(new DateTimeOffset(2011, 12, 29, 12, 0, 0, TimeSpan.Zero), "Samoa Standard Time", 60);
        var boundaries = snapshot.Rows.Select(row => row.ReferenceDayBoundary).OfType<AvailabilityDayBoundary>().ToArray();

        Assert.Equal(4, boundaries.Length);
        Assert.Equal(boundaries.Length, boundaries.Select(boundary => boundary.InstantUtc).Distinct().Count());
        Assert.DoesNotContain(boundaries, boundary => boundary.LocalDate == new DateOnly(2011, 12, 30));
        Assert.Contains(boundaries, boundary => boundary.LocalDate == new DateOnly(2011, 12, 31));
        Assert.Equal(96, (snapshot.Rows[^1].EndUtc - snapshot.Rows[0].StartUtc).TotalHours);
    }

    /// <summary>Builds a second-column reference without relying on the displayed city order.</summary>
    private static AvailabilityPlannerSnapshot BuildForReference(DateTimeOffset instant, string timeZoneId, int stepMinutes)
    {
        var projection = Snapshot(instant);
        var reference = projection.Clocks[0] with
        {
            CityId = "reference",
            CountryCode = "",
            TimeZoneId = timeZoneId,
            LocalTime = TimeZoneInfo.ConvertTime(instant, TimeZoneInfo.FindSystemTimeZoneById(timeZoneId))
        };
        projection = projection with { Clocks = [projection.Clocks[0], reference] };
        return AvailabilityPlannerService.Build(projection, projection, reference.CityId, stepMinutes, new AppSettings(), CancellationToken.None);
    }

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
