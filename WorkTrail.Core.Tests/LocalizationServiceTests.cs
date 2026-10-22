// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using WorkTrail.Application;
using WorkTrail.Services;
using Xunit;

namespace WorkTrail.Core.Tests;

public sealed class LocalizationServiceTests
{
    [Theory]
    [InlineData("en-US")]
    [InlineData("it-IT")]
    [InlineData("fr-FR")]
    [InlineData("de-DE")]
    [InlineData("es-ES")]
    [InlineData("zh-Hans")]
    [InlineData("vi-VN")]
    [InlineData("ko-KR")]
    [InlineData("pt-PT")]
    [InlineData("pt-BR")]
    public void LauncherAndWeatherNames_ResolveInEveryShippedLanguage(string locale)
    {
        var strings = new LocalizationService(locale);
        foreach (var key in new[] { "Launcher.Settings", "Launcher.Clocks", "Launcher.Calendar", "Launcher.Sky",
            "Launcher.Moon", "Launcher.Map", "Launcher.Globe", "Launcher.Startup", "Launcher.Snapping", "Launcher.Theme",
            "Launcher.Language", "WeatherSetup.Title", "WeatherSetup.Key", "WeatherSetup.Save" })
            Assert.NotEqual(key, strings.Translate(key));
        foreach (var pair in LocalizationService.AutomationNameKeys)
            Assert.NotEqual(pair.Value, strings.Translate(pair.Value));
    }

    /// <summary>Every dynamic identity displayed by sky, agenda, and clocks resolves in every shipped language.</summary>
    [Theory]
    [InlineData("en-US")]
    [InlineData("it-IT")]
    [InlineData("fr-FR")]
    [InlineData("de-DE")]
    [InlineData("es-ES")]
    [InlineData("zh-Hans")]
    [InlineData("vi-VN")]
    [InlineData("ko-KR")]
    [InlineData("pt-PT")]
    [InlineData("pt-BR")]
    public void DynamicWidgetLabels_ResolveFromActualEnumsAndEmbeddedCatalogs(string locale)
    {
        var strings = new LocalizationService(locale);
        var keys = Enum.GetValues<CelestialBodyKind>().Select(body => $"CelestialBody{body}")
            .Concat(Enum.GetValues<TropicalZodiacSign>().Select(sign => $"CelestialZodiac{sign}"))
            .Concat(Enum.GetValues<CelestialEventKind>()
                .Where(kind => kind is not (CelestialEventKind.ImportantDate or CelestialEventKind.SpaceWeather or CelestialEventKind.Holiday or CelestialEventKind.Saint))
                .Select(kind => $"CelestialEvent{kind}"))
            .Concat(Enum.GetValues<SpaceWeatherEventKind>().Select(kind => $"CelestialSpaceWeather{kind}"))
            .Concat(CelestialSkyCatalog.Current.Satellites.Select(item => $"CelestialSatellite{item.Id}"))
            .Concat(CelestialSkyCatalog.Current.Constellations.Where(item => item.ZodiacSign is null)
                .Select(item => $"CelestialConstellation{item.Id}"))
            .Concat(CatalogKeys("meteor-showers", "showers", "CelestialMeteor"))
            .Concat(CatalogKeys("important-dates", "dates", "CelestialImportantDate"))
            .Concat(new[] { "WorldClock.Week", "Dialog.Save", "Launcher.CloseTitle", "Launcher.CloseMessage",
                "Launcher.CloseToTray", "Launcher.Exit", "Welcome.Title", "Welcome.Message", "Welcome.Manual", "Welcome.Continue" });
        foreach (var key in keys) Assert.False(string.IsNullOrWhiteSpace(strings.Translate(key)), key);
        Assert.DoesNotContain("{0}", strings.Format("Welcome.Suggestion", "Windows time zone"), StringComparison.Ordinal);
    }

    private static IEnumerable<string> CatalogKeys(string name, string collection, string prefix)
    {
        using var stream = typeof(LocalizationService).Assembly.GetManifestResourceStream($"WorkTrail.Data.{name}.json")
            ?? throw new InvalidDataException(name);
        using var document = JsonDocument.Parse(stream);
        return document.RootElement.GetProperty(collection).EnumerateArray()
            .Select(item => prefix + item.GetProperty("id").GetString()).ToArray();
    }

    [Fact]
    public void SystemLanguage_UsesTheSuppliedWindowsCulture() =>
        Assert.Equal("it-IT", LocalizationService.ResolveLanguage("system", CultureInfo.GetCultureInfo("it-IT")));

    [Fact]
    public void LanguagePatch_AcceptsEveryShippedLocaleAndRejectsAnUnknownChoice()
    {
        foreach (var locale in ProductLanguageCatalog.UiChoices)
            Assert.True(SettingsCatalog.Apply(new AppSettings(), new SettingsPatch(new Dictionary<string, string?> { ["language"] = locale })).Succeeded);
        Assert.False(SettingsCatalog.Apply(new AppSettings(), new SettingsPatch(new Dictionary<string, string?> { ["language"] = "unsupported" })).Succeeded);
    }
}
