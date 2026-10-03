// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using WorkTrail.Application;
using WorkTrail.Services;
using Xunit;

namespace WorkTrail.Core.Tests;

public sealed class SettingsAndRetentionSafetyTests
{
    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(false, false)]
    public void AstronomyTaskbarPreferences_PersistIndependently(bool mapVisible, bool moonVisible)
    {
        var dataDirectory = Path.Combine(Path.GetTempPath(), "WorkTrail.Tests", Guid.NewGuid().ToString("N"));
        try
        {
            var store = new LocalStore(dataDirectory);
            var original = store.LoadSettings();
            Assert.True(original.WorldMapWindowShowInTaskbar);
            Assert.True(original.LunarPhaseWindowShowInTaskbar);
            var result = SettingsCatalog.Apply(original, new SettingsPatch(new Dictionary<string, string?>
            {
                ["window.world_map.show_in_taskbar"] = mapVisible ? "true" : "false",
                ["window.lunar_phase.show_in_taskbar"] = moonVisible ? "true" : "false"
            }));
            Assert.True(result.Succeeded);
            store.SaveSettings(Assert.IsType<AppSettings>(result.Value));
            var restored = new LocalStore(dataDirectory).LoadSettings();
            Assert.Equal(mapVisible, restored.WorldMapWindowShowInTaskbar);
            Assert.Equal(moonVisible, restored.LunarPhaseWindowShowInTaskbar);
            Assert.Equal(original.WorldClockWindowShowInTaskbar, restored.WorldClockWindowShowInTaskbar);
            var changedTheme = SettingsCatalog.Apply(restored, new SettingsPatch(new Dictionary<string, string?> { ["theme"] = "dark" }));
            Assert.True(changedTheme.Succeeded);
            Assert.True(SettingsCatalog.TryGetValue(changedTheme.Value!, "window.world_map.show_in_taskbar", out var map));
            Assert.True(SettingsCatalog.TryGetValue(changedTheme.Value!, "window.lunar_phase.show_in_taskbar", out var moon));
            Assert.Equal(mapVisible, Assert.IsType<bool>(map));
            Assert.Equal(moonVisible, Assert.IsType<bool>(moon));
        }
        finally
        {
            if (Directory.Exists(dataDirectory)) Directory.Delete(dataDirectory, recursive: true);
        }
    }

    [Theory]
    [InlineData("window.world_map.show_in_taskbar")]
    [InlineData("window.lunar_phase.show_in_taskbar")]
    public void AstronomyTaskbarPreferences_RejectInvalidValuesAtomically(string key)
    {
        var result = SettingsCatalog.Apply(new AppSettings(), new SettingsPatch(new Dictionary<string, string?>
        {
            [key] = "sometimes",
            ["theme"] = "dark"
        }));
        Assert.False(result.Succeeded);
        Assert.Null(result.Value);
        Assert.Contains(result.Issues, issue => issue.Field == key);
    }

    /// <summary>Verifies that the global preference survives storage and unrelated settings changes.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AutoHideTitleBar_RoundTripsThroughTheCatalogAndLocalStore(bool enabled)
    {
        var dataDirectory = Path.Combine(Path.GetTempPath(), "WorkTrail.Tests", Guid.NewGuid().ToString("N"));
        try
        {
            var store = new LocalStore(dataDirectory);
            var defaults = store.LoadSettings();
            Assert.True(defaults.AutoHideTitleBar);
            var descriptor = Assert.Single(SettingsCatalog.Definitions, item => item.Key == "window.titlebar.auto_hide");
            Assert.False(descriptor.RequiresRestart);

            var updated = SettingsCatalog.Apply(defaults, new SettingsPatch(new Dictionary<string, string?>
            {
                ["window.titlebar.auto_hide"] = enabled ? "true" : "false"
            }));
            Assert.True(updated.Succeeded);
            store.SaveSettings(Assert.IsType<AppSettings>(updated.Value));

            var restored = new LocalStore(dataDirectory).LoadSettings();
            Assert.Equal(enabled, restored.AutoHideTitleBar);
            Assert.True(SettingsCatalog.TryGetValue(restored, "window.titlebar.auto_hide", out var storedValue));
            Assert.Equal(enabled, Assert.IsType<bool>(storedValue));

            var themeChanged = SettingsCatalog.Apply(restored, new SettingsPatch(new Dictionary<string, string?>
            {
                ["theme"] = "dark"
            }));
            Assert.True(themeChanged.Succeeded);
            Assert.Equal(enabled, themeChanged.Value!.AutoHideTitleBar);
        }
        finally
        {
            if (Directory.Exists(dataDirectory))
            {
                Directory.Delete(dataDirectory, recursive: true);
            }
        }
    }

    /// <summary>Rejects an invalid global preference without applying any accompanying settings.</summary>
    [Fact]
    public void AutoHideTitleBar_RejectsAnInvalidValueAtomically()
    {
        var defaults = Assert.IsType<AppSettings>(JsonSerializer.Deserialize<AppSettings>(
            "{}", new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        Assert.True(defaults.AutoHideTitleBar);

        var result = SettingsCatalog.Apply(defaults, new SettingsPatch(new Dictionary<string, string?>
        {
            ["window.titlebar.auto_hide"] = "sometimes",
            ["theme"] = "dark"
        }));

        Assert.False(result.Succeeded);
        Assert.Null(result.Value);
        Assert.Contains(result.Issues, issue => issue.Field == "window.titlebar.auto_hide");
        Assert.Equal("system", defaults.Theme);
    }

    /// <summary>Preserves the global snapping preference through storage and unrelated settings changes.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void WindowSnapping_RoundTripsThroughTheCatalogAndLocalStore(bool enabled)
    {
        var dataDirectory = Path.Combine(Path.GetTempPath(), "WorkTrail.Tests", Guid.NewGuid().ToString("N"));
        try
        {
            var store = new LocalStore(dataDirectory);
            var defaults = store.LoadSettings();
            Assert.True(defaults.WindowSnappingEnabled);
            var descriptor = Assert.Single(SettingsCatalog.Definitions, item => item.Key == "window.snapping.enabled");
            Assert.False(descriptor.RequiresRestart);

            var updated = SettingsCatalog.Apply(defaults, new SettingsPatch(new Dictionary<string, string?>
            {
                ["window.snapping.enabled"] = enabled ? "true" : "false"
            }));
            Assert.True(updated.Succeeded);
            store.SaveSettings(Assert.IsType<AppSettings>(updated.Value));

            var restored = new LocalStore(dataDirectory).LoadSettings();
            Assert.Equal(enabled, restored.WindowSnappingEnabled);
            Assert.True(SettingsCatalog.TryGetValue(restored, "window.snapping.enabled", out var storedValue));
            Assert.Equal(enabled, Assert.IsType<bool>(storedValue));

            var themeChanged = SettingsCatalog.Apply(restored, new SettingsPatch(new Dictionary<string, string?>
            {
                ["theme"] = "dark",
                ["window.titlebar.auto_hide"] = "false"
            }));
            Assert.True(themeChanged.Succeeded);
            Assert.Equal(enabled, themeChanged.Value!.WindowSnappingEnabled);
        }
        finally
        {
            if (Directory.Exists(dataDirectory))
            {
                Directory.Delete(dataDirectory, recursive: true);
            }
        }
    }

    /// <summary>Rejects invalid snapping values atomically while preserving the enabled default.</summary>
    [Fact]
    public void WindowSnapping_RejectsAnInvalidValueAtomically()
    {
        var defaults = Assert.IsType<AppSettings>(JsonSerializer.Deserialize<AppSettings>(
            "{}", new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        Assert.True(defaults.WindowSnappingEnabled);

        var result = SettingsCatalog.Apply(defaults, new SettingsPatch(new Dictionary<string, string?>
        {
            ["window.snapping.enabled"] = "sometimes",
            ["theme"] = "dark"
        }));

        Assert.False(result.Succeeded);
        Assert.Null(result.Value);
        Assert.Contains(result.Issues, issue => issue.Field == "window.snapping.enabled");
        Assert.True(defaults.WindowSnappingEnabled);
        Assert.Equal("system", defaults.Theme);
    }

    [Fact]
    public void Apply_UsesOneTransactionalCatalogForAiTuning()
    {
        var original = new AppSettings(AiProvider: "anthropic");
        var patch = new SettingsPatch(new Dictionary<string, string?>
        {
            ["ai.provider"] = "OpenAI",
            ["ai.model"] = "gpt-5.6",
            ["ai.output_detail"] = "DETAILED",
            ["ai.reasoning_effort"] = "XHIGH"
        });

        var result = SettingsCatalog.Apply(original, patch);

        Assert.True(result.Succeeded);
        Assert.NotNull(result.Value);
        Assert.Equal("openai", result.Value.AiProvider);
        Assert.Equal("OPENAI_API_KEY", result.Value.AiApiKeyName);
        Assert.Equal("https://api.openai.com/v1/responses", result.Value.AiEndpoint);
        Assert.Equal("detailed", result.Value.AiOutputDetail);
        Assert.Equal("xhigh", result.Value.AiReasoningEffort);
    }

    [Fact]
    public void Apply_RejectsTheWholePatchWhenOneValueIsInvalid()
    {
        var original = new AppSettings(Theme: "system");
        var patch = new SettingsPatch(new Dictionary<string, string?>
        {
            ["theme"] = "dark",
            ["ai.endpoint"] = "http://remote.example.invalid/v1"
        });

        var result = SettingsCatalog.Apply(original, patch);

        Assert.False(result.Succeeded);
        Assert.Null(result.Value);
        Assert.Equal("system", original.Theme);
        Assert.Contains(result.Issues, issue => issue.Field == "ai.endpoint");
    }

    [Fact]
    public void Apply_PersistsCustomPromptAndInformationalWeeklyHours()
    {
        var result = SettingsCatalog.Apply(
            new AppSettings(),
            new SettingsPatch(new Dictionary<string, string?>
            {
                ["ai.custom_prompt"] = "Prioritize short summaries.",
                ["active_hours.monday.active"] = "09:00-18:00",
                ["active_hours.monday.breaks"] = "13:00-14:00, 16:30-16:45",
                ["ai.include_device_location"] = "true"
            }));

        Assert.True(result.Succeeded);
        var settings = Assert.IsType<AppSettings>(result.Value);
        Assert.Equal("Prioritize short summaries.", settings.AiCustomPrompt);
        Assert.True(settings.IncludeDeviceLocation);
        var monday = Assert.Single(settings.ActiveHours!, day => day.Day == "monday");
        Assert.Equal("09:00-18:00", monday.ActivePeriod);
        Assert.Equal("13:00-14:00, 16:30-16:45", monday.BreakPeriods);
    }

    [Fact]
    public void Apply_PersistsScheduledScreenshotInterval()
    {
        var result = SettingsCatalog.Apply(
            new AppSettings(),
            new SettingsPatch(new Dictionary<string, string?>
            {
                ["screenshots.interval_minutes"] = "15"
            }));

        Assert.True(result.Succeeded);
        Assert.Equal(15, result.Value?.ScreenshotIntervalMinutes);
    }

    [Fact]
    public void ScreenshotDetailsPanePreference_RoundTripsThroughTheSettingsCatalog()
    {
        var defaults = Assert.IsType<AppSettings>(
            JsonSerializer.Deserialize<AppSettings>("{}", new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        var opened = SettingsCatalog.Apply(
            defaults,
            new SettingsPatch(new Dictionary<string, string?>
            {
                ["screenshots.details_pane_open"] = "true"
            }));

        Assert.False(defaults.ScreenshotDetailsPaneOpen);
        Assert.True(opened.Succeeded);
        var openSettings = Assert.IsType<AppSettings>(opened.Value);
        Assert.True(openSettings.ScreenshotDetailsPaneOpen);
        Assert.True(SettingsCatalog.TryGetValue(openSettings, "screenshots.details_pane_open", out var storedPreference));
        Assert.Equal(true, storedPreference);

        var restored = Assert.IsType<AppSettings>(JsonSerializer.Deserialize<AppSettings>(
            JsonSerializer.Serialize(openSettings, new JsonSerializerOptions(JsonSerializerDefaults.Web)),
            new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        Assert.True(restored.ScreenshotDetailsPaneOpen);

        var closed = SettingsCatalog.Apply(
            restored,
            new SettingsPatch(new Dictionary<string, string?>
            {
                ["screenshots.details_pane_open"] = "false"
            }));
        Assert.True(closed.Succeeded);
        Assert.False(closed.Value?.ScreenshotDetailsPaneOpen);

        var invalid = SettingsCatalog.Apply(
            defaults,
            new SettingsPatch(new Dictionary<string, string?>
            {
                ["screenshots.details_pane_open"] = "yes"
            }));
        Assert.False(invalid.Succeeded);
        Assert.Contains(invalid.Issues, issue => issue.Field == "screenshots.details_pane_open");
    }

    [Fact]
    public void AiMonthlySpendPreference_IsOptInAndRoundTripsThroughTheSettingsCatalog()
    {
        var defaults = Assert.IsType<AppSettings>(
            JsonSerializer.Deserialize<AppSettings>("{}", new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        var enabled = SettingsCatalog.Apply(
            defaults,
            new SettingsPatch(new Dictionary<string, string?>
            {
                ["ai.show_monthly_spend"] = "true"
            }));

        Assert.False(defaults.ShowAiMonthlySpend);
        Assert.True(enabled.Succeeded);
        var settings = Assert.IsType<AppSettings>(enabled.Value);
        Assert.True(settings.ShowAiMonthlySpend);
        Assert.True(SettingsCatalog.TryGetValue(settings, "ai.show_monthly_spend", out var storedPreference));
        Assert.Equal(true, storedPreference);

        var restored = Assert.IsType<AppSettings>(JsonSerializer.Deserialize<AppSettings>(
            JsonSerializer.Serialize(settings, new JsonSerializerOptions(JsonSerializerDefaults.Web)),
            new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        Assert.True(restored.ShowAiMonthlySpend);

        var disabled = SettingsCatalog.Apply(
            restored,
            new SettingsPatch(new Dictionary<string, string?>
            {
                ["ai.show_monthly_spend"] = "false"
            }));
        Assert.True(disabled.Succeeded);
        Assert.False(disabled.Value?.ShowAiMonthlySpend);

        var invalid = SettingsCatalog.Apply(
            defaults,
            new SettingsPatch(new Dictionary<string, string?>
            {
                ["ai.show_monthly_spend"] = "yes"
            }));
        Assert.False(invalid.Succeeded);
        Assert.Contains(invalid.Issues, issue => issue.Field == "ai.show_monthly_spend");
    }

    [Fact]
    public void WorldClockWeather_IsEnabledByDefaultAndRoundTripsThroughTheSettingsCatalog()
    {
        var defaults = Assert.IsType<AppSettings>(
            JsonSerializer.Deserialize<AppSettings>("{}", new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        var disabled = SettingsCatalog.Apply(
            defaults,
            new SettingsPatch(new Dictionary<string, string?>
            {
                ["world_clocks.weather.enabled"] = "false"
            }));

        Assert.True(defaults.WorldClockWeatherEnabled);
        Assert.True(disabled.Succeeded);
        var disabledSettings = Assert.IsType<AppSettings>(disabled.Value);
        Assert.False(disabledSettings.WorldClockWeatherEnabled);
        Assert.True(SettingsCatalog.TryGetValue(
            disabledSettings,
            "world_clocks.weather.enabled",
            out var storedPreference));
        Assert.Equal(false, storedPreference);

        var restored = Assert.IsType<AppSettings>(JsonSerializer.Deserialize<AppSettings>(
            JsonSerializer.Serialize(disabledSettings, new JsonSerializerOptions(JsonSerializerDefaults.Web)),
            new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        Assert.False(restored.WorldClockWeatherEnabled);

        var enabled = SettingsCatalog.Apply(
            restored,
            new SettingsPatch(new Dictionary<string, string?>
            {
                ["world_clocks.weather.enabled"] = "true"
            }));
        Assert.True(enabled.Succeeded);
        Assert.True(enabled.Value?.WorldClockWeatherEnabled);

        var invalid = SettingsCatalog.Apply(
            defaults,
            new SettingsPatch(new Dictionary<string, string?>
            {
                ["world_clocks.weather.enabled"] = "yes"
            }));
        Assert.False(invalid.Succeeded);
        Assert.Contains(invalid.Issues, issue => issue.Field == "world_clocks.weather.enabled");
    }

    [Theory]
    [InlineData("1")]
    [InlineData("0")]
    [InlineData("yes")]
    [InlineData("no")]
    [InlineData("on")]
    [InlineData("off")]
    [InlineData("TRUE")]
    [InlineData("False")]
    public void Apply_RejectsNonCanonicalBooleanValues(string value)
    {
        var result = SettingsCatalog.Apply(
            new AppSettings(),
            new SettingsPatch(new Dictionary<string, string?> { ["screenshots.enabled"] = value }));

        Assert.False(result.Succeeded);
        Assert.Contains(result.Issues, issue => issue.Field == "screenshots.enabled");
    }

    [Fact]
    public void Apply_RestrictsDailyAiProcessingLimitToFourHundred()
    {
        var accepted = SettingsCatalog.Apply(
            new AppSettings(),
            new SettingsPatch(new Dictionary<string, string?>
            {
                ["ai.daily_limit"] = SettingsCatalog.MaximumAiDailyLimit.ToString()
            }));
        var rejected = SettingsCatalog.Apply(
            new AppSettings(),
            new SettingsPatch(new Dictionary<string, string?>
            {
                ["ai.daily_limit"] = (SettingsCatalog.MaximumAiDailyLimit + 1).ToString()
            }));

        Assert.True(accepted.Succeeded);
        Assert.Equal(400, accepted.Value?.OpenAiDailyLimit);
        Assert.False(rejected.Succeeded);
        Assert.Contains(rejected.Issues, issue => issue.Field == "ai.daily_limit");
    }

    [Fact]
    public void SettingsWithoutAnInterval_UseTheFifteenMinuteScheduleDefault()
    {
        var settings = JsonSerializer.Deserialize<AppSettings>("{}", new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.Equal(15, settings?.ScreenshotIntervalMinutes);
    }

    [Fact]
    public void Apply_PersistsTheBoundedLocalSpanLabel()
    {
        var result = SettingsCatalog.Apply(
            new AppSettings(),
            new SettingsPatch(new Dictionary<string, string?> { ["activity.span_label"] = "  Project brief  " }));

        var settings = Assert.IsType<AppSettings>(result.Value);
        Assert.Equal("Project brief", settings.SpanLabel);
        Assert.True(SettingsCatalog.TryGetValue(settings, "activity.span_label", out var storedLabel));
        Assert.Equal("Project brief", storedLabel);

        var invalid = SettingsCatalog.Apply(
            settings,
            new SettingsPatch(new Dictionary<string, string?> { ["activity.span_label"] = new string('x', 21) }));
        Assert.False(invalid.Succeeded);
        Assert.Contains(invalid.Issues, issue => issue.Field == "activity.span_label");
    }

    [Fact]
    public void TaskbarWidget_IsUnavailableInV1AndPersistedVisibilityIsDisabled()
    {
        var defaults = Assert.IsType<AppSettings>(JsonSerializer.Deserialize<AppSettings>("{}", new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        var result = SettingsCatalog.Apply(
            defaults,
            new SettingsPatch(new Dictionary<string, string?>
            {
                ["taskbar.widget.visible"] = "true",
                ["taskbar.widget.position"] = "right"
            }));

        Assert.False(defaults.TaskbarWidgetVisible);
        Assert.False(result.Succeeded);
        Assert.False(SettingsCatalog.TryGetValue(defaults, "taskbar.widget.visible", out _));
        Assert.False(SettingsCatalog.TryGetValue(defaults, "taskbar.widget.position", out _));
        Assert.DoesNotContain(SettingsCatalog.Definitions, setting => setting.Key.StartsWith("taskbar.widget.", StringComparison.Ordinal));
        var normalized = SettingsCatalog.NormalizePersisted(defaults with { TaskbarWidgetVisible = true }, Path.GetTempPath());
        Assert.False(normalized.TaskbarWidgetVisible);

        var invalidPosition = SettingsCatalog.Apply(
            defaults,
            new SettingsPatch(new Dictionary<string, string?> { ["taskbar.widget.position"] = "center" }));
        Assert.False(invalidPosition.Succeeded);
        Assert.Contains(invalidPosition.Issues, issue => issue.Field == "taskbar.widget.position");
    }

    [Fact]
    public void Apply_RejectsBreakOutsideItsInformationalActivePeriod()
    {
        var result = SettingsCatalog.Apply(
            new AppSettings(),
            new SettingsPatch(new Dictionary<string, string?>
            {
                ["active_hours.monday.active"] = "09:00-18:00",
                ["active_hours.monday.breaks"] = "19:00-19:30"
            }));

        Assert.False(result.Succeeded);
        Assert.Contains(result.Issues, issue => issue.Field == "active_hours");
    }

    /// <summary>Verifies that active-hours boundaries must use quarter-hour increments.</summary>
    [Fact]
    public void Apply_RejectsActiveHoursOutsideTheQuarterHourContract()
    {
        var result = SettingsCatalog.Apply(
            new AppSettings(),
            new SettingsPatch(new Dictionary<string, string?>
            {
                ["active_hours.monday.active"] = "09:10-18:45"
            }));

        Assert.False(result.Succeeded);
        Assert.Contains(result.Issues, issue => issue.Field == "active_hours.monday.active");
    }

    [Fact]
    public void InformationalSchedule_UsesTheDeviceLocalWeekdayForUtcSnapshots()
    {
        var deviceTimeZone = TimeZoneInfo.CreateCustomTimeZone("test-device", TimeSpan.FromHours(7), "Test", "Test");
        var note = ActiveHoursSchedule.BuildInformationalNote(
            [new ActiveHoursDay("monday", "09:00-18:00", "13:00-14:00")],
            new DateTimeOffset(2026, 1, 4, 18, 0, 0, TimeSpan.Zero),
            deviceTimeZone);

        Assert.Equal("Monday: planned active hours 09:00-18:00; planned breaks 13:00-14:00. This is informational only.", note);
    }

    [Fact]
    public void NormalizePersisted_ClampsAndReplacesUnsupportedValues()
    {
        var normalized = SettingsCatalog.NormalizePersisted(
            new AppSettings(
                AiProvider: "unsupported",
                AiEndpoint: "http://remote.example.invalid/v1",
                AiApiKeyName: "SECRET_FROM_SETTINGS",
                AiOutputDetail: "unbounded",
                AiReasoningEffort: "extreme",
                OpenAiDailyLimit: 10_000,
                ScreenshotIntervalMinutes: 50_000,
                DataRetentionDays: -50,
                ScreenshotRetentionDays: 50_000),
            Path.Combine(Path.GetTempPath(), "WorkTrail", "screenshots"));

        Assert.Equal("openai", normalized.AiProvider);
        Assert.Equal("https://api.openai.com/v1/responses", normalized.AiEndpoint);
        Assert.Equal("OPENAI_API_KEY", normalized.AiApiKeyName);
        Assert.Equal("balanced", normalized.AiOutputDetail);
        Assert.Equal("auto", normalized.AiReasoningEffort);
        Assert.Equal(400, normalized.OpenAiDailyLimit);
        Assert.Equal(1440, normalized.ScreenshotIntervalMinutes);
        Assert.Equal(30, normalized.DataRetentionDays);
        Assert.Equal(90, normalized.ScreenshotRetentionDays);
    }

    [Fact]
    public void NormalizePersisted_UsesAllDayEveryDayOnFirstRun()
    {
        var normalized = SettingsCatalog.NormalizePersisted(
            new AppSettings(),
            Path.Combine(Path.GetTempPath(), "WorkTrail", "screenshots"));

        Assert.All(normalized.ActiveHours!, day =>
        {
            Assert.Equal("00:00-24:00", day.ActivePeriod);
            Assert.Empty(day.BreakPeriods);
        });
    }

    [Fact]
    public void ActiveHours_AllDayEndBoundaryIncludesTheFinalSlot()
    {
        var schedule = ActiveHoursSchedule.Normalize(null);

        Assert.True(ActiveHoursSchedule.HasAnyActivePeriod(schedule));
        Assert.True(ActiveHoursSchedule.IsWithinActiveHours(
            schedule,
            new DateTimeOffset(2026, 8, 3, 23, 59, 0, TimeSpan.FromHours(7))));
    }

    [Fact]
    public void NormalizePersisted_PreservesAnExplicitlyClearedSchedule()
    {
        var cleared = ActiveHoursSchedule.Days.Select(day => new ActiveHoursDay(day)).ToArray();
        var normalized = SettingsCatalog.NormalizePersisted(
            new AppSettings(ActiveHours: cleared),
            Path.Combine(Path.GetTempPath(), "WorkTrail", "screenshots"));

        Assert.False(ActiveHoursSchedule.HasAnyActivePeriod(normalized.ActiveHours));
        Assert.All(normalized.ActiveHours!, day => Assert.Empty(day.ActivePeriod));
    }

    /// <summary>Verifies that persisted active-hours boundaries outside the supported grid are rejected.</summary>
    [Fact]
    public void NormalizePersisted_RejectsUnsupportedActiveHoursBoundaries()
    {
        var settings = new AppSettings(ActiveHours: [new ActiveHoursDay("monday", "09:10-18:45")]);

        var exception = Assert.Throws<InvalidDataException>(() => SettingsCatalog.NormalizePersisted(
            settings,
            Path.Combine(Path.GetTempPath(), "WorkTrail", "screenshots")));

        Assert.Contains("15-minute increments", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("0123456789abcdef0123456789abcdef_1.2.3_manual_monitor-1.webp", true)]
    [InlineData("0123456789abcdef0123456789abcdef_1.2.3_scheduled_active-window-raw.webp", true)]
    [InlineData("0123456789abcdef0123456789abcdef_1.2.3_manual_monitor-2.png", true)]
    [InlineData("0123456789abcdef0123456789abcdef_1.2.3_monitor-1.webp", false)]
    [InlineData("family-photo.webp", false)]
    [InlineData("0123456789abcdef0123456789abcdef_notes.webp", false)]
    [InlineData("0123456789abcdef0123456789abcdef_1.2.3_monitor-0.webp", false)]
    public void ScreenshotOwnership_IsFailClosed(string fileName, bool expected)
    {
        Assert.Equal(expected, ScreenCaptureService.IsOwnedArtifact(fileName));
    }

    [Fact]
    public void ScreenshotRetention_UsesPersistedCaptureTimeInsteadOfFileModificationTime()
    {
        var dataDirectory = Path.Combine(Path.GetTempPath(), "WorkTrail.Tests", Guid.NewGuid().ToString("N"));
        try
        {
            var store = new LocalStore(dataDirectory);
            var settings = store.LoadSettings();
            var captureId = Guid.NewGuid().ToString("N");
            var screenshotPath = Path.Combine(
                dataDirectory,
                $"{captureId}_1.2.3_manual_monitor-1.webp");
            File.WriteAllBytes(screenshotPath, [1, 2, 3]);
            File.SetLastWriteTimeUtc(screenshotPath, DateTime.UtcNow);
            var capturedAt = DateTimeOffset.UtcNow.AddDays(-60);
            store.RegisterScreenshotCapture(
                captureId,
                settings.InstallationId,
                capturedAt,
                ScreenshotCaptureOrigins.Manual);

            var timestamps = store.LoadScreenshotCaptureTimes([screenshotPath], CancellationToken.None);

            Assert.Equal(capturedAt.UtcDateTime.Ticks, timestamps[screenshotPath].UtcDateTime.Ticks);
            Assert.True(File.GetLastWriteTimeUtc(screenshotPath) > capturedAt.UtcDateTime);
        }
        finally
        {
            if (Directory.Exists(dataDirectory))
            {
                Directory.Delete(dataDirectory, recursive: true);
            }
        }
    }

    [Fact]
    public void ScreenshotRetention_PrunesOnlyExpiredUnreferencedCaptureProvenance()
    {
        var dataDirectory = Path.Combine(Path.GetTempPath(), "WorkTrail.Tests", Guid.NewGuid().ToString("N"));
        try
        {
            var store = new LocalStore(dataDirectory);
            var installationId = store.LoadSettings().InstallationId;
            var expiredAt = DateTimeOffset.UtcNow.AddDays(-60);
            var retainedAt = DateTimeOffset.UtcNow;
            var expiredOrphanId = Guid.NewGuid().ToString("N");
            var expiredReferencedId = Guid.NewGuid().ToString("N");
            var expiredJobReferencedId = Guid.NewGuid().ToString("N");
            var retainedOrphanId = Guid.NewGuid().ToString("N");
            var expiredOrphanPath = ScreenshotPath(expiredOrphanId);
            var expiredReferencedPath = ScreenshotPath(expiredReferencedId);
            var expiredJobReferencedPath = ScreenshotPath(expiredJobReferencedId);
            var retainedOrphanPath = ScreenshotPath(retainedOrphanId);

            store.RegisterScreenshotCapture(expiredOrphanId, installationId, expiredAt, ScreenshotCaptureOrigins.Manual);
            store.RegisterScreenshotCapture(expiredReferencedId, installationId, expiredAt, ScreenshotCaptureOrigins.Manual);
            store.RegisterScreenshotCapture(expiredJobReferencedId, installationId, expiredAt, ScreenshotCaptureOrigins.Manual);
            store.RegisterScreenshotCapture(retainedOrphanId, installationId, retainedAt, ScreenshotCaptureOrigins.Manual);
            store.UpsertScreenshotIntervalTelemetry(
                expiredReferencedId,
                [expiredReferencedPath],
                new ScreenshotIntervalTelemetry(expiredAt.AddMinutes(-5), expiredAt, null, null));
            var jobId = Guid.NewGuid();
            store.CreateAiReprocessJob(
                new AiReprocessJobRecord(
                    jobId,
                    expiredAt,
                    expiredAt,
                    expiredAt.AddHours(-1),
                    expiredAt.AddHours(1),
                    DateOnly.FromDateTime(expiredAt.Date),
                    ScreenshotCaptureOrigins.Manual,
                    "retention-test",
                    "pending",
                    1,
                    1,
                    null),
                [new AiReprocessJobItemRecord(
                    jobId,
                    expiredJobReferencedId,
                    0,
                    expiredAt,
                    ScreenshotCaptureOrigins.Manual,
                    [Path.GetFileNameWithoutExtension(expiredJobReferencedPath)],
                    1,
                    "pending",
                    0,
                    null,
                    expiredAt)]);

            Assert.Equal(1, store.PruneOrphanedScreenshotCaptures(DateTimeOffset.UtcNow.AddDays(-30)));
            var firstPass = store.LoadScreenshotCaptureTimes(
                [expiredOrphanPath, expiredReferencedPath, expiredJobReferencedPath, retainedOrphanPath],
                CancellationToken.None);
            Assert.DoesNotContain(expiredOrphanPath, firstPass.Keys);
            Assert.Contains(expiredReferencedPath, firstPass.Keys);
            Assert.Contains(expiredJobReferencedPath, firstPass.Keys);
            Assert.Contains(retainedOrphanPath, firstPass.Keys);

            Assert.Equal(1, store.DeleteScreenshotIntervalTelemetry(expiredReferencedPath));
            store.TransitionAiReprocessJob(jobId, "completed", null, expiredAt);
            Assert.Equal(1, store.PruneTerminalAiReprocessJobs(DateTimeOffset.UtcNow.AddDays(-30)));
            Assert.Equal(2, store.PruneOrphanedScreenshotCaptures(DateTimeOffset.UtcNow.AddDays(-30)));
            var secondPass = store.LoadScreenshotCaptureTimes(
                [expiredReferencedPath, expiredJobReferencedPath, retainedOrphanPath],
                CancellationToken.None);
            Assert.DoesNotContain(expiredReferencedPath, secondPass.Keys);
            Assert.DoesNotContain(expiredJobReferencedPath, secondPass.Keys);
            Assert.Contains(retainedOrphanPath, secondPass.Keys);

            string ScreenshotPath(string captureId) => Path.Combine(
                dataDirectory,
                $"{captureId}_1.2.3_manual_monitor-1.webp");
        }
        finally
        {
            if (Directory.Exists(dataDirectory))
            {
                Directory.Delete(dataDirectory, recursive: true);
            }
        }
    }

    [Fact]
    public async Task ConcurrentFirstLaunch_UsesOneInstallationIdentity()
    {
        var dataDirectory = Path.Combine(Path.GetTempPath(), "WorkTrail.Tests", Guid.NewGuid().ToString("N"));
        try
        {
            var loads = Enumerable.Range(0, 16)
                .Select(_ => Task.Run(() => new LocalStore(dataDirectory).LoadSettings().InstallationId));

            var installationIds = await Task.WhenAll(loads);
            var persisted = JsonSerializer.Deserialize<AppSettings>(
                await File.ReadAllTextAsync(Path.Combine(dataDirectory, "appsettings.json")),
                new JsonSerializerOptions(JsonSerializerDefaults.Web));

            Assert.Single(installationIds.Distinct(StringComparer.Ordinal));
            Assert.False(string.IsNullOrWhiteSpace(installationIds[0]));
            Assert.Equal(installationIds[0], persisted?.InstallationId);
        }
        finally
        {
            if (Directory.Exists(dataDirectory))
            {
                Directory.Delete(dataDirectory, recursive: true);
            }
        }
    }

    [Fact]
    public void Settings_FailFastWhenPersistedJsonIsMalformed()
    {
        var dataDirectory = Path.Combine(Path.GetTempPath(), "WorkTrail.Tests", Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(dataDirectory);
            File.WriteAllText(Path.Combine(dataDirectory, "appsettings.json"), "{ invalid json");

            Assert.Throws<JsonException>(() => new LocalStore(dataDirectory).LoadSettings());
            Assert.True(File.Exists(Path.Combine(dataDirectory, "appsettings.json")));
        }
        finally
        {
            if (Directory.Exists(dataDirectory))
            {
                Directory.Delete(dataDirectory, recursive: true);
            }
        }
    }

    [Fact]
    public void DataRetention_RemovesOnlyExpiredActivityRowsFromSQLite()
    {
        var dataDirectory = Path.Combine(Path.GetTempPath(), "WorkTrail.Tests", Guid.NewGuid().ToString("N"));
        try
        {
            var store = new LocalStore(dataDirectory);
            store.AppendSample(Sample(DateTimeOffset.UtcNow.AddDays(-10), "expired"));
            store.AppendSample(Sample(DateTimeOffset.UtcNow, "current"));

            var cutoff = DateTimeOffset.UtcNow.AddDays(-1);
            var databasePath = Path.Combine(dataDirectory, "activity.sqlite3");
            Assert.Contains(databasePath, store.GetRetentionCandidates(cutoff));
            var preview = store.GetRetentionPreview(cutoff);
            Assert.Equal(1, preview.RecordCount);
            Assert.True(preview.TotalBytes > 0);

            var removed = store.ApplyRetention(cutoff);

            Assert.Equal(1, removed);
            Assert.True(File.Exists(databasePath));
            Assert.False(File.Exists(Path.Combine(dataDirectory, "activity.jsonl")));
            Assert.Equal("current", store.LoadLatestSample()?.Context);
            Assert.Empty(store.GetRetentionCandidates(cutoff));
        }
        finally
        {
            if (Directory.Exists(dataDirectory))
            {
                Directory.Delete(dataDirectory, recursive: true);
            }
        }

        static ActivitySample Sample(DateTimeOffset timestamp, string context) => new(
            timestamp,
            5,
            "active",
            "test",
            "Test",
            context,
            "Test window",
            "test-installation",
            0,
            0);
    }

    [Fact]
    public void ActivityMonitor_PropagatesPersistenceFailures()
    {
        var dataDirectory = Path.Combine(Path.GetTempPath(), "WorkTrail.Tests", Guid.NewGuid().ToString("N"));
        try
        {
            var store = new LocalStore(dataDirectory);
            using var hooks = new InputHookService();
            using var monitor = new ActivityMonitorService(store, hooks);
            using (var connection = new SqliteConnection($"Data Source={Path.Combine(dataDirectory, "activity.sqlite3")};Pooling=False"))
            {
                connection.Open();
                using var command = connection.CreateCommand();
                command.CommandText = "DROP TABLE activity_samples;";
                command.ExecuteNonQuery();
            }

            var sample = new ActivitySample(
                DateTimeOffset.UtcNow,
                5,
                "active",
                "test",
                "Test",
                "context",
                "window",
                "test-installation",
                0,
                0);

            Assert.Throws<SqliteException>(() => monitor.PersistSample(sample));
            Assert.Null(monitor.CurrentSample);
        }
        finally
        {
            if (Directory.Exists(dataDirectory))
            {
                Directory.Delete(dataDirectory, recursive: true);
            }
        }
    }

    /// <summary>Verifies that timer persistence failures are contained and reported once as degraded health.</summary>
    [Fact]
    public void ActivityMonitor_TimerPersistenceFailure_IsContainedAndReportsDegradedHealthOnce()
    {
        var dataDirectory = Path.Combine(Path.GetTempPath(), "WorkTrail.Tests", Guid.NewGuid().ToString("N"));
        try
        {
            var store = new LocalStore(dataDirectory);
            using var tracking = new TrackingDomainService(store);
            var persisted = new ActivitySample(
                DateTimeOffset.UtcNow.AddSeconds(-5),
                5,
                "active",
                "test",
                "Test",
                "persisted",
                "window",
                "test-installation",
                1,
                0);
            var rejected = persisted with { Timestamp = DateTimeOffset.UtcNow, Context = "rejected" };
            var changes = new List<TrackingRuntimeHealth>();
            tracking.RuntimeHealthChanged += changes.Add;

            Assert.True(tracking.TryPersistActivitySample(persisted));
            using (var connection = new SqliteConnection($"Data Source={Path.Combine(dataDirectory, "activity.sqlite3")};Pooling=False"))
            {
                connection.Open();
                using var command = connection.CreateCommand();
                command.CommandText = "DROP TABLE activity_samples;";
                command.ExecuteNonQuery();
            }

            Assert.False(tracking.TryPersistActivitySample(rejected));
            Assert.False(tracking.TryPersistActivitySample(rejected));

            var health = tracking.RuntimeHealth;
            Assert.True(health.IsDegraded);
            Assert.Equal("tracking.persistence.failed", health.StatusCode);
            Assert.Equal(persisted.Timestamp, health.LastPersistedSampleAt);
            Assert.NotNull(health.LastPersistenceFailureAt);
            var transition = Assert.Single(changes);
            Assert.True(transition.IsDegraded);
        }
        finally
        {
            if (Directory.Exists(dataDirectory))
            {
                Directory.Delete(dataDirectory, recursive: true);
            }
        }
    }

}
