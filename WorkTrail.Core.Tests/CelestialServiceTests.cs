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
using System.Text.Json;
using System.Threading;
using WorkTrail.Application;
using WorkTrail.Services;
using Xunit;

namespace WorkTrail.Core.Tests;

public sealed class CelestialServiceTests
{
    private static readonly WorldClockCitySummary London = new("london", "London", "GB", "Europe/London", 51.5074, -0.1278, true);

    /// <summary>Checks the March 2024 equinox against the published USNO minute and local solar geometry.</summary>
    [Fact]
    public void Snapshot_ProjectsReferenceEquinoxAndSunPosition()
    {
        var instant = new DateTimeOffset(2024, 3, 19, 12, 0, 0, TimeSpan.Zero);
        var snapshot = Build(London, instant);
        var equinox = Assert.Single(snapshot.Agenda, item => item.Kind == CelestialEventKind.MarchEquinox);
        var reference = new DateTimeOffset(2024, 3, 20, 3, 6, 0, TimeSpan.Zero);
        Assert.InRange(Math.Abs((equinox.StartUtc - reference).TotalMinutes), 0, 3);
        var sun = Assert.Single(snapshot.Bodies, body => body.Kind == CelestialBodyKind.Sun);
        Assert.InRange(sun.AltitudeDegrees, 37, 40);
        Assert.InRange(sun.AzimuthDegrees, 175, 181);
        Assert.True(sun.IsAboveHorizon);
        Assert.Equal(9, snapshot.Bodies.Count);
    }

    /// <summary>Ensures all consumers reuse the same lunar result and equivalent offset instants reuse a snapshot.</summary>
    [Fact]
    public void Snapshot_SharesLunarEngineAndCachesEquivalentInstants()
    {
        var instant = new DateTimeOffset(2026, 9, 19, 12, 0, 0, TimeSpan.Zero);
        var snapshot = Build(London, instant);
        var same = Build(London, instant.ToOffset(TimeSpan.FromHours(7)));
        var global = LocalAstronomy.CalculateGlobal(instant);
        var clock = LocalAstronomy.Calculate(London.Latitude, London.Longitude, TimeZoneInfo.FindSystemTimeZoneById(London.TimeZoneId), instant);
        Assert.Same(snapshot, same);
        Assert.Same(global, LocalAstronomy.CalculateGlobal(instant));
        Assert.Equal(global.MoonPhaseAngleDegrees, snapshot.MoonPhaseAngleDegrees);
        Assert.Equal(clock.MoonPhaseAngleDegrees, snapshot.MoonPhaseAngleDegrees);
    }

    /// <summary>Checks that polar daylight leaves absent solar events absent while lunar and seasonal events remain available.</summary>
    [Fact]
    public void Snapshot_PolarDayDoesNotInventSunriseOrBlueHour()
    {
        var pole = London with { Id = "north-pole", Latitude = 90, Longitude = 0, TimeZoneId = "UTC" };
        var snapshot = Build(pole, new DateTimeOffset(2024, 6, 21, 12, 0, 0, TimeSpan.Zero));
        Assert.True(snapshot.SunAltitudeDegrees > 20);
        Assert.DoesNotContain(snapshot.Agenda, item => item.Kind is CelestialEventKind.Sunrise or CelestialEventKind.Sunset
            or CelestialEventKind.CivilDawn or CelestialEventKind.CivilDusk
            or CelestialEventKind.MorningBlueHour or CelestialEventKind.EveningBlueHour);
        Assert.Equal(5, snapshot.Agenda.Count(item => item.Kind is not CelestialEventKind.MoonPlanetConjunction
            and not CelestialEventKind.MeteorShower and not CelestialEventKind.ImportantDate));
        Assert.Equal(3, snapshot.Agenda.Count(item => item.Kind == CelestialEventKind.ImportantDate));
        var clock = LocalAstronomy.Calculate(90, 0, TimeZoneInfo.Utc, snapshot.InstantUtc);
        Assert.Null(clock.Sunrise);
        Assert.Null(clock.Sunset);
    }

    /// <summary>Verifies real interval endpoints, chronological ordering and daylight-saving-aware event offsets.</summary>
    [Fact]
    public void Agenda_UsesCityOffsetsAcrossDaylightSavingTransition()
    {
        var snapshot = Build(London, new DateTimeOffset(2024, 3, 30, 0, 0, 0, TimeSpan.Zero));
        Assert.Equal(snapshot.Agenda.OrderBy(item => item.StartUtc).ThenBy(item => item.Kind), snapshot.Agenda);
        var sunrises = snapshot.Agenda.Where(item => item.Kind == CelestialEventKind.Sunrise).ToArray();
        Assert.Equal(2, sunrises.Length);
        Assert.Equal(TimeSpan.Zero, sunrises[0].StartLocal.Offset);
        Assert.Equal(TimeSpan.FromHours(1), sunrises[1].StartLocal.Offset);
        Assert.All(snapshot.Agenda, item =>
        {
            Assert.Equal(item.StartUtc, item.StartLocal.ToUniversalTime());
            if (item.EndUtc is { } end)
            {
                Assert.True(end > item.StartUtc);
                Assert.Equal(end, item.EndLocal!.Value.ToUniversalTime());
                Assert.InRange((end - item.StartUtc).TotalMinutes, 1, 90);
            }
        });
    }

    /// <summary>An interval already in progress stays visible without relabeling its true start instant.</summary>
    [Fact]
    public void Agenda_RetainsActiveBlueHour()
    {
        var snapshot = Build(London, new DateTimeOffset(2024, 3, 20, 0, 0, 0, TimeSpan.Zero));
        var blue = snapshot.Agenda.First(item => item.Kind == CelestialEventKind.MorningBlueHour);
        var middle = blue.StartUtc + (blue.EndUtc!.Value - blue.StartUtc) / 2;
        var current = Build(London, middle);
        Assert.Contains(current.Agenda, item => item.Kind == blue.Kind && item.StartUtc < middle && item.EndUtc > middle);
    }

    /// <summary>Checks DTO round-tripping and ensures every constellation endpoint names a genuine finite catalog star.</summary>
    [Fact]
    public void Snapshot_RoundTripsThroughRuntimeJsonWithValidConstellationReferences()
    {
        var snapshot = Build(London, new DateTimeOffset(2026, 9, 19, 0, 0, 0, TimeSpan.Zero));
        var json = JsonSerializer.Serialize(snapshot);
        var restored = JsonSerializer.Deserialize<CelestialSnapshot>(json)!;
        Assert.Equal(snapshot.InstantUtc, restored.InstantUtc);
        Assert.Equal(snapshot.Zodiac.CurrentSign, restored.Zodiac.CurrentSign);
        Assert.Equal(snapshot.SkyAppearance, restored.SkyAppearance);
        Assert.Equal(snapshot.Agenda, restored.Agenda);
        Assert.Equal(CelestialSkyCatalog.Current.Stars.Count, restored.Stars.Count);
        Assert.All(restored.Stars, star =>
        {
            Assert.InRange(star.AltitudeDegrees, -90, 90);
            Assert.InRange(star.AzimuthDegrees, 0, 360);
        });
        Assert.All(restored.ConstellationSegments, segment =>
        {
            Assert.Contains(restored.Stars, star => star.Id == segment.StartStarId);
            Assert.Contains(restored.Stars, star => star.Id == segment.EndStarId);
        });
    }

    /// <summary>Both Dippers retain all seven stars and respect their northern sky visibility.</summary>
    [Theory]
    [InlineData("UrsaMajor")]
    [InlineData("UrsaMinor")]
    public void Snapshot_DippersHaveCompleteFiguresAndRespectObserverHemisphere(string constellationId)
    {
        var instant = new DateTimeOffset(2026, 9, 19, 0, 0, 0, TimeSpan.Zero);
        var north = Build(London, instant);
        var south = Build(London with { Id = "southern-observer", Latitude = -60, TimeZoneId = "UTC" }, instant);
        var segments = north.ConstellationSegments.Where(segment => segment.ConstellationId == constellationId).ToArray();
        var starIds = segments.SelectMany(segment => new[] { segment.StartStarId, segment.EndStarId }).Distinct().ToArray();

        Assert.Contains(north.Constellations, figure => figure.Id == constellationId && figure.ZodiacSign is null);
        Assert.Equal(7, segments.Length);
        Assert.Equal(7, starIds.Length);
        Assert.All(starIds, id =>
        {
            // Both figures are circumpolar at London's latitude and never rise at latitude 60 south.
            Assert.True(Assert.Single(north.Stars, star => star.Id == id).AltitudeDegrees > 0);
            Assert.True(Assert.Single(south.Stars, star => star.Id == id).AltitudeDegrees < 0);
        });
    }

    /// <summary>Rejects unsupported dates and invalid coordinates before invoking ephemeris calculations.</summary>
    [Theory]
    [InlineData(1899)]
    [InlineData(2101)]
    public void Snapshot_RejectsUnsupportedDates(int year) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => Build(London, new DateTimeOffset(year, 1, 1, 0, 0, 0, TimeSpan.Zero)));

    /// <summary>Missing cities, corrupt coordinates, missing zones, and canceled requests fail explicitly.</summary>
    [Fact]
    public void Snapshot_RejectsInvalidInputAndCancellation()
    {
        var instant = new DateTimeOffset(2024, 3, 20, 0, 0, 0, TimeSpan.Zero);
        Assert.Throws<ArgumentException>(() => CelestialService.Build(new("missing", instant), new([London], 12), CancellationToken.None));
        Assert.Throws<ArgumentException>(() => Build(London with { Latitude = double.NaN }, instant));
        Assert.Throws<TimeZoneNotFoundException>(() => Build(London with { TimeZoneId = "invalid/celestial-zone" }, instant));
        Assert.Throws<OperationCanceledException>(() => CelestialService.Build(new(London.Id, instant), new([London], 12), new CancellationToken(true)));
    }

    private static CelestialSnapshot Build(WorldClockCitySummary city, DateTimeOffset instant) =>
        CelestialService.Build(new(city.Id, instant), new([city], 12), CancellationToken.None);
}
