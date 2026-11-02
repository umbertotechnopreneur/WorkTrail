// SPDX-License-Identifier: MIT
namespace WorkTrail.Application;

/// <summary>Describes a desktop view independently of its native or WinUI menu renderer.</summary>
public sealed record WindowViewDefinition(string WindowKey, string LabelKey, string Glyph);

/// <summary>Provides the canonical navigation order, labels and glyphs for WorkTrail views.</summary>
public static class WindowViewCatalog
{
    /// <summary>Gets the seven views in the same order as the launcher.</summary>
    public static IReadOnlyList<WindowViewDefinition> All { get; } = Array.AsReadOnly<WindowViewDefinition>(
    [
        new(WindowStateKeys.WorldClocks, "Launcher.Clocks", "\uE823"),
        new(WindowStateKeys.WorldMap, "Launcher.Map", "\uE707"),
        new(WindowStateKeys.AstronomyAgenda, "Launcher.Calendar", "\uE787"),
        new(WindowStateKeys.LocalSky, "Launcher.Sky", "\uE9CE"),
        new(WindowStateKeys.CelestialMap, "Launcher.Globe", "\uE774"),
        new(WindowStateKeys.LunarPhase, "Launcher.Moon", "\uE708"),
        new(WindowStateKeys.AvailabilityPlanner, "Planner.Title", "\uE8A5")
    ]);
}
