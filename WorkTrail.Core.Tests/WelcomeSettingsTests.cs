// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using WorkTrail.Application;
using WorkTrail.Services;
using Xunit;

namespace WorkTrail.Core.Tests;

/// <summary>Protects offline first-run setup and atomic persistence of the confirmed home city.</summary>
public sealed class WelcomeSettingsTests
{
    /// <summary>A fresh install assumes no location and persists the completion flag only after a valid choice.</summary>
    [Fact]
    public async Task Welcome_RequiresAValidCityAndPersistsOneAtomicSelection()
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "welcome-data", Guid.NewGuid().ToString("N"));
        try
        {
            var store = new LocalStore(directory);
            var clocks = new WorldClockService(CatalogPath());
            await using (var application = new WorkTrailApplication(store, worldClockService: clocks))
            {
                var initial = (await application.GetSettingsAsync(CancellationToken.None)).Value!;
                Assert.False(initial.WelcomeCompleted);
                Assert.Empty(initial.WorldClockCityIds!);
                var invalid = await application.PatchSettingsAsync(Home("missing-city"), CancellationToken.None);
                Assert.False(invalid.Succeeded);
                Assert.False(store.LoadSettings().WelcomeCompleted);
                var cancelled = new CancellationToken(canceled: true);
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => application.PatchSettingsAsync(Home("rome"), cancelled));
                Assert.False(store.LoadSettings().WelcomeCompleted);
                var saved = await application.PatchSettingsAsync(Home("rome"), CancellationToken.None);
                Assert.True(saved.Succeeded);
                Assert.Equal("rome", saved.Value!.HomeCityId);
                Assert.Equal("rome", saved.Value.AstronomyAgendaCityId);
                Assert.Equal(new[] { "rome" }, saved.Value.WorldClockCityIds);
                Assert.True(saved.Value.WelcomeCompleted);
            }
            var reopened = new LocalStore(directory).LoadSettings();
            Assert.True(reopened.WelcomeCompleted);
            Assert.Equal("rome", reopened.HomeCityId);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true); }
    }

    /// <summary>Choosing an already selected city preserves the other clocks and all saved window bounds.</summary>
    [Fact]
    public async Task Welcome_PreservesExistingCitiesAndWindowPlacement()
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "welcome-data", Guid.NewGuid().ToString("N"));
        try
        {
            var store = new LocalStore(directory);
            var placement = new WindowState(20, 40, 600, 420, "display");
            store.SaveSettings(store.LoadSettings() with
            {
                WorldClockCityIds = ["tokyo", "rome", "london"],
                WindowStates = new Dictionary<string, WindowState> { [WindowStateKeys.WorldClocks] = placement }
            });
            await using var application = new WorkTrailApplication(store, worldClockService: new WorldClockService(CatalogPath()));
            Assert.True((await application.PatchSettingsAsync(Home("rome"), CancellationToken.None)).Succeeded);
            Assert.Equal(new[] { "rome", "tokyo", "london" }, store.LoadSettings().WorldClockCityIds);
            Assert.Equal(placement, store.LoadSettings().WindowStates![WindowStateKeys.WorldClocks]);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true); }
    }

    /// <summary>Suggestions use the actual offline catalog and return no location when Windows has no matching zone.</summary>
    [Fact]
    public void Suggestion_UsesWindowsTimeZoneWithoutAssumingAnExactLocation()
    {
        using var clocks = new WorldClockService(CatalogPath());
        var catalog = clocks.GetCatalog();
        var zone = TimeZoneInfo.FindSystemTimeZoneById("W. Europe Standard Time");
        var id = HomeCitySuggestion.Resolve(catalog.Cities, zone);
        Assert.NotNull(id);
        var city = Assert.Single(catalog.Cities, city => city.Id == id);
        Assert.Equal(zone.BaseUtcOffset, TimeZoneInfo.FindSystemTimeZoneById(city.TimeZoneId).BaseUtcOffset);
        var unknown = TimeZoneInfo.CreateCustomTimeZone("NoCatalogZone", TimeSpan.FromHours(3), "Unknown", "Unknown");
        Assert.Null(HomeCitySuggestion.Resolve(catalog.Cities, unknown));
    }

    private static SettingsPatch Home(string id) => new(new Dictionary<string, string?> { ["home.city_id"] = id });
    private static string CatalogPath()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "WorkTrail.slnx"))) directory = directory.Parent;
        return Path.Combine(directory?.FullName ?? throw new DirectoryNotFoundException(), "WorkTrail/Assets/WorldClocks/world-clocks.sqlite3");
    }
}
