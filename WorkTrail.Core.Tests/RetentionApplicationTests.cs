// SPDX-License-Identifier: MIT

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using WorkTrail.Application;
using WorkTrail.Services;
using Xunit;

namespace WorkTrail.Core.Tests;

/// <summary>Checks calendar-month cleanup using isolated databases and owned screenshot artifacts.</summary>
[Collection(ProcessEnvironmentCollection.Name)]
public sealed class RetentionApplicationTests
{
    /// <summary>Persisted capture dates and verified tier limits decide what cleanup removes.</summary>
    [Theory]
    [InlineData(ProductTier.Free, 2, 1)]
    [InlineData(ProductTier.Premium, 1, 3)]
    // tier determines the verified calendar-month limit.
    // expiredCount is the expected number of deleted records and screenshots.
    // maximumMonths is the limit exposed to the passive UI.
    public async Task Cleanup_UsesCalendarMonthsAndPreservesRecentAndForeignFiles(ProductTier tier, int expiredCount, int maximumMonths)
    {
        await using var fixture = new Fixture(tier);
        var now = DateTimeOffset.Now;
        var oldest = fixture.AddHistory(now.AddMonths(-4), "oldest");
        var middle = fixture.AddHistory(now.AddMonths(-2), "middle");
        var newest = fixture.AddHistory(now.AddDays(-5), "newest");
        var foreign = Path.Combine(fixture.DirectoryPath, "personal-photo.webp");
        File.WriteAllBytes(foreign, [4, 5, 6]);
        File.SetLastWriteTimeUtc(foreign, now.AddYears(-1).UtcDateTime);
        File.SetLastWriteTimeUtc(newest, now.AddYears(-1).UtcDateTime);

        var preview = await fixture.Application.PreviewRetentionAsync(CancellationToken.None);
        Assert.True(preview.Succeeded);
        Assert.Equal(expiredCount, preview.Value!.RecordCount);
        Assert.Equal(expiredCount, preview.Value.ScreenshotCount);
        Assert.Null(fixture.Store.LoadSettings().LastRetentionCleanupAt);
        Assert.True(File.Exists(oldest));

        var operationId = Guid.NewGuid();
        var result = await fixture.Application.RunRetentionAsync(new RetentionRequest(true, true, OperationId: operationId), CancellationToken.None);
        Assert.True(result.Succeeded);
        Assert.False(File.Exists(oldest));
        Assert.Equal(tier == ProductTier.Premium, File.Exists(middle));
        Assert.True(File.Exists(newest));
        Assert.True(File.Exists(foreign));
        Assert.Equal(3 - expiredCount, fixture.Store.GetRetentionPreview(now.AddMinutes(1)).RecordCount);
        Assert.NotNull(fixture.Store.LoadSettings().LastRetentionCleanupAt);

        var status = await fixture.Application.GetRetentionStatusAsync(CancellationToken.None);
        Assert.Equal(maximumMonths, status.Value!.MaximumMonths);
        Assert.False(status.Value.IsCleanupDue);
        Assert.Equal(operationId, status.Value.Progress!.OperationId);
        Assert.Equal("Completed", status.Value.Progress.Phase);
        Assert.Equal(status.Value.Progress.TotalItems, status.Value.Progress.CompletedItems);
    }

    /// <summary>Scheduled cleanup cannot bypass the monthly due date or manual confirmation.</summary>
    [Fact]
    public async Task Cleanup_NotDueAndUnconfirmedRequestsLeaveHistoryUntouched()
    {
        await using var fixture = new Fixture(ProductTier.Free);
        var screenshot = fixture.AddHistory(DateTimeOffset.Now.AddMonths(-4), "expired");
        var original = await fixture.Application.GetRetentionStatusAsync(CancellationToken.None);
        Assert.False(original.Value!.IsCleanupDue);

        var manual = await fixture.Application.RunRetentionAsync(new RetentionRequest(true, false), CancellationToken.None);
        Assert.False(manual.Succeeded);
        var scheduled = await fixture.Application.RunRetentionAsync(new RetentionRequest(true, false, Scheduled: true), CancellationToken.None);
        Assert.True(scheduled.Succeeded);
        Assert.Equal("retention.not_due", scheduled.Code);
        Assert.True(File.Exists(screenshot));
        Assert.Null(fixture.Store.LoadSettings().LastRetentionCleanupAt);
    }

    /// <summary>A cancelled request never advances the durable monthly checkpoint.</summary>
    [Fact]
    public async Task Cleanup_CancelledRequestDoesNotDeleteOrAdvanceSchedule()
    {
        await using var fixture = new Fixture(ProductTier.Free);
        var screenshot = fixture.AddHistory(DateTimeOffset.Now.AddMonths(-4), "expired");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fixture.Application.RunRetentionAsync(
            new RetentionRequest(true, true), cancellation.Token));
        Assert.True(File.Exists(screenshot));
        Assert.Null(fixture.Store.LoadSettings().LastRetentionCleanupAt);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        // tier selects the trusted access state for this isolated runtime.
        internal Fixture(ProductTier tier)
        {
            DirectoryPath = Path.Combine(Path.GetTempPath(), "WorkTrail.Tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(DirectoryPath);
            Store = new LocalStore(DirectoryPath);
            var settings = Store.LoadSettings() with
            {
                ScreenshotDirectory = DirectoryPath,
                DataRetentionDays = 90,
                ScreenshotRetentionDays = 90,
                OpenAiEnabled = false
            };
            Store.SaveSettings(settings);
            var snapshot = new SettingsSnapshot(settings);
            var capture = new NoCaptureService();
            Application = new WorkTrailApplication(Store, new UtilityService(), new TrackingDomainService(Store, snapshot),
                capture, new FakeHardwareTelemetryService(), new OpenAiAnalysisService(Store, capture),
                new StartupService(), new BuildInformationService(), settingsSnapshot: snapshot,
                startScheduledSnapshotTimer: false, featureLicenseSource: new TestLicense(tier));
        }

        internal string DirectoryPath { get; }
        internal LocalStore Store { get; }
        internal WorkTrailApplication Application { get; }

        // timestamp is the authoritative date of both the activity and capture.
        // context distinguishes the retained activity samples.
        internal string AddHistory(DateTimeOffset timestamp, string context)
        {
            var settings = Store.LoadSettings();
            Store.AppendSample(new ActivitySample(timestamp, 5, "active", "test", "Test", context,
                "Test window", settings.InstallationId, 0, 0));
            var captureId = Guid.NewGuid().ToString("N");
            var dayDirectory = ScreenshotStorageLayout.GetDayDirectory(DirectoryPath, DateOnly.FromDateTime(timestamp.LocalDateTime));
            Directory.CreateDirectory(dayDirectory);
            var path = Path.Combine(dayDirectory, $"{captureId}_1.0.0_manual_monitor-1.webp");
            File.WriteAllBytes(path, [1, 2, 3]);
            Store.RegisterScreenshotCapture(captureId, settings.InstallationId, timestamp, ScreenshotCaptureOrigins.Manual);
            return path;
        }

        /// <inheritdoc />
        public async ValueTask DisposeAsync()
        {
            await Application.DisposeAsync();
            SqliteConnection.ClearAllPools();
            var root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "WorkTrail.Tests")) + Path.DirectorySeparatorChar;
            if (!Path.GetFullPath(DirectoryPath).StartsWith(root, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Retention fixture cleanup must stay inside its temporary test root.");
            Directory.Delete(DirectoryPath, recursive: true);
        }
    }

    // tier supplies a trusted, deterministic license state without external services.
    private sealed class TestLicense(ProductTier tier) : IFeatureLicenseSource
    {
        /// <inheritdoc />
        public ProductTier Tier => tier;
    }

    private sealed class NoCaptureService : IScreenCaptureService
    {
        /// <inheritdoc />
        // directory is unused because retention tests never read screen pixels.
        // captureMode is unused because capture is forbidden in this fixture.
        // captureOrigin is unused because capture is forbidden in this fixture.
        // authorizeCapture is unused because capture is forbidden in this fixture.
        // InvalidOperationException reports any accidental capture call.
        public ScreenshotCaptureResult CaptureByMode(string directory, string captureMode, string captureOrigin,
            Func<ScreenshotCaptureContext, ScreenshotCaptureDecision> authorizeCapture) =>
            throw new InvalidOperationException("Retention tests must not capture the owner's screen.");
    }
}
