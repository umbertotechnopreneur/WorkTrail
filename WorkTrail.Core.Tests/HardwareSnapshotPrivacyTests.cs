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
using System.Threading;
using System.Threading.Tasks;
using WorkTrail.Application;
using WorkTrail.Services;
using Xunit;

namespace WorkTrail.Core.Tests;

/// <summary>Verifies that captured location never bypasses the current AI privacy preference.</summary>
[Collection(ProcessEnvironmentCollection.Name)]
public sealed class HardwareSnapshotPrivacyTests
{
    private const string TestApiKeyVariable = "OPENAI_API_KEY";
    private const string TestApiKey = "sk-test-only-hardware-privacy-1234567890";

    /// <summary>Deferred, historical and context-only requests redact revoked location without changing storage.</summary>
    [Theory]
    [InlineData("deferred", false)]
    [InlineData("historical", false)]
    [InlineData("context-only", false)]
    [InlineData("deferred", true)]
    [InlineData("historical", true)]
    [InlineData("context-only", true)]
    public async Task Analysis_AppliesCurrentLocationPreferenceToFrozenContext(string mode, bool includeLocation)
    {
        var directory = Path.Combine(Path.GetTempPath(), "WorkTrail.PrivacyTests", Guid.NewGuid().ToString("N"));
        var previousKey = Environment.GetEnvironmentVariable(TestApiKeyVariable, EnvironmentVariableTarget.Process);
        Environment.SetEnvironmentVariable(TestApiKeyVariable, TestApiKey, EnvironmentVariableTarget.Process);
        try
        {
            var store = new LocalStore(directory);
            store.SaveSettings(store.LoadSettings() with
            {
                OpenAiEnabled = true,
                IncludeDeviceLocation = true,
                AiApiKeyName = TestApiKeyVariable,
                ScreenshotDirectory = directory
            });
            Assert.Equal(TestApiKeyVariable, store.LoadSettings().AiApiKeyName);
            var capturedAt = DateTimeOffset.UtcNow.AddMinutes(-2);
            var captureId = Guid.NewGuid().ToString("N");
            var screenshot = Path.Combine(directory, $"{captureId}_1.0.0_manual_monitor-1.webp");
            File.WriteAllBytes(screenshot, [1, 2, 3]);
            File.SetLastWriteTimeUtc(screenshot, capturedAt.UtcDateTime);
            var snapshot = HardwareTestData.Snapshot(capturedAt) with { DeviceContext = TestContext() };
            store.UpsertScreenshotIntervalTelemetry(captureId, [screenshot],
                new ScreenshotIntervalTelemetry(capturedAt.AddMinutes(-5), capturedAt, 25, 15), snapshot);
            var frozen = store.LoadCaptureHardwareSnapshot(captureId)!;
            var context = new AnalysisContextSnapshot("Editor", "Work", "Window", "active", null,
                Snapshot: mode == "deferred" ? null : frozen);
            var capture = new ScreenshotCaptureResult(captureId, [screenshot], [screenshot], ScreenshotCaptureOrigins.Manual,
                CapturedAt: capturedAt, HardwareSnapshot: mode == "deferred" ? frozen : null);
            store.SaveSettings(store.LoadSettings() with { IncludeDeviceLocation = includeLocation });
            var decoder = new PromptRecordingDecoder();
            var service = new OpenAiAnalysisService(store, new UnexpectedCapture(), decoder);

            var result = mode switch
            {
                "historical" => await service.AnalyzeHistoricalCapturedScreenAsync(context, capture, "snapshot.reprocess"),
                "context-only" => await service.AnalyzeCurrentScreenAsync(context, allowCapture: false),
                _ => await service.AnalyzeCapturedScreenAsync(context, capture, keepCapture: true, "snapshot.manual")
            };

            Assert.NotNull(decoder.Prompt);
            Assert.Contains("Europe/Rome", decoder.Prompt, StringComparison.Ordinal);
            Assert.Contains("it-IT", decoder.Prompt, StringComparison.Ordinal);
            Assert.Contains("CPU Total", decoder.Prompt, StringComparison.Ordinal);
            Assert.Equal(includeLocation, decoder.Prompt.Contains("12.345678", StringComparison.Ordinal));
            Assert.Equal(includeLocation, decoder.Prompt.Contains("98.765432", StringComparison.Ordinal));
            Assert.Equal(includeLocation ? (double?)12.345678 : null, result.Snapshot!.DeviceContext!.Location.Latitude);
            if (!includeLocation)
            {
                Assert.Null(result.Snapshot.DeviceContext.Location.Longitude);
                Assert.Null(result.Snapshot.DeviceContext.Location.AccuracyMeters);
                Assert.Equal("disabled_by_setting", result.Snapshot.DeviceContext.Location.Status);
            }

            // Request-time redaction does not rewrite the original capture or the caller's shared object.
            Assert.Equal(12.345678, frozen.DeviceContext!.Location.Latitude);
            Assert.Equal(12.345678, store.LoadCaptureHardwareSnapshot(captureId)!.DeviceContext!.Location.Latitude);
        }
        finally
        {
            Environment.SetEnvironmentVariable(TestApiKeyVariable, previousKey, EnvironmentVariableTarget.Process);
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>Hardware capture only asks Windows for coordinates when AI and location sharing are both enabled.</summary>
    [Theory]
    [InlineData(false, true, 0)]
    [InlineData(true, false, 0)]
    [InlineData(true, true, 1)]
    public async Task ScreenshotCapture_CollectsLocationOnlyWithActiveAiOptIn(bool aiEnabled, bool includeLocation, int expectedLocationReads)
    {
        var directory = Path.Combine(Path.GetTempPath(), "WorkTrail.PrivacyTests", Guid.NewGuid().ToString("N"));
        try
        {
            var store = new LocalStore(directory);
            store.SaveSettings(store.LoadSettings() with
            {
                ScreenshotsEnabled = true,
                OpenAiEnabled = aiEnabled,
                IncludeDeviceLocation = includeLocation,
                ScreenshotDirectory = directory
            });
            var capture = new SingleCapture();
            var platform = new RecordingDevicePlatform();
            await using var application = new WorkTrailApplication(
                store, new UtilityService(), new TrackingDomainService(store), capture,
                new FakeHardwareTelemetryService(), new OpenAiAnalysisService(store, capture),
                new StartupService(), new BuildInformationService(),
                deviceContext: new DeviceContextService(platform), startScheduledSnapshotTimer: false);

            var result = await application.CaptureScreenshotAsync(
                new CaptureScreenshotRequest("all-screens", Keep: true, CaptureOrigin: ScreenshotCaptureOrigins.Manual,
                    DeferAiAnalysis: true), CancellationToken.None);

            Assert.True(result.Succeeded);
            Assert.Equal(expectedLocationReads, platform.LocationReadCount);
            var context = result.Value!.HardwareSnapshot!.DeviceContext!;
            Assert.Equal("Europe/Rome", context.TimeZone.Value);
            Assert.Equal(expectedLocationReads == 1 ? (double?)12.345678 : null, context.Location.Latitude);
            Assert.Equal(expectedLocationReads == 1 ? "available" : "disabled_by_setting", context.Location.Status);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static DeviceContextSnapshot TestContext() => new(
        new DeviceContextValue("Europe/Rome", "windows-time-zone", "available"),
        new DeviceContextValue("it-IT", "windows-user-ui-language", "available"),
        new DeviceContextValue("it-IT", "windows-foreground-keyboard-layout", "available"),
        new DeviceLocationSnapshot(12.345678, 98.765432, 25, "windows-geolocator", "available"));

    private sealed class PromptRecordingDecoder : IAIDecoder
    {
        public string Provider => "openai";
        internal string? Prompt { get; private set; }

        /// <inheritdoc />
        public Task<AiProviderResult> DecodeAsync(string prompt, IReadOnlyList<string> screenshotPaths, AppSettings settings,
            string apiKey, string correlationId, AiProviderRequestOptions? requestOptions = null, CancellationToken cancellationToken = default)
        {
            Assert.Equal(TestApiKey, apiKey);
            Prompt = prompt;
            return Task.FromResult(new AiProviderResult("Recorded", new AiUsageMetrics(), null, null, settings.Model, "completed", 200, 1, null));
        }
    }

    private sealed class UnexpectedCapture : IScreenCaptureService
    {
        /// <inheritdoc />
        public ScreenshotCaptureResult CaptureByMode(string directory, string captureMode, string captureOrigin,
            Func<ScreenshotCaptureContext, ScreenshotCaptureDecision> authorizeCapture) =>
            throw new InvalidOperationException("A frozen-context analysis must not take a new screenshot.");
    }

    private sealed class SingleCapture : IScreenCaptureService
    {
        /// <inheritdoc />
        public ScreenshotCaptureResult CaptureByMode(string directory, string captureMode, string captureOrigin,
            Func<ScreenshotCaptureContext, ScreenshotCaptureDecision> authorizeCapture)
        {
            var decision = authorizeCapture(ScreenshotCaptureContext.Unavailable);
            if (decision != ScreenshotCaptureDecision.Allowed) throw new ScreenshotCapturePreconditionException(decision);
            var captureId = Guid.NewGuid().ToString("N");
            var capturedAt = DateTimeOffset.UtcNow;
            var day = ScreenshotStorageLayout.GetDayDirectory(directory, capturedAt);
            Directory.CreateDirectory(day);
            var path = Path.Combine(day, $"{captureId}_1.0.0_{captureOrigin}_monitor-1.webp");
            File.WriteAllBytes(ScreenshotPublicationJournal.StagingPath(path), [1, 2, 3]);
            return new ScreenshotCaptureResult(captureId, [path], [path], captureOrigin, CapturedAt: capturedAt);
        }
    }

    private sealed class RecordingDevicePlatform : IDeviceContextPlatform
    {
        internal int LocationReadCount { get; private set; }
        /// <inheritdoc />
        public DeviceContextValue GetTimeZone() => TestContext().TimeZone;
        /// <inheritdoc />
        public DeviceContextValue GetWindowsUiLanguage() => TestContext().WindowsUiLanguage;
        /// <inheritdoc />
        public DeviceContextValue GetActiveInputLanguage() => TestContext().InputLanguage;
        /// <inheritdoc />
        public Task<DeviceLocationSnapshot> GetCurrentLocationAsync(CancellationToken cancellationToken)
        {
            LocationReadCount++;
            return Task.FromResult(TestContext().Location);
        }
        /// <inheritdoc />
        public Task<DeviceLocationAccessResult> RequestLocationAccessAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new DeviceLocationAccessResult("windows-geolocator", "allowed"));
    }
}
