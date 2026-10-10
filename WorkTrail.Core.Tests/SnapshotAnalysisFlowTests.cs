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
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using WorkTrail.Application;
using WorkTrail.Ocr;
using WorkTrail.Services;
using Xunit;

namespace WorkTrail.Core.Tests;

[Collection(ProcessEnvironmentCollection.Name)]
public sealed class SnapshotAnalysisFlowTests
{
    private const string TestApiKeyVariable = "WORKTRAIL_OPENAI_APIKEY";

    /// <summary>Reading and retention preferences are independent and never remove older capture evidence.</summary>
    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public async Task CaptureScreenshot_HonorsSensorReadingAndSavingPreferences(bool enabled, bool save)
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var store = new LocalStore(directory);
            store.SaveSettings(store.LoadSettings() with
            {
                ScreenshotsEnabled = true,
                ScreenshotDirectory = directory,
                HardwareSensorsEnabled = enabled,
                HardwareSaveSnapshots = save
            });
            var olderId = Guid.NewGuid().ToString("N");
            var olderTime = DateTimeOffset.UtcNow.AddMinutes(-1);
            var olderSnapshot = HardwareTestData.Snapshot(olderTime);
            store.UpsertScreenshotIntervalTelemetry(olderId,
                [Path.Combine(directory, $"{olderId}_1.0.0_manual_monitor-1.webp")],
                new ScreenshotIntervalTelemetry(olderTime.AddMinutes(-1), olderTime, 25, 15), olderSnapshot);
            var capture = new RecordingCaptureService(directory);
            var hardware = new FakeHardwareTelemetryService();
            await using var application = CreateApplication(store, capture,
                new RecordingAnalysisService(store.LoadSettings().InstallationId), hardwareTelemetry: hardware);

            var result = await application.CaptureScreenshotAsync(
                new CaptureScreenshotRequest("all-screens", Keep: true, ScreenshotCaptureOrigins.Manual), CancellationToken.None);

            Assert.True(result.Succeeded);
            Assert.Equal(enabled ? 1 : 0, hardware.CaptureCount);
            Assert.Equal(enabled && save, store.LoadCaptureHardwareSnapshot(capture.Result.CaptureId) is not null);
            Assert.Equal(olderTime, store.LoadCaptureHardwareSnapshot(olderId)!.Timestamp);
            Assert.Equal(enabled ? "ready" : "disabled", result.Value!.HardwareSnapshot!.Status);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    /// <summary>Saved advanced preference never causes automatic elevation; changes reach the existing service immediately.</summary>
    [Fact]
    public async Task SensorSettings_ApplyLiveWithoutImplicitAdvancedActivation()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var store = new LocalStore(directory);
            store.SaveSettings(store.LoadSettings() with { HardwareUseAdvancedSensors = true, HardwareSamplingProfile = "slow" });
            var hardware = new FakeHardwareTelemetryService();
            await using var application = CreateApplication(store, new RecordingCaptureService(directory),
                new RecordingAnalysisService(store.LoadSettings().InstallationId), hardwareTelemetry: hardware);
            Assert.Equal("slow", hardware.Configuration.SamplingProfile);
            Assert.True(hardware.Configuration.UseAdvancedSensors);
            Assert.False(hardware.AdvancedEnabled);

            var changed = await application.PatchSettingsAsync(new SettingsPatch(new Dictionary<string, string?>
            {
                ["sensors.sampling_profile"] = "fastest",
                ["sensors.enabled"] = "false",
                ["sensors.save_snapshots"] = "false"
            }), CancellationToken.None);

            Assert.True(changed.Succeeded);
            Assert.Equal("fastest", hardware.Configuration.SamplingProfile);
            Assert.False(hardware.Configuration.Enabled);
            Assert.False(store.LoadSettings().HardwareSaveSnapshots);
            Assert.True(store.LoadSettings().HardwareUseAdvancedSensors);
            var disabledActivation = await application.EnableAdvancedHardwareTelemetryAsync(CancellationToken.None);
            Assert.False(disabledActivation.Succeeded);
            Assert.False(hardware.AdvancedEnabled);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    /// <summary>A post-commit index failure cannot restore sensor collection against the saved and published preference.</summary>
    [Fact]
    public async Task SensorSettings_PostCommitFailureKeepsHardwareAlignedWithPublishedPreferences()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var store = new LocalStore(directory);
            var hardware = new FakeHardwareTelemetryService();
            await using var application = CreateApplication(store, new RecordingCaptureService(directory),
                new RecordingAnalysisService(store.LoadSettings().InstallationId), hardwareTelemetry: hardware);
            using (var connection = new SqliteConnection($"Data Source={store.ActivityDatabasePath};Pooling=False"))
            {
                connection.Open();
                using var command = connection.CreateCommand();
                command.CommandText = """
                    CREATE TRIGGER fail_settings_search_marker BEFORE INSERT ON search_change_log
                    WHEN NEW.entity_id = 'settings-search-projection'
                    BEGIN SELECT RAISE(ABORT, 'test settings search marker failure'); END;
                    """;
                command.ExecuteNonQuery();
            }

            await Assert.ThrowsAsync<SqliteException>(() => application.PatchSettingsAsync(
                new SettingsPatch(new Dictionary<string, string?>
                {
                    ["sensors.enabled"] = "false",
                    ["sensors.sampling_profile"] = "fast",
                    ["search.synonyms"] = "false"
                }), CancellationToken.None));

            var persisted = store.LoadSettings();
            var published = Assert.IsType<AppSettings>((await application.GetSettingsAsync(CancellationToken.None)).Value);
            Assert.False(persisted.HardwareSensorsEnabled);
            Assert.False(published.HardwareSensorsEnabled);
            Assert.False(persisted.SearchSynonymsEnabled);
            Assert.False(published.SearchSynonymsEnabled);
            Assert.Equal("fast", persisted.HardwareSamplingProfile);
            Assert.Equal("fast", published.HardwareSamplingProfile);
            Assert.Equal(persisted.HardwareSensorsEnabled, hardware.Configuration.Enabled);
            Assert.Equal(persisted.HardwareSamplingProfile, hardware.Configuration.SamplingProfile);

            var retry = await application.PatchSettingsAsync(new SettingsPatch(new Dictionary<string, string?>
            {
                ["sensors.enabled"] = "false"
            }), CancellationToken.None);
            Assert.True(retry.Succeeded);
            Assert.False(hardware.Configuration.Enabled);
            var snapshot = await application.CaptureSystemSnapshotAsync(CancellationToken.None);
            Assert.True(snapshot.Succeeded);
            Assert.Equal("disabled", snapshot.Value!.Status);
            Assert.Equal(0, hardware.CaptureCount);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CaptureScreenshot_AnalyzesTheSameSnapshot_WhenOpenAiIsEnabled(bool saveSensorSnapshot)
    {
        var dataDirectory = CreateTemporaryDirectory();
        var previousApiKey = Environment.GetEnvironmentVariable(TestApiKeyVariable, EnvironmentVariableTarget.Process);
        Environment.SetEnvironmentVariable(TestApiKeyVariable, "sk-test-only-key-1234567890", EnvironmentVariableTarget.Process);

        try
        {
            var store = new LocalStore(dataDirectory);
            store.SaveSettings(store.LoadSettings() with
            {
                ScreenshotsEnabled = true,
                OpenAiEnabled = true,
                AiApiKeyName = TestApiKeyVariable,
                HardwareSaveSnapshots = saveSensorSnapshot,
                ScreenshotDirectory = dataDirectory
            });

            var capture = new RecordingCaptureService(dataDirectory);
            var analysis = new RecordingAnalysisService(store.LoadSettings().InstallationId);
            var hardware = new FakeHardwareTelemetryService();
            await using var application = CreateApplication(store, capture, analysis, hardwareTelemetry: hardware);

            var result = await application.CaptureScreenshotAsync(
                new CaptureScreenshotRequest("all-screens", Keep: true, ScreenshotCaptureOrigins.Manual),
                CancellationToken.None);

            Assert.True(result.Succeeded);
            Assert.NotNull(result.Value);
            Assert.Equal(1, capture.CallCount);
            Assert.Equal(1, analysis.CallCount);
            Assert.Equal(capture.Result.CaptureId, analysis.Capture?.CaptureId);
            Assert.Same(result.Value.HardwareSnapshot, analysis.Capture?.HardwareSnapshot);
            Assert.Equal(1, hardware.CaptureCount);
            var savedSnapshot = store.LoadCaptureHardwareSnapshot(capture.Result.CaptureId);
            if (saveSensorSnapshot)
                Assert.Equal(result.Value.HardwareSnapshot?.Timestamp, savedSnapshot?.Timestamp);
            else
                Assert.Null(savedSnapshot);
            Assert.Equal("snapshot.manual", analysis.Origin);
            Assert.True(analysis.KeepCapture);
        }
        finally
        {
            Environment.SetEnvironmentVariable(TestApiKeyVariable, previousApiKey, EnvironmentVariableTarget.Process);
            Directory.Delete(dataDirectory, recursive: true);
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task DeferredCapture_AnalyzesTheSameSnapshotOnlyAfterExplicitRequest_WhenOpenAiIsEnabled(bool saveSensorSnapshot)
    {
        var dataDirectory = CreateTemporaryDirectory();
        var previousApiKey = Environment.GetEnvironmentVariable(TestApiKeyVariable, EnvironmentVariableTarget.Process);
        Environment.SetEnvironmentVariable(TestApiKeyVariable, "sk-test-only-key-1234567890", EnvironmentVariableTarget.Process);

        try
        {
            var store = new LocalStore(dataDirectory);
            store.SaveSettings(store.LoadSettings() with
            {
                ScreenshotsEnabled = true,
                OpenAiEnabled = true,
                AiApiKeyName = TestApiKeyVariable,
                HardwareSaveSnapshots = saveSensorSnapshot,
                ScreenshotDirectory = dataDirectory
            });

            var capture = new RecordingCaptureService(dataDirectory);
            var analysis = new RecordingAnalysisService(store.LoadSettings().InstallationId);
            var hardware = new FakeHardwareTelemetryService();
            await using var application = CreateApplication(store, capture, analysis, hardwareTelemetry: hardware);

            var captureResult = await application.CaptureScreenshotAsync(
                new CaptureScreenshotRequest("all-screens", Keep: true, ScreenshotCaptureOrigins.Manual, DeferAiAnalysis: true),
                CancellationToken.None);

            Assert.True(captureResult.Succeeded);
            Assert.Equal(1, capture.CallCount);
            Assert.Equal(0, analysis.CallCount);
            hardware.Snapshot = HardwareTestData.Snapshot(DateTimeOffset.UtcNow, cpu: 99, gpu: 99);

            var analysisResult = await application.AnalyzeCapturedScreenshotAsync(
                new AnalyzeCapturedScreenshotRequest(capture.Result, KeepCapture: true),
                CancellationToken.None);

            Assert.True(analysisResult.Succeeded);
            Assert.Equal(1, analysis.CallCount);
            Assert.Equal(capture.Result.CaptureId, analysis.Capture?.CaptureId);
            if (saveSensorSnapshot)
            {
                Assert.Equal(captureResult.Value?.HardwareSnapshot?.Timestamp, analysis.Capture?.HardwareSnapshot?.Timestamp);
                Assert.Equal(25, HardwareUsageProjection.Read(analysis.Capture!.HardwareSnapshot!).Cpu);
            }
            else
            {
                Assert.Null(analysis.Capture?.HardwareSnapshot);
                Assert.Null(store.LoadCaptureHardwareSnapshot(capture.Result.CaptureId));
            }
            Assert.Equal(1, hardware.CaptureCount);
            Assert.Equal("snapshot.manual", analysis.Origin);
            Assert.True(analysis.KeepCapture);
        }
        finally
        {
            Environment.SetEnvironmentVariable(TestApiKeyVariable, previousApiKey, EnvironmentVariableTarget.Process);
            Directory.Delete(dataDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task CaptureScreenshot_DoesNotAnalyze_WhenOpenAiIsDisabled()
    {
        var dataDirectory = CreateTemporaryDirectory();
        try
        {
            var store = new LocalStore(dataDirectory);
            store.SaveSettings(store.LoadSettings() with
            {
                ScreenshotsEnabled = true,
                OpenAiEnabled = false,
                ScreenshotDirectory = dataDirectory
            });

            var capture = new RecordingCaptureService(dataDirectory);
            var analysis = new RecordingAnalysisService(store.LoadSettings().InstallationId);
            await using var application = CreateApplication(store, capture, analysis);

            var result = await application.CaptureScreenshotAsync(
                new CaptureScreenshotRequest("all-screens", Keep: true, ScreenshotCaptureOrigins.Manual),
                CancellationToken.None);

            Assert.True(result.Succeeded);
            Assert.Equal(1, capture.CallCount);
            Assert.Equal(0, analysis.CallCount);
        }
        finally
        {
            Directory.Delete(dataDirectory, recursive: true);
        }
    }

    /// <summary>Ensures an unexpected OCR failure removes every captured artifact and returns an explicit failure.</summary>
    [Fact]
    public async Task CaptureScreenshot_UnexpectedOcrFailureCleansAllArtifactsAndReturnsFailure()
    {
        var dataDirectory = CreateTemporaryDirectory();
        try
        {
            var store = new LocalStore(dataDirectory);
            store.SaveSettings(store.LoadSettings() with
            {
                ScreenshotsEnabled = true,
                OpenAiEnabled = false,
                ScreenshotDirectory = dataDirectory
            });
            var capture = new ArtifactCaptureService();
            var analysis = new RecordingAnalysisService(store.LoadSettings().InstallationId);
            await using var application = CreateApplication(
                store,
                capture,
                analysis,
                screenshotOcr: new UnexpectedFailureOcrService());

            var result = await application.CaptureScreenshotAsync(
                new CaptureScreenshotRequest("all-screens", Keep: true, ScreenshotCaptureOrigins.Manual),
                CancellationToken.None);

            Assert.False(result.Succeeded);
            Assert.Equal("screenshot.capture.failed", result.Code);
            Assert.NotNull(capture.RawPath);
            Assert.NotNull(capture.StoredPath);
            Assert.False(File.Exists(capture.RawPath));
            Assert.False(File.Exists(capture.StoredPath));
            Assert.Equal(0, analysis.CallCount);
        }
        finally
        {
            Directory.Delete(dataDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task DeferredCapture_StillSavesVisualDescription_WhenOcrRefinementFails()
    {
        var dataDirectory = CreateTemporaryDirectory();
        var previousApiKey = Environment.GetEnvironmentVariable(TestApiKeyVariable, EnvironmentVariableTarget.Process);
        Environment.SetEnvironmentVariable(TestApiKeyVariable, "sk-test-only-key-1234567890", EnvironmentVariableTarget.Process);

        try
        {
            var store = new LocalStore(dataDirectory);
            store.SaveSettings(store.LoadSettings() with
            {
                ScreenshotsEnabled = true,
                OpenAiEnabled = true,
                AiApiKeyName = TestApiKeyVariable,
                ScreenshotDirectory = dataDirectory
            });
            var capture = new RecordingCaptureService(dataDirectory);
            var analysis = new RecordingAnalysisService(store.LoadSettings().InstallationId);
            await using var application = CreateApplication(
                store,
                capture,
                analysis,
                ocrRefinement: new FailingOcrRefinementService());

            var result = await application.AnalyzeCapturedScreenshotAsync(
                new AnalyzeCapturedScreenshotRequest(capture.Result, KeepCapture: true),
                CancellationToken.None);

            Assert.True(result.Succeeded);
            Assert.Equal("analyzed", result.Value?.Summary);
            Assert.Equal(1, analysis.CallCount);
            Assert.Equal(capture.Result.CaptureId, analysis.Capture?.CaptureId);
        }
        finally
        {
            Environment.SetEnvironmentVariable(TestApiKeyVariable, previousApiKey, EnvironmentVariableTarget.Process);
            Directory.Delete(dataDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task CaptureScreenshot_StillSavesVisualDescription_WhenOcrRefinementFails()
    {
        var dataDirectory = CreateTemporaryDirectory();
        var previousApiKey = Environment.GetEnvironmentVariable(TestApiKeyVariable, EnvironmentVariableTarget.Process);
        Environment.SetEnvironmentVariable(TestApiKeyVariable, "sk-test-only-key-1234567890", EnvironmentVariableTarget.Process);

        try
        {
            var store = new LocalStore(dataDirectory);
            store.SaveSettings(store.LoadSettings() with
            {
                ScreenshotsEnabled = true,
                OpenAiEnabled = true,
                AiApiKeyName = TestApiKeyVariable,
                ScreenshotDirectory = dataDirectory
            });
            var capture = new RecordingCaptureService(dataDirectory);
            var analysis = new RecordingAnalysisService(store.LoadSettings().InstallationId);
            await using var application = CreateApplication(
                store,
                capture,
                analysis,
                ocrRefinement: new FailingOcrRefinementService());

            var result = await application.CaptureScreenshotAsync(
                new CaptureScreenshotRequest("all-screens", Keep: true, ScreenshotCaptureOrigins.Manual),
                CancellationToken.None);

            Assert.True(result.Succeeded);
            Assert.Equal(1, analysis.CallCount);
            Assert.Equal(capture.Result.CaptureId, analysis.Capture?.CaptureId);
            Assert.NotNull(analysis.Capture?.HardwareSnapshot);
        }
        finally
        {
            Environment.SetEnvironmentVariable(TestApiKeyVariable, previousApiKey, EnvironmentVariableTarget.Process);
            Directory.Delete(dataDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task ProviderRateLimit_IsNotMisclassifiedAsInvalidConfiguration()
    {
        var dataDirectory = CreateTemporaryDirectory();
        var previousApiKey = Environment.GetEnvironmentVariable(TestApiKeyVariable, EnvironmentVariableTarget.Process);
        Environment.SetEnvironmentVariable(TestApiKeyVariable, "sk-test-only-key-1234567890", EnvironmentVariableTarget.Process);

        try
        {
            var store = new LocalStore(dataDirectory);
            store.SaveSettings(store.LoadSettings() with
            {
                ScreenshotsEnabled = true,
                OpenAiEnabled = true,
                AiApiKeyName = TestApiKeyVariable,
                ScreenshotDirectory = dataDirectory
            });

            var capture = new RecordingCaptureService(dataDirectory);
            await using var application = CreateApplication(store, capture, new ProviderFailureAnalysisService());

            var captureResult = await application.CaptureScreenshotAsync(
                new CaptureScreenshotRequest("all-screens", Keep: true, ScreenshotCaptureOrigins.Manual),
                CancellationToken.None);
            var deferredCapture = await application.CaptureScreenshotAsync(
                new CaptureScreenshotRequest(
                    "all-screens",
                    Keep: true,
                    ScreenshotCaptureOrigins.Manual,
                    DeferAiAnalysis: true),
                CancellationToken.None);
            var deferredAnalysis = await application.AnalyzeCapturedScreenshotAsync(
                new AnalyzeCapturedScreenshotRequest(deferredCapture.Value!, KeepCapture: true),
                CancellationToken.None);
            var directResult = await application.AnalyzeCurrentActivityAsync(
                new AnalyzeCurrentActivityRequest(AllowCapture: false, Origin: "manual"),
                CancellationToken.None);

            Assert.False(captureResult.Succeeded);
            Assert.Equal("ai.provider.failed", captureResult.Code);
            Assert.True(deferredCapture.Succeeded);
            Assert.False(deferredAnalysis.Succeeded);
            Assert.Equal("ai.provider.failed", deferredAnalysis.Code);
            Assert.False(directResult.Succeeded);
            Assert.Equal("ai.provider.failed", directResult.Code);
        }
        finally
        {
            Environment.SetEnvironmentVariable(TestApiKeyVariable, previousApiKey, EnvironmentVariableTarget.Process);
            Directory.Delete(dataDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task DiagnosticsFacade_MapsUnavailableLogAndRejectsArbitraryProductLinks()
    {
        var dataDirectory = CreateTemporaryDirectory();
        var emptyLogDirectory = Path.Combine(dataDirectory, "logs");
        Directory.CreateDirectory(emptyLogDirectory);
        try
        {
            var store = new LocalStore(dataDirectory);
            var capture = new RecordingCaptureService(dataDirectory);
            var logs = new ApplicationLogService(emptyLogDirectory, Path.Combine(dataDirectory, "exports"));
            await using var application = CreateApplication(
                store,
                capture,
                new RecordingAnalysisService(store.LoadSettings().InstallationId),
                logs);

            var open = await application.OpenApplicationLogAsync(CancellationToken.None);
            var share = await application.ShareApplicationLogAsync(0, CancellationToken.None);
            var arbitraryLink = await application.OpenProductLinkAsync("https://example.invalid", CancellationToken.None);

            Assert.False(open.Succeeded);
            Assert.Equal("diagnostics.log.unavailable", open.Code);
            Assert.False(share.Succeeded);
            Assert.Equal("diagnostics.log.share.window.invalid", share.Code);
            Assert.False(arbitraryLink.Succeeded);
            Assert.Equal("product.link.invalid", arbitraryLink.Code);
        }
        finally
        {
            Directory.Delete(dataDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task DeferredScheduledCapture_IsRetainedBeforeAiConfigurationIsAvailable()
    {
        var dataDirectory = CreateTemporaryDirectory();
        var previousApiKey = Environment.GetEnvironmentVariable(TestApiKeyVariable, EnvironmentVariableTarget.Process);
        Environment.SetEnvironmentVariable(TestApiKeyVariable, null, EnvironmentVariableTarget.Process);
        try
        {
            var store = new LocalStore(dataDirectory);
            store.SaveSettings(store.LoadSettings() with
            {
                ScreenshotsEnabled = true,
                OpenAiEnabled = true,
                AiApiKeyName = TestApiKeyVariable,
                ScreenshotDirectory = dataDirectory
            });

            var capture = new RecordingCaptureService(dataDirectory);
            var analysis = new RecordingAnalysisService(store.LoadSettings().InstallationId);
            await using var application = CreateApplication(store, capture, analysis);

            var result = await application.CaptureScreenshotAsync(
                new CaptureScreenshotRequest(
                    Mode: null,
                    Keep: true,
                    CaptureOrigin: ScreenshotCaptureOrigins.Scheduled,
                    DeferAiAnalysis: true),
                CancellationToken.None);

            Assert.True(result.Succeeded);
            Assert.Equal(1, capture.CallCount);
            Assert.Equal(ScreenshotCaptureOrigins.Scheduled, capture.LastCaptureOrigin);
            Assert.Equal(0, analysis.CallCount);
        }
        finally
        {
            Environment.SetEnvironmentVariable(TestApiKeyVariable, previousApiKey, EnvironmentVariableTarget.Process);
            Directory.Delete(dataDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task FailedFrameAnalysis_QueuesOneRedactedErrorNotification()
    {
        var dataDirectory = CreateTemporaryDirectory();
        var previousApiKey = Environment.GetEnvironmentVariable(TestApiKeyVariable, EnvironmentVariableTarget.Process);
        Environment.SetEnvironmentVariable(TestApiKeyVariable, "sk-test-only-key-1234567890", EnvironmentVariableTarget.Process);
        try
        {
            var store = new LocalStore(dataDirectory);
            store.SaveSettings(store.LoadSettings() with
            {
                ScreenshotsEnabled = true,
                OpenAiEnabled = true,
                AiApiKeyName = TestApiKeyVariable,
                ScreenshotDirectory = dataDirectory
            });
            var capture = new RecordingCaptureService(dataDirectory);
            await using var application = CreateApplication(store, capture, new ProviderFailureAnalysisService());

            var result = await application.AnalyzeCapturedScreenshotAsync(
                new AnalyzeCapturedScreenshotRequest(capture.Result, KeepCapture: true),
                CancellationToken.None);
            var notifications = await application.DrainApplicationNotificationsAsync(CancellationToken.None);
            var drainedAgain = await application.DrainApplicationNotificationsAsync(CancellationToken.None);

            Assert.False(result.Succeeded);
            Assert.Equal("ai.provider.failed", result.Code);
            var notification = Assert.Single(Assert.IsAssignableFrom<IReadOnlyList<ApplicationNotification>>(notifications.Value));
            Assert.Equal(ApplicationNotificationSeverity.Error, notification.Severity);
            Assert.Equal("Notification.AiAnalysisFailed.Title", notification.TitleKey);
            Assert.Equal("Notification.AiAnalysisFailed.Message", notification.MessageKey);
            Assert.Equal("ai.provider.failed", notification.Code);
            Assert.NotNull(notification.Detail);
            Assert.Contains("HTTP status: 429", notification.Detail, StringComparison.Ordinal);
            Assert.Contains("Failure: http_429.insufficient_quota", notification.Detail, StringComparison.Ordinal);
            Assert.Contains("Latency: 42 ms", notification.Detail, StringComparison.Ordinal);
            Assert.Contains("Provider request id: req_safe_test", notification.Detail, StringComparison.Ordinal);
            Assert.DoesNotContain("sk-test", notification.ToString(), StringComparison.OrdinalIgnoreCase);
            Assert.Empty(Assert.IsAssignableFrom<IReadOnlyList<ApplicationNotification>>(drainedAgain.Value));
        }
        finally
        {
            Environment.SetEnvironmentVariable(TestApiKeyVariable, previousApiKey, EnvironmentVariableTarget.Process);
            Directory.Delete(dataDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task DisabledAi_DoesNotQueueFrameAnalysisNotifications()
    {
        var dataDirectory = CreateTemporaryDirectory();
        try
        {
            var store = new LocalStore(dataDirectory);
            store.SaveSettings(store.LoadSettings() with
            {
                ScreenshotsEnabled = true,
                OpenAiEnabled = false,
                ScreenshotDirectory = dataDirectory
            });
            var capture = new RecordingCaptureService(dataDirectory);
            await using var application = CreateApplication(store, capture, new FailingAnalysisService());

            var result = await application.AnalyzeCapturedScreenshotAsync(
                new AnalyzeCapturedScreenshotRequest(capture.Result, KeepCapture: true),
                CancellationToken.None);
            var notifications = await application.DrainApplicationNotificationsAsync(CancellationToken.None);

            Assert.False(result.Succeeded);
            Assert.Equal("ai.disabled", result.Code);
            Assert.Empty(Assert.IsAssignableFrom<IReadOnlyList<ApplicationNotification>>(notifications.Value));
        }
        finally
        {
            Directory.Delete(dataDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task CaptureScreenshotWithoutExplicitMode_UsesPersistedCaptureMode()
    {
        var dataDirectory = CreateTemporaryDirectory();
        try
        {
            var store = new LocalStore(dataDirectory);
            store.SaveSettings(store.LoadSettings() with
            {
                ScreenshotsEnabled = true,
                OpenAiEnabled = false,
                ScreenshotCaptureMode = "active-window",
                ScreenshotDirectory = dataDirectory
            });

            var capture = new RecordingCaptureService(dataDirectory);
            var analysis = new RecordingAnalysisService(store.LoadSettings().InstallationId);
            await using var application = CreateApplication(store, capture, analysis);

            var result = await application.CaptureScreenshotAsync(
                new CaptureScreenshotRequest(
                    Mode: null,
                    Keep: true,
                    CaptureOrigin: ScreenshotCaptureOrigins.Scheduled,
                    DeferAiAnalysis: true),
                CancellationToken.None);

            Assert.True(result.Succeeded);
            Assert.Equal(1, capture.CallCount);
            Assert.Equal("active-window", capture.LastCaptureMode);
            Assert.Equal(0, analysis.CallCount);
        }
        finally
        {
            Directory.Delete(dataDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task CaptureScreenshot_RejectsUnsupportedExplicitModeBeforeCapture()
    {
        var dataDirectory = CreateTemporaryDirectory();
        try
        {
            var store = new LocalStore(dataDirectory);
            store.SaveSettings(store.LoadSettings() with
            {
                ScreenshotsEnabled = true,
                OpenAiEnabled = false,
                ScreenshotCaptureMode = "active-window",
                ScreenshotDirectory = dataDirectory
            });

            var capture = new RecordingCaptureService(dataDirectory);
            var analysis = new RecordingAnalysisService(store.LoadSettings().InstallationId);
            await using var application = CreateApplication(store, capture, analysis);

            var result = await application.CaptureScreenshotAsync(
                new CaptureScreenshotRequest("unsupported-mode", Keep: true, ScreenshotCaptureOrigins.Manual),
                CancellationToken.None);

            Assert.False(result.Succeeded);
            Assert.Equal("screenshot.mode.invalid", result.Code);
            Assert.Equal("ScreenshotModeUnsupported", result.MessageKey);
            var issue = Assert.Single(result.Issues);
            Assert.Equal("mode", issue.Field);
            Assert.Equal("unsupported", issue.Code);
            Assert.Equal("ScreenshotModeUnsupported", issue.MessageKey);
            Assert.Equal(0, capture.CallCount);
            Assert.Equal(0, analysis.CallCount);
        }
        finally
        {
            Directory.Delete(dataDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task CaptureScreenshot_RejectsUnsupportedPersistedModelBeforeCapture()
    {
        var dataDirectory = CreateTemporaryDirectory();
        var previousApiKey = Environment.GetEnvironmentVariable(TestApiKeyVariable, EnvironmentVariableTarget.Process);
        Environment.SetEnvironmentVariable(TestApiKeyVariable, "sk-test-only-key-1234567890", EnvironmentVariableTarget.Process);

        try
        {
            var store = new LocalStore(dataDirectory);
            store.SaveSettings(store.LoadSettings() with
            {
                ScreenshotsEnabled = true,
                OpenAiEnabled = true,
                AiApiKeyName = TestApiKeyVariable,
                Model = "unsupported-model",
                ScreenshotDirectory = dataDirectory
            });

            var capture = new RecordingCaptureService(dataDirectory);
            var analysis = new RecordingAnalysisService(store.LoadSettings().InstallationId);
            await using var application = CreateApplication(store, capture, analysis);

            var result = await application.CaptureScreenshotAsync(
                new CaptureScreenshotRequest("all-screens", Keep: true, ScreenshotCaptureOrigins.Manual),
                CancellationToken.None);

            Assert.False(result.Succeeded);
            Assert.Equal("ai.configuration.invalid", result.Code);
            Assert.Contains(result.Issues, issue => issue.Field == "ai.model" && issue.Code == "unsupported");
            Assert.Equal(0, capture.CallCount);
            Assert.Equal(0, analysis.CallCount);
        }
        finally
        {
            Environment.SetEnvironmentVariable(TestApiKeyVariable, previousApiKey, EnvironmentVariableTarget.Process);
            Directory.Delete(dataDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task CaptureScreenshot_RejectsUnsupportedPersistedThinkingEffortBeforeCapture()
    {
        var dataDirectory = CreateTemporaryDirectory();
        var previousApiKey = Environment.GetEnvironmentVariable(TestApiKeyVariable, EnvironmentVariableTarget.Process);
        Environment.SetEnvironmentVariable(TestApiKeyVariable, "sk-test-only-key-1234567890", EnvironmentVariableTarget.Process);

        try
        {
            var store = new LocalStore(dataDirectory);
            store.SaveSettings(store.LoadSettings() with
            {
                ScreenshotsEnabled = true,
                OpenAiEnabled = true,
                AiApiKeyName = TestApiKeyVariable,
                Model = "gpt-5.5",
                AiReasoningEffort = "max",
                ScreenshotDirectory = dataDirectory
            });

            var capture = new RecordingCaptureService(dataDirectory);
            var analysis = new RecordingAnalysisService(store.LoadSettings().InstallationId);
            await using var application = CreateApplication(store, capture, analysis);

            var result = await application.CaptureScreenshotAsync(
                new CaptureScreenshotRequest("all-screens", Keep: true, ScreenshotCaptureOrigins.Manual),
                CancellationToken.None);

            Assert.False(result.Succeeded);
            Assert.Equal("ai.configuration.invalid", result.Code);
            Assert.Contains(result.Issues, issue => issue.Field == "ai.reasoning_effort" && issue.Code == "unsupported");
            Assert.Equal(0, capture.CallCount);
            Assert.Equal(0, analysis.CallCount);
        }
        finally
        {
            Environment.SetEnvironmentVariable(TestApiKeyVariable, previousApiKey, EnvironmentVariableTarget.Process);
            Directory.Delete(dataDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task CaptureScreenshot_RejectsTextOnlyPreviewModelBeforeCapture()
    {
        var dataDirectory = CreateTemporaryDirectory();
        var previousApiKey = Environment.GetEnvironmentVariable(TestApiKeyVariable, EnvironmentVariableTarget.Process);
        Environment.SetEnvironmentVariable(TestApiKeyVariable, "sk-test-only-key-1234567890", EnvironmentVariableTarget.Process);

        try
        {
            var store = new LocalStore(dataDirectory);
            store.SaveSettings(store.LoadSettings() with
            {
                ScreenshotsEnabled = true,
                OpenAiEnabled = true,
                AiApiKeyName = TestApiKeyVariable,
                Model = "gpt-5.3-codex-spark",
                AiReasoningEffort = "auto",
                ScreenshotDirectory = dataDirectory
            });

            var capture = new RecordingCaptureService(dataDirectory);
            var analysis = new RecordingAnalysisService(store.LoadSettings().InstallationId);
            await using var application = CreateApplication(store, capture, analysis);

            var result = await application.CaptureScreenshotAsync(
                new CaptureScreenshotRequest("all-screens", Keep: true, ScreenshotCaptureOrigins.Manual),
                CancellationToken.None);

            Assert.False(result.Succeeded);
            Assert.Equal("ai.configuration.invalid", result.Code);
            Assert.Contains(result.Issues, issue => issue.Field == "ai.model" && issue.Code == "image_input_unsupported");
            Assert.Equal(0, capture.CallCount);
            Assert.Equal(0, analysis.CallCount);
        }
        finally
        {
            Environment.SetEnvironmentVariable(TestApiKeyVariable, previousApiKey, EnvironmentVariableTarget.Process);
            Directory.Delete(dataDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task CaptureScreenshot_CanonicalizesPersistedModelAliasBeforeAnalysis()
    {
        var dataDirectory = CreateTemporaryDirectory();
        var previousApiKey = Environment.GetEnvironmentVariable(TestApiKeyVariable, EnvironmentVariableTarget.Process);
        Environment.SetEnvironmentVariable(TestApiKeyVariable, "sk-test-only-key-1234567890", EnvironmentVariableTarget.Process);

        try
        {
            var store = new LocalStore(dataDirectory);
            store.SaveSettings(store.LoadSettings() with
            {
                ScreenshotsEnabled = true,
                OpenAiEnabled = true,
                AiApiKeyName = TestApiKeyVariable,
                Model = "gpt-5.6",
                ScreenshotDirectory = dataDirectory
            });

            var capture = new RecordingCaptureService(dataDirectory);
            var analysis = new RecordingAnalysisService(store.LoadSettings().InstallationId);
            await using var application = CreateApplication(store, capture, analysis);

            var result = await application.CaptureScreenshotAsync(
                new CaptureScreenshotRequest("all-screens", Keep: true, ScreenshotCaptureOrigins.Manual),
                CancellationToken.None);

            Assert.True(result.Succeeded);
            Assert.Equal("gpt-5.6-sol", store.LoadSettings().Model);
            Assert.Equal(1, analysis.CallCount);
        }
        finally
        {
            Environment.SetEnvironmentVariable(TestApiKeyVariable, previousApiKey, EnvironmentVariableTarget.Process);
            Directory.Delete(dataDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task CaptureScreenshot_PropagatesCancellationIntoAnalysis()
    {
        var dataDirectory = CreateTemporaryDirectory();
        var previousApiKey = Environment.GetEnvironmentVariable(TestApiKeyVariable, EnvironmentVariableTarget.Process);
        Environment.SetEnvironmentVariable(TestApiKeyVariable, "sk-test-only-key-1234567890", EnvironmentVariableTarget.Process);

        try
        {
            var store = new LocalStore(dataDirectory);
            store.SaveSettings(store.LoadSettings() with
            {
                ScreenshotsEnabled = true,
                OpenAiEnabled = true,
                AiApiKeyName = TestApiKeyVariable,
                ScreenshotDirectory = dataDirectory
            });

            var capture = new RecordingCaptureService(dataDirectory);
            var analysis = new CancellationAwareAnalysisService();
            await using var application = CreateApplication(store, capture, analysis);
            using var cancellation = new CancellationTokenSource();

            var captureTask = application.CaptureScreenshotAsync(
                new CaptureScreenshotRequest("all-screens", Keep: true, ScreenshotCaptureOrigins.Manual),
                cancellation.Token);
            await analysis.Entered.WaitAsync(TimeSpan.FromSeconds(10));
            cancellation.Cancel();
            var result = await captureTask;

            Assert.False(result.Succeeded);
            Assert.Equal("operation.cancelled", result.Code);
            Assert.True(analysis.CancellationObserved);
            Assert.True(analysis.ReceivedToken.CanBeCanceled);
        }
        finally
        {
            Environment.SetEnvironmentVariable(TestApiKeyVariable, previousApiKey, EnvironmentVariableTarget.Process);
            Directory.Delete(dataDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task DeferredAnalysis_CostGuardrailDeletesRawRetainsStoredAndWarnsOncePerDay()
    {
        var dataDirectory = CreateTemporaryDirectory();
        var previousApiKey = Environment.GetEnvironmentVariable(TestApiKeyVariable, EnvironmentVariableTarget.Process);
        Environment.SetEnvironmentVariable(TestApiKeyVariable, "sk-test-only-key-1234567890", EnvironmentVariableTarget.Process);

        try
        {
            var store = new LocalStore(dataDirectory);
            store.SaveSettings(store.LoadSettings() with
            {
                OpenAiEnabled = true,
                OpenAiDailyLimit = 0,
                AiApiKeyName = TestApiKeyVariable,
                ScreenshotDirectory = dataDirectory
            });
            var captureService = new RecordingCaptureService(dataDirectory);
            var analysis = new RecordingAnalysisService(store.LoadSettings().InstallationId);
            await using var application = CreateApplication(store, captureService, analysis);
            var captureId = Guid.NewGuid().ToString("N");
            var dayDirectory = ScreenshotStorageLayout.GetDayDirectory(dataDirectory, DateTimeOffset.UtcNow);
            Directory.CreateDirectory(dayDirectory);
            var rawPath = Path.Combine(dayDirectory, $"{captureId}_1.0.0_scheduled_monitor-1-raw.webp");
            var storedPath = Path.Combine(dayDirectory, $"{captureId}_1.0.0_scheduled_monitor-1.webp");
            await File.WriteAllBytesAsync(rawPath, [1, 2, 3]);
            await File.WriteAllBytesAsync(storedPath, [4, 5, 6]);
            var capture = new ScreenshotCaptureResult(
                captureId,
                [rawPath],
                [storedPath],
                ScreenshotCaptureOrigins.Scheduled);

            var result = await application.AnalyzeCapturedScreenshotAsync(
                new AnalyzeCapturedScreenshotRequest(capture, KeepCapture: true, Origin: "snapshot.scheduled"),
                CancellationToken.None);

            Assert.False(result.Succeeded);
            Assert.Equal("ai.cost_guardrail", result.Code);
            Assert.False(File.Exists(rawPath));
            Assert.True(File.Exists(storedPath));
            Assert.Equal(0, analysis.CallCount);

            var repeated = await application.AnalyzeCapturedScreenshotAsync(
                new AnalyzeCapturedScreenshotRequest(capture, KeepCapture: true),
                CancellationToken.None);
            Assert.False(repeated.Succeeded);
            Assert.Equal("ai.cost_guardrail", repeated.Code);

            var notifications = await application.DrainApplicationNotificationsAsync(CancellationToken.None);
            var notification = Assert.Single(Assert.IsAssignableFrom<IReadOnlyList<ApplicationNotification>>(notifications.Value));
            Assert.Equal(ApplicationNotificationSeverity.Warning, notification.Severity);
            Assert.Equal("Notification.AiDailyLimitReached.Title", notification.TitleKey);
            Assert.Equal("Notification.AiDailyLimitReached.Message", notification.MessageKey);
            Assert.Equal("ai.cost_guardrail", notification.Code);
            Assert.Equal("0 / 0", notification.Detail);
        }
        finally
        {
            Environment.SetEnvironmentVariable(TestApiKeyVariable, previousApiKey, EnvironmentVariableTarget.Process);
            Directory.Delete(dataDirectory, recursive: true);
        }
    }

    private static WorkTrailApplication CreateApplication(
        LocalStore store,
        IScreenCaptureService capture,
        IAiAnalysisService analysis,
        ApplicationLogService? applicationLogs = null,
        IAiOcrRefinementService? ocrRefinement = null,
        IScreenshotOcrService? screenshotOcr = null,
        IHardwareTelemetryService? hardwareTelemetry = null)
    {
        var utilities = new UtilityService();
        return new WorkTrailApplication(
            store,
            utilities,
            new TrackingDomainService(store),
            capture,
            hardwareTelemetry ?? new FakeHardwareTelemetryService(),
            analysis,
            new StartupService(),
            new BuildInformationService(),
            applicationLogs: applicationLogs,
            screenshotOcr: screenshotOcr,
            ocrRefinement: ocrRefinement);
    }

    private static string CreateTemporaryDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "WorkTrail.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private sealed class RecordingCaptureService : IScreenCaptureService
    {
        private readonly string _directory;

        public RecordingCaptureService(string directory)
        {
            _directory = directory;
            Result = CreateResult(ScreenshotCaptureOrigins.Manual);
        }

        public int CallCount { get; private set; }

        public string? LastCaptureOrigin { get; private set; }

        public string? LastCaptureMode { get; private set; }

        public ScreenshotCaptureResult Result { get; private set; }

        /// <inheritdoc />
        public ScreenshotCaptureResult CaptureByMode(
            string directory,
            string captureMode,
            string captureOrigin,
            Func<ScreenshotCaptureContext, ScreenshotCaptureDecision> authorizeCapture)
        {
            Authorize(authorizeCapture);
            CallCount++;
            LastCaptureOrigin = captureOrigin;
            LastCaptureMode = captureMode;
            Result = CreateResult(captureOrigin, directory);
            return Result;
        }

        /// <summary>Stages a synthetic capture using the authorized root and current dated storage layout.</summary>
        private ScreenshotCaptureResult CreateResult(string captureOrigin, string? directory = null)
        {
            var captureId = Guid.NewGuid().ToString("N");
            var capturedAt = DateTimeOffset.UtcNow;
            var dayDirectory = ScreenshotStorageLayout.GetDayDirectory(directory ?? _directory, capturedAt);
            Directory.CreateDirectory(dayDirectory);
            var path = Path.Combine(dayDirectory, $"{captureId}_1.0.0_{captureOrigin}_monitor-1.webp");
            File.WriteAllBytes(ScreenshotPublicationJournal.StagingPath(path), [1, 2, 3]);
            return new ScreenshotCaptureResult(
                captureId,
                [path],
                [path],
                captureOrigin,
                CapturedAt: capturedAt);
        }
    }

    private sealed class ArtifactCaptureService : IScreenCaptureService
    {
        public string? RawPath { get; private set; }

        public string? StoredPath { get; private set; }

        /// <inheritdoc />
        public ScreenshotCaptureResult CaptureByMode(
            string requestedDirectory,
            string captureMode,
            string captureOrigin,
            Func<ScreenshotCaptureContext, ScreenshotCaptureDecision> authorizeCapture)
        {
            Authorize(authorizeCapture);
            var captureId = Guid.NewGuid().ToString("N");
            var capturedAt = DateTimeOffset.UtcNow;
            var dayDirectory = ScreenshotStorageLayout.GetDayDirectory(requestedDirectory, capturedAt);
            Directory.CreateDirectory(dayDirectory);
            RawPath = Path.Combine(dayDirectory, $"{captureId}_1.0.0_{captureOrigin}_monitor-1-raw.webp");
            StoredPath = Path.Combine(dayDirectory, $"{captureId}_1.0.0_{captureOrigin}_monitor-1.webp");
            File.WriteAllBytes(ScreenshotPublicationJournal.StagingPath(RawPath), [1, 2, 3]);
            File.WriteAllBytes(ScreenshotPublicationJournal.StagingPath(StoredPath), [4, 5, 6]);
            return new ScreenshotCaptureResult(captureId, [RawPath], [StoredPath], captureOrigin, CapturedAt: capturedAt);
        }
    }

    private static void Authorize(Func<ScreenshotCaptureContext, ScreenshotCaptureDecision> authorizeCapture)
    {
        var decision = authorizeCapture(new ScreenshotCaptureContext("allowed-app", "Allowed", "Work", "Allowed window"));
        if (decision != ScreenshotCaptureDecision.Allowed)
        {
            throw new ScreenshotCapturePreconditionException(decision);
        }
    }

    private sealed class UnexpectedFailureOcrService : IScreenshotOcrService
    {
        public bool IsEnabled => true;

        public Task<ScreenshotOcrResult> ExtractAsync(string imagePath, CancellationToken cancellationToken = default) =>
            Task.FromException<ScreenshotOcrResult>(new InvalidDataException("Unexpected OCR failure."));
    }

    private sealed class FailingOcrRefinementService : IAiOcrRefinementService
    {
        public Task<ScreenshotCaptureResult> RefineAsync(
            ScreenshotCaptureResult capture,
            AppSettings settings,
            CancellationToken cancellationToken) =>
            Task.FromException<ScreenshotCaptureResult>(new InvalidDataException("Truncated OCR JSON."));
    }

    private sealed class RecordingAnalysisService(string installationId) : IAiAnalysisService
    {
        public int CallCount { get; private set; }

        public ScreenshotCaptureResult? Capture { get; private set; }

        public bool KeepCapture { get; private set; }

        public string? Origin { get; private set; }

        public Task<AiAnalysis> AnalyzeCurrentScreenAsync(
            AnalysisContextSnapshot? activity,
            bool allowCapture = true,
            string origin = "manual",
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<AiAnalysis> AnalyzeCapturedScreenAsync(
            AnalysisContextSnapshot? activity,
            ScreenshotCaptureResult captureResult,
            bool keepCapture,
            string origin,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            Capture = captureResult;
            KeepCapture = keepCapture;
            Origin = origin;
            return Task.FromResult(new AiAnalysis(
                DateTimeOffset.UtcNow,
                "WorkTrail",
                "test",
                "analyzed",
                installationId,
                keepCapture ? string.Join(';', captureResult.StoredScreenshotPaths) : null,
                CorrelationId: captureResult.CaptureId,
                Origin: origin));
        }

        public Task<AiAnalysis> AnalyzeHistoricalCapturedScreenAsync(
            AnalysisContextSnapshot activity,
            ScreenshotCaptureResult captureResult,
            string origin,
            CancellationToken cancellationToken = default) =>
            AnalyzeCapturedScreenAsync(activity, captureResult, keepCapture: true, origin, cancellationToken);
    }

    private sealed class CancellationAwareAnalysisService : IAiAnalysisService
    {
        private readonly TaskCompletionSource<bool> _entered = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task Entered => _entered.Task;

        public CancellationToken ReceivedToken { get; private set; }

        public bool CancellationObserved { get; private set; }

        public Task<AiAnalysis> AnalyzeCurrentScreenAsync(
            AnalysisContextSnapshot? activity,
            bool allowCapture = true,
            string origin = "manual",
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public async Task<AiAnalysis> AnalyzeCapturedScreenAsync(
            AnalysisContextSnapshot? activity,
            ScreenshotCaptureResult captureResult,
            bool keepCapture,
            string origin,
            CancellationToken cancellationToken = default)
        {
            ReceivedToken = cancellationToken;
            _entered.TrySetResult(true);
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                throw new InvalidOperationException("Cancellation-aware analysis unexpectedly completed.");
            }
            catch (OperationCanceledException)
            {
                CancellationObserved = true;
                throw;
            }
        }

        public Task<AiAnalysis> AnalyzeHistoricalCapturedScreenAsync(
            AnalysisContextSnapshot activity,
            ScreenshotCaptureResult captureResult,
            string origin,
            CancellationToken cancellationToken = default) =>
            AnalyzeCapturedScreenAsync(activity, captureResult, keepCapture: true, origin, cancellationToken);
    }

    private sealed class ProviderFailureAnalysisService : IAiAnalysisService
    {
        private static AiProviderRequestException Failure() => new(
            "Provider rate limit.",
            new AiProviderFailure(
                "http_429.insufficient_quota",
                429,
                42,
                ProviderRequestId: "req_safe_test"));

        public Task<AiAnalysis> AnalyzeCurrentScreenAsync(
            AnalysisContextSnapshot? activity,
            bool allowCapture = true,
            string origin = "manual",
            CancellationToken cancellationToken = default) =>
            Task.FromException<AiAnalysis>(Failure());

        public Task<AiAnalysis> AnalyzeCapturedScreenAsync(
            AnalysisContextSnapshot? activity,
            ScreenshotCaptureResult captureResult,
            bool keepCapture,
            string origin,
            CancellationToken cancellationToken = default) =>
            Task.FromException<AiAnalysis>(Failure());

        public Task<AiAnalysis> AnalyzeHistoricalCapturedScreenAsync(
            AnalysisContextSnapshot activity,
            ScreenshotCaptureResult captureResult,
            string origin,
            CancellationToken cancellationToken = default) =>
            Task.FromException<AiAnalysis>(Failure());
    }

    private sealed class FailingAnalysisService : IAiAnalysisService
    {
        public Task<AiAnalysis> AnalyzeCurrentScreenAsync(
            AnalysisContextSnapshot? activity,
            bool allowCapture = true,
            string origin = "manual",
            CancellationToken cancellationToken = default) =>
            throw new HttpRequestException("Test-only provider failure.");

        public Task<AiAnalysis> AnalyzeCapturedScreenAsync(
            AnalysisContextSnapshot? activity,
            ScreenshotCaptureResult captureResult,
            bool keepCapture,
            string origin,
            CancellationToken cancellationToken = default) =>
            throw new HttpRequestException("Test-only provider failure.");

        public Task<AiAnalysis> AnalyzeHistoricalCapturedScreenAsync(
            AnalysisContextSnapshot activity,
            ScreenshotCaptureResult captureResult,
            string origin,
            CancellationToken cancellationToken = default) =>
            throw new HttpRequestException("Test-only provider failure.");
    }
}
