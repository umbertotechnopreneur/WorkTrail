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
using Microsoft.Extensions.Logging;
using WorkTrail.Application;
using WorkTrail.Services;
using Xunit;

namespace WorkTrail.Core.Tests;

public sealed class ScreenshotImageLoadingTests
{
    private const int MaximumScreenshotImageBytes = 10 * 1024 * 1024;

    /// <summary>Verifies that one retained artifact crosses the application boundary as exact bytes.</summary>
    [Fact]
    public async Task GetScreenshotImage_ReturnsExactOwnedArtifactBytes()
    {
        var dataDirectory = CreateTemporaryDirectory();
        try
        {
            var screenshotDirectory = Path.Combine(dataDirectory, "screenshots");
            var store = CreateStore(dataDirectory, screenshotDirectory);
            var screenshotPath = CreateOwnedArtifact(screenshotDirectory, [1, 3, 5, 7]);
            await using var application = CreateApplication(store);

            var result = await application.GetScreenshotImageAsync(
                new ScreenshotImageRequest(screenshotPath),
                CancellationToken.None);

            Assert.True(result.Succeeded);
            Assert.Equal("screenshot.image.loaded", result.Code);
            Assert.Equal(Path.GetFileNameWithoutExtension(screenshotPath), result.Value?.ArtifactIdentity);
            Assert.Equal(new byte[] { 1, 3, 5, 7 }, result.Value?.Content);
        }
        finally
        {
            await DeleteTemporaryDirectoryAsync(dataDirectory);
        }
    }

    /// <summary>Verifies that owned-looking paths outside the configured screenshot root cannot be read.</summary>
    [Fact]
    public async Task GetScreenshotImage_RejectsArtifactOutsideConfiguredRoot()
    {
        var dataDirectory = CreateTemporaryDirectory();
        try
        {
            var screenshotDirectory = Path.Combine(dataDirectory, "screenshots");
            var store = CreateStore(dataDirectory, screenshotDirectory);
            var outsidePath = CreateOwnedArtifact(Path.Combine(dataDirectory, "outside"), [2, 4, 6]);
            await using var application = CreateApplication(store);

            var result = await application.GetScreenshotImageAsync(
                new ScreenshotImageRequest(outsidePath),
                CancellationToken.None);

            Assert.False(result.Succeeded);
            Assert.Equal("screenshot.image.not_found", result.Code);
            Assert.Null(result.Value);
        }
        finally
        {
            await DeleteTemporaryDirectoryAsync(dataDirectory);
        }
    }

    /// <summary>Verifies that a retained image cannot exceed the bounded runtime payload contract.</summary>
    [Fact]
    public async Task GetScreenshotImage_RejectsArtifactAboveIpcSafeLimit()
    {
        var dataDirectory = CreateTemporaryDirectory();
        try
        {
            var screenshotDirectory = Path.Combine(dataDirectory, "screenshots");
            var store = CreateStore(dataDirectory, screenshotDirectory);
            var screenshotPath = CreateOwnedArtifact(screenshotDirectory, []);
            await using (var stream = new FileStream(screenshotPath, FileMode.Open, FileAccess.Write, FileShare.None))
            {
                stream.SetLength(MaximumScreenshotImageBytes + 1L);
            }

            await using var application = CreateApplication(store);
            var result = await application.GetScreenshotImageAsync(
                new ScreenshotImageRequest(screenshotPath),
                CancellationToken.None);

            Assert.False(result.Succeeded);
            Assert.Equal("screenshot.image.too_large", result.Code);
            Assert.Null(result.Value);
        }
        finally
        {
            await DeleteTemporaryDirectoryAsync(dataDirectory);
        }
    }

    /// <summary>Verifies that superseded presentation requests cancel before touching screenshot storage.</summary>
    [Fact]
    public async Task GetScreenshotImage_PropagatesCallerCancellation()
    {
        var dataDirectory = CreateTemporaryDirectory();
        try
        {
            var screenshotDirectory = Path.Combine(dataDirectory, "screenshots");
            var store = CreateStore(dataDirectory, screenshotDirectory);
            var screenshotPath = CreateOwnedArtifact(screenshotDirectory, [9]);
            var logger = new RecordingLogger<WorkTrailApplication>();
            await using var application = CreateApplication(store, logger);
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                application.GetScreenshotImageAsync(
                    new ScreenshotImageRequest(screenshotPath),
                    cancellation.Token));
            Assert.DoesNotContain(logger.Entries, entry => entry.Level >= LogLevel.Warning);
        }
        finally
        {
            await DeleteTemporaryDirectoryAsync(dataDirectory);
        }
    }

    /// <summary>Verifies that inaccessible retained bytes fail safely without exposing their local path in logs.</summary>
    [Fact]
    public async Task GetScreenshotImage_ReturnsSanitizedReadFailureWhenArtifactIsLocked()
    {
        var dataDirectory = CreateTemporaryDirectory();
        try
        {
            var screenshotDirectory = Path.Combine(dataDirectory, "screenshots");
            var store = CreateStore(dataDirectory, screenshotDirectory);
            var screenshotPath = CreateOwnedArtifact(screenshotDirectory, [2, 7, 1, 8]);
            var artifactIdentity = Path.GetFileNameWithoutExtension(screenshotPath);
            var logger = new RecordingLogger<WorkTrailApplication>();
            await using var application = CreateApplication(store, logger);
            await using var exclusiveLock = new FileStream(
                screenshotPath,
                FileMode.Open,
                FileAccess.ReadWrite,
                FileShare.None);

            var result = await application.GetScreenshotImageAsync(
                new ScreenshotImageRequest(screenshotPath),
                CancellationToken.None);

            Assert.False(result.Succeeded);
            Assert.Equal("screenshot.image.read_failed", result.Code);
            Assert.Null(result.Value);
            var entry = Assert.Single(logger.Entries, candidate => candidate.Level == LogLevel.Warning);
            Assert.Equal(LogLevel.Warning, entry.Level);
            Assert.Null(entry.Exception);
            Assert.Contains(artifactIdentity, entry.Message, StringComparison.Ordinal);
            Assert.Contains(nameof(IOException), entry.Message, StringComparison.Ordinal);
            Assert.DoesNotContain(screenshotPath, entry.Message, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(dataDirectory, entry.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            await DeleteTemporaryDirectoryAsync(dataDirectory);
        }
    }

    private static LocalStore CreateStore(string dataDirectory, string screenshotDirectory)
    {
        Directory.CreateDirectory(screenshotDirectory);
        var store = new LocalStore(dataDirectory);
        store.SaveSettings(store.LoadSettings() with { ScreenshotDirectory = screenshotDirectory });
        return store;
    }

    private static string CreateOwnedArtifact(string directory, byte[] content)
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(
            directory,
            $"{Guid.NewGuid():N}_1.0.0_scheduled_monitor-1.webp");
        File.WriteAllBytes(path, content);
        return path;
    }

    private static WorkTrailApplication CreateApplication(
        LocalStore store,
        ILogger<WorkTrailApplication>? logger = null) =>
        new(
            store,
            new UtilityService(),
            new TrackingDomainService(store),
            new UnexpectedCaptureService(),
            new FakeHardwareTelemetryService(),
            new UnexpectedAnalysisService(),
            new StartupService(),
            new BuildInformationService(),
            logger: logger,
            startScheduledSnapshotTimer: false);

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

    private sealed class UnexpectedCaptureService : IScreenCaptureService
    {
        /// <inheritdoc />
        public ScreenshotCaptureResult CaptureByMode(
            string directory,
            string captureMode,
            string captureOrigin,
            Func<ScreenshotCaptureContext, ScreenshotCaptureDecision> authorizeCapture) =>
            throw new InvalidOperationException("Image loading must not capture a new screenshot.");
    }

    private sealed class UnexpectedAnalysisService : IAiAnalysisService
    {
        /// <inheritdoc />
        public Task<AiAnalysis> AnalyzeCurrentScreenAsync(
            AnalysisContextSnapshot? activity,
            bool allowCapture = true,
            string origin = "manual",
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Image loading must not start AI analysis.");

        /// <inheritdoc />
        public Task<AiAnalysis> AnalyzeCapturedScreenAsync(
            AnalysisContextSnapshot? activity,
            ScreenshotCaptureResult captureResult,
            bool keepCapture,
            string origin,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Image loading must not start AI analysis.");

        /// <inheritdoc />
        public Task<AiAnalysis> AnalyzeHistoricalCapturedScreenAsync(
            AnalysisContextSnapshot activity,
            ScreenshotCaptureResult captureResult,
            string origin,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Image loading must not start AI analysis.");
    }

    private sealed class RecordingLogger<T> : ILogger<T>
    {
        internal List<(LogLevel Level, string Message, Exception? Exception)> Entries { get; } = [];

        /// <inheritdoc />
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        /// <inheritdoc />
        public bool IsEnabled(LogLevel logLevel) => true;

        /// <inheritdoc />
        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            Entries.Add((logLevel, formatter(state, exception), exception));
    }
}
