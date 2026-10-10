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
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using Microsoft.Data.Sqlite;
using WorkTrail.Application;
using WorkTrail.Services;
using Xunit;

namespace WorkTrail.Core.Tests;

/// <summary>Verifies durable capture-time hardware evidence across the artifact lifecycle.</summary>
public sealed class CaptureHardwarePersistenceTests
{
    /// <summary>One snapshot survives reopening and is shared by all retained monitor images without AI.</summary>
    [Fact]
    public void HardwareSnapshot_RoundTripsOncePerCaptureWithoutAi()
    {
        using var fixture = new CaptureFixture();
        var snapshot = Snapshot(fixture.CapturedAt);
        fixture.Store.UpsertCaptureHardwareSnapshot(fixture.CaptureId, snapshot);
        fixture.Store.UpsertCaptureHardwareSnapshot(fixture.CaptureId, snapshot);

        var reopened = new LocalStore(fixture.Directory);
        var retained = Assert.IsType<SystemSnapshot>(reopened.LoadCaptureHardwareSnapshot(fixture.CaptureId));
        Assert.Equal(snapshot.Timestamp, retained.Timestamp);
        Assert.Equal(snapshot.Status, retained.Status);
        Assert.Equal(snapshot.DriverStatus, retained.DriverStatus);
        var device = Assert.Single(retained.Devices);
        Assert.Equal(41.25d, device.Sensors[0].Value);
        Assert.Equal(0d, device.Sensors[1].Value);
        Assert.Null(device.Sensors[2].Value);
        Assert.Null(reopened.LoadAiAnalysis(fixture.CaptureId));
        Assert.Equal(1L, fixture.Scalar("SELECT COUNT(*) FROM capture_hardware_snapshots;"));
        var gallery = reopened.GetScreenshotGallery(DateOnly.FromDateTime(fixture.CapturedAt.LocalDateTime), CancellationToken.None);
        Assert.Equal(2, gallery.Items.Count);
        Assert.All(gallery.Items, item => Assert.Equal(snapshot.Timestamp, item.HardwareSnapshot!.Timestamp));
    }

    /// <summary>Capture-time evidence cannot be silently replaced with a later measurement.</summary>
    [Fact]
    public void HardwareSnapshot_RejectsReplacementAndUnregisteredCapture()
    {
        using var fixture = new CaptureFixture();
        var snapshot = Snapshot(fixture.CapturedAt);
        fixture.Store.UpsertCaptureHardwareSnapshot(fixture.CaptureId, snapshot);

        Assert.Throws<InvalidDataException>(() => fixture.Store.UpsertCaptureHardwareSnapshot(
            fixture.CaptureId, snapshot with { Timestamp = fixture.CapturedAt.AddSeconds(1) }));
        Assert.Throws<SqliteException>(() => fixture.Store.UpsertCaptureHardwareSnapshot(Guid.NewGuid().ToString("N"), snapshot));
        Assert.Equal(snapshot.Timestamp, fixture.Store.LoadCaptureHardwareSnapshot(fixture.CaptureId)!.Timestamp);
    }

    /// <summary>A hardware-write failure rolls back new provenance and every monitor interval.</summary>
    [Fact]
    public void HardwareWriteFailure_RollsBackAllCaptureMetadata()
    {
        using var fixture = new CaptureFixture();
        fixture.Execute("""
            CREATE TRIGGER fail_hardware_insert BEFORE INSERT ON capture_hardware_snapshots
            BEGIN SELECT RAISE(ABORT, 'test hardware storage failure'); END;
            """);
        var captureId = Guid.NewGuid().ToString("N");
        var paths = fixture.Paths.Select(path => path.Replace(fixture.CaptureId, captureId, StringComparison.Ordinal)).ToArray();

        Assert.Throws<SqliteException>(() => fixture.Store.UpsertScreenshotIntervalTelemetry(
            captureId, paths,
            new ScreenshotIntervalTelemetry(fixture.CapturedAt.AddMinutes(-5), fixture.CapturedAt, 12, 34),
            Snapshot(fixture.CapturedAt)));

        Assert.Equal(1L, fixture.Scalar("SELECT COUNT(*) FROM screenshot_captures;"));
        Assert.Equal(2L, fixture.Scalar("SELECT COUNT(*) FROM screenshot_interval_telemetry;"));
        Assert.Equal(0L, fixture.Scalar("SELECT COUNT(*) FROM capture_hardware_snapshots;"));
        Assert.Null(fixture.Store.LoadScreenshotIntervalTelemetry(paths[0]));
        Assert.Null(fixture.Store.LoadScreenshotIntervalTelemetry(paths[1]));
    }

    /// <summary>Removing one monitor's metadata does not remove hardware still owned by its sibling.</summary>
    [Fact]
    public void AnalysisRemoval_PreservesSiblingHardwareUntilLastTelemetryIsRemoved()
    {
        using var fixture = new CaptureFixture();
        fixture.Store.UpsertCaptureHardwareSnapshot(fixture.CaptureId, Snapshot(fixture.CapturedAt));

        Assert.Equal(1, fixture.Store.DeleteScreenshotIntervalTelemetry(fixture.Paths[0]));
        Assert.NotNull(fixture.Store.LoadCaptureHardwareSnapshot(fixture.CaptureId));
        var gallery = fixture.Store.GetScreenshotGallery(DateOnly.FromDateTime(fixture.CapturedAt.LocalDateTime), CancellationToken.None);
        Assert.Null(gallery.Items.Single(item => item.Path == fixture.Paths[0]).HardwareSnapshot);
        Assert.NotNull(gallery.Items.Single(item => item.Path == fixture.Paths[1]).HardwareSnapshot);

        Assert.Equal(2, fixture.Store.DeleteScreenshotIntervalTelemetry(fixture.Paths[1]));
        Assert.Null(fixture.Store.LoadCaptureHardwareSnapshot(fixture.CaptureId));
        Assert.Equal(0, fixture.Store.DeleteScreenshotIntervalTelemetry(fixture.Paths[1]));
    }

    /// <summary>Malformed persisted readings are rejected rather than substituted with empty telemetry.</summary>
    [Fact]
    public void HardwareSnapshotRead_RejectsMismatchedTimestampAndUnknownFields()
    {
        using var fixture = new CaptureFixture();
        fixture.Store.UpsertCaptureHardwareSnapshot(fixture.CaptureId, Snapshot(fixture.CapturedAt));
        fixture.Execute("UPDATE capture_hardware_snapshots SET sampled_utc_ticks = sampled_utc_ticks + 1;");
        Assert.Throws<InvalidDataException>(() => fixture.Store.LoadCaptureHardwareSnapshot(fixture.CaptureId));

        fixture.Execute("UPDATE capture_hardware_snapshots SET snapshot_json = '{\"unexpected\":1}';");
        Assert.Throws<JsonException>(() => fixture.Store.LoadCaptureHardwareSnapshot(fixture.CaptureId));
    }

    /// <summary>Data retention accounts for and removes hardware using the capture date, not sample clock skew.</summary>
    [Fact]
    public void Retention_RemovesExpiredHardware()
    {
        using var fixture = new CaptureFixture();
        fixture.Store.UpsertCaptureHardwareSnapshot(fixture.CaptureId, Snapshot(fixture.CapturedAt));
        var activity = new SqliteActivityStore(fixture.Store.ActivityDatabasePath);
        var preview = activity.GetRetentionPreview(fixture.CapturedAt.AddMinutes(1));
        Assert.Equal(3, preview.Count);
        Assert.True(preview.Bytes > 0);
        var removed = activity.ApplyRetention(fixture.CapturedAt.AddMinutes(1));

        Assert.Equal(3, removed);
        Assert.Null(fixture.Store.LoadCaptureHardwareSnapshot(fixture.CaptureId));
    }

    /// <summary>Archives reject hardware identity collisions instead of rewriting capture-time evidence.</summary>
    [Fact]
    public void ArchiveImport_RejectsConflictingHardwareForExistingCapture()
    {
        using var fixture = new CaptureFixture();
        fixture.Store.UpsertCaptureHardwareSnapshot(fixture.CaptureId, Snapshot(fixture.CapturedAt));
        var firstArchive = Path.Combine(fixture.Directory, "original.tmuarchive");
        new DataArchiveService(fixture.Store).Export(
            new DataArchiveExportRequest(firstArchive, IncludeScreenshots: false), CancellationToken.None);
        var target = new LocalStore(Path.Combine(fixture.Directory, "target"));
        var importer = new DataArchiveService(target);
        var firstPlan = importer.PreviewImport(new DataArchiveImportPreviewRequest(firstArchive), CancellationToken.None);
        importer.Import(firstPlan.PlanId, CancellationToken.None);

        fixture.Execute("UPDATE capture_hardware_snapshots SET snapshot_json = replace(snapshot_json, '41.25', '99.5');");
        var conflictArchive = Path.Combine(fixture.Directory, "conflicting.tmuarchive");
        new DataArchiveService(fixture.Store).Export(
            new DataArchiveExportRequest(conflictArchive, IncludeScreenshots: false), CancellationToken.None);
        Assert.Throws<InvalidDataException>(() => importer.PreviewImport(
            new DataArchiveImportPreviewRequest(conflictArchive), CancellationToken.None));
        Assert.Equal(41.25d, target.LoadCaptureHardwareSnapshot(fixture.CaptureId)!.Devices[0].Sensors[0].Value);
    }

    /// <summary>Invalid stored hardware JSON cannot be published as an apparently valid archive.</summary>
    [Fact]
    public void ArchiveExport_RejectsMalformedHardwarePayload()
    {
        using var fixture = new CaptureFixture();
        fixture.Store.UpsertCaptureHardwareSnapshot(fixture.CaptureId, Snapshot(fixture.CapturedAt));
        fixture.Execute("UPDATE capture_hardware_snapshots SET snapshot_json = '{\"unexpected\":1}';");
        var archivePath = Path.Combine(fixture.Directory, "invalid.tmuarchive");

        Assert.Throws<JsonException>(() => new DataArchiveService(fixture.Store).Export(
            new DataArchiveExportRequest(archivePath, IncludeScreenshots: false), CancellationToken.None));
        Assert.False(File.Exists(archivePath));
    }

    private static SystemSnapshot Snapshot(DateTimeOffset timestamp) => new(timestamp, "partial",
    [
        new HardwareDeviceSnapshot("/gpu-nvidia/0", "Test GPU", "GpuNvidia", timestamp,
        [
            new HardwareSensorSnapshot("/gpu-nvidia/0/temperature/0", "GPU Core", "Temperature", "°C", 41.25),
            new HardwareSensorSnapshot("/gpu-nvidia/0/power/0", "GPU Power", "Power", "W", 0),
            new HardwareSensorSnapshot("/gpu-nvidia/0/clock/0", "GPU Core", "Clock", "MHz", null)
        ])
    ]);

    private sealed class CaptureFixture : IDisposable
    {
        internal string Directory { get; } = Path.Combine(Path.GetTempPath(), "WorkTrail.HardwareTests", Guid.NewGuid().ToString("N"));
        internal LocalStore Store { get; }
        internal string CaptureId { get; } = Guid.NewGuid().ToString("N");
        internal DateTimeOffset CapturedAt { get; } = DateTimeOffset.UtcNow.AddMinutes(-1);
        internal string[] Paths { get; }

        internal CaptureFixture()
        {
            System.IO.Directory.CreateDirectory(Directory);
            Store = new LocalStore(Directory);
            var screenshotRoot = Path.Combine(Directory, "screenshots");
            Store.SaveSettings(Store.LoadSettings() with { ScreenshotDirectory = screenshotRoot });
            var day = ScreenshotStorageLayout.GetDayDirectory(screenshotRoot, CapturedAt);
            System.IO.Directory.CreateDirectory(day);
            Paths = Enumerable.Range(1, 2).Select(index => Path.Combine(day, $"{CaptureId}_1.0.0_manual_monitor-{index}.webp")).ToArray();
            foreach (var path in Paths)
            {
                File.WriteAllBytes(path, [1, 2, 3]);
                File.SetLastWriteTimeUtc(path, CapturedAt.UtcDateTime);
            }

            Store.UpsertScreenshotIntervalTelemetry(CaptureId, Paths, new ScreenshotIntervalTelemetry(
                CapturedAt.AddMinutes(-5), CapturedAt, null, null));
        }

        internal long Scalar(string sql)
        {
            using var connection = new SqliteConnection($"Data Source={Store.ActivityDatabasePath};Pooling=False");
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = sql;
            return Convert.ToInt64(command.ExecuteScalar());
        }

        internal void Execute(string sql)
        {
            using var connection = new SqliteConnection($"Data Source={Store.ActivityDatabasePath};Pooling=False");
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = sql;
            command.ExecuteNonQuery();
        }

        /// <summary>Removes only this fixture's uniquely allocated test directory.</summary>
        public void Dispose() => System.IO.Directory.Delete(Directory, recursive: true);
    }
}
