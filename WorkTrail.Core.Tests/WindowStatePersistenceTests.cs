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
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using WorkTrail.Application;
using WorkTrail.Services;
using Xunit;

namespace WorkTrail.Core.Tests;

public sealed class WindowStatePersistenceTests
{
    /// <summary>Verifies saving returns committed settings and restoring geometry cannot reveal a hidden window.</summary>
    [Fact]
    public void RestorePlacement_PreservesAHiddenNativeWindow()
    {
        var directory = CreateTemporaryDirectory();
        var handle = IntPtr.Zero;
        try
        {
            var store = new LocalStore(directory);
            var mapBounds = new WindowState(120, 140, 960, 540, "display-one");
            store.SaveSettings(store.LoadSettings() with
            {
                Theme = "dark",
                WindowStates = new Dictionary<string, WindowState> { [WindowStateKeys.WorldMap] = mapBounds }
            });
            var service = new WindowStateService(store);
            handle = CreateWindowEx(0, "STATIC", "", 0x80000000, 160, 180, 960, 540,
                IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
            Assert.NotEqual(IntPtr.Zero, handle);
            Assert.False(IsWindowVisible(handle));
            var (savedState, savedSettings) = service.Save(WindowStateKeys.Main, handle.ToInt64());

            Assert.Equal(savedState, savedSettings.WindowStates![WindowStateKeys.Main]);
            Assert.Equal(savedState, new LocalStore(directory).LoadSettings().WindowStates![WindowStateKeys.Main]);
            Assert.Equal(mapBounds, savedSettings.WindowStates[WindowStateKeys.WorldMap]);
            Assert.Equal("dark", savedSettings.Theme);

            var restored = service.Restore(WindowStateKeys.Main, handle.ToInt64());

            Assert.NotNull(restored);
            Assert.False(IsWindowVisible(handle));
        }
        finally
        {
            if (handle != IntPtr.Zero) Assert.True(DestroyWindow(handle));
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>Verifies that opening and closing one surface preserves every other surface and its geometry.</summary>
    [Fact]
    public void OpenStates_RoundTripWithoutReplacingOtherWindowsOrPlacements()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var store = new LocalStore(directory);
            var mapBounds = new WindowState(120, 140, 960, 540, "display-one");
            store.SaveSettings(store.LoadSettings() with
            {
                WindowStates = new Dictionary<string, WindowState> { [WindowStateKeys.WorldMap] = mapBounds }
            });
            var service = new WindowStateService(store);

            service.SetOpenState(WindowStateKeys.WorldMap, true);
            service.SetOpenState(WindowStateKeys.LunarPhase, true);
            service.SetOpenState(WindowStateKeys.WorldClocks, true);
            service.SetOpenState(WindowStateKeys.WorldClocks, false);

            var restored = new LocalStore(directory).LoadSettings();
            Assert.NotNull(restored.WindowOpenStates);
            Assert.True(restored.WindowOpenStates[WindowStateKeys.WorldMap]);
            Assert.True(restored.WindowOpenStates[WindowStateKeys.LunarPhase]);
            Assert.False(restored.WindowOpenStates[WindowStateKeys.WorldClocks]);
            Assert.Equal(mapBounds, restored.WindowStates![WindowStateKeys.WorldMap]);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>Verifies that an unsupported window key never becomes a silently ignored session entry.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("unknown-window")]
    public void SetOpenState_RejectsUnsupportedKeysWithoutWriting(string key)
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var store = new LocalStore(directory);

            Assert.Throws<ArgumentException>(() => new WindowStateService(store).SetOpenState(key, true));

            Assert.Null(store.LoadSettings().WindowOpenStates);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>Verifies that unsupported persisted session keys fail before application startup uses them.</summary>
    [Fact]
    public void PersistedOpenStates_RejectUnsupportedKeys()
    {
        var settings = new AppSettings(
            WindowOpenStates: new Dictionary<string, bool> { ["unknown-window"] = true });

        Assert.Throws<ArgumentException>(() => SettingsCatalog.NormalizePersisted(settings, Path.GetTempPath()));
    }

    /// <summary>Verifies that an OCR context cannot silently resolve to an unrelated or ambiguous screenshot.</summary>
    [Fact]
    public void OcrTextSource_RejectsRelativePathsAndMissingCaptureTimes()
    {
        var capturedAt = new DateTimeOffset(2026, 9, 15, 12, 30, 0, TimeSpan.Zero);
        var relative = new AppSettings(OcrTextWindowSource: new OcrTextWindowSource("capture.webp", capturedAt));
        var missingTime = new AppSettings(OcrTextWindowSource: new OcrTextWindowSource(Path.Combine(Path.GetTempPath(), "capture.webp"), default));

        Assert.Throws<ArgumentException>(() => SettingsCatalog.NormalizePersisted(relative, Path.GetTempPath()));
        Assert.Throws<ArgumentException>(() => SettingsCatalog.NormalizePersisted(missingTime, Path.GetTempPath()));
    }

    /// <summary>Guards against later settings changes overwriting geometry and session flags from a stale runtime snapshot.</summary>
    [Fact]
    public async Task WindowPersistence_SurvivesAnUnrelatedSettingsPatch()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var store = new LocalStore(directory);
            var utilities = new UtilityService();
            var capture = new ScreenCaptureService(utilities.GetAppVersion());
            await using var application = new WorkTrailApplication(
                store,
                utilities,
                new TrackingDomainService(store),
                capture,
                new FakeHardwareTelemetryService(),
                new OpenAiAnalysisService(store, capture),
                new StartupService(),
                new BuildInformationService(),
                startScheduledSnapshotTimer: false);
            var saved = SaveHiddenTestWindow(application);
            Assert.True(saved.Succeeded);
            Assert.NotNull(saved.Value);
            var savedGeometry = await application.GetSettingsAsync(CancellationToken.None);
            Assert.NotNull(savedGeometry.Value?.WindowStates);
            Assert.Equal(saved.Value, savedGeometry.Value.WindowStates[WindowStateKeys.WorldMap]);
            Assert.True((await application.SetWindowOpenStateAsync(WindowStateKeys.WorldMap, true, CancellationToken.None)).Succeeded);
            Assert.True((await application.SetWindowOpenStateAsync(WindowStateKeys.WorldClocks, false, CancellationToken.None)).Succeeded);
            var savedVisibility = await application.GetSettingsAsync(CancellationToken.None);
            Assert.NotNull(savedVisibility.Value?.WindowOpenStates);
            Assert.True(savedVisibility.Value.WindowOpenStates[WindowStateKeys.WorldMap]);
            Assert.False(savedVisibility.Value.WindowOpenStates[WindowStateKeys.WorldClocks]);
            var source = new OcrTextWindowSource(Path.Combine(directory, "capture.webp"), new DateTimeOffset(2026, 9, 15, 12, 30, 0, TimeSpan.Zero));
            var sourceSaved = await application.SetOcrTextWindowSourceAsync(source.ScreenshotPath, source.CapturedAt, CancellationToken.None);
            Assert.True(sourceSaved.Succeeded);
            var current = await application.GetSettingsAsync(CancellationToken.None);
            Assert.Equal(saved.Value, current.Value!.WindowStates![WindowStateKeys.WorldMap]);
            Assert.True(current.Value.WindowOpenStates![WindowStateKeys.WorldMap]);
            Assert.Equal(source, current.Value.OcrTextWindowSource);

            var patched = await application.PatchSettingsAsync(
                new SettingsPatch(new Dictionary<string, string?> { ["theme"] = "dark" }),
                CancellationToken.None);
            Assert.True(patched.Succeeded);

            var persisted = new LocalStore(directory).LoadSettings();
            Assert.Equal("dark", persisted.Theme);
            Assert.Equal(saved.Value, persisted.WindowStates![WindowStateKeys.WorldMap]);
            Assert.True(persisted.WindowOpenStates![WindowStateKeys.WorldMap]);
            Assert.False(persisted.WindowOpenStates[WindowStateKeys.WorldClocks]);
            Assert.Equal(source, persisted.OcrTextWindowSource);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static OperationResult<WindowState> SaveHiddenTestWindow(IWorkTrailApplication application)
    {
        // Keep native creation, capture, and destruction on one thread without displaying or activating the surface.
        var handle = CreateWindowEx(0, "STATIC", "", 0x80000000, 160, 180, 960, 540,
            IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
        Assert.NotEqual(IntPtr.Zero, handle);
        try
        {
            return application.SaveWindowStateAsync(WindowStateKeys.WorldMap, handle.ToInt64(), CancellationToken.None).GetAwaiter().GetResult();
        }
        finally
        {
            Assert.True(DestroyWindow(handle));
        }
    }

    private static string CreateTemporaryDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "WorkTrail.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    [DllImport("user32.dll", EntryPoint = "CreateWindowExW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateWindowEx(uint extendedStyle, string className, string windowName, uint style,
        int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr parameter);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyWindow(IntPtr handle);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr handle);
}
