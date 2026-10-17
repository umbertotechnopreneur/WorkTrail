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
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using WorkTrail.Application;
using WorkTrail.Search;
using WorkTrail.Services;
using Xunit;

namespace WorkTrail.Core.Tests;

[Collection(ProcessEnvironmentCollection.Name)]
public sealed class LocalSearchIntegrationTests
{
    /// <summary>New source data is indexed by the worker without making reads wait for its batch.</summary>
    [Fact]
    public async Task BackgroundIndexing_DoesNotBlockQueriesAndStopsWithCoordinator()
    {
        var dataDirectory = CreateDataDirectory();
        try
        {
            var store = CreateStore(dataDirectory);
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var stopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var service = new ThreadRecordingSearchService
            {
                BatchAction = async cancellationToken =>
                {
                    entered.TrySetResult();
                    try { await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken); }
                    finally { stopped.TrySetResult(); }
                }
            };
            await using (var coordinator = new LocalSearchCoordinator(store, service))
            {
                await coordinator.SynchronizeAsync(CancellationToken.None);
                store.AppendSample(new ActivitySample(DateTimeOffset.Now, 1, "active", "test", "Test",
                    "newmarker", "Search", store.LoadSettings().InstallationId, 0, 0));
                coordinator.Start();
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));

                await coordinator.SearchAsync(new SearchRequest { Text = "existing" }, CancellationToken.None)
                    .WaitAsync(TimeSpan.FromSeconds(5));
                Assert.Equal(1, service.SearchCount);
                Assert.False(stopped.Task.IsCompleted);
            }

            Assert.True(stopped.Task.IsCompletedSuccessfully);
            Assert.True(service.Disposed);
        }
        finally { DeleteDataDirectory(dataDirectory); }
    }

    /// <summary>Background projection faults fail queries explicitly until a successful manual rebuild.</summary>
    [Fact]
    public async Task BackgroundIndexing_FailureIsVisibleAndExplicitRebuildRecovers()
    {
        var dataDirectory = CreateDataDirectory();
        try
        {
            var service = new ThreadRecordingSearchService
            {
                RebuildAction = () => throw new InvalidDataException("Synthetic source failure")
            };
            await using var coordinator = new LocalSearchCoordinator(CreateStore(dataDirectory), service);
            coordinator.Start();

            await Assert.ThrowsAsync<InvalidOperationException>(() => coordinator.SearchAsync(
                new SearchRequest { Text = "example" }, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10)));
            service.RebuildAction = null;
            await coordinator.RebuildAsync(CancellationToken.None);
            await coordinator.SearchAsync(new SearchRequest { Text = "example" }, CancellationToken.None);
            Assert.Equal(1, service.SearchCount);
        }
        finally { DeleteDataDirectory(dataDirectory); }
    }

    /// <summary>A backlog exceeding the batch size is drained incrementally instead of rebuilding all sources.</summary>
    [Fact]
    public async Task Synchronize_LargeBacklogUsesBoundedIncrementalBatches()
    {
        var dataDirectory = CreateDataDirectory();
        try
        {
            var store = CreateStore(dataDirectory);
            var service = new ThreadRecordingSearchService();
            await using var coordinator = new LocalSearchCoordinator(store, service);
            await coordinator.SynchronizeAsync(CancellationToken.None);
            var installationId = store.LoadSettings().InstallationId;
            var now = DateTimeOffset.Now;
            for (var i = 0; i < 2_001; i++)
                store.AppendSample(new ActivitySample(now.AddTicks(i), 1, "active", "test", "Test",
                    "backlog marker", "Search", installationId, 0, 0));

            await coordinator.SynchronizeAsync(CancellationToken.None);

            Assert.Equal(1, service.RebuildCount);
            Assert.Equal(2, service.BatchCount);
            Assert.Single(service.LastBatch);
            Assert.Equal(store.GetSearchSourceRevision(), service.CommittedSourceRevision);
        }
        finally { DeleteDataDirectory(dataDirectory); }
    }

    /// <summary>Refreshing requested captures does not read unrelated analysis metadata from the same day.</summary>
    [Fact]
    public void ScreenshotBatch_ReadsOnlyRequestedCaptureMetadata()
    {
        var dataDirectory = CreateDataDirectory();
        try
        {
            var store = CreateStore(dataDirectory);
            var settings = store.LoadSettings();
            var now = DateTimeOffset.Now;
            var requested = CreateOwnedScreenshot(settings.ScreenshotDirectory!, 'a', now);
            var unrelated = CreateOwnedScreenshot(settings.ScreenshotDirectory!, 'b', now);
            foreach (var (path, id) in new[] { (requested, new string('a', 32)), (unrelated, new string('b', 32)) })
            {
                store.RegisterScreenshotCapture(id, settings.InstallationId, now, ScreenshotCaptureOrigins.Manual);
                AppendAnalysis(store, id, now, path, "Saved project description");
            }
            using (var connection = new SqliteConnection($"Data Source={Path.Combine(dataDirectory, "activity.sqlite3")};Pooling=False"))
            {
                connection.Open();
                using var command = connection.CreateCommand();
                command.CommandText = "UPDATE ai_analysis_results SET timestamp_utc_ticks = 9223372036854775807 WHERE correlation_id = $id;";
                command.Parameters.AddWithValue("$id", new string('b', 32));
                Assert.Equal(1, command.ExecuteNonQuery());
            }

            var items = store.GetScreenshotGalleryItemsForCaptures([new string('a', 32)], CancellationToken.None);

            Assert.Equal(requested, Assert.Single(items).Path);
            Assert.Equal("Saved project description", items[0].AiDescriptionMarkdown);
        }
        finally { DeleteDataDirectory(dataDirectory); }
    }

    [Fact]
    public async Task SearchCoordinator_QueuesSearchBeforeSynchronousLuceneWork()
    {
        var dataDirectory = CreateDataDirectory();
        try
        {
            var service = new ThreadRecordingSearchService();
            await using var coordinator = new LocalSearchCoordinator(CreateStore(dataDirectory), service);

            var searchCallerThread = RunOnDedicatedThread(() => SearchCurrentAsync(coordinator,
                new SearchRequest { Text = "snapshot" },
                CancellationToken.None));

            Assert.NotEqual(searchCallerThread, service.SearchThreadId);
        }
        finally
        {
            DeleteDataDirectory(dataDirectory);
        }
    }

    [Fact]
    public async Task SearchCoordinator_CoalescesWritesThatContinueDuringARebuild()
    {
        var dataDirectory = CreateDataDirectory();
        try
        {
            var store = CreateStore(dataDirectory);
            var service = new ThreadRecordingSearchService
            {
                RebuildAction = () => store.AppendSample(new ActivitySample(
                    DateTimeOffset.UtcNow,
                    1,
                    "active",
                    "test",
                    "Test",
                    "Tracking continued during index rebuild",
                    "Search",
                    store.LoadSettings().InstallationId,
                    0,
                    0))
            };
            await using var coordinator = new LocalSearchCoordinator(store, service);

            _ = await SearchCurrentAsync(coordinator, new SearchRequest { Text = "tracking" }, CancellationToken.None);
            store.AppendSample(new ActivitySample(
                DateTimeOffset.UtcNow.AddSeconds(1),
                1,
                "active",
                "test",
                "Test",
                "A second write in the refresh window",
                "Search",
                store.LoadSettings().InstallationId,
                0,
                0));
            _ = await SearchCurrentAsync(coordinator, new SearchRequest { Text = "tracking" }, CancellationToken.None);
            _ = await SearchCurrentAsync(coordinator, new SearchRequest { Text = "tracking" }, CancellationToken.None);

            Assert.Equal(1, service.RebuildCount);
            Assert.Equal(1, service.BatchCount);
            Assert.Equal(2, service.LastBatch.Count);
            Assert.All(service.LastBatch, mutation => Assert.StartsWith("activity:", mutation.Id, StringComparison.Ordinal));
        }
        finally
        {
            DeleteDataDirectory(dataDirectory);
        }
    }

    [Fact]
    public void ScreenshotAvailabilityCounts_DeduplicatesArtifactsWithoutLoadingGalleryMetadata()
    {
        var dataDirectory = CreateDataDirectory();
        try
        {
            var store = CreateStore(dataDirectory);
            var screenshotDirectory = Path.Combine(dataDirectory, "screenshots");
            Directory.CreateDirectory(screenshotDirectory);
            store.SaveSettings(store.LoadSettings() with { ScreenshotDirectory = screenshotDirectory });
            var today = DateOnly.FromDateTime(DateTime.Now);
            var todayCapture = new DateTimeOffset(DateTime.Today.AddHours(12));
            var firstCaptureId = new string('e', 32);
            var storedPath = CreateOwnedScreenshot(screenshotDirectory, 'e', todayCapture);
            AppendAnalysis(store, firstCaptureId, todayCapture, storedPath, "Retained screenshot description");
            var rawPath = Path.Combine(
                Path.GetDirectoryName(storedPath)!,
                $"{firstCaptureId}_1.0.0_manual_monitor-1-raw.webp");
            File.WriteAllBytes(rawPath, [4, 5, 6]);
            File.SetLastWriteTimeUtc(rawPath, todayCapture.AddDays(-1).UtcDateTime);
            _ = CreateOwnedScreenshot(screenshotDirectory, 'f', todayCapture.AddDays(-1));
            File.WriteAllText(Path.Combine(screenshotDirectory, "unrelated.png"), "not a WorkTrail capture");

            using (var connection = new SqliteConnection(
                       $"Data Source={Path.Combine(dataDirectory, "activity.sqlite3")};Pooling=False"))
            {
                connection.Open();
                using var command = connection.CreateCommand();
                command.CommandText = "UPDATE ai_analysis_results SET timestamp_utc_ticks = 9223372036854775807 WHERE correlation_id = $id;";
                command.Parameters.AddWithValue("$id", firstCaptureId);
                command.ExecuteNonQuery();
            }

            var counts = store.GetScreenshotAvailabilityCounts(today, CancellationToken.None);

            Assert.Equal(2, counts.TotalSnapshotCount);
            Assert.Equal(1, counts.TodaySnapshotCount);
        }
        finally
        {
            DeleteDataDirectory(dataDirectory);
        }
    }

    /// <summary>Reopens an otherwise usable beta database without rejecting its old informational version.</summary>
    [Fact]
    public void ActivitySchema_AllowsSupersededDatabaseVersion()
    {
        var dataDirectory = CreateDataDirectory();
        try
        {
            _ = new LocalStore(dataDirectory);
            var databasePath = Path.Combine(dataDirectory, "activity.sqlite3");
            using (var connection = new SqliteConnection($"Data Source={databasePath};Pooling=False"))
            {
                connection.Open();
                using var command = connection.CreateCommand();
                command.CommandText = "PRAGMA user_version = 5;";
                command.ExecuteNonQuery();
            }

            _ = new LocalStore(dataDirectory);
            using var check = new SqliteConnection($"Data Source={databasePath};Pooling=False");
            check.Open();
            using var checkCommand = check.CreateCommand();
            checkCommand.CommandText = "PRAGMA user_version;";
            Assert.Equal(5L, Convert.ToInt64(checkCommand.ExecuteScalar()));
        }
        finally
        {
            DeleteDataDirectory(dataDirectory);
        }
    }

    [Fact]
    public async Task SearchCoordinator_IndexesRawFieldsWhenAiDescriptionIsMissing()
    {
        var dataDirectory = CreateDataDirectory();
        try
        {
            var store = CreateStore(dataDirectory);
            var screenshotDirectory = Path.Combine(dataDirectory, "screenshots");
            Directory.CreateDirectory(screenshotDirectory);
            store.SaveSettings(store.LoadSettings() with
            {
                ScreenshotDirectory = screenshotDirectory,
                UiLanguage = "it-IT"
            });
            var timestamp = DateTimeOffset.Now.AddSeconds(-1);
            store.AppendSample(new ActivitySample(
                timestamp,
                5,
                "active",
                "code",
                "Visual Studio Code",
                "Quarterly roadmap planning",
                "WorkTrail - Pianificazione Q3",
                store.LoadSettings().InstallationId,
                3,
                1,
                new Dictionary<string, string>
                {
                    ["Project"] = "WorkTrail",
                    [ActivityAttributeKeys.SpanLabel] = "Roadmap"
                }));
            var screenshotPath = CreateOwnedScreenshot(screenshotDirectory, 'b', timestamp);
            AppendAnalysis(store, new string('b', 32), timestamp, screenshotPath, "Fattura marzo 2026: controllare la scadenza.");
            store.UpsertScreenshotIntervalTelemetry(
                new string('b', 32),
                [screenshotPath],
                new ScreenshotIntervalTelemetry(timestamp.AddMinutes(-15), timestamp));

            await using var coordinator = new LocalSearchCoordinator(
                store,
                new LocalSearchService(new SearchOptions
                {
                    IndexRootPath = Path.Combine(dataDirectory, "test-search")
                }));
            var indexed = await coordinator.RebuildAsync(CancellationToken.None);
            var activity = await SearchCurrentAsync(coordinator, new SearchRequest
            {
                Text = "roadmop",
                QueryLanguage = "en"
            }, CancellationToken.None);
            var screenshot = await SearchCurrentAsync(coordinator, new SearchRequest
            {
                Text = "fattura",
                QueryLanguage = "it"
            }, CancellationToken.None);

            Assert.True(indexed >= 2);
            Assert.Contains(activity.Hits, hit => hit.Document.Kind == "activity" && hit.Document.AiDescription is null);
            Assert.Contains(screenshot.Hits, hit =>
                hit.Document.Kind == "screenshot"
                && hit.Document.AiDescription == "Fattura marzo 2026: controllare la scadenza."
                && hit.Document.AttributesRaw.TryGetValue(SearchAttributeKeys.MouseClicks, out var clicks)
                && clicks == "1");
        }
        finally
        {
            DeleteDataDirectory(dataDirectory);
        }
    }

    [Fact]
    public async Task SearchCoordinator_IndexesSavedAiDescriptionAfterScreenshotArtifactIsRemoved()
    {
        var dataDirectory = CreateDataDirectory();
        try
        {
            var store = CreateStore(dataDirectory);
            var screenshotDirectory = Path.Combine(dataDirectory, "screenshots");
            Directory.CreateDirectory(screenshotDirectory);
            store.SaveSettings(store.LoadSettings() with
            {
                ScreenshotDirectory = screenshotDirectory,
                SearchLanguage = "it-IT"
            });
            var capturedAt = DateTimeOffset.Now.AddSeconds(-1);
            var screenshotPath = CreateOwnedScreenshot(screenshotDirectory, 'd', capturedAt);
            AppendAnalysis(store, new string('d', 32), capturedAt, screenshotPath, "Preventivo cliente senza allegato");
            File.Delete(screenshotPath);

            await using var coordinator = new LocalSearchCoordinator(
                store,
                new LocalSearchService(new SearchOptions
                {
                    IndexRootPath = Path.Combine(dataDirectory, "test-search")
                }));
            var response = await SearchCurrentAsync(coordinator, new SearchRequest
            {
                Text = "preventivo",
                QueryLanguage = "it"
            }, CancellationToken.None);

            var hit = Assert.Single(response.Hits);
            Assert.Equal("analysis", hit.Document.Kind);
            Assert.Equal(screenshotPath, store.LoadAiAnalysis(new string('d', 32))!.ScreenshotPaths);
            Assert.Equal("Preventivo cliente senza allegato", hit.Document.AiDescription);
            Assert.False(File.Exists(screenshotPath));
        }
        finally
        {
            DeleteDataDirectory(dataDirectory);
        }
    }

    [Fact]
    public async Task SearchCoordinator_AppliesPersistedSynonymAndTypoPreferencesToNextQuery()
    {
        var dataDirectory = CreateDataDirectory();
        try
        {
            var store = CreateStore(dataDirectory);
            store.SaveSettings(store.LoadSettings() with
            {
                SearchLanguage = "it-IT",
                SearchSynonymsEnabled = false,
                SearchTypoToleranceEnabled = false
            });
            store.AppendSample(new ActivitySample(
                DateTimeOffset.Now,
                5,
                "active",
                "browser",
                "Browser",
                "Fatturazione computer aziendale",
                "Preventivo",
                store.LoadSettings().InstallationId,
                0,
                0));

            var settings = new SettingsSnapshot(store.LoadSettings());
            await using var coordinator = new LocalSearchCoordinator(
                store,
                new LocalSearchService(new SearchOptions
                {
                    IndexRootPath = Path.Combine(dataDirectory, "test-search"),
                    SynonymSets =
                    [
                        new SearchSynonymSet
                        {
                            Language = "it",
                            Terms = ["computer", "pc"]
                        }
                    ]
                }), settings);

            Assert.Empty((await SearchCurrentAsync(coordinator,
                new SearchRequest { Text = "pc" },
                CancellationToken.None)).Hits);
            Assert.Empty((await SearchCurrentAsync(coordinator,
                new SearchRequest { Text = "fatturazone" },
                CancellationToken.None)).Hits);

            store.SaveSettings(store.LoadSettings() with
            {
                SearchSynonymsEnabled = true,
                SearchTypoToleranceEnabled = true
            });
            settings.Replace(store.LoadSettings());

            Assert.NotEmpty((await SearchCurrentAsync(coordinator,
                new SearchRequest { Text = "pc" },
                CancellationToken.None)).Hits);
            Assert.NotEmpty((await SearchCurrentAsync(coordinator,
                new SearchRequest { Text = "fatturazone" },
                CancellationToken.None)).Hits);
        }
        finally
        {
            DeleteDataDirectory(dataDirectory);
        }
    }

    /// <summary>Every shipped search locale includes the complete curated concept catalog.</summary>
    [Fact]
    public void SearchSynonymConfiguration_CoversEverySearchLocale()
    {
        var sets = SearchSynonymConfiguration.Load(Path.Combine(AppContext.BaseDirectory, "search-synonyms.json"));

        Assert.All(
            ProductLanguageCatalog.SearchChoices.Where(locale => locale != ProductLanguageCatalog.SystemLanguage),
            locale => Assert.True(
                sets.Count(set => string.Equals(set.Language, locale, StringComparison.OrdinalIgnoreCase)) >= 300,
                $"Expected at least 300 curated synonym groups for {locale}."));
    }

    [Fact]
    public void SearchSettings_ControlQueryBehaviorWithoutDisablingSearch()
    {
        var result = SettingsCatalog.Apply(
            new AppSettings(),
            new SettingsPatch(new Dictionary<string, string?>
            {
                ["search.language"] = "it-IT",
                ["search.synonyms"] = "false",
                ["search.typo_tolerance"] = "false"
            }));

        Assert.True(result.Succeeded);
        Assert.Equal("it-IT", result.Value!.SearchLanguage);
        Assert.False(result.Value.SearchSynonymsEnabled);
        Assert.False(result.Value.SearchTypoToleranceEnabled);
        Assert.DoesNotContain(
            SettingsCatalog.Definitions,
            definition => string.Equals(definition.Key, "search.enabled", StringComparison.Ordinal));
        Assert.All(
            SettingsCatalog.Definitions.Where(definition => definition.Key.StartsWith("search.", StringComparison.Ordinal)),
            definition => Assert.False(definition.RequiresRestart));
    }

    private static async Task<SearchResponse> SearchCurrentAsync(
        LocalSearchCoordinator coordinator, SearchRequest request, CancellationToken cancellationToken)
    {
        await coordinator.SynchronizeAsync(cancellationToken);
        return await coordinator.SearchAsync(request, cancellationToken);
    }

    private static string CreateOwnedScreenshot(string directory, char captureCharacter, DateTimeOffset capturedAt)
    {
        var dayDirectory = ScreenshotStorageLayout.GetDayDirectory(directory, capturedAt);
        Directory.CreateDirectory(dayDirectory);
        var path = Path.Combine(
            dayDirectory,
            $"{new string(captureCharacter, 32)}_1.0.0_manual_monitor-1.webp");
        File.WriteAllBytes(path, [1, 2, 3]);
        File.SetLastWriteTimeUtc(path, capturedAt.UtcDateTime);
        return path;
    }

    private static void AppendAnalysis(
        LocalStore store, string captureId, DateTimeOffset capturedAt, string screenshotPath, string summary)
    {
        var usage = new AiRequestUsageRecord(
            Guid.NewGuid().ToString("N"), captureId, capturedAt, capturedAt.AddMilliseconds(10),
            "snapshot.manual", "screen_analysis", "test-provider", "provider.invalid", "test-model", "test-model",
            null, null, 200, 10, null, 1, 10, 100, new AiUsageMetrics(10, 5, 15), "stop", true, null);
        store.AppendAiAnalysisAndUsage(usage, new AiAnalysis(
            capturedAt, "Test", "Search", summary, store.LoadSettings().InstallationId,
            screenshotPath, CorrelationId: captureId, Origin: "snapshot.manual"));
    }

    private static LocalStore CreateStore(string dataDirectory)
    {
        var store = new LocalStore(dataDirectory);
        store.SaveSettings(store.LoadSettings() with
        {
            ScreenshotDirectory = Path.Combine(dataDirectory, "screenshots")
        });
        return store;
    }

    private static string CreateDataDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), $"WorkTrail-search-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    private static int RunOnDedicatedThread(Func<Task> operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        var callerThreadId = 0;
        Exception? failure = null;
        using var completed = new ManualResetEventSlim();
        var thread = new Thread(() =>
        {
            callerThreadId = Environment.CurrentManagedThreadId;
            try
            {
                operation().GetAwaiter().GetResult();
            }
            catch (Exception exception)
            {
                failure = exception;
            }
            finally
            {
                completed.Set();
            }
        })
        {
            IsBackground = true
        };
        thread.Start();
        Assert.True(completed.Wait(TimeSpan.FromSeconds(10)), "The queued search operation did not complete.");
        thread.Join();
        if (failure is not null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }

        return callerThreadId;
    }

    private sealed class ThreadRecordingSearchService : ILocalSearchService
    {
        public long CommittedSourceRevision { get; private set; }

        internal int SearchThreadId { get; private set; }
        internal int SearchCount { get; private set; }
        internal bool Disposed { get; private set; }

        internal int RebuildCount { get; private set; }

        internal int BatchCount { get; private set; }

        internal IReadOnlyList<SearchIndexMutation> LastBatch { get; private set; } = [];

        internal Action? RebuildAction { get; set; }
        internal Func<CancellationToken, Task>? BatchAction { get; init; }

        public Task UpsertAsync(SearchDocument document, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task DeleteAsync(string id, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public async Task ApplyBatchAsync(
            IReadOnlyCollection<SearchIndexMutation> mutations,
            long sourceRevision,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (BatchAction is not null) await BatchAction(cancellationToken);
            BatchCount++;
            LastBatch = mutations.ToArray();
            await SetCommittedRevisionAsync(sourceRevision);
        }

        public Task RebuildAsync(
            IEnumerable<SearchDocument> documents,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _ = documents.ToArray();
            RebuildCount++;
            RebuildAction?.Invoke();
            return Task.CompletedTask;
        }

        public Task RebuildAsync(
            IEnumerable<SearchDocument> documents,
            long sourceRevision,
            CancellationToken cancellationToken = default)
        {
            CommittedSourceRevision = sourceRevision;
            return RebuildAsync(documents, cancellationToken);
        }

        public Task<SearchResponse> SearchAsync(
            SearchRequest request,
            CancellationToken cancellationToken = default)
        {
            SearchThreadId = Environment.CurrentManagedThreadId;
            SearchCount++;
            return Task.FromResult(new SearchResponse
            {
                TotalCount = 0,
                Offset = request.Offset
            });
        }

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }

        private Task SetCommittedRevisionAsync(long sourceRevision)
        {
            CommittedSourceRevision = sourceRevision;
            return Task.CompletedTask;
        }
    }

    private static void DeleteDataDirectory(string path)
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }
}
