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


using Microsoft.Extensions.Logging;
using WorkTrail.Services;

namespace WorkTrail.Application;

/// <summary>Owns runtime disposal, cancellation and serialized application work.</summary>
public sealed partial class WorkTrailApplication
{
    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        CancelLiveWork();
        _runtimeTimerCancellation.Cancel();
        Task liveWorkDrained;
        lock (_liveWorkLock)
        {
            liveWorkDrained = _liveWorkCount == 0 ? Task.CompletedTask
                : (_liveWorkDrained = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously)).Task;
        }
        await liveWorkDrained.ConfigureAwait(false);
        _screenshotNotifications?.Dispose();
        _liveWorkCancellation.Dispose();
        await _runtimeTimerTask.ConfigureAwait(false);
        await _timesheetRecoveryTask.ConfigureAwait(false);
        _runtimeTimerCancellation.Dispose();
        await _screenshotReprocessing.DisposeAsync().ConfigureAwait(false);
        _tracking.DashboardStateChanged -= OnDashboardStateChanged;
        _tracking.TrackingStateChanged -= OnTrackingStateChanged;
        _tracking.RuntimeHealthChanged -= OnTrackingRuntimeHealthChanged;
        if (_pricingRefresh is not null)
        {
            await _pricingRefresh.DisposeAsync().ConfigureAwait(false);
        }
        _tracking.Dispose();
        await _search.DisposeAsync().ConfigureAwait(false);
        _captureWorker.Dispose();
        _manualScreenshotCaptureGate.Dispose();
        _systemSnapshotGate.Dispose();
        await _snapshot.DisposeAsync().ConfigureAwait(false);
        _worldClockOperations.Dispose();
        _timesheetGate.Dispose();
        _mutations.Dispose();
    }

    private async Task<OperationResult<T>> MutateAsync<T>(Func<Task<OperationResult<T>>> operation, CancellationToken cancellationToken)
    {
        await _mutations.WaitAsync(cancellationToken);
        try
        {
            return await operation();
        }
        finally
        {
            _mutations.Release();
        }
    }

    private async Task<T> RunCancellableLiveWorkAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken)
    {
        CancellationTokenSource linked;
        CancellationToken policyToken;
        lock (_liveWorkLock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            policyToken = _liveWorkCancellation.Token;
            linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, policyToken);
            _liveWorkCount++;
        }
        try { return await AiPolicyCancellation.RunAsync(() => operation(linked.Token), policyToken).ConfigureAwait(false); }
        finally
        {
            linked.Dispose();
            lock (_liveWorkLock)
            {
                if (--_liveWorkCount == 0) _liveWorkDrained?.TrySetResult(true);
            }
        }
    }

    private void CancelLiveWork()
    {
        CancellationTokenSource previous;
        lock (_liveWorkLock)
        {
            previous = _liveWorkCancellation;
            _liveWorkCancellation = new CancellationTokenSource();
        }
        previous.Cancel();
        previous.Dispose();
    }

    private async Task<T> RunLiveAnalysisOutsideMutationAsync<T>(Func<Task<T>> operation, CancellationToken cancellationToken)
    {
        // Visual work retains its own serialization boundary. The global command gate is not held
        // while waiting for that boundary or the provider; stop/disable can persist policy immediately.
        _mutations.Release();
        try
        {
            return await _screenshotReprocessing.RunLiveAnalysisAsync(async () =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                var result = await operation().ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                return result;
            }, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            // Reacquire even after cancellation: outer cleanup and mutation release still own this lease.
            await _mutations.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        }
    }

    private async Task RecoverScreenshotDeletionsAsync(CancellationToken cancellationToken)
    {
        // Recovery failures remain retryable and visible, but cannot prevent the application from starting.
        void ReportFailure(Exception exception)
        {
            _logger.LogWarning("Screenshot deletion recovery failed. ExceptionType={ExceptionType}", exception.GetType().Name);
            EnqueueNotification(new ApplicationNotification(Guid.NewGuid(), DateTimeOffset.UtcNow,
                ApplicationNotificationSeverity.Error, "Notification.ScreenshotCaptureFailed.Title",
                "Notification.ScreenshotCaptureFailed.Message", "screenshot.deletion.recovery.failed", exception.GetType().Name));
        }
        IReadOnlyList<ScreenshotDeletionJournal.Plan> pending;
        try { pending = _screenshotDeletions.Pending(ReportFailure); }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException)
        {
            ReportFailure(exception);
            return;
        }
        if (pending.Count == 0) return;
        var completed = new List<ScreenshotDeletionJournal.Plan>();
        var batch = new ScreenshotDeletionJournal.Batch();
        foreach (var deletion in pending)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                _screenshotDeletions.Execute(deletion, batch, cancellationToken);
                completed.Add(deletion);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                ReportFailure(exception);
            }
        }
        if (completed.Count == 0) return;
        try
        {
            await _search.SynchronizeAsync(cancellationToken).ConfigureAwait(false);
            foreach (var deletion in completed) _screenshotDeletions.Complete(deletion);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            ReportFailure(exception);
        }
    }
}
