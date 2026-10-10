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
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using WorkTrail.Application;
using WorkTrail.Ocr;
using WorkTrail.Services;
using Xunit;

namespace WorkTrail.Core.Tests;

[Collection(ProcessEnvironmentCollection.Name)]
public sealed class RuntimeCaptureSafetyTests
{
    /// <summary>Each snapshot notifies once for its focused target, while all requested monitor artifacts remain available.</summary>
    /// <param name="mode">Whether the snapshot contains all screens or only the active window.</param>
    /// <param name="keep">Whether the snapshot retains images after the pipeline completes.</param>
    /// <param name="isVip">Whether the retained snapshot is marked VIP.</param>
    [Theory]
    [InlineData("all-screens", true, false)]
    [InlineData("all-screens", false, false)]
    [InlineData("all-screens", true, true)]
    [InlineData("active-window", true, false)]
    [InlineData("active-window", false, false)]
    public async Task CaptureNotification_ShowsFocusedImageOncePerSnapshot(string mode, bool keep, bool isVip)
    {
        var dataDirectory = CreateTemporaryDirectory();
        try
        {
            var store = new LocalStore(dataDirectory);
            var settings = store.LoadSettings() with
            {
                ScreenshotsEnabled = true,
                ScreenshotDirectory = dataDirectory,
                OpenAiEnabled = false,
                NotificationsEnabled = true,
                ScreenshotNotificationsEnabled = true
            };
            store.SaveSettings(settings);
            var notifications = new RecordingScreenshotNotifications();
            await using var application = CreateApplication(store, new SettingsSnapshot(settings),
                new NotificationCaptureService(keep), startTimer: false, screenshotNotifications: notifications);

            for (var snapshotIndex = 0; snapshotIndex < 2; snapshotIndex++)
            {
                var result = await application.CaptureScreenshotAsync(
                    new(mode, keep, ScreenshotCaptureOrigins.Manual, DeferAiAnalysis: true, IsVip: isVip), CancellationToken.None);
                Assert.True(result.Succeeded);
                var capture = Assert.IsType<ScreenshotCaptureResult>(result.Value);
                Assert.Equal(mode == "all-screens" ? 2 : 1, capture.AnalysisScreenshotPaths.Count);
                Assert.Equal(snapshotIndex + 1, notifications.Paths.Count);
                Assert.Equal(capture.AnalysisScreenshotPaths[0], notifications.Paths[snapshotIndex]);
                Assert.EndsWith(mode == "all-screens" ? "_monitor-2.webp" : "_active-window.webp", notifications.Paths[snapshotIndex]);
                if (keep) Assert.All(capture.StoredScreenshotPaths, path => Assert.True(File.Exists(path)));
            }
            Assert.NotEqual(notifications.Paths[0], notifications.Paths[1]);
        }
        finally { await DeleteTemporaryDirectoryAsync(dataDirectory); }
    }

    /// <summary>Notification preferences suppress every preview without reducing a multi-monitor capture.</summary>
    /// <param name="notificationsEnabled">Whether global notifications are enabled.</param>
    /// <param name="screenshotNotificationsEnabled">Whether screenshot notifications are enabled.</param>
    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task CaptureNotification_RespectsNotificationPreferences(bool notificationsEnabled, bool screenshotNotificationsEnabled)
    {
        var dataDirectory = CreateTemporaryDirectory();
        try
        {
            var store = new LocalStore(dataDirectory);
            var settings = store.LoadSettings() with
            {
                ScreenshotsEnabled = true,
                ScreenshotDirectory = dataDirectory,
                OpenAiEnabled = false,
                NotificationsEnabled = notificationsEnabled,
                ScreenshotNotificationsEnabled = screenshotNotificationsEnabled
            };
            store.SaveSettings(settings);
            var notifications = new RecordingScreenshotNotifications();
            await using var application = CreateApplication(store, new SettingsSnapshot(settings),
                new NotificationCaptureService(keep: true), startTimer: false, screenshotNotifications: notifications);
            var result = await application.CaptureScreenshotAsync(
                new("all-screens", Keep: true, ScreenshotCaptureOrigins.Manual, DeferAiAnalysis: true), CancellationToken.None);

            Assert.True(result.Succeeded);
            var capture = Assert.IsType<ScreenshotCaptureResult>(result.Value);
            Assert.Equal(2, capture.StoredScreenshotPaths.Count);
            Assert.All(capture.StoredScreenshotPaths, path => Assert.True(File.Exists(path)));
            Assert.Empty(notifications.Paths);
        }
        finally { await DeleteTemporaryDirectoryAsync(dataDirectory); }
    }

    /// <summary>VIP importance and its user note survive reopening; deleting the last artifact removes its calendar indicator.</summary>
    [Fact]
    public async Task VipCapture_PersistsNoteAndClearsCalendarAfterDeletion()
    {
        var dataDirectory = CreateTemporaryDirectory();
        try
        {
            var store = new LocalStore(dataDirectory);
            var settings = store.LoadSettings() with { ScreenshotsEnabled = true, ScreenshotDirectory = dataDirectory, OpenAiEnabled = false };
            store.SaveSettings(settings);
            var capture = new BoundaryCaptureService(dataDirectory);
            await using var application = CreateApplication(store, new SettingsSnapshot(settings), capture, startTimer: false);
            var result = await application.CaptureScreenshotAsync(
                new("all-screens", Keep: true, ScreenshotCaptureOrigins.Manual, DeferAiAnalysis: true, IsVip: true), CancellationToken.None);
            Assert.True(result.Succeeded);
            var retained = Assert.IsType<ScreenshotCaptureResult>(result.Value);
            var note = "An important decision, recorded by me.";
            Assert.True((await application.SaveVipScreenshotNoteAsync(new(retained.CaptureId, note), CancellationToken.None)).Succeeded);
            var date = DateOnly.FromDateTime(retained.CapturedAt!.Value.LocalDateTime);
            var reopened = new LocalStore(dataDirectory);
            var item = Assert.Single(reopened.GetScreenshotGallery(date).Items);
            Assert.True(item.IsVip);
            Assert.Equal(ScreenshotCaptureOrigins.Manual, item.CaptureOrigin);
            Assert.Equal(note, item.UserNote);
            Assert.Contains(date, reopened.GetVipScreenshotDates(new(date, date), CancellationToken.None));
            Assert.True((await application.DeleteScreenshotAsync(item.Path, CancellationToken.None)).Succeeded);
            Assert.Empty(reopened.GetVipScreenshotDates(new(date, date), CancellationToken.None));
        }
        finally { await DeleteTemporaryDirectoryAsync(dataDirectory); }
    }

    /// <summary>Ordinary manual captures are never promoted by trying to save a VIP note.</summary>
    [Fact]
    public async Task OrdinaryCapture_RejectsVipNoteWithoutChangingImportance()
    {
        var dataDirectory = CreateTemporaryDirectory();
        try
        {
            var store = new LocalStore(dataDirectory);
            var settings = store.LoadSettings() with { ScreenshotsEnabled = true, ScreenshotDirectory = dataDirectory, OpenAiEnabled = false };
            store.SaveSettings(settings);
            await using var application = CreateApplication(store, new SettingsSnapshot(settings), new BoundaryCaptureService(dataDirectory), startTimer: false);
            var result = await application.CaptureScreenshotAsync(new("all-screens", true, ScreenshotCaptureOrigins.Manual, true), CancellationToken.None);
            Assert.True(result.Succeeded);
            var capture = Assert.IsType<ScreenshotCaptureResult>(result.Value);
            var saved = await application.SaveVipScreenshotNoteAsync(new(capture.CaptureId, "Not a VIP capture"), CancellationToken.None);
            Assert.False(saved.Succeeded);
            Assert.Equal("snapshot.vip.note.missing", saved.Code);
            Assert.False(Assert.Single(store.GetScreenshotGallery(DateOnly.FromDateTime(capture.CapturedAt!.Value.LocalDateTime)).Items).IsVip);
        }
        finally { await DeleteTemporaryDirectoryAsync(dataDirectory); }
    }

    /// <summary>Only retained manual captures can be marked VIP, before any pixel acquisition occurs.</summary>
    /// <param name="keep">Whether the request retains the capture.</param>
    /// <param name="origin">The requested capture origin.</param>
    [Theory]
    [InlineData(false, ScreenshotCaptureOrigins.Manual)]
    [InlineData(true, ScreenshotCaptureOrigins.Scheduled)]
    public async Task VipCapture_RejectsInvalidOriginOrRetention(bool keep, string origin)
    {
        var dataDirectory = CreateTemporaryDirectory();
        try
        {
            var store = new LocalStore(dataDirectory);
            var capture = new BoundaryCaptureService(dataDirectory);
            await using var application = CreateApplication(store, new SettingsSnapshot(store.LoadSettings()), capture, startTimer: false);
            var result = await application.CaptureScreenshotAsync(new("all-screens", keep, origin, true, true), CancellationToken.None);
            Assert.False(result.Succeeded);
            Assert.Equal("snapshot.vip.capture.invalid", result.Code);
            Assert.Equal(0, capture.PixelReadCount);
        }
        finally { await DeleteTemporaryDirectoryAsync(dataDirectory); }
    }

    /// <summary>Verifies that privacy rules are reevaluated immediately before pixels are read.</summary>
    [Fact]
    public async Task Capture_RechecksPrivacyImmediatelyBeforePixels()
    {
        var dataDirectory = CreateTemporaryDirectory();
        try
        {
            var store = new LocalStore(dataDirectory);
            var settings = store.LoadSettings() with
            {
                ScreenshotsEnabled = true,
                ScreenshotDirectory = dataDirectory,
                OpenAiEnabled = false
            };
            store.SaveSettings(settings);
            var snapshot = new SettingsSnapshot(settings);
            var capture = new BoundaryCaptureService(
                dataDirectory,
                beforeAuthorization: () => snapshot.Replace(snapshot.Value with
                {
                    PrivacyProcessNames = "private-rule|secret-app"
                }),
                context: new ScreenshotCaptureContext("secret-app", "Secret", "Private work", "Private window"));
            await using var application = CreateApplication(store, snapshot, capture, startTimer: false);

            var result = await application.CaptureScreenshotAsync(
                new CaptureScreenshotRequest("all-screens", Keep: true, CaptureOrigin: ScreenshotCaptureOrigins.Manual, DeferAiAnalysis: true),
                CancellationToken.None);

            Assert.False(result.Succeeded);
            Assert.Equal("privacy.blocked", result.Code);
            Assert.Equal(0, capture.PixelReadCount);
            Assert.False(File.Exists(capture.OutputPath));
        }
        finally
        {
            await DeleteTemporaryDirectoryAsync(dataDirectory);
        }
    }

    /// <summary>Verifies that disabling screenshots prevents the final pixel-read operation.</summary>
    [Fact]
    public async Task Capture_RechecksEnabledStateImmediatelyBeforePixels()
    {
        var dataDirectory = CreateTemporaryDirectory();
        try
        {
            var store = new LocalStore(dataDirectory);
            var settings = store.LoadSettings() with
            {
                ScreenshotsEnabled = true,
                ScreenshotDirectory = dataDirectory,
                OpenAiEnabled = false
            };
            store.SaveSettings(settings);
            var snapshot = new SettingsSnapshot(settings);
            var capture = new BoundaryCaptureService(
                dataDirectory,
                beforeAuthorization: () => snapshot.Replace(snapshot.Value with { ScreenshotsEnabled = false }),
                context: new ScreenshotCaptureContext("allowed-app", "Allowed", "Work", "Allowed window"));
            await using var application = CreateApplication(store, snapshot, capture, startTimer: false);

            var result = await application.CaptureScreenshotAsync(
                new CaptureScreenshotRequest("all-screens", Keep: true, CaptureOrigin: ScreenshotCaptureOrigins.Manual, DeferAiAnalysis: true),
                CancellationToken.None);

            Assert.False(result.Succeeded);
            Assert.Equal("screenshot.disabled", result.Code);
            Assert.Equal(0, capture.PixelReadCount);
            Assert.False(File.Exists(capture.OutputPath));
        }
        finally
        {
            await DeleteTemporaryDirectoryAsync(dataDirectory);
        }
    }

    /// <summary>Verifies that concurrent manual requests create only one owned pending capture.</summary>
    [Fact]
    public async Task ConcurrentManualCapture_CreatesOneOwnedPendingSnapshot()
    {
        var dataDirectory = CreateTemporaryDirectory();
        try
        {
            var store = new LocalStore(dataDirectory);
            var settings = store.LoadSettings() with
            {
                ScreenshotsEnabled = true,
                ScreenshotDirectory = dataDirectory,
                OpenAiEnabled = false
            };
            store.SaveSettings(settings);
            var snapshot = new SettingsSnapshot(settings);
            var capture = new BlockingCaptureService();
            await using var application = CreateApplication(store, snapshot, capture, startTimer: false);

            var first = application.CaptureManualScreenshotAsync(CancellationToken.None);
            await capture.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var second = application.CaptureManualScreenshotAsync(CancellationToken.None);
            capture.Release.Set();

            var firstResult = await first.WaitAsync(TimeSpan.FromSeconds(5));
            var secondResult = await second.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.True(firstResult.Succeeded);
            Assert.False(secondResult.Succeeded);
            Assert.Equal("snapshot.pending.exists", secondResult.Code);
            Assert.Equal(1, capture.CallCount);
            Assert.True(File.Exists(firstResult.Value?.ScreenshotPath));
        }
        finally
        {
            await DeleteTemporaryDirectoryAsync(dataDirectory);
        }
    }

    /// <summary>Verifies that cancellation removes captured files and releases pending ownership.</summary>
    [Fact]
    public async Task CancelledManualCapture_RemovesFilesAndLeavesNoPendingOwner()
    {
        var dataDirectory = CreateTemporaryDirectory();
        try
        {
            var store = new LocalStore(dataDirectory);
            var settings = store.LoadSettings() with
            {
                ScreenshotsEnabled = true,
                ScreenshotDirectory = dataDirectory,
                OcrEnabled = true,
                OpenAiEnabled = false
            };
            store.SaveSettings(settings);
            var snapshot = new SettingsSnapshot(settings);
            var capture = new BoundaryCaptureService(dataDirectory);
            var ocr = new CancellationAwareOcrService();
            await using var application = CreateApplication(
                store,
                snapshot,
                capture,
                startTimer: false,
                screenshotOcr: ocr);
            using var cancellation = new CancellationTokenSource();

            var operation = application.CaptureManualScreenshotAsync(cancellation.Token);
            await capture.Captured.Task.WaitAsync(TimeSpan.FromSeconds(5));
            cancellation.Cancel();
            var result = await operation.WaitAsync(TimeSpan.FromSeconds(5));
            var dashboard = await application.GetDashboardAsync(CancellationToken.None);

            Assert.False(result.Succeeded);
            Assert.Equal("operation.cancelled", result.Code);
            Assert.False(File.Exists(capture.OutputPath));
            Assert.Null(dashboard.Value?.PendingManualScreenshot);
        }
        finally
        {
            await DeleteTemporaryDirectoryAsync(dataDirectory);
        }
    }

    /// <summary>Verifies that contained persistence failures surface through health and notifications.</summary>
    [Fact]
    public async Task RuntimeHealthAndNotification_ReportContainedPersistenceFailure()
    {
        var dataDirectory = CreateTemporaryDirectory();
        try
        {
            var store = new LocalStore(dataDirectory);
            var settings = store.LoadSettings();
            var snapshot = new SettingsSnapshot(settings);
            var tracking = new TrackingDomainService(store, snapshot);
            await using var application = CreateApplication(
                store,
                snapshot,
                new BoundaryCaptureService(dataDirectory),
                startTimer: false,
                tracking: tracking);
            var persisted = Sample(DateTimeOffset.UtcNow.AddSeconds(-5), "persisted");

            Assert.True(tracking.TryPersistActivitySample(persisted));
            using (var connection = new SqliteConnection($"Data Source={Path.Combine(dataDirectory, "activity.sqlite3")};Pooling=False"))
            {
                connection.Open();
                using var command = connection.CreateCommand();
                command.CommandText = "DROP TABLE activity_samples;";
                command.ExecuteNonQuery();
            }

            Assert.False(tracking.TryPersistActivitySample(Sample(DateTimeOffset.UtcNow, "rejected")));
            var health = await application.GetRuntimeHealthAsync(CancellationToken.None);
            var notifications = await application.DrainApplicationNotificationsAsync(CancellationToken.None);

            Assert.True(health.Succeeded);
            Assert.True(health.Value?.Tracking?.IsDegraded);
            Assert.Equal(persisted.Timestamp, health.Value?.Tracking?.LastPersistedSampleAt);
            var notification = Assert.Single(notifications.Value ?? Array.Empty<ApplicationNotification>());
            Assert.Equal("tracking.persistence.failed", notification.Code);
            Assert.Equal(ApplicationNotificationSeverity.Warning, notification.Severity);
        }
        finally
        {
            await DeleteTemporaryDirectoryAsync(dataDirectory);
        }
    }

    /// <summary>Verifies that disposal waits for timer work owned by the application runtime.</summary>
    [Fact]
    public async Task DisposeAsync_WaitsForOwnedRuntimeTimerWork()
    {
        var dataDirectory = CreateTemporaryDirectory();
        WorkTrailApplication? application = null;
        try
        {
            var store = new LocalStore(dataDirectory);
            var settings = store.LoadSettings() with
            {
                ScreenshotsEnabled = true,
                ScreenshotDirectory = dataDirectory,
                ScreenshotIntervalMinutes = 1,
                OpenAiEnabled = false
            };
            store.SaveSettings(settings);
            var snapshot = new SettingsSnapshot(settings);
            var capture = new BlockingCaptureService();
            application = CreateApplication(store, snapshot, capture, startTimer: true);
            var started = await application.StartTrackingAsync(new StartTrackingRequest(), CancellationToken.None);
            Assert.True(started.Succeeded);
            var deadline = typeof(WorkTrailApplication).GetField(
                "_nextScheduledSnapshotAt",
                BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException("Scheduled snapshot deadline field was not found.");
            deadline.SetValue(application, DateTimeOffset.Now.AddSeconds(-1));
            await capture.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));

            var disposal = application.DisposeAsync().AsTask();
            await Task.Delay(TimeSpan.FromMilliseconds(100));
            Assert.False(disposal.IsCompleted);

            capture.Release.Set();
            await disposal.WaitAsync(TimeSpan.FromSeconds(5));
            application = null;
        }
        finally
        {
            if (application is not null)
            {
                await application.DisposeAsync();
            }

            await DeleteTemporaryDirectoryAsync(dataDirectory);
        }
    }

    private static WorkTrailApplication CreateApplication(
        LocalStore store,
        SettingsSnapshot settings,
        IScreenCaptureService capture,
        bool startTimer,
        TrackingDomainService? tracking = null,
        IScreenshotOcrService? screenshotOcr = null,
        IScreenshotNotificationService? screenshotNotifications = null)
    {
        var utilities = new UtilityService();
        return new WorkTrailApplication(
            store,
            utilities,
            tracking ?? new TrackingDomainService(store, settings),
            capture,
            new FakeHardwareTelemetryService(),
            new OpenAiAnalysisService(store, capture),
            new StartupService(),
            new BuildInformationService(),
            screenshotOcr: screenshotOcr,
            settingsSnapshot: settings,
            startScheduledSnapshotTimer: startTimer,
            screenshotNotifications: screenshotNotifications);
    }

    private static ActivitySample Sample(DateTimeOffset timestamp, string context) => new(
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

    private static string CreateTemporaryDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "WorkTrail.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static async Task DeleteTemporaryDirectoryAsync(string path)
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                Directory.Delete(path, recursive: true);
                return;
            }
            catch (IOException) when (attempt < 4)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(50));
            }
        }
    }

    private sealed class RecordingScreenshotNotifications : IScreenshotNotificationService
    {
        internal List<string> Paths { get; } = [];

        /// <inheritdoc />
        public void Show(string screenshotPath) => Paths.Add(screenshotPath);

        /// <inheritdoc />
        public void Clear() { }

        /// <inheritdoc />
        public void Dispose() { }
    }

    private sealed class NotificationCaptureService(bool keep) : IScreenCaptureService
    {
        /// <inheritdoc />
        public ScreenshotCaptureResult CaptureByMode(
            string directory,
            string captureMode,
            string captureOrigin,
            Func<ScreenshotCaptureContext, ScreenshotCaptureDecision> authorizeCapture)
        {
            var decision = authorizeCapture(new ScreenshotCaptureContext("allowed-app", "Allowed", "Work", "Allowed window"));
            if (decision != ScreenshotCaptureDecision.Allowed) throw new ScreenshotCapturePreconditionException(decision);
            var captureId = Guid.NewGuid().ToString("N");
            var capturedAt = DateTimeOffset.UtcNow;
            var day = ScreenshotStorageLayout.GetDayDirectory(directory, capturedAt);
            Directory.CreateDirectory(day);
            // The focused monitor is deliberately not monitor 1, matching the real capture ordering contract.
            string[] stems = captureMode == "active-window" ? ["active-window"] : ["monitor-2", "monitor-1"];
            var paths = new List<string>();
            foreach (var stem in stems)
            {
                var path = Path.Combine(day, $"{captureId}_1.0.0_{captureOrigin}_{stem}.webp");
                File.WriteAllBytes(ScreenshotPublicationJournal.StagingPath(path), [1, 2, 3]);
                paths.Add(path);
            }
            return new ScreenshotCaptureResult(captureId, paths, keep ? paths : [], captureOrigin, CapturedAt: capturedAt);
        }
    }

    private sealed class BoundaryCaptureService : IScreenCaptureService
    {
        private readonly Action _beforeAuthorization;
        private readonly ScreenshotCaptureContext _context;
        private readonly string _captureId = Guid.NewGuid().ToString("N");
        private readonly DateTimeOffset _capturedAt = DateTimeOffset.UtcNow;

        internal BoundaryCaptureService(
            string directory,
            Action? beforeAuthorization = null,
            ScreenshotCaptureContext? context = null)
        {
            _beforeAuthorization = beforeAuthorization ?? (() => { });
            _context = context ?? new ScreenshotCaptureContext("allowed-app", "Allowed", "Work", "Allowed window");
            OutputPath = Path.Combine(ScreenshotStorageLayout.GetDayDirectory(directory, _capturedAt),
                $"{_captureId}_1.0.0_manual_monitor-1.webp");
        }

        internal int PixelReadCount { get; private set; }

        internal string OutputPath { get; }

        internal TaskCompletionSource<bool> Captured { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <inheritdoc />
        public ScreenshotCaptureResult CaptureByMode(
            string directory,
            string captureMode,
            string captureOrigin,
            Func<ScreenshotCaptureContext, ScreenshotCaptureDecision> authorizeCapture)
        {
            _beforeAuthorization();
            var decision = authorizeCapture(_context);
            if (decision != ScreenshotCaptureDecision.Allowed)
            {
                throw new ScreenshotCapturePreconditionException(decision);
            }

            PixelReadCount++;
            Directory.CreateDirectory(Path.GetDirectoryName(OutputPath)!);
            File.WriteAllBytes(ScreenshotPublicationJournal.StagingPath(OutputPath), [1, 2, 3]);
            Captured.TrySetResult(true);
            return new ScreenshotCaptureResult(
                _captureId,
                [OutputPath],
                [OutputPath],
                captureOrigin, CapturedAt: _capturedAt);
        }
    }

    private sealed class BlockingCaptureService : IScreenCaptureService
    {
        internal TaskCompletionSource<bool> Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal ManualResetEventSlim Release { get; } = new(initialState: false);

        internal int CallCount { get; private set; }

        /// <inheritdoc />
        public ScreenshotCaptureResult CaptureByMode(
            string requestedDirectory,
            string captureMode,
            string captureOrigin,
            Func<ScreenshotCaptureContext, ScreenshotCaptureDecision> authorizeCapture)
        {
            var decision = authorizeCapture(new ScreenshotCaptureContext("allowed-app", "Allowed", "Work", "Allowed window"));
            if (decision != ScreenshotCaptureDecision.Allowed)
            {
                throw new ScreenshotCapturePreconditionException(decision);
            }

            CallCount++;
            Started.TrySetResult(true);
            Release.Wait(TimeSpan.FromSeconds(10));
            var captureId = Guid.NewGuid().ToString("N");
            var capturedAt = DateTimeOffset.UtcNow;
            var day = ScreenshotStorageLayout.GetDayDirectory(requestedDirectory, capturedAt);
            Directory.CreateDirectory(day);
            var outputPath = Path.Combine(day, $"{captureId}_1.0.0_{captureOrigin}_monitor-1.webp");
            File.WriteAllBytes(ScreenshotPublicationJournal.StagingPath(outputPath), [1, 2, 3]);
            return new ScreenshotCaptureResult(captureId, [outputPath], [outputPath], captureOrigin, CapturedAt: capturedAt);
        }
    }

    private sealed class CancellationAwareOcrService : IScreenshotOcrService
    {
        internal TaskCompletionSource<bool> Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool IsEnabled => true;

        /// <inheritdoc />
        public async Task<ScreenshotOcrResult> ExtractAsync(
            string imagePath,
            CancellationToken cancellationToken = default)
        {
            Started.TrySetResult(true);
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException("Cancellation should stop OCR before a result is created.");
        }
    }
}
