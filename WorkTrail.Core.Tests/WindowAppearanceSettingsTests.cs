// SPDX-License-Identifier: MIT
using System.Collections.Generic;
using WorkTrail.Application;
using Xunit;

namespace WorkTrail.Core.Tests;

/// <summary>Protects independent widget preferences against accidental shared-setting changes.</summary>
public sealed class WindowAppearanceSettingsTests
{
    /// <summary>Changing one widget must not change any other widget's presentation.</summary>
    [Theory]
    [InlineData(WindowStateKeys.WorldClocks)]
    [InlineData(WindowStateKeys.WorldMap)]
    [InlineData(WindowStateKeys.LunarPhase)]
    [InlineData(WindowStateKeys.LocalSky)]
    [InlineData(WindowStateKeys.AstronomyAgenda)]
    [InlineData(WindowStateKeys.CelestialMap)]
    [InlineData(WindowStateKeys.AvailabilityPlanner)]
    public void PresentationPatchAffectsOnlyItsOwningWidget(string windowKey)
    {
        var original = new AppSettings();
        var requested = new WindowAppearance(75, false, true);
        var result = SettingsCatalog.Apply(original, WindowAppearanceSettings.CreatePatch(windowKey, requested));
        Assert.True(result.Succeeded);
        Assert.NotNull(result.Value);
        Assert.Equal(requested, WindowAppearanceSettings.Get(result.Value, windowKey));
        foreach (var other in new[]
        {
            WindowStateKeys.WorldClocks, WindowStateKeys.WorldMap, WindowStateKeys.LunarPhase,
            WindowStateKeys.LocalSky, WindowStateKeys.AstronomyAgenda, WindowStateKeys.CelestialMap,
            WindowStateKeys.AvailabilityPlanner
        })
        {
            if (other != windowKey)
            {
                Assert.Equal(WindowAppearanceSettings.Get(original, other), WindowAppearanceSettings.Get(result.Value, other));
            }
        }
    }

    /// <summary>Unsupported timetable density values cannot replace persisted preferences.</summary>
    [Theory]
    [InlineData("planner.visible_hours", "12")]
    [InlineData("planner.step_minutes", "5")]
    [InlineData("planner.column_width", "139")]
    [InlineData("planner.column_width", "NaN")]
    [InlineData("planner.column_width", "Infinity")]
    public void InvalidTimetablePreferencesAreRejected(string key, string value)
    {
        var original = new AppSettings(PlannerVisibleHours: 16, PlannerStepMinutes: 15);
        var result = SettingsCatalog.Apply(original, new SettingsPatch(new Dictionary<string, string?> { [key] = value }));
        Assert.False(result.Succeeded);
        Assert.Equal(16, original.PlannerVisibleHours);
        Assert.Equal(15, original.PlannerStepMinutes);
    }

    /// <summary>Column preferences support automatic fitting and fractional logical pixels.</summary>
    [Theory]
    [InlineData("0", 0)]
    [InlineData("236.5", 236.5)]
    public void ColumnPreferencesPreserveAutomaticAndManualWidths(string text, double expected)
    {
        var result = SettingsCatalog.Apply(new AppSettings(), new SettingsPatch(new Dictionary<string, string?>
        {
            ["planner.column_width"] = text
        }));
        Assert.True(result.Succeeded);
        Assert.Equal(expected, result.Value!.PlannerColumnWidth);
    }
}
