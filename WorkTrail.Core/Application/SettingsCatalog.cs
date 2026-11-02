// SPDX-License-Identifier: MIT
using System.Globalization;
using WorkTrail.Services;

namespace WorkTrail.Application;

/// <summary>Validates the desktop preferences accepted by the shared facade.</summary>
public static class SettingsCatalog
{

    #region Methods and properties
    /// <summary>Normalizes widget preferences while preserving saved cities and window placement.</summary>
    public static AppSettings NormalizePersisted(AppSettings settings) => settings with
    {
        UiLanguage = ProductLanguageCatalog.CanonicalUiChoice(settings.UiLanguage) ?? "system",
        Theme = settings.Theme is "light" or "dark" ? settings.Theme : "system",
        WorldClockCityIds = WorldClockSelection.NormalizePersisted(settings.WorldClockCityIds),
        MainWindowOpacityPercent = Math.Clamp(settings.MainWindowOpacityPercent, 25, 100),
        WorldClockWindowOpacityPercent = Math.Clamp(settings.WorldClockWindowOpacityPercent, 25, 100),
        WorldMapWindowOpacityPercent = Math.Clamp(settings.WorldMapWindowOpacityPercent, 25, 100),
        LunarPhaseWindowOpacityPercent = Math.Clamp(settings.LunarPhaseWindowOpacityPercent, 25, 100),
        LocalSkyWindowOpacityPercent = Math.Clamp(settings.LocalSkyWindowOpacityPercent, 25, 100),
        AstronomyAgendaWindowOpacityPercent = Math.Clamp(settings.AstronomyAgendaWindowOpacityPercent, 25, 100),
        CelestialMapWindowOpacityPercent = Math.Clamp(settings.CelestialMapWindowOpacityPercent, 25, 100),
        AvailabilityPlannerWindowOpacityPercent = Math.Clamp(settings.AvailabilityPlannerWindowOpacityPercent, 25, 100),
        AstronomyAgendaCityId = settings.AstronomyAgendaCityId ?? "",
        HomeCityId = settings.HomeCityId ?? "",
        PlannerFirstDayOfWeek = settings.PlannerFirstDayOfWeek == DayOfWeek.Sunday ? DayOfWeek.Sunday : DayOfWeek.Monday,
        PlannerVisibleHours = settings.PlannerVisibleHours == 16 ? 16 : 24,
        PlannerStepMinutes = settings.PlannerStepMinutes is 15 or 60 ? settings.PlannerStepMinutes : 30,
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
                    "planner.first_day_of_week" => current with
                    {
                        PlannerFirstDayOfWeek = value switch
                        { "monday" => DayOfWeek.Monday, "sunday" => DayOfWeek.Sunday, _ => throw new ArgumentException() }
                    },
                    "planner.visible_hours" => current with { PlannerVisibleHours = value switch { "16" => 16, "24" => 24, _ => throw new ArgumentException() } },
                    "planner.step_minutes" => current with { PlannerStepMinutes = value switch { "15" => 15, "30" => 30, "60" => 60, _ => throw new ArgumentException() } },
                    "window.world_clocks.opacity_percent" => current with { WorldClockWindowOpacityPercent = ParseOpacity(value) },
                    "window.world_clocks.show_in_taskbar" => current with { WorldClockWindowShowInTaskbar = ParseBoolean(value) },
                    "window.world_clocks.always_on_top" => current with { WorldClockWindowAlwaysOnTop = ParseBoolean(value) },
                    "window.world_map.opacity_percent" => current with { WorldMapWindowOpacityPercent = ParseOpacity(value) },
                    "window.world_map.show_in_taskbar" => current with { WorldMapWindowShowInTaskbar = ParseBoolean(value) },
                    "window.world_map.always_on_top" => current with { WorldMapWindowAlwaysOnTop = ParseBoolean(value) },
                    "window.lunar_phase.opacity_percent" => current with { LunarPhaseWindowOpacityPercent = ParseOpacity(value) },
                    "window.lunar_phase.show_in_taskbar" => current with { LunarPhaseWindowShowInTaskbar = ParseBoolean(value) },
                    "window.lunar_phase.always_on_top" => current with { LunarPhaseWindowAlwaysOnTop = ParseBoolean(value) },
                    "window.local_sky.opacity_percent" => current with { LocalSkyWindowOpacityPercent = ParseOpacity(value) },
                    "window.local_sky.show_in_taskbar" => current with { LocalSkyWindowShowInTaskbar = ParseBoolean(value) },
                    "window.local_sky.always_on_top" => current with { LocalSkyWindowAlwaysOnTop = ParseBoolean(value) },
                    "window.astronomy_agenda.opacity_percent" => current with { AstronomyAgendaWindowOpacityPercent = ParseOpacity(value) },
                    "window.astronomy_agenda.show_in_taskbar" => current with { AstronomyAgendaWindowShowInTaskbar = ParseBoolean(value) },
                    "window.astronomy_agenda.always_on_top" => current with { AstronomyAgendaWindowAlwaysOnTop = ParseBoolean(value) },
                    "window.celestial_map.opacity_percent" => current with { CelestialMapWindowOpacityPercent = ParseOpacity(value) },
                    "window.celestial_map.show_in_taskbar" => current with { CelestialMapWindowShowInTaskbar = ParseBoolean(value) },
                    "window.celestial_map.always_on_top" => current with { CelestialMapWindowAlwaysOnTop = ParseBoolean(value) },
                    "window.availability_planner.opacity_percent" => current with { AvailabilityPlannerWindowOpacityPercent = ParseOpacity(value) },
                    "window.availability_planner.show_in_taskbar" => current with { AvailabilityPlannerWindowShowInTaskbar = ParseBoolean(value) },
                    "window.availability_planner.always_on_top" => current with { AvailabilityPlannerWindowAlwaysOnTop = ParseBoolean(value) },
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

    #endregion

    #region Implementation

    /// <summary>Parses a Boolean preference and rejects unsupported text.</summary>
    private static bool ParseBoolean(string value) => bool.TryParse(value, out var result) ? result : throw new ArgumentException();
    /// <summary>Accepts an invariant integer opacity within the supported range.</summary>
    private static int ParseOpacity(string value) => int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var result)
        && result is >= 25 and <= 100 ? result : throw new ArgumentException();
    /// <summary>Validates a comma-separated selection of distinct supported countries.</summary>
    private static IReadOnlyList<string> ParseCountries(string value)
    {
        if (value.Length == 0)
        {
            return [];
        }

        var values = value.Split(',', StringSplitOptions.TrimEntries);
        if (values.Any(code => !CelestialCalendarCountries.All.Any(country => country.Code.Equals(code, StringComparison.OrdinalIgnoreCase)))
            || values.Distinct(StringComparer.OrdinalIgnoreCase).Count() != values.Length)
        {
            throw new ArgumentException();
        }

        return NormalizeCountries(values);
    }
    /// <summary>Removes unsupported country codes and returns unique uppercase identifiers.</summary>
    private static IReadOnlyList<string> NormalizeCountries(IReadOnlyList<string> values) => values
        .Where(code => CelestialCalendarCountries.All.Any(country => country.Code.Equals(code, StringComparison.OrdinalIgnoreCase)))
        .Select(code => code.ToUpperInvariant()).Distinct(StringComparer.Ordinal).ToArray();

    #endregion

}
