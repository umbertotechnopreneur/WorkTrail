// SPDX-License-Identifier: MIT

namespace WorkTrail.Application;

/// <summary>Requests a city-based ephemeris for an instant between 1900 and 2100 inclusive.</summary>
public sealed record CelestialRequest(string CityId, DateTimeOffset InstantUtc, bool IncludeSatellites = false);

/// <summary>Identifies the Sun, Moon, or a planet visible from Earth.</summary>
public enum CelestialBodyKind { Sun, Moon, Mercury, Venus, Mars, Jupiter, Saturn, Uranus, Neptune }

/// <summary>Contains apparent horizontal coordinates; above the horizon does not guarantee visibility through daylight or weather.</summary>
public sealed record CelestialBodyPosition(CelestialBodyKind Kind, double AltitudeDegrees, double AzimuthDegrees, bool IsAboveHorizon);

/// <summary>Contains a fixed catalog star projected with precession and nutation; proper motion is omitted and missing V magnitudes remain null.</summary>
public sealed record CelestialStarPosition(string Id, string Name, double AltitudeDegrees, double AzimuthDegrees, double? Magnitude);

/// <summary>Connects catalog star identifiers in a schematic constellation figure, not an IAU boundary.</summary>
public sealed record CelestialConstellationSegment(string ConstellationId, string StartStarId, string EndStarId);

/// <summary>Identifies a drawn constellation figure and, where applicable, its related tropical sign without equating their boundaries.</summary>
public sealed record CelestialConstellationInfo(string Id, TropicalZodiacSign? ZodiacSign);

/// <summary>Contains an SGP4 satellite position from recent CelesTrak elements; above the horizon does not establish optical visibility.</summary>
public sealed record CelestialSatellitePosition(string Id, double AltitudeDegrees, double AzimuthDegrees, DateTimeOffset ElementsEpochUtc);

/// <summary>Identifies calculated solar, lunar, and seasonal events; blue hour uses geometric solar altitude from -6 to -4 degrees.</summary>
public enum CelestialEventKind
{
    Sunrise, Sunset, Moonrise, Moonset, CivilDawn, CivilDusk, MorningBlueHour, EveningBlueHour,
    NewMoon, FirstQuarter, FullMoon, LastQuarter,
    MarchEquinox, JuneSolstice, SeptemberEquinox, DecemberSolstice,
    MoonPlanetConjunction, MeteorShower, ImportantDate, SpaceWeather, Holiday, Saint
}

/// <summary>Classifies a significant NOAA Space Weather Prediction Center condition without inferring local aurora visibility.</summary>
public enum SpaceWeatherEventKind { GeomagneticStorm, SolarRadiationStorm, RadioBlackout, HighEnergyElectronFlux }

/// <summary>Contains one three-hour planetary K-index observation, estimate, or forecast published by NOAA SWPC.</summary>
public sealed record SpaceWeatherKpForecast(DateTimeOffset StartUtc, double KpIndex, string Status, int? NoaaScale);

/// <summary>Contains a currently active NOAA SWPC alert with parsed validity and severity metadata.</summary>
public sealed record SpaceWeatherAlert(
    string Id, DateTimeOffset IssuedUtc, DateTimeOffset? ValidFromUtc, DateTimeOffset? ValidToUtc,
    SpaceWeatherEventKind Kind, int? NoaaScale, double? KpIndex, string Summary);

/// <summary>Contains the cached NOAA SWPC space-weather feed used to add significant conditions to a local agenda.</summary>
public sealed record SpaceWeatherSnapshot(
    DateTimeOffset RetrievedUtc, IReadOnlyList<SpaceWeatherKpForecast> KpForecast,
    IReadOnlyList<SpaceWeatherAlert> ActiveAlerts)
{
    /// <summary>Represents an unavailable or intentionally inapplicable live feed without inventing space-weather data.</summary>
    public static SpaceWeatherSnapshot Empty { get; } = new(DateTimeOffset.MinValue, [], []);
}

/// <summary>Contains event instants and city-local offsets; conjunctions are global longitude equality, meteor activity dates accompany recurring peaks, and important dates identify a local catalog entry.</summary>
public sealed record CelestialAgendaEvent(
    CelestialEventKind Kind, DateTimeOffset StartUtc, DateTimeOffset? EndUtc,
    DateTimeOffset StartLocal, DateTimeOffset? EndLocal,
    CelestialBodyKind? RelatedBody = null, string? MeteorShowerId = null, double? SeparationDegrees = null,
    DateOnly? ActivityStartDate = null, DateOnly? ActivityEndDate = null, bool IsApproximate = false,
    double? MoonPhaseAngleDegrees = null, string? ImportantDateId = null,
    SpaceWeatherEventKind? SpaceWeatherKind = null, int? NoaaScale = null, double? KpIndex = null,
    string? SpaceWeatherAlertId = null,
    string? CalendarCountryCode = null, string? CalendarLabel = null, string? CalendarQuality = null,
    string? CalendarArtworkFileName = null, string? CalendarEntryKey = null, DateOnly? CalendarDate = null)
{
    /// <summary>Indicates that the projected instant falls in this event's half-open interval; point events are not ongoing.</summary>
    public bool IsAtReferenceInstant { get; init; }
}

/// <summary>Identifies equal tropical zodiac sectors beginning at the March equinox; these are not IAU constellations.</summary>
public enum TropicalZodiacSign { Aries, Taurus, Gemini, Cancer, Leo, Virgo, Libra, Scorpio, Sagittarius, Capricorn, Aquarius, Pisces }

/// <summary>Describes one 30-degree tropical sector, inclusive at its start and exclusive at its end.</summary>
public sealed record CelestialZodiacSector(TropicalZodiacSign Sign, double StartLongitudeDegrees, double EndLongitudeDegrees);

/// <summary>Contains the current solar tropical sector and the complete informational zodiac; no predictions are inferred.</summary>
public sealed record CelestialZodiacSnapshot(TropicalZodiacSign CurrentSign, double SunLongitudeDegrees, IReadOnlyList<CelestialZodiacSector> Signs);

/// <summary>Contains a local sky, tropical zodiac and sorted agenda: 48-hour solar/lunar horizon events, lunar quarters, next season, conjunctions, annual meteor peaks, and local important dates.</summary>
public sealed record CelestialSnapshot(
    string CityId, string CityName, string TimeZoneId,
    DateTimeOffset InstantUtc, DateTimeOffset LocalTime,
    double Latitude, double Longitude, double MoonPhaseAngleDegrees, double SunAltitudeDegrees,
    IReadOnlyList<CelestialBodyPosition> Bodies, IReadOnlyList<CelestialStarPosition> Stars,
    IReadOnlyList<CelestialConstellationSegment> ConstellationSegments, IReadOnlyList<CelestialAgendaEvent> Agenda)
{
    /// <summary>Insertion position of the reference-time marker in the sorted agenda, after events starting at that instant.</summary>
    public int AgendaReferenceIndex => Agenda.Count(item => item.StartUtc <= InstantUtc);

    /// <summary>Describes the schematic constellation figures drawn from the embedded sky catalog.</summary>
    public IReadOnlyList<CelestialConstellationInfo> Constellations { get; init; } = [];

    /// <summary>Contains recent optional satellite positions; empty when current orbital elements are unavailable or the instant is historical.</summary>
    public IReadOnlyList<CelestialSatellitePosition> Satellites { get; init; } = [];

    /// <summary>Provides the current tropical solar sector independently of astronomical constellation figures.</summary>
    public required CelestialZodiacSnapshot Zodiac { get; init; }

    /// <summary>Contains the interpolated city-specific solar-altitude sky palette.</summary>
    public required CelestialSkyAppearance SkyAppearance { get; init; }

    /// <summary>Contains currently relevant NOAA SWPC data; it is empty for historical snapshots or unavailable feeds.</summary>
    public SpaceWeatherSnapshot SpaceWeather { get; init; } = SpaceWeatherSnapshot.Empty;
}

/// <summary>Identifies a national holiday calendar available in the offline astronomical agenda.</summary>
public sealed record CelestialCalendarCountry(string Code, string Name);

/// <summary>Lists the national calendars bundled with WorkTrail; local subdivisions are outside this catalog.</summary>
public static class CelestialCalendarCountries
{
    /// <summary>Gets all supported national calendars in stable display order.</summary>
    public static IReadOnlyList<CelestialCalendarCountry> All { get; } =
    [
        new("CN", "China"), new("VN", "Vietnam"), new("IT", "Italy"), new("FR", "France"),
        new("DE", "Germany"), new("CA", "Canada"), new("US", "United States"),
        new("IN", "India"), new("BD", "Bangladesh"), new("JP", "Japan"),
        new("KR", "South Korea"), new("LA", "Laos"), new("KH", "Cambodia"),
        new("AU", "Australia"), new("NZ", "New Zealand"), new("SG", "Singapore"),
        new("PL", "Poland")
    ];
}
