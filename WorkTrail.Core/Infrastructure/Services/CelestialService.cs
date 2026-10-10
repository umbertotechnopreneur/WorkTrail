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


using CosineKitty;
using WorkTrail.Application;

namespace WorkTrail.Services;

/// <summary>Calculates offline astronomical ephemerides without weather, location discovery, or presentation dependencies.</summary>
public static class CelestialService
{
    private static readonly object SnapshotGate = new();
    private static readonly Dictionary<(WorldClockCitySummary City, long UtcTicks), CelestialSnapshot> SnapshotCache = new();


    /// <summary>Builds ephemerides for a catalog city; rejects missing cities, invalid coordinates/time zones, and unsupported dates. Cancellation propagates.</summary>
    public static CelestialSnapshot Build(CelestialRequest request, WorldClockCityCatalog catalog, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(catalog);
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(request.CityId);
        var instant = request.InstantUtc.ToUniversalTime();
        if (instant.Year is < 1900 or > 2100)
        {
            // The product deliberately bounds its ephemeris range instead of suggesting historical/future precision.
            throw new ArgumentOutOfRangeException(nameof(request), "Celestial dates must be between 1900 and 2100.");
        }

        var city = catalog.Cities.SingleOrDefault(city => string.Equals(city.Id, request.CityId, StringComparison.Ordinal))
            ?? throw new ArgumentException("The celestial city is not in the approved catalog.", nameof(request));
        if (!double.IsFinite(city.Latitude) || city.Latitude is < -90 or > 90 ||
            !double.IsFinite(city.Longitude) || city.Longitude is < -180 or > 180)
        {
            throw new ArgumentException("The celestial city has invalid geographic coordinates.", nameof(catalog));
        }

        // Installed time-zone rules are authoritative; missing/corrupt rules fail rather than fall back to UTC.
        var zone = TimeZoneInfo.FindSystemTimeZoneById(city.TimeZoneId);
        lock (SnapshotGate)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var key = (city, instant.UtcTicks);
            if (SnapshotCache.TryGetValue(key, out var cached)) return cached;
            var snapshot = BuildCore(city, instant, zone, cancellationToken);
            if (SnapshotCache.Count >= 16) SnapshotCache.Remove(SnapshotCache.Keys.First());
            SnapshotCache.Add(key, snapshot);
            return snapshot;
        }
    }

    private static CelestialSnapshot BuildCore(WorldClockCitySummary city, DateTimeOffset instant, TimeZoneInfo zone, CancellationToken cancellationToken)
    {
        var observer = new Observer(city.Latitude, city.Longitude, 0);
        var time = new AstroTime(instant.UtcDateTime);
        var bodies = Enum.GetValues<CelestialBodyKind>().Select(kind =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var body = Enum.Parse<Body>(kind.ToString());
            var equator = Astronomy.Equator(body, time, observer, EquatorEpoch.OfDate, Aberration.Corrected);
            var horizon = Astronomy.Horizon(time, observer, equator.ra, equator.dec, Refraction.Normal);
            return new CelestialBodyPosition(kind, horizon.altitude, horizon.azimuth, horizon.altitude >= 0);
        }).ToArray();

        var catalog = CelestialSkyCatalog.Current;
        var rotation = Astronomy.Rotation_EQJ_EQD(time);
        var stars = catalog.Stars.Select(star =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var vector = Astronomy.VectorFromSphere(new Spherical(star.DeclinationDegrees, star.RightAscensionDegrees, 1), time);
            var equator = Astronomy.EquatorFromVector(Astronomy.RotateVector(rotation, vector));
            var horizon = Astronomy.Horizon(time, observer, equator.ra, equator.dec, Refraction.Normal);
            return new CelestialStarPosition(star.Id, star.Name, horizon.altitude, horizon.azimuth, star.Magnitude);
        }).ToArray();

        var segments = catalog.Constellations.SelectMany(figure => figure.Segments.Select(segment =>
            new CelestialConstellationSegment(figure.Id, segment[0], segment[1]))).ToArray();
        return new CelestialSnapshot(city.Id, city.Name, city.TimeZoneId, instant, TimeZoneInfo.ConvertTime(instant, zone),
            city.Latitude, city.Longitude, LocalAstronomy.CalculateGlobal(instant).MoonPhaseAngleDegrees, bodies[0].AltitudeDegrees,
            Array.AsReadOnly(bodies), Array.AsReadOnly(stars), Array.AsReadOnly(segments), BuildAgenda(time, observer, zone, cancellationToken))
        {
            Constellations = Array.AsReadOnly(catalog.Constellations.Select(figure => new CelestialConstellationInfo(
                figure.Id, figure.ZodiacSign is null ? null : Enum.Parse<TropicalZodiacSign>(figure.ZodiacSign))).ToArray()),
            Zodiac = CelestialEventService.BuildZodiac(instant),
            SkyAppearance = CelestialSkyPalette.Create(bodies[0].AltitudeDegrees)
        };
    }

    private static IReadOnlyList<CelestialAgendaEvent> BuildAgenda(AstroTime time, Observer observer, TimeZoneInfo zone, CancellationToken cancellationToken)
    {
        var events = new List<CelestialAgendaEvent>();
        var end = time.AddDays(2);
        AddSolarEvents(CelestialEventKind.Sunrise, Direction.Rise, null);
        AddSolarEvents(CelestialEventKind.Sunset, Direction.Set, null);
        AddLunarEvents(CelestialEventKind.Moonrise, Direction.Rise);
        AddLunarEvents(CelestialEventKind.Moonset, Direction.Set);
        AddSolarEvents(CelestialEventKind.CivilDawn, Direction.Rise, -6);
        AddSolarEvents(CelestialEventKind.CivilDusk, Direction.Set, -6);
        AddBlueHours(Direction.Rise, CelestialEventKind.MorningBlueHour, -6, -4);
        AddBlueHours(Direction.Set, CelestialEventKind.EveningBlueHour, -4, -6);

        var quarter = Astronomy.SearchMoonQuarter(time);
        for (var index = 0; index < 4; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var kind = quarter.quarter switch
            {
                0 => CelestialEventKind.NewMoon,
                1 => CelestialEventKind.FirstQuarter,
                2 => CelestialEventKind.FullMoon,
                3 => CelestialEventKind.LastQuarter,
                _ => throw new InvalidOperationException("Astronomy Engine returned an unsupported lunar quarter.")
            };
            AddEvent(kind, quarter.time);
            if (index < 3) quarter = Astronomy.NextMoonQuarter(quarter);
        }

        var year = time.ToUtcDateTime().Year;
        var seasons = new List<(CelestialEventKind Kind, AstroTime Time)>();
        for (var nextYear = year; nextYear <= year + 1; nextYear++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var season = Astronomy.Seasons(nextYear);
            seasons.Add((CelestialEventKind.MarchEquinox, season.mar_equinox));
            seasons.Add((CelestialEventKind.JuneSolstice, season.jun_solstice));
            seasons.Add((CelestialEventKind.SeptemberEquinox, season.sep_equinox));
            seasons.Add((CelestialEventKind.DecemberSolstice, season.dec_solstice));
        }

        var nextSeason = seasons.Where(item => item.Time.ut >= time.ut).MinBy(item => item.Time.ut);
        AddEvent(nextSeason.Kind, nextSeason.Time);
        events.AddRange(CelestialEventService.BuildUpcoming(new DateTimeOffset(time.ToUtcDateTime()), zone, cancellationToken));
        events.AddRange(CelestialImportantDateService.BuildUpcoming(new DateTimeOffset(time.ToUtcDateTime()), zone));
        return Array.AsReadOnly(events.OrderBy(item => item.StartUtc).ThenBy(item => item.Kind).ToArray());

        void AddEvent(CelestialEventKind kind, AstroTime start, AstroTime? finish = null)
        {
            var startUtc = new DateTimeOffset(start.ToUtcDateTime());
            DateTimeOffset? endUtc = finish is null ? null : new DateTimeOffset(finish.ToUtcDateTime());
            events.Add(new(kind, startUtc, endUtc, TimeZoneInfo.ConvertTime(startUtc, zone),
                endUtc is { } value ? TimeZoneInfo.ConvertTime(value, zone) : null));
        }

        void AddSolarEvents(CelestialEventKind kind, Direction direction, double? altitude)
        {
            var cursor = time;
            while (cursor.ut < end.ut)
            {
                cancellationToken.ThrowIfCancellationRequested();
                // A null crossing is normal in polar day/night; do not invent sunrise or substitute another date.
                var crossing = altitude is { } degrees
                    ? Astronomy.SearchAltitude(Body.Sun, observer, direction, cursor, end.ut - cursor.ut, degrees)
                    : Astronomy.SearchRiseSet(Body.Sun, observer, direction, cursor, end.ut - cursor.ut);
                if (crossing is null || crossing.ut >= end.ut) break;
                AddEvent(kind, crossing);
                cursor = crossing.AddDays(1.0 / 86400);
            }
        }

        void AddLunarEvents(CelestialEventKind kind, Direction direction)
        {
            var cursor = time;
            while (cursor.ut < end.ut)
            {
                cancellationToken.ThrowIfCancellationRequested();
                // A null crossing is normal when the Moon does not cross this observer's horizon in the interval.
                var crossing = Astronomy.SearchRiseSet(Body.Moon, observer, direction, cursor, end.ut - cursor.ut);
                if (crossing is null || crossing.ut >= end.ut) break;
                AddEvent(kind, crossing);
                cursor = crossing.AddDays(1.0 / 86400);
            }
        }

        void AddBlueHours(Direction direction, CelestialEventKind kind, double startAltitude, double endAltitude)
        {
            // Look back one day to include an interval already in progress at the requested instant.
            var cursor = time.AddDays(-1);
            while (cursor.ut < end.ut)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var start = Astronomy.SearchAltitude(Body.Sun, observer, direction, cursor, end.ut - cursor.ut, startAltitude);
                if (start is null || start.ut >= end.ut) break;
                var finish = Astronomy.SearchAltitude(Body.Sun, observer, direction, start, 1, endAltitude);
                var reversal = Astronomy.SearchAltitude(Body.Sun, observer,
                    direction == Direction.Rise ? Direction.Set : Direction.Rise, start.AddDays(1.0 / 86400), 1, startAltitude);
                // At high latitudes the Sun can turn back before the other threshold. Such partial intervals are omitted.
                if (finish is not null && finish.ut > time.ut && (reversal is null || finish.ut < reversal.ut))
                    AddEvent(kind, start, finish);
                cursor = start.AddDays(1.0 / 86400);
            }
        }
    }

}
