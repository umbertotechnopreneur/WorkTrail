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
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using WorkTrail.Application;
using WorkTrail.Services;
using Xunit;

namespace WorkTrail.Core.Tests;

public sealed class DataArchiveServiceTests
{
    /// <summary>Real archive work emits ordered phase snapshots and honest per-phase counters.</summary>
    [Fact]
    public void ExportPreviewImport_ReportRealPhasesAndCounts()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var source = new LocalStore(Path.Combine(root, "source"));
            var path = Path.Combine(root, "progress.tmuarchive");
            var reports = new List<DataArchiveProgress>();
            new DataArchiveService(source).Export(new(path, IncludeScreenshots: false), CancellationToken.None, reports.Add);
            Assert.Equal(DataArchivePhase.PreparingDatabase, reports[0].Phase);
            Assert.Contains(reports, item => item.Phase == DataArchivePhase.WritingArchive && item.CompletedItems == 1 && item.TotalItems == 1);
            Assert.Equal(DataArchivePhase.Finalizing, reports[^1].Phase);
            var importer = CreateImporter(root);
            reports.Clear();
            var plan = importer.PreviewImport(new(path), CancellationToken.None, reports.Add);
            Assert.Equal(DataArchivePhase.VerifyingArchive, reports[0].Phase);
            Assert.Contains(reports, item => item.Phase == DataArchivePhase.VerifyingArchive && item.TotalItems > 0 && item.CompletedItems == item.TotalItems);
            reports.Clear();
            importer.Import(plan.PlanId, CancellationToken.None, reports.Add);
            Assert.Contains(reports, item => item.Phase == DataArchivePhase.MergingData);
            Assert.Equal(DataArchivePhase.Finalizing, reports[^1].Phase);
            Assert.All(reports, item => Assert.True(item.TotalItems is null || item.CompletedItems <= item.TotalItems));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    /// <summary>Retries a real SQLite lock and publishes a valid archive after the writer releases it.</summary>
    [Fact]
    public void Export_TransientDatabaseLock_RetriesAndProducesImportableArchive()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var source = new LocalStore(Path.Combine(root, "source"));
            var destination = Path.Combine(root, "history.tmuarchive");
            using var blocker = LockDatabaseForExport(source);
            var logger = new RecordingLogger(() =>
            {
                using var release = blocker.CreateCommand();
                release.CommandText = "ROLLBACK;";
                release.ExecuteNonQuery();
            });

            var exported = new DataArchiveService(source, logger).Export(
                new DataArchiveExportRequest(destination, IncludeScreenshots: false), CancellationToken.None);

            Assert.Equal(LogLevel.Warning, Assert.Single(logger.Entries).Level);
            Assert.Equal(destination, exported.Path);
            var importer = CreateImporter(root);
            var preview = importer.PreviewImport(new DataArchiveImportPreviewRequest(destination), CancellationToken.None);
            Assert.Single(preview.Installations);
            Assert.Equal(1, importer.Import(preview.PlanId, CancellationToken.None).AddedInstallationCount);
            Assert.Empty(Directory.EnumerateFiles(root, "*.tmp"));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>Bounds lock retries and observes cancellation without overwriting an existing archive.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Export_PersistentDatabaseLock_FailsOrCancelsWithoutPublishing(bool cancel)
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var source = new LocalStore(Path.Combine(root, "source"));
            var destination = Path.Combine(root, "history.tmuarchive");
            var original = new byte[] { 1, 2, 3 };
            File.WriteAllBytes(destination, original);
            using var blocker = LockDatabaseForExport(source);
            using var cancellation = new CancellationTokenSource();
            var logger = new RecordingLogger(() =>
            {
                if (cancel) cancellation.CancelAfter(TimeSpan.FromMilliseconds(20));
            });
            var exporter = new DataArchiveService(source, logger);
            var stopwatch = Stopwatch.StartNew();

            if (cancel)
            {
                var exception = Assert.Throws<OperationCanceledException>(() => exporter.Export(
                    new DataArchiveExportRequest(destination, IncludeScreenshots: false), cancellation.Token));
                Assert.Equal(cancellation.Token, exception.CancellationToken);
                Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(3));
            }
            else
            {
                var exception = Assert.Throws<SqliteException>(() => exporter.Export(
                    new DataArchiveExportRequest(destination, IncludeScreenshots: false), cancellation.Token));
                Assert.Equal(5, exception.SqliteErrorCode);
                Assert.InRange(stopwatch.Elapsed, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(15));
            }

            Assert.Equal(LogLevel.Warning, Assert.Single(logger.Entries).Level);
            Assert.Equal(original, File.ReadAllBytes(destination));
            Assert.Empty(Directory.EnumerateFiles(root, "*.tmp"));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>Propagates a non-lock database failure immediately without retrying or publishing an archive.</summary>
    [Fact]
    public void Export_InvalidDatabase_DoesNotRetry()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var source = new LocalStore(Path.Combine(root, "source"));
            File.WriteAllBytes(source.ActivityDatabasePath, new byte[4096]);
            var destination = Path.Combine(root, "history.tmuarchive");
            var logger = new RecordingLogger();

            var exception = Assert.Throws<SqliteException>(() => new DataArchiveService(source, logger).Export(
                new DataArchiveExportRequest(destination, IncludeScreenshots: false), CancellationToken.None));

            Assert.Equal(26, exception.SqliteErrorCode);
            Assert.Empty(logger.Entries);
            Assert.False(File.Exists(destination));
            Assert.Empty(Directory.EnumerateFiles(root, "*.tmp"));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>Publishes an export into a destination whose parent directories do not exist yet.</summary>
    [Fact]
    public void Export_CreatesDestinationDirectoryBeforeWritingArchive()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var source = new LocalStore(Path.Combine(root, "source"));
            var destination = Path.Combine(root, "new", "nested", "history.tmuarchive");

            var result = new DataArchiveService(source).Export(
                new DataArchiveExportRequest(destination, IncludeScreenshots: false), CancellationToken.None);

            Assert.Equal(destination, result.Path);
            using var archive = ZipFile.OpenRead(destination);
            Assert.NotNull(archive.GetEntry("data.sqlite3"));
            Assert.Empty(Directory.EnumerateFiles(Path.GetDirectoryName(destination)!, "*.tmp"));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>A failed search invalidation must roll back the imported rows and the import ledger together.</summary>
    [Fact]
    public void Import_SearchMarkerFailureRollsBackTheMerge()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var archivePath = CreateMinimalArchive(root);
            var targetDirectory = Path.Combine(root, "target");
            var target = new LocalStore(targetDirectory);
            var importer = new DataArchiveService(target);
            var plan = importer.PreviewImport(new DataArchiveImportPreviewRequest(archivePath), CancellationToken.None);
            using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = target.ActivityDatabasePath,
                Mode = SqliteOpenMode.ReadWrite,
                Pooling = false
            }.ToString()))
            {
                connection.Open();
                using var command = connection.CreateCommand();
                command.CommandText = """
                    CREATE TRIGGER fail_archive_search_marker BEFORE INSERT ON search_change_log
                    WHEN NEW.kind = 'rebuild' AND NEW.entity_id = 'archive-import'
                    BEGIN
                        SELECT RAISE(ABORT, 'test search marker failure');
                    END;
                    """;
                command.ExecuteNonQuery();
            }

            var exception = Assert.Throws<SqliteException>(() => importer.Import(plan.PlanId, CancellationToken.None));

            Assert.Contains("test search marker failure", exception.Message, StringComparison.Ordinal);
            Assert.Single(target.GetInstallationProfiles());
            Assert.Equal(0, ReadCount(targetDirectory, "archive_imports"));
            Assert.Equal(0, target.ActivityRevision);
            Assert.DoesNotContain(target.LoadSearchSourceChanges(0, 100), change => change.EntityId == "archive-import");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>Exercises the current SQLite AI path contract and OCR paths across archive and destination roots.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ExportAndImport_WithAiAndOcrRecords_RemapsEveryScreenshotPath(bool includeScreenshots)
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var sourceDirectory = Path.Combine(root, "source");
            var targetDirectory = Path.Combine(root, "target");
            Directory.CreateDirectory(sourceDirectory);
            Directory.CreateDirectory(targetDirectory);
            var source = new LocalStore(sourceDirectory);
            var sourceScreenshots = Path.Combine(sourceDirectory, "screenshots");
            source.SaveSettings(source.LoadSettings() with { ScreenshotDirectory = sourceScreenshots });
            var capturedAt = DateTimeOffset.UtcNow.AddMinutes(-1);
            var captureId = Guid.NewGuid().ToString("N");
            var dayDirectory = ScreenshotStorageLayout.GetDayDirectory(sourceScreenshots, capturedAt);
            Directory.CreateDirectory(dayDirectory);
            var sourcePaths = Enumerable.Range(1, 2)
                .Select(monitor => Path.Combine(dayDirectory, $"{captureId}_1.0.0_manual_monitor-{monitor}.webp"))
                .ToArray();
            foreach (var path in sourcePaths)
            {
                File.WriteAllBytes(path, [1, 2, 3]);
                File.SetLastWriteTimeUtc(path, capturedAt.UtcDateTime);
                source.UpsertScreenshotTextSnapshot(captureId, new ScreenshotTextSnapshot(
                    path,
                    new OcrRawSnapshot(ScreenshotTextExtractionStatus.Succeeded, "Archive test text", "en-US",
                        null, capturedAt, "test-ocr", 100, 100, [])));
            }

            var sourcePathList = string.Join(';', sourcePaths);
            source.UpsertScreenshotIntervalTelemetry(captureId, sourcePaths,
                new ScreenshotIntervalTelemetry(capturedAt.AddMinutes(-5), capturedAt, 12, 4));
            source.RegisterVipScreenshot(captureId);
            Assert.True(source.SaveVipScreenshotNote(new(captureId, "A decision worth keeping across installations.")));
            source.UpsertCaptureHardwareSnapshot(captureId, new SystemSnapshot(capturedAt, "partial",
            [
                new HardwareDeviceSnapshot("/gpu/0", "Archive GPU", "GpuNvidia", capturedAt,
                [new HardwareSensorSnapshot("/gpu/0/power/0", "GPU Power", "Power", "W", 37.5)])
            ]));
            AppendAnalysis(source, captureId, capturedAt, sourcePathList, sourcePaths.Length);
            var archivePath = Path.Combine(root, "ai-history.tmuarchive");
            var exporter = new DataArchiveService(source);
            var exported = exporter.Export(
                new DataArchiveExportRequest(archivePath, IncludeScreenshots: includeScreenshots),
                CancellationToken.None);

            Assert.Equal(1, exported.AiAnalysisCount);
            Assert.Equal(1, exported.AiRequestCount);
            Assert.Equal(includeScreenshots ? 2 : 0, exported.ScreenshotFileCount);
            using (var archive = ZipFile.OpenRead(archivePath))
            {
                var snapshotPath = Path.Combine(root, "exported.sqlite3");
                archive.GetEntry("data.sqlite3")!.ExtractToFile(snapshotPath);
                using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
                {
                    DataSource = snapshotPath,
                    Mode = SqliteOpenMode.ReadOnly,
                    Pooling = false
                }.ToString());
                connection.Open();
                using var command = connection.CreateCommand();
                command.CommandText = "SELECT screenshot_paths FROM ai_analysis_results;";
                var archivedPaths = Assert.IsType<string>(command.ExecuteScalar()).Split(';');
                Assert.Equal(2, archivedPaths.Length);
                Assert.All(archivedPaths, path =>
                {
                    Assert.StartsWith("screenshots/", path, StringComparison.Ordinal);
                    Assert.False(Path.IsPathFullyQualified(path));
                    Assert.DoesNotContain(sourceDirectory, path, StringComparison.OrdinalIgnoreCase);
                });
            }

            var target = new LocalStore(targetDirectory);
            var targetScreenshots = Path.Combine(targetDirectory, "screenshots");
            target.SaveSettings(target.LoadSettings() with { ScreenshotDirectory = targetScreenshots });
            var importer = new DataArchiveService(target);
            var preview = importer.PreviewImport(new DataArchiveImportPreviewRequest(archivePath), CancellationToken.None);
            var imported = importer.Import(preview.PlanId, CancellationToken.None);
            Assert.Equal(1, imported.AddedAiAnalysisCount);
            Assert.Equal(1, imported.AddedAiRequestCount);
            var importedHardware = Assert.IsType<SystemSnapshot>(target.LoadCaptureHardwareSnapshot(captureId));
            Assert.Equal(capturedAt, importedHardware.Timestamp);
            Assert.Equal(37.5d, Assert.Single(Assert.Single(importedHardware.Devices).Sensors).Value);

            var targetPaths = sourcePaths.Select(path => Path.Combine(
                ScreenshotStorageLayout.GetDayDirectory(targetScreenshots, capturedAt), Path.GetFileName(path))).ToArray();
            var importedAnalysis = Assert.IsType<AiAnalysis>(target.LoadAiAnalysis(captureId));
            Assert.Equal(string.Join(';', targetPaths), importedAnalysis.ScreenshotPaths);
            foreach (var path in targetPaths)
            {
                var snapshot = Assert.IsType<ScreenshotTextSnapshot>(target.LoadScreenshotTextSnapshot(path));
                Assert.Equal(path, snapshot.SourceScreenshotPath);
                Assert.Equal("Archive test text", snapshot.Ocr.RawText);
                Assert.Equal(includeScreenshots, File.Exists(path));
            }

            Assert.Equal(sourcePathList, source.LoadAiAnalysis(captureId)!.ScreenshotPaths);
            var vip = new SqliteActivityStore(target.ActivityDatabasePath).LoadScreenshotCaptures([captureId])[captureId];
            Assert.True(vip.IsVip);
            Assert.Equal("A decision worth keeping across installations.", vip.UserNote);
            if (includeScreenshots)
            {
                var gallery = target.GetScreenshotGallery(DateOnly.FromDateTime(capturedAt.LocalDateTime));
                Assert.Equal(2, gallery.Items.Count);
                Assert.All(gallery.Items, item => { Assert.True(item.IsVip); Assert.Equal(vip.UserNote, item.UserNote); });
            }
            Assert.Equal(sourcePaths[0], source.LoadScreenshotTextSnapshot(sourcePaths[0])!.SourceScreenshotPath);
            var repeat = importer.PreviewImport(new DataArchiveImportPreviewRequest(archivePath), CancellationToken.None);
            Assert.True(repeat.AlreadyImported);
            Assert.Equal(1, importer.Import(repeat.PlanId, CancellationToken.None).SkippedAiAnalysisCount);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>Preserves analysis records that have no retained screenshot references.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void ExportAndImport_WithNoAnalysisScreenshotPaths_PreservesTheRecord(string? screenshotPaths)
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var sourceDirectory = Path.Combine(root, "source");
            var targetDirectory = Path.Combine(root, "target");
            Directory.CreateDirectory(sourceDirectory);
            Directory.CreateDirectory(targetDirectory);
            var source = new LocalStore(sourceDirectory);
            var captureId = Guid.NewGuid().ToString("N");
            AppendAnalysis(source, captureId, DateTimeOffset.UtcNow.AddMinutes(-1), screenshotPaths, 0);
            var archivePath = Path.Combine(root, "no-screenshots.tmuarchive");
            _ = new DataArchiveService(source).Export(
                new DataArchiveExportRequest(archivePath, IncludeScreenshots: false), CancellationToken.None);
            var target = new LocalStore(targetDirectory);
            var importer = new DataArchiveService(target);
            var preview = importer.PreviewImport(new DataArchiveImportPreviewRequest(archivePath), CancellationToken.None);
            _ = importer.Import(preview.PlanId, CancellationToken.None);

            Assert.Equal(screenshotPaths, target.LoadAiAnalysis(captureId)!.ScreenshotPaths);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>Preserves the committed import result even when post-commit journal or staging cleanup fails.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("journal")]
    [InlineData("staging")]
    public void ExportPreviewAndMerge_RoundTripsSqlAndScreenshotsIdempotently(string? cleanupFailure)
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var sourceDirectory = Path.Combine(root, "source");
            var targetDirectory = Path.Combine(root, "target");
            var sourceScreenshots = Path.Combine(sourceDirectory, "screenshots");
            var targetScreenshots = Path.Combine(targetDirectory, "screenshots");
            Directory.CreateDirectory(sourceDirectory);
            Directory.CreateDirectory(targetDirectory);

            var source = new LocalStore(sourceDirectory);
            source.SaveSettings(source.LoadSettings() with { ScreenshotDirectory = sourceScreenshots });
            var sourceSettings = source.LoadSettings();
            var capturedAt = DateTimeOffset.UtcNow.AddMinutes(-1);
            source.AppendSample(new ActivitySample(
                capturedAt,
                5,
                "active",
                "test",
                "Archive test",
                "round trip",
                "Archive window",
                sourceSettings.InstallationId,
                2,
                1,
                new Dictionary<string, string> { [ActivityAttributeKeys.SpanLabel] = "Portable work" }));

            var captureId = Guid.NewGuid().ToString("N");
            var dayDirectory = ScreenshotStorageLayout.GetDayDirectory(sourceScreenshots, capturedAt);
            Directory.CreateDirectory(dayDirectory);
            var screenshotPath = Path.Combine(dayDirectory, $"{captureId}_1.0.0_manual_monitor-1.webp");
            File.WriteAllBytes(screenshotPath, [1, 2, 3, 4, 5]);
            File.SetLastWriteTimeUtc(screenshotPath, capturedAt.UtcDateTime);
            source.UpsertScreenshotIntervalTelemetry(
                captureId,
                [screenshotPath],
                new ScreenshotIntervalTelemetry(capturedAt.AddMinutes(-5), capturedAt, 12, 4));

            var archivePath = Path.Combine(root, "history.tmuarchive");
            var exporter = new DataArchiveService(source);
            var exported = exporter.Export(new DataArchiveExportRequest(archivePath), CancellationToken.None);

            Assert.Equal(1, exported.ActivitySampleCount);
            Assert.Equal(1, exported.ScreenshotFileCount);
            Assert.True(File.Exists(archivePath));
            using (var zip = ZipFile.OpenRead(archivePath))
            {
                Assert.Contains(zip.Entries, entry => entry.FullName == "manifest.json");
                Assert.Contains(zip.Entries, entry => entry.FullName == "data.sqlite3");
                Assert.DoesNotContain(zip.Entries, entry => entry.FullName.Contains(sourceDirectory, StringComparison.OrdinalIgnoreCase));
            }

            var target = new LocalStore(targetDirectory);
            target.SaveSettings(target.LoadSettings() with { ScreenshotDirectory = targetScreenshots });
            var logger = new RecordingLogger();
            var journalPath = Path.Combine(targetDirectory, "archive-import-journal.json");
            var failureInjected = false;
            var importer = new DataArchiveService(target, logger, deleteFile: path =>
            {
                if (!failureInjected
                    && ((cleanupFailure == "journal" && path == journalPath)
                        || (cleanupFailure == "staging" && path.EndsWith(".importing", StringComparison.Ordinal))))
                {
                    failureInjected = true;
                    throw new IOException("Simulated post-commit cleanup failure.");
                }

                File.Delete(path);
            });
            var preview = importer.PreviewImport(new DataArchiveImportPreviewRequest(archivePath), CancellationToken.None);
            Assert.False(preview.AlreadyImported);
            Assert.Single(preview.Installations);

            var imported = importer.Import(preview.PlanId, CancellationToken.None);
            Assert.Equal(1, imported.AddedInstallationCount);
            Assert.Equal(1, imported.AddedActivitySampleCount);
            Assert.Equal(1, imported.AddedScreenshotFileCount);
            Assert.Equal(2, target.GetInstallationProfiles().Count);
            Assert.Equal(1, ReadCount(targetDirectory, "activity_samples"));
            Assert.Equal(1, target.ActivityRevision);
            Assert.Contains(target.LoadSearchSourceChanges(0, 100), change => change.Kind == "rebuild" && change.EntityId == "archive-import");
            Assert.Equal(cleanupFailure is not null, failureInjected);
            if (cleanupFailure is not null)
            {
                var warning = Assert.Single(logger.Entries);
                Assert.Equal(LogLevel.Warning, warning.Level);
                Assert.Contains("import committed", warning.Message, StringComparison.Ordinal);
            }
            else
            {
                Assert.Empty(logger.Entries);
            }

            if (cleanupFailure == "journal")
            {
                Assert.True(File.Exists(journalPath));
                _ = new DataArchiveService(target);
                Assert.False(File.Exists(journalPath));
            }

            var importedScreenshot = Path.Combine(
                ScreenshotStorageLayout.GetDayDirectory(targetScreenshots, capturedAt),
                Path.GetFileName(screenshotPath));
            Assert.Equal(new byte[] { 1, 2, 3, 4, 5 }, File.ReadAllBytes(importedScreenshot));
            var gallery = target.GetScreenshotGallery(DateOnly.FromDateTime(capturedAt.ToLocalTime().DateTime));
            var galleryItem = Assert.Single(gallery.Items);
            Assert.Equal(sourceSettings.InstallationId, galleryItem.Installation?.InstallationId);

            var secondPreview = importer.PreviewImport(new DataArchiveImportPreviewRequest(archivePath), CancellationToken.None);
            Assert.True(secondPreview.AlreadyImported);
            var secondImport = importer.Import(secondPreview.PlanId, CancellationToken.None);
            Assert.Equal(0, secondImport.AddedActivitySampleCount);
            Assert.Equal(1, secondImport.SkippedActivitySampleCount);
            Assert.Equal(0, secondImport.AddedScreenshotFileCount);
            Assert.Equal(1, secondImport.SkippedScreenshotFileCount);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void PreviewImport_RejectsCaseInsensitiveArchiveEntryCollisions()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var archivePath = CreateMinimalArchive(root);
            using (var archive = ZipFile.Open(archivePath, ZipArchiveMode.Update))
            {
                var alias = archive.CreateEntry("MANIFEST.JSON");
                using var destination = alias.Open();
                destination.Write([1]);
            }

            var importer = CreateImporter(root);
            var exception = Assert.Throws<InvalidDataException>(() =>
                importer.PreviewImport(new DataArchiveImportPreviewRequest(archivePath), CancellationToken.None));

            Assert.Contains("duplicate", exception.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void PreviewImport_RejectsDeclaredEntriesOutsideTheArchiveContract()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var archivePath = CreateMinimalArchive(root);
            AddDeclaredUnexpectedEntry(archivePath, "notes.txt", Encoding.UTF8.GetBytes("not WorkTrail data"));

            var importer = CreateImporter(root);
            Assert.Throws<InvalidDataException>(() =>
                importer.PreviewImport(new DataArchiveImportPreviewRequest(archivePath), CancellationToken.None));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void PreviewImport_RejectsTraversalEntryNames()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var archivePath = CreateMinimalArchive(root);
            using (var archive = ZipFile.Open(archivePath, ZipArchiveMode.Update))
            {
                var traversal = archive.CreateEntry("../outside.webp");
                using var destination = traversal.Open();
                destination.Write([1]);
            }

            var importer = CreateImporter(root);
            Assert.Throws<InvalidDataException>(() =>
                importer.PreviewImport(new DataArchiveImportPreviewRequest(archivePath), CancellationToken.None));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ConstructorRecovery_UsesImportLedgerAsTheCommitAuthority()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var targetDirectory = Path.Combine(root, "recovery-target");
            var screenshotRoot = Path.Combine(targetDirectory, "screenshots");
            Directory.CreateDirectory(targetDirectory);
            var target = new LocalStore(targetDirectory);
            target.SaveSettings(target.LoadSettings() with { ScreenshotDirectory = screenshotRoot });

            var capturedAt = DateTimeOffset.UtcNow.AddMinutes(-1);
            var uncommittedPath = CreateRecoveryScreenshot(screenshotRoot, capturedAt, [1, 2, 3]);
            var uncommittedArchiveId = Guid.NewGuid();
            var uncommittedFingerprint = new string('a', 64);
            WriteRecoveryJournal(targetDirectory, uncommittedArchiveId, uncommittedFingerprint, uncommittedPath);

            _ = new DataArchiveService(target);

            Assert.False(File.Exists(uncommittedPath));
            Assert.False(File.Exists(Path.Combine(targetDirectory, "archive-import-journal.json")));

            var committedPath = CreateRecoveryScreenshot(screenshotRoot, capturedAt, [4, 5, 6]);
            var committedArchiveId = Guid.NewGuid();
            var committedFingerprint = new string('b', 64);
            InsertImportLedger(targetDirectory, committedArchiveId, committedFingerprint);
            WriteRecoveryJournal(targetDirectory, committedArchiveId, committedFingerprint, committedPath);

            _ = new DataArchiveService(target);

            Assert.True(File.Exists(committedPath));
            Assert.False(File.Exists(Path.Combine(targetDirectory, "archive-import-journal.json")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>Round-tripping existing OCR ignores JSON presentation but still rejects changed data at the same revision.</summary>
    [Theory]
    [InlineData("compact", false)]
    [InlineData("reordered", false)]
    [InlineData("ocr", true)]
    [InlineData("extracted", true)]
    public void PreviewImport_ExistingSnapshot_ComparesContentInsteadOfJsonFormatting(string mutation, bool conflict)
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var store = new LocalStore(Path.Combine(root, "source"));
            var screenshotRoot = Path.Combine(root, "screenshots");
            store.SaveSettings(store.LoadSettings() with { ScreenshotDirectory = screenshotRoot });
            var capturedAt = DateTimeOffset.UtcNow.AddMinutes(-1);
            var captureId = Guid.NewGuid().ToString("N");
            var dayDirectory = ScreenshotStorageLayout.GetDayDirectory(screenshotRoot, capturedAt);
            Directory.CreateDirectory(dayDirectory);
            var screenshot = Path.Combine(dayDirectory, $"{captureId}_1.0.0_manual_monitor-1.webp");
            File.WriteAllBytes(screenshot, [1, 2, 3]);
            File.SetLastWriteTimeUtc(screenshot, capturedAt.UtcDateTime);
            store.UpsertScreenshotIntervalTelemetry(captureId, [screenshot],
                new ScreenshotIntervalTelemetry(capturedAt.AddMinutes(-5), capturedAt, 12, 4));
            store.UpsertScreenshotTextSnapshot(captureId, new ScreenshotTextSnapshot(screenshot,
                new OcrRawSnapshot(ScreenshotTextExtractionStatus.Succeeded, "Archive text\nCafé Ω", "en-US", null, capturedAt, "test-ocr", 100, 100, [])));
            AppendAnalysis(store, captureId, capturedAt, screenshot, 1);
            var archivePath = Path.Combine(root, "same-history.tmuarchive");
            var service = new DataArchiveService(store);
            service.Export(new DataArchiveExportRequest(archivePath, IncludeScreenshots: false), CancellationToken.None);
            var archiveHash = SHA256.HashData(File.ReadAllBytes(archivePath));
            var snapshotDirectory = Path.Combine(root, "archive-check");
            Directory.CreateDirectory(snapshotDirectory);
            using (var archive = ZipFile.OpenRead(archivePath))
                archive.GetEntry("data.sqlite3")!.ExtractToFile(Path.Combine(snapshotDirectory, SqliteActivityStore.DatabaseFileName));
            // A missing capture would be pruned on export, making both accepted and rejected cases meaningless.
            Assert.Equal(1, ReadCount(snapshotDirectory, "screenshot_captures"));
            Assert.Equal(1, ReadCount(snapshotDirectory, "screenshot_text_snapshots"));

            using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = store.ActivityDatabasePath,
                Mode = SqliteOpenMode.ReadWrite,
                Pooling = false
            }.ToString());
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT snapshot_json FROM screenshot_text_snapshots;";
            var json = JsonNode.Parse((string)command.ExecuteScalar()!)!.AsObject();
            if (mutation == "reordered")
            {
                var reordered = new JsonObject();
                foreach (var property in json.Reverse()) reordered.Add(property.Key, property.Value?.DeepClone());
                json = reordered;
            }
            if (mutation == "ocr") json["ocr"]!["rawText"] = "Actually different OCR";
            command.CommandText = mutation == "extracted"
                ? "UPDATE screenshot_text_snapshots SET extracted_utc_ticks = extracted_utc_ticks + 1;"
                : "UPDATE screenshot_text_snapshots SET snapshot_json = $json;";
            if (mutation != "extracted") command.Parameters.AddWithValue("$json", json.ToJsonString());
            command.ExecuteNonQuery();
            command.Parameters.Clear();
            command.CommandText = "SELECT snapshot_json FROM screenshot_text_snapshots;";
            var before = (string)command.ExecuteScalar()!;

            if (conflict)
            {
                var error = Assert.Throws<InvalidDataException>(() => service.PreviewImport(new(archivePath), CancellationToken.None));
                Assert.Contains("conflicting screenshot_text_snapshots revision", error.Message);
            }
            else
            {
                var preview = service.PreviewImport(new(archivePath), CancellationToken.None);
                Assert.False(preview.AlreadyImported);
                service.Import(preview.PlanId, CancellationToken.None);
                Assert.True(service.PreviewImport(new(archivePath), CancellationToken.None).AlreadyImported);
            }
            Assert.Equal(before, command.ExecuteScalar());
            Assert.Equal(1, ReadCount(Path.Combine(root, "source"), "screenshot_text_snapshots"));
            Assert.Equal(archiveHash, SHA256.HashData(File.ReadAllBytes(archivePath)));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(root, recursive: true);
        }
    }

    private static SqliteConnection LockDatabaseForExport(LocalStore store)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = store.ActivityDatabasePath,
            Mode = SqliteOpenMode.ReadWrite,
            Pooling = false
        }.ToString());
        try
        {
            connection.Open();
            using var command = connection.CreateCommand();
            // An exclusive rollback-journal transaction deterministically blocks a separate backup reader.
            // This changes only the isolated fixture; production continues to use WAL.
            command.CommandText = "PRAGMA journal_mode = DELETE; BEGIN EXCLUSIVE;";
            command.ExecuteNonQuery();
            return connection;
        }
        catch
        {
            connection.Dispose();
            throw;
        }
    }

    private static void AppendAnalysis(
        LocalStore store,
        string captureId,
        DateTimeOffset capturedAt,
        string? screenshotPaths,
        int imageCount)
    {
        var usage = new AiRequestUsageRecord(
            Guid.NewGuid().ToString("N"), captureId, capturedAt, capturedAt.AddMilliseconds(10),
            "snapshot.manual", "screen_analysis", "test-provider", "provider.invalid", "test-model", "test-model",
            null, null, 200, 10, null, imageCount, 10, 100, new AiUsageMetrics(10, 5, 15), "stop", true, null);
        var analysis = new AiAnalysis(
            capturedAt, "Test", "Archive", "Portable AI analysis", store.LoadSettings().InstallationId,
            screenshotPaths, CorrelationId: captureId, Origin: "snapshot.manual");
        store.AppendAiAnalysisAndUsage(usage, analysis);
    }

    private static int ReadCount(string dataDirectory, string table)
    {
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = Path.Combine(dataDirectory, SqliteActivityStore.DatabaseFileName),
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false
        };
        using var connection = new SqliteConnection(builder.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM {table};";
        return Convert.ToInt32(command.ExecuteScalar());
    }

    private static string CreateMinimalArchive(string root)
    {
        var sourceDirectory = Path.Combine(root, "minimal-source");
        Directory.CreateDirectory(sourceDirectory);
        var source = new LocalStore(sourceDirectory);
        var archivePath = Path.Combine(root, Guid.NewGuid().ToString("N") + ".tmuarchive");
        _ = new DataArchiveService(source).Export(
            new DataArchiveExportRequest(archivePath, IncludeScreenshots: false),
            CancellationToken.None);
        return archivePath;
    }

    private static DataArchiveService CreateImporter(string root)
    {
        var targetDirectory = Path.Combine(root, "import-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(targetDirectory);
        return new DataArchiveService(new LocalStore(targetDirectory));
    }

    private static void AddDeclaredUnexpectedEntry(string archivePath, string entryName, byte[] content)
    {
        using var archive = ZipFile.Open(archivePath, ZipArchiveMode.Update);
        var manifestEntry = archive.GetEntry("manifest.json")
            ?? throw new InvalidOperationException("The test archive manifest is missing.");
        JsonObject manifest;
        using (var manifestStream = manifestEntry.Open())
        {
            manifest = JsonNode.Parse(manifestStream)?.AsObject()
                ?? throw new InvalidOperationException("The test archive manifest is invalid.");
        }

        manifestEntry.Delete();
        var entries = manifest["entries"]?.AsArray()
            ?? throw new InvalidOperationException("The test archive entry list is missing.");
        entries.Add(new JsonObject
        {
            ["path"] = entryName,
            ["length"] = content.LongLength,
            ["sha256"] = Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant()
        });

        var unexpected = archive.CreateEntry(entryName);
        using (var destination = unexpected.Open())
        {
            destination.Write(content);
        }

        var replacementManifest = archive.CreateEntry("manifest.json");
        using var replacementStream = replacementManifest.Open();
        JsonSerializer.Serialize(replacementStream, manifest);
    }

    private static string CreateRecoveryScreenshot(
        string screenshotRoot,
        DateTimeOffset capturedAt,
        byte[] content)
    {
        var dayDirectory = ScreenshotStorageLayout.GetDayDirectory(screenshotRoot, capturedAt);
        Directory.CreateDirectory(dayDirectory);
        var path = Path.Combine(
            dayDirectory,
            $"{Guid.NewGuid():N}_1.0.0_manual_monitor-1.webp");
        File.WriteAllBytes(path, content);
        return path;
    }

    private static void WriteRecoveryJournal(
        string dataDirectory,
        Guid archiveId,
        string fingerprint,
        string screenshotPath)
    {
        var content = File.ReadAllBytes(screenshotPath);
        var journal = new
        {
            archiveId,
            fingerprint,
            files = new[]
            {
                new
                {
                    path = screenshotPath,
                    sha256 = Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant()
                }
            }
        };
        File.WriteAllText(
            Path.Combine(dataDirectory, "archive-import-journal.json"),
            JsonSerializer.Serialize(journal, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
    }

    private static void InsertImportLedger(string dataDirectory, Guid archiveId, string fingerprint)
    {
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = Path.Combine(dataDirectory, SqliteActivityStore.DatabaseFileName),
            Mode = SqliteOpenMode.ReadWrite,
            Pooling = false
        };
        using var connection = new SqliteConnection(builder.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO archive_imports (archive_id, archive_fingerprint, imported_utc_ticks)
            VALUES ($archiveId, $fingerprint, $importedAt);
            """;
        command.Parameters.AddWithValue("$archiveId", archiveId.ToString("N"));
        command.Parameters.AddWithValue("$fingerprint", fingerprint);
        command.Parameters.AddWithValue("$importedAt", DateTimeOffset.UtcNow.UtcDateTime.Ticks);
        command.ExecuteNonQuery();
    }

    private static string CreateTemporaryDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "WorkTrail.Archive.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private sealed class RecordingLogger(Action? onFirstEntry = null) : ILogger
    {
        internal List<(LogLevel Level, string Message)> Entries { get; } = [];

        /// <inheritdoc />
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        /// <inheritdoc />
        public bool IsEnabled(LogLevel logLevel) => true;

        /// <inheritdoc />
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Entries.Add((logLevel, formatter(state, exception)));
            if (Entries.Count == 1) onFirstEntry?.Invoke();
        }
    }
}
