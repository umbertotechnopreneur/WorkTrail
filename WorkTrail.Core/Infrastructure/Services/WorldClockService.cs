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
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using WorkTrail.Application;

namespace WorkTrail.Services;

/// <summary>Defines the stable world-clock selection contract.</summary>
public static class WorldClockSelection
{

    #region Fields
    /// <summary>Maximum number of clocks visible in the comparison window.</summary>
    public const int MaximumClocks = 12;

    #endregion

    #region Methods and properties

    /// <summary>No city is assumed before the owner chooses a home city.</summary>
    public static IReadOnlyList<string> Defaults { get; } = [];

    /// <summary>Validates persisted identifiers without requiring catalog I/O during settings deserialization.</summary>
    public static IReadOnlyList<string> NormalizePersisted(IReadOnlyList<string>? cityIds)
    {
        if (cityIds is null)
        {
            return Defaults;
        }

        if (cityIds.Count > MaximumClocks)
        {
            throw new InvalidDataException($"World-clock selection cannot contain more than {MaximumClocks} cities.");
        }

        var normalized = cityIds.Select(static id => id?.Trim().ToLowerInvariant() ?? string.Empty).ToArray();
        if (normalized.Any(static id => id.Length is < 1 or > 80 || id.Any(static character => !char.IsAsciiLetterOrDigit(character) && character != '-')))
        {
            throw new InvalidDataException("World-clock selection contains an invalid city identifier.");
        }

        if (normalized.Distinct(StringComparer.Ordinal).Count() != normalized.Length)
        {
            throw new InvalidDataException("World-clock selection contains duplicate cities.");
        }

        return normalized;
    }

    #endregion

}

/// <summary>Maps locally calculated daylight events to decorative packaged atmosphere layers.</summary>
internal static class WorldClockAtmosphereResolver
{

    #region Fields
    private const string BackdropRoot = "Assets/WorldClocks/Overlays/Backdrops";
    private const string ForegroundRoot = "Assets/WorldClocks/Overlays/Foregrounds";

    #endregion

    #region Methods and properties

    /// <summary>Selects decorative atmosphere layers from local light phase and current weather.</summary>
    internal static WorldClockAtmosphere Resolve(
        DateTimeOffset localTime,
        DateTimeOffset? sunrise,
        DateTimeOffset? sunset,
        bool isDaylight,
        string? currentConditionKey = null)
    {
        var dawnDistance = EventDistance(localTime, sunrise, -60, 45);
        var sunsetDistance = EventDistance(localTime, sunset, -45, 60);
        string phase;
        bool useGoldenHour;
        if (dawnDistance is not null && (sunsetDistance is null || dawnDistance <= sunsetDistance))
        {
            phase = "dawn";
            useGoldenHour = dawnDistance.Value <= 45;
        }
        else if (sunsetDistance is not null)
        {
            phase = "sunset";
            useGoldenHour = sunsetDistance.Value <= 45;
        }
        else
        {
            phase = isDaylight ? "day" : "night";
            useGoldenHour = false;
        }

        if (currentConditionKey is not null
            && currentConditionKey is not (
                "clear" or
                "cloudy" or
                "rain" or
                "snow" or
                "mixed-precipitation" or
                "fog" or
                "lightning" or
                "unknown"))
        {
            throw new InvalidDataException($"Unsupported current weather condition '{currentConditionKey}'.");
        }

        var backdrops = new List<string>();
        var foregrounds = new List<string>();
        if (phase == "night")
        {
            backdrops.Add($"{BackdropRoot}/stars.png");
        }
        else if (useGoldenHour)
        {
            backdrops.Add($"{BackdropRoot}/golden-hour.png");
        }

        var requiresClouds = currentConditionKey is
            "cloudy" or
            "rain" or
            "snow" or
            "mixed-precipitation" or
            "lightning";
        if (requiresClouds)
        {
            var cloudFileName = phase switch
            {
                "dawn" => "clouds-dawn.png",
                "sunset" => "clouds-sunset.png",
                "day" => "clouds-day.png",
                "night" => "clouds-night.png",
                _ => throw new InvalidDataException($"Unsupported local-time phase '{phase}'.")
            };
            backdrops.Add($"{BackdropRoot}/{cloudFileName}");
        }

        switch (currentConditionKey)
        {
            case "rain":
                foregrounds.Add($"{ForegroundRoot}/rain.png");
                break;
            case "snow":
                foregrounds.Add($"{ForegroundRoot}/snow.png");
                break;
            case "mixed-precipitation":
                foregrounds.Add($"{ForegroundRoot}/rain.png");
                foregrounds.Add($"{ForegroundRoot}/snow.png");
                break;
            case "fog":
                foregrounds.Add($"{ForegroundRoot}/fog.png");
                break;
            case "lightning":
                backdrops.Add($"{BackdropRoot}/lightning.png");
                break;
        }

        return new WorldClockAtmosphere(phase, backdrops, foregrounds);
    }

    #endregion

    #region Implementation

    /// <summary>Returns distance from an event only inside its configured phase interval.</summary>
    private static double? EventDistance(
        DateTimeOffset localTime,
        DateTimeOffset? localEvent,
        double startMinutes,
        double endMinutes)
    {
        if (localEvent is null)
        {
            return null;
        }

        var delta = (localTime - localEvent.Value).TotalMinutes;
        return delta >= startMinutes && delta < endMinutes ? Math.Abs(delta) : null;
    }

    #endregion

}

/// <summary>Calculates clocks locally and optionally attaches fresh current weather through a bounded Core service.</summary>
public sealed class WorldClockService : IDisposable
{

    #region Fields
    private readonly string _catalogPath;
    private readonly WorldClockWeatherService _currentWeather;
    private readonly TimeProvider _timeProvider;
    private IReadOnlyDictionary<string, CityRecord>? _cities;

    #endregion

    #region Initialization

    /// <summary>Creates a service over the packaged catalog and the optional environment-configured weather provider.</summary>
    public WorldClockService(
        string? catalogPath = null,
        ILogger<WorldClockService>? logger = null)
        : this(
            catalogPath,
            OpenWeatherCurrentProvider.CreateFromEnvironment(),
            TimeProvider.System,
            logger)
    {
    }

    /// <summary>Connects the packaged city catalog, time source, and weather provider.</summary>
    internal WorldClockService(
        string? catalogPath,
        IWorldClockWeatherProvider weatherProvider,
        TimeProvider timeProvider,
        ILogger<WorldClockService>? logger = null)
    {
        _catalogPath = Path.GetFullPath(catalogPath ?? Path.Combine(AppContext.BaseDirectory, "Assets", "WorldClocks", "world-clocks.sqlite3"));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _currentWeather = new WorldClockWeatherService(weatherProvider, _timeProvider, logger);
    }

    #endregion

    #region Methods and properties

    /// <summary>Gets every approved city in the distributed world-clock catalog.</summary>
    public WorldClockCityCatalog GetCatalog()
    {
        var cities = LoadCities().Values
            .OrderByDescending(static city => city.IsCapital)
            .ThenBy(static city => city.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(static city => new WorldClockCitySummary(
                city.Id,
                city.Name,
                city.CountryCode,
                city.TimeZoneId,
                city.Latitude,
                city.Longitude,
                city.IsCapital))
            .ToArray();
        return new WorldClockCityCatalog(cities, WorldClockSelection.MaximumClocks);
    }

    /// <summary>Builds a weather-free live celestial reference aligned to the UTC minute for shared-window cache reuse.</summary>
    public WorldClockSnapshot BuildCurrentCelestialSnapshot(IReadOnlyList<string>? cityIds)
    {
        var now = _timeProvider.GetUtcNow();
        var instant = new DateTimeOffset(now.UtcTicks - now.UtcTicks % TimeSpan.TicksPerMinute, TimeSpan.Zero);
        return BuildSnapshot(cityIds, instant);
    }

    /// <summary>Builds a deterministic world-clock snapshot for the supplied UTC instant.</summary>
    public WorldClockSnapshot BuildSnapshot(IReadOnlyList<string>? cityIds, DateTimeOffset utcInstant)
    {
        var selection = WorldClockSelection.NormalizePersisted(cityIds);
        var cities = LoadCities();
        var providerConfiguration = _currentWeather.CaptureConfiguration();
        return BuildSnapshotCore(
            selection,
            cities,
            utcInstant,
            new Dictionary<string, WorldClockWeather>(StringComparer.Ordinal),
            new WorldClockWeatherStatus(
                "openweather",
                "not-requested",
                "explicit-instant",
                selection.Count,
                0,
                providerConfiguration.IsConfigured));
    }

    /// <summary>Builds the current snapshot and optionally enriches it with fresh cached weather.</summary>
    public async Task<WorldClockSnapshot> BuildCurrentSnapshotAsync(
        IReadOnlyList<string>? cityIds,
        bool weatherEnabled,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var selection = WorldClockSelection.NormalizePersisted(cityIds);
        var cities = LoadCities();
        var providerConfiguration = _currentWeather.CaptureConfiguration();
        if (selection.Count == 0)
        {
            return await AttachCurrentSpaceWeatherAsync(BuildSnapshotCore(
                selection,
                cities,
                _timeProvider.GetUtcNow(),
                new Dictionary<string, WorldClockWeather>(StringComparer.Ordinal),
                new WorldClockWeatherStatus(
                    "openweather",
                    "not-requested",
                    "no-clocks",
                    0,
                    0,
                    providerConfiguration.IsConfigured)), cancellationToken).ConfigureAwait(false);
        }

        if (!weatherEnabled)
        {
            return await AttachCurrentSpaceWeatherAsync(BuildSnapshotCore(
                selection,
                cities,
                _timeProvider.GetUtcNow(),
                new Dictionary<string, WorldClockWeather>(StringComparer.Ordinal),
                new WorldClockWeatherStatus(
                    "openweather",
                    "disabled",
                    "user-disabled",
                    selection.Count,
                    0,
                    providerConfiguration.IsConfigured)), cancellationToken).ConfigureAwait(false);
        }

        var locations = selection.Select(cityId =>
        {
            if (!cities.TryGetValue(cityId, out var city))
            {
                throw new InvalidDataException($"World-clock city '{cityId}' is not present in the distributed catalog.");
            }

            return new WorldClockWeatherLocation(city.Id, city.Latitude, city.Longitude);
        }).ToArray();
        var weather = await _currentWeather.LoadCurrentAsync(locations, cancellationToken).ConfigureAwait(false);
        // Project clocks after optional network work so the returned local times are current at completion.
        var instantUtc = _timeProvider.GetUtcNow();
        var snapshotWeather = _currentWeather.RevalidateForSnapshot(weather, instantUtc);
        return await AttachCurrentSpaceWeatherAsync(BuildSnapshotCore(
            selection,
            cities,
            instantUtc,
            snapshotWeather.Observations,
            snapshotWeather.Status), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Invalidates observations that were loaded with the previous provider configuration.</summary>
    internal void InvalidateCurrentWeatherConfiguration() => _currentWeather.InvalidateConfiguration();

    /// <summary>Checks a candidate weather key directly with the configured provider.</summary>
    internal Task<WorldClockWeatherApiKeyValidation> ValidateWeatherApiKeyAsync(
        string secret,
        CancellationToken cancellationToken) =>
        _currentWeather.ValidateApiKeyAsync(secret, cancellationToken);

    #endregion

    #region Implementation

    /// <summary>Adds the shared current NOAA condition to each projected clock.</summary>
    private static async Task<WorldClockSnapshot> AttachCurrentSpaceWeatherAsync(
        WorldClockSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        var clocks = new List<WorldClockItem>(snapshot.Clocks.Count);
        foreach (var clock in snapshot.Clocks)
        {
            // Reuse the shared NOAA cache so every selected city receives the same current global condition.
            var alert = await CelestialSpaceWeatherService.GetCurrentSignificantAlertAsync(
                snapshot.InstantUtc, cancellationToken).ConfigureAwait(false);
            clocks.Add(clock with { SpaceWeatherAlert = alert });
        }

        return snapshot with { Clocks = clocks.AsReadOnly() };
    }

    /// <summary>Projects selected cities at one instant with local astronomy, seasons, and weather.</summary>
    private static WorldClockSnapshot BuildSnapshotCore(
        IReadOnlyList<string> selection,
        IReadOnlyDictionary<string, CityRecord> cities,
        DateTimeOffset utcInstant,
        IReadOnlyDictionary<string, WorldClockWeather> weatherByCity,
        WorldClockWeatherStatus weatherStatus)
    {
        var items = new List<WorldClockItem>(selection.Count);
        var mapCities = new List<WorldClockMapCity>(selection.Count);
        foreach (var cityId in selection)
        {
            if (!cities.TryGetValue(cityId, out var city))
            {
                throw new InvalidDataException($"World-clock city '{cityId}' is not present in the distributed catalog.");
            }

            var timeZone = TimeZoneInfo.FindSystemTimeZoneById(city.TimeZoneId);
            var localTime = TimeZoneInfo.ConvertTime(utcInstant, timeZone);
            var events = LocalAstronomy.Calculate(city.Latitude, city.Longitude, timeZone, utcInstant);
            var season = ResolveSeason(city.Hemisphere, localTime.Month);
            var skylineRelativePath = season == "summer" ? city.SummerAssetPath : city.WinterAssetPath;
            var skylineAssetPath = $"Assets/WorldClocks/{skylineRelativePath}";
            var isDaylight = events.SunAltitudeDegrees >= -0.833;
            weatherByCity.TryGetValue(city.Id, out var weather);
            items.Add(new WorldClockItem(
                city.Id,
                city.Name,
                city.CountryCode,
                city.TimeZoneId,
                localTime,
                timeZone.IsDaylightSavingTime(utcInstant),
                WorldClockDaylightSaving.FindEnd(timeZone, utcInstant),
                isDaylight,
                events.Sunrise,
                events.Sunset,
                events.MoonPhaseAngleDegrees,
                skylineAssetPath,
                season,
                WorldClockAtmosphereResolver.Resolve(
                    localTime,
                    events.Sunrise,
                    events.Sunset,
                    isDaylight,
                    weather?.ConditionKey),
                weather));
            mapCities.Add(new WorldClockMapCity(city.Id, city.Name, city.Latitude, city.Longitude));
        }

        var celestial = LocalAstronomy.CalculateGlobal(utcInstant);

        return new WorldClockSnapshot(
            utcInstant.ToUniversalTime(),
            items,
            WorldClockSelection.MaximumClocks,
            weatherStatus,
            new WorldClockMapProjection(
                new WorldClockMapCoordinate(celestial.SunLatitude, celestial.SunLongitude),
                new WorldClockMapCoordinate(celestial.MoonLatitude, celestial.MoonLongitude),
                celestial.MoonPhaseAngleDegrees,
                mapCities));
    }

    #endregion

    #region Methods and properties

    /// <summary>Resolves a selected city's local civil time and projects every selected clock at that instant.</summary>
    internal WorldClockSnapshot BuildSnapshotForLocalTime(
        IReadOnlyList<string>? cityIds,
        WorldClockConversionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var selection = WorldClockSelection.NormalizePersisted(cityIds);
        var referenceCityId = request.ReferenceCityId?.Trim().ToLowerInvariant() ?? string.Empty;
        if (!selection.Contains(referenceCityId, StringComparer.Ordinal))
        {
            throw new WorldClockConversionException(
                "world_clocks.reference_not_selected",
                "WorldClocksReferenceNotSelected",
                "reference_not_selected");
        }

        var cities = LoadCities();
        if (!cities.TryGetValue(referenceCityId, out var referenceCity))
        {
            throw new WorldClockConversionException(
                "world_clocks.reference_not_found",
                "WorldClocksNotFound",
                "not_found");
        }

        var timeZone = TimeZoneInfo.FindSystemTimeZoneById(referenceCity.TimeZoneId);
        var localTime = DateTime.SpecifyKind(request.ReferenceLocalTime, DateTimeKind.Unspecified);
        if (timeZone.IsInvalidTime(localTime))
        {
            throw new WorldClockConversionException(
                "world_clocks.local_time.invalid",
                "WorldClocksLocalTimeInvalid",
                "invalid");
        }

        if (timeZone.IsAmbiguousTime(localTime))
        {
            throw new WorldClockConversionException(
                "world_clocks.local_time.ambiguous",
                "WorldClocksLocalTimeAmbiguous",
                "ambiguous");
        }

        var utcInstant = new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(localTime, timeZone), TimeSpan.Zero);
        return BuildSnapshot(selection, utcInstant);
    }

    /// <summary>Throws when a city identifier is absent from the immutable packaged catalog.</summary>
    public void ValidateCityId(string cityId)
    {
        if (string.IsNullOrWhiteSpace(cityId) || !LoadCities().ContainsKey(cityId.Trim().ToLowerInvariant()))
        {
            throw new ArgumentException("City identifier is not present in the distributed world-clock catalog.", nameof(cityId));
        }
    }

    #endregion

    #region Implementation

    /// <summary>Loads and validates the read-only packaged SQLite city catalog once.</summary>
    private IReadOnlyDictionary<string, CityRecord> LoadCities()
    {
        if (_cities is not null)
        {
            return _cities;
        }

        if (!File.Exists(_catalogPath))
        {
            // Missing product content is a packaging error: no online or hard-coded fallback is allowed.
            throw new FileNotFoundException("The distributed world-clock catalog is missing.", _catalogPath);
        }

        var builder = new SqliteConnectionStringBuilder { DataSource = _catalogPath, Mode = SqliteOpenMode.ReadOnly };
        using var connection = new SqliteConnection(builder.ConnectionString);
        connection.Open();
        using var versionCommand = connection.CreateCommand();
        versionCommand.CommandText = "SELECT value FROM catalog_metadata WHERE key = 'schema_version';";
        if (!string.Equals(versionCommand.ExecuteScalar() as string, "1", StringComparison.Ordinal))
        {
            throw new InvalidDataException("The distributed world-clock catalog schema is unsupported.");
        }

        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT c.id, c.name, c.country_code, c.latitude, c.longitude, c.timezone_id,
                   c.is_capital, c.hemisphere,
                   summer.relative_path, winter.relative_path
            FROM city c
            JOIN skyline_asset summer ON summer.city_id = c.id AND summer.season = 'summer'
            JOIN skyline_asset winter ON winter.city_id = c.id AND winter.season = 'winter';
            """;
        using var reader = command.ExecuteReader();
        var result = new Dictionary<string, CityRecord>(StringComparer.Ordinal);
        while (reader.Read())
        {
            var city = new CityRecord(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetDouble(3),
                reader.GetDouble(4),
                reader.GetString(5),
                reader.GetBoolean(6),
                reader.GetString(7),
                reader.GetString(8),
                reader.GetString(9));
            if (!result.TryAdd(city.Id, city))
            {
                throw new InvalidDataException($"Duplicate city '{city.Id}' in the distributed world-clock catalog.");
            }
        }

        if (result.Count == 0 || !result.ContainsKey("ho-chi-minh-city"))
        {
            throw new InvalidDataException("The distributed world-clock catalog must contain its approved local city.");
        }

        _cities = result;
        return result;
    }

    /// <summary>Selects the packaged seasonal skyline according to hemisphere and month.</summary>
    private static string ResolveSeason(string hemisphere, int month) => hemisphere switch
    {
        "north" => month is >= 4 and <= 9 ? "summer" : "winter",
        "south" => month is >= 10 or <= 3 ? "summer" : "winter",
        "equatorial" => month is >= 4 and <= 9 ? "summer" : "winter",
        _ => throw new InvalidDataException($"Unsupported season model '{hemisphere}'.")
    };

    #endregion

    #region Nested types

    /// <summary>Contains catalog location metadata and seasonal skyline paths.</summary>
    private sealed record CityRecord(
        string Id,
        string Name,
        string CountryCode,
        double Latitude,
        double Longitude,
        string TimeZoneId,
        bool IsCapital,
        string Hemisphere,
        string SummerAssetPath,
        string WinterAssetPath);

    #endregion

    #region Methods and properties

    /// <summary>Releases optional current-weather cache timers.</summary>
    public void Dispose() => _currentWeather.Dispose();

    #endregion

}

/// <summary>Carries stable application and field-validation codes for local-time conversion errors.</summary>
internal sealed class WorldClockConversionException(
    string code,
    string messageKey,
    string validationCode) : Exception(messageKey)
{
    internal string Code { get; } = code;

    internal string MessageKey { get; } = messageKey;

    internal string ValidationCode { get; } = validationCode;
}

/// <summary>Shares the astronomical engine and bounded exact-instant caches across clocks, Moon, sky, and globe.</summary>
internal static class LocalAstronomy
{

    #region Fields
    private static readonly object CacheGate = new();
    private static readonly Dictionary<long, GlobalResult> GlobalCache = new();
    private static readonly Dictionary<DayKey, (DateTimeOffset? Rise, DateTimeOffset? Set)> DayCache = new();

    #endregion

    #region Nested types

    /// <summary>Contains local solar crossings, altitude, and lunar phase at one instant.</summary>
    internal sealed record Result(DateTimeOffset? Sunrise, DateTimeOffset? Sunset, double SunAltitudeDegrees, double MoonPhaseAngleDegrees);
    /// <summary>Contains shared geocentric coordinates and solar and lunar angles.</summary>
    internal sealed record GlobalResult(double SunLatitude, double SunLongitude, double MoonLatitude, double MoonLongitude, double MoonPhaseAngleDegrees, double SolarEclipticLongitudeDegrees);

    #endregion

    #region Methods and properties

    /// <summary>Calculates apparent rise/set crossings for the city-local date and the shared lunar phase.</summary>
    public static Result Calculate(double latitude, double longitude, TimeZoneInfo timeZone, DateTimeOffset utcNow)
    {
        ArgumentNullException.ThrowIfNull(timeZone);
        var localDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(utcNow, timeZone).DateTime);
        var (startUtc, endUtc) = GetUtcDayBounds(localDate, timeZone);
        var observer = new Observer(latitude, longitude, 0);
        var key = new DayKey(latitude, longitude, startUtc, endUtc);
        (DateTimeOffset? Rise, DateTimeOffset? Set) crossings;
        lock (CacheGate)
        {
            if (!DayCache.TryGetValue(key, out crossings))
            {
                var start = new AstroTime(startUtc.UtcDateTime);
                var limit = (endUtc - startUtc).TotalDays;
                // No crossing on polar days is a genuine absent event, not a calculation failure.
                var rise = Astronomy.SearchRiseSet(Body.Sun, observer, Direction.Rise, start, limit);
                var set = Astronomy.SearchRiseSet(Body.Sun, observer, Direction.Set, start, limit);
                crossings = (ToInstant(rise, endUtc), ToInstant(set, endUtc));
                if (DayCache.Count >= 64)
                {
                    DayCache.Remove(DayCache.Keys.First());
                }

                DayCache.Add(key, crossings);
            }
        }

        var time = new AstroTime(utcNow.UtcDateTime);
        var equator = Astronomy.Equator(Body.Sun, time, observer, EquatorEpoch.OfDate, Aberration.Corrected);
        // Retain geometric altitude for the existing daylight threshold and atmosphere contract.
        var horizontal = Astronomy.Horizon(time, observer, equator.ra, equator.dec, Refraction.None);
        return new Result(ToLocal(crossings.Rise, timeZone), ToLocal(crossings.Set, timeZone),
            horizontal.altitude, CalculateGlobal(utcNow).MoonPhaseAngleDegrees);
    }

    /// <summary>Returns cached geocentric subsolar/sublunar coordinates and lunar phase for an exact UTC instant.</summary>
    internal static GlobalResult CalculateGlobal(DateTimeOffset utcNow)
    {
        lock (CacheGate)
        {
            if (GlobalCache.TryGetValue(utcNow.UtcTicks, out var cached))
            {
                return cached;
            }

            var time = new AstroTime(utcNow.UtcDateTime);
            var rotation = Astronomy.Rotation_EQJ_EQD(time);
            var sun = Astronomy.EquatorFromVector(Astronomy.RotateVector(rotation, Astronomy.GeoVector(Body.Sun, time, Aberration.Corrected)));
            var moon = Astronomy.EquatorFromVector(Astronomy.RotateVector(rotation, Astronomy.GeoVector(Body.Moon, time, Aberration.Corrected)));
            var sidereal = Astronomy.SiderealTime(time);
            var result = new GlobalResult(sun.dec, SignedLongitude(15 * (sun.ra - sidereal)),
                moon.dec, SignedLongitude(15 * (moon.ra - sidereal)), Astronomy.MoonPhase(time), Astronomy.SunPosition(time).elon);
            if (GlobalCache.Count >= 32)
            {
                GlobalCache.Remove(GlobalCache.Keys.First());
            }

            GlobalCache.Add(utcNow.UtcTicks, result);
            return result;
        }
    }

    #endregion

    #region Implementation

    /// <summary>Returns a crossing only when it falls before the exclusive day boundary.</summary>
    private static DateTimeOffset? ToInstant(AstroTime? time, DateTimeOffset exclusiveEnd)
    {
        if (time is null)
        {
            return null;
        }

        var instant = new DateTimeOffset(time.ToUtcDateTime());
        return instant < exclusiveEnd ? instant : null;
    }

    /// <summary>Projects an optional UTC crossing into the city time zone.</summary>
    private static DateTimeOffset? ToLocal(DateTimeOffset? utc, TimeZoneInfo zone) =>
        utc is { } instant ? TimeZoneInfo.ConvertTime(instant, zone) : null;

    /// <summary>Normalizes geographic longitude into the signed half-turn interval.</summary>
    private static double SignedLongitude(double degrees) => ((degrees + 180) % 360 + 360) % 360 - 180;

    #endregion

    #region Nested types

    /// <summary>Identifies a location and exact UTC bounds for the local-day cache.</summary>
    private sealed record DayKey(double Latitude, double Longitude, DateTimeOffset StartUtc, DateTimeOffset EndUtc);

    #endregion

    #region Methods and properties

    /// <summary>Resolves the first UTC instant of one local date and the following local date.</summary>
    internal static (DateTimeOffset StartUtc, DateTimeOffset EndUtc) GetUtcDayBounds(
        DateOnly localDate,
        TimeZoneInfo timeZone)
    {
        ArgumentNullException.ThrowIfNull(timeZone);
        var startUtc = ResolveLocalDateStart(localDate, timeZone);
        var endUtc = ResolveLocalDateStart(localDate.AddDays(1), timeZone);
        if (endUtc <= startUtc)
        {
            throw new InvalidDataException("The resolved local-day bounds are not chronological.");
        }

        return (startUtc, endUtc);
    }

    #endregion

    #region Implementation

    /// <summary>Finds the first valid local-day instant across skipped or repeated midnight transitions.</summary>
    private static DateTimeOffset ResolveLocalDateStart(DateOnly localDate, TimeZoneInfo timeZone)
    {
        var localTime = DateTime.SpecifyKind(localDate.ToDateTime(TimeOnly.MinValue), DateTimeKind.Unspecified);
        if (timeZone.IsInvalidTime(localTime))
        {
            var firstInvalidTick = localTime.Ticks;
            var firstValid = localTime;
            var searchLimit = localTime.AddDays(2);
            do
            {
                firstValid = firstValid.AddHours(1);
                if (firstValid > searchLimit)
                {
                    throw new InvalidDataException($"Local date '{localDate:yyyy-MM-dd}' has no valid boundary in time zone '{timeZone.Id}'.");
                }
            }
            while (timeZone.IsInvalidTime(firstValid));

            var lastInvalidTick = firstInvalidTick;
            var firstValidTick = firstValid.Ticks;
            // Narrow the skipped civil-time interval to its first valid tick, rather than assuming a one-hour DST gap.
            while (firstValidTick - lastInvalidTick > 1)
            {
                var candidateTick = lastInvalidTick + ((firstValidTick - lastInvalidTick) / 2);
                var candidate = new DateTime(candidateTick, DateTimeKind.Unspecified);
                if (timeZone.IsInvalidTime(candidate))
                {
                    lastInvalidTick = candidateTick;
                }
                else
                {
                    firstValidTick = candidateTick;
                }
            }

            localTime = new DateTime(firstValidTick, DateTimeKind.Unspecified);
        }

        // The larger offset maps repeated midnight to its earliest UTC occurrence.
        var offset = timeZone.IsAmbiguousTime(localTime)
            ? timeZone.GetAmbiguousTimeOffsets(localTime).Max()
            : timeZone.GetUtcOffset(localTime);
        return new DateTimeOffset(localTime, offset).ToUniversalTime();
    }

    #endregion

}
