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
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using WorkTrail.Application;
using WorkTrail.Services;
using Xunit;

namespace WorkTrail.Core.Tests;

public sealed class WorldClockSelectionMutationTests
{
    [Fact]
    public async Task AddAndRemove_PersistWithoutRequestingWeather_AndProjectionChoosesTheMode()
    {
        var dataDirectory = CreateTemporaryDirectory();
        try
        {
            var store = new LocalStore(dataDirectory);
            Assert.True(store.LoadSettings().WorldClockWeatherEnabled);
            store.SaveSettings(store.LoadSettings() with
            {
                WorldClockCityIds = ["ho-chi-minh-city"]
            });
            var provider = new FailingWeatherProvider();
            var worldClocks = new WorldClockService(
                RepositoryFile("WorkTrail", "Assets", "WorldClocks", "world-clocks.sqlite3"),
                provider,
                TimeProvider.System);
            string? writtenKeyName = null;
            string? writtenSecret = null;
            var utilities = new UtilityService((keyName, secret) =>
            {
                writtenKeyName = keyName;
                writtenSecret = secret;
            });
            var capture = new ScreenCaptureService(utilities.GetAppVersion());
            await using var application = new WorkTrailApplication(
                store,
                utilities,
                new TrackingDomainService(store),
                capture,
                new FakeHardwareTelemetryService(),
                new OpenAiAnalysisService(store, capture),
                new StartupService(),
                new BuildInformationService(),
                worldClockService: worldClocks,
                startScheduledSnapshotTimer: false);

            var invalidWeatherKey = await application.SetWorldClockWeatherKeyAsync("short", CancellationToken.None);

            Assert.False(invalidWeatherKey.Succeeded);
            Assert.Equal("world_clocks.weather.key.invalid", invalidWeatherKey.Code);
            Assert.Contains(invalidWeatherKey.Issues, issue => issue.Field == "secret");
            Assert.Null(writtenKeyName);
            Assert.Null(writtenSecret);

            const string weatherKey = "0123456789abcdef0123456789abcdef";
            var storedWeatherKey = await application.SetWorldClockWeatherKeyAsync(weatherKey, CancellationToken.None);

            Assert.True(storedWeatherKey.Succeeded);
            Assert.Equal("WORKTRAIL_OPENWEATHER_API_KEY", storedWeatherKey.Value);
            Assert.Equal("WORKTRAIL_OPENWEATHER_API_KEY", writtenKeyName);
            Assert.Equal(weatherKey, writtenSecret);

            var added = await application.AddWorldClockAsync("tokyo", CancellationToken.None);

            Assert.True(added.Succeeded);
            Assert.Equal(["ho-chi-minh-city", "tokyo"], added.Value?.CityIds);
            Assert.Equal(0, provider.CallCount);
            Assert.Equal(
                ["ho-chi-minh-city", "tokyo"],
                store.LoadSettings().WorldClockCityIds);

            var moved = await application.MoveWorldClockAsync(
                "tokyo",
                WorldClockMoveDirection.Up,
                CancellationToken.None);

            Assert.True(moved.Succeeded);
            Assert.Equal(["tokyo", "ho-chi-minh-city"], moved.Value?.CityIds);
            Assert.Equal(0, provider.CallCount);
            Assert.Equal(["tokyo", "ho-chi-minh-city"], store.LoadSettings().WorldClockCityIds);

            var converted = await application.ConvertWorldClocksAsync(
                new WorldClockConversionRequest(
                    "ho-chi-minh-city",
                    new DateTime(2026, 8, 30, 10, 18, 0, DateTimeKind.Unspecified)),
                CancellationToken.None);

            Assert.True(converted.Succeeded);
            Assert.Equal("not-requested", converted.Value?.WeatherStatus.State);
            Assert.All(converted.Value!.Clocks, static clock => Assert.Null(clock.Weather));
            Assert.Equal(0, provider.CallCount);

            var current = await application.GetWorldClocksAsync(CancellationToken.None);

            Assert.True(current.Succeeded);
            Assert.Equal("unavailable", current.Value?.WeatherStatus.State);
            Assert.Equal(2, provider.CallCount);

            var weatherDisabled = await application.PatchSettingsAsync(
                new SettingsPatch(new Dictionary<string, string?>
                {
                    ["world_clocks.weather.enabled"] = "false"
                }),
                CancellationToken.None);
            var disabledSnapshot = await application.GetWorldClocksAsync(CancellationToken.None);

            Assert.True(weatherDisabled.Succeeded);
            Assert.False(weatherDisabled.Value?.WorldClockWeatherEnabled);
            Assert.False(store.LoadSettings().WorldClockWeatherEnabled);
            Assert.True(disabledSnapshot.Succeeded);
            Assert.Equal("disabled", disabledSnapshot.Value?.WeatherStatus.State);
            Assert.Equal("user-disabled", disabledSnapshot.Value?.WeatherStatus.ReasonCode);
            Assert.Equal(2, provider.CallCount);

            var removed = await application.RemoveWorldClockAsync("tokyo", CancellationToken.None);

            Assert.True(removed.Succeeded);
            Assert.Equal(["ho-chi-minh-city"], removed.Value?.CityIds);
            Assert.Equal(2, provider.CallCount);
            Assert.Equal(["ho-chi-minh-city"], store.LoadSettings().WorldClockCityIds);
        }
        finally
        {
            await DeleteTemporaryDirectoryAsync(dataDirectory);
        }
    }

    private sealed class FailingWeatherProvider : IWorldClockWeatherProvider
    {
        private int _callCount;

        public string Name => "failing-test-provider";

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
            throw new HttpRequestException("Synthetic optional-weather failure.");
        }
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

    private static string CreateTemporaryDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "WorkTrail.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static async Task DeleteTemporaryDirectoryAsync(string path)
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                Directory.Delete(path, recursive: true);
                return;
            }
            catch (IOException) when (attempt < 4)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(50));
            }
        }
    }
}
