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
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using WorkTrail.Application;
using WorkTrail.Services;
using Xunit;

namespace WorkTrail.Core.Tests;

public sealed class WorldClockServiceTests
{
    /// <summary>Projects independent DST state and each active city's own end date through the shared snapshot.</summary>
    [Fact]
    public void Snapshot_ReportsDaylightSavingForEverySelectedClock()
    {
        using var service = new WorldClockService(
            RepositoryFile("WorkTrail", "Assets", "WorldClocks", "world-clocks.sqlite3"));

        var snapshot = service.BuildSnapshot(
            ["ho-chi-minh-city", "rome", "new-york", "sydney"],
            new DateTimeOffset(2026, 9, 5, 12, 0, 0, TimeSpan.Zero));

        Assert.False(snapshot.Clocks[0].IsDaylightSavingTime);
        Assert.Null(snapshot.Clocks[0].DaylightSavingEndsAt);
        Assert.True(snapshot.Clocks[1].IsDaylightSavingTime);
        Assert.Equal(new DateTimeOffset(2026, 10, 25, 1, 0, 0, TimeSpan.Zero), snapshot.Clocks[1].DaylightSavingEndsAt);
        Assert.True(snapshot.Clocks[2].IsDaylightSavingTime);
        Assert.Equal(new DateTimeOffset(2026, 11, 1, 6, 0, 0, TimeSpan.Zero), snapshot.Clocks[2].DaylightSavingEndsAt);
        Assert.False(snapshot.Clocks[3].IsDaylightSavingTime);
        Assert.Null(snapshot.Clocks[3].DaylightSavingEndsAt);
    }

    [Fact]
    public void Catalog_ContainsRequiredLocalCityAndApprovedAdditionalCities()
    {
        var catalogPath = RepositoryFile("WorkTrail", "Assets", "WorldClocks", "world-clocks.sqlite3");
        var service = new WorldClockService(catalogPath);

        var catalog = service.GetCatalog();

        Assert.NotEmpty(catalog.Cities);
        Assert.True(catalog.Cities.Count(static city => city.IsCapital) >= 100);
        Assert.Equal(WorldClockSelection.MaximumClocks, catalog.MaximumClocks);
        Assert.Contains(catalog.Cities, static city => city.Id == "ho-chi-minh-city" && !city.IsCapital);
        Assert.All(new[] { "new-york", "toronto", "sydney", "saint-petersburg", "mumbai" }, cityId =>
            Assert.Contains(catalog.Cities, city => city.Id == cityId && !city.IsCapital));
    }

    [Fact]
    public void Catalog_ContainsEveryEuropeanCapitalAndTenApprovedCitiesForUnitedStatesAustraliaAndRussia()
    {
        var catalogPath = RepositoryFile("WorkTrail", "Assets", "WorldClocks", "world-clocks.sqlite3");
        var catalog = new WorldClockService(catalogPath).GetCatalog();
        var europeanCapitalCountries = new[]
        {
            "AD", "AL", "AT", "BA", "BE", "BG", "BY", "CH", "CZ", "DE", "DK", "EE",
            "ES", "FI", "FR", "GB", "GR", "HR", "HU", "IE", "IS", "IT", "LI", "LT",
            "LU", "LV", "MC", "MD", "ME", "MK", "MT", "NL", "NO", "PL", "PT", "RO",
            "RS", "RU", "SE", "SI", "SK", "SM", "UA", "VA",
        };

        Assert.All(europeanCapitalCountries, countryCode =>
            Assert.Contains(catalog.Cities, city => city.CountryCode == countryCode && city.IsCapital));
        Assert.All(new[] { "US", "AU", "RU" }, countryCode =>
            Assert.Equal(10, catalog.Cities.Count(city => city.CountryCode == countryCode)));
    }

    [Fact]
    public void Astronomy_KnownNewAndFullMoonInstantsHaveExpectedPhaseAngles()
    {
        var utc = TimeZoneInfo.Utc;

        var newMoon = LocalAstronomy.Calculate(0d, 0d, utc, new DateTimeOffset(2000, 1, 6, 18, 14, 0, TimeSpan.Zero));
        var fullMoon = LocalAstronomy.Calculate(0d, 0d, utc, new DateTimeOffset(2000, 1, 21, 4, 40, 0, TimeSpan.Zero));

        var newMoonDistance = Math.Min(
            newMoon.MoonPhaseAngleDegrees,
            360d - newMoon.MoonPhaseAngleDegrees);
        Assert.InRange(newMoonDistance, 0d, 5d);
        Assert.InRange(fullMoon.MoonPhaseAngleDegrees, 175d, 185d);
    }

    [Fact]
    public void Astronomy_LondonEquinoxProducesPlausibleLocalSunriseAndSunset()
    {
        var result = LocalAstronomy.Calculate(
            51.5074,
            -0.1278,
            TimeZoneInfo.Utc,
            new DateTimeOffset(2026, 3, 20, 12, 0, 0, TimeSpan.Zero));

        Assert.NotNull(result.Sunrise);
        Assert.NotNull(result.Sunset);
        Assert.InRange(result.Sunrise.Value.Hour, 5, 7);
        Assert.InRange(result.Sunset.Value.Hour, 17, 19);
        Assert.True(result.Sunrise < result.Sunset);
    }

    [Fact]
    public void Astronomy_GlobalProjectionTracksEquinoxSunAndFiniteMoonPosition()
    {
        var projection = LocalAstronomy.CalculateGlobal(
            new DateTimeOffset(2026, 3, 20, 12, 0, 0, TimeSpan.Zero));

        Assert.InRange(projection.SunLatitude, -1d, 1d);
        Assert.InRange(projection.SunLongitude, -5d, 5d);
        Assert.InRange(projection.MoonLatitude, -90d, 90d);
        Assert.InRange(projection.MoonLongitude, -180d, 180d);
        Assert.InRange(projection.MoonPhaseAngleDegrees, 0d, 360d);
    }

    [Fact]
    public void Snapshot_MapCarriesSelectedCitiesAndCelestialProjection()
    {
        using var service = new WorldClockService(
            RepositoryFile("WorkTrail", "Assets", "WorldClocks", "world-clocks.sqlite3"));

        var snapshot = service.BuildSnapshot(
            ["ho-chi-minh-city", "rome", "new-york"],
            new DateTimeOffset(2026, 9, 4, 0, 45, 0, TimeSpan.Zero));

        Assert.Equal(snapshot.Clocks.Select(static clock => clock.CityId), snapshot.Map.Cities.Select(static city => city.CityId));
        Assert.Equal(10.8231d, snapshot.Map.Cities[0].Latitude, precision: 3);
        Assert.Equal(106.6297d, snapshot.Map.Cities[0].Longitude, precision: 3);
        Assert.InRange(snapshot.Map.Sun.Latitude, -90d, 90d);
        Assert.InRange(snapshot.Map.Sun.Longitude, -180d, 180d);
        Assert.InRange(snapshot.Map.Moon.Latitude, -90d, 90d);
        Assert.InRange(snapshot.Map.Moon.Longitude, -180d, 180d);
    }

    [Fact]
    public void Astronomy_LocalDayBoundsHandleMidnightDstTransitionsExplicitly()
    {
        var cuba = TimeZoneInfo.FindSystemTimeZoneById("Cuba Standard Time");

        var spring = LocalAstronomy.GetUtcDayBounds(new DateOnly(2026, 3, 8), cuba);
        var autumn = LocalAstronomy.GetUtcDayBounds(new DateOnly(2026, 11, 1), cuba);

        Assert.Equal(new DateTimeOffset(2026, 3, 8, 5, 0, 0, TimeSpan.Zero), spring.StartUtc);
        Assert.Equal(TimeSpan.FromHours(23), spring.EndUtc - spring.StartUtc);
        Assert.Equal(new DateTimeOffset(2026, 11, 1, 4, 0, 0, TimeSpan.Zero), autumn.StartUtc);
        Assert.Equal(TimeSpan.FromHours(25), autumn.EndUtc - autumn.StartUtc);
    }

    /// <summary>Verifies empty selections are explicit while null still resolves to product defaults.</summary>
    [Fact]
    public void Selection_RejectsDuplicatesAndMoreThanTwelveClocks()
    {
        Assert.Empty(WorldClockSelection.NormalizePersisted([]));
        Assert.Equal(WorldClockSelection.Defaults, WorldClockSelection.NormalizePersisted(null));
        Assert.Throws<InvalidDataException>(() => WorldClockSelection.NormalizePersisted(["london", "london"]));
        Assert.Throws<InvalidDataException>(() => WorldClockSelection.NormalizePersisted(["london", "paris", "tokyo", "hanoi", "berlin", "rome", "madrid", "lisbon", "oslo", "stockholm", "helsinki", "vienna", "prague"]));
    }

    /// <summary>Verifies an empty selection does not invoke optional weather and retains safe configuration state.</summary>
    [Fact]
    public async Task CurrentSnapshot_EmptySelectionSkipsWeatherAndPreservesProviderPresence()
    {
        var provider = new FakeWeatherProvider(_ => throw new InvalidOperationException("Weather must not run."));
        using var service = new WorldClockService(
            RepositoryFile("WorkTrail", "Assets", "WorldClocks", "world-clocks.sqlite3"),
            provider,
            TimeProvider.System);

        var snapshot = await service.BuildCurrentSnapshotAsync([], weatherEnabled: true, CancellationToken.None);

        Assert.Empty(snapshot.Clocks);
        Assert.Equal(0, provider.CallCount);
        Assert.Equal("not-requested", snapshot.WeatherStatus.State);
        Assert.Equal("no-clocks", snapshot.WeatherStatus.ReasonCode);
        Assert.True(snapshot.WeatherStatus.IsProviderConfigured);
    }

    [Fact]
    public void Conversion_UsesTheReferenceCityAndPreservesFractionalOffsets()
    {
        var catalogPath = RepositoryFile("WorkTrail", "Assets", "WorldClocks", "world-clocks.sqlite3");
        var service = new WorldClockService(catalogPath);

        var snapshot = service.BuildSnapshotForLocalTime(
            ["ho-chi-minh-city", "kathmandu"],
            new WorldClockConversionRequest(
                "ho-chi-minh-city",
                new DateTime(2026, 8, 30, 10, 18, 0, DateTimeKind.Unspecified)));

        Assert.Equal(new DateTimeOffset(2026, 8, 30, 3, 18, 0, TimeSpan.Zero), snapshot.InstantUtc);
        Assert.Equal(new TimeSpan(7, 0, 0), snapshot.Clocks.Single(clock => clock.CityId == "ho-chi-minh-city").LocalTime.Offset);
        Assert.Equal(new TimeSpan(5, 45, 0), snapshot.Clocks.Single(clock => clock.CityId == "kathmandu").LocalTime.Offset);
        Assert.Equal(new TimeOnly(9, 3), TimeOnly.FromDateTime(snapshot.Clocks.Single(clock => clock.CityId == "kathmandu").LocalTime.DateTime));
    }

    [Fact]
    public void Conversion_MatchesTheApprovedHoChiMinhTokyoParisExample()
    {
        var catalogPath = RepositoryFile("WorkTrail", "Assets", "WorldClocks", "world-clocks.sqlite3");
        var service = new WorldClockService(catalogPath);

        var snapshot = service.BuildSnapshotForLocalTime(
            ["ho-chi-minh-city", "tokyo", "paris"],
            new WorldClockConversionRequest(
                "ho-chi-minh-city",
                new DateTime(2026, 8, 30, 10, 18, 0, DateTimeKind.Unspecified)));

        Assert.Equal(new TimeOnly(10, 18), LocalTime("ho-chi-minh-city"));
        Assert.Equal(new TimeOnly(12, 18), LocalTime("tokyo"));
        Assert.Equal(new TimeOnly(5, 18), LocalTime("paris"));

        TimeOnly LocalTime(string cityId) =>
            TimeOnly.FromDateTime(snapshot.Clocks.Single(clock => clock.CityId == cityId).LocalTime.DateTime);
    }

    [Theory]
    [InlineData(2026, 3, 29, 2, 30, "world_clocks.local_time.invalid")]
    [InlineData(2026, 10, 25, 2, 30, "world_clocks.local_time.ambiguous")]
    public void Conversion_RejectsInvalidAndAmbiguousCivilTimes(
        int year,
        int month,
        int day,
        int hour,
        int minute,
        string expectedCode)
    {
        var catalogPath = RepositoryFile("WorkTrail", "Assets", "WorldClocks", "world-clocks.sqlite3");
        var service = new WorldClockService(catalogPath);

        var exception = Assert.Throws<WorldClockConversionException>(() => service.BuildSnapshotForLocalTime(
            ["paris"],
            new WorldClockConversionRequest(
                "paris",
                new DateTime(year, month, day, hour, minute, 0, DateTimeKind.Unspecified))));

        Assert.Equal(expectedCode, exception.Code);
    }

    [Fact]
    public void Atmosphere_UsesNeutralAstronomyBackdropsWithoutInventingWeather()
    {
        var offset = TimeSpan.FromHours(2);
        var sunrise = new DateTimeOffset(2026, 6, 15, 6, 0, 0, offset);
        var sunset = new DateTimeOffset(2026, 6, 15, 20, 0, 0, offset);

        var earlyDawn = WorldClockAtmosphereResolver.Resolve(sunrise.AddMinutes(-55), sunrise, sunset, false);
        var dawn = WorldClockAtmosphereResolver.Resolve(sunrise.AddMinutes(-30), sunrise, sunset, false);
        var day = WorldClockAtmosphereResolver.Resolve(sunrise.AddHours(4), sunrise, sunset, true);
        var dusk = WorldClockAtmosphereResolver.Resolve(sunset.AddMinutes(30), sunrise, sunset, false);
        var night = WorldClockAtmosphereResolver.Resolve(sunset.AddHours(3), sunrise, sunset, false);
        var polarDay = WorldClockAtmosphereResolver.Resolve(sunrise, null, null, true);
        var polarNight = WorldClockAtmosphereResolver.Resolve(sunrise, null, null, false);

        Assert.Equal("dawn", earlyDawn.Phase);
        Assert.Empty(earlyDawn.BackdropAssetPaths);
        Assert.Equal(
            new[]
            {
                "Assets/WorldClocks/Overlays/Backdrops/golden-hour.png"
            },
            dawn.BackdropAssetPaths);
        Assert.Equal("day", day.Phase);
        Assert.Empty(day.BackdropAssetPaths);
        Assert.Equal(
            new[]
            {
                "Assets/WorldClocks/Overlays/Backdrops/golden-hour.png"
            },
            dusk.BackdropAssetPaths);
        Assert.Equal("night", night.Phase);
        Assert.Equal(
            new[]
            {
                "Assets/WorldClocks/Overlays/Backdrops/stars.png"
            },
            night.BackdropAssetPaths);
        Assert.Equal("day", polarDay.Phase);
        Assert.Equal("night", polarNight.Phase);

        foreach (var atmosphere in new[] { earlyDawn, dawn, day, dusk, night, polarDay, polarNight })
        {
            Assert.Empty(atmosphere.ForegroundAssetPaths);
            Assert.DoesNotContain(
                atmosphere.BackdropAssetPaths,
                static path => path.Contains("rain", StringComparison.Ordinal)
                    || path.Contains("fog", StringComparison.Ordinal)
                    || path.Contains("snow", StringComparison.Ordinal)
                    || path.Contains("lightning", StringComparison.Ordinal)
                    || path.Contains("aurora", StringComparison.Ordinal));
        }
    }

    [Fact]
    public void Atmosphere_ComposesFreshWeatherWithTheResolvedLocalTimePhase()
    {
        var offset = TimeSpan.FromHours(2);
        var sunrise = new DateTimeOffset(2026, 6, 15, 6, 0, 0, offset);
        var sunset = new DateTimeOffset(2026, 6, 15, 20, 0, 0, offset);

        var cloudyDawn = WorldClockAtmosphereResolver.Resolve(
            sunrise.AddMinutes(-30), sunrise, sunset, false, "cloudy");
        var rainySunset = WorldClockAtmosphereResolver.Resolve(
            sunset.AddMinutes(20), sunrise, sunset, false, "rain");
        var snowDay = WorldClockAtmosphereResolver.Resolve(
            sunrise.AddHours(4), sunrise, sunset, true, "snow");
        var mixedNight = WorldClockAtmosphereResolver.Resolve(
            sunset.AddHours(3), sunrise, sunset, false, "mixed-precipitation");
        var fogDay = WorldClockAtmosphereResolver.Resolve(
            sunrise.AddHours(4), sunrise, sunset, true, "fog");
        var lightningDay = WorldClockAtmosphereResolver.Resolve(
            sunrise.AddHours(4), sunrise, sunset, true, "lightning");

        Assert.Equal(
            new[]
            {
                "Assets/WorldClocks/Overlays/Backdrops/golden-hour.png",
                "Assets/WorldClocks/Overlays/Backdrops/clouds-dawn.png"
            },
            cloudyDawn.BackdropAssetPaths);
        Assert.Equal(
            new[]
            {
                "Assets/WorldClocks/Overlays/Backdrops/golden-hour.png",
                "Assets/WorldClocks/Overlays/Backdrops/clouds-sunset.png"
            },
            rainySunset.BackdropAssetPaths);
        Assert.Equal(
            new[] { "Assets/WorldClocks/Overlays/Foregrounds/rain.png" },
            rainySunset.ForegroundAssetPaths);
        Assert.Equal(
            new[] { "Assets/WorldClocks/Overlays/Foregrounds/snow.png" },
            snowDay.ForegroundAssetPaths);
        Assert.Equal(
            new[]
            {
                "Assets/WorldClocks/Overlays/Foregrounds/rain.png",
                "Assets/WorldClocks/Overlays/Foregrounds/snow.png"
            },
            mixedNight.ForegroundAssetPaths);
        Assert.Equal(
            new[] { "Assets/WorldClocks/Overlays/Foregrounds/fog.png" },
            fogDay.ForegroundAssetPaths);
        Assert.Equal(
            new[]
            {
                "Assets/WorldClocks/Overlays/Backdrops/clouds-day.png",
                "Assets/WorldClocks/Overlays/Backdrops/lightning.png"
            },
            lightningDay.BackdropAssetPaths);

        foreach (var atmosphere in new[] { cloudyDawn, rainySunset, snowDay, mixedNight, fogDay, lightningDay })
        {
            Assert.DoesNotContain(
                atmosphere.BackdropAssetPaths.Concat(atmosphere.ForegroundAssetPaths),
                static path => path.Contains("aurora", StringComparison.Ordinal));
        }
    }

    /// <summary>Verifies provider response codes map to safe validation outcomes without echoing the candidate key.</summary>
    [Theory]
    [InlineData(211, "lightning")]
    [InlineData(301, "rain")]
    [InlineData(511, "rain")]
    [InlineData(615, "mixed-precipitation")]
    [InlineData(621, "snow")]
    [InlineData(701, "fog")]
    [InlineData(741, "fog")]
    [InlineData(800, "clear")]
    [InlineData(804, "cloudy")]
    public void OpenWeatherConditionIds_MapToStableAtmosphereConditions(
        int conditionId,
        string expectedCondition)
    {
        Assert.Equal(expectedCondition, OpenWeatherCurrentProvider.MapCondition([conditionId]));
    }

    /// <summary>Verifies that an unknown weather condition preserves the local sky without adding an overlay.</summary>
    /// <summary>Verifies transport failures produce an unavailable result without exposing the candidate key.</summary>
    [Fact]
    public void UnknownWeatherCondition_KeepsTheLocalSkyWithoutInventingAnOverlay()
    {
        var localTime = new DateTimeOffset(2026, 8, 30, 12, 0, 0, TimeSpan.Zero);

        var atmosphere = WorldClockAtmosphereResolver.Resolve(
            localTime,
            sunrise: null,
            sunset: null,
            isDaylight: true,
            currentConditionKey: "unknown");

        Assert.Equal("day", atmosphere.Phase);
        Assert.Empty(atmosphere.BackdropAssetPaths);
        Assert.Empty(atmosphere.ForegroundAssetPaths);
    }

    /// <summary>Verifies that unsupported OpenWeather atmosphere events map to the explicit unknown condition.</summary>
    [Theory]
    [InlineData(711)]
    [InlineData(721)]
    [InlineData(731)]
    [InlineData(751)]
    [InlineData(761)]
    [InlineData(762)]
    [InlineData(771)]
    [InlineData(781)]
    public void OpenWeatherConditionIds_UseExplicitUnknownFallbackForUnsupportedAtmosphereEvents(
        int conditionId)
    {
        Assert.Equal("unknown", OpenWeatherCurrentProvider.MapCondition([conditionId]));
    }

    /// <summary>Verifies that parsing an unknown condition retains its temperature and observation timestamp.</summary>
    [Fact]
    public void OpenWeatherObservation_PreservesTemperatureAndTimeForUnknownCondition()
    {
        using var document = JsonDocument.Parse(
            """
            {
              "weather": [{ "id": 781 }],
              "main": { "temp": 27.4 },
              "dt": 1788060600
            }
            """);

        var observation = OpenWeatherCurrentProvider.ParseObservation(document.RootElement);

        Assert.Equal(27.4d, observation.TemperatureCelsius);
        Assert.Equal("unknown", observation.ConditionKey);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1788060600), observation.ObservedAtUtc);
    }

    [Theory]
    [InlineData("[{ \"id\": \"200\" }, { \"id\": 800 }]")]
    [InlineData("[800]")]
    public void OpenWeatherObservation_RejectsAnyMalformedConditionElement(
        string weatherJson)
    {
        using var document = JsonDocument.Parse(
            $$"""
            {
              "weather": {{weatherJson}},
              "main": { "temp": 27.4 },
              "dt": 1788060600
            }
            """);

        var exception = Assert.Throws<InvalidDataException>(() =>
            OpenWeatherCurrentProvider.ParseObservation(document.RootElement));

        Assert.Equal(
            "Current weather response contains an invalid condition identifier.",
            exception.Message);
    }

    [Fact]
    public async Task OpenWeatherProvider_UsesCatalogCoordinatesMetricUnitsAndObservationTimestamp()
    {
        var observedAt = new DateTimeOffset(2026, 8, 30, 3, 15, 0, TimeSpan.Zero);
        var handler = new RecordingHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                $$"""
                {
                  "weather": [{ "id": 802 }],
                  "main": { "temp": 27.4 },
                  "dt": {{observedAt.ToUnixTimeSeconds()}}
                }
                """,
                Encoding.UTF8,
                "application/json")
        });
        using var httpClient = new HttpClient(handler);
        var provider = OpenWeatherCurrentProvider.CreateForTests(httpClient, "0123456789abcdef0123456789abcdef");

        var observation = await provider.GetCurrentAsync(
            new WorldClockWeatherLocation("london", 51.5074, -0.1278),
            CancellationToken.None);

        Assert.Equal(27.4, observation.TemperatureCelsius);
        Assert.Equal("cloudy", observation.ConditionKey);
        Assert.Equal(observedAt, observation.ObservedAtUtc);
        Assert.Equal(1, handler.CallCount);
        Assert.NotNull(handler.RequestUri);
        Assert.Equal("api.openweathermap.org", handler.RequestUri.Host);
        Assert.Contains("lat=51.5074", handler.RequestUri.Query, StringComparison.Ordinal);
        Assert.Contains("lon=-0.1278", handler.RequestUri.Query, StringComparison.Ordinal);
        Assert.Contains("units=metric", handler.RequestUri.Query, StringComparison.Ordinal);
        Assert.Contains("appid=0123456789abcdef0123456789abcdef", handler.RequestUri.Query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task OpenWeatherProvider_ResolvesDynamicConfigurationOncePerRequest()
    {
        var observedAt = new DateTimeOffset(2026, 8, 30, 3, 15, 0, TimeSpan.Zero);
        var handler = new RecordingHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                $$"""
                {
                  "weather": [{ "id": 800 }],
                  "main": { "temp": 26.0 },
                  "dt": {{observedAt.ToUnixTimeSeconds()}}
                }
                """,
                Encoding.UTF8,
                "application/json")
        });
        using var httpClient = new HttpClient(handler);
        var configuration = (ApiKey: (string?)"0123456789abcdef0123456789abcdef", State: "configured");
        var resolverCalls = 0;
        var provider = OpenWeatherCurrentProvider.CreateDynamicForTests(
            httpClient,
            () =>
            {
                Interlocked.Increment(ref resolverCalls);
                return configuration;
            });

        _ = await provider.GetCurrentAsync(
            new WorldClockWeatherLocation("london", 51.5074, -0.1278),
            CancellationToken.None);

        Assert.Equal(1, resolverCalls);
        Assert.Contains("appid=0123456789abcdef0123456789abcdef", handler.RequestUri!.Query, StringComparison.Ordinal);

        configuration = ("fedcba9876543210fedcba9876543210", "configured");
        _ = await provider.GetCurrentAsync(
            new WorldClockWeatherLocation("london", 51.5074, -0.1278),
            CancellationToken.None);

        Assert.Equal(2, resolverCalls);
        Assert.Contains("appid=fedcba9876543210fedcba9876543210", handler.RequestUri!.Query, StringComparison.Ordinal);
    }

    [Fact]
    public void OpenWeatherApiKeyValidation_AcceptsBoundedVisibleSecretsOnly()
    {
        Assert.True(OpenWeatherCurrentProvider.IsPlausibleApiKey("0123456789abcdef0123456789abcdef"));
        Assert.False(OpenWeatherCurrentProvider.IsPlausibleApiKey(null));
        Assert.False(OpenWeatherCurrentProvider.IsPlausibleApiKey("short"));
        Assert.False(OpenWeatherCurrentProvider.IsPlausibleApiKey(" 0123456789abcdef0123456789abcdef"));
        Assert.False(OpenWeatherCurrentProvider.IsPlausibleApiKey("0123456789abcdef\n0123456789abcdef"));
        Assert.False(OpenWeatherCurrentProvider.IsPlausibleApiKey(new string('a', 129)));
    }

    [Theory]
    [InlineData(HttpStatusCode.OK, (int)WorldClockWeatherApiKeyValidation.Accepted)]
    [InlineData(HttpStatusCode.Unauthorized, (int)WorldClockWeatherApiKeyValidation.Rejected)]
    [InlineData(HttpStatusCode.Forbidden, (int)WorldClockWeatherApiKeyValidation.Rejected)]
    [InlineData(HttpStatusCode.TooManyRequests, (int)WorldClockWeatherApiKeyValidation.RateLimited)]
    [InlineData(HttpStatusCode.ServiceUnavailable, (int)WorldClockWeatherApiKeyValidation.Unavailable)]
    public async Task OpenWeatherApiKeyValidation_MapsProviderResponsesWithoutReturningTheSecret(
        HttpStatusCode statusCode,
        int expectedValue)
    {
        var expected = (WorldClockWeatherApiKeyValidation)expectedValue;
        using var httpClient = new HttpClient(new RecordingHttpMessageHandler(_ => new HttpResponseMessage(statusCode)));
        var provider = OpenWeatherCurrentProvider.CreateForTests(
            httpClient,
            new string('x', 32));
        var candidate = new string('y', 32);

        var result = await provider.ValidateApiKeyAsync(candidate, CancellationToken.None);

        Assert.Equal(expected, result);
        Assert.DoesNotContain(candidate, result.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task OpenWeatherApiKeyValidation_ReportsNetworkFailureWithoutExposingTheSecret()
    {
        using var httpClient = new HttpClient(new RecordingHttpMessageHandler(_ =>
            throw new HttpRequestException("Synthetic validation failure.")));
        var provider = OpenWeatherCurrentProvider.CreateForTests(httpClient, new string('x', 32));

        var result = await provider.ValidateApiKeyAsync(new string('z', 32), CancellationToken.None);

        Assert.Equal(WorldClockWeatherApiKeyValidation.Unavailable, result);
    }

    [Fact]
    public async Task OpenWeatherProvider_TimesOutWhileReadingAStalledResponseBody()
    {
        var handler = new RecordingHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StreamContent(new BlockingReadStream())
        });
        using var httpClient = new HttpClient(handler)
        {
            Timeout = Timeout.InfiniteTimeSpan
        };
        var provider = OpenWeatherCurrentProvider.CreateForTests(
            httpClient,
            "0123456789abcdef0123456789abcdef",
            TimeSpan.FromMilliseconds(50));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            provider.GetCurrentAsync(
                new WorldClockWeatherLocation("london", 51.5074, -0.1278),
                CancellationToken.None));

        Assert.Equal("Current weather provider request timed out.", exception.Message);
        Assert.DoesNotContain("0123456789abcdef0123456789abcdef", exception.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task OpenWeatherProvider_RejectsAnOversizedResponseBodyBeforeParsing()
    {
        var handler = new RecordingHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(new byte[OpenWeatherCurrentProvider.MaximumResponseBytes + 1])
        });
        using var httpClient = new HttpClient(handler);
        var provider = OpenWeatherCurrentProvider.CreateForTests(httpClient, "0123456789abcdef0123456789abcdef");

        var exception = await Assert.ThrowsAsync<InvalidDataException>(() =>
            provider.GetCurrentAsync(
                new WorldClockWeatherLocation("london", 51.5074, -0.1278),
                CancellationToken.None));

        Assert.Equal("Current weather response exceeds the supported size.", exception.Message);
        Assert.DoesNotContain("0123456789abcdef0123456789abcdef", exception.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task CurrentWeather_IsCachedForTwelveMinutesAcrossTheSelectedCities()
    {
        var catalogPath = RepositoryFile("WorkTrail", "Assets", "WorldClocks", "world-clocks.sqlite3");
        var timeProvider = new ManualTimeProvider(
            new DateTimeOffset(2026, 8, 30, 3, 18, 0, TimeSpan.Zero));
        var provider = new FakeWeatherProvider(_ => new WorldClockWeatherObservation(
            25d,
            "cloudy",
            timeProvider.GetUtcNow()));
        var service = new WorldClockService(catalogPath, provider, timeProvider);

        var first = await service.BuildCurrentSnapshotAsync(
            ["london", "paris"],
            true,
            CancellationToken.None);
        timeProvider.Advance(TimeSpan.FromMinutes(11));
        var cached = await service.BuildCurrentSnapshotAsync(
            ["london", "paris"],
            true,
            CancellationToken.None);
        timeProvider.Advance(TimeSpan.FromMinutes(2));
        var refreshed = await service.BuildCurrentSnapshotAsync(
            ["london", "paris"],
            true,
            CancellationToken.None);

        Assert.Equal(4, provider.CallCount);
        Assert.Equal("available", first.WeatherStatus.State);
        Assert.Equal("available", cached.WeatherStatus.State);
        Assert.Equal("available", refreshed.WeatherStatus.State);
        Assert.All(first.Clocks, static clock =>
        {
            Assert.NotNull(clock.Weather);
            Assert.True(clock.Weather.IsFresh);
        });
    }

    [Fact]
    public async Task ProviderConfigurationInvalidation_DropsCachedObservationsImmediately()
    {
        var catalogPath = RepositoryFile("WorkTrail", "Assets", "WorldClocks", "world-clocks.sqlite3");
        var timeProvider = new ManualTimeProvider(
            new DateTimeOffset(2026, 8, 30, 3, 18, 0, TimeSpan.Zero));
        var provider = new FakeWeatherProvider(_ => new WorldClockWeatherObservation(
            25d,
            "clear",
            timeProvider.GetUtcNow()));
        var service = new WorldClockService(catalogPath, provider, timeProvider);

        _ = await service.BuildCurrentSnapshotAsync(["london"], true, CancellationToken.None);
        _ = await service.BuildCurrentSnapshotAsync(["london"], true, CancellationToken.None);
        Assert.Equal(1, provider.CallCount);

        service.InvalidateCurrentWeatherConfiguration();
        _ = await service.BuildCurrentSnapshotAsync(["london"], true, CancellationToken.None);

        Assert.Equal(2, provider.CallCount);
    }

    /// <summary>Verifies that an unrequested cached observation remains available until its maximum age.</summary>
    [Fact]
    public async Task CachedObservation_IsRetainedUntilItsMaximumAgeWithoutAnotherClockLoad()
    {
        var timeProvider = new ManualTimeProvider(
            new DateTimeOffset(2026, 8, 30, 3, 18, 0, TimeSpan.Zero));
        var provider = new FakeWeatherProvider(_ => new WorldClockWeatherObservation(
            25d,
            "clear",
            timeProvider.GetUtcNow()));
        using var weather = new WorldClockWeatherService(provider, timeProvider);

        _ = await weather.LoadCurrentAsync(
            [new WorldClockWeatherLocation("london", 51.5074, -0.1278)],
            CancellationToken.None);

        Assert.Equal(1, weather.CachedObservationCount);
        timeProvider.Advance(WorldClockWeatherService.CacheDuration);
        Assert.Equal(1, weather.CachedObservationCount);

        timeProvider.Advance(
            WorldClockWeatherService.MaximumObservationAge
            - WorldClockWeatherService.CacheDuration
            - TimeSpan.FromSeconds(1));
        Assert.Equal(1, weather.CachedObservationCount);

        timeProvider.Advance(TimeSpan.FromSeconds(1));

        Assert.Equal(0, weather.CachedObservationCount);
        Assert.Equal(1, provider.CallCount);
    }

    /// <summary>Verifies that an expired refresh window serves cached weather while successful revalidation completes.</summary>
    [Fact]
    public async Task ExpiredRefreshWindow_ServesCachedObservationWhileSuccessfulRevalidationRuns()
    {
        var timeProvider = new ManualTimeProvider(
            new DateTimeOffset(2026, 8, 30, 3, 18, 0, TimeSpan.Zero));
        var factoryCalls = 0;
        var provider = new FakeWeatherProvider(_ =>
        {
            var call = Interlocked.Increment(ref factoryCalls);
            return new WorldClockWeatherObservation(
                call == 1 ? 20d : 25d,
                call == 1 ? "rain" : "clear",
                timeProvider.GetUtcNow());
        });
        using var weather = new WorldClockWeatherService(provider, timeProvider);
        WorldClockWeatherLocation[] locations = [new("london", 51.5074, -0.1278)];

        var initial = await weather.LoadCurrentAsync(locations, CancellationToken.None);
        timeProvider.Advance(WorldClockWeatherService.CacheDuration + TimeSpan.FromSeconds(1));
        var whileRevalidating = await weather.LoadCurrentAsync(locations, CancellationToken.None);
        var refreshed = await weather.LoadCurrentAsync(locations, CancellationToken.None);

        Assert.Equal(20d, initial.Observations["london"].TemperatureCelsius);
        Assert.Equal(20d, whileRevalidating.Observations["london"].TemperatureCelsius);
        Assert.Equal(25d, refreshed.Observations["london"].TemperatureCelsius);
        Assert.Equal(2, provider.CallCount);
        Assert.Equal("available", whileRevalidating.Status.State);
    }

    /// <summary>Verifies that transient refresh failures retain the last observation only within its maximum age.</summary>
    [Fact]
    public async Task TransientRefreshFailure_PreservesLastObservationOnlyUntilItsMaximumAge()
    {
        var timeProvider = new ManualTimeProvider(
            new DateTimeOffset(2026, 8, 30, 3, 18, 0, TimeSpan.Zero));
        var factoryCalls = 0;
        var provider = new FakeWeatherProvider(_ =>
        {
            if (Interlocked.Increment(ref factoryCalls) == 1)
            {
                return new WorldClockWeatherObservation(23d, "cloudy", timeProvider.GetUtcNow());
            }

            throw new HttpRequestException("Synthetic transient failure.");
        });
        using var weather = new WorldClockWeatherService(provider, timeProvider);
        WorldClockWeatherLocation[] locations = [new("london", 51.5074, -0.1278)];

        _ = await weather.LoadCurrentAsync(locations, CancellationToken.None);
        timeProvider.Advance(WorldClockWeatherService.CacheDuration + TimeSpan.FromMinutes(1));
        var firstFallback = await weather.LoadCurrentAsync(locations, CancellationToken.None);
        timeProvider.Advance(
            WorldClockWeatherService.MaximumObservationAge
            - WorldClockWeatherService.CacheDuration
            - TimeSpan.FromMinutes(1)
            - TimeSpan.FromSeconds(1));
        var lastFallback = await weather.LoadCurrentAsync(locations, CancellationToken.None);
        timeProvider.Advance(TimeSpan.FromSeconds(2));
        var expired = await weather.LoadCurrentAsync(locations, CancellationToken.None);

        Assert.Equal(23d, firstFallback.Observations["london"].TemperatureCelsius);
        Assert.Equal(23d, lastFallback.Observations["london"].TemperatureCelsius);
        Assert.Equal("available", firstFallback.Status.State);
        Assert.Empty(expired.Observations);
        Assert.Equal("unavailable", expired.Status.State);
        Assert.Equal("request-failed", expired.Status.ReasonCode);
        Assert.Equal(4, provider.CallCount);
    }

    [Fact]
    public void ReplacedObservation_IsNotDeletedByThePreviousExpirationTimer()
    {
        var timeProvider = new ManualTimeProvider(
            new DateTimeOffset(2026, 8, 30, 3, 18, 0, TimeSpan.Zero));
        using var cache = new WorldClockWeatherCache(
            timeProvider,
            WorldClockWeatherService.CacheDuration);
        cache.Set(
            "london",
            new WorldClockWeatherObservation(20d, "rain", timeProvider.GetUtcNow()),
            timeProvider.GetUtcNow());
        timeProvider.Advance(TimeSpan.FromMinutes(1));
        cache.Set(
            "london",
            new WorldClockWeatherObservation(24d, "clear", timeProvider.GetUtcNow()),
            timeProvider.GetUtcNow());

        timeProvider.Advance(TimeSpan.FromMinutes(11));

        Assert.Equal(1, cache.Count);
        Assert.True(cache.TryGet("london", out var replacement));
        Assert.Equal(24d, replacement.Observation.TemperatureCelsius);

        timeProvider.Advance(TimeSpan.FromMinutes(1));

        Assert.Equal(0, cache.Count);
    }

    [Fact]
    public async Task ConcurrentFailure_IsSingleFlight_AndALaterLoadCanRetry()
    {
        var timeProvider = new ManualTimeProvider(
            new DateTimeOffset(2026, 8, 30, 3, 18, 0, TimeSpan.Zero));
        var provider = new CoordinatedWeatherProvider(timeProvider, fail: true);
        using var weather = new WorldClockWeatherService(provider, timeProvider);
        WorldClockWeatherLocation[] locations =
        [
            new("london", 51.5074, -0.1278)
        ];

        var first = weather.LoadCurrentAsync(locations, CancellationToken.None);
        await provider.Started;
        var second = weather.LoadCurrentAsync(locations, CancellationToken.None);
        provider.Release();

        var concurrent = await Task.WhenAll(first, second);

        Assert.Equal(1, provider.CallCount);
        Assert.All(concurrent, static result =>
        {
            Assert.Equal("unavailable", result.Status.State);
            Assert.Equal("request-failed", result.Status.ReasonCode);
        });

        var retry = await weather.LoadCurrentAsync(locations, CancellationToken.None);

        Assert.Equal(2, provider.CallCount);
        Assert.Equal("unavailable", retry.Status.State);
    }

    [Fact]
    public async Task CancellingOneWaiter_DoesNotCancelTheSharedSuccessfulRequest()
    {
        var timeProvider = new ManualTimeProvider(
            new DateTimeOffset(2026, 8, 30, 3, 18, 0, TimeSpan.Zero));
        var provider = new CoordinatedWeatherProvider(timeProvider, fail: false);
        using var weather = new WorldClockWeatherService(provider, timeProvider);
        WorldClockWeatherLocation[] locations =
        [
            new("london", 51.5074, -0.1278)
        ];
        using var cancelledWaiter = new CancellationTokenSource();

        var first = weather.LoadCurrentAsync(locations, cancelledWaiter.Token);
        await provider.Started;
        var second = weather.LoadCurrentAsync(locations, CancellationToken.None);
        cancelledWaiter.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);

        provider.Release();
        var completed = await second;
        var cached = await weather.LoadCurrentAsync(locations, CancellationToken.None);

        Assert.Equal(1, provider.CallCount);
        Assert.Equal("available", completed.Status.State);
        Assert.Equal("available", cached.Status.State);
        Assert.Equal(1, weather.CachedObservationCount);
    }

    [Fact]
    public async Task Snapshot_RevalidatesEveryObservationAgainstItsSingleFinalInstant()
    {
        var catalogPath = RepositoryFile("WorkTrail", "Assets", "WorldClocks", "world-clocks.sqlite3");
        var timeProvider = new ManualTimeProvider(
            new DateTimeOffset(2026, 8, 30, 12, 0, 0, TimeSpan.Zero));
        var provider = new AdvancingWeatherProvider(timeProvider);
        var service = new WorldClockService(catalogPath, provider, timeProvider);

        var snapshot = await service.BuildCurrentSnapshotAsync(
            ["london", "paris"],
            true,
            CancellationToken.None);

        Assert.Equal(
            new DateTimeOffset(2026, 8, 30, 12, 0, 2, TimeSpan.Zero),
            snapshot.InstantUtc);
        var london = snapshot.Clocks.Single(static clock => clock.CityId == "london");
        var paris = snapshot.Clocks.Single(static clock => clock.CityId == "paris");
        Assert.Null(london.Weather);
        Assert.DoesNotContain(
            london.Atmosphere.ForegroundAssetPaths,
            static path => path.Contains("rain", StringComparison.Ordinal));
        Assert.NotNull(paris.Weather);
        Assert.True(paris.Weather.IsFresh);
        Assert.Equal("partial", snapshot.WeatherStatus.State);
        Assert.Equal("stale-observation", snapshot.WeatherStatus.ReasonCode);
        Assert.Equal(1, snapshot.WeatherStatus.AvailableObservations);
    }

    [Fact]
    public async Task ObservationBeyondTheFiveMinuteFutureSkew_IsRejected()
    {
        var catalogPath = RepositoryFile("WorkTrail", "Assets", "WorldClocks", "world-clocks.sqlite3");
        var timeProvider = new ManualTimeProvider(
            new DateTimeOffset(2026, 8, 30, 12, 0, 0, TimeSpan.Zero));
        var provider = new FakeWeatherProvider(_ => new WorldClockWeatherObservation(
            25d,
            "clear",
            timeProvider.GetUtcNow() + TimeSpan.FromMinutes(5) + TimeSpan.FromSeconds(1)));
        var service = new WorldClockService(catalogPath, provider, timeProvider);

        var snapshot = await service.BuildCurrentSnapshotAsync(
            ["london"],
            true,
            CancellationToken.None);

        Assert.Null(snapshot.Clocks[0].Weather);
        Assert.Equal("unavailable", snapshot.WeatherStatus.State);
        Assert.Equal("stale-observation", snapshot.WeatherStatus.ReasonCode);
        Assert.Equal(0, snapshot.WeatherStatus.AvailableObservations);
    }

    [Fact]
    public void ReferenceInstant_DoesNotRequestOrAttachCurrentWeather()
    {
        var catalogPath = RepositoryFile("WorkTrail", "Assets", "WorldClocks", "world-clocks.sqlite3");
        var timeProvider = new ManualTimeProvider(
            new DateTimeOffset(2026, 8, 30, 3, 18, 0, TimeSpan.Zero));
        var provider = new FakeWeatherProvider(_ => new WorldClockWeatherObservation(
            25d,
            "rain",
            timeProvider.GetUtcNow()));
        var service = new WorldClockService(catalogPath, provider, timeProvider);

        var snapshot = service.BuildSnapshotForLocalTime(
            ["ho-chi-minh-city", "paris"],
            new WorldClockConversionRequest(
                "ho-chi-minh-city",
                new DateTime(2025, 8, 30, 10, 18, 0, DateTimeKind.Unspecified)));

        Assert.Equal(0, provider.CallCount);
        Assert.Equal("not-requested", snapshot.WeatherStatus.State);
        Assert.Equal("explicit-instant", snapshot.WeatherStatus.ReasonCode);
        Assert.All(snapshot.Clocks, static clock => Assert.Null(clock.Weather));
    }

    /// <summary>Verifies that an observation at the maximum age is rejected without hiding the local clock.</summary>
    [Fact]
    public async Task ObservationAtMaximumAge_IsRejectedWithoutSuppressingTheLocalClock()
    {
        var catalogPath = RepositoryFile("WorkTrail", "Assets", "WorldClocks", "world-clocks.sqlite3");
        var timeProvider = new ManualTimeProvider(
            new DateTimeOffset(2026, 8, 30, 3, 18, 0, TimeSpan.Zero));
        var provider = new FakeWeatherProvider(_ => new WorldClockWeatherObservation(
            25d,
            "rain",
            timeProvider.GetUtcNow() - WorldClockWeatherService.MaximumObservationAge));
        var service = new WorldClockService(catalogPath, provider, timeProvider);

        var snapshot = await service.BuildCurrentSnapshotAsync(
            ["london"],
            true,
            CancellationToken.None);

        Assert.Single(snapshot.Clocks);
        Assert.Null(snapshot.Clocks[0].Weather);
        Assert.Equal("unavailable", snapshot.WeatherStatus.State);
        Assert.Equal("stale-observation", snapshot.WeatherStatus.ReasonCode);
    }

    [Fact]
    public async Task DisabledWeather_DoesNotContactTheConfiguredProvider()
    {
        var catalogPath = RepositoryFile("WorkTrail", "Assets", "WorldClocks", "world-clocks.sqlite3");
        var timeProvider = new ManualTimeProvider(
            new DateTimeOffset(2026, 8, 30, 3, 18, 0, TimeSpan.Zero));
        var provider = new FakeWeatherProvider(_ => throw new InvalidOperationException("Weather must not be requested when disabled."));
        var service = new WorldClockService(catalogPath, provider, timeProvider);

        var snapshot = await service.BuildCurrentSnapshotAsync(
            ["london", "paris"],
            false,
            CancellationToken.None);

        Assert.Equal(0, provider.CallCount);
        Assert.Equal("disabled", snapshot.WeatherStatus.State);
        Assert.Equal("user-disabled", snapshot.WeatherStatus.ReasonCode);
        Assert.Equal(2, snapshot.WeatherStatus.RequestedCities);
        Assert.Equal(0, snapshot.WeatherStatus.AvailableObservations);
        Assert.All(snapshot.Clocks, static clock => Assert.Null(clock.Weather));
    }

    [Fact]
    public async Task MissingKeyAndProviderFailure_RemainExplicitNonFatalDiagnostics()
    {
        var catalogPath = RepositoryFile("WorkTrail", "Assets", "WorldClocks", "world-clocks.sqlite3");
        var timeProvider = new ManualTimeProvider(
            new DateTimeOffset(2026, 8, 30, 3, 18, 0, TimeSpan.Zero));
        var handler = new RecordingHttpMessageHandler(_ =>
            throw new InvalidOperationException("HTTP must not run without a key."));
        using var httpClient = new HttpClient(handler);
        var missingKeyService = new WorldClockService(
            catalogPath,
            OpenWeatherCurrentProvider.CreateForTests(httpClient, apiKey: null),
            timeProvider);

        var missingKey = await missingKeyService.BuildCurrentSnapshotAsync(
            ["london"],
            true,
            CancellationToken.None);

        Assert.Single(missingKey.Clocks);
        Assert.Equal(0, handler.CallCount);
        Assert.Equal("configuration-required", missingKey.WeatherStatus.State);
        Assert.Equal("missing-api-key", missingKey.WeatherStatus.ReasonCode);

        var invalidKeyService = new WorldClockService(
            catalogPath,
            OpenWeatherCurrentProvider.CreateForTests(httpClient, "short"),
            timeProvider);
        var invalidKey = await invalidKeyService.BuildCurrentSnapshotAsync(
            ["london"],
            true,
            CancellationToken.None);

        Assert.Equal(0, handler.CallCount);
        Assert.Equal("configuration-required", invalidKey.WeatherStatus.State);
        Assert.Equal("invalid-api-key", invalidKey.WeatherStatus.ReasonCode);

        var failingProvider = new FakeWeatherProvider(_ =>
            throw new HttpRequestException("Synthetic provider failure."));
        var failingService = new WorldClockService(catalogPath, failingProvider, timeProvider);
        var failed = await failingService.BuildCurrentSnapshotAsync(
            ["london"],
            true,
            CancellationToken.None);

        Assert.Single(failed.Clocks);
        Assert.Null(failed.Clocks[0].Weather);
        Assert.Equal("unavailable", failed.WeatherStatus.State);
        Assert.Equal("request-failed", failed.WeatherStatus.ReasonCode);
    }

    [Fact]
    public void CatalogAssets_HaveExactProvenancedChecksummedSet()
    {
        var worldClockRoot = RepositoryFile("WorkTrail", "Assets", "WorldClocks");
        var catalogPath = Path.Combine(worldClockRoot, "world-clocks.sqlite3");
        using var connection = new SqliteConnection($"Data Source={catalogPath};Mode=ReadOnly");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT city_id, season, relative_path, title, author, license_name, sha256
            FROM skyline_asset
            ORDER BY city_id, season;
            """;

        var rows = new List<(string CityId, string Season, string RelativePath, string Title, string Author, string License, string Hash)>();
        using (var reader = command.ExecuteReader())
        {
            while (reader.Read())
            {
                rows.Add((
                    reader.GetString(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.GetString(3),
                    reader.GetString(4),
                    reader.GetString(5),
                    reader.GetString(6)));
            }
        }

        Assert.NotEmpty(rows);
        foreach (var group in rows.GroupBy(static row => row.CityId, StringComparer.Ordinal))
        {
            Assert.Equal(2, group.Count());
            Assert.Equal(new[] { "summer", "winter" }, group.Select(static row => row.Season).OrderBy(static season => season).ToArray());
            Assert.Equal(2, group.Select(static row => row.Title).Distinct(StringComparer.Ordinal).Count());
        }

        var expectedPaths = rows.Select(static row => row.RelativePath).OrderBy(static path => path, StringComparer.Ordinal).ToArray();
        Assert.All(rows, static row => Assert.EndsWith(".png", row.RelativePath, StringComparison.Ordinal));
        var actualPaths = Directory.GetFiles(Path.Combine(worldClockRoot, "Skylines"), "*.png", SearchOption.TopDirectoryOnly)
            .Select(path => Path.GetRelativePath(worldClockRoot, path).Replace('\\', '/'))
            .OrderBy(static path => path, StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(expectedPaths, actualPaths);

        foreach (var row in rows)
        {
            Assert.False(string.IsNullOrWhiteSpace(row.Author));
            Assert.False(string.IsNullOrWhiteSpace(row.License));
            Assert.Matches("^[0-9a-f]{64}$", row.Hash);
            var assetPath = Path.Combine(worldClockRoot, row.RelativePath.Replace('/', Path.DirectorySeparatorChar));
            using var stream = File.OpenRead(assetPath);
            var actualHash = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
            Assert.Equal(row.Hash, actualHash);
        }

        using var attribution = JsonDocument.Parse(File.ReadAllText(Path.Combine(worldClockRoot, "ATTRIBUTION.json")));
        var attributionAssets = attribution.RootElement.GetProperty("assets").EnumerateArray().ToArray();
        Assert.Equal(rows.Count, attributionAssets.Length);
        var attributionKeys = attributionAssets
            .Select(static asset => asset.GetProperty("cityId").GetString() + "/" + asset.GetProperty("season").GetString())
            .OrderBy(static key => key, StringComparer.Ordinal)
            .ToArray();
        var databaseKeys = rows
            .Select(static row => $"{row.CityId}/{row.Season}")
            .OrderBy(static key => key, StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(databaseKeys, attributionKeys);

        var packagedManifestReference = attribution.RootElement.GetProperty("packagedManifest");
        var packagedManifestName = packagedManifestReference.GetProperty("file").GetString();
        Assert.Equal("PACKAGED-ASSET-MANIFEST.json", packagedManifestName);
        var packagedManifestPath = Path.Combine(worldClockRoot, packagedManifestName!);
        using var packagedManifestStream = File.OpenRead(packagedManifestPath);
        var packagedManifestHash = Convert.ToHexString(SHA256.HashData(packagedManifestStream)).ToLowerInvariant();
        Assert.Equal(packagedManifestReference.GetProperty("sha256").GetString(), packagedManifestHash);
        using var packagedManifest = JsonDocument.Parse(File.ReadAllText(packagedManifestPath));
        Assert.True(packagedManifest.RootElement.GetProperty("complete").GetBoolean());
        Assert.Equal(rows.Count, packagedManifest.RootElement.GetProperty("assets").GetArrayLength());
    }

    [Fact]
    public void AtmosphereOverlays_HaveExactChecksummedSet()
    {
        var overlayRoot = RepositoryFile("WorkTrail", "Assets", "WorldClocks", "Overlays");
        var expected = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Backdrops/aurora.png"] = "9da5a92be65aefc6bb5c3b3b7bbb653df29c1345b39bdc8c9ed567107eee15c7",
            ["Backdrops/clouds-dawn.png"] = "c16ba677b67dcbfea6e5667d5d73e6dbaa575352565b27987c4fb33de366f80b",
            ["Backdrops/clouds-day.png"] = "ffa6f227a33966116dcfc83bc7c838acfee8cc071ac9cd474f47cd347ddbec6a",
            ["Backdrops/clouds-night.png"] = "2553a3a22b66b50d88f3eaa0850138efd2fd1e88922db3e9f6c38f2ada38fc69",
            ["Backdrops/clouds-sunset.png"] = "7257b2ca05b26667cee3c20e442f81b2b01e27c0dc14eab0556dd4b6881ca782",
            ["Backdrops/golden-hour.png"] = "98a2eb0852c4f972840156be0286551139f0fcc166bc0749ffa7b98cf795ba2a",
            ["Backdrops/lightning.png"] = "74b3d0698db64744bdf4f7908d6bc9e6cf36249e1c2262b127b58f6148daeafd",
            ["Backdrops/stars.png"] = "c947bdd98c86961e3d43dbf127d35526e9b88fba432d95515989144a778344eb",
            ["Foregrounds/fog.png"] = "a8214e31d0f3bb4d470cd35cd48749c123e79c401f1ac3addefc3c08030ec60a",
            ["Foregrounds/rain.png"] = "4e9788d72203c14418d75005d78cabc51922f5fa9882ac36eccfa067d6c00c82",
            ["Foregrounds/snow.png"] = "0dd05952a2c93f8eab05296893fb5cb5132ae27508e6b2c62a1925f677021648"
        };
        var actualPaths = Directory.GetFiles(overlayRoot, "*.png", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(overlayRoot, path).Replace('\\', '/'))
            .OrderBy(static path => path, StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(expected.Keys.OrderBy(static path => path, StringComparer.Ordinal).ToArray(), actualPaths);

        foreach (var (relativePath, expectedHash) in expected)
        {
            using var stream = File.OpenRead(Path.Combine(overlayRoot, relativePath.Replace('/', Path.DirectorySeparatorChar)));
            var actualHash = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
            Assert.Equal(expectedHash, actualHash);
        }
    }

    private sealed class FakeWeatherProvider(
        Func<WorldClockWeatherLocation, WorldClockWeatherObservation> observationFactory)
        : IWorldClockWeatherProvider
    {
        private int _callCount;

        public string Name => "fake-weather";

        public string ConfigurationState => "configured";

        public bool IsConfigured => true;

        public int CallCount => Volatile.Read(ref _callCount);

        /// <inheritdoc />
        public Task<WorldClockWeatherApiKeyValidation> ValidateApiKeyAsync(
            string secret,
            CancellationToken cancellationToken) =>
            Task.FromResult(WorldClockWeatherApiKeyValidation.Accepted);

        public Task<WorldClockWeatherObservation> GetCurrentAsync(
            WorldClockWeatherLocation location,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Interlocked.Increment(ref _callCount);
            return Task.FromResult(observationFactory(location));
        }
    }

    private sealed class AdvancingWeatherProvider(ManualTimeProvider timeProvider)
        : IWorldClockWeatherProvider
    {
        public string Name => "advancing-test-provider";

        public string ConfigurationState => "configured";

        public bool IsConfigured => true;

        /// <inheritdoc />
        public Task<WorldClockWeatherApiKeyValidation> ValidateApiKeyAsync(
            string secret,
            CancellationToken cancellationToken) =>
            Task.FromResult(WorldClockWeatherApiKeyValidation.Accepted);

        public Task<WorldClockWeatherObservation> GetCurrentAsync(
            WorldClockWeatherLocation location,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (location.CityId == "london")
            {
                return Task.FromResult(new WorldClockWeatherObservation(
                    22d,
                    "rain",
                    timeProvider.GetUtcNow() - TimeSpan.FromMinutes(44) - TimeSpan.FromSeconds(59)));
            }

            if (location.CityId == "paris")
            {
                timeProvider.Advance(TimeSpan.FromSeconds(2));
                return Task.FromResult(new WorldClockWeatherObservation(
                    24d,
                    "cloudy",
                    timeProvider.GetUtcNow()));
            }

            throw new InvalidOperationException($"Unexpected test city '{location.CityId}'.");
        }
    }

    private sealed class CoordinatedWeatherProvider(
        ManualTimeProvider timeProvider,
        bool fail) : IWorldClockWeatherProvider
    {
        private readonly TaskCompletionSource<bool> _started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<bool> _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _callCount;

        public string Name => "coordinated-test-provider";

        public string ConfigurationState => "configured";

        public bool IsConfigured => true;

        public int CallCount => Volatile.Read(ref _callCount);

        /// <inheritdoc />
        public Task<WorldClockWeatherApiKeyValidation> ValidateApiKeyAsync(
            string secret,
            CancellationToken cancellationToken) =>
            Task.FromResult(WorldClockWeatherApiKeyValidation.Accepted);

        internal Task Started => _started.Task;

        internal void Release() => _release.TrySetResult(true);

        public async Task<WorldClockWeatherObservation> GetCurrentAsync(
            WorldClockWeatherLocation location,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _callCount);
            _started.TrySetResult(true);
            await _release.Task.WaitAsync(cancellationToken);
            if (fail)
            {
                throw new HttpRequestException("Synthetic coordinated failure.");
            }

            return new WorldClockWeatherObservation(
                24d,
                "clear",
                timeProvider.GetUtcNow());
        }
    }

    private sealed class ManualTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        private readonly object _gate = new();
        private readonly List<ManualTimer> _timers = [];
        private DateTimeOffset _utcNow = utcNow.ToUniversalTime();

        public override DateTimeOffset GetUtcNow()
        {
            lock (_gate)
            {
                return _utcNow;
            }
        }

        public override ITimer CreateTimer(
            TimerCallback callback,
            object? state,
            TimeSpan dueTime,
            TimeSpan period)
        {
            ArgumentNullException.ThrowIfNull(callback);
            var timer = new ManualTimer(this, callback, state, dueTime, period);
            lock (_gate)
            {
                _timers.Add(timer);
            }

            return timer;
        }

        internal void Advance(TimeSpan duration)
        {
            if (duration < TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(duration));
            }

            ManualTimer[] timers;
            DateTimeOffset now;
            lock (_gate)
            {
                _utcNow += duration;
                now = _utcNow;
                timers = _timers.ToArray();
            }

            foreach (var timer in timers)
            {
                timer.FireIfDue(now);
            }
        }

        private void Unregister(ManualTimer timer)
        {
            lock (_gate)
            {
                _timers.Remove(timer);
            }
        }

        private sealed class ManualTimer : ITimer
        {
            private readonly ManualTimeProvider _owner;
            private readonly TimerCallback _callback;
            private readonly object? _state;
            private readonly object _gate = new();
            private DateTimeOffset? _dueAtUtc;
            private TimeSpan _period;
            private bool _disposed;

            internal ManualTimer(
                ManualTimeProvider owner,
                TimerCallback callback,
                object? state,
                TimeSpan dueTime,
                TimeSpan period)
            {
                _owner = owner;
                _callback = callback;
                _state = state;
                _ = Change(dueTime, period);
            }

            public bool Change(TimeSpan dueTime, TimeSpan period)
            {
                ValidateTimeout(dueTime, nameof(dueTime));
                ValidateTimeout(period, nameof(period));
                lock (_gate)
                {
                    if (_disposed)
                    {
                        return false;
                    }

                    _dueAtUtc = dueTime == Timeout.InfiniteTimeSpan
                        ? null
                        : _owner.GetUtcNow() + dueTime;
                    _period = period;
                    return true;
                }
            }

            public void Dispose()
            {
                lock (_gate)
                {
                    if (_disposed)
                    {
                        return;
                    }

                    _disposed = true;
                    _dueAtUtc = null;
                }

                _owner.Unregister(this);
            }

            public ValueTask DisposeAsync()
            {
                Dispose();
                return ValueTask.CompletedTask;
            }

            internal void FireIfDue(DateTimeOffset nowUtc)
            {
                lock (_gate)
                {
                    if (_disposed || _dueAtUtc is null || _dueAtUtc > nowUtc)
                    {
                        return;
                    }

                    _dueAtUtc = _period == Timeout.InfiniteTimeSpan
                        ? null
                        : nowUtc + _period;
                }

                _callback(_state);
            }

            private static void ValidateTimeout(TimeSpan value, string parameterName)
            {
                if (value < TimeSpan.Zero && value != Timeout.InfiniteTimeSpan)
                {
                    throw new ArgumentOutOfRangeException(parameterName);
                }
            }
        }
    }

    private sealed class RecordingHttpMessageHandler(
        Func<HttpRequestMessage, HttpResponseMessage> responseFactory) : HttpMessageHandler
    {
        private int _callCount;

        internal int CallCount => Volatile.Read(ref _callCount);

        internal Uri? RequestUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Interlocked.Increment(ref _callCount);
            RequestUri = request.RequestUri;
            return Task.FromResult(responseFactory(request));
        }
    }

    private sealed class BlockingReadStream : Stream
    {
        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush() => throw new NotSupportedException();

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return 0;
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private static string RepositoryFile(params string[] pathSegments)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "WorkTrail.slnx")))
            {
                return Path.Combine(new[] { directory.FullName }.Concat(pathSegments).ToArray());
            }
        }

        throw new DirectoryNotFoundException("Could not locate the WorkTrail repository root.");
    }
}
