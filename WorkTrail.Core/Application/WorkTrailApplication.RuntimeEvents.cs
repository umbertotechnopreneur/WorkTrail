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


using System.Globalization;
using Microsoft.Extensions.Logging;
using WorkTrail.Services;

namespace WorkTrail.Application;

/// <summary>Coordinates tracking runtime events, scheduled captures and runtime-owned serialization.</summary>
public sealed partial class WorkTrailApplication
{
    private void ConfigureScheduledSnapshots(AppSettings settings, bool restartCountdown)
    {
        _scheduledSnapshotIntervalMinutes = settings.ScreenshotIntervalMinutes;
        _scheduledSnapshotsEnabled = settings.ScreenshotsEnabled
            && _scheduledSnapshotIntervalMinutes > 0
            && ActiveHoursSchedule.HasAnyActivePeriod(settings.ActiveHours);
        if (!_scheduledSnapshotsEnabled)
        {
            // With capture disabled or no eligible hours there is no countdown and no silent schedule reset.
            _nextScheduledSnapshotAt = null;
            _pausedScheduledSnapshotRemaining = null;
            return;
        }

        if (_tracking.IsTracking)
        {
            if (restartCountdown || _nextScheduledSnapshotAt is null)
            {
                _nextScheduledSnapshotAt = DateTimeOffset.Now.AddMinutes(_scheduledSnapshotIntervalMinutes);
            }

            _pausedScheduledSnapshotRemaining = null;
            return;
        }

        if (restartCountdown || _pausedScheduledSnapshotRemaining is null)
        {
            _pausedScheduledSnapshotRemaining = TimeSpan.FromMinutes(_scheduledSnapshotIntervalMinutes);
        }

        _nextScheduledSnapshotAt = null;
    }

    private void PauseScheduledSnapshots()
    {
        _pausedScheduledSnapshotRemaining = GetScheduledSnapshotRemaining();
        _nextScheduledSnapshotAt = null;
    }

    private void ResumeScheduledSnapshots()
    {
        if (!_scheduledSnapshotsEnabled)
        {
            return;
        }

        var remaining = _pausedScheduledSnapshotRemaining ?? TimeSpan.FromMinutes(_scheduledSnapshotIntervalMinutes);
        _nextScheduledSnapshotAt = DateTimeOffset.Now.Add(remaining);
        _pausedScheduledSnapshotRemaining = null;
    }

    private async Task RunRuntimeTimerLoopAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        while (true)
        {
            try
            {
                if (!await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
                {
                    return;
                }

                // This owned loop awaits each pass, so slow capture work cannot overlap or outlive disposal.
                await ProcessRuntimeTimerAsync().ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                // Runtime work retries on the next bounded tick; failures never become unobserved callbacks.
                _logger.LogError("Runtime timer processing failed. ExceptionType={ExceptionType}", exception.GetType().Name);
            }
        }
    }

    private Task<OperationResult<T>> MutateVisualStateAsync<T>(
        Func<Task<OperationResult<T>>> operation,
        CancellationToken cancellationToken) =>
        _screenshotReprocessing.RunExclusiveMutationAsync(
            () => MutateAsync(operation, cancellationToken),
            cancellationToken);

    private async Task ProcessRuntimeTimerAsync()
    {
        if (_atomicResetPrepared)
        {
            return;
        }

        await _snapshot.SetTrackingAsync(_tracking.IsTracking, _runtimeTimerCancellation.Token).ConfigureAwait(false);
        await CaptureActivityScoreTelemetryIfDueAsync();
        await ProcessScheduledSnapshotAsync();
    }

    private void OnDashboardStateChanged(DashboardState state) => RuntimeStateChanged?.Invoke(this, new RuntimeStateChangedEventArgs(EnrichDashboardState(state), "runtime.dashboard.changed"));

    private void OnTrackingStateChanged(bool isTracking) => RuntimeStateChanged?.Invoke(this, new RuntimeStateChangedEventArgs(LoadDashboardState(), isTracking ? "tracking.started" : "tracking.paused"));

    private void OnTrackingRuntimeHealthChanged(TrackingRuntimeHealth health)
    {
        if (!health.IsDegraded)
        {
            _logger.LogInformation("Activity sample persistence recovered. LastPersistedAt={LastPersistedAt}", health.LastPersistedSampleAt);
            return;
        }

        var lastPersisted = health.LastPersistedSampleAt?.ToString("O", CultureInfo.InvariantCulture) ?? "none";
        _logger.LogError("Activity sample persistence is degraded. LastPersistedAt={LastPersistedAt}", lastPersisted);
        EnqueueNotification(new ApplicationNotification(
            Guid.NewGuid(),
            DateTimeOffset.UtcNow,
            ApplicationNotificationSeverity.Warning,
            "Notification.TrackingPersistenceDegraded.Title",
            "Notification.TrackingPersistenceDegraded.Message",
            health.StatusCode,
            lastPersisted));
    }
}
