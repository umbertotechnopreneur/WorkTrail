// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using WorkTrail.Application;
using WorkTrail.Runtime;
using WorkTrail.Services;
using Xunit;

namespace WorkTrail.Core.Tests;

/// <summary>Protects the separation between desktop widgets and existing personal activity data.</summary>
public sealed class WidgetRuntimeTests
{
    #region Implementation

    /// <summary>Widget startup succeeds with unreadable legacy history and leaves those files unchanged.</summary>
    [Fact]
    public async Task Startup_DoesNotOpenOrModifyExistingHistory()
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "widget-data", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var originals = new Dictionary<string, byte[]>
            {
                ["activity.sqlite3"] = [0, 255, 42, 17],
                ["activity.jsonl"] = [13, 37, 255],
                ["analyses.jsonl"] = [0, 7, 99]
            };
            foreach (var item in originals)
            {
                File.WriteAllBytes(Path.Combine(directory, item.Key), item.Value);
            }
            Directory.CreateDirectory(Path.Combine(directory, "screenshots"));
            var screenshot = Path.Combine(directory, "screenshots", "existing.png");
            File.WriteAllBytes(screenshot, [137, 80, 78, 71]);
            var initial = Directory.GetFiles(directory, "*", SearchOption.AllDirectories).OrderBy(path => path).ToArray();

            await using (var application = new WorkTrailApplication(new LocalStore(directory)))
            {
                var health = await application.GetRuntimeHealthAsync(CancellationToken.None);

                Assert.True(health.Succeeded);
                Assert.DoesNotContain("tracking", health.Value!.Capabilities);
                Assert.DoesNotContain("screenshots", health.Value.Capabilities);

                var settings = await application.GetSettingsAsync(CancellationToken.None);

                Assert.True(settings.Succeeded);
            }

            foreach (var item in originals)
            {
                Assert.Equal(item.Value, File.ReadAllBytes(Path.Combine(directory, item.Key)));
            }

            Assert.Equal(new byte[] { 137, 80, 78, 71 }, File.ReadAllBytes(screenshot));
            Assert.Equal(initial, Directory.GetFiles(directory, "*", SearchOption.AllDirectories)
                .Where(path => Path.GetFileName(path) != "appsettings.json").OrderBy(path => path).ToArray());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>Saving essential preferences keeps selected cities and independent window placement.</summary>
    [Fact]
    public async Task PreferenceChanges_PreserveCitiesAndWidgetSession()
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "widget-data", Guid.NewGuid().ToString("N"));
        try
        {
            var store = new LocalStore(directory);
            var placement = new WindowState(100, 200, 480, 350, "monitor");
            store.SaveSettings(store.LoadSettings() with
            {
                WorldClockCityIds = ["tokyo", "london"],
                WindowStates = new Dictionary<string, WindowState> { [WindowStateKeys.WorldClocks] = placement }
            });
            await using var application = new WorkTrailApplication(store);
            var theme = application.PatchSettingsAsync(new SettingsPatch(new Dictionary<string, string?> { ["theme"] = "dark" }), CancellationToken.None);
            var visibility = application.SetWindowOpenStateAsync(WindowStateKeys.WorldMap, true, CancellationToken.None);
            await Task.WhenAll(theme, visibility);

            Assert.True((await theme).Succeeded);
            Assert.True((await visibility).Succeeded);

            var saved = store.LoadSettings();

            Assert.Equal(new[] { "tokyo", "london" }, saved.WorldClockCityIds);
            Assert.Equal(placement, saved.WindowStates![WindowStateKeys.WorldClocks]);
            Assert.True(saved.WindowOpenStates![WindowStateKeys.WorldMap]);
            Assert.Equal("dark", saved.Theme);

            var invalid = await application.PatchSettingsAsync(new SettingsPatch(new Dictionary<string, string?>
            { ["theme"] = "light", ["ai.enabled"] = "true" }), CancellationToken.None);

            Assert.False(invalid.Succeeded);
            Assert.Equal("dark", store.LoadSettings().Theme);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    /// <summary>Removed command-line entry points cannot start data collection.</summary>
    [Theory]
    [InlineData("--start-tracking")]
    [InlineData("--paused")]
    [InlineData("--cli")]
    public void RemovedLaunchArguments_AreRejected(string argument) =>
        Assert.Throws<ArgumentException>(() => LaunchOptions.Parse([argument]));

    #endregion
}
