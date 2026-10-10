// SPDX-License-Identifier: MIT
/* VBWR B
 *
 * Project: WorkTrail
 * Repository: https://github.com/umbertotechnopreneur/WorkTrail
 * Creator: Umberto Giacobbi | https://umbertogiacobbi.biz
 *
 * VibeWare: Human intent, AI execution, and plenty of tokens
 * Manifesto: https://umbertogiacobbi.biz/vibeware/manifesto
 *
 * Modified with AI: OpenAI Codex; added this header on 2026-10-10.
 * Human guidance: Umberto Giacobbi; requested VibeWare branding.
 *
 * Copyright (c) 2026 Umberto Giacobbi
 * License: MIT - see LICENSE
 *
 * VBWR E */


using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Xml.Linq;
using WorkTrail.Application;
using WorkTrail.Services;
using Xunit;

namespace WorkTrail.Core.Tests;

public sealed class ScreenshotNotificationTests
{
    /// <summary>The global pause persists independently of capture, tracking startup, and per-screenshot notices.</summary>
    [Fact]
    public void GlobalNotificationPausePreservesCaptureAndScreenshotPreference()
    {
        var settings = new AppSettings(ScreenshotsEnabled: true, KeepScreenshots: true, StartTrackingOnLaunch: true);
        var result = SettingsCatalog.Apply(settings,
            new SettingsPatch(new Dictionary<string, string?> { ["notifications.enabled"] = "false" }));
        Assert.True(result.Succeeded);
        var persisted = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(result.Value));
        Assert.NotNull(persisted);
        Assert.False(persisted.NotificationsEnabled);
        Assert.True(persisted.ScreenshotNotificationsEnabled);
        Assert.True(persisted.ScreenshotsEnabled);
        Assert.True(persisted.KeepScreenshots);
        Assert.True(persisted.StartTrackingOnLaunch);
        var resumed = SettingsCatalog.Apply(persisted,
            new SettingsPatch(new Dictionary<string, string?> { ["notifications.enabled"] = "true" }));
        Assert.True(resumed.Succeeded);
        Assert.True(resumed.Value!.NotificationsEnabled);
        Assert.Equal(settings.ScreenshotNotificationsEnabled, resumed.Value.ScreenshotNotificationsEnabled);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("worktrail://notifications/screenshots/disable?path=C:/private.png")]
    [InlineData("worktrail://notifications/screenshots/disable/other")]
    [InlineData("worktrail://notifications/screenshots/delete")]
    [InlineData("https://notifications/screenshots/disable")]
    // uri models externally supplied protocol input rather than a trusted toast payload.
    public void ProtocolRejectsAnythingExceptFixedActions(string? uri)
    {
        Assert.False(ScreenshotNotificationActivation.IsSupported(uri));
        Assert.True(ScreenshotNotificationActivation.IsSupported(ScreenshotNotificationActivation.Open));
        Assert.True(ScreenshotNotificationActivation.IsSupported(ScreenshotNotificationActivation.Disable));
    }

    [Theory]
    [InlineData("https://example.invalid/private.png")]
    [InlineData("ms-appdata:///local/private.png")]
    [InlineData("ms-appdata:///temp/screenshot-notifications/../private.png")]
    // uri attempts to make the notification expose an image outside its bounded thumbnail cache.
    public void NotificationRejectsExternalAndUnownedImages(string uri)
    {
        Assert.Throws<ArgumentException>(() => ScreenshotNotificationService.CreateContent(new LocalizationService("en-US"), uri));
    }

    [Fact]
    public void EveryLanguageHasAnImageAndAPersistedDisableAction()
    {
        var imageUri = "ms-appdata:///temp/screenshot-notifications/" + Guid.NewGuid().ToString("N") + ".png";
        foreach (var language in LocalizationService.SupportedLanguages)
        {
            var strings = new LocalizationService(language);
            var xml = XDocument.Parse(ScreenshotNotificationService.CreateContent(strings, imageUri).GetXml());
            Assert.Equal(ScreenshotNotificationActivation.Open, (string?)xml.Root!.Attribute("launch"));
            Assert.Equal(imageUri, (string?)xml.Descendants("image").Single().Attribute("src"));
            var action = xml.Descendants("action").Single();
            Assert.Equal("protocol", (string?)action.Attribute("activationType"));
            Assert.Equal(ScreenshotNotificationActivation.Disable, (string?)action.Attribute("arguments"));
            Assert.Equal(strings.Translate("Notification.ScreenshotCaptured.Disable"), (string?)action.Attribute("content"));
        }
    }

    [Fact]
    public void DisablingNotificationsPreservesCaptureAndRetentionPreferences()
    {
        var settings = new AppSettings(ScreenshotsEnabled: true, KeepScreenshots: true);
        var result = SettingsCatalog.Apply(settings,
            new SettingsPatch(new Dictionary<string, string?> { ["screenshots.notifications"] = "false" }));
        Assert.True(result.Succeeded);
        var persisted = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(result.Value));
        Assert.NotNull(persisted);
        Assert.False(persisted.ScreenshotNotificationsEnabled);
        Assert.True(persisted.ScreenshotsEnabled);
        Assert.True(persisted.KeepScreenshots);
        Assert.Equal(settings.DataRetentionDays, persisted.DataRetentionDays);
        Assert.Equal(settings.ScreenshotRetentionDays, persisted.ScreenshotRetentionDays);
    }
}
