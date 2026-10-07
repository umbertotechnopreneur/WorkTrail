// SPDX-License-Identifier: MIT

using Microsoft.Extensions.Logging;
using WorkTrail.Services;

namespace WorkTrail.Application;

/// <summary>Coordinates bounded desktop capture work and its optional hardware context.</summary>
public sealed partial class WorkTrailApplication
{
    private readonly ScreenshotPublicationJournal _screenshotPublications;
    private int _screenshotCaptureFailureNotificationActive;

    // capture contains only published, privacy-approved images; notify once for each monitor image.
    // cancellationToken stops optional notification work when capture is being cancelled.
    private async Task NotifyScreenshotCaptureAsync(ScreenshotCaptureResult capture, CancellationToken cancellationToken)
    {
        // A completed capture ends the failure episode even when successful-capture notifications are disabled.
        Interlocked.Exchange(ref _screenshotCaptureFailureNotificationActive, 0);
        if (_screenshotNotifications is null || !_settingsSnapshot.Value.NotificationsEnabled || !_settingsSnapshot.Value.ScreenshotNotificationsEnabled) return;
        var paths = capture.StoredScreenshotPaths.Count > 0 ? capture.StoredScreenshotPaths : capture.AnalysisScreenshotPaths;
        try
        {
            await RunCaptureWorkAsync(() =>
            {
                foreach (var path in paths.Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    if (cancellationToken.IsCancellationRequested) break;
                    _screenshotNotifications.Show(path);
                }
                return true;
            }, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The image pipeline owns cancellation; an optional notice has no capture cleanup authority.
        }
    }

    // gallery identifies incomplete artifacts without exposing their contents or assigning a false owner.
    private void ReportIncompleteScreenshotGallery(ScreenshotGallery gallery)
    {
        if (gallery.UnavailableArtifactCount == 0) return;
        _logger.LogWarning("Screenshot gallery contains incomplete captures. Date={Date} ArtifactCount={ArtifactCount}",
            gallery.Date, gallery.UnavailableArtifactCount);
        EnqueueNotification(new ApplicationNotification(Guid.NewGuid(), DateTimeOffset.UtcNow,
            ApplicationNotificationSeverity.Error, "Notification.ScreenshotCaptureFailed.Title", "ScreenshotGalleryFailed",
            "screenshot.gallery.incomplete", gallery.UnavailableArtifactCount.ToString(System.Globalization.CultureInfo.InvariantCulture)));
    }

    private void RecoverScreenshotPublications()
    {
        // Recovery reports incomplete work without blocking startup or inventing capture provenance.
        void ReportFailure(Exception exception)
        {
            _logger.LogWarning("Screenshot publication recovery failed. ExceptionType={ExceptionType}", exception.GetType().Name);
            EnqueueNotification(new ApplicationNotification(Guid.NewGuid(), DateTimeOffset.UtcNow,
                ApplicationNotificationSeverity.Error, "Notification.ScreenshotCaptureFailed.Title",
                "Notification.ScreenshotCaptureFailed.Message", "screenshot.publication.recovery.failed", exception.GetType().Name));
        }
        try
        {
            _screenshotPublications.Recover(ReportFailure);
            // No capture worker exists yet. Remove only private transient staging without a durable owner.
            _screenshotPublications.CleanupUnclaimedStages(_screenshotPublications.TransientRoot, cutoff: null, CancellationToken.None);
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException)
        {
            ReportFailure(exception);
        }
    }
    private async Task<SystemSnapshot> CaptureScreenshotHardwareAsync(CancellationToken cancellationToken)
    {
        try
        {
            var settings = _settingsSnapshot.Value;
            var context = await _deviceContext.CaptureAsync(
                settings.OpenAiEnabled && settings.IncludeDeviceLocation, cancellationToken).ConfigureAwait(false);
            // Read hardware last so an optional slower location lookup cannot age the sensor sample before pixels.
            var snapshot = await CaptureAndRecordSystemSnapshotAsync(cancellationToken).ConfigureAwait(false);
            return snapshot with
            {
                DeviceContext = context,
                InformationalSchedule = ActiveHoursSchedule.BuildInformationalNote(settings.ActiveHours, snapshot.Timestamp)
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            // Optional sensor failure is durable and visible; image capture is still allowed.
            _logger.LogWarning("Screenshot hardware collection failed. ExceptionType={ExceptionType}", exception.GetType().Name);
            return new SystemSnapshot(DateTimeOffset.UtcNow, "error", [], ErrorCode: "collection-failed");
        }
    }

    private async Task<T> RunCaptureWorkAsync<T>(Func<T> operation, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation);
        await _captureWorker.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // The bounded worker keeps synchronous desktop capture and codecs off WinUI and prevents overlap.
            return await Task.Run(operation, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _captureWorker.Release();
        }
    }
}
