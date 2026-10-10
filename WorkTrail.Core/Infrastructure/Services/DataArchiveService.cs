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


using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using WorkTrail.Application;

namespace WorkTrail.Services;

/// <summary>
/// Creates and merges private portable archives without exporting settings, secrets, caches, or runtime jobs.
/// </summary>
internal sealed class DataArchiveService
{
    private const int ArchiveSchemaVersion = 1;
    private const int MaximumArchiveEntries = 100_000;
    private const long MaximumDatabaseBytes = 4L * 1024 * 1024 * 1024;
    private const long MaximumManifestBytes = 4L * 1024 * 1024;
    private const long MaximumScreenshotBytes = 256L * 1024 * 1024;
    private const long MaximumArchiveUncompressedBytes = 64L * 1024 * 1024 * 1024;
    private const string ManifestEntryName = "manifest.json";
    private const string DatabaseEntryName = "data.sqlite3";
    private const string ScreenshotEntryPrefix = "screenshots/";
    private const string ArchiveExtension = ".tmuarchive";
    private static readonly TimeSpan ImportPlanLifetime = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan SnapshotLockTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan SnapshotRetryDelay = TimeSpan.FromMilliseconds(100);

    private static readonly string[] ActivityColumns =
    [
        "sample_id", "timestamp_utc_ticks", "start_utc_ticks", "timestamp_offset_minutes",
        "duration_seconds", "state", "process_name", "application", "context", "window_title",
        "installation_id", "key_presses", "mouse_clicks", "attributes_json", "estimated_bytes"
    ];

    private static readonly string[] AiRequestColumns =
    [
        "attempt_id", "correlation_id", "occurred_utc_ticks", "completed_utc_ticks", "origin", "request_kind",
        "provider", "endpoint_host", "requested_model", "returned_model", "provider_response_id", "provider_request_id",
        "http_status", "elapsed_ms", "provider_processing_ms", "image_count", "prompt_characters", "max_output_tokens",
        "input_tokens", "output_tokens", "total_tokens", "cached_input_tokens", "cache_write_tokens",
        "cache_creation_input_tokens", "cache_read_input_tokens", "reasoning_tokens", "thinking_tokens",
        "reported_cost_microusd", "reported_upstream_cost_microusd", "cost_source", "finish_reason", "success", "failure_code"
    ];

    private static readonly string[] AiAnalysisColumns =
    [
        "correlation_id", "attempt_id", "timestamp_utc_ticks", "snapshot_utc_ticks", "application", "context",
        "summary", "installation_id", "origin", "informational_schedule", "screenshot_paths", "image_count"
    ];

    private static readonly string[] ScreenshotCaptureColumns =
    [
        "capture_id", "installation_id", "captured_utc_ticks", "origin"
    ];

    private static readonly string[] VipScreenshotColumns =
    [
        "capture_id", "note", "updated_utc_ticks"
    ];

    private static readonly string[] ScreenshotSnapshotColumns =
    [
        "artifact_identity", "capture_id", "source_path", "extracted_utc_ticks", "snapshot_json", "updated_utc_ticks"
    ];

    private static readonly string[] ScreenshotTelemetryColumns =
    [
        "artifact_identity", "capture_id", "interval_started_utc_ticks", "captured_utc_ticks",
        "cpu_usage_percent", "gpu_usage_percent", "updated_utc_ticks"
    ];

    private static readonly string[] CaptureHardwareSnapshotColumns =
    [
        "capture_id", "sampled_utc_ticks", "snapshot_json"
    ];

    private static readonly string[] AiArtifactColumns =
    [
        "artifact_identity", "capture_id", "correlation_id"
    ];

    private static readonly string[] InstallationColumns =
    [
        "installation_id", "machine_name", "friendly_name", "color", "icon",
        "first_seen_utc_ticks", "updated_utc_ticks", "profile_revision"
    ];

    private readonly LocalStore _store;
    private readonly ILogger _logger;
    private readonly Action<string> _deleteFile;
    private readonly ConcurrentDictionary<Guid, PendingImportPlan> _pendingPlans = new();
    private readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };
    private readonly string _journalPath;

    internal DataArchiveService(LocalStore store, ILogger? logger = null, Action<string>? deleteFile = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _logger = logger ?? NullLogger.Instance;
        _deleteFile = deleteFile ?? DeleteFileIfPresent;
        _journalPath = Path.Combine(_store.DataDirectory, "archive-import-journal.json");
        RecoverInterruptedImport();
    }

    internal DataArchiveExportResult Export(DataArchiveExportRequest request, CancellationToken cancellationToken, Action<DataArchiveProgress>? progress = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        var destination = ValidateArchivePath(request.DestinationPath, mustExist: false);
        var (fromUtcTicks, toUtcTicks) = ResolveUtcRange(request.From, request.ToInclusive);
        var createdAt = DateTimeOffset.UtcNow;
        var archiveId = Guid.NewGuid();
        var workDirectory = Path.Combine(Path.GetTempPath(), "WorkTrail.Archive." + Guid.NewGuid().ToString("N"));
        var snapshotPath = Path.Combine(workDirectory, DatabaseEntryName);
        var temporaryArchivePath = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";

        Directory.CreateDirectory(workDirectory);
        try
        {
            progress?.Invoke(new(DataArchivePhase.PreparingDatabase));
            CreateConsistentDatabaseSnapshot(snapshotPath, cancellationToken);
            SanitizeArchiveDatabase(snapshotPath, fromUtcTicks, toUtcTicks, cancellationToken);

            var settings = _store.LoadSettings();
            var screenshotRoot = ScreenshotStorageLayout.NormalizeRoot(settings.ScreenshotDirectory);
            RewriteDatabasePathsForArchive(snapshotPath, screenshotRoot, cancellationToken);
            CompactDatabase(snapshotPath);
            EnsureDatabaseContainsNoAbsoluteScreenshotPaths(snapshotPath, cancellationToken);

            var databaseSummary = ReadDatabaseSummary(snapshotPath, cancellationToken);
            progress?.Invoke(new(DataArchivePhase.ScanningScreenshots));
            var screenshotEntries = request.IncludeScreenshots
                ? BuildScreenshotEntries(snapshotPath, screenshotRoot, progress, cancellationToken)
                : Array.Empty<ArchiveSourceEntry>();
            var entryManifest = new List<ArchiveEntryManifest>(screenshotEntries.Count + 1)
            {
                BuildEntryManifest(DatabaseEntryName, snapshotPath, MaximumDatabaseBytes, cancellationToken)
            };
            entryManifest.AddRange(screenshotEntries.Select(entry => entry.Manifest));

            var manifest = new DataArchiveManifest(
                ArchiveSchemaVersion,
                archiveId,
                createdAt,
                request.From,
                request.ToInclusive,
                request.IncludeScreenshots,
                databaseSummary.Installations,
                databaseSummary.ActivitySampleCount,
                databaseSummary.AiRequestCount,
                databaseSummary.AiAnalysisCount,
                screenshotEntries.Count,
                screenshotEntries.Sum(entry => entry.Manifest.Length),
                entryManifest);
            var parent = Path.GetDirectoryName(destination)
                ?? throw new InvalidDataException("The archive destination has no parent directory.");
            // The temporary archive shares the destination directory so final publication stays atomic.
            Directory.CreateDirectory(parent);
            WriteArchive(temporaryArchivePath, snapshotPath, screenshotEntries, manifest, progress, cancellationToken);
            progress?.Invoke(new(DataArchivePhase.Finalizing));
            File.Move(temporaryArchivePath, destination, overwrite: true);
            return new DataArchiveExportResult(
                archiveId,
                destination,
                createdAt,
                request.From,
                request.ToInclusive,
                databaseSummary.Installations.Count,
                databaseSummary.ActivitySampleCount,
                databaseSummary.AiRequestCount,
                databaseSummary.AiAnalysisCount,
                screenshotEntries.Count,
                screenshotEntries.Sum(entry => entry.Manifest.Length));
        }
        finally
        {
            DeleteFileIfPresent(temporaryArchivePath);
            DeleteDirectoryIfPresent(workDirectory);
        }
    }

    internal DataArchiveImportPlan PreviewImport(
        DataArchiveImportPreviewRequest request,
        CancellationToken cancellationToken, Action<DataArchiveProgress>? progress = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        var archivePath = ValidateArchivePath(request.ArchivePath, mustExist: true);
        var validation = ValidateAndExtractArchiveDatabase(archivePath, progress, cancellationToken);
        try
        {
            progress?.Invoke(new(DataArchivePhase.CheckingData));
            RewriteDatabasePathsForImport(validation.DatabasePath, CurrentScreenshotRoot(), cancellationToken);
            PreflightDatabaseMerge(validation.DatabasePath, validation.Manifest, validation.Fingerprint, cancellationToken);
            PreflightScreenshotMerge(validation.ArchivePath, validation.Manifest, cancellationToken);
            var alreadyImported = IsArchiveImported(validation.Manifest.ArchiveId, validation.Fingerprint);
            var planId = Guid.NewGuid();
            var expiresAt = DateTimeOffset.UtcNow.Add(ImportPlanLifetime);
            RemoveExpiredPlans();
            _pendingPlans[planId] = new PendingImportPlan(
                planId,
                archivePath,
                validation.Fingerprint,
                validation.Manifest.ArchiveId,
                expiresAt);
            return ToPublicPlan(planId, expiresAt, validation.Manifest, validation.Fingerprint, alreadyImported);
        }
        finally
        {
            DeleteDirectoryIfPresent(validation.WorkDirectory);
        }
    }

    internal DataArchiveImportResult Import(Guid planId, CancellationToken cancellationToken, Action<DataArchiveProgress>? progress = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_pendingPlans.TryRemove(planId, out var pending)
            || pending.ExpiresAt <= DateTimeOffset.UtcNow)
        {
            throw new InvalidOperationException("The archive import plan is missing or expired.");
        }

        var validation = ValidateAndExtractArchiveDatabase(pending.ArchivePath, progress, cancellationToken);
        var mergeCommitted = false;
        try
        {
            if (!string.Equals(validation.Fingerprint, pending.Fingerprint, StringComparison.Ordinal)
                || validation.Manifest.ArchiveId != pending.ArchiveId)
            {
                throw new InvalidDataException("The archive changed after its import preview.");
            }

            var screenshotRoot = CurrentScreenshotRoot();
            progress?.Invoke(new(DataArchivePhase.CheckingData));
            RewriteDatabasePathsForImport(validation.DatabasePath, screenshotRoot, cancellationToken);
            PreflightDatabaseMerge(validation.DatabasePath, validation.Manifest, validation.Fingerprint, cancellationToken);
            var screenshotPlan = PreflightScreenshotMerge(validation.ArchivePath, validation.Manifest, cancellationToken);
            var staging = StageScreenshotFiles(validation.ArchivePath, screenshotPlan, planId, progress, cancellationToken);
            try
            {
                var result = MergeDatabaseAndFiles(
                    validation.DatabasePath,
                    validation.Manifest,
                    validation.Fingerprint,
                    screenshotPlan,
                    staging,
                    progress, cancellationToken);
                mergeCommitted = true;
                CleanupImportResource(() => _deleteFile(_journalPath), mergeCommitted, "recovery journal");
                return result;
            }
            finally
            {
                foreach (var stagedPath in staging.Values)
                {
                    CleanupImportResource(() => _deleteFile(stagedPath), mergeCommitted, "staged screenshot");
                }
            }
        }
        finally
        {
            CleanupImportResource(() => DeleteDirectoryIfPresent(validation.WorkDirectory), mergeCommitted, "temporary database");
        }
    }

    private void CleanupImportResource(Action cleanup, bool mergeCommitted, string resource)
    {
        try
        {
            cleanup();
        }
        catch (Exception exception) when (mergeCommitted && exception is IOException or UnauthorizedAccessException)
        {
            // The durable ledger is authoritative after COMMIT. A cleanup failure cannot undo the import;
            // retain its successful result and leave any journal available for the next startup recovery.
            _logger.LogWarning(exception,
                "Data archive import committed, but {Resource} cleanup failed. ExceptionType={ExceptionType}",
                resource, exception.GetType().Name);
        }
    }

    private void CreateConsistentDatabaseSnapshot(string snapshotPath, CancellationToken cancellationToken)
    {
        var sourceBuilder = new SqliteConnectionStringBuilder
        {
            DataSource = _store.ActivityDatabasePath,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false
        };
        var destinationBuilder = new SqliteConnectionStringBuilder
        {
            DataSource = snapshotPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false
        };
        using var source = new SqliteConnection(sourceBuilder.ToString());
        using var destination = new SqliteConnection(destinationBuilder.ToString());
        source.Open();
        destination.Open();
        var startedAt = Stopwatch.GetTimestamp();
        var lockReported = false;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                // BackupDatabase makes one native backup attempt without retrying SQLITE_BUSY/SQLITE_LOCKED.
                // Each failed attempt rolls back its destination; retry only the consistent snapshot operation.
                source.BackupDatabase(destination);
                cancellationToken.ThrowIfCancellationRequested();
                return;
            }
            catch (SqliteException exception) when (exception.SqliteErrorCode is 5 or 6)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var remaining = SnapshotLockTimeout - Stopwatch.GetElapsedTime(startedAt);
                if (remaining <= TimeSpan.Zero)
                {
                    // Persistent locks fail with the original SQLite error; never publish a partial snapshot.
                    throw;
                }

                if (!lockReported)
                {
                    _logger.LogWarning(
                        "Data archive snapshot is locked; retrying for up to {TimeoutSeconds} seconds. SqliteErrorCode={SqliteErrorCode}",
                        SnapshotLockTimeout.TotalSeconds, exception.SqliteErrorCode);
                    lockReported = true;
                }

                // Shutdown/cancellation interrupts the wait instead of waiting for the retry budget to expire.
                cancellationToken.WaitHandle.WaitOne(remaining < SnapshotRetryDelay ? remaining : SnapshotRetryDelay);
            }
        }
    }

    private static void SanitizeArchiveDatabase(
        string databasePath,
        long? fromUtcTicks,
        long? toUtcTicks,
        CancellationToken cancellationToken)
    {
        using var connection = OpenDatabase(databasePath, readOnly: false);
        ExecuteNonQuery(connection, "PRAGMA foreign_keys = OFF;");
        using var transaction = connection.BeginTransaction();
        if (fromUtcTicks is { } from && toUtcTicks is { } to)
        {
            ExecuteNonQuery(connection, transaction,
                "DELETE FROM activity_samples WHERE timestamp_utc_ticks < $from OR timestamp_utc_ticks >= $to;",
                ("$from", from), ("$to", to));
            ExecuteNonQuery(connection, transaction,
                "DELETE FROM ai_analysis_results WHERE timestamp_utc_ticks < $from OR timestamp_utc_ticks >= $to;",
                ("$from", from), ("$to", to));
            ExecuteNonQuery(connection, transaction,
                "DELETE FROM ai_request_usage WHERE (occurred_utc_ticks < $from OR occurred_utc_ticks >= $to) " +
                "AND attempt_id NOT IN (SELECT attempt_id FROM ai_analysis_results);",
                ("$from", from), ("$to", to));
            ExecuteNonQuery(connection, transaction,
                "DELETE FROM screenshot_captures WHERE captured_utc_ticks < $from OR captured_utc_ticks >= $to;",
                ("$from", from), ("$to", to));
        }

        cancellationToken.ThrowIfCancellationRequested();
        ExecuteNonQuery(connection, transaction,
            "DELETE FROM ai_analysis_artifacts WHERE correlation_id NOT IN (SELECT correlation_id FROM ai_analysis_results) " +
            "OR capture_id NOT IN (SELECT capture_id FROM screenshot_captures);");
        ExecuteNonQuery(connection, transaction,
            "DELETE FROM screenshot_text_snapshots WHERE capture_id NOT IN (SELECT capture_id FROM screenshot_captures);");
        ExecuteNonQuery(connection, transaction,
            "DELETE FROM screenshot_interval_telemetry WHERE capture_id NOT IN (SELECT capture_id FROM screenshot_captures);");
        ExecuteNonQuery(connection, transaction,
            "DELETE FROM capture_hardware_snapshots WHERE capture_id NOT IN (SELECT capture_id FROM screenshot_captures);");
        ExecuteNonQuery(connection, transaction, "DELETE FROM ai_analysis_search;");
        ExecuteNonQuery(connection, transaction, "DELETE FROM ai_model_pricing;");
        ExecuteNonQuery(connection, transaction, "DELETE FROM ai_reprocess_job_items;");
        ExecuteNonQuery(connection, transaction, "DELETE FROM ai_reprocess_jobs;");
        ExecuteNonQuery(connection, transaction, "DELETE FROM archive_imports;");
        ExecuteNonQuery(connection, transaction, "DELETE FROM store_metadata;");
        transaction.Commit();
    }

    private void RewriteDatabasePathsForArchive(
        string databasePath,
        string screenshotRoot,
        CancellationToken cancellationToken)
    {
        RewriteDatabaseScreenshotPaths(
            databasePath,
            path => ToArchiveScreenshotPath(screenshotRoot, path),
            cancellationToken);
    }

    private void RewriteDatabasePathsForImport(
        string databasePath,
        string screenshotRoot,
        CancellationToken cancellationToken)
    {
        RewriteDatabaseScreenshotPaths(
            databasePath,
            path => ToLocalScreenshotPath(screenshotRoot, path),
            cancellationToken);
    }

    private void RewriteDatabaseScreenshotPaths(
        string databasePath,
        Func<string, string> pathMapper,
        CancellationToken cancellationToken)
    {
        using var connection = OpenDatabase(databasePath, readOnly: false);
        using var transaction = connection.BeginTransaction();

        var analysisUpdates = new List<(string CorrelationId, string ScreenshotPaths)>();
        using (var select = connection.CreateCommand())
        {
            select.Transaction = transaction;
            select.CommandText = "SELECT correlation_id, screenshot_paths FROM ai_analysis_results WHERE screenshot_paths IS NOT NULL;";
            using var reader = select.ExecuteReader();
            while (reader.Read())
            {
                cancellationToken.ThrowIfCancellationRequested();
                // Archives retain the current SQLite schema: only the path roots change.
                // Invalid or unowned paths fail in the mapper rather than being omitted.
                var paths = SqliteActivityStore.EnumerateScreenshotPaths(reader.GetString(1));
                analysisUpdates.Add((
                    reader.GetString(0),
                    string.Join(';', paths.Select(pathMapper))));
            }
        }

        foreach (var update in analysisUpdates)
        {
            ExecuteNonQuery(connection, transaction,
                "UPDATE ai_analysis_results SET screenshot_paths = $paths WHERE correlation_id = $id;",
                ("$paths", update.ScreenshotPaths), ("$id", update.CorrelationId));
        }

        var snapshotUpdates = new List<(string Identity, string SourcePath, string SnapshotJson)>();
        using (var select = connection.CreateCommand())
        {
            select.Transaction = transaction;
            select.CommandText = "SELECT artifact_identity, source_path, snapshot_json FROM screenshot_text_snapshots;";
            using var reader = select.ExecuteReader();
            while (reader.Read())
            {
                cancellationToken.ThrowIfCancellationRequested();
                var sourcePath = pathMapper(reader.GetString(1));
                var snapshot = JsonSerializer.Deserialize<ScreenshotTextSnapshot>(reader.GetString(2), _json)
                    ?? throw new InvalidDataException("An archived screenshot text snapshot is invalid.");
                var snapshotPath = pathMapper(snapshot.SourceScreenshotPath);
                if (!string.Equals(sourcePath, snapshotPath, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException("Screenshot text paths do not refer to the same artifact.");
                }

                snapshotUpdates.Add((
                    reader.GetString(0),
                    sourcePath,
                    JsonSerializer.Serialize(snapshot with { SourceScreenshotPath = snapshotPath }, _json)));
            }
        }

        foreach (var update in snapshotUpdates)
        {
            ExecuteNonQuery(connection, transaction,
                "UPDATE screenshot_text_snapshots SET source_path = $path, snapshot_json = $json WHERE artifact_identity = $id;",
                ("$path", update.SourcePath), ("$json", update.SnapshotJson), ("$id", update.Identity));
        }

        transaction.Commit();
    }

    private static void CompactDatabase(string databasePath)
    {
        using var connection = OpenDatabase(databasePath, readOnly: false);
        ExecuteNonQuery(connection, "VACUUM;");
        ExecuteNonQuery(connection, "PRAGMA journal_mode = DELETE;");
    }

    private void EnsureDatabaseContainsNoAbsoluteScreenshotPaths(
        string databasePath,
        CancellationToken cancellationToken)
    {
        using var connection = OpenDatabase(databasePath, readOnly: true);
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT screenshot_paths FROM ai_analysis_results WHERE screenshot_paths IS NOT NULL;";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                cancellationToken.ThrowIfCancellationRequested();
                var paths = SqliteActivityStore.EnumerateScreenshotPaths(reader.GetString(0));
                foreach (var path in paths)
                {
                    ValidateArchiveScreenshotPath(path);
                }
            }
        }

        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT source_path, snapshot_json FROM screenshot_text_snapshots;";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                cancellationToken.ThrowIfCancellationRequested();
                ValidateArchiveScreenshotPath(reader.GetString(0));
                var snapshot = JsonSerializer.Deserialize<ScreenshotTextSnapshot>(reader.GetString(1), _json)
                    ?? throw new InvalidDataException("An archive screenshot text snapshot is invalid.");
                ValidateArchiveScreenshotPath(snapshot.SourceScreenshotPath);
            }
        }
    }

    private DatabaseSummary ReadDatabaseSummary(string databasePath, CancellationToken cancellationToken)
    {
        ValidateArchiveDatabase(databasePath, cancellationToken);
        using var connection = OpenDatabase(databasePath, readOnly: true);
        var installations = ReadInstallationProfiles(connection)
            .Select(profile => new DataArchiveInstallationSummary(
                profile.InstallationId,
                profile.MachineName,
                profile.FriendlyName,
                profile.Color,
                profile.Icon))
            .ToArray();
        return new DatabaseSummary(
            installations,
            ReadCount(connection, "activity_samples"),
            ReadCount(connection, "ai_request_usage"),
            ReadCount(connection, "ai_analysis_results"));
    }

    private IReadOnlyList<ArchiveSourceEntry> BuildScreenshotEntries(
        string archiveDatabasePath,
        string screenshotRoot,
        Action<DataArchiveProgress>? progress, CancellationToken cancellationToken)
    {
        using var connection = OpenDatabase(archiveDatabasePath, readOnly: true);
        var captureIds = ReadStringSet(connection, "SELECT capture_id FROM screenshot_captures;");
        var entries = new List<ArchiveSourceEntry>();
        var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var sourcePath in ScreenshotStorageLayout.EnumerateOwnedArtifacts(screenshotRoot))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var identity = LocalStore.ScreenshotIdentity(Path.GetFileName(sourcePath));
            var captureId = TryGetCaptureId(identity);
            if (captureId is null || !captureIds.Contains(captureId))
            {
                continue;
            }

            var archivePath = ToArchiveScreenshotPath(screenshotRoot, sourcePath);
            if (!seenPaths.Add(archivePath))
            {
                throw new InvalidDataException("The screenshot archive contains a duplicate destination path.");
            }

            var manifest = BuildEntryManifest(archivePath, sourcePath, MaximumScreenshotBytes, cancellationToken);
            entries.Add(new ArchiveSourceEntry(sourcePath, manifest));
            progress?.Invoke(new(DataArchivePhase.ScanningScreenshots, entries.Count));
        }

        return entries.OrderBy(entry => entry.Manifest.Path, StringComparer.Ordinal).ToArray();
    }

    private void WriteArchive(
        string archivePath,
        string databasePath,
        IReadOnlyList<ArchiveSourceEntry> screenshotEntries,
        DataArchiveManifest manifest,
        Action<DataArchiveProgress>? progress, CancellationToken cancellationToken)
    {
        using var stream = new FileStream(
            archivePath,
            FileMode.CreateNew,
            FileAccess.ReadWrite,
            FileShare.None,
            1024 * 1024,
            FileOptions.SequentialScan | FileOptions.WriteThrough);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true, Encoding.UTF8);
        var manifestEntry = archive.CreateEntry(ManifestEntryName, CompressionLevel.Optimal);
        using (var destination = manifestEntry.Open())
        {
            JsonSerializer.Serialize(destination, manifest, _json);
        }

        var total = screenshotEntries.Count + 1;
        var completed = 0;
        progress?.Invoke(new(DataArchivePhase.WritingArchive, completed, total));
        AddFileToArchive(archive, DatabaseEntryName, databasePath, CompressionLevel.Optimal, cancellationToken);
        progress?.Invoke(new(DataArchivePhase.WritingArchive, ++completed, total));
        foreach (var screenshot in screenshotEntries)
        {
            AddFileToArchive(
                archive,
                screenshot.Manifest.Path,
                screenshot.SourcePath,
                CompressionLevel.NoCompression,
                cancellationToken);
            progress?.Invoke(new(DataArchivePhase.WritingArchive, ++completed, total));
        }

        progress?.Invoke(new(DataArchivePhase.Finalizing));
        archive.Dispose();
        stream.Flush(flushToDisk: true);
    }

    private ArchiveValidation ValidateAndExtractArchiveDatabase(
        string archivePath,
        Action<DataArchiveProgress>? progress, CancellationToken cancellationToken)
    {
        progress?.Invoke(new(DataArchivePhase.VerifyingArchive));
        var fingerprint = ComputeSha256(archivePath, long.MaxValue, cancellationToken);
        var workDirectory = Path.Combine(Path.GetTempPath(), "WorkTrail.Import." + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workDirectory);
        try
        {
            using var archive = ZipFile.OpenRead(archivePath);
            if (archive.Entries.Count is < 2 or > MaximumArchiveEntries)
            {
                throw new InvalidDataException("The archive entry count is invalid.");
            }

            // Windows resolves archive destinations case-insensitively, so reject aliases before extraction.
            var entries = new Dictionary<string, ZipArchiveEntry>(StringComparer.OrdinalIgnoreCase);
            long totalUncompressedBytes = 0;
            foreach (var entry in archive.Entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                ValidateZipEntryName(entry.FullName);
                if (!entries.TryAdd(entry.FullName, entry))
                {
                    throw new InvalidDataException("The archive contains duplicate entries.");
                }

                totalUncompressedBytes = checked(totalUncompressedBytes + entry.Length);
                if (totalUncompressedBytes > MaximumArchiveUncompressedBytes)
                {
                    throw new InvalidDataException("The archive expands beyond the supported size.");
                }
            }

            if (!entries.TryGetValue(ManifestEntryName, out var manifestEntry)
                || !string.Equals(manifestEntry.FullName, ManifestEntryName, StringComparison.Ordinal)
                || manifestEntry.Length > MaximumManifestBytes)
            {
                throw new InvalidDataException("The archive manifest is missing or too large.");
            }

            DataArchiveManifest manifest;
            using (var manifestStream = manifestEntry.Open())
            using (var manifestBuffer = new MemoryStream())
            {
                CopyStream(manifestStream, manifestBuffer, MaximumManifestBytes, cancellationToken);
                if (manifestBuffer.Length != manifestEntry.Length)
                {
                    throw new InvalidDataException("The archive manifest length is invalid.");
                }

                manifestBuffer.Position = 0;
                manifest = JsonSerializer.Deserialize<DataArchiveManifest>(manifestBuffer, _json)
                    ?? throw new InvalidDataException("The archive manifest is invalid.");
            }

            ValidateManifest(manifest, entries.Keys);
            var verified = 0;
            progress?.Invoke(new(DataArchivePhase.VerifyingArchive, verified, manifest.Entries.Count));
            foreach (var expected in manifest.Entries)
            {
                var entry = entries[expected.Path];
                if (!string.Equals(entry.FullName, expected.Path, StringComparison.Ordinal)
                    || entry.Length != expected.Length)
                {
                    throw new InvalidDataException("An archive entry length does not match its manifest.");
                }

                var maximumLength = expected.Path == DatabaseEntryName
                    ? MaximumDatabaseBytes
                    : MaximumScreenshotBytes;
                if (entry.Length > maximumLength)
                {
                    throw new InvalidDataException("An archive entry exceeds its supported size.");
                }

                using var content = entry.Open();
                var hash = ComputeSha256(content, expected.Length, cancellationToken);
                if (!string.Equals(hash, expected.Sha256, StringComparison.Ordinal))
                {
                    throw new InvalidDataException("An archive entry hash does not match its manifest.");
                }
                progress?.Invoke(new(DataArchivePhase.VerifyingArchive, ++verified, manifest.Entries.Count));
            }

            var databasePath = Path.Combine(workDirectory, DatabaseEntryName);
            progress?.Invoke(new(DataArchivePhase.CheckingData));
            var databaseManifest = manifest.Entries.Single(entry => entry.Path == DatabaseEntryName);
            ExtractEntry(entries[DatabaseEntryName], databasePath, databaseManifest.Length, cancellationToken);
            if (new FileInfo(databasePath).Length != databaseManifest.Length)
            {
                throw new InvalidDataException("The extracted archive database length is invalid.");
            }

            // Beta archives are opened on demand without rejecting an informational schema version.
            SqliteActivityStore.EnsureArchiveDatabaseReadable(databasePath);
            // Normalize only the extracted copy; adding a missing metadata table preserves older retained captures.
            SqliteActivityStore.EnsureVipScreenshotSchema(databasePath);
            EnsureDatabaseContainsNoAbsoluteScreenshotPaths(databasePath, cancellationToken);
            var summary = ReadDatabaseSummary(databasePath, cancellationToken);
            ValidateManifestSummary(manifest, summary);
            return new ArchiveValidation(archivePath, workDirectory, databasePath, fingerprint, manifest);
        }
        catch
        {
            DeleteDirectoryIfPresent(workDirectory);
            throw;
        }
    }

    /// <summary>Checks archive integrity and stored data without an exact schema/version preflight.</summary>
    /// <param name="databasePath">The archive database opened read-only.</param>
    /// <param name="cancellationToken">Cancels the retained data checks.</param>
    /// <exception cref="InvalidDataException">Database integrity or stored archive data is invalid.</exception>
    private void ValidateArchiveDatabase(string databasePath, CancellationToken cancellationToken)
    {
        using var connection = OpenDatabase(databasePath, readOnly: true);
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "PRAGMA quick_check;";
            if (!string.Equals(Convert.ToString(command.ExecuteScalar(), CultureInfo.InvariantCulture), "ok", StringComparison.Ordinal))
            {
                throw new InvalidDataException("The archive database integrity check failed.");
            }
        }

        foreach (var profile in ReadInstallationProfiles(connection))
        {
            cancellationToken.ThrowIfCancellationRequested();
            _ = InstallationProfileCatalog.ValidatePersisted(profile);
        }

        EnsureArchiveRelations(connection);
        using (var hardwareCommand = connection.CreateCommand())
        {
            hardwareCommand.CommandText = "SELECT sampled_utc_ticks, snapshot_json FROM capture_hardware_snapshots;";
            using var hardwareReader = hardwareCommand.ExecuteReader();
            while (hardwareReader.Read())
            {
                cancellationToken.ThrowIfCancellationRequested();
                // Imported readings must satisfy the same schema and value contract as local
                // measurements; malformed evidence fails preflight before any destination writes.
                _ = SqliteActivityStore.ReadCaptureHardwareSnapshot(hardwareReader.GetInt64(0), hardwareReader.GetString(1));
            }
        }

        using var captureCommand = connection.CreateCommand();
        captureCommand.CommandText = "SELECT capture_id, installation_id, origin FROM screenshot_captures;";
        using var captureReader = captureCommand.ExecuteReader();
        while (captureReader.Read())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!Guid.TryParseExact(captureReader.GetString(0), "N", out _)
                || !Guid.TryParseExact(captureReader.GetString(1), "N", out _))
            {
                throw new InvalidDataException("The archive contains invalid screenshot provenance identifiers.");
            }

            _ = ScreenshotCaptureOrigins.Validate(captureReader.GetString(2));
        }
    }

    private static void ValidateManifest(DataArchiveManifest manifest, IEnumerable<string> actualEntryPaths)
    {
        if (manifest.SchemaVersion != ArchiveSchemaVersion
            || manifest.ArchiveId == Guid.Empty
            || manifest.CreatedAt.Offset != TimeSpan.Zero
            || manifest.ActivitySampleCount < 0
            || manifest.AiRequestCount < 0
            || manifest.AiAnalysisCount < 0
            || manifest.ScreenshotFileCount < 0
            || manifest.ScreenshotBytes < 0
            || manifest.Installations is null
            || manifest.Entries is null
            || manifest.Entries.Count is < 1 or >= MaximumArchiveEntries)
        {
            throw new InvalidDataException("The archive manifest contract is invalid.");
        }

        _ = ResolveUtcRange(manifest.From, manifest.ToInclusive);
        var expectedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in manifest.Entries)
        {
            if (entry is null)
            {
                throw new InvalidDataException("The archive entry manifest is invalid.");
            }

            ValidateZipEntryName(entry.Path);
            var isDatabase = string.Equals(entry.Path, DatabaseEntryName, StringComparison.Ordinal);
            var isScreenshot = entry.Path.StartsWith(ScreenshotEntryPrefix, StringComparison.Ordinal);
            if (isScreenshot)
            {
                ValidateArchiveScreenshotPath(entry.Path);
            }

            if (entry.Path == ManifestEntryName
                || (!isDatabase && !isScreenshot)
                || entry.Length < 0
                || string.IsNullOrEmpty(entry.Sha256)
                || entry.Sha256.Length != 64
                || !entry.Sha256.All(Uri.IsHexDigit)
                || !string.Equals(entry.Sha256, entry.Sha256.ToLowerInvariant(), StringComparison.Ordinal)
                || !expectedPaths.Add(entry.Path))
            {
                throw new InvalidDataException("The archive entry manifest is invalid.");
            }
        }

        if (!expectedPaths.Contains(DatabaseEntryName))
        {
            throw new InvalidDataException("The archive database entry is missing.");
        }

        var screenshotEntries = manifest.Entries.Where(entry => entry.Path.StartsWith(ScreenshotEntryPrefix, StringComparison.Ordinal)).ToArray();
        if (screenshotEntries.Length != manifest.ScreenshotFileCount
            || screenshotEntries.Sum(entry => entry.Length) != manifest.ScreenshotBytes
            || (!manifest.IncludesScreenshots && screenshotEntries.Length != 0))
        {
            throw new InvalidDataException("The archive screenshot summary is inconsistent.");
        }

        var actual = actualEntryPaths.ToHashSet(StringComparer.OrdinalIgnoreCase);
        expectedPaths.Add(ManifestEntryName);
        if (!actual.SetEquals(expectedPaths))
        {
            throw new InvalidDataException("The archive contains undeclared or missing entries.");
        }

        var installationIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var installation in manifest.Installations)
        {
            if (installation is null
                || !Guid.TryParseExact(installation.InstallationId, "N", out var parsed)
                || !string.Equals(parsed.ToString("N"), installation.InstallationId, StringComparison.Ordinal)
                || string.IsNullOrWhiteSpace(installation.MachineName)
                || string.IsNullOrWhiteSpace(installation.FriendlyName)
                || !InstallationProfileCatalog.Colors.Contains(installation.Color, StringComparer.Ordinal)
                || !InstallationProfileCatalog.Icons.Contains(installation.Icon, StringComparer.Ordinal)
                || !installationIds.Add(installation.InstallationId))
            {
                throw new InvalidDataException("The archive installation summary is invalid.");
            }
        }
    }

    private static void ValidateManifestSummary(DataArchiveManifest manifest, DatabaseSummary summary)
    {
        if (manifest.ActivitySampleCount != summary.ActivitySampleCount
            || manifest.AiRequestCount != summary.AiRequestCount
            || manifest.AiAnalysisCount != summary.AiAnalysisCount
            || !manifest.Installations.SequenceEqual(summary.Installations))
        {
            throw new InvalidDataException("The archive manifest does not match its SQLite payload.");
        }
    }

    private static void EnsureArchiveRelations(SqliteConnection connection)
    {
        var checks = new[]
        {
            "SELECT 1 FROM screenshot_vip_metadata AS vip LEFT JOIN screenshot_captures AS capture ON capture.capture_id = vip.capture_id WHERE capture.capture_id IS NULL OR capture.origin <> 'manual' OR length(vip.note) > 4000 OR vip.updated_utc_ticks < 0 OR vip.updated_utc_ticks > 3155378975999999999 LIMIT 1;",
            "SELECT 1 FROM activity_samples AS row LEFT JOIN installation_profiles AS profile ON profile.installation_id = row.installation_id WHERE profile.installation_id IS NULL LIMIT 1;",
            "SELECT 1 FROM ai_analysis_results AS row LEFT JOIN installation_profiles AS profile ON profile.installation_id = row.installation_id WHERE profile.installation_id IS NULL LIMIT 1;",
            "SELECT 1 FROM screenshot_captures AS row LEFT JOIN installation_profiles AS profile ON profile.installation_id = row.installation_id WHERE profile.installation_id IS NULL LIMIT 1;",
            "SELECT 1 FROM ai_analysis_results AS row LEFT JOIN ai_request_usage AS request ON request.attempt_id = row.attempt_id WHERE request.attempt_id IS NULL LIMIT 1;",
            "SELECT 1 FROM screenshot_text_snapshots AS row LEFT JOIN screenshot_captures AS capture ON capture.capture_id = row.capture_id WHERE capture.capture_id IS NULL LIMIT 1;",
            "SELECT 1 FROM screenshot_interval_telemetry AS row LEFT JOIN screenshot_captures AS capture ON capture.capture_id = row.capture_id WHERE capture.capture_id IS NULL LIMIT 1;",
            "SELECT 1 FROM capture_hardware_snapshots AS row LEFT JOIN screenshot_captures AS capture ON capture.capture_id = row.capture_id WHERE capture.capture_id IS NULL LIMIT 1;",
            "SELECT 1 FROM ai_analysis_artifacts AS row LEFT JOIN ai_analysis_results AS analysis ON analysis.correlation_id = row.correlation_id LEFT JOIN screenshot_captures AS capture ON capture.capture_id = row.capture_id WHERE analysis.correlation_id IS NULL OR capture.capture_id IS NULL LIMIT 1;"
        };
        foreach (var sql in checks)
        {
            using var command = connection.CreateCommand();
            command.CommandText = sql;
            if (command.ExecuteScalar() is not null)
            {
                throw new InvalidDataException("The archive database contains broken relationships.");
            }
        }
    }

    private void PreflightDatabaseMerge(
        string incomingDatabasePath,
        DataArchiveManifest manifest,
        string archiveFingerprint,
        CancellationToken cancellationToken)
    {
        using var connection = OpenDatabase(_store.ActivityDatabasePath, readOnly: false);
        AttachIncomingDatabase(connection, incomingDatabasePath);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            EnsureLedgerCompatible(connection, manifest.ArchiveId, archiveFingerprint);
            EnsureProfileMergeIsSafe(connection);
            EnsureNoPayloadConflict(connection, "activity_samples", ["sample_id"], ActivityColumns.Except(["sample_id"]).ToArray());
            EnsureNoPayloadConflict(connection, "ai_request_usage", ["attempt_id"], AiRequestColumns.Except(["attempt_id"]).ToArray());
            EnsureNoPayloadConflict(connection, "ai_analysis_results", ["correlation_id"], AiAnalysisColumns.Except(["correlation_id"]).ToArray());
            EnsureNoAlternateUniqueConflict(connection, "ai_analysis_results", "attempt_id", "correlation_id");
            EnsureNoPayloadConflict(connection, "screenshot_captures", ["capture_id"], ScreenshotCaptureColumns.Except(["capture_id"]).ToArray());
            EnsureNoVersionedPayloadConflict(connection, "screenshot_vip_metadata", "capture_id", "updated_utc_ticks", VipScreenshotColumns);
            EnsureNoPayloadConflict(connection, "ai_analysis_artifacts", ["artifact_identity"], AiArtifactColumns.Except(["artifact_identity"]).ToArray());
            EnsureNoVersionedPayloadConflict(connection, "screenshot_text_snapshots", "artifact_identity", "updated_utc_ticks", ScreenshotSnapshotColumns, "snapshot_json");
            EnsureNoVersionedPayloadConflict(connection, "screenshot_interval_telemetry", "artifact_identity", "updated_utc_ticks", ScreenshotTelemetryColumns);
            EnsureNoPayloadConflict(connection, "capture_hardware_snapshots", ["capture_id"], CaptureHardwareSnapshotColumns.Except(["capture_id"]).ToArray());
        }
        finally
        {
            DetachIncomingDatabase(connection);
        }
    }

    private IReadOnlyList<ScreenshotImportEntry> PreflightScreenshotMerge(
        string archivePath,
        DataArchiveManifest manifest,
        CancellationToken cancellationToken)
    {
        var screenshotRoot = CurrentScreenshotRoot();
        using var archive = ZipFile.OpenRead(archivePath);
        var entries = archive.Entries.ToDictionary(entry => entry.FullName, StringComparer.OrdinalIgnoreCase);
        var plan = new List<ScreenshotImportEntry>();
        foreach (var expected in manifest.Entries.Where(entry => entry.Path.StartsWith(ScreenshotEntryPrefix, StringComparison.Ordinal)))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var destination = ToLocalScreenshotPath(screenshotRoot, expected.Path);
            var exists = File.Exists(destination);
            if (exists)
            {
                if (new FileInfo(destination).Length != expected.Length)
                {
                    throw new InvalidDataException("A local screenshot conflicts with an archive entry.");
                }

                var existingHash = ComputeSha256(destination, expected.Length, cancellationToken);
                if (!string.Equals(existingHash, expected.Sha256, StringComparison.Ordinal))
                {
                    throw new InvalidDataException("A local screenshot conflicts with an archive entry.");
                }
            }

            if (!entries.ContainsKey(expected.Path))
            {
                throw new InvalidDataException("An archive screenshot entry is missing.");
            }

            plan.Add(new ScreenshotImportEntry(expected.Path, destination, expected.Length, expected.Sha256, exists));
        }

        return plan;
    }

    private static IReadOnlyDictionary<string, string> StageScreenshotFiles(
        string archivePath,
        IReadOnlyList<ScreenshotImportEntry> plan,
        Guid planId,
        Action<DataArchiveProgress>? progress, CancellationToken cancellationToken)
    {
        var staged = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        using var archive = ZipFile.OpenRead(archivePath);
        var entries = archive.Entries.ToDictionary(entry => entry.FullName, StringComparer.OrdinalIgnoreCase);
        try
        {
            var total = plan.Count(item => !item.AlreadyExists);
            progress?.Invoke(new(DataArchivePhase.CopyingScreenshots, 0, total));
            foreach (var item in plan.Where(item => !item.AlreadyExists))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var parent = Path.GetDirectoryName(item.DestinationPath)
                    ?? throw new InvalidDataException("An imported screenshot destination has no parent directory.");
                Directory.CreateDirectory(parent);
                var stagingPath = item.DestinationPath + "." + planId.ToString("N") + ".importing";
                if (File.Exists(stagingPath))
                {
                    throw new IOException("An imported screenshot staging path already exists.");
                }

                ExtractEntry(entries[item.ArchivePath], stagingPath, item.Length, cancellationToken);
                if (new FileInfo(stagingPath).Length != item.Length)
                {
                    throw new InvalidDataException("A staged screenshot length does not match its archive manifest.");
                }

                var hash = ComputeSha256(stagingPath, item.Length, cancellationToken);
                if (!string.Equals(hash, item.Sha256, StringComparison.Ordinal))
                {
                    throw new InvalidDataException("A staged screenshot does not match its archive hash.");
                }

                staged.Add(item.DestinationPath, stagingPath);
                progress?.Invoke(new(DataArchivePhase.CopyingScreenshots, staged.Count, total));
            }

            return staged;
        }
        catch
        {
            foreach (var path in staged.Values)
            {
                DeleteFileIfPresent(path);
            }

            throw;
        }
    }

    private DataArchiveImportResult MergeDatabaseAndFiles(
        string incomingDatabasePath,
        DataArchiveManifest manifest,
        string fingerprint,
        IReadOnlyList<ScreenshotImportEntry> screenshotPlan,
        IReadOnlyDictionary<string, string> staging,
        Action<DataArchiveProgress>? progress, CancellationToken cancellationToken)
    {
        progress?.Invoke(new(DataArchivePhase.MergingData));
        var newScreenshots = screenshotPlan.Where(item => !item.AlreadyExists).ToArray();
        if (newScreenshots.Length > 0)
        {
            WriteImportJournal(new ImportJournal(
                manifest.ArchiveId,
                fingerprint,
                newScreenshots.Select(item => new ImportJournalFile(item.DestinationPath, item.Sha256)).ToArray()));
        }

        var mergeCommitted = false;
        try
        {
            using var connection = OpenDatabase(_store.ActivityDatabasePath, readOnly: false);
            AttachIncomingDatabase(connection, incomingDatabasePath);
            try
            {
                using var transaction = connection.BeginTransaction();
                EnsureLedgerCompatible(connection, manifest.ArchiveId, fingerprint, transaction);
                var addedInstallations = MergeInstallationProfiles(connection, transaction);
                var activity = MergeImmutableTable(
                    connection, transaction, "activity_samples", ["sample_id"], ActivityColumns, cancellationToken);
                var aiRequests = MergeImmutableTable(
                    connection, transaction, "ai_request_usage", ["attempt_id"], AiRequestColumns, cancellationToken);
                var aiAnalyses = MergeImmutableTable(
                    connection, transaction, "ai_analysis_results", ["correlation_id"], AiAnalysisColumns, cancellationToken);
                _ = MergeImmutableTable(
                    connection, transaction, "screenshot_captures", ["capture_id"], ScreenshotCaptureColumns, cancellationToken);
                MergeVersionedTable(
                    connection, transaction, "screenshot_vip_metadata", "capture_id", "updated_utc_ticks", VipScreenshotColumns);
                _ = MergeImmutableTable(
                    connection, transaction, "ai_analysis_artifacts", ["artifact_identity"], AiArtifactColumns, cancellationToken);
                MergeVersionedTable(
                    connection, transaction, "screenshot_text_snapshots", "artifact_identity", "updated_utc_ticks", ScreenshotSnapshotColumns);
                MergeVersionedTable(
                    connection, transaction, "screenshot_interval_telemetry", "artifact_identity", "updated_utc_ticks", ScreenshotTelemetryColumns);
                _ = MergeImmutableTable(
                    connection, transaction, "capture_hardware_snapshots", ["capture_id"], CaptureHardwareSnapshotColumns, cancellationToken);
                RebuildSqliteAiSearch(connection, transaction);

                var published = 0;
                progress?.Invoke(new(DataArchivePhase.PublishingScreenshots, published, newScreenshots.Length));
                foreach (var item in newScreenshots)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (File.Exists(item.DestinationPath))
                    {
                        throw new IOException("A screenshot destination changed after import preflight.");
                    }

                    File.Move(staging[item.DestinationPath], item.DestinationPath);
                    progress?.Invoke(new(DataArchivePhase.PublishingScreenshots, ++published, newScreenshots.Length));
                }

                ExecuteNonQuery(connection, transaction, """
                    INSERT INTO archive_imports (archive_id, archive_fingerprint, imported_utc_ticks)
                    VALUES ($archiveId, $fingerprint, $importedAt)
                    ON CONFLICT(archive_id) DO NOTHING;
                    """,
                    ("$archiveId", manifest.ArchiveId.ToString("N")),
                    ("$fingerprint", fingerprint),
                    ("$importedAt", DateTimeOffset.UtcNow.UtcDateTime.Ticks));
                progress?.Invoke(new(DataArchivePhase.Finalizing));
                // Data, import ledger and the derived-index invalidation become durable together.
                // Marker failures roll back the import; after COMMIT only in-memory invalidation remains.
                _ = SqliteActivityStore.MarkSearchSourceRebuild(connection, transaction, "archive-import");
                transaction.Commit();
                mergeCommitted = true;
                _store.NotifyHistoryImportCommitted();
                return new DataArchiveImportResult(
                    manifest.ArchiveId,
                    addedInstallations,
                    activity.Added,
                    activity.Skipped,
                    aiRequests.Added,
                    aiRequests.Skipped,
                    aiAnalyses.Added,
                    aiAnalyses.Skipped,
                    newScreenshots.Length,
                    screenshotPlan.Count - newScreenshots.Length,
                    newScreenshots.Sum(item => item.Length));
            }
            finally
            {
                if (!mergeCommitted)
                {
                    DetachIncomingDatabase(connection);
                }
            }
        }
        catch
        {
            if (File.Exists(_journalPath))
            {
                // The durable ledger decides whether an ambiguous COMMIT keeps or removes materialized files.
                RecoverInterruptedImport();
            }

            throw;
        }
    }

    private static int MergeInstallationProfiles(SqliteConnection connection, SqliteTransaction transaction)
    {
        var incoming = ReadInstallationProfiles(connection, "incoming", transaction);
        var existing = ReadInstallationProfiles(connection, "main", transaction)
            .ToDictionary(profile => profile.InstallationId, StringComparer.Ordinal);
        var added = 0;
        foreach (var profile in incoming)
        {
            var validated = InstallationProfileCatalog.ValidatePersisted(profile);
            if (!existing.TryGetValue(validated.InstallationId, out var current))
            {
                using var insert = connection.CreateCommand();
                insert.Transaction = transaction;
                insert.CommandText = """
                    INSERT INTO installation_profiles (
                        installation_id, machine_name, friendly_name, color, icon,
                        first_seen_utc_ticks, updated_utc_ticks, profile_revision)
                    VALUES ($id, $machine, $friendly, $color, $icon, $firstSeen, $updated, $revision);
                    """;
                AddProfileParameters(insert, validated);
                insert.ExecuteNonQuery();
                existing.Add(validated.InstallationId, validated);
                added++;
                continue;
            }

            EnsureProfilePairIsCompatible(current, validated);
            if (validated.Revision <= current.Revision)
            {
                continue;
            }

            using var update = connection.CreateCommand();
            update.Transaction = transaction;
            update.CommandText = """
                UPDATE installation_profiles
                SET machine_name = $machine,
                    friendly_name = $friendly,
                    color = $color,
                    icon = $icon,
                    first_seen_utc_ticks = $firstSeen,
                    updated_utc_ticks = $updated,
                    profile_revision = $revision
                WHERE installation_id = $id AND profile_revision = $previousRevision;
                """;
            AddProfileParameters(update, validated);
            update.Parameters.AddWithValue("$previousRevision", current.Revision);
            if (update.ExecuteNonQuery() != 1)
            {
                throw new InvalidOperationException("An installation profile changed during archive merge.");
            }

            existing[validated.InstallationId] = validated;
        }

        return added;
    }

    private static MergeCount MergeImmutableTable(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string table,
        IReadOnlyList<string> keys,
        IReadOnlyList<string> columns,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var incomingCount = ReadCount(connection, "incoming", table, transaction);
        var join = BuildKeyJoin("target", "source", keys);
        using var existingCommand = connection.CreateCommand();
        existingCommand.Transaction = transaction;
        existingCommand.CommandText = $"SELECT COUNT(*) FROM incoming.{Quote(table)} AS source JOIN main.{Quote(table)} AS target ON {join};";
        var skipped = checked(Convert.ToInt32(existingCommand.ExecuteScalar(), CultureInfo.InvariantCulture));
        var columnSql = string.Join(", ", columns.Select(Quote));
        using var insert = connection.CreateCommand();
        insert.Transaction = transaction;
        insert.CommandText = $"""
            INSERT INTO main.{Quote(table)} ({columnSql})
            SELECT {string.Join(", ", columns.Select(column => "source." + Quote(column)))}
            FROM incoming.{Quote(table)} AS source
            WHERE NOT EXISTS (
                SELECT 1 FROM main.{Quote(table)} AS target WHERE {join});
            """;
        var added = insert.ExecuteNonQuery();
        if (added != incomingCount - skipped)
        {
            throw new InvalidOperationException("An archive table changed during merge.");
        }

        return new MergeCount(added, skipped);
    }

    private static void MergeVersionedTable(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string table,
        string key,
        string revision,
        IReadOnlyList<string> columns)
    {
        var columnSql = string.Join(", ", columns.Select(Quote));
        var updateColumns = columns
            .Where(column => !string.Equals(column, key, StringComparison.Ordinal))
            .Select(column => $"{Quote(column)} = excluded.{Quote(column)}");
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"""
            INSERT INTO main.{Quote(table)} ({columnSql})
            SELECT {columnSql} FROM incoming.{Quote(table)} WHERE 1
            ON CONFLICT({Quote(key)}) DO UPDATE SET {string.Join(", ", updateColumns)}
            WHERE excluded.{Quote(revision)} > main.{Quote(table)}.{Quote(revision)};
            """;
        command.ExecuteNonQuery();
    }

    private static void RebuildSqliteAiSearch(SqliteConnection connection, SqliteTransaction transaction)
    {
        ExecuteNonQuery(connection, transaction, "DELETE FROM ai_analysis_search;");
        ExecuteNonQuery(connection, transaction,
            "DELETE FROM screenshot_vip_metadata WHERE capture_id NOT IN (SELECT capture_id FROM screenshot_captures);");
        ExecuteNonQuery(connection, transaction, """
            INSERT INTO ai_analysis_search (correlation_id, application, context, summary)
            SELECT correlation_id, application, context, summary
            FROM ai_analysis_results
            ORDER BY timestamp_utc_ticks, correlation_id;
            """);
    }

    private static void EnsureProfileMergeIsSafe(SqliteConnection connection)
    {
        var incoming = ReadInstallationProfiles(connection, "incoming", null);
        var existing = ReadInstallationProfiles(connection, "main", null)
            .ToDictionary(profile => profile.InstallationId, StringComparer.Ordinal);
        foreach (var profile in incoming)
        {
            var validated = InstallationProfileCatalog.ValidatePersisted(profile);
            if (existing.TryGetValue(validated.InstallationId, out var current))
            {
                EnsureProfilePairIsCompatible(current, validated);
            }
        }
    }

    private static void EnsureProfilePairIsCompatible(InstallationProfile current, InstallationProfile incoming)
    {
        if (current.FirstSeenAt != incoming.FirstSeenAt)
        {
            throw new InvalidDataException("An installation profile has a conflicting first-seen timestamp.");
        }

        if (current.Revision == incoming.Revision && !ProfilesEqual(current, incoming))
        {
            throw new InvalidDataException("An installation profile has the same revision but different content.");
        }
    }

    private static bool ProfilesEqual(InstallationProfile left, InstallationProfile right) =>
        string.Equals(left.InstallationId, right.InstallationId, StringComparison.Ordinal)
        && string.Equals(left.MachineName, right.MachineName, StringComparison.Ordinal)
        && string.Equals(left.FriendlyName, right.FriendlyName, StringComparison.Ordinal)
        && string.Equals(left.Color, right.Color, StringComparison.Ordinal)
        && string.Equals(left.Icon, right.Icon, StringComparison.Ordinal)
        && left.FirstSeenAt == right.FirstSeenAt
        && left.UpdatedAt == right.UpdatedAt
        && left.Revision == right.Revision;

    private static void EnsureNoPayloadConflict(
        SqliteConnection connection,
        string table,
        IReadOnlyList<string> keys,
        IReadOnlyList<string> payloadColumns)
    {
        var join = BuildKeyJoin("target", "source", keys);
        var differences = string.Join(" OR ", payloadColumns.Select(column =>
            $"NOT (target.{Quote(column)} IS source.{Quote(column)})"));
        using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT 1
            FROM incoming.{Quote(table)} AS source
            JOIN main.{Quote(table)} AS target ON {join}
            WHERE {differences}
            LIMIT 1;
            """;
        if (command.ExecuteScalar() is not null)
        {
            throw new InvalidDataException($"The archive contains a conflicting {table} identity.");
        }
    }

    private static void EnsureNoAlternateUniqueConflict(
        SqliteConnection connection,
        string table,
        string alternateKey,
        string primaryKey)
    {
        using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT 1
            FROM incoming.{Quote(table)} AS source
            JOIN main.{Quote(table)} AS target
              ON target.{Quote(alternateKey)} = source.{Quote(alternateKey)}
            WHERE NOT (target.{Quote(primaryKey)} IS source.{Quote(primaryKey)})
            LIMIT 1;
            """;
        if (command.ExecuteScalar() is not null)
        {
            throw new InvalidDataException($"The archive contains a conflicting {table} alternate identity.");
        }
    }

    private static void EnsureNoVersionedPayloadConflict(
        SqliteConnection connection,
        string table,
        string key,
        string revision,
        IReadOnlyList<string> columns,
        string? jsonColumn = null)
    {
        var payload = columns.Where(column =>
            !string.Equals(column, key, StringComparison.Ordinal)
            && !string.Equals(column, revision, StringComparison.Ordinal));
        var differences = string.Join(" OR ", payload.Select(column =>
            $"NOT (target.{Quote(column)} IS source.{Quote(column)})"));
        using var command = connection.CreateCommand();
        if (jsonColumn is not null)
        {
            var scalarDifferences = string.Join(" OR ", payload.Where(column => column != jsonColumn).Select(column =>
                $"NOT (target.{Quote(column)} IS source.{Quote(column)})"));
            command.CommandText = $"""
                SELECT ({scalarDifferences}), target.{Quote(jsonColumn)}, source.{Quote(jsonColumn)}
                FROM incoming.{Quote(table)} AS source
                JOIN main.{Quote(table)} AS target ON target.{Quote(key)} = source.{Quote(key)}
                WHERE target.{Quote(revision)} = source.{Quote(revision)} AND ({differences});
                """;
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                // Path remapping reserializes snapshots. Ignore formatting and object-property order only;
                // changed values or non-JSON columns remain conflicts, and malformed JSON fails explicitly.
                if (reader.GetBoolean(0) || !JsonNode.DeepEquals(JsonNode.Parse(reader.GetString(1)), JsonNode.Parse(reader.GetString(2))))
                    throw new InvalidDataException($"The archive contains a conflicting {table} revision.");
            }
            return;
        }
        command.CommandText = $"""
            SELECT 1
            FROM incoming.{Quote(table)} AS source
            JOIN main.{Quote(table)} AS target ON target.{Quote(key)} = source.{Quote(key)}
            WHERE target.{Quote(revision)} = source.{Quote(revision)}
              AND ({differences})
            LIMIT 1;
            """;
        if (command.ExecuteScalar() is not null)
        {
            throw new InvalidDataException($"The archive contains a conflicting {table} revision.");
        }
    }

    private static void EnsureLedgerCompatible(
        SqliteConnection connection,
        Guid archiveId,
        string fingerprint,
        SqliteTransaction? transaction = null)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT archive_fingerprint
            FROM archive_imports
            WHERE archive_id = $archiveId;
            """;
        command.Parameters.AddWithValue("$archiveId", archiveId.ToString("N"));
        if (command.ExecuteScalar() is string existingFingerprint
            && !string.Equals(existingFingerprint, fingerprint, StringComparison.Ordinal))
        {
            throw new InvalidDataException("The archive identity was already imported with different content.");
        }

        using var fingerprintCommand = connection.CreateCommand();
        fingerprintCommand.Transaction = transaction;
        fingerprintCommand.CommandText = """
            SELECT archive_id
            FROM archive_imports
            WHERE archive_fingerprint = $fingerprint;
            """;
        fingerprintCommand.Parameters.AddWithValue("$fingerprint", fingerprint);
        if (fingerprintCommand.ExecuteScalar() is string existingArchiveId
            && !string.Equals(existingArchiveId, archiveId.ToString("N"), StringComparison.Ordinal))
        {
            throw new InvalidDataException("The archive content was already imported with a different identity.");
        }
    }

    private bool IsArchiveImported(Guid archiveId, string fingerprint)
    {
        using var connection = OpenDatabase(_store.ActivityDatabasePath, readOnly: true);
        EnsureLedgerCompatible(connection, archiveId, fingerprint);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT 1 FROM archive_imports WHERE archive_id = $archiveId AND archive_fingerprint = $fingerprint;";
        command.Parameters.AddWithValue("$archiveId", archiveId.ToString("N"));
        command.Parameters.AddWithValue("$fingerprint", fingerprint);
        return command.ExecuteScalar() is not null;
    }

    private void RecoverInterruptedImport()
    {
        if (!File.Exists(_journalPath))
        {
            return;
        }

        var journal = JsonSerializer.Deserialize<ImportJournal>(File.ReadAllText(_journalPath), _json)
            ?? throw new InvalidDataException("The archive import recovery journal is invalid.");
        var imported = IsArchiveImported(journal.ArchiveId, journal.Fingerprint);
        if (!imported)
        {
            var screenshotRoot = CurrentScreenshotRoot();
            foreach (var file in journal.Files)
            {
                var fullPath = Path.GetFullPath(file.Path);
                ValidateLocalScreenshotPath(screenshotRoot, fullPath);
                if (!File.Exists(fullPath))
                {
                    continue;
                }

                var hash = ComputeSha256(fullPath, MaximumScreenshotBytes, CancellationToken.None);
                if (!string.Equals(hash, file.Sha256, StringComparison.Ordinal))
                {
                    throw new InvalidDataException("An interrupted import left a screenshot with unexpected content.");
                }

                File.Delete(fullPath);
            }
        }

        File.Delete(_journalPath);
    }

    private void WriteImportJournal(ImportJournal journal)
    {
        var temporaryPath = _journalPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                64 * 1024,
                FileOptions.WriteThrough))
            {
                JsonSerializer.Serialize(stream, journal, _json);
                stream.Flush(flushToDisk: true);
            }

            File.Move(temporaryPath, _journalPath, overwrite: true);
        }
        finally
        {
            DeleteFileIfPresent(temporaryPath);
        }
    }

    private static string ValidateArchivePath(string path, bool mustExist)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("An archive path is required.", nameof(path));
        }

        var fullPath = Path.GetFullPath(path);
        if (!string.Equals(Path.GetExtension(fullPath), ArchiveExtension, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"A WorkTrail archive must use the {ArchiveExtension} extension.");
        }

        if (mustExist && !File.Exists(fullPath))
        {
            throw new FileNotFoundException("The WorkTrail archive was not found.", fullPath);
        }

        if (Directory.Exists(fullPath))
        {
            throw new InvalidDataException("The archive path refers to a directory.");
        }

        return fullPath;
    }

    private static (long? FromUtcTicks, long? ToUtcTicks) ResolveUtcRange(
        DateOnly? from,
        DateOnly? toInclusive)
    {
        if (from.HasValue != toInclusive.HasValue)
        {
            throw new ArgumentException("Both archive range dates must be supplied together.");
        }

        if (!from.HasValue)
        {
            return (null, null);
        }

        if (from.Value > toInclusive!.Value || toInclusive.Value == DateOnly.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(toInclusive), "The archive date range is invalid.");
        }

        var fromLocal = DateTime.SpecifyKind(from.Value.ToDateTime(TimeOnly.MinValue), DateTimeKind.Unspecified);
        var toLocal = DateTime.SpecifyKind(toInclusive.Value.AddDays(1).ToDateTime(TimeOnly.MinValue), DateTimeKind.Unspecified);
        var fromUtc = TimeZoneInfo.ConvertTimeToUtc(fromLocal, TimeZoneInfo.Local);
        var toUtc = TimeZoneInfo.ConvertTimeToUtc(toLocal, TimeZoneInfo.Local);
        return (fromUtc.Ticks, toUtc.Ticks);
    }

    private string CurrentScreenshotRoot()
    {
        var settings = _store.LoadSettings();
        return ScreenshotStorageLayout.NormalizeRoot(settings.ScreenshotDirectory);
    }

    private static string ToArchiveScreenshotPath(string screenshotRoot, string localPath)
    {
        var fullPath = Path.GetFullPath(localPath);
        ValidateLocalScreenshotPath(screenshotRoot, fullPath);
        var relative = Path.GetRelativePath(screenshotRoot, fullPath).Replace('\\', '/');
        var archivePath = ScreenshotEntryPrefix + relative;
        ValidateArchiveScreenshotPath(archivePath);
        return archivePath;
    }

    private static string ToLocalScreenshotPath(string screenshotRoot, string archivePath)
    {
        ValidateArchiveScreenshotPath(archivePath);
        var relative = archivePath[ScreenshotEntryPrefix.Length..].Replace('/', Path.DirectorySeparatorChar);
        var fullPath = Path.GetFullPath(Path.Combine(screenshotRoot, relative));
        ValidateLocalScreenshotPath(screenshotRoot, fullPath);
        return fullPath;
    }

    private static void ValidateArchiveScreenshotPath(string path)
    {
        ValidateZipEntryName(path);
        var segments = path.Split('/');
        if (segments.Length != 5
            || !string.Equals(segments[0], "screenshots", StringComparison.Ordinal)
            || !DateOnly.TryParseExact(segments[3], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day)
            || !string.Equals(segments[1], day.ToString("yyyy-MM", CultureInfo.InvariantCulture), StringComparison.Ordinal)
            || !string.Equals(
                segments[2],
                $"week-{ISOWeek.GetYear(day.ToDateTime(TimeOnly.MinValue)):0000}-{ISOWeek.GetWeekOfYear(day.ToDateTime(TimeOnly.MinValue)):00}",
                StringComparison.Ordinal)
            || !ScreenCaptureService.IsOwnedArtifact(segments[4]))
        {
            throw new InvalidDataException("An archive screenshot path is not canonical.");
        }
    }

    private static void ValidateLocalScreenshotPath(string screenshotRoot, string fullPath)
    {
        var normalizedRoot = ScreenshotStorageLayout.NormalizeRoot(screenshotRoot);
        var normalizedPath = Path.GetFullPath(fullPath);
        var rootPrefix = normalizedRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        if (!normalizedPath.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase)
            || !ScreenCaptureService.IsOwnedArtifact(normalizedPath)
            || !ScreenshotStorageLayout.TryGetDay(normalizedRoot, normalizedPath, out _))
        {
            throw new InvalidDataException("A screenshot path escapes the canonical local screenshot root.");
        }
    }

    private static void ValidateZipEntryName(string path)
    {
        if (string.IsNullOrWhiteSpace(path)
            || path.Contains('\\', StringComparison.Ordinal)
            || path.StartsWith("/", StringComparison.Ordinal)
            || Path.IsPathFullyQualified(path))
        {
            throw new InvalidDataException("An archive entry path is invalid.");
        }

        var segments = path.Split('/');
        if (segments.Any(segment => segment.Length == 0 || segment is "." or ".." || segment.Contains(':', StringComparison.Ordinal)))
        {
            throw new InvalidDataException("An archive entry path is invalid.");
        }
    }

    private static string? TryGetCaptureId(string artifactIdentity)
    {
        var separator = artifactIdentity.IndexOf('_', StringComparison.Ordinal);
        if (separator <= 0)
        {
            return null;
        }

        var value = artifactIdentity[..separator];
        return Guid.TryParseExact(value, "N", out var parsed) ? parsed.ToString("N") : null;
    }

    private static ArchiveEntryManifest BuildEntryManifest(
        string archivePath,
        string sourcePath,
        long maximumLength,
        CancellationToken cancellationToken)
    {
        ValidateZipEntryName(archivePath);
        var info = new FileInfo(sourcePath);
        if (!info.Exists || info.Length > maximumLength)
        {
            throw new InvalidDataException("An archive source entry is missing or too large.");
        }

        return new ArchiveEntryManifest(
            archivePath,
            info.Length,
            ComputeSha256(sourcePath, maximumLength, cancellationToken));
    }

    private static string ComputeSha256(
        string path,
        long maximumLength,
        CancellationToken cancellationToken)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024, FileOptions.SequentialScan);
        if (stream.Length > maximumLength)
        {
            throw new InvalidDataException("A file exceeds its supported size.");
        }

        return ComputeSha256(stream, maximumLength, cancellationToken);
    }

    private static string ComputeSha256(
        Stream stream,
        long maximumLength,
        CancellationToken cancellationToken)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[1024 * 1024];
        long total = 0;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var read = stream.Read(buffer, 0, buffer.Length);
            if (read == 0)
            {
                break;
            }

            total = checked(total + read);
            if (total > maximumLength)
            {
                throw new InvalidDataException("An archive entry expands beyond its supported size.");
            }

            hash.AppendData(buffer, 0, read);
        }

        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    private static void AddFileToArchive(
        ZipArchive archive,
        string archivePath,
        string sourcePath,
        CompressionLevel compressionLevel,
        CancellationToken cancellationToken)
    {
        var entry = archive.CreateEntry(archivePath, compressionLevel);
        using var source = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024, FileOptions.SequentialScan);
        using var destination = entry.Open();
        CopyStream(source, destination, long.MaxValue, cancellationToken);
    }

    private static void ExtractEntry(
        ZipArchiveEntry entry,
        string destinationPath,
        long maximumLength,
        CancellationToken cancellationToken)
    {
        if (entry.Length > maximumLength)
        {
            throw new InvalidDataException("An archive entry exceeds its supported size.");
        }

        using var source = entry.Open();
        using var destination = new FileStream(
            destinationPath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            1024 * 1024,
            FileOptions.SequentialScan | FileOptions.WriteThrough);
        CopyStream(source, destination, maximumLength, cancellationToken);
        destination.Flush(flushToDisk: true);
    }

    private static void CopyStream(
        Stream source,
        Stream destination,
        long maximumLength,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[1024 * 1024];
        long total = 0;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var read = source.Read(buffer, 0, buffer.Length);
            if (read == 0)
            {
                break;
            }

            total = checked(total + read);
            if (total > maximumLength)
            {
                throw new InvalidDataException("An archive entry expands beyond its supported size.");
            }

            destination.Write(buffer, 0, read);
        }
    }

    private static SqliteConnection OpenDatabase(string databasePath, bool readOnly)
    {
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = Path.GetFullPath(databasePath),
            Mode = readOnly ? SqliteOpenMode.ReadOnly : SqliteOpenMode.ReadWrite,
            Pooling = false,
            DefaultTimeout = 30
        };
        var connection = new SqliteConnection(builder.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA busy_timeout = 30000; PRAGMA foreign_keys = ON;";
        command.ExecuteNonQuery();
        return connection;
    }

    private static void AttachIncomingDatabase(SqliteConnection connection, string incomingDatabasePath)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "ATTACH DATABASE $path AS incoming;";
        command.Parameters.AddWithValue("$path", Path.GetFullPath(incomingDatabasePath));
        command.ExecuteNonQuery();
    }

    private static void DetachIncomingDatabase(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "DETACH DATABASE incoming;";
        command.ExecuteNonQuery();
    }

    private static void ValidateColumns(
        SqliteConnection connection,
        string table,
        IReadOnlyList<string> expected)
    {
        using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info({Quote(table)});";
        var actual = new List<string>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            actual.Add(reader.GetString(1));
        }

        if (!actual.SequenceEqual(expected, StringComparer.Ordinal))
        {
            throw new InvalidDataException($"The archive table {table} has an unsupported schema.");
        }
    }

    private static IReadOnlyList<InstallationProfile> ReadInstallationProfiles(SqliteConnection connection) =>
        ReadInstallationProfiles(connection, "main", null);

    private static IReadOnlyList<InstallationProfile> ReadInstallationProfiles(
        SqliteConnection connection,
        string database,
        SqliteTransaction? transaction)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"""
            SELECT installation_id, machine_name, friendly_name, color, icon,
                   first_seen_utc_ticks, updated_utc_ticks, profile_revision
            FROM {Quote(database)}.installation_profiles
            ORDER BY installation_id;
            """;
        var profiles = new List<InstallationProfile>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            profiles.Add(InstallationProfileCatalog.ValidatePersisted(new InstallationProfile(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                new DateTimeOffset(reader.GetInt64(5), TimeSpan.Zero),
                new DateTimeOffset(reader.GetInt64(6), TimeSpan.Zero),
                reader.GetInt64(7))));
        }

        return profiles;
    }

    private static void AddProfileParameters(SqliteCommand command, InstallationProfile profile)
    {
        command.Parameters.AddWithValue("$id", profile.InstallationId);
        command.Parameters.AddWithValue("$machine", profile.MachineName);
        command.Parameters.AddWithValue("$friendly", profile.FriendlyName);
        command.Parameters.AddWithValue("$color", profile.Color);
        command.Parameters.AddWithValue("$icon", profile.Icon);
        command.Parameters.AddWithValue("$firstSeen", profile.FirstSeenAt.UtcDateTime.Ticks);
        command.Parameters.AddWithValue("$updated", profile.UpdatedAt.UtcDateTime.Ticks);
        command.Parameters.AddWithValue("$revision", profile.Revision);
    }

    private static int ReadCount(SqliteConnection connection, string table) =>
        ReadCount(connection, "main", table, null);

    private static int ReadCount(
        SqliteConnection connection,
        string database,
        string table,
        SqliteTransaction? transaction)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"SELECT COUNT(*) FROM {Quote(database)}.{Quote(table)};";
        return checked(Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture));
    }

    private static HashSet<string> ReadStringSet(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        var result = new HashSet<string>(StringComparer.Ordinal);
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            result.Add(reader.GetString(0));
        }

        return result;
    }

    private static void ExecuteNonQuery(SqliteConnection connection, string sql) =>
        ExecuteNonQuery(connection, null, sql);

    private static void ExecuteNonQuery(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        string sql,
        params (string Name, object Value)[] parameters)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        foreach (var parameter in parameters)
        {
            command.Parameters.AddWithValue(parameter.Name, parameter.Value);
        }

        command.ExecuteNonQuery();
    }

    private static string BuildKeyJoin(string leftAlias, string rightAlias, IEnumerable<string> keys) =>
        string.Join(" AND ", keys.Select(key =>
            $"{leftAlias}.{Quote(key)} = {rightAlias}.{Quote(key)}"));

    private static string Quote(string identifier)
    {
        if (string.IsNullOrEmpty(identifier)
            || identifier.Any(character => !(char.IsAsciiLetterOrDigit(character) || character == '_')))
        {
            throw new InvalidOperationException("An internal SQLite identifier is invalid.");
        }

        return '"' + identifier + '"';
    }

    private static DataArchiveImportPlan ToPublicPlan(
        Guid planId,
        DateTimeOffset expiresAt,
        DataArchiveManifest manifest,
        string fingerprint,
        bool alreadyImported) =>
        new(
            planId,
            manifest.ArchiveId,
            expiresAt,
            fingerprint,
            manifest.CreatedAt,
            manifest.From,
            manifest.ToInclusive,
            manifest.Installations,
            manifest.ActivitySampleCount,
            manifest.AiRequestCount,
            manifest.AiAnalysisCount,
            manifest.ScreenshotFileCount,
            manifest.ScreenshotBytes,
            alreadyImported);

    private void RemoveExpiredPlans()
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var plan in _pendingPlans)
        {
            if (plan.Value.ExpiresAt <= now)
            {
                _pendingPlans.TryRemove(plan.Key, out _);
            }
        }
    }

    private static void DeleteFileIfPresent(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private static void DeleteDirectoryIfPresent(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }

    private sealed record DataArchiveManifest(
        int SchemaVersion,
        Guid ArchiveId,
        DateTimeOffset CreatedAt,
        DateOnly? From,
        DateOnly? ToInclusive,
        bool IncludesScreenshots,
        IReadOnlyList<DataArchiveInstallationSummary> Installations,
        int ActivitySampleCount,
        int AiRequestCount,
        int AiAnalysisCount,
        int ScreenshotFileCount,
        long ScreenshotBytes,
        IReadOnlyList<ArchiveEntryManifest> Entries);

    private sealed record ArchiveEntryManifest(string Path, long Length, string Sha256);

    private sealed record ArchiveSourceEntry(string SourcePath, ArchiveEntryManifest Manifest);

    private sealed record ArchiveValidation(
        string ArchivePath,
        string WorkDirectory,
        string DatabasePath,
        string Fingerprint,
        DataArchiveManifest Manifest);

    private sealed record DatabaseSummary(
        IReadOnlyList<DataArchiveInstallationSummary> Installations,
        int ActivitySampleCount,
        int AiRequestCount,
        int AiAnalysisCount);

    private sealed record PendingImportPlan(
        Guid PlanId,
        string ArchivePath,
        string Fingerprint,
        Guid ArchiveId,
        DateTimeOffset ExpiresAt);

    private sealed record ScreenshotImportEntry(
        string ArchivePath,
        string DestinationPath,
        long Length,
        string Sha256,
        bool AlreadyExists);

    private sealed record MergeCount(int Added, int Skipped);

    private sealed record ImportJournal(
        Guid ArchiveId,
        string Fingerprint,
        IReadOnlyList<ImportJournalFile> Files);

    private sealed record ImportJournalFile(string Path, string Sha256);
}
