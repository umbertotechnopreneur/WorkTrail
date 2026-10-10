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
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using WorkTrail.Application;

namespace WorkTrail.Services;

/// <summary>
/// Owns short-lived historical screenshot plans and the single low-priority visual-analysis worker.
/// </summary>
internal sealed class AiScreenshotReprocessingService : IAsyncDisposable
{
    private static readonly TimeSpan PlanLifetime = TimeSpan.FromMinutes(5);
    private readonly LocalStore _store;
    private readonly IAiAnalysisService _analysis;
    private readonly Func<AppSettings, bool> _canAnalyzeImages;
    private readonly Func<AppSettings, AnalysisCostGate> _buildCostGate;
    private readonly Func<AppSettings, string, AnalysisContextSnapshot, bool> _isPrivate;
    private readonly Func<string, string> _canonicalizeModel;
    private readonly Func<Task>? _beforeWorkerHandoff;
    private readonly ILogger _logger;
    private readonly SemaphoreSlim _visualAnalysisGate = new(1, 1);
    private readonly CancellationTokenSource _shutdown = new();
    private readonly object _stateGate = new();
    private readonly object _workerGate = new();
    private readonly object _lifecycleGate = new();
    private readonly Dictionary<Guid, PlannedWork> _plans = new();
    private readonly ConcurrentDictionary<Guid, AiScreenshotReprocessJobSnapshot> _snapshots = new();
    private readonly ConcurrentDictionary<Guid, AiScreenshotReprocessCurrentItem> _currentItems = new();
    private Task? _workerTask;
    private Guid? _pendingWorkerJobId;
    private TaskCompletionSource<bool>? _priorityOperationsDrained;
    private int _activePriorityOperations;
    private int _priorityVisualWaiters;
    private bool _disposed;

    internal AiScreenshotReprocessingService(
        LocalStore store,
        IAiAnalysisService analysis,
        Func<AppSettings, bool> canAnalyzeImages,
        Func<AppSettings, AnalysisCostGate> buildCostGate,
        Func<AppSettings, string, AnalysisContextSnapshot, bool> isPrivate,
        Func<string, string>? canonicalizeModel = null,
        ILogger? logger = null,
        Func<Task>? beforeWorkerHandoff = null)
    {
        _store = store;
        _analysis = analysis;
        _canAnalyzeImages = canAnalyzeImages;
        _buildCostGate = buildCostGate;
        _isPrivate = isPrivate;
        _canonicalizeModel = canonicalizeModel ?? (model => model.Trim());
        _logger = logger ?? NullLogger.Instance;
        _beforeWorkerHandoff = beforeWorkerHandoff;

        var active = _store.LoadActiveAiReprocessJob();
        if (active is not null)
        {
            if (active.State is AiScreenshotReprocessJobStatuses.Running or AiScreenshotReprocessJobStatuses.PauseRequested)
            {
                // A request may have reached the provider during shutdown. Recovery pauses at the durable item boundary.
                _store.RecoverInterruptedAiReprocessJob(active.JobId, DateTimeOffset.UtcNow);
            }

            RefreshSnapshot(active.JobId);
        }
    }

    internal async Task<OperationResult<AiScreenshotReprocessPlan>> PreviewAsync(
        AiScreenshotReprocessRequest request,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        var (fromUtc, toUtc) = LocalStore.ConvertLocalDateRangeToUtc(request.Date);
        var candidates = await Task.Run(
            () => _store.ListAiReprocessCandidates(fromUtc, toUtc, cancellationToken),
            cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();

        var settings = _store.LoadSettings();
        var gate = _buildCostGate(settings);
        var remainingAllowance = Math.Max(0, settings.OpenAiDailyLimit - gate.DailyAnalysisCount);
        var missingCandidates = candidates.Where(candidate => !candidate.HasAiDescription).ToArray();
        var missingScreenshotCount = missingCandidates.Sum(candidate => candidate.ScreenshotPaths.Count + candidate.MissingFileCount);
        var missingFileCount = missingCandidates.Sum(candidate => candidate.MissingFileCount);
        var missingMetadata = missingCandidates.Count(candidate =>
            candidate.MissingFileCount == 0 &&
            (candidate.InstallationId is null ||
             candidate.HistoricalContext is null ||
             HasInsufficientMultiMonitorPrivacyContext(settings, candidate.ScreenshotPaths.Count)));
        var privacyBlocked = missingCandidates.Count(candidate =>
            candidate.MissingFileCount == 0 &&
            candidate.InstallationId is not null &&
            candidate.HistoricalContext is { } context &&
            !HasInsufficientMultiMonitorPrivacyContext(settings, candidate.ScreenshotPaths.Count) &&
            _isPrivate(settings, candidate.ProcessName, context));
        var eligible = missingCandidates
            .Where(candidate =>
                candidate.MissingFileCount == 0 &&
                candidate.InstallationId is not null &&
                candidate.HistoricalContext is { } context &&
                !HasInsufficientMultiMonitorPrivacyContext(settings, candidate.ScreenshotPaths.Count) &&
                !_isPrivate(settings, candidate.ProcessName, context))
            .OrderBy(candidate => candidate.CapturedAt)
            .ThenBy(candidate => candidate.CaptureId, StringComparer.Ordinal)
            .ToArray();
        var processable = eligible.Take(remainingAllowance).ToArray();
        var configured = settings.OpenAiEnabled && _canAnalyzeImages(settings);
        var activeJob = _store.LoadActiveAiReprocessJob();
        var blockingReason = activeJob is not null
            ? "job_active"
            : !settings.OpenAiEnabled
                ? "ai_disabled"
                : !configured
                    ? "ai_configuration_invalid"
                    : eligible.Length == 0
                        ? "no_eligible_captures"
                        : remainingAllowance == 0
                            ? "daily_quota"
                            : null;
        var now = DateTimeOffset.UtcNow;
        var planId = Guid.NewGuid();
        var plan = new AiScreenshotReprocessPlan(
            planId,
            now.Add(PlanLifetime),
            request.Date,
            missingScreenshotCount,
            missingCandidates.Length,
            eligible.Sum(candidate => candidate.ScreenshotPaths.Count),
            eligible.Length,
            missingFileCount,
            privacyBlocked,
            missingMetadata,
            gate.DailyAnalysisCount,
            settings.OpenAiDailyLimit,
            remainingAllowance,
            processable.Length,
            processable.Sum(candidate => candidate.ScreenshotPaths.Count),
            processable.Length * settings.EstimatedCostPerAnalysisUsd,
            settings.AiProvider,
            settings.Model,
            blockingReason is null,
            blockingReason,
            activeJob?.JobId);

        lock (_stateGate)
        {
            RemoveExpiredPlans(now);
            // Freeze at most today's available request allowance. Failed provider attempts may still be billable,
            // so the job never backfills from the larger eligible backlog during this run.
            _plans[planId] = new PlannedWork(plan, ConfigurationFingerprint(settings), fromUtc, toUtc, processable);
        }

        return OperationResult<AiScreenshotReprocessPlan>.Success(
            "ai.screenshot_reprocess.preview.ready",
            "AiScreenshotReprocessPreviewReady",
            plan);
    }

    internal async Task<OperationResult<AiScreenshotReprocessJobSnapshot>> StartAsync(
        Guid planId,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();
        // Durable plan creation may open SQLite and serialize many items; never perform it on a presentation caller.
        // The visual boundary also makes the allowance check authoritative against concurrent live analyses.
        return await RunExclusiveMutationAsync(
            () => Task.Run(() =>
            {
                AiScreenshotReprocessJobSnapshot snapshot;
                lock (_stateGate)
                {
                    RemoveExpiredPlans(DateTimeOffset.UtcNow);
                    if (!_plans.Remove(planId, out var planned))
                    {
                        return OperationResult<AiScreenshotReprocessJobSnapshot>.Failure(
                            "ai.screenshot_reprocess.plan.not_found",
                            "AiScreenshotReprocessPlanNotFound");
                    }

                    if (!planned.Plan.CanStart)
                    {
                        return OperationResult<AiScreenshotReprocessJobSnapshot>.Failure(
                            "ai.screenshot_reprocess.plan.blocked",
                            "AiScreenshotReprocessPlanBlocked");
                    }

                    var settings = _store.LoadSettings();
                    if (!string.Equals(planned.ConfigurationFingerprint, ConfigurationFingerprint(settings), StringComparison.Ordinal)
                        || !_canAnalyzeImages(settings))
                    {
                        return OperationResult<AiScreenshotReprocessJobSnapshot>.Failure(
                            "ai.screenshot_reprocess.configuration.changed",
                            "AiScreenshotReprocessConfigurationChanged");
                    }

                    if (_store.LoadActiveAiReprocessJob() is not null)
                    {
                        return OperationResult<AiScreenshotReprocessJobSnapshot>.Failure(
                            "ai.screenshot_reprocess.job.active",
                            "AiScreenshotReprocessJobActive");
                    }

                    var gate = _buildCostGate(settings);
                    var currentAllowance = Math.Max(0, settings.OpenAiDailyLimit - gate.DailyAnalysisCount);
                    if (currentAllowance < planned.Candidates.Count)
                    {
                        return OperationResult<AiScreenshotReprocessJobSnapshot>.Failure(
                            "ai.screenshot_reprocess.plan.stale",
                            "AiScreenshotReprocessPlanStale");
                    }

                    var candidates = planned.Candidates;

                    var now = DateTimeOffset.UtcNow;
                    var jobId = Guid.NewGuid();
                    var items = candidates
                        .Select((candidate, ordinal) => new AiReprocessJobItemRecord(
                            jobId,
                            candidate.CaptureId,
                            ordinal,
                            candidate.CapturedAt,
                            candidate.CaptureOrigin,
                            candidate.ScreenshotPaths
                                .Select(path => LocalStore.ScreenshotIdentity(Path.GetFileName(path)))
                                .ToArray(),
                            candidate.ScreenshotPaths.Count,
                            "pending",
                            0,
                            null,
                            now))
                        .ToArray();
                    var job = new AiReprocessJobRecord(
                        jobId,
                        now,
                        now,
                        planned.FromUtc,
                        planned.ToUtc,
                        planned.Plan.Date,
                        null,
                        planned.ConfigurationFingerprint,
                        AiScreenshotReprocessJobStatuses.Running,
                        items.Length,
                        items.Sum(item => item.ScreenshotCount),
                        null);
                    _store.CreateAiReprocessJob(job, items);
                    snapshot = BuildSnapshot(job, items);
                    _snapshots[jobId] = snapshot;
                    EnsureWorkerScheduled(jobId);
                }

                return OperationResult<AiScreenshotReprocessJobSnapshot>.Success(
                    "ai.screenshot_reprocess.started",
                    "AiScreenshotReprocessStarted",
                    snapshot);
            }, cancellationToken),
            cancellationToken).ConfigureAwait(false);
    }

    internal async Task<OperationResult<AiScreenshotReprocessJobSnapshot>> GetAsync(
        Guid jobId,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();
        if (_snapshots.TryGetValue(jobId, out var cached))
        {
            return OperationResult<AiScreenshotReprocessJobSnapshot>.Success(
                "ai.screenshot_reprocess.status.loaded",
                "AiScreenshotReprocessStatusLoaded",
                cached);
        }

        var snapshot = await Task.Run(() => RefreshSnapshot(jobId), cancellationToken).ConfigureAwait(false);
        return snapshot is null
            ? OperationResult<AiScreenshotReprocessJobSnapshot>.Failure(
                "ai.screenshot_reprocess.job.not_found",
                "AiScreenshotReprocessJobNotFound")
            : OperationResult<AiScreenshotReprocessJobSnapshot>.Success(
                "ai.screenshot_reprocess.status.loaded",
                "AiScreenshotReprocessStatusLoaded",
                snapshot);
    }

    internal Task<OperationResult<AiScreenshotReprocessJobSnapshot>> PauseAsync(
        Guid jobId,
        CancellationToken cancellationToken)
    {
        lock (_lifecycleGate)
        {
            ThrowIfDisposed();
            cancellationToken.ThrowIfCancellationRequested();
            AiScreenshotReprocessJobSnapshot? snapshot;
            lock (_stateGate)
            {
                var job = _store.LoadAiReprocessJob(jobId);
                if (job is null)
                {
                    return Task.FromResult(OperationResult<AiScreenshotReprocessJobSnapshot>.Failure(
                        "ai.screenshot_reprocess.job.not_found",
                        "AiScreenshotReprocessJobNotFound"));
                }

                if (job.State == AiScreenshotReprocessJobStatuses.Running)
                {
                    _store.TransitionAiReprocessJob(
                        jobId,
                        AiScreenshotReprocessJobStatuses.PauseRequested,
                        "user",
                        DateTimeOffset.UtcNow);
                }

                snapshot = RefreshSnapshot(jobId);
            }

            return Task.FromResult(OperationResult<AiScreenshotReprocessJobSnapshot>.Success(
                "ai.screenshot_reprocess.pause.requested",
                "AiScreenshotReprocessPauseRequested",
                snapshot!));
        }
    }

    internal Task<OperationResult<AiScreenshotReprocessJobSnapshot>> ResumeAsync(
        Guid jobId,
        CancellationToken cancellationToken)
    {
        lock (_lifecycleGate)
        {
            ThrowIfDisposed();
            cancellationToken.ThrowIfCancellationRequested();
            AiScreenshotReprocessJobSnapshot? snapshot;
            lock (_stateGate)
            {
                var job = _store.LoadAiReprocessJob(jobId);
                if (job is null)
                {
                    return Task.FromResult(OperationResult<AiScreenshotReprocessJobSnapshot>.Failure(
                        "ai.screenshot_reprocess.job.not_found",
                        "AiScreenshotReprocessJobNotFound"));
                }

                if (job.State is not (AiScreenshotReprocessJobStatuses.PausedByUser or AiScreenshotReprocessJobStatuses.PausedDailyQuota))
                {
                    return Task.FromResult(OperationResult<AiScreenshotReprocessJobSnapshot>.Failure(
                        "ai.screenshot_reprocess.resume.invalid_state",
                        "AiScreenshotReprocessResumeInvalidState"));
                }

                var settings = _store.LoadSettings();
                if (!string.Equals(job.ConfigurationFingerprint, ConfigurationFingerprint(settings), StringComparison.Ordinal)
                    || !_canAnalyzeImages(settings))
                {
                    return Task.FromResult(OperationResult<AiScreenshotReprocessJobSnapshot>.Failure(
                        "ai.screenshot_reprocess.configuration.changed",
                        "AiScreenshotReprocessConfigurationChanged"));
                }

                if (!_buildCostGate(settings).Allowed)
                {
                    return Task.FromResult(OperationResult<AiScreenshotReprocessJobSnapshot>.Failure(
                        "ai.screenshot_reprocess.daily_quota",
                        "AiScreenshotReprocessDailyQuota"));
                }

                _store.TransitionAiReprocessJob(
                    jobId,
                    AiScreenshotReprocessJobStatuses.Running,
                    null,
                    DateTimeOffset.UtcNow);
                snapshot = RefreshSnapshot(jobId);
                EnsureWorkerScheduled(jobId);
            }

            return Task.FromResult(OperationResult<AiScreenshotReprocessJobSnapshot>.Success(
                "ai.screenshot_reprocess.resumed",
                "AiScreenshotReprocessResumed",
                snapshot!));
        }
    }

    internal Task<T> RunLiveAnalysisAsync<T>(Func<Task<T>> operation, CancellationToken cancellationToken) =>
        RunPriorityVisualOperationAsync(operation, enforceDailyQuota: true, cancellationToken);

    /// <summary>
    /// Runs a settings, privacy, retention, or artifact mutation at the same exclusive visual boundary as AI requests.
    /// </summary>
    internal Task<T> RunExclusiveMutationAsync<T>(Func<Task<T>> operation, CancellationToken cancellationToken) =>
        RunPriorityVisualOperationAsync(operation, enforceDailyQuota: false, cancellationToken);

    private async Task<T> RunPriorityVisualOperationAsync<T>(
        Func<Task<T>> operation,
        bool enforceDailyQuota,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation);
        lock (_lifecycleGate)
        {
            ThrowIfDisposed();
            _activePriorityOperations++;
        }

        var acquired = false;
        try
        {
            Interlocked.Increment(ref _priorityVisualWaiters);
            try
            {
                await _visualAnalysisGate.WaitAsync(cancellationToken).ConfigureAwait(false);
                acquired = true;
            }
            finally
            {
                Interlocked.Decrement(ref _priorityVisualWaiters);
            }

            // The early facade preflight avoids unnecessary capture work; this authoritative check closes
            // the race with the batch that may have consumed the last daily slot while this call waited.
            if (enforceDailyQuota && !_buildCostGate(_store.LoadSettings()).Allowed)
            {
                throw new AiDailyAnalysisQuotaReachedException();
            }

            return await operation().ConfigureAwait(false);
        }
        finally
        {
            if (acquired)
            {
                _visualAnalysisGate.Release();
            }

            lock (_lifecycleGate)
            {
                _activePriorityOperations--;
                if (_activePriorityOperations == 0)
                {
                    _priorityOperationsDrained?.TrySetResult(true);
                }
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        Task liveOperationsDrained;
        lock (_lifecycleGate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            if (_activePriorityOperations == 0)
            {
                liveOperationsDrained = Task.CompletedTask;
            }
            else
            {
                _priorityOperationsDrained = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                liveOperationsDrained = _priorityOperationsDrained.Task;
            }
        }

        _shutdown.Cancel();
        Task? worker;
        lock (_workerGate)
        {
            worker = _workerTask;
        }

        if (worker is not null)
        {
            try
            {
                await worker.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Shutdown cancellation leaves a running checkpoint for deterministic recovery on next launch.
            }
        }

        await liveOperationsDrained.ConfigureAwait(false);

        _shutdown.Dispose();
        _visualAnalysisGate.Dispose();
    }

    private void EnsureWorkerScheduled(Guid jobId)
    {
        lock (_workerGate)
        {
            if (_shutdown.IsCancellationRequested)
            {
                return;
            }

            _pendingWorkerJobId = jobId;
            if (_workerTask is { IsCompleted: false })
            {
                return;
            }

            var shutdownToken = _shutdown.Token;
            _workerTask = Task.Run(() => RunWorkerAsync(shutdownToken));
        }
    }

    private async Task RunWorkerAsync(CancellationToken shutdownToken)
    {
        Guid jobId;
        lock (_workerGate)
        {
            if (shutdownToken.IsCancellationRequested || _pendingWorkerJobId is not { } pendingJobId)
            {
                _pendingWorkerJobId = null;
                _workerTask = null;
                return;
            }

            jobId = pendingJobId;
            _pendingWorkerJobId = null;
        }

        while (true)
        {
            await RunWorkerPassAsync(jobId, shutdownToken).ConfigureAwait(false);
            var continueCurrentJob = false;
            if (!shutdownToken.IsCancellationRequested)
            {
                // This durable check remains inside the task owned by DisposeAsync. The handoff
                // below then atomically consumes any Start/Resume request that raced with it.
                continueCurrentJob = _store.LoadAiReprocessJob(jobId)?.State == AiScreenshotReprocessJobStatuses.Running;
                if (_beforeWorkerHandoff is not null)
                {
                    await _beforeWorkerHandoff().ConfigureAwait(false);
                }
            }

            lock (_workerGate)
            {
                if (shutdownToken.IsCancellationRequested)
                {
                    _pendingWorkerJobId = null;
                    _workerTask = null;
                    return;
                }

                if (_pendingWorkerJobId is { } pendingJobId)
                {
                    jobId = pendingJobId;
                    _pendingWorkerJobId = null;
                    continue;
                }

                if (continueCurrentJob)
                {
                    continue;
                }

                // Publish inactivity only after the final store access, under the same lock used
                // by EnsureWorkerScheduled, so a concurrent request either hands off or starts a task.
                _workerTask = null;
                return;
            }
        }
    }

    private async Task RunWorkerPassAsync(Guid jobId, CancellationToken shutdownToken)
    {
        try
        {
            while (!shutdownToken.IsCancellationRequested)
            {
                AiReprocessJobRecord? job;
                AiReprocessJobItemRecord? item;
                lock (_stateGate)
                {
                    job = _store.LoadAiReprocessJob(jobId);
                    if (job is null || IsTerminal(job.State) || job.State is AiScreenshotReprocessJobStatuses.PausedByUser or AiScreenshotReprocessJobStatuses.PausedDailyQuota)
                    {
                        return;
                    }

                    if (job.State == AiScreenshotReprocessJobStatuses.PauseRequested)
                    {
                        _store.TransitionAiReprocessJob(
                            jobId,
                            AiScreenshotReprocessJobStatuses.PausedByUser,
                            "user",
                            DateTimeOffset.UtcNow);
                        RefreshSnapshot(jobId);
                        return;
                    }

                    item = _store.LoadNextAiReprocessItem(jobId);
                    if (item is null)
                    {
                        CompleteJob(jobId);
                        return;
                    }
                }

                while (Volatile.Read(ref _priorityVisualWaiters) > 0)
                {
                    await Task.Delay(25, shutdownToken).ConfigureAwait(false);
                }

                await _visualAnalysisGate.WaitAsync(shutdownToken).ConfigureAwait(false);
                try
                {
                    if (!IsJobRunning(jobId))
                    {
                        continue;
                    }

                    await ProcessItemAsync(item!, shutdownToken).ConfigureAwait(false);
                }
                finally
                {
                    _visualAnalysisGate.Release();
                }
            }
        }
        catch (OperationCanceledException) when (shutdownToken.IsCancellationRequested)
        {
            // The provider call is cancelled only for runtime shutdown; recovery will reset the durable running item.
        }
        catch (Exception exception)
        {
            _logger.LogError(
                "Historical screenshot worker failed. Job={JobId} ExceptionType={ExceptionType}",
                jobId.ToString("N"),
                exception.GetType().Name);
            lock (_stateGate)
            {
                if (_store.LoadAiReprocessJob(jobId) is { } job && !IsTerminal(job.State))
                {
                    _store.TransitionAiReprocessJob(
                        jobId,
                        AiScreenshotReprocessJobStatuses.Failed,
                        "worker_failed",
                        DateTimeOffset.UtcNow);
                    RefreshSnapshot(jobId);
                }
            }
        }
    }

    private async Task ProcessItemAsync(
        AiReprocessJobItemRecord item,
        CancellationToken shutdownToken)
    {
        var candidate = await Task.Run(
            () => _store.LoadAiReprocessCandidate(item.CaptureId, shutdownToken),
            shutdownToken).ConfigureAwait(false);
        if (candidate is null ||
            candidate.MissingFileCount > 0 ||
            candidate.ScreenshotPaths.Count != item.ScreenshotCount ||
            !MatchesArtifactIdentities(item.ArtifactIdentities, candidate.ScreenshotPaths))
        {
            CheckpointItem(
                item,
                "skipped",
                candidate is not null && candidate.MissingFileCount == 0
                    ? "artifact_identity_changed"
                    : "screenshot_missing",
                incrementAttempt: false);
            return;
        }

        if (candidate.HasAiDescription || _store.HasAiDescription(item.CaptureId))
        {
            CheckpointItem(item, "skipped", "already_described", incrementAttempt: false);
            return;
        }

        if (candidate.InstallationId is null || candidate.HistoricalContext is not { } context)
        {
            CheckpointItem(item, "skipped", "privacy_context_unavailable", incrementAttempt: false);
            return;
        }

        var attemptCount = checked(item.AttemptCount + 1);
        var capture = new ScreenshotCaptureResult(
            item.CaptureId,
            candidate.ScreenshotPaths,
            candidate.ScreenshotPaths,
            item.CaptureOrigin,
            candidate.TextSnapshots);
        var attemptStarted = false;
        try
        {
            Task<AiAnalysis> analysisTask;
            lock (_stateGate)
            {
                var currentJob = _store.LoadAiReprocessJob(item.JobId);
                if (currentJob?.State == AiScreenshotReprocessJobStatuses.PauseRequested)
                {
                    _store.TransitionAiReprocessJob(
                        item.JobId,
                        AiScreenshotReprocessJobStatuses.PausedByUser,
                        "user",
                        DateTimeOffset.UtcNow);
                    RefreshSnapshot(item.JobId);
                    return;
                }

                if (currentJob?.State != AiScreenshotReprocessJobStatuses.Running)
                {
                    return;
                }

                var settings = _store.LoadSettings();
                if (!string.Equals(currentJob.ConfigurationFingerprint, ConfigurationFingerprint(settings), StringComparison.Ordinal)
                    || !_canAnalyzeImages(settings))
                {
                    _store.TransitionAiReprocessJob(
                        item.JobId,
                        AiScreenshotReprocessJobStatuses.Failed,
                        "configuration_changed",
                        DateTimeOffset.UtcNow);
                    RefreshSnapshot(item.JobId);
                    return;
                }

                if (HasInsufficientMultiMonitorPrivacyContext(settings, item.ScreenshotCount))
                {
                    _store.TransitionAiReprocessItem(
                        item.JobId,
                        item.CaptureId,
                        "skipped",
                        item.AttemptCount,
                        "privacy_context_unavailable",
                        DateTimeOffset.UtcNow);
                    RefreshSnapshot(item.JobId);
                    return;
                }

                if (_isPrivate(settings, candidate.ProcessName, context))
                {
                    _store.TransitionAiReprocessItem(
                        item.JobId,
                        item.CaptureId,
                        "skipped",
                        item.AttemptCount,
                        "privacy_blocked",
                        DateTimeOffset.UtcNow);
                    RefreshSnapshot(item.JobId);
                    return;
                }

                // This check is authoritative and runs while the shared visual gate is held. The frozen job
                // also caps attempts to the allowance shown in preview because failed calls may still be billable.
                if (!_buildCostGate(settings).Allowed)
                {
                    _store.TransitionAiReprocessJob(
                        item.JobId,
                        AiScreenshotReprocessJobStatuses.PausedDailyQuota,
                        "daily_quota",
                        DateTimeOffset.UtcNow);
                    RefreshSnapshot(item.JobId);
                    return;
                }

                _store.TransitionAiReprocessItem(
                    item.JobId,
                    item.CaptureId,
                    "running",
                    attemptCount,
                    null,
                    DateTimeOffset.UtcNow);
                _currentItems[item.JobId] = new AiScreenshotReprocessCurrentItem(
                    item.Ordinal + 1,
                    item.CapturedAt,
                    item.CaptureOrigin,
                    context.Application,
                    item.ScreenshotCount);
                attemptStarted = true;
                RefreshSnapshot(item.JobId);
            }

            // The durable running transition above is the cooperative pause boundary. Provider implementations
            // are invoked outside the monitor so synchronous validation cannot block status and pause requests.
            analysisTask = _analysis.AnalyzeHistoricalCapturedScreenAsync(
                context,
                capture,
                "snapshot.reprocess",
                shutdownToken);
            _ = await analysisTask.ConfigureAwait(false);
            CheckpointItem(item with { AttemptCount = attemptCount }, "succeeded", "ai.analyzed", incrementAttempt: false);
        }
        catch (OperationCanceledException) when (shutdownToken.IsCancellationRequested)
        {
            throw;
        }
        catch (AiProviderRequestException exception) when (attemptStarted)
        {
            _logger.LogWarning(
                "Historical screenshot analysis failed. Job={JobId} Capture={Capture} FailureCategory={FailureCategory} HttpStatus={HttpStatus}",
                item.JobId.ToString("N"),
                SafeToken(item.CaptureId),
                exception.Failure.FailureCode,
                exception.Failure.HttpStatusCode);
            CheckpointItem(item with { AttemptCount = attemptCount }, "failed", "ai.provider.failed", incrementAttempt: false);
        }
        catch (InvalidOperationException) when (attemptStarted)
        {
            CheckpointItem(item with { AttemptCount = attemptCount }, "failed", "ai.configuration.invalid", incrementAttempt: false);
        }
        catch (Exception exception) when (attemptStarted)
        {
            _logger.LogWarning(
                "Historical screenshot analysis failed. Job={JobId} Capture={Capture} ExceptionType={ExceptionType}",
                item.JobId.ToString("N"),
                SafeToken(item.CaptureId),
                exception.GetType().Name);
            CheckpointItem(item with { AttemptCount = attemptCount }, "failed", "ai.provider.failed", incrementAttempt: false);
        }
        finally
        {
            _currentItems.TryRemove(item.JobId, out _);
            RefreshSnapshot(item.JobId);
        }
    }

    private void CheckpointItem(
        AiReprocessJobItemRecord item,
        string state,
        string code,
        bool incrementAttempt)
    {
        lock (_stateGate)
        {
            _store.TransitionAiReprocessItem(
                item.JobId,
                item.CaptureId,
                state,
                incrementAttempt ? checked(item.AttemptCount + 1) : item.AttemptCount,
                code,
                DateTimeOffset.UtcNow);
            RefreshSnapshot(item.JobId);
        }
    }

    private bool IsJobRunning(Guid jobId)
    {
        lock (_stateGate)
        {
            var job = _store.LoadAiReprocessJob(jobId);
            if (job?.State == AiScreenshotReprocessJobStatuses.PauseRequested)
            {
                _store.TransitionAiReprocessJob(
                    jobId,
                    AiScreenshotReprocessJobStatuses.PausedByUser,
                    "user",
                    DateTimeOffset.UtcNow);
                RefreshSnapshot(jobId);
                return false;
            }

            return job?.State == AiScreenshotReprocessJobStatuses.Running;
        }
    }

    private void CompleteJob(Guid jobId)
    {
        var items = _store.ListAiReprocessJobItems(jobId);
        var state = items.Any(item => item.State is "failed" or "skipped")
            ? AiScreenshotReprocessJobStatuses.CompletedWithErrors
            : AiScreenshotReprocessJobStatuses.Completed;
        _store.TransitionAiReprocessJob(jobId, state, null, DateTimeOffset.UtcNow);
        _currentItems.TryRemove(jobId, out _);
        RefreshSnapshot(jobId);
    }

    private AiScreenshotReprocessJobSnapshot? RefreshSnapshot(Guid jobId)
    {
        var job = _store.LoadAiReprocessJob(jobId);
        if (job is null)
        {
            return null;
        }

        var snapshot = BuildSnapshot(job, _store.ListAiReprocessJobItems(jobId));
        _snapshots[jobId] = snapshot;
        return snapshot;
    }

    private AiScreenshotReprocessJobSnapshot BuildSnapshot(
        AiReprocessJobRecord job,
        IReadOnlyList<AiReprocessJobItemRecord> items)
    {
        static bool Completed(AiReprocessJobItemRecord item) => item.State is "succeeded" or "skipped" or "failed";

        var completed = items.Where(Completed).ToArray();
        var succeeded = items.Where(item => item.State == "succeeded").ToArray();
        var skipped = items.Where(item => item.State == "skipped").ToArray();
        var failed = items.Where(item => item.State == "failed").ToArray();
        _currentItems.TryGetValue(job.JobId, out var current);
        var completedScreenshots = completed.Sum(item => item.ScreenshotCount);
        return new AiScreenshotReprocessJobSnapshot(
            job.JobId,
            job.SelectedDate,
            job.State,
            job.TotalCaptures,
            job.TotalScreenshots,
            completed.Length,
            completedScreenshots,
            Math.Max(0, job.TotalCaptures - completed.Length),
            Math.Max(0, job.TotalScreenshots - completedScreenshots),
            succeeded.Length,
            succeeded.Sum(item => item.ScreenshotCount),
            skipped.Length,
            skipped.Sum(item => item.ScreenshotCount),
            failed.Length,
            failed.Sum(item => item.ScreenshotCount),
            current,
            job.PauseReason,
            items.Count == 0 ? job.UpdatedAt : items.Max(item => item.UpdatedAt > job.UpdatedAt ? item.UpdatedAt : job.UpdatedAt));
    }

    private void RemoveExpiredPlans(DateTimeOffset now)
    {
        foreach (var expired in _plans.Where(entry => entry.Value.Plan.ExpiresAt <= now).Select(entry => entry.Key).ToArray())
        {
            _plans.Remove(expired);
        }
    }

    private string ConfigurationFingerprint(AppSettings settings)
    {
        var model = string.Equals(settings.AiProvider, "openai", StringComparison.OrdinalIgnoreCase)
            ? _canonicalizeModel(settings.Model)
            : settings.Model.Trim();
        var source = string.Join(
            '\n',
            settings.AiProvider.Trim().ToLowerInvariant(),
            model,
            settings.AiEndpoint.Trim(),
            settings.AiApiKeyName.Trim(),
            settings.AiOutputDetail.Trim().ToLowerInvariant(),
            settings.AiReasoningEffort.Trim().ToLowerInvariant(),
            settings.AiCustomPrompt,
            CanonicalDirectory(settings.ScreenshotDirectory));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source)));
    }

    private static string CanonicalDirectory(string path) =>
        Path.GetFullPath(path)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .ToUpperInvariant();

    private static bool MatchesArtifactIdentities(
        IReadOnlyList<string> expectedIdentities,
        IReadOnlyList<string> currentPaths) =>
        expectedIdentities.Count == currentPaths.Count &&
        expectedIdentities
            .OrderBy(identity => identity, StringComparer.OrdinalIgnoreCase)
            .SequenceEqual(
                currentPaths
                    .Select(path => LocalStore.ScreenshotIdentity(Path.GetFileName(path)))
                    .OrderBy(identity => identity, StringComparer.OrdinalIgnoreCase),
                StringComparer.OrdinalIgnoreCase);

    private static bool IsTerminal(string state) =>
        state is AiScreenshotReprocessJobStatuses.Completed or
            AiScreenshotReprocessJobStatuses.CompletedWithErrors or
            AiScreenshotReprocessJobStatuses.Failed;

    private static bool HasInsufficientMultiMonitorPrivacyContext(AppSettings settings, int screenshotCount) =>
        screenshotCount > 1 &&
        (!string.IsNullOrWhiteSpace(settings.PrivacyProcessNames) ||
         !string.IsNullOrWhiteSpace(settings.PrivacyWindowTitles) ||
         !string.IsNullOrWhiteSpace(settings.PrivacyWindowHints));

    private static string SafeToken(string value) => value[..Math.Min(12, value.Length)];

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    private sealed record PlannedWork(
        AiScreenshotReprocessPlan Plan,
        string ConfigurationFingerprint,
        DateTimeOffset FromUtc,
        DateTimeOffset ToUtc,
        IReadOnlyList<AiScreenshotReprocessCandidate> Candidates);
}

/// <summary>Signals that the authoritative post-gate daily quota check rejected a visual request.</summary>
internal sealed class AiDailyAnalysisQuotaReachedException : InvalidOperationException
{
}
