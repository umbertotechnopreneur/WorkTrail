// SPDX-License-Identifier: MIT
using System.Globalization;
using WorkTrail.Services;

namespace WorkTrail.Application;

/// <summary>Validates the desktop preferences accepted by the shared facade.</summary>
public static class SettingsCatalog
{
    /// <summary>Normalizes widget preferences while preserving saved cities and window placement.</summary>
    public static AppSettings NormalizePersisted(AppSettings settings) => settings with
    {
        UiLanguage = ProductLanguageCatalog.CanonicalUiChoice(settings.UiLanguage) ?? "system",
        Theme = settings.Theme is "light" or "dark" ? settings.Theme : "system",
        WorldClockCityIds = WorldClockSelection.NormalizePersisted(settings.WorldClockCityIds),
        MainWindowOpacityPercent = Math.Clamp(settings.MainWindowOpacityPercent, 25, 100),
        WorldClockWindowOpacityPercent = Math.Clamp(settings.WorldClockWindowOpacityPercent, 25, 100),
        AstronomyAgendaCityId = settings.AstronomyAgendaCityId ?? "",
        HomeCityId = settings.HomeCityId ?? "",
        WelcomeCompleted = settings.WelcomeCompleted && !string.IsNullOrWhiteSpace(settings.HomeCityId),
        AstronomyAgendaCountryCodes = NormalizeCountries(settings.AstronomyAgendaCountryCodes ?? ["IT"])
    };

    /// <summary>Validates a complete patch in memory before settings are written.</summary>
    public static OperationResult<AppSettings> Apply(AppSettings settings, SettingsPatch patch)
    {
        ArgumentNullException.ThrowIfNull(patch);
        var current = settings;
        foreach (var pair in patch.Values)
        {
            var key = pair.Key.Trim().ToLowerInvariant();
            var value = pair.Value?.Trim() ?? "";
            try
            {
                current = key switch
                {
                    "language" => current with { UiLanguage = ProductLanguageCatalog.CanonicalUiChoice(value) ?? throw new ArgumentException() },
                    "theme" => current with { Theme = value is "system" or "light" or "dark" ? value : throw new ArgumentException() },
                    "window.titlebar.auto_hide" => current with { AutoHideTitleBar = ParseBoolean(value) },
                    "window.snapping.enabled" => current with { WindowSnappingEnabled = ParseBoolean(value) },
                    "window.main.opacity_percent" => current with { MainWindowOpacityPercent = ParseOpacity(value) },
                    "window.main.show_in_taskbar" => current with { MainWindowShowInTaskbar = ParseBoolean(value) },
                    "world_clocks.weather.enabled" => current with { WorldClockWeatherEnabled = ParseBoolean(value) },
                    "window.world_clocks.opacity_percent" => current with { WorldClockWindowOpacityPercent = ParseOpacity(value) },
                    "window.world_clocks.show_in_taskbar" => current with { WorldClockWindowShowInTaskbar = ParseBoolean(value) },
                    "window.world_map.show_in_taskbar" => current with { WorldMapWindowShowInTaskbar = ParseBoolean(value) },
                    "window.lunar_phase.show_in_taskbar" => current with { LunarPhaseWindowShowInTaskbar = ParseBoolean(value) },
                    "astronomy.agenda.city_id" => current with { AstronomyAgendaCityId = value.ToLowerInvariant() },
                    "home.city_id" => current with { HomeCityId = value.ToLowerInvariant() },
                    "astronomy.agenda.country_codes" => current with { AstronomyAgendaCountryCodes = ParseCountries(value) },
                    "astronomy.agenda.show_saints" => current with { AstronomyAgendaShowSaints = ParseBoolean(value) },
                    _ => throw new ArgumentException()
                };
            }
            catch (ArgumentException)
            {
                return OperationResult<AppSettings>.Failure("settings.validation.failed", "SettingsValidationFailed",
                    new ValidationIssue(key, "invalid_value", "SettingsFieldInvalid"));
            }
        }
        return OperationResult<AppSettings>.Success("settings.valid", "SettingsSaved", current);
    }

    private static bool ParseBoolean(string value) => bool.TryParse(value, out var result) ? result : throw new ArgumentException();
    private static int ParseOpacity(string value) => int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var result)
        && result is >= 25 and <= 100 ? result : throw new ArgumentException();
    private static IReadOnlyList<string> ParseCountries(string value)
    {
        if (value.Length == 0) return [];
        var values = value.Split(',', StringSplitOptions.TrimEntries);
        if (values.Any(code => !CelestialCalendarCountries.All.Any(country => country.Code.Equals(code, StringComparison.OrdinalIgnoreCase)))
            || values.Distinct(StringComparer.OrdinalIgnoreCase).Count() != values.Length)
            throw new ArgumentException();
        return NormalizeCountries(values);
    }
    private static IReadOnlyList<string> NormalizeCountries(IReadOnlyList<string> values) => values
        .Where(code => CelestialCalendarCountries.All.Any(country => country.Code.Equals(code, StringComparison.OrdinalIgnoreCase)))
        .Select(code => code.ToUpperInvariant()).Distinct(StringComparer.Ordinal).ToArray();
}
