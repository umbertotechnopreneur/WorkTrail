// SPDX-License-Identifier: MIT

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
