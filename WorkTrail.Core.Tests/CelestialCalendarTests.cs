// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Microsoft.Data.Sqlite;
using WorkTrail.Application;
using WorkTrail.Services;
using Xunit;

namespace WorkTrail.Core.Tests;

public sealed class CelestialCalendarTests
{
    [Fact]
    public void Catalog_ContainsReviewedOfflineCoverage()
    {
        var catalog = CelestialCalendarCatalog.Current;
        Assert.Equal(new DateOnly(2026, 1, 1), catalog.CoverageStart);
        Assert.Equal(new DateOnly(2028, 3, 31), catalog.CoverageEnd);
        Assert.Equal(17, catalog.Countries.Count);
        Assert.Equal(565, catalog.Holidays.Count);
        Assert.Equal(211, catalog.Saints.Count);
        Assert.All(catalog.Holidays, holiday => Assert.Matches("^[A-Za-z0-9._-]+\\.png$", holiday.ArtworkFileName));
        Assert.Contains(catalog.Holidays, holiday => holiday.ArtworkFileName == "new-year-v1.png");
        Assert.Contains(catalog.Saints, saint => saint.EventKey == "StFrancisAssisi"
            && saint.NameLatin.Contains("Francisci", StringComparison.Ordinal));
    }

    [Fact]
    public void ExistingActivityDatabase_StoresCalendarInNormalizedTables()
    {
        var directory = Path.Combine(Path.GetTempPath(), "WorkTrail.CalendarTests", Guid.NewGuid().ToString("N"));
        try
        {
            var store = new LocalStore(directory);
            store.EnsureCelestialCalendar(CelestialCalendarCatalog.Current);
            store.EnsureCelestialCalendar(CelestialCalendarCatalog.Current);
            Assert.Contains(store.LoadCalendarHolidays(new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 1)),
                holiday => holiday.Country == "IT" && holiday.Kind == "holiday");
            Assert.Equal(211, CelestialCalendarCatalog.Current.Saints.Count);

            using var connection = new SqliteConnection($"Data Source={store.ActivityDatabasePath};Pooling=False");
            connection.Open();
            Assert.Equal(12L, Scalar(connection, "PRAGMA user_version;"));
            Assert.Equal(17L, Scalar(connection, "SELECT COUNT(*) FROM calendar_countries;"));
            Assert.Equal(565L, Scalar(connection, "SELECT COUNT(*) FROM calendar_holidays;"));
            Assert.Equal(211L, Scalar(connection, "SELECT COUNT(*) FROM calendar_saints;"));
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Events_StartAtNineLocalAndDeduplicateSelectedHolidays()
    {
        var city = new WorldClockCitySummary("rome", "Rome", "IT", "Europe/Rome", 41.9028, 12.4964, true);
        var instant = new DateTimeOffset(2026, 9, 24, 0, 0, 0, TimeSpan.Zero);
        var snapshot = CelestialService.Build(new CelestialRequest(city.Id, instant),
            new WorldClockCityCatalog([city], 12), CancellationToken.None);
        var zone = TimeZoneInfo.FindSystemTimeZoneById(city.TimeZoneId);
        var today = new DateOnly(2026, 9, 24);
        var holidays = new[]
        {
            new CelestialCalendarHoliday(
                today, "IT", "Test holiday", "holiday", "rule_based", "holiday-it-v1.png", "https://example.com"),
            new CelestialCalendarHoliday(
                today, "FR", "Other country", "holiday", "rule_based", "holiday-fr-v1.png", "https://example.com"),
            new CelestialCalendarHoliday(
                today, "IT", "Make-up day", "workday", "rule_based", "holiday-it-v1.png", "https://example.com")
        };
        var saints = new[] { new CelestialCalendarSaint(9, 24, "OwnerOurLadyOfMercy", "Beatae Mariae Virginis de Mercede", "https://example.com") };
        var calendar = CelestialCalendarEventService.Build(snapshot, zone, holidays, saints, ["IT", "FR"], true, CancellationToken.None);
        Assert.Equal(2, calendar.Count);
        Assert.All(calendar,
            item => Assert.Equal(new TimeOnly(9, 0), TimeOnly.FromDateTime(item.StartLocal.DateTime)));
        Assert.Equal(CelestialEventKind.Holiday, calendar[0].Kind);
        Assert.Equal("holiday-fr-v1.png", calendar[0].CalendarArtworkFileName);
        Assert.Equal(CelestialEventKind.Saint, calendar[1].Kind);
        Assert.Empty(CelestialCalendarEventService.Build(snapshot, zone, holidays, saints, [], false, CancellationToken.None));
    }

    [Fact]
    public void Settings_RejectUnsupportedOrMalformedCountrySelections()
    {
        var initial = new AppSettings();
        var valid = SettingsCatalog.Apply(initial, new SettingsPatch(new Dictionary<string, string?>
        {
            ["astronomy.agenda.city_id"] = "rome",
            ["astronomy.agenda.country_codes"] = "IT,VN,JP",
            ["astronomy.agenda.show_saints"] = "false"
        }));
        Assert.True(valid.Succeeded);
        Assert.Equal(new[] { "IT", "VN", "JP" }, valid.Value!.AstronomyAgendaCountryCodes);
        Assert.False(valid.Value.AstronomyAgendaShowSaints);
        foreach (var malformed in new[] { "IT,,VN", "XX", "IT,IT" })
        {
            Assert.False(SettingsCatalog.Apply(initial, new SettingsPatch(new Dictionary<string, string?>
            {
                ["astronomy.agenda.country_codes"] = malformed
            })).Succeeded);
        }
    }

    private static long Scalar(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt64(command.ExecuteScalar());
    }
}
