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
using WorkTrail.Services;
using Xunit;

namespace WorkTrail.Core.Tests;

[Collection(ProcessEnvironmentCollection.Name)]
public sealed class OpenAiAnalysisCleanupTests
{
    private const string TestApiKeyVariable = "WORKTRAIL_OPENAI_APIKEY";

    [Fact]
    public async Task RetainedCapture_WithDistinctAnalysisArtifact_DeletesOnlyAnalysisArtifact()
    {
        var dataDirectory = CreateTemporaryDirectory();
        var previousApiKey = Environment.GetEnvironmentVariable(TestApiKeyVariable, EnvironmentVariableTarget.Process);
        Environment.SetEnvironmentVariable(TestApiKeyVariable, "test-only-key", EnvironmentVariableTarget.Process);

        try
        {
            var store = CreateStore(dataDirectory, openAiEnabled: true);
            var capture = CreateDistinctArtifactCapture(dataDirectory);
            var service = new OpenAiAnalysisService(store, new UnexpectedCaptureService(), decoder: new SuccessfulDecoder());

            var analysis = await service.AnalyzeCapturedScreenAsync(
                activity: null,
                capture,
                keepCapture: true,
                origin: "snapshot.manual",
                CancellationToken.None);

            Assert.False(File.Exists(capture.AnalysisScreenshotPaths[0]));
            Assert.True(File.Exists(capture.StoredScreenshotPaths[0]));
            Assert.Equal(capture.StoredScreenshotPaths[0], analysis.ScreenshotPaths);
        }
        finally
        {
            Environment.SetEnvironmentVariable(TestApiKeyVariable, previousApiKey, EnvironmentVariableTarget.Process);
            Directory.Delete(dataDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task InvalidConfigurationBeforeProvider_DeletesTransientCapture()
    {
        var dataDirectory = CreateTemporaryDirectory();
        try
        {
            var store = CreateStore(dataDirectory, openAiEnabled: false);
            var capture = CreateDistinctArtifactCapture(dataDirectory);
            var service = new OpenAiAnalysisService(store, new UnexpectedCaptureService(), decoder: new SuccessfulDecoder());

            await Assert.ThrowsAsync<InvalidOperationException>(() => service.AnalyzeCapturedScreenAsync(
                activity: null,
                capture,
                keepCapture: false,
                origin: "snapshot.manual",
                CancellationToken.None));

            Assert.All(capture.AllScreenshotPaths, path => Assert.False(File.Exists(path)));
        }
        finally
        {
            Directory.Delete(dataDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task Cancellation_ReachesDecoderAndDeletesTransientCapture()
    {
        var dataDirectory = CreateTemporaryDirectory();
        var previousApiKey = Environment.GetEnvironmentVariable(TestApiKeyVariable, EnvironmentVariableTarget.Process);
        Environment.SetEnvironmentVariable(TestApiKeyVariable, "test-only-key", EnvironmentVariableTarget.Process);

        try
        {
            var store = CreateStore(dataDirectory, openAiEnabled: true);
            var capture = CreateDistinctArtifactCapture(dataDirectory);
            var decoder = new CancellationDecoder();
            var service = new OpenAiAnalysisService(store, new UnexpectedCaptureService(), decoder: decoder);
            using var cancellation = new CancellationTokenSource();

            var task = service.AnalyzeCapturedScreenAsync(
                activity: null,
                capture,
                keepCapture: false,
                origin: "snapshot.manual",
                cancellation.Token);
            await decoder.Entered;
            cancellation.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
            Assert.True(decoder.CancellationObserved);
            Assert.All(capture.AllScreenshotPaths, path => Assert.False(File.Exists(path)));
        }
        finally
        {
            Environment.SetEnvironmentVariable(TestApiKeyVariable, previousApiKey, EnvironmentVariableTarget.Process);
            Directory.Delete(dataDirectory, recursive: true);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancellationAfterResponse_PreservesShutdownCheckpointButDiscardsRevokedPolicy(bool revokedPolicy)
    {
        var dataDirectory = CreateTemporaryDirectory();
        var previousApiKey = Environment.GetEnvironmentVariable(TestApiKeyVariable, EnvironmentVariableTarget.Process);
        Environment.SetEnvironmentVariable(TestApiKeyVariable, "test-only-key", EnvironmentVariableTarget.Process);

        try
        {
            var store = CreateStore(dataDirectory, openAiEnabled: true);
            var capture = CreateDistinctArtifactCapture(dataDirectory);
            using var cancellation = new CancellationTokenSource();
            var service = new OpenAiAnalysisService(
                store,
                new UnexpectedCaptureService(),
                decoder: new PostResponseCancellationDecoder(cancellation));

            var pending = AiPolicyCancellation.RunAsync(() => service.AnalyzeCapturedScreenAsync(
                activity: null,
                capture,
                keepCapture: true,
                origin: "snapshot.reprocess",
                cancellation.Token), revokedPolicy ? cancellation.Token : CancellationToken.None);

            if (revokedPolicy)
            {
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
                Assert.Null(store.LoadLatestAnalysis());
                return;
            }
            var analysis = await pending;

            Assert.True(cancellation.IsCancellationRequested);
            Assert.Equal(capture.CaptureId, analysis.CorrelationId);
            Assert.Equal(capture.CaptureId, store.LoadLatestAnalysis()?.CorrelationId);
        }
        finally
        {
            Environment.SetEnvironmentVariable(TestApiKeyVariable, previousApiKey, EnvironmentVariableTarget.Process);
            Directory.Delete(dataDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task ProviderFailure_LogsOnlyStructuredRedactedAttemptMetadata()
    {
        var dataDirectory = CreateTemporaryDirectory();
        var previousApiKey = Environment.GetEnvironmentVariable(TestApiKeyVariable, EnvironmentVariableTarget.Process);
        const string apiKey = "sk-test-only-private-secret";
        Environment.SetEnvironmentVariable(TestApiKeyVariable, apiKey, EnvironmentVariableTarget.Process);

        try
        {
            var store = CreateStore(dataDirectory, openAiEnabled: true);
            var capture = CreateDistinctArtifactCapture(dataDirectory);
            var logger = new RecordingLogger<OpenAiAnalysisService>();
            var service = new OpenAiAnalysisService(
                store,
                new UnexpectedCaptureService(),
                decoder: new ProviderFailureDecoder(),
                logger: logger);

            await Assert.ThrowsAsync<AiProviderRequestException>(() => service.AnalyzeCapturedScreenAsync(
                activity: null,
                capture,
                keepCapture: false,
                origin: "snapshot.scheduled",
                CancellationToken.None));

            var log = string.Join(Environment.NewLine, logger.Messages);
            Assert.Contains("Attempt=", log, StringComparison.Ordinal);
            Assert.Contains("Correlation=", log, StringComparison.Ordinal);
            Assert.Contains("Origin=snapshot.scheduled", log, StringComparison.Ordinal);
            Assert.Contains("HttpStatus=429", log, StringComparison.Ordinal);
            Assert.Contains("FailureCategory=http_429.insufficient_quota", log, StringComparison.Ordinal);
            Assert.Contains("ProviderRequestId=req_safe_test", log, StringComparison.Ordinal);
            Assert.DoesNotContain(apiKey, log, StringComparison.Ordinal);
            Assert.DoesNotContain(dataDirectory, log, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("input_text", log, StringComparison.Ordinal);
        }
        finally
        {
            Environment.SetEnvironmentVariable(TestApiKeyVariable, previousApiKey, EnvironmentVariableTarget.Process);
            Directory.Delete(dataDirectory, recursive: true);
        }
    }

    private static LocalStore CreateStore(string dataDirectory, bool openAiEnabled)
    {
        var store = new LocalStore(dataDirectory);
        store.SaveSettings(store.LoadSettings() with
        {
            OpenAiEnabled = openAiEnabled,
            AiApiKeyName = TestApiKeyVariable,
            IncludeDeviceLocation = false,
            ScreenshotDirectory = dataDirectory
        });
        return store;
    }

    private static ScreenshotCaptureResult CreateDistinctArtifactCapture(string directory)
    {
        var captureId = Guid.NewGuid().ToString("N");
        var analysisPath = Path.Combine(directory, $"{captureId}_1.0.0_manual_monitor-1-raw.webp");
        var storedPath = Path.Combine(directory, $"{captureId}_1.0.0_manual_monitor-1.webp");
        File.WriteAllBytes(analysisPath, [1, 2, 3]);
        File.WriteAllBytes(storedPath, [4, 5, 6]);
        return new ScreenshotCaptureResult(
            captureId,
            [analysisPath],
            [storedPath],
            ScreenshotCaptureOrigins.Manual);
    }

    private static string CreateTemporaryDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "WorkTrail.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private sealed class UnexpectedCaptureService : IScreenCaptureService
    {
        /// <inheritdoc />
        public ScreenshotCaptureResult CaptureByMode(
            string directory,
            string captureMode,
            string captureOrigin,
            Func<ScreenshotCaptureContext, ScreenshotCaptureDecision> authorizeCapture) =>
            throw new InvalidOperationException("The supplied capture must be reused.");
    }

    private sealed class SuccessfulDecoder : IAIDecoder
    {
        public string Provider => "openai";

        public Task<AiProviderResult> DecodeAsync(
            string prompt,
            IReadOnlyList<string> screenshotPaths,
            AppSettings settings,
            string apiKey,
            string correlationId,
            AiProviderRequestOptions? requestOptions = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new AiProviderResult(
                "analyzed",
                new AiUsageMetrics(),
                "response-id",
                "request-id",
                settings.Model,
                "completed",
                200,
                1,
                null));
    }

    private sealed class CancellationDecoder : IAIDecoder
    {
        private readonly TaskCompletionSource<bool> _entered = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public string Provider => "openai";

        public Task Entered => _entered.Task;

        public bool CancellationObserved { get; private set; }

        public async Task<AiProviderResult> DecodeAsync(
            string prompt,
            IReadOnlyList<string> screenshotPaths,
            AppSettings settings,
            string apiKey,
            string correlationId,
            AiProviderRequestOptions? requestOptions = null,
            CancellationToken cancellationToken = default)
        {
            _entered.TrySetResult(true);
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                throw new InvalidOperationException("Cancellation decoder unexpectedly completed.");
            }
            catch (OperationCanceledException)
            {
                CancellationObserved = true;
                throw;
            }
        }
    }

    private sealed class PostResponseCancellationDecoder(CancellationTokenSource cancellation) : IAIDecoder
    {
        public string Provider => "openai";

        public Task<AiProviderResult> DecodeAsync(
            string prompt,
            IReadOnlyList<string> screenshotPaths,
            AppSettings settings,
            string apiKey,
            string correlationId,
            AiProviderRequestOptions? requestOptions = null,
            CancellationToken cancellationToken = default)
        {
            cancellation.Cancel();
            return Task.FromResult(new AiProviderResult(
                "analyzed",
                new AiUsageMetrics(),
                "response-id",
                "request-id",
                settings.Model,
                "completed",
                200,
                1,
                null));
        }
    }

    private sealed class ProviderFailureDecoder : IAIDecoder
    {
        public string Provider => "openai";

        public Task<AiProviderResult> DecodeAsync(
            string prompt,
            IReadOnlyList<string> screenshotPaths,
            AppSettings settings,
            string apiKey,
            string correlationId,
            AiProviderRequestOptions? requestOptions = null,
            CancellationToken cancellationToken = default) =>
            Task.FromException<AiProviderResult>(new AiProviderRequestException(
                "Provider rate limit.",
                new AiProviderFailure(
                    "http_429.insufficient_quota",
                    429,
                    87,
                    ProviderRequestId: "req_safe_test")));
    }

    private sealed class RecordingLogger<T> : ILogger<T>
    {
        public List<string> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            Messages.Add(formatter(state, exception));
    }
}
