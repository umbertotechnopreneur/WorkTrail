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
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using CosineKitty;
using WorkTrail.Application;
using WorkTrail.Services;
using Xunit;

namespace WorkTrail.Core.Tests;

public sealed class CelestialEventServiceTests
{
    /// <summary>The advertised date limits remain valid even when upcoming events and cross-year activity extend into 1899 or 2101.</summary>
    [Theory]
    [InlineData(1900, 1, 1)]
    [InlineData(1900, 12, 31)]
    [InlineData(2100, 1, 1)]
    [InlineData(2100, 12, 31)]
    public void Snapshot_SupportsDateRangeEndpointsAndUpcomingYear(int year, int month, int day)
    {
        var instant = new DateTimeOffset(year, month, day, 12, 0, 0, TimeSpan.Zero);
        var city = new WorldClockCitySummary("equator", "Equator", "", "UTC", 0, 0, false);
        var snapshot = CelestialService.Build(new(city.Id, instant), new([city], 12), CancellationToken.None);
        Assert.Equal(instant, snapshot.InstantUtc);
        Assert.InRange(snapshot.MoonPhaseAngleDegrees, 0, 360);
        Assert.Equal(TropicalZodiacSign.Capricorn, snapshot.Zodiac.CurrentSign);
        Assert.All(snapshot.Bodies, body => Assert.InRange(body.AltitudeDegrees, -90, 90));
        var meteorEvents = snapshot.Agenda.Where(item => item.Kind == CelestialEventKind.MeteorShower).ToArray();
        Assert.Equal(8, meteorEvents.Length);
        Assert.All(meteorEvents, item =>
        {
            Assert.InRange(item.StartUtc, instant, instant.AddDays(367));
            Assert.InRange(DateOnly.FromDateTime(item.StartUtc.UtcDateTime), item.ActivityStartDate!.Value, item.ActivityEndDate!.Value);
            Assert.True(item.IsApproximate);
        });
        var quadrantids = Assert.Single(meteorEvents, item => item.MeteorShowerId == "Quadrantids");
        Assert.Equal(month == 12 ? year + 1 : year, quadrantids.StartUtc.Year);
        Assert.True(quadrantids.ActivityStartDate!.Value.Year < quadrantids.ActivityEndDate!.Value.Year);
        Assert.InRange(snapshot.Agenda.Count(item => item.Kind == CelestialEventKind.MoonPlanetConjunction), 5, 10);
    }

    /// <summary>Checks that found conjunctions are longitude equality, never opposition, and reports their actual angular separation.</summary>
    [Fact]
    public void Conjunctions_AreActualMoonPlanetLongitudeCrossings()
    {
        var instant = new DateTimeOffset(2026, 9, 19, 0, 0, 0, TimeSpan.Zero);
        var events = CelestialEventService.BuildUpcoming(instant, TimeZoneInfo.Utc, CancellationToken.None)
            .Where(item => item.Kind == CelestialEventKind.MoonPlanetConjunction).ToArray();
        Assert.InRange(events.Length, 5, 10);
        Assert.Equal(5, events.Select(item => item.RelatedBody).Distinct().Count());
        Assert.All(events, item =>
        {
            Assert.InRange(item.StartUtc, instant, instant.AddDays(35));
            Assert.False(item.IsApproximate);
            Assert.NotNull(item.RelatedBody);
            Assert.NotNull(item.MoonPhaseAngleDegrees);
            Assert.InRange(item.MoonPhaseAngleDegrees!.Value, 0, 360);
            Assert.Equal(LocalAstronomy.CalculateGlobal(item.StartUtc).MoonPhaseAngleDegrees, item.MoonPhaseAngleDegrees);
            var planet = Enum.Parse<Body>(item.RelatedBody!.Value.ToString());
            var time = new AstroTime(item.StartUtc.UtcDateTime);
            var longitude = Astronomy.PairLongitude(Body.Moon, planet, time);
            Assert.InRange(Math.Min(longitude, 360 - longitude), 0, 0.001);
            var separation = Astronomy.AngleBetween(Astronomy.GeoVector(Body.Moon, time, Aberration.None),
                Astronomy.GeoVector(planet, time, Aberration.None));
            Assert.InRange(Math.Abs(separation - item.SeparationDegrees!.Value), 0, 0.000001);
        });
    }

    /// <summary>Verifies the January recurring peak, J2000 coordinate frame, and a December-to-January activity period.</summary>
    [Fact]
    public void Meteors_PredictNextAnnualPeakWithCrossYearActivity()
    {
        var instant = new DateTimeOffset(2025, 12, 31, 0, 0, 0, TimeSpan.Zero);
        var meteors = CelestialEventService.BuildUpcoming(instant, TimeZoneInfo.Utc, CancellationToken.None)
            .Where(item => item.Kind == CelestialEventKind.MeteorShower).ToArray();
        Assert.Equal(8, meteors.Length);
        Assert.Equal(8, meteors.Select(item => item.MeteorShowerId).Distinct().Count());
        Assert.All(meteors, item =>
        {
            Assert.True(item.IsApproximate);
            Assert.InRange(item.StartUtc, instant, instant.AddDays(367));
            Assert.NotNull(item.ActivityStartDate);
            Assert.NotNull(item.ActivityEndDate);
        });
        var quadrantids = Assert.Single(meteors, item => item.MeteorShowerId == "Quadrantids");
        Assert.Equal(new DateOnly(2025, 12, 28), quadrantids.ActivityStartDate);
        Assert.Equal(new DateOnly(2026, 1, 12), quadrantids.ActivityEndDate);
        // IGN/IMO 2026 reference: January 3, approximately 21h UT. Broad tolerance reflects an expected annual peak.
        var published = new DateTimeOffset(2026, 1, 3, 21, 0, 0, TimeSpan.Zero);
        Assert.InRange(Math.Abs((quadrantids.StartUtc - published).TotalHours), 0, 3);
        var time = new AstroTime(quadrantids.StartUtc.UtcDateTime);
        var sun = Astronomy.GeoVector(Body.Sun, time, Aberration.None);
        var j2000 = Astronomy.SphereFromVector(Astronomy.RotateVector(Astronomy.Rotation_EQJ_ECL(), sun)).lon;
        Assert.InRange(Math.Abs(j2000 - 283.15), 0, 0.001);
        Assert.True(Math.Abs(Astronomy.SunPosition(time).elon - 283.15) > 0.2);
    }

    /// <summary>Shows the next year's shower immediately after a peak and excludes already-past conjunctions from the same daily cache.</summary>
    [Fact]
    public void Upcoming_RefreshesWithinCachedDayAndRollsMeteorYear()
    {
        var start = new DateTimeOffset(2026, 1, 3, 0, 0, 0, TimeSpan.Zero);
        var initial = CelestialEventService.BuildUpcoming(start, TimeZoneInfo.Utc, CancellationToken.None);
        var peak = initial.Single(item => item.MeteorShowerId == "Quadrantids");
        var afterPeak = peak.StartUtc.AddSeconds(1);
        var next = CelestialEventService.BuildUpcoming(afterPeak, TimeZoneInfo.Utc, CancellationToken.None);
        Assert.Equal(2027, next.Single(item => item.MeteorShowerId == "Quadrantids").StartUtc.Year);
        Assert.All(next, item => Assert.True(item.StartUtc >= afterPeak));
    }

    /// <summary>Tropical Aries starts at the March equinox even though the Sun lies in the astronomical constellation Pisces.</summary>
    [Fact]
    public void Zodiac_UsesEquinoxBoundaryAndTwelveEqualSectors()
    {
        var before = CelestialEventService.BuildZodiac(new DateTimeOffset(2024, 3, 20, 2, 0, 0, TimeSpan.Zero));
        var after = CelestialEventService.BuildZodiac(new DateTimeOffset(2024, 3, 20, 4, 0, 0, TimeSpan.Zero));
        Assert.Equal(TropicalZodiacSign.Pisces, before.CurrentSign);
        Assert.Equal(TropicalZodiacSign.Aries, after.CurrentSign);
        Assert.Equal(12, after.Signs.Count);
        Assert.Equal(0, after.Signs[0].StartLongitudeDegrees);
        Assert.Equal(360, after.Signs[^1].EndLongitudeDegrees);
        Assert.All(after.Signs, sign => Assert.Equal(30, sign.EndLongitudeDegrees - sign.StartLongitudeDegrees));
    }

    /// <summary>Corrupt versions, coordinate frames, dates and duplicate shower identifiers cannot silently enter the local database.</summary>
    [Theory]
    [InlineData("\"schemaVersion\": 1", "\"schemaVersion\": 2")]
    [InlineData("\"solarLongitudeFrame\": \"J2000\"", "\"solarLongitudeFrame\": \"of-date\"")]
    [InlineData("\"startMonth\": 12", "\"startMonth\": 13")]
    [InlineData("\"id\": \"Perseids\"", "\"id\": \"Lyrids\"")]
    public void MeteorCatalog_RejectsInvalidData(string original, string replacement)
    {
        using var resource = typeof(CelestialService).Assembly.GetManifestResourceStream("WorkTrail.Data.meteor-showers.json")!;
        using var reader = new StreamReader(resource);
        using var invalid = new MemoryStream(Encoding.UTF8.GetBytes(reader.ReadToEnd().Replace(original, replacement, StringComparison.Ordinal)));
        Assert.Throws<InvalidDataException>(() => CelestialEventService.ParseCatalog(invalid));
    }

    /// <summary>Missing required provenance data fails strict deserialization instead of acquiring a default.</summary>
    [Fact]
    public void MeteorCatalog_RejectsMissingRequiredMetadata()
    {
        using var invalid = new MemoryStream(Encoding.UTF8.GetBytes("{\"schemaVersion\":1}"));
        Assert.Throws<JsonException>(() => CelestialEventService.ParseCatalog(invalid));
    }
}
