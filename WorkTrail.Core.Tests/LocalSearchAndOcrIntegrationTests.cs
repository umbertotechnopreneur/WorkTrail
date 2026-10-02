// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
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
public sealed class LocalSearchAndOcrIntegrationTests
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

    /// <summary>Refreshing requested captures does not deserialize unrelated OCR from the same day.</summary>
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
                store.UpsertScreenshotTextSnapshot(id, CreateTextSnapshot(path, "synthetic OCR"));
            }
            using (var connection = new SqliteConnection($"Data Source={Path.Combine(dataDirectory, "activity.sqlite3")};Pooling=False"))
            {
                connection.Open();
                using var command = connection.CreateCommand();
                command.CommandText = "UPDATE screenshot_text_snapshots SET snapshot_json = 'invalid-json' WHERE artifact_identity = $identity;";
                command.Parameters.AddWithValue("$identity", Path.GetFileNameWithoutExtension(unrelated));
                Assert.Equal(1, command.ExecuteNonQuery());
            }

            var items = store.GetScreenshotGalleryItemsForCaptures([new string('a', 32)], CancellationToken.None);

            Assert.Equal(requested, Assert.Single(items).Path);
            Assert.Equal("synthetic OCR", items[0].TextSnapshot!.Ocr.RawText);
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
            var firstArtifactIdentity = Path.GetFileNameWithoutExtension(storedPath);
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
                command.CommandText = """
                    INSERT INTO screenshot_text_snapshots (
                        artifact_identity,
                        capture_id,
                        source_path,
                        extracted_utc_ticks,
                        snapshot_json,
                        updated_utc_ticks)
                    VALUES ($identity, $captureId, $sourcePath, $ticks, $json, $ticks);
                    """;
                command.Parameters.AddWithValue("$identity", firstArtifactIdentity);
                command.Parameters.AddWithValue("$captureId", firstCaptureId);
                command.Parameters.AddWithValue("$sourcePath", storedPath);
                command.Parameters.AddWithValue("$ticks", todayCapture.UtcTicks);
                command.Parameters.AddWithValue("$json", "not-json");
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

    [Fact]
    public void ScreenshotTextSnapshot_RoundTripsThroughSqliteAndGallery()
    {
        var dataDirectory = CreateDataDirectory();
        try
        {
            var store = CreateStore(dataDirectory);
            var screenshotDirectory = Path.Combine(dataDirectory, "screenshots");
            Directory.CreateDirectory(screenshotDirectory);
            store.SaveSettings(store.LoadSettings() with { ScreenshotDirectory = screenshotDirectory });
            var capturedAt = DateTimeOffset.Now.AddSeconds(-1);
            var screenshotPath = CreateOwnedScreenshot(screenshotDirectory, 'a', capturedAt);
            var snapshot = CreateTextSnapshot(screenshotPath, "Riunone proggeto WorkTrail") with
            {
                AiRefinement = new OcrAiRefinement(
                    "Riunione progetto WorkTrail",
                    "it-IT",
                    new OcrStructuredSummary(
                        "Riunione di progetto.",
                        Array.Empty<string>(),
                        Array.Empty<string>(),
                        Array.Empty<string>()),
                    capturedAt.AddSeconds(1))
            };

            store.UpsertScreenshotTextSnapshot(new string('a', 32), snapshot);
            store.UpsertScreenshotIntervalTelemetry(
                new string('a', 32),
                [screenshotPath],
                new ScreenshotIntervalTelemetry(capturedAt.AddMinutes(-15), capturedAt, 50, 50));

            var loaded = store.LoadScreenshotTextSnapshot(screenshotPath);
            var gallery = store.GetScreenshotGallery(DateOnly.FromDateTime(capturedAt.LocalDateTime.Date));
            var item = Assert.Single(gallery.Items);
            Assert.Equal(snapshot.SourceScreenshotPath, loaded?.SourceScreenshotPath);
            Assert.Equal(snapshot.Ocr.RawText, loaded?.Ocr.RawText);
            Assert.Equal(snapshot.Ocr.Lines[0].Words[0], loaded?.Ocr.Lines[0].Words[0]);
            Assert.Equal("Riunone proggeto WorkTrail", item.TextSnapshot?.Ocr.RawText);
            Assert.Equal("Riunione progetto WorkTrail", loaded?.AiRefinement?.CorrectedText);
            Assert.Equal("Riunione progetto WorkTrail", item.TextSnapshot?.AiRefinement?.CorrectedText);
            Assert.Equal(42, item.TextSnapshot?.Ocr.Lines[0].Words[0].X);
            Assert.Equal(50, item.CpuUsagePercent);
            Assert.Equal(50, item.GpuUsagePercent);
            Assert.Equal(3, item.ActivityIndex);
        }
        finally
        {
            DeleteDataDirectory(dataDirectory);
        }
    }

    [Fact]
    public void ActivitySchema_RejectsSupersededDatabaseVersion()
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

            var exception = Assert.Throws<InvalidOperationException>(() => new LocalStore(dataDirectory));
            Assert.Contains("Unsupported activity database schema version 5; expected 12", exception.Message, StringComparison.Ordinal);
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
            var textSnapshot = CreateTextSnapshot(screenshotPath, "Fattura marzo 2026") with
            {
                AiRefinement = new OcrAiRefinement(
                    "Fattura marzo 2026",
                    "it",
                    new OcrStructuredSummary(
                        "Riepilogo della fattura di marzo.",
                        ["Importo da verificare"],
                        ["Fornitore"],
                        ["Controllare la scadenza"]),
                    DateTimeOffset.UtcNow)
            };
            store.UpsertScreenshotTextSnapshot(new string('b', 32), textSnapshot);
            store.UpsertScreenshotIntervalTelemetry(
                new string('b', 32),
                [screenshotPath],
                new ScreenshotIntervalTelemetry(timestamp.AddMinutes(-15), timestamp, 37, 61));

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
                && hit.Document.OcrRawText == "Fattura marzo 2026"
                && hit.Document.OcrStructuredSummary?.Contains("scadenza", StringComparison.OrdinalIgnoreCase) == true
                && hit.Document.AttributesRaw.TryGetValue(SearchAttributeKeys.MouseClicks, out var clicks)
                && clicks == "1"
                && hit.Document.AttributesRaw.TryGetValue(SearchAttributeKeys.CpuUsagePercent, out var cpu)
                && cpu == "37"
                && hit.Document.AttributesRaw.TryGetValue(SearchAttributeKeys.GpuUsagePercent, out var gpu)
                && gpu == "61");
        }
        finally
        {
            DeleteDataDirectory(dataDirectory);
        }
    }

    [Fact]
    public async Task SearchCoordinator_IndexesDurableOcrAfterScreenshotArtifactIsRemoved()
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
            var screenshotPath = CreateOwnedScreenshot(screenshotDirectory, 'd', DateTimeOffset.Now.AddSeconds(-1));
            var textSnapshot = CreateTextSnapshot(screenshotPath, "Preventivo cliente senza allegato");
            store.UpsertScreenshotTextSnapshot(new string('d', 32), textSnapshot);
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
            Assert.Equal("screenshot-text", hit.Document.Kind);
            Assert.Equal(screenshotPath, hit.Document.CapturePath);
            Assert.Equal("Preventivo cliente senza allegato", hit.Document.OcrRawText);
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
    public async Task AiOcrRefinement_UsesDedicatedPromptAndPersistsStructuredResult()
    {
        var dataDirectory = CreateDataDirectory();
        const string apiKeyVariable = "WORKTRAIL_OCR_INTEGRATION_TEST_KEY";
        var previousApiKey = Environment.GetEnvironmentVariable(apiKeyVariable, EnvironmentVariableTarget.Process);
        try
        {
            Environment.SetEnvironmentVariable(apiKeyVariable, "test-ocr-refinement-key", EnvironmentVariableTarget.Process);
            var store = CreateStore(dataDirectory);
            var screenshotDirectory = Path.Combine(dataDirectory, "screenshots");
            Directory.CreateDirectory(screenshotDirectory);
            var screenshotPath = CreateOwnedScreenshot(screenshotDirectory, 'c', DateTimeOffset.Now);
            var rawSnapshot = CreateTextSnapshot(screenshotPath, "Riunlone proggetto");
            store.UpsertScreenshotTextSnapshot(new string('c', 32), rawSnapshot);
            var capture = new ScreenshotCaptureResult(
                new string('c', 32),
                [screenshotPath],
                [screenshotPath],
                ScreenshotCaptureOrigins.Manual,
                [rawSnapshot]);
            var decoder = new RecordingOcrDecoder();
            var service = new OpenAiOcrRefinementService(store, decoder);
            var settings = store.LoadSettings() with
            {
                OpenAiEnabled = true,
                AiApiKeyName = apiKeyVariable,
                AiProvider = "openai",
                Model = "gpt-test"
            };

            var refined = await service.RefineAsync(capture, settings, CancellationToken.None);

            var result = Assert.Single(refined.TextSnapshots!);
            Assert.Contains("Riunlone proggetto", decoder.Prompt, StringComparison.Ordinal);
            Assert.Equal([screenshotPath], decoder.ScreenshotPaths);
            Assert.True(decoder.RequestOptions?.OmitOutputTokenLimitWhenSupported);
            Assert.Equal("none", decoder.RequestOptions?.ReasoningEffort);
            Assert.Equal("Riunione progetto", result.AiRefinement?.CorrectedText);
            Assert.Equal("Preparazione della riunione.", result.AiRefinement?.Summary.Overview);
            Assert.Equal("Riunione progetto", store.LoadScreenshotTextSnapshot(screenshotPath)?.AiRefinement?.CorrectedText);
        }
        finally
        {
            Environment.SetEnvironmentVariable(apiKeyVariable, previousApiKey, EnvironmentVariableTarget.Process);
            DeleteDataDirectory(dataDirectory);
        }
    }

    [Fact]
    public void OcrSettings_AreExplicitlyPatchableAndRestartBound()
    {
        var result = SettingsCatalog.Apply(
            new AppSettings(),
            new SettingsPatch(new Dictionary<string, string?>
            {
                ["ocr.enabled"] = "false",
                ["ocr.language"] = "it-IT"
            }));

        Assert.True(result.Succeeded);
        Assert.False(result.Value!.OcrEnabled);
        Assert.Equal("it-IT", result.Value.OcrLanguage);
        Assert.All(
            SettingsCatalog.Definitions.Where(definition => definition.Key.StartsWith("ocr.", StringComparison.Ordinal)),
            definition => Assert.True(definition.RequiresRestart));
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

    private static ScreenshotTextSnapshot CreateTextSnapshot(string screenshotPath, string rawText) => new(
        screenshotPath,
        new OcrRawSnapshot(
            ScreenshotTextExtractionStatus.Succeeded,
            rawText,
            "it",
            null,
            DateTimeOffset.UtcNow,
            "test-ocr",
            1920,
            1080,
            [new OcrLineSnapshot(rawText, [new OcrWordSnapshot(rawText, 42, 24, 120, 20)])]));

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

    /// <summary>Ensures deleting OCR refreshes, rather than removes, the retained screenshot search document.</summary>
    [Fact]
    public async Task SearchCoordinator_KeepsRetainedImageDocumentWhenOcrIsDeleted()
    {
        var dataDirectory = CreateDataDirectory();
        try
        {
            var store = CreateStore(dataDirectory);
            var screenshotDirectory = Path.Combine(dataDirectory, "screenshots");
            Directory.CreateDirectory(screenshotDirectory);
            var timestamp = DateTimeOffset.Now.AddSeconds(-1);
            var captureId = new string('c', 32);
            var screenshotPath = CreateOwnedScreenshot(screenshotDirectory, 'c', timestamp);
            var artifactIdentity = Path.GetFileNameWithoutExtension(screenshotPath);
            store.RegisterScreenshotCapture(
                captureId,
                store.LoadSettings().InstallationId,
                timestamp,
                ScreenshotCaptureOrigins.Manual);
            store.UpsertScreenshotTextSnapshot(captureId, CreateTextSnapshot(screenshotPath, "temporary OCR text"));
            var service = new ThreadRecordingSearchService();
            await using var coordinator = new LocalSearchCoordinator(store, service);
            _ = await SearchCurrentAsync(coordinator, new SearchRequest { Text = "temporary" }, CancellationToken.None);

            Assert.Equal(1, store.DeleteScreenshotTextSnapshot(screenshotPath));
            _ = await SearchCurrentAsync(coordinator, new SearchRequest { Text = "desktop" }, CancellationToken.None);

            var imageMutation = Assert.Single(
                service.LastBatch,
                mutation => mutation.Id == $"screenshot:{artifactIdentity}");
            Assert.NotNull(imageMutation.Document);
            Assert.Equal(screenshotPath, imageMutation.Document.CapturePath);
        }
        finally
        {
            DeleteDataDirectory(dataDirectory);
        }
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
        var path = Path.Combine(Path.GetTempPath(), $"WorkTrail-search-ocr-tests-{Guid.NewGuid():N}");
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

    private sealed class RecordingOcrDecoder : IAIDecoder
    {
        public string Provider => "test";

        internal string Prompt { get; private set; } = string.Empty;

        internal IReadOnlyList<string> ScreenshotPaths { get; private set; } = [];

        public Task<AiProviderResult> DecodeAsync(
            string prompt,
            IReadOnlyList<string> screenshotPaths,
            AppSettings settings,
            string apiKey,
            string correlationId,
            AiProviderRequestOptions? requestOptions = null,
            CancellationToken cancellationToken = default)
        {
            Prompt = prompt;
            ScreenshotPaths = screenshotPaths;
            RequestOptions = requestOptions;
            const string response = """
                {
                  "items": [
                    {
                      "sourceIndex": 0,
                      "languageTag": "it",
                      "correctedText": "Riunione progetto",
                      "summary": {
                        "overview": "Preparazione della riunione.",
                        "keyPoints": ["Agenda"],
                        "entities": ["WorkTrail"],
                        "actions": ["Confermare orario"]
                      }
                    }
                  ]
                }
                """;
            return Task.FromResult(new AiProviderResult(
                response,
                new AiUsageMetrics(10, 20, 30),
                "response-test",
                "request-test",
                settings.Model,
                "stop",
                200,
                5,
                4));
        }

        public AiProviderRequestOptions? RequestOptions { get; private set; }
    }
}
