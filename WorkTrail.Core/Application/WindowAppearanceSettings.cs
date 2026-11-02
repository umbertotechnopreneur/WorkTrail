// SPDX-License-Identifier: MIT
using System.Globalization;

namespace WorkTrail.Application;

/// <summary>Contains the persisted presentation choices of one independent widget.</summary>
public sealed record WindowAppearance(int OpacityPercent, bool ShowInTaskbar, bool AlwaysOnTop);

/// <summary>Maps independent widget identities to their own presentation preferences.</summary>
public static class WindowAppearanceSettings
{
    /// <summary>Reads the presentation preferences of one supported widget.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The key does not identify a supported widget.</exception>
    public static WindowAppearance Get(AppSettings settings, string windowKey)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return windowKey switch
        {
            WindowStateKeys.WorldClocks => new(settings.WorldClockWindowOpacityPercent, settings.WorldClockWindowShowInTaskbar, settings.WorldClockWindowAlwaysOnTop),
            WindowStateKeys.WorldMap => new(settings.WorldMapWindowOpacityPercent, settings.WorldMapWindowShowInTaskbar, settings.WorldMapWindowAlwaysOnTop),
            WindowStateKeys.LunarPhase => new(settings.LunarPhaseWindowOpacityPercent, settings.LunarPhaseWindowShowInTaskbar, settings.LunarPhaseWindowAlwaysOnTop),
            WindowStateKeys.LocalSky => new(settings.LocalSkyWindowOpacityPercent, settings.LocalSkyWindowShowInTaskbar, settings.LocalSkyWindowAlwaysOnTop),
            WindowStateKeys.AstronomyAgenda => new(settings.AstronomyAgendaWindowOpacityPercent, settings.AstronomyAgendaWindowShowInTaskbar, settings.AstronomyAgendaWindowAlwaysOnTop),
            WindowStateKeys.CelestialMap => new(settings.CelestialMapWindowOpacityPercent, settings.CelestialMapWindowShowInTaskbar, settings.CelestialMapWindowAlwaysOnTop),
            WindowStateKeys.AvailabilityPlanner => new(settings.AvailabilityPlannerWindowOpacityPercent, settings.AvailabilityPlannerWindowShowInTaskbar, settings.AvailabilityPlannerWindowAlwaysOnTop),
            _ => throw new ArgumentOutOfRangeException(nameof(windowKey), windowKey, "Unsupported widget window.")
        };
    }

    /// <summary>Builds a presentation patch affecting only the requested widget.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The key does not identify a supported widget.</exception>
    public static SettingsPatch CreatePatch(string windowKey, WindowAppearance appearance)
    {
        ArgumentNullException.ThrowIfNull(appearance);
        var prefix = windowKey switch
        {
            WindowStateKeys.WorldClocks => "window.world_clocks",
            WindowStateKeys.WorldMap => "window.world_map",
            WindowStateKeys.LunarPhase => "window.lunar_phase",
            WindowStateKeys.LocalSky => "window.local_sky",
            WindowStateKeys.AstronomyAgenda => "window.astronomy_agenda",
            WindowStateKeys.CelestialMap => "window.celestial_map",
            WindowStateKeys.AvailabilityPlanner => "window.availability_planner",
            _ => throw new ArgumentOutOfRangeException(nameof(windowKey), windowKey, "Unsupported widget window.")
        };
        return new SettingsPatch(new Dictionary<string, string?>
        {
            [$"{prefix}.opacity_percent"] = appearance.OpacityPercent.ToString(CultureInfo.InvariantCulture),
            [$"{prefix}.show_in_taskbar"] = appearance.ShowInTaskbar ? "true" : "false",
            [$"{prefix}.always_on_top"] = appearance.AlwaysOnTop ? "true" : "false"
        });
    }
}
