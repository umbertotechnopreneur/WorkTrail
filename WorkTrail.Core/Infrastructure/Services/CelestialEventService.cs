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


using System.Text.Json;
using System.Text.Json.Serialization;
using CosineKitty;
using WorkTrail.Application;

namespace WorkTrail.Services;

/// <summary>Shares global conjunction searches, source-backed recurring meteor estimates, and tropical solar sectors.</summary>
internal static class CelestialEventService
{
    private static readonly Lazy<MeteorCatalog> MeteorData = new(LoadCatalog);
    private static readonly object EventGate = new();
    private static readonly Dictionary<DateOnly, IReadOnlyList<GlobalEvent>> EventCache = new();
    private static readonly IReadOnlyList<CelestialZodiacSector> ZodiacSectors = Array.AsReadOnly(
        Enum.GetValues<TropicalZodiacSign>().Select(sign => new CelestialZodiacSector(sign, (int)sign * 30, ((int)sign + 1) * 30)).ToArray());

    /// <summary>Uses the shared solar longitude to classify twelve equal tropical sectors, independently of constellation boundaries.</summary>
    internal static CelestialZodiacSnapshot BuildZodiac(DateTimeOffset instant)
    {
        var longitude = LocalAstronomy.CalculateGlobal(instant).SolarEclipticLongitudeDegrees;
        return new CelestialZodiacSnapshot((TropicalZodiacSign)(int)(longitude / 30), longitude, ZodiacSectors);
    }

    /// <summary>Projects cached global event instants into the observer's time zone without claiming local visibility.</summary>
    internal static IReadOnlyList<CelestialAgendaEvent> BuildUpcoming(DateTimeOffset instant, TimeZoneInfo zone, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var utc = instant.ToUniversalTime();
        var date = DateOnly.FromDateTime(utc.UtcDateTime);
        IReadOnlyList<GlobalEvent> globalEvents;
        lock (EventGate)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!EventCache.TryGetValue(date, out globalEvents!))
            {
                globalEvents = BuildGlobalEvents(date, cancellationToken);
                if (EventCache.Count >= 8) EventCache.Remove(EventCache.Keys.First());
                EventCache.Add(date, globalEvents);
            }
        }

        var conjunctionEnd = utc.AddDays(35);
        var meteorEnd = utc.AddDays(367);
        var upcoming = globalEvents.Where(item => item.InstantUtc >= utc
            && item.InstantUtc <= (item.Kind == CelestialEventKind.MoonPlanetConjunction ? conjunctionEnd : meteorEnd));
        // Each shower is represented once by its next expected annual peak, including a December/January activity span.
        var selected = upcoming.Where(item => item.Kind != CelestialEventKind.MeteorShower)
            .Concat(upcoming.Where(item => item.Kind == CelestialEventKind.MeteorShower)
                .GroupBy(item => item.ShowerId).Select(group => group.MinBy(item => item.InstantUtc)!));
        return Array.AsReadOnly(selected.OrderBy(item => item.InstantUtc).Select(item => new CelestialAgendaEvent(
            item.Kind, item.InstantUtc, null, TimeZoneInfo.ConvertTime(item.InstantUtc, zone), null,
            item.Body, item.ShowerId, item.SeparationDegrees, item.ActivityStart, item.ActivityEnd,
            item.Kind == CelestialEventKind.MeteorShower,
            item.Kind == CelestialEventKind.MoonPlanetConjunction
                ? LocalAstronomy.CalculateGlobal(item.InstantUtc).MoonPhaseAngleDegrees : null)).ToArray());
    }

    private static IReadOnlyList<GlobalEvent> BuildGlobalEvents(DateOnly date, CancellationToken cancellationToken)
    {
        var events = new List<GlobalEvent>();
        var start = new AstroTime(date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));
        var end = start.AddDays(36);
        foreach (var name in CelestialSkyCatalog.Current.ConjunctionBodies)
        {
            var kind = Enum.Parse<CelestialBodyKind>(name);
            var body = Enum.Parse<Body>(kind.ToString());
            var context = new ConjunctionContext(body);
            foreach (var crossing in FindAscendingCrossings(context, start, end, 0.5, cancellationToken))
            {
                var separation = Astronomy.AngleBetween(
                    Astronomy.GeoVector(Body.Moon, crossing, Aberration.None), Astronomy.GeoVector(body, crossing, Aberration.None));
                events.Add(new(CelestialEventKind.MoonPlanetConjunction, new DateTimeOffset(crossing.ToUtcDateTime()), kind,
                    SeparationDegrees: separation));
            }
        }

        foreach (var shower in MeteorData.Value.Showers)
        {
            for (var year = date.Year; year <= date.Year + 1; year++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var crossesYear = shower.StartMonth > shower.EndMonth;
                var activityStart = new DateOnly(crossesYear ? year - 1 : year, shower.StartMonth, shower.StartDay);
                var activityEnd = new DateOnly(year, shower.EndMonth, shower.EndDay);
                var searchStart = new AstroTime(activityStart.AddDays(-2).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));
                var searchEnd = new AstroTime(activityEnd.AddDays(3).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));
                var peaks = FindAscendingCrossings(new MeteorPeakContext(shower.PeakSolarLongitudeDegrees), searchStart, searchEnd, 1, cancellationToken);
                // A missing/multiple crossing means the curated catalog and its coordinate frame disagree; never substitute a civil date.
                var peak = peaks.SingleOrDefault() ?? throw new InvalidDataException("A meteor catalog peak cannot be resolved in its activity period.");
                events.Add(new(CelestialEventKind.MeteorShower, new DateTimeOffset(peak.ToUtcDateTime()),
                    ShowerId: shower.Id, ActivityStart: activityStart, ActivityEnd: activityEnd));
            }
        }

        return Array.AsReadOnly(events.OrderBy(item => item.InstantUtc).ToArray());
    }

    private static IEnumerable<AstroTime> FindAscendingCrossings(SearchContext context, AstroTime start, AstroTime end, double stepDays, CancellationToken cancellationToken)
    {
        var left = start;
        var leftValue = context.Eval(left);
        while (left.ut < end.ut)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var right = new AstroTime(Math.Min(left.ut + stepDays, end.ut));
            var rightValue = context.Eval(right);
            // Signed longitude jumps +180 to -180 at opposition. Only continuous negative-to-positive brackets contain conjunctions.
            if (leftValue <= 0 && rightValue > 0 && rightValue - leftValue < 180)
            {
                var crossing = Astronomy.Search(context, left, right, 0.1)
                    ?? throw new InvalidOperationException("A bracketed celestial longitude crossing did not converge.");
                yield return crossing;
            }
            left = right;
            leftValue = rightValue;
        }
    }

    private static MeteorCatalog LoadCatalog()
    {
        // The curated embedded resource is read once; absent or invalid data fails explicitly instead of manufacturing event dates.
        using var stream = typeof(CelestialEventService).Assembly.GetManifestResourceStream("WorkTrail.Data.meteor-showers.json")
            ?? throw new InvalidDataException("The embedded meteor catalog is missing.");
        return ParseCatalog(stream);
    }

    /// <summary>Validates the strict local catalog schema, coordinate frame, identifiers, ranges, and provenance before use.</summary>
    internal static MeteorCatalog ParseCatalog(Stream stream)
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
            RespectRequiredConstructorParameters = true
        };
        var catalog = JsonSerializer.Deserialize<MeteorCatalog>(stream, options)
            ?? throw new InvalidDataException("The meteor catalog is empty.");
        if (catalog.SchemaVersion != 1 || catalog.ReferenceYear is < 1900 or > 2100 || catalog.SolarLongitudeFrame != "J2000"
            || !Uri.TryCreate(catalog.SourceUrl, UriKind.Absolute, out var source) || source.Scheme != Uri.UriSchemeHttps
            || catalog.Showers is null || catalog.Showers.Count == 0)
            throw new InvalidDataException("The meteor catalog metadata is unsupported.");

        var identifiers = new HashSet<string>(StringComparer.Ordinal);
        var iauCodes = new HashSet<string>(StringComparer.Ordinal);
        foreach (var shower in catalog.Showers)
        {
            if (shower is null || string.IsNullOrEmpty(shower.Id) || !identifiers.Add(shower.Id)
                || shower.IauCode is not { Length: 3 } || !shower.IauCode.All(char.IsAsciiLetterUpper)
                || !iauCodes.Add(shower.IauCode)
                || !double.IsFinite(shower.PeakSolarLongitudeDegrees) || shower.PeakSolarLongitudeDegrees is < 0 or >= 360)
                throw new InvalidDataException("The meteor catalog contains invalid or duplicate shower data.");
            try
            {
                // Use a non-leap year because these activity ranges must exist in every supported year.
                var nonLeapYear = DateTime.IsLeapYear(catalog.ReferenceYear) ? catalog.ReferenceYear + 1 : catalog.ReferenceYear;
                var start = new DateOnly(shower.StartMonth > shower.EndMonth ? nonLeapYear - 1 : nonLeapYear, shower.StartMonth, shower.StartDay);
                var finish = new DateOnly(nonLeapYear, shower.EndMonth, shower.EndDay);
                if (finish <= start || finish.DayNumber - start.DayNumber > 90)
                    throw new InvalidDataException("A meteor activity period is invalid.");
            }
            catch (ArgumentOutOfRangeException exception)
            {
                throw new InvalidDataException("A meteor activity date is invalid.", exception);
            }
        }
        return catalog with { Showers = Array.AsReadOnly(catalog.Showers.ToArray()) };
    }

    private static double SignedLongitude(double longitude) => ((longitude + 180) % 360 + 360) % 360 - 180;

    private sealed class ConjunctionContext(Body planet) : SearchContext
    {
        /// <summary>Returns the signed geocentric ecliptic longitude difference, increasing through a Moon/planet conjunction.</summary>
        public override double Eval(AstroTime time) => SignedLongitude(Astronomy.PairLongitude(Body.Moon, planet, time));
    }

    private sealed class MeteorPeakContext(double targetLongitude) : SearchContext
    {
        /// <summary>Uses the catalog's J2000 ecliptic frame, not the precessing tropical longitude used by zodiac and seasons.</summary>
        public override double Eval(AstroTime time)
        {
            var sun = Astronomy.GeoVector(Body.Sun, time, Aberration.None);
            var ecliptic = Astronomy.SphereFromVector(Astronomy.RotateVector(Astronomy.Rotation_EQJ_ECL(), sun));
            return SignedLongitude(ecliptic.lon - targetLongitude);
        }
    }

    internal sealed record MeteorCatalog(int SchemaVersion, int ReferenceYear, string SourceUrl, string SolarLongitudeFrame, IReadOnlyList<MeteorShower> Showers);
    internal sealed record MeteorShower(string Id, string IauCode, int StartMonth, int StartDay, int EndMonth, int EndDay, double PeakSolarLongitudeDegrees);
    private sealed record GlobalEvent(CelestialEventKind Kind, DateTimeOffset InstantUtc, CelestialBodyKind? Body = null,
        string? ShowerId = null, double? SeparationDegrees = null, DateOnly? ActivityStart = null, DateOnly? ActivityEnd = null);
}
