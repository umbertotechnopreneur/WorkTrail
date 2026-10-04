// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Microsoft.Data.Sqlite;
using WorkTrail.Application;
using WorkTrail.Services;
using Xunit;

namespace WorkTrail.Core.Tests;

public sealed class AiScreenshotReprocessPersistenceTests
{
    [Fact]
    public void CandidateCatalog_GroupsMonitorsAndExcludesDescribedCapture()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var store = CreateStore(directory);
            var captureId = Guid.NewGuid().ToString("N");
            var capturedAt = DateTimeOffset.UtcNow.AddMinutes(-1);
            var paths = CreateCapturePaths(directory, captureId, 2);
            store.UpsertScreenshotIntervalTelemetry(
                captureId,
                paths,
                new ScreenshotIntervalTelemetry(capturedAt.AddMinutes(-5), capturedAt, 12, 4));

            var candidates = store.ListAiReprocessCandidates(
                capturedAt.AddMinutes(-1),
                capturedAt.AddMinutes(1),
                CancellationToken.None);

            var candidate = Assert.Single(candidates);
            Assert.Equal(captureId, candidate.CaptureId);
            Assert.Equal(2, candidate.ScreenshotPaths.Count);
            Assert.Equal(0, candidate.MissingFileCount);
            Assert.False(candidate.HasAiDescription);

            store.AppendAiAnalysisAndUsage(
                SuccessfulUsage(captureId, capturedAt, paths.Count),
                SuccessfulAnalysis(captureId, capturedAt, paths));

            Assert.True(store.HasAiDescription(captureId));
            Assert.Empty(store.ListAiReprocessCandidates(
                capturedAt.AddMinutes(-1),
                capturedAt.AddMinutes(1),
                CancellationToken.None));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void CandidateContext_UsesOnlySampleCoveringCaptureAndKeepsProcessSeparateForPrivacy()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var store = CreateStore(directory);
            var captureId = Guid.NewGuid().ToString("N");
            var capturedAt = DateTimeOffset.UtcNow.AddMinutes(-1);
            var paths = CreateCapturePaths(directory, captureId, 1);
            store.UpsertScreenshotIntervalTelemetry(
                captureId,
                paths,
                new ScreenshotIntervalTelemetry(capturedAt.AddMinutes(-5), capturedAt, null, null));
            store.UpsertCaptureHardwareSnapshot(captureId, new SystemSnapshot(capturedAt, "unavailable", []));
            store.AppendSample(ActivitySampleAt(
                capturedAt.AddSeconds(-1),
                durationSeconds: 20,
                processName: "nearby-public.exe",
                application: "Nearby public app",
                store.LoadSettings().InstallationId));
            store.AppendSample(ActivitySampleAt(
                capturedAt.AddSeconds(5),
                durationSeconds: 10,
                processName: "secret-editor.exe",
                application: "Friendly editor",
                store.LoadSettings().InstallationId));

            var candidate = Assert.Single(store.ListAiReprocessCandidates(
                capturedAt.AddMinutes(-1),
                capturedAt.AddMinutes(1),
                CancellationToken.None));

            Assert.Equal("Friendly editor", candidate.HistoricalContext!.Application);
            Assert.Equal(capturedAt, candidate.HistoricalContext.Snapshot!.Timestamp);
            Assert.Equal("unavailable", candidate.HistoricalContext.Snapshot.Status);
            Assert.Equal("secret-editor.exe", candidate.ProcessName);
            Assert.True(TrackingDomainService.IsHistoricalContextPrivate(
                store.LoadSettings() with { PrivacyProcessNames = "rule-id|secret-editor.exe" },
                candidate.ProcessName,
                candidate.HistoricalContext));
            Assert.False(TrackingDomainService.IsHistoricalContextPrivate(
                store.LoadSettings() with { PrivacyProcessNames = "rule-id|Friendly editor" },
                candidate.ProcessName,
                candidate.HistoricalContext));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void CandidateContext_FailsClosedWhenNoSampleCoversCaptureInstant()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var store = CreateStore(directory);
            var captureId = Guid.NewGuid().ToString("N");
            var capturedAt = DateTimeOffset.UtcNow.AddMinutes(-1);
            var paths = CreateCapturePaths(directory, captureId, 1);
            store.UpsertScreenshotIntervalTelemetry(
                captureId,
                paths,
                new ScreenshotIntervalTelemetry(capturedAt.AddMinutes(-5), capturedAt, null, null));
            store.AppendSample(ActivitySampleAt(
                capturedAt.AddSeconds(-1),
                durationSeconds: 20,
                processName: "nearby.exe",
                application: "Nearby app",
                store.LoadSettings().InstallationId));

            var candidate = Assert.Single(store.ListAiReprocessCandidates(
                capturedAt.AddMinutes(-1),
                capturedAt.AddMinutes(1),
                CancellationToken.None));

            Assert.Null(candidate.HistoricalContext);
            Assert.Equal(string.Empty, candidate.ProcessName);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void CandidateContext_UsesOnlyActivityOwnedByTheCaptureInstallation()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var store = CreateStore(directory);
            var captureId = Guid.NewGuid().ToString("N");
            var capturedAt = DateTimeOffset.UtcNow.AddMinutes(-1);
            var paths = CreateCapturePaths(directory, captureId, 1);
            var captureInstallationId = store.LoadSettings().InstallationId;
            store.UpsertScreenshotIntervalTelemetry(
                captureId,
                paths,
                new ScreenshotIntervalTelemetry(capturedAt.AddMinutes(-5), capturedAt, null, null));
            store.AppendSample(ActivitySampleAt(
                capturedAt.AddSeconds(5),
                durationSeconds: 10,
                processName: "owner-private.exe",
                application: "Owner private app",
                captureInstallationId));
            store.AppendSample(ActivitySampleAt(
                capturedAt.AddSeconds(5),
                durationSeconds: 10,
                processName: "foreign-public.exe",
                application: "Foreign public app",
                Guid.NewGuid().ToString("N")));

            var candidate = Assert.Single(store.ListAiReprocessCandidates(
                capturedAt.AddMinutes(-1),
                capturedAt.AddMinutes(1),
                CancellationToken.None));

            Assert.Equal(captureInstallationId, candidate.InstallationId);
            Assert.Equal("Owner private app", candidate.HistoricalContext!.Application);
            Assert.Equal("owner-private.exe", candidate.ProcessName);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void CandidateContext_FailsClosedWhenOnlyForeignInstallationActivityCoversCapture()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var store = CreateStore(directory);
            var captureId = Guid.NewGuid().ToString("N");
            var capturedAt = DateTimeOffset.UtcNow.AddMinutes(-1);
            var paths = CreateCapturePaths(directory, captureId, 1);
            store.UpsertScreenshotIntervalTelemetry(
                captureId,
                paths,
                new ScreenshotIntervalTelemetry(capturedAt.AddMinutes(-5), capturedAt, null, null));
            store.AppendSample(ActivitySampleAt(
                capturedAt.AddSeconds(5),
                durationSeconds: 10,
                processName: "foreign-public.exe",
                application: "Foreign public app",
                Guid.NewGuid().ToString("N")));

            var candidate = Assert.Single(store.ListAiReprocessCandidates(
                capturedAt.AddMinutes(-1),
                capturedAt.AddMinutes(1),
                CancellationToken.None));

            Assert.Equal(store.LoadSettings().InstallationId, candidate.InstallationId);
            Assert.Null(candidate.HistoricalContext);
            Assert.Equal(string.Empty, candidate.ProcessName);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void CandidateContext_FailsClosedWhenCaptureProvenanceIsMissing()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var store = CreateStore(directory);
            var captureId = Guid.NewGuid().ToString("N");
            var capturedAt = DateTimeOffset.UtcNow.AddMinutes(-1);
            var paths = CreateCapturePaths(directory, captureId, 1);
            store.UpsertScreenshotIntervalTelemetry(
                captureId,
                paths,
                new ScreenshotIntervalTelemetry(capturedAt.AddMinutes(-5), capturedAt, null, null));
            store.AppendSample(ActivitySampleAt(
                capturedAt.AddSeconds(5),
                durationSeconds: 10,
                processName: "public.exe",
                application: "Public app",
                store.LoadSettings().InstallationId));
            using (var connection = OpenDatabase(directory))
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "DELETE FROM screenshot_captures WHERE capture_id = $captureId;";
                command.Parameters.AddWithValue("$captureId", captureId);
                Assert.Equal(1, command.ExecuteNonQuery());
            }

            var candidate = Assert.Single(store.ListAiReprocessCandidates(
                capturedAt.AddMinutes(-1),
                capturedAt.AddMinutes(1),
                CancellationToken.None));

            Assert.Null(candidate.InstallationId);
            Assert.Null(candidate.HistoricalContext);
            Assert.Equal(string.Empty, candidate.ProcessName);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Theory]
    [InlineData("rule|secret.exe", "", "", "", "window", "context")]
    [InlineData("", "rule|private title", "", "process.exe", "", "context")]
    [InlineData("", "", "rule|private hint", "process.exe", "window", "")]
    public void HistoricalPrivacy_ConfiguredRuleWithoutItsTarget_FailsClosed(
        string processRules,
        string titleRules,
        string hintRules,
        string processName,
        string windowTitle,
        string context)
    {
        var settings = new AppSettings
        {
            PrivacyProcessNames = processRules,
            PrivacyWindowTitles = titleRules,
            PrivacyWindowHints = hintRules
        };
        var snapshot = new AnalysisContextSnapshot("Application", context, windowTitle, "active", null);

        Assert.True(TrackingDomainService.IsHistoricalContextPrivate(settings, processName, snapshot));
    }

    [Theory]
    [InlineData("malformed-row", "", "")]
    [InlineData("", "missing-value|", "")]
    [InlineData("", "", "|missing-id")]
    [InlineData("", "", "id|value|unexpected")]
    public void HistoricalPrivacy_MalformedNonEmptyRule_FailsClosed(
        string processRules,
        string titleRules,
        string hintRules)
    {
        var settings = new AppSettings
        {
            PrivacyProcessNames = processRules,
            PrivacyWindowTitles = titleRules,
            PrivacyWindowHints = hintRules
        };
        var snapshot = new AnalysisContextSnapshot("Application", "context", "window", "active", null);

        Assert.True(TrackingDomainService.IsHistoricalContextPrivate(settings, "process.exe", snapshot));
    }

    [Fact]
    public void CandidateContext_AtSharedBoundary_SelectsFollowingHalfOpenSample()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var store = CreateStore(directory);
            var captureId = Guid.NewGuid().ToString("N");
            var capturedAt = DateTimeOffset.UtcNow.AddMinutes(-2);
            var paths = CreateCapturePaths(directory, captureId, 1);
            store.UpsertScreenshotIntervalTelemetry(
                captureId,
                paths,
                new ScreenshotIntervalTelemetry(capturedAt.AddMinutes(-5), capturedAt, null, null));
            store.AppendSample(ActivitySampleAt(
                capturedAt,
                durationSeconds: 60,
                processName: "previous.exe",
                application: "Previous interval",
                store.LoadSettings().InstallationId));
            store.AppendSample(ActivitySampleAt(
                capturedAt.AddSeconds(60),
                durationSeconds: 60,
                processName: "following.exe",
                application: "Following interval",
                store.LoadSettings().InstallationId));

            var candidate = Assert.Single(store.ListAiReprocessCandidates(
                capturedAt.AddMinutes(-1),
                capturedAt.AddMinutes(1),
                CancellationToken.None));

            Assert.Equal("Following interval", candidate.HistoricalContext!.Application);
            Assert.Equal("following.exe", candidate.ProcessName);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void CandidateContext_OverlappingSamplesFailClosedAsAmbiguous()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var store = CreateStore(directory);
            var captureId = Guid.NewGuid().ToString("N");
            var capturedAt = DateTimeOffset.UtcNow.AddMinutes(-2);
            var paths = CreateCapturePaths(directory, captureId, 1);
            store.UpsertScreenshotIntervalTelemetry(
                captureId,
                paths,
                new ScreenshotIntervalTelemetry(capturedAt.AddMinutes(-5), capturedAt, null, null));
            store.AppendSample(ActivitySampleAt(
                capturedAt.AddSeconds(10),
                durationSeconds: 20,
                processName: "public.exe",
                application: "Public app",
                store.LoadSettings().InstallationId));
            store.AppendSample(ActivitySampleAt(
                capturedAt.AddSeconds(5),
                durationSeconds: 20,
                processName: "secret.exe",
                application: "Private app",
                store.LoadSettings().InstallationId));

            var candidate = Assert.Single(store.ListAiReprocessCandidates(
                capturedAt.AddMinutes(-1),
                capturedAt.AddMinutes(1),
                CancellationToken.None));

            Assert.Null(candidate.HistoricalContext);
            Assert.Equal(string.Empty, candidate.ProcessName);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void CandidateCatalog_IncludesRetainedOwnedFileWithoutTelemetryAsMissingMetadata()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var store = CreateStore(directory);
            var captureId = Guid.NewGuid().ToString("N");
            var capturedAt = DateTimeOffset.UtcNow.AddMinutes(-1);
            var paths = CreateCapturePaths(directory, captureId, 2);
            foreach (var path in paths)
            {
                File.SetLastWriteTimeUtc(path, capturedAt.UtcDateTime);
            }
            store.AppendSample(ActivitySampleAt(
                capturedAt,
                durationSeconds: 60,
                processName: "covered.exe",
                application: "Covered app",
                store.LoadSettings().InstallationId));

            var candidate = Assert.Single(store.ListAiReprocessCandidates(
                capturedAt.AddMinutes(-1),
                capturedAt.AddMinutes(1),
                CancellationToken.None));

            Assert.Equal(captureId, candidate.CaptureId);
            Assert.Equal(2, candidate.ScreenshotPaths.Count);
            Assert.Equal(0, candidate.MissingFileCount);
            Assert.Null(candidate.HistoricalContext);
            Assert.Equal(string.Empty, candidate.ProcessName);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>Opens an older beta database without rewriting its informational version.</summary>
    [Fact]
    public void SchemaVersionSix_IsOpenedWithoutRewritingItsVersion()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            _ = CreateStore(directory);
            using (var connection = OpenDatabase(directory))
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "PRAGMA user_version = 6;";
                command.ExecuteNonQuery();
            }

            _ = new LocalStore(directory);
            using var check = OpenDatabase(directory);
            using var version = check.CreateCommand();
            version.CommandText = "PRAGMA user_version;";
            Assert.Equal(6L, Convert.ToInt64(version.ExecuteScalar()));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void RuntimeAppend_InvalidCaptureCorrelationOrArtifactPrefix_RollsBackEntireTransaction()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var store = CreateStore(directory);
            var capturedAt = DateTimeOffset.UtcNow.AddMinutes(-1);
            var artifactCaptureId = Guid.NewGuid().ToString("N");
            var paths = CreateCapturePaths(directory, artifactCaptureId, 1);
            var mismatchedCaptureId = Guid.NewGuid().ToString("N");

            Assert.Throws<InvalidDataException>(() => store.AppendAiAnalysisAndUsage(
                SuccessfulUsage(mismatchedCaptureId, capturedAt, paths.Count),
                SuccessfulAnalysis(mismatchedCaptureId, capturedAt, paths)));
            Assert.Throws<InvalidDataException>(() => store.AppendAiAnalysisAndUsage(
                SuccessfulUsage("not-a-guid", capturedAt, paths.Count),
                SuccessfulAnalysis("not-a-guid", capturedAt, paths)));

            using var connection = OpenDatabase(directory);
            foreach (var table in new[] { "ai_request_usage", "ai_analysis_results", "ai_analysis_artifacts" })
            {
                using var count = connection.CreateCommand();
                count.CommandText = $"SELECT COUNT(*) FROM {table};";
                Assert.Equal(0L, Convert.ToInt64(count.ExecuteScalar()));
            }
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void JobCheckpoint_IsSingleFlightAndRecoversRunningItem()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var store = CreateStore(directory);
            var now = DateTimeOffset.UtcNow;
            var firstJobId = Guid.NewGuid();
            var selectedDate = new DateOnly(2001, 2, 3);
            var firstJob = Job(firstJobId, now, selectedDate);
            var firstItem = Item(firstJobId, now);
            store.CreateAiReprocessJob(firstJob, [firstItem]);

            var blockedJobId = Guid.NewGuid();
            Assert.Throws<SqliteException>(() => store.CreateAiReprocessJob(
                Job(blockedJobId, now),
                [Item(blockedJobId, now)]));

            store.TransitionAiReprocessJob(firstJobId, "running", null, now.AddSeconds(1));
            store.TransitionAiReprocessItem(
                firstJobId,
                firstItem.CaptureId,
                "running",
                attemptCount: 1,
                lastCode: null,
                updatedAt: now.AddSeconds(1));
            store.RecoverInterruptedAiReprocessJob(firstJobId, now.AddSeconds(2));

            var recoveredJob = Assert.IsType<AiReprocessJobRecord>(store.LoadAiReprocessJob(firstJobId));
            var recoveredItem = Assert.IsType<AiReprocessJobItemRecord>(store.LoadNextAiReprocessItem(firstJobId));
            Assert.Equal("paused_by_user", recoveredJob.State);
            Assert.Equal("runtime_restart", recoveredJob.PauseReason);
            Assert.Equal("pending", recoveredItem.State);
            Assert.Equal("runtime_restart", recoveredItem.LastCode);
            Assert.Equal(1, recoveredItem.AttemptCount);
            Assert.Equal(selectedDate, recoveredJob.SelectedDate);

            store.TransitionAiReprocessJob(firstJobId, "completed", null, now.AddSeconds(3));
            var secondJobId = Guid.NewGuid();
            store.CreateAiReprocessJob(Job(secondJobId, now.AddSeconds(4)), [Item(secondJobId, now.AddSeconds(4))]);
            Assert.Equal(secondJobId, store.LoadActiveAiReprocessJob()!.JobId);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void JobRecovery_ReconcilesPersistedAnalysisBeforeRetryingRunningItem()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var store = CreateStore(directory);
            var now = DateTimeOffset.UtcNow;
            var jobId = Guid.NewGuid();
            var item = Item(jobId, now);
            var screenshotPath = Path.Combine(directory, item.ArtifactIdentities.Single() + ".webp");
            File.WriteAllBytes(screenshotPath, [1, 2, 3]);
            store.CreateAiReprocessJob(Job(jobId, now), [item]);
            store.TransitionAiReprocessJob(jobId, "running", null, now.AddSeconds(1));
            store.TransitionAiReprocessItem(
                jobId,
                item.CaptureId,
                "running",
                attemptCount: 1,
                lastCode: null,
                updatedAt: now.AddSeconds(1));
            store.AppendAiAnalysisAndUsage(
                SuccessfulUsage(item.CaptureId, now.AddSeconds(2), 1),
                SuccessfulAnalysis(item.CaptureId, now.AddSeconds(2), [screenshotPath]));

            store.RecoverInterruptedAiReprocessJob(jobId, now.AddSeconds(3));

            var recoveredJob = Assert.IsType<AiReprocessJobRecord>(store.LoadAiReprocessJob(jobId));
            var recoveredItem = Assert.Single(store.ListAiReprocessJobItems(jobId));
            Assert.Equal("paused_by_user", recoveredJob.State);
            Assert.Equal("succeeded", recoveredItem.State);
            Assert.Equal("ai.analyzed.recovered", recoveredItem.LastCode);
            Assert.Null(store.LoadNextAiReprocessItem(jobId));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void TerminalJobRetention_PrunesOnlyExpiredTerminalJobs()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var store = CreateStore(directory);
            var now = DateTimeOffset.UtcNow;
            var expiredJobId = Guid.NewGuid();
            var recentJobId = Guid.NewGuid();
            var activeJobId = Guid.NewGuid();

            store.CreateAiReprocessJob(Job(expiredJobId, now.AddDays(-10)), [Item(expiredJobId, now.AddDays(-10))]);
            store.TransitionAiReprocessJob(expiredJobId, "completed", null, now.AddDays(-9));
            store.CreateAiReprocessJob(Job(recentJobId, now.AddDays(-1)), [Item(recentJobId, now.AddDays(-1))]);
            store.TransitionAiReprocessJob(recentJobId, "completed_with_errors", null, now);
            store.CreateAiReprocessJob(Job(activeJobId, now.AddDays(-10)), [Item(activeJobId, now.AddDays(-10))]);

            Assert.Equal(1, store.PruneTerminalAiReprocessJobs(now.AddDays(-5)));

            Assert.Null(store.LoadAiReprocessJob(expiredJobId));
            Assert.Empty(store.ListAiReprocessJobItems(expiredJobId));
            Assert.NotNull(store.LoadAiReprocessJob(recentJobId));
            Assert.NotNull(store.LoadAiReprocessJob(activeJobId));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void DailyVisualQuota_CountsSuccessfulAndFailedProviderAttemptsButNotConnectionTests()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var store = CreateStore(directory);
            var now = DateTimeOffset.Now;
            var captureId = Guid.NewGuid().ToString("N");

            store.AppendAiUsage(SuccessfulUsage(captureId, now, 1));
            store.AppendAiUsage(SuccessfulUsage(captureId, now.AddSeconds(1), 1) with
            {
                AttemptId = Guid.NewGuid().ToString("N"),
                RequestKind = "screen_analysis",
                Success = false,
                FailureCode = "timeout"
            });
            store.AppendAiUsage(SuccessfulUsage(captureId, now.AddSeconds(2), 1) with
            {
                AttemptId = Guid.NewGuid().ToString("N"),
                RequestKind = "ocr_refinement"
            });
            store.AppendAiUsage(SuccessfulUsage(captureId, now.AddSeconds(3), 0) with
            {
                AttemptId = Guid.NewGuid().ToString("N"),
                RequestKind = "connection_test"
            });

            Assert.Equal(3, store.GetTodayAnalysisCount());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static LocalStore CreateStore(string directory)
    {
        var store = new LocalStore(directory);
        store.SaveSettings(store.LoadSettings() with { ScreenshotDirectory = directory });
        return store;
    }

    private static IReadOnlyList<string> CreateCapturePaths(string directory, string captureId, int count)
    {
        var paths = Enumerable.Range(1, count)
            .Select(index => Path.Combine(directory, $"{captureId}_1.0.0_scheduled_monitor-{index}.webp"))
            .ToArray();
        foreach (var path in paths)
        {
            File.WriteAllBytes(path, [1, 2, 3]);
        }

        return paths;
    }

    private static AiRequestUsageRecord SuccessfulUsage(string captureId, DateTimeOffset at, int imageCount) => new(
        Guid.NewGuid().ToString("N"),
        captureId,
        at,
        at.AddSeconds(1),
        "snapshot.reprocess",
        "screen_analysis",
        "test-provider",
        "provider.invalid",
        "test-model",
        "test-model",
        null,
        null,
        200,
        100,
        null,
        imageCount,
        40,
        100,
        new AiUsageMetrics(10, 5, 15),
        "stop",
        true,
        null);

    private static ActivitySample ActivitySampleAt(
        DateTimeOffset timestamp,
        int durationSeconds,
        string processName,
        string application,
        string installationId) => new(
        timestamp,
        durationSeconds,
        "active",
        processName,
        application,
        "test context",
        "test window",
        installationId,
        0,
        0);

    private static AiAnalysis SuccessfulAnalysis(
        string captureId,
        DateTimeOffset at,
        IReadOnlyList<string> paths) => new(
        at,
        "Test app",
        "Test context",
        "Test summary",
        "test-installation",
        string.Join(';', paths),
        CorrelationId: captureId,
        Origin: "snapshot.reprocess");

    private static AiReprocessJobRecord Job(Guid jobId, DateTimeOffset at, DateOnly? selectedDate = null) => new(
        jobId,
        at,
        at,
        at.AddHours(-1),
        at.AddHours(1),
        selectedDate ?? DateOnly.FromDateTime(at.Date),
        null,
        "test-fingerprint",
        "pending",
        1,
        1,
        null);

    private static AiReprocessJobItemRecord Item(Guid jobId, DateTimeOffset at)
    {
        var captureId = Guid.NewGuid().ToString("N");
        return new AiReprocessJobItemRecord(
            jobId,
            captureId,
            0,
            at,
            "scheduled",
            [captureId + "_1.0.0_scheduled_monitor-1"],
            1,
            "pending",
            0,
            null,
            at);
    }

    private static SqliteConnection OpenDatabase(string directory)
    {
        var connection = new SqliteConnection($"Data Source={Path.Combine(directory, "activity.sqlite3")};Pooling=False");
        connection.Open();
        using var foreignKeys = connection.CreateCommand();
        foreignKeys.CommandText = "PRAGMA foreign_keys = ON;";
        foreignKeys.ExecuteNonQuery();
        return connection;
    }

    private static string CreateTemporaryDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "WorkTrailReprocessPersistenceTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }
}
