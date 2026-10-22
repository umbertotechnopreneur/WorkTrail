// SPDX-License-Identifier: MIT
namespace WorkTrail;

/// <summary>Stores desktop widget preferences and window placement.</summary>
public sealed record AppSettings(
    string InstallationId = "",
    bool StartWithWindows = false,
    string UiLanguage = "system",
    string Theme = "system",
    IReadOnlyDictionary<string, WorkTrail.Application.WindowState>? WindowStates = null,
    IReadOnlyList<string>? WorldClockCityIds = null,
    bool WorldClockWeatherEnabled = true,
    int MainWindowOpacityPercent = 100,
    bool MainWindowShowInTaskbar = true,
    int WorldClockWindowOpacityPercent = 100,
    bool WorldClockWindowShowInTaskbar = true,
    IReadOnlyDictionary<string, bool>? WindowOpenStates = null,
    bool AutoHideTitleBar = true,
    bool WorldMapWindowShowInTaskbar = true,
    bool LunarPhaseWindowShowInTaskbar = true,
    bool WindowSnappingEnabled = true,
    string AstronomyAgendaCityId = "",
    IReadOnlyList<string>? AstronomyAgendaCountryCodes = null,
    bool AstronomyAgendaShowSaints = true,
    string HomeCityId = "",
    bool WelcomeCompleted = false);
