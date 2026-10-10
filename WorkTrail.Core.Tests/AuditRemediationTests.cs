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
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using WorkTrail.Application;
using WorkTrail.Providers;
using WorkTrail.Runtime;
using WorkTrail.Search;
using WorkTrail.Services;
using Xunit;

namespace WorkTrail.Core.Tests;

[Collection(ProcessEnvironmentCollection.Name)]
public sealed class AuditRemediationTests : IDisposable
{
    private readonly List<string> _roots = [];

    [Theory]
    [InlineData("process")]
    [InlineData("title")]
    [InlineData("hint")]
    public void ExcludedActivityNeverReachesStorageOrCurrentContext(string rule)
    {
        var store = CreateStore();
        var snapshot = new SettingsSnapshot(store.LoadSettings());
        using var tracking = new TrackingDomainService(store, snapshot);
        Assert.True(tracking.TryPersistActivitySample(Sample(store, "allowed", DateTimeOffset.UtcNow)));
        snapshot.Replace(snapshot.Value with
        {
            PrivacyProcessNames = rule == "process" ? "rule|private" : "",
            PrivacyWindowTitles = rule == "title" ? "rule|private" : "",
            PrivacyWindowHints = rule == "hint" ? "rule|private" : ""
        });
        Assert.True(tracking.TryPersistActivitySample(Sample(store, "private", DateTimeOffset.UtcNow)));
        Assert.Equal("allowed", store.LoadLatestSample()!.ProcessName);
        Assert.Null(tracking.LatestAnalysisContext);
    }

    [Theory]
    [InlineData("WINWORD")]
    [InlineData("EXCEL")]
    [InlineData("Code")]
    [InlineData("Code - Insiders")]
    [InlineData("chrome")]
    [InlineData("msedge")]
    [InlineData("firefox")]
    public void DisabledDetailsStripAllPersistedTitleChannels(string process)
    {
        var store = CreateStore();
        var snapshot = new SettingsSnapshot(store.LoadSettings());
        using var hooks = new InputHookService();
        using var monitor = new ActivityMonitorService(store, hooks, snapshot);
        snapshot.Replace(snapshot.Value with
        {
            EnableWordDetailPlugin = false,
            EnableExcelDetailPlugin = false,
            EnableVsCodeDetailPlugin = false,
            EnableBrowserDetailPlugin = false
        });
        var context = new ActivityContextProviderRegistry().Resolve(new ForegroundWindowInfo(process, "private title"), snapshot.Value);
        Assert.Empty(context.Context);
        monitor.PersistSample(Sample(store, "private title", DateTimeOffset.UtcNow) with
        {
            ProcessName = process,
            Attributes = new Dictionary<string, string> { ["Title"] = "private title" },
            KeyPresses = 7
        });
        var persisted = store.LoadLatestSample()!;
        Assert.Empty(persisted.WindowTitle);
        Assert.Empty(persisted.Context);
        Assert.Null(persisted.Attributes);
        Assert.Equal(7, persisted.KeyPresses);
    }

    [Fact]
    public void CaptureRejectsExcludedNonForegroundWindowOnAnotherMonitor()
    {
        var settings = new AppSettings(ScreenshotsEnabled: true, PrivacyProcessNames: "rule|vault");
        var area = new NativeMethods.Rect { Left = 1920, Top = 0, Right = 3840, Bottom = 1080 };
        var privateWindow = new ScreenCaptureService.VisibleCaptureWindow(
            new NativeMethods.Rect { Left = 2000, Top = 10, Right = 2500, Bottom = 500 },
            new ScreenshotCaptureContext("vault", "Vault", "Secrets", "Secrets"));
        var exception = Assert.Throws<ScreenshotCapturePreconditionException>(() => ScreenCaptureService.AuthorizeVisibleWindows(
            area, [privateWindow], context => TrackingDomainService.EvaluateScreenshotCapture(settings, context)));
        Assert.Equal(ScreenshotCaptureDecision.PrivacyBlocked, exception.Decision);
        var otherMonitor = new NativeMethods.Rect { Left = 0, Top = 0, Right = 1920, Bottom = 1080 };
        ScreenCaptureService.AuthorizeVisibleWindows(otherMonitor, [privateWindow],
            context => TrackingDomainService.EvaluateScreenshotCapture(settings, context));
    }

    [Theory]
    [InlineData(false, "openai", 0)]
    [InlineData(true, "openrouter", 0)]
    [InlineData(true, "openai", 1)]
    public async Task PricingUsesOnlyEnabledSelectedProvider(bool enabled, string provider, int expectedRequests)
    {
        var store = CreateStore();
        store.SaveSettings(store.LoadSettings() with { OpenAiEnabled = enabled, AiProvider = provider });
        using var handler = new PricingHandler();
        using var http = new HttpClient(handler);
        await using var service = new OpenAiPricingRefreshService(store, httpClient: http);
        await service.RefreshIfStaleAsync(CancellationToken.None);
        Assert.Equal(expectedRequests, handler.Requests);
        store.SaveSettings(store.LoadSettings() with { OpenAiEnabled = false });
        await service.RefreshAsync(CancellationToken.None);
        Assert.Equal(expectedRequests, handler.Requests);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InterruptedDeletionCompletesOnRetryOrRestart(bool restart)
    {
        var store = CreateStore();
        var capture = CreateCapture(store, DateTimeOffset.UtcNow);
        var path = capture.StoredScreenshotPaths[0];
        await using (var application = CreateApplication(store))
        {
            ExecuteSql(store, "CREATE TRIGGER test_fail_delete BEFORE DELETE ON screenshot_text_snapshots BEGIN SELECT RAISE(ABORT, 'test fault'); END;");
            await Assert.ThrowsAsync<SqliteException>(() => application.DeleteScreenshotAsync(path, CancellationToken.None));
            Assert.False(File.Exists(path));
            Assert.NotNull(store.LoadScreenshotTextSnapshot(path));
            ExecuteSql(store, "DROP TRIGGER test_fail_delete;");
            if (!restart)
                Assert.True((await application.DeleteScreenshotAsync(path, CancellationToken.None)).Succeeded);
        }
        await using var reopened = CreateApplication(store);
        Assert.Null(store.LoadScreenshotTextSnapshot(path));
        Assert.Empty(new ScreenshotDeletionJournal(store).Pending());
    }

    [Fact]
    public async Task RetentionExpiresOrphanOcrAndCommitsSearchDeletion()
    {
        var store = CreateStore();
        var capture = CreateCapture(store, DateTimeOffset.UtcNow.AddDays(-60));
        var path = capture.StoredScreenshotPaths[0];
        File.Delete(path);
        store.AppendSample(Sample(store, "syntheticconfidential", DateTimeOffset.UtcNow.AddDays(-60)));
        Assert.True(store.GetRetentionPreview(DateTimeOffset.UtcNow.AddDays(-30)).RecordCount >= 2);
        await using (var application = CreateApplication(store))
        {
            Assert.True((await application.SearchAsync(new SearchRequest { Text = "syntheticconfidential" }, CancellationToken.None)).Value!.TotalCount > 0);
            Assert.True((await application.RunRetentionAsync(new RetentionRequest(true, true), CancellationToken.None)).Succeeded);
            Assert.Null(store.LoadScreenshotTextSnapshot(path));
        }
        await using var search = new LocalSearchService(new SearchOptions { IndexRootPath = store.SearchIndexRootDirectory });
        Assert.Empty((await search.SearchAsync(new SearchRequest { Text = "syntheticconfidential" })).Hits);
    }

    [Fact]
    public async Task PauseAndDisableCompleteWhileProviderIgnoresCancellation()
    {
        var previousKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY", EnvironmentVariableTarget.Process);
        var provider = new BlockedAnalysis();
        try
        {
            Environment.SetEnvironmentVariable("OPENAI_API_KEY", "sk-test-only-key-1234567890", EnvironmentVariableTarget.Process);
            var store = CreateStore();
            store.SaveSettings(store.LoadSettings() with { OpenAiEnabled = true, OcrEnabled = false });
            var capture = CreateCapture(store, DateTimeOffset.UtcNow);
            await using var application = CreateApplication(store, provider);
            var analysis = application.AnalyzeCapturedScreenshotAsync(new AnalyzeCapturedScreenshotRequest(capture, true), CancellationToken.None);
            try
            {
                var first = await Task.WhenAny(provider.Started.Task, analysis).WaitAsync(TimeSpan.FromSeconds(10));
                Assert.Same(provider.Started.Task, first);
                Assert.True((await application.PauseTrackingAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(2))).Succeeded);
                Assert.True((await application.SetAiEnabledAsync(false, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(2))).Succeeded);
                Assert.False(store.LoadSettings().OpenAiEnabled);
                Assert.True(provider.Token.IsCancellationRequested);
                Assert.False(analysis.IsCompleted);
            }
            finally { provider.Release.TrySetResult(true); }
            Assert.False((await analysis.WaitAsync(TimeSpan.FromSeconds(5))).Succeeded);
        }
        finally
        {
            provider.Release.TrySetResult(true);
            Environment.SetEnvironmentVariable("OPENAI_API_KEY", previousKey, EnvironmentVariableTarget.Process);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task IncompleteIpcFrameExpiresWithoutHostShutdown(bool sendHeader)
    {
        var store = CreateStore();
        await using var application = CreateApplication(store);
        var endpoint = RuntimeProtocol.CreateEndpoint("isolated-regression-" + Guid.NewGuid().ToString("N"));
        var server = new RuntimePipeServer(endpoint, new RuntimeRequestDispatcher(application, NullLogger.Instance),
            NullLogger.Instance, _ => throw new InvalidOperationException(), maximumConnections: 1, frameTimeout: TimeSpan.FromMilliseconds(250));
        using var shutdown = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var serving = server.ServeAsync(shutdown.Token);
        try
        {
            await using var client = new NamedPipeClientStream(".", endpoint.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            await client.ConnectAsync(2000, shutdown.Token);
            if (sendHeader)
            {
                var header = new byte[4];
                BinaryPrimitives.WriteInt32LittleEndian(header, RuntimeProtocol.MaximumMessageBytes);
                await client.WriteAsync(header, shutdown.Token);
                await client.FlushAsync(shutdown.Token);
            }
            var read = await client.ReadAsync(new byte[1], shutdown.Token).AsTask().WaitAsync(TimeSpan.FromSeconds(3));
            Assert.Equal(0, read);
            // A new client can occupy the released slot without restarting the host.
            await using var next = new NamedPipeClientStream(".", endpoint.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            await next.ConnectAsync(2000, shutdown.Token);
        }
        finally
        {
            shutdown.Cancel();
            try { await serving; } catch (OperationCanceledException) { }
            await server.DrainRequestsAsync();
        }
    }

    private LocalStore CreateStore()
    {
        var root = Path.Combine(Path.GetTempPath(), "WorkTrail.Tests", Guid.NewGuid().ToString("N"));
        _roots.Add(root);
        var store = new LocalStore(root);
        store.SaveSettings(store.LoadSettings() with
        {
            ScreenshotDirectory = Path.Combine(root, "screenshots"),
            IncludeDeviceLocation = false,
            WorldClockWeatherEnabled = false,
            DataRetentionDays = 30,
            ScreenshotRetentionDays = 30
        });
        return store;
    }

    private static ActivitySample Sample(LocalStore store, string text, DateTimeOffset timestamp) =>
        new(timestamp, 5, "active", text, text, text, text, store.LoadSettings().InstallationId, 0, 0);

    private static ScreenshotCaptureResult CreateCapture(LocalStore store, DateTimeOffset timestamp)
    {
        var id = Guid.NewGuid().ToString("N");
        var day = ScreenshotStorageLayout.GetDayDirectory(store.LoadSettings().ScreenshotDirectory, timestamp);
        Directory.CreateDirectory(day);
        var path = Path.Combine(day, $"{id}_1.0.0_manual_monitor-1.webp");
        File.WriteAllBytes(path, [1, 2, 3]); // Synthetic bytes: never decoded or sent over the network.
        store.RegisterScreenshotCapture(id, store.LoadSettings().InstallationId, timestamp, ScreenshotCaptureOrigins.Manual);
        store.UpsertScreenshotTextSnapshot(id, new ScreenshotTextSnapshot(path, new OcrRawSnapshot(
            ScreenshotTextExtractionStatus.Succeeded, "syntheticconfidential OCR", "en-US", null, timestamp, "test", 1, 1, [])));
        return new ScreenshotCaptureResult(id, [path], [path], ScreenshotCaptureOrigins.Manual, CapturedAt: timestamp);
    }

    private static void ExecuteSql(LocalStore store, string sql)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = store.ActivityDatabasePath, Pooling = false }.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static WorkTrailApplication CreateApplication(LocalStore store, IAiAnalysisService? analysis = null) =>
        new(store, new UtilityService(), new TrackingDomainService(store), new NoCapture(), new FakeHardwareTelemetryService(),
            analysis ?? new NoAnalysis(), new StartupService(), new BuildInformationService(), startScheduledSnapshotTimer: false);

    /// <inheritdoc />
    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        foreach (var root in _roots) if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
    }

    private sealed class NoCapture : IScreenCaptureService
    {
        /// <inheritdoc />
        public ScreenshotCaptureResult CaptureByMode(string directory, string mode, string origin,
            Func<ScreenshotCaptureContext, ScreenshotCaptureDecision> authorizeCapture) => throw new NotSupportedException("No real capture in tests.");
    }

    private class NoAnalysis : IAiAnalysisService
    {
        /// <inheritdoc />
        public Task<AiAnalysis> AnalyzeCurrentScreenAsync(AnalysisContextSnapshot? activity, bool allowCapture = true,
            string origin = "manual", CancellationToken cancellationToken = default) => throw new NotSupportedException();
        /// <inheritdoc />
        public virtual Task<AiAnalysis> AnalyzeCapturedScreenAsync(AnalysisContextSnapshot? activity, ScreenshotCaptureResult captureResult,
            bool keepCapture, string origin, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        /// <inheritdoc />
        public Task<AiAnalysis> AnalyzeHistoricalCapturedScreenAsync(AnalysisContextSnapshot activity, ScreenshotCaptureResult captureResult,
            string origin, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class BlockedAnalysis : NoAnalysis
    {
        internal TaskCompletionSource<bool> Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource<bool> Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal CancellationToken Token { get; private set; }
        /// <inheritdoc />
        public override async Task<AiAnalysis> AnalyzeCapturedScreenAsync(AnalysisContextSnapshot? activity, ScreenshotCaptureResult captureResult,
            bool keepCapture, string origin, CancellationToken cancellationToken = default)
        {
            Token = cancellationToken;
            Started.TrySetResult(true);
            await Release.Task;
            cancellationToken.ThrowIfCancellationRequested();
            throw new InvalidOperationException("The test must cancel the request.");
        }
    }

    private sealed class PricingHandler : HttpMessageHandler
    {
        internal int Requests { get; private set; }
        /// <inheritdoc />
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("""
                ### Standard pricing data
                | Model | Input | Cached input | Cache write | Output | Long context input | Long context cached input | Long context cache write | Long context output |
                | --- | --- | --- | --- | --- | --- | --- | --- | --- |
                | gpt-test | $1.00 | $0.10 | - | $2.00 | - | - | - | - |
                ### End
                """) });
        }
    }
}
