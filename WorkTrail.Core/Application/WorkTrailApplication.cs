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
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using WorkTrail.Runtime;
using WorkTrail.Search;
using WorkTrail.Services;

namespace WorkTrail.Application;

/// <summary>Creates the application facade and keeps concrete infrastructure construction in one composition boundary.</summary>
public static class WorkTrailApplicationFactory
{
    /// <summary>
    /// Loads only the stable installation identity needed to arbitrate runtime ownership.
    /// </summary>
    public static string LoadInstallationId() => new LocalStore().LoadSettings().InstallationId;

    /// <summary>Creates the in-process runtime application used by the Windows host.</summary>
    public static IWorkTrailApplication Create(ILoggerFactory? loggerFactory = null, ObservabilityHealth? observability = null)
    {
        var logger = loggerFactory?.CreateLogger<WorkTrailApplication>() ?? NullLogger<WorkTrailApplication>.Instance;
        var utilities = new UtilityService();
        var store = new LocalStore();
        var settings = store.LoadSettings();
        var settingsSnapshot = new SettingsSnapshot(settings);
        var tracking = new TrackingDomainService(store, settingsSnapshot);
        var capture = new ScreenCaptureService(utilities.GetAppVersion(), settingsSnapshot);
        var deviceContext = new DeviceContextService();
        var buildInformation = new BuildInformationService();
        var aiModelCatalog = AiModelCatalog.LoadDefault();
        var fileShare = new WindowsFileShareService();
        var localSearch = CreateLocalSearchService(store);
        var analysis = new OpenAiAnalysisService(
            store,
            capture,
            logger: loggerFactory?.CreateLogger<OpenAiAnalysisService>());
        return new WorkTrailApplication(
            store,
            utilities,
            tracking,
            capture,
            analysis,
            new StartupService(),
            buildInformation,
            logger,
            observability,
            deviceContext,
            new ScreenshotShareService(fileShare),
            new WindowStateService(store),
            aiModelCatalog,
            new ApplicationLogService(fileShare: fileShare),
            localSearch,
            settingsSnapshot: settingsSnapshot,
            screenshotNotifications: new ScreenshotNotificationService(settingsSnapshot,
                loggerFactory?.CreateLogger<ScreenshotNotificationService>() ?? NullLogger<ScreenshotNotificationService>.Instance),
            worldClockService: new WorldClockService(
                logger: loggerFactory?.CreateLogger<WorldClockService>()));
    }

    private static ILocalSearchService CreateLocalSearchService(LocalStore store)
    {
        var options = new SearchOptions
        {
            IndexRootPath = store.SearchIndexRootDirectory,
            SynonymSets = SearchSynonymConfiguration.Load(Path.Combine(AppContext.BaseDirectory, "search-synonyms.json"))
        };
        try
        {
            return new LocalSearchService(options);
        }
        catch (InvalidDataException)
        {
            // The constructor releases all Lucene handles before reporting an invalid derived index.
            // Delete only the versioned index, mark the source projection stale, and retry once.
            var indexPath = Path.Combine(options.IndexRootPath, LocalSearchService.IndexDirectoryName);
            Directory.Delete(indexPath, recursive: true);
            store.MarkSearchSourceRebuild("invalid-lucene-index");
            return new LocalSearchService(options);
        }
    }
}

/// <summary>Implements UI-independent use cases over the existing local infrastructure services.</summary>
public sealed partial class WorkTrailApplication : IWorkTrailApplication
{
    private readonly AiConnectionVerification _aiConnectionVerification = new();
    private readonly WindowSnappingService _windowSnapping = new();
    private RetentionCleanupProgress? _retentionCleanupProgress;

    /// <inheritdoc />
    public IWindowSnappingRegistration RegisterWindowSnapping(long windowHandle, Action<Exception> reportFailure) =>
        _windowSnapping.Register(windowHandle, reportFailure);

    /// <inheritdoc />
    public void ConfigureWindowSnapping(bool enabled) => _windowSnapping.Configure(enabled);

    private readonly LocalStore _store;
    private readonly SettingsSnapshot _settingsSnapshot;
    private readonly UtilityService _utilities;
    private readonly TrackingDomainService _tracking;
    private readonly IScreenCaptureService _capture;
    private readonly IScreenshotNotificationService? _screenshotNotifications;
    private readonly DeviceContextService _deviceContext;
    private readonly IAiAnalysisService _analysis;
    private readonly LocalSearchCoordinator _search;
    private readonly ScreenshotDeletionJournal _screenshotDeletions;
    private readonly ScreenshotShareService _screenshotShare;
    private readonly ApplicationLogService _applicationLogs;
    private readonly WindowStateService _windowState;
    private readonly StartupService _startup;
    private readonly BuildInformationService _buildInformation;
    private readonly AiModelCatalogSnapshot _aiModelCatalog;
    private readonly ReportAggregationService _reports;
    private readonly AtomicResetService _atomicReset;
    private readonly AiScreenshotReprocessingService _screenshotReprocessing;
    private readonly DataArchiveService _archives;
    private readonly DataArchiveProgressRegistry _archiveProgress = new();
    private readonly WorldClockApplicationService _worldClockOperations;
    private readonly CelestialMapService _celestialMap = new();
    private readonly ILogger<WorkTrailApplication> _logger;
    private readonly ObservabilityHealth _observability;
    private readonly SemaphoreSlim _mutations = new(1, 1);
    private readonly FeatureAccessPolicy _featureAccess;
    private readonly object _liveWorkLock = new();
    private CancellationTokenSource _liveWorkCancellation = new();
    private int _liveWorkCount;
    private TaskCompletionSource<bool>? _liveWorkDrained;
    private readonly SemaphoreSlim _captureWorker = new(1, 1);
    private readonly SemaphoreSlim _manualScreenshotCaptureGate = new(1, 1);
    private readonly ConcurrentQueue<ApplicationNotification> _notifications = new();
    private readonly CancellationTokenSource _runtimeTimerCancellation = new();
    private readonly Task _runtimeTimerTask;
    private DateTimeOffset? _nextScheduledSnapshotAt;
    private TimeSpan? _pausedScheduledSnapshotRemaining;
    private PendingManualScreenshotRegistration? _pendingManualScreenshot;
    private int _scheduledSnapshotIntervalMinutes;
    private bool _scheduledSnapshotsEnabled;
    private bool _atomicResetPrepared;
    private int _lastAiDailyLimitNotificationDateStamp;
    private DateTimeOffset _lastScreenshotStorageWarningAt = DateTimeOffset.MinValue;
    private const int ManualScreenshotDeletionWindowSeconds = 30;
    private const int MaximumPendingNotifications = 32;
    private const int MaximumScreenshotImageBytes = 10 * 1024 * 1024;
    private const long MinimumScreenshotFreeBytes = 512L * 1024 * 1024;
    private static readonly TimeSpan ScreenshotStorageNotificationInterval = TimeSpan.FromMinutes(30);
    private const string ProductRepositoryUrl = "https://github.com/umbertotechnopreneur/WorkTrail";
    private const string ProductIssuesUrl = "https://github.com/umbertotechnopreneur/WorkTrail/issues";
    private const string ProductAuthorUrl = "https://umbertogiacobbi.biz/?utm_source=worktrail_app&utm_medium=referral&utm_campaign=worktrail&utm_content=about_author";
    private const string ProductPrivacyUrl = "https://umbertogiacobbi.biz/privacy/";
    private const string ProductTermsUrl = "https://umbertogiacobbi.biz/terms/";
    private const string OpenWeatherUrl = "https://openweathermap.org/";
    private bool _disposed;

    /// <summary>Initializes an application facade with infrastructure owned by the runtime host.</summary>
    public WorkTrailApplication(
        LocalStore store,
        UtilityService utilities,
        TrackingDomainService tracking,
        IScreenCaptureService capture,
        IAiAnalysisService analysis,
        StartupService startup,
        BuildInformationService buildInformation,
        ILogger<WorkTrailApplication>? logger = null,
        ObservabilityHealth? observability = null,
        DeviceContextService? deviceContext = null,
        ScreenshotShareService? screenshotShare = null,
        WindowStateService? windowState = null,
        AiModelCatalog? aiModelCatalog = null,
        ApplicationLogService? applicationLogs = null,
        ILocalSearchService? localSearch = null,
        AtomicResetService? atomicResetService = null,
        SettingsSnapshot? settingsSnapshot = null,
        WorldClockService? worldClockService = null,
        bool startScheduledSnapshotTimer = true,
        IFeatureLicenseSource? featureLicenseSource = null,
        IScreenshotNotificationService? screenshotNotifications = null)
    {
        _store = store;
        _timesheetService = new TimesheetBatchService(store);
        _settingsSnapshot = settingsSnapshot ?? new SettingsSnapshot(store.LoadSettings());
        _featureAccess = new FeatureAccessPolicy(featureLicenseSource);
        _ = _featureAccess.Snapshot;
        _utilities = utilities;
        _tracking = tracking;
        _capture = capture;
        _screenshotNotifications = screenshotNotifications;
        _deviceContext = deviceContext ?? new DeviceContextService();
        _screenshotShare = screenshotShare ?? new ScreenshotShareService();
        _applicationLogs = applicationLogs ?? new ApplicationLogService();
        _windowState = windowState ?? new WindowStateService(store);
        _analysis = analysis;
        _logger = logger ?? NullLogger<WorkTrailApplication>.Instance;
        _search = new LocalSearchCoordinator(
            store,
            localSearch ?? new LocalSearchService(new SearchOptions { IndexRootPath = store.SearchIndexRootDirectory }),
            _settingsSnapshot,
            logger);
        _screenshotDeletions = new ScreenshotDeletionJournal(store);
        _screenshotPublications = new ScreenshotPublicationJournal(store);
        RecoverScreenshotPublications();
        RecoverScreenshotDeletionsAsync(CancellationToken.None).GetAwaiter().GetResult();
        _startup = startup;
        _buildInformation = buildInformation;
        _aiModelCatalog = (aiModelCatalog ?? AiModelCatalog.LoadDefault()).Snapshot;
        _reports = new ReportAggregationService(store);
        _atomicReset = atomicResetService ?? new AtomicResetService();
        _screenshotReprocessing = new AiScreenshotReprocessingService(
            store,
            analysis,
            CanAnalyzeHistoricalImages,
            BuildRequestGate,
            TrackingDomainService.IsHistoricalContextPrivate,
            model => ResolveAiModel(model)?.Key ?? model.Trim(),
            _logger);
        _archives = new DataArchiveService(store, _logger);
        _worldClockOperations = new WorldClockApplicationService(
            worldClockService ?? new WorldClockService(),
            _settingsSnapshot,
            _utilities.SetApiKey,
            PersistSettings);
        _observability = observability ?? new ObservabilityHealth(false, false, "unknown", false);
        _tracking.DashboardStateChanged += OnDashboardStateChanged;
        _tracking.TrackingStateChanged += OnTrackingStateChanged;
        _tracking.RuntimeHealthChanged += OnTrackingRuntimeHealthChanged;
        ConfigureScheduledSnapshots(_settingsSnapshot.Value, restartCountdown: true);
        _runtimeTimerTask = startScheduledSnapshotTimer
            ? RunRuntimeTimerLoopAsync(_runtimeTimerCancellation.Token)
            : Task.CompletedTask;
        _timesheetRecoveryTask = startScheduledSnapshotTimer
            ? Task.Run(() => RunTimesheetRecoveryLoopAsync(_runtimeTimerCancellation.Token))
            : Task.CompletedTask;
        _search.Start();
        _logger.LogInformation("Application facade initialized.");
    }

    /// <inheritdoc />
    public event EventHandler<RuntimeStateChangedEventArgs>? RuntimeStateChanged;

    /// <inheritdoc />
    public Task<OperationResult<RuntimeHealth>> GetRuntimeHealthAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var installationFingerprint = RuntimeProtocol.CreateEndpoint(_settingsSnapshot.Value.InstallationId)
            .PipeName["WorkTrail.Runtime.".Length..];
        var health = new RuntimeHealth(
            _utilities.GetAppVersion(),
            RuntimeProtocol.ProtocolVersion,
            installationFingerprint,
            true,
            ["tracking", "tracking.health.v1", "sessions", "screenshots", "screenshots.image.v1", "screenshots.save", "screenshots.share", "screenshots.delete", "screenshots.analysis.delete.v1", "screenshots.analyze", "screenshots.reprocess.v1", "installations.v1", "archive.v1", "search", "search.rebuild.v1", "notifications", "window.state", "ai", "ai.models", "reports", "reports.query.v1", "privacy", "retention", "app.atomic-reset.v1", "plugins", "settings", "quick-setup", "startup", "links", "observability", "diagnostics.logs"],
            _tracking.RuntimeHealth,
            _observability);
        return Task.FromResult(OperationResult<RuntimeHealth>.Success("runtime.healthy", "RuntimeHealthy", health));
    }

    /// <inheritdoc />
    public Task<OperationResult<DashboardState>> StartTrackingAsync(StartTrackingRequest request, CancellationToken cancellationToken) => MutateAsync(async () =>
    {
        if (request.SafeMode)
        {
            return OperationResult<DashboardState>.Failure("tracking.safe_mode", "TrackingBlockedSafeMode", new ValidationIssue("safeMode", "safe_mode", "SafeModeBlocksTracking"));
        }

        try
        {
            ResumeScheduledSnapshots();
            _tracking.Start();
            _logger.LogInformation("Tracking started. SafeMode={SafeMode}", request.SafeMode);
            var state = LoadDashboardState();
            await Task.CompletedTask;
            return OperationResult<DashboardState>.Success("tracking.started", "TrackingStarted", state);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Tracking could not start. ExceptionType={ExceptionType}", exception.GetType().Name);
            EnqueueTrackingUnavailable(exception);
            return OperationResult<DashboardState>.Failure("tracking.start.failed", "TrackingUnavailable");
        }
    }, cancellationToken);

    /// <inheritdoc />
    public Task<OperationResult<DashboardState>> PauseTrackingAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        CancelLiveWork();
        return MutateAsync(async () =>
        {
            PauseScheduledSnapshots();
            _tracking.Stop();
            CancelLiveWork();
            _logger.LogInformation("Tracking paused.");
            var state = LoadDashboardState();
            await Task.CompletedTask;
            return OperationResult<DashboardState>.Success("tracking.paused", "TrackingPaused", state);
        }, cancellationToken);
    }

    /// <inheritdoc />
    public Task<OperationResult<DashboardState>> ToggleTrackingAsync(CancellationToken cancellationToken) => _tracking.IsTracking
        ? PauseTrackingAsync(cancellationToken)
        : StartTrackingAsync(new StartTrackingRequest(), cancellationToken);

    /// <inheritdoc />
    public async Task<OperationResult<DashboardState>> GetDashboardAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // Cache reloads are rare but may touch SQLite; the one-second UI refresh never performs them on its dispatcher.
        var state = await Task.Run(LoadDashboardState, cancellationToken).ConfigureAwait(false);
        return OperationResult<DashboardState>.Success("dashboard.loaded", "DashboardLoaded", state);
    }

    /// <inheritdoc />
    public Task<OperationResult<LastSessionState?>> GetLastSessionAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(OperationResult<LastSessionState?>.Success("session.last.loaded", "LastSessionLoaded", _tracking.LoadLastSessionState()));
    }

    /// <inheritdoc />
    public Task<OperationResult<DailySummary>> GetTodaySummaryAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(OperationResult<DailySummary>.Success("session.today.loaded", "TodaySummaryLoaded", _store.GetTodaySummary()));
    }

    /// <inheritdoc />
    public async Task<OperationResult<ReportSnapshot>> GetReportAsync(ReportQuery query, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            // Large SQLite scans run off the presentation/pipe thread and observe cancellation once per row.
            return await Task.Run(() => _reports.Build(query, cancellationToken), cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Closing a report is expected cancellation, not a report-generation failure.
            throw;
        }
        catch (Exception exception)
        {
            // Preserve the failure and log only code metadata; exception messages may contain private data.
            _logger.LogError(
                "Activity report generation failed. ExceptionType={ExceptionType} FailureComponent={FailureComponent} FailureMethod={FailureMethod}",
                exception.GetType().Name,
                exception.TargetSite?.DeclaringType?.FullName,
                exception.TargetSite?.Name);
            throw;
        }
    }

    /// <inheritdoc />
    public Task<OperationResult<ScreenshotCaptureResult>> CaptureScreenshotAsync(CaptureScreenshotRequest request, CancellationToken cancellationToken) =>
        RunCancellableLiveWorkAsync(token => CaptureScreenshotCoreAsync(request, token), cancellationToken);

    private Task<OperationResult<ScreenshotCaptureResult>> CaptureScreenshotCoreAsync(CaptureScreenshotRequest request, CancellationToken cancellationToken) => MutateAsync(async () =>
    {
        if (request.IsVip && (!request.Keep || request.CaptureOrigin != ScreenshotCaptureOrigins.Manual))
        {
            return OperationResult<ScreenshotCaptureResult>.Failure("snapshot.vip.capture.invalid", "ScreenshotCaptureFailed");
        }
        var settings = _settingsSnapshot.Value;
        var mode = request.Mode switch
        {
            null => settings.ScreenshotCaptureMode,
            "all-screens" => "all-screens",
            "active-window" => "active-window",
            _ => null
        };
        if (mode is null)
        {
            return OperationResult<ScreenshotCaptureResult>.Failure(
                "screenshot.mode.invalid",
                "ScreenshotModeUnsupported",
                new ValidationIssue("mode", "unsupported", "ScreenshotModeUnsupported"));
        }

        if (!settings.ScreenshotsEnabled)
        {
            return OperationResult<ScreenshotCaptureResult>.Failure("screenshot.disabled", "ScreenshotsDisabled");
        }

        if (IsCurrentContextPrivate(settings))
        {
            return OperationResult<ScreenshotCaptureResult>.Failure("privacy.blocked", "PrivacyBlocked");
        }

        // Provider validation belongs to the AI boundary after planning: gallery-only rules need no provider.
        // Capture happens only after the privacy and enabled-state gates above have succeeded.
        var captureRoot = request.Keep ? settings.ScreenshotDirectory : _screenshotPublications.TransientRoot;
        if (TryGetScreenshotStorageWarning(captureRoot))
        {
            return OperationResult<ScreenshotCaptureResult>.Failure("screenshot.storage.low", "ScreenshotStorageLow");
        }

        long pipelineStartedTimestamp = Stopwatch.GetTimestamp();
        var telemetryIntervalStartedAt = ResolveScreenshotTelemetryIntervalStart(settings.ScreenshotIntervalMinutes);
        long telemetryStartedTimestamp = Stopwatch.GetTimestamp();
        var deviceContext = await CaptureScreenshotContextAsync(cancellationToken).ConfigureAwait(false);
        long telemetryElapsedMilliseconds = (long)Stopwatch.GetElapsedTime(telemetryStartedTimestamp).TotalMilliseconds;

        ScreenshotCaptureResult result;
        long captureStartedTimestamp = Stopwatch.GetTimestamp();
        try
        {
            result = await RunCaptureWorkAsync(
                () => _capture.CaptureByMode(
                    captureRoot,
                    mode,
                    request.CaptureOrigin,
                    context => request.CaptureOrigin == ScreenshotCaptureOrigins.Scheduled
                        ? EvaluateScheduledCaptureDecision(context)
                        : EvaluateCaptureDecision(context)),
                cancellationToken).ConfigureAwait(false);
        }
        catch (ScreenshotCapturePreconditionException exception)
        {
            // Settings and foreground privacy are re-evaluated inside the worker immediately before pixels.
            return CapturePreconditionFailure<ScreenshotCaptureResult>(exception.Decision);
        }
        catch (ScreenshotIdlePolicyException exception)
        {
            _logger.LogWarning("Scheduled screen capture skipped because the idle policy was unavailable. ExceptionType={ExceptionType}", exception.InnerException?.GetType().Name);
            return CapturePreconditionFailure<ScreenshotCaptureResult>(ScreenshotCaptureDecision.UserIdle);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Screenshot capture failed. ExceptionType={ExceptionType}", exception.GetType().Name);
            EnqueueScreenshotCaptureFailure(exception);
            return OperationResult<ScreenshotCaptureResult>.Failure("screenshot.capture.failed", "ScreenshotCaptureFailed");
        }
        long captureElapsedMilliseconds = (long)Stopwatch.GetElapsedTime(captureStartedTimestamp).TotalMilliseconds;
        result = result with { DeviceContext = deviceContext };

        long persistenceStartedTimestamp = Stopwatch.GetTimestamp();
        try
        {
            _screenshotPublications.Publish(result, captureRoot, settings.InstallationId, request.Keep);
            if (request.Keep)
            {
                PersistScreenshotIntervalTelemetry(result, telemetryIntervalStartedAt, DateTimeOffset.UtcNow);
                if (request.IsVip)
                {
                    // Mark every monitor artifact through its shared capture identity before reporting success.
                    _store.RegisterVipScreenshot(result.CaptureId);
                }
            }
        }
        catch (Exception exception)
        {
            // A failed durable snapshot cannot be reported as a successful retained capture.
            CleanupAbandonedCapture(result);
            _logger.LogWarning("Screenshot telemetry persistence failed. ExceptionType={ExceptionType}", exception.GetType().Name);
            return OperationResult<ScreenshotCaptureResult>.Failure("screenshot.telemetry.persistence.failed", "ScreenshotCaptureFailed");
        }
        long persistenceElapsedMilliseconds = (long)Stopwatch.GetElapsedTime(persistenceStartedTimestamp).TotalMilliseconds;

        await NotifyScreenshotCaptureAsync(result, cancellationToken).ConfigureAwait(false);
        _logger.LogInformation(
            "Local screenshot pipeline completed. Origin={Origin} Mode={Mode} Artifacts={ArtifactCount} ContextMs={ContextMilliseconds} CaptureMs={CaptureMilliseconds} PersistenceMs={PersistenceMilliseconds} TotalMs={TotalMilliseconds}",
            request.CaptureOrigin,
            mode,
            result.AnalysisScreenshotPaths.Count,
            telemetryElapsedMilliseconds,
            captureElapsedMilliseconds,
            persistenceElapsedMilliseconds,
            (long)Stopwatch.GetElapsedTime(pipelineStartedTimestamp).TotalMilliseconds);

        if (settings.OpenAiEnabled && !request.DeferAiAnalysis && result.AnalysisScreenshotPaths.Count > 0)
        {
            try
            {
                var analysisOrigin = result.CaptureOrigin == ScreenshotCaptureOrigins.Scheduled
                    ? "snapshot.scheduled"
                    : "snapshot.manual";
                var pipeline = await AnalyzeLiveCaptureAsync(
                    result,
                    request.Keep,
                    analysisOrigin,
                    cancellationToken);
                result = pipeline.Capture;
            }
            catch (OperationCanceledException)
            {
                return OperationResult<ScreenshotCaptureResult>.Failure("operation.cancelled", "OperationCancelled");
            }
            catch (AiProviderRequestException exception)
            {
                LogAiProviderFailure("screenshot.capture", exception);
                EnqueueAiAnalysisFailure(ProviderFailureCode(exception), BuildAiProviderFailureDetail(exception));
                return OperationResult<ScreenshotCaptureResult>.Failure(ProviderFailureCode(exception), ProviderFailureMessage(exception));
            }
            catch (AiDailyAnalysisQuotaReachedException)
            {
                EnqueueAiAnalysisFailure("ai.daily_limit");
                return OperationResult<ScreenshotCaptureResult>.Failure("ai.daily_limit", "AiDailyLimit");
            }
            catch (AiLiveAnalysisPreflightException exception)
            {
                return OperationResult<ScreenshotCaptureResult>.Failure(exception.Code, exception.MessageKey);
            }
            catch (InvalidOperationException)
            {
                EnqueueAiAnalysisFailure("ai.configuration.invalid");
                return OperationResult<ScreenshotCaptureResult>.Failure("ai.configuration.invalid", "AiConfigurationInvalid");
            }
            catch (Exception exception)
            {
                _logger.LogWarning("Snapshot AI analysis failed. ExceptionType={ExceptionType}", exception.GetType().Name);
                EnqueueAiAnalysisFailure("ai.provider.failed");
                return OperationResult<ScreenshotCaptureResult>.Failure("ai.provider.failed", "AiProviderFailed");
            }
            finally
            {
                // Core must clean raw files when analysis fails before a retained result is available.
                CleanupCaptureArtifacts(result, request.Keep);
            }
        }
        else if (!request.Keep || !settings.OpenAiEnabled || result.AnalysisScreenshotPaths.Count == 0)
        {
            // With no later AI pass, raw analysis artifacts have no owner even when stored copies are retained.
            CleanupCaptureArtifacts(result, request.Keep);
        }

        await Task.CompletedTask;
        return OperationResult<ScreenshotCaptureResult>.Success("screenshot.captured", "ScreenshotCaptured", result);
    }, cancellationToken);

    /// <inheritdoc />
    public async Task<OperationResult<PendingManualScreenshotState>> CaptureManualScreenshotAsync(CancellationToken cancellationToken)
    {
        await _manualScreenshotCaptureGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (Volatile.Read(ref _pendingManualScreenshot) is not null)
            {
                // An expired capture is still owned by the timer until it is handed to analysis.
                return OperationResult<PendingManualScreenshotState>.Failure("snapshot.pending.exists", "PendingManualSnapshotExists");
            }

            var capture = await CaptureScreenshotAsync(
                new CaptureScreenshotRequest(
                    // The player is foreground when its capture button runs, so active-window would capture WorkTrail itself.
                    Mode: "all-screens",
                    Keep: true,
                    CaptureOrigin: ScreenshotCaptureOrigins.Manual,
                    DeferAiAnalysis: true),
                cancellationToken).ConfigureAwait(false);
            if (!capture.Succeeded || capture.Value is not { } captured)
            {
                return OperationResult<PendingManualScreenshotState>.Failure(capture.Code, capture.MessageKey, capture.Issues.ToArray());
            }

            if (captured.StoredScreenshotPaths.FirstOrDefault() is not { } screenshotPath)
            {
                CleanupAbandonedCapture(captured);
                return OperationResult<PendingManualScreenshotState>.Failure("screenshot.capture.failed", "ScreenshotCaptureFailed");
            }

            try
            {
                // Once files exist, register their owner without caller cancellation so no completed capture is orphaned.
                return await MutateAsync(async () =>
                {
                    var expiresAt = DateTimeOffset.Now.AddSeconds(ManualScreenshotDeletionWindowSeconds);
                    Volatile.Write(ref _pendingManualScreenshot, new PendingManualScreenshotRegistration(captured, expiresAt));
                    var pending = new PendingManualScreenshotState(screenshotPath, expiresAt);
                    await Task.CompletedTask;
                    return OperationResult<PendingManualScreenshotState>.Success("snapshot.pending.created", "PendingManualSnapshotCreated", pending);
                }, CancellationToken.None).ConfigureAwait(false);
            }
            catch
            {
                // Registration failed after durable files were created; remove every artifact and sidecar before rethrowing.
                CleanupAbandonedCapture(captured);
                throw;
            }
        }
        finally
        {
            _manualScreenshotCaptureGate.Release();
        }
    }

    /// <inheritdoc />
    public Task<OperationResult<bool>> DeletePendingManualScreenshotAsync(CancellationToken cancellationToken) => MutateVisualStateAsync(async () =>
    {
        if (Volatile.Read(ref _pendingManualScreenshot) is not { } registration || GetPendingManualScreenshotState() is null)
        {
            return OperationResult<bool>.Failure("snapshot.pending.not_found", "PendingManualSnapshotNotFound");
        }

        var capture = registration.Capture;
        var deletions = new List<ScreenshotDeletionJournal.Plan>();
        try
        {
            foreach (var artifact in capture.AllScreenshotPaths.DistinctBy(
                         path => LocalStore.ScreenshotIdentity(Path.GetFileName(path)), StringComparer.OrdinalIgnoreCase))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var deletion = _screenshotDeletions.Begin(artifact, deleteAnalysis: true);
                if (deletion is null) continue;
                deletions.Add(deletion);
            }
            _screenshotNotifications?.Clear();
            foreach (var deletion in deletions) _screenshotDeletions.Execute(deletion);
        }
        catch (IOException)
        {
            return OperationResult<bool>.Failure("screenshot.delete.failed", "ScreenshotDeleteFailed");
        }
        catch (UnauthorizedAccessException)
        {
            return OperationResult<bool>.Failure("screenshot.delete.failed", "ScreenshotDeleteFailed");
        }

        await _search.SynchronizeAsync(cancellationToken).ConfigureAwait(false);
        foreach (var deletion in deletions) _screenshotDeletions.Complete(deletion);
        Volatile.Write(ref _pendingManualScreenshot, null);
        return OperationResult<bool>.Success("snapshot.pending.deleted", "PendingManualSnapshotDeleted", true);
    }, cancellationToken);

    /// <inheritdoc />
    public Task<OperationResult<AiAnalysis>> AnalyzeCapturedScreenshotAsync(AnalyzeCapturedScreenshotRequest request, CancellationToken cancellationToken) =>
        RunCancellableLiveWorkAsync(token => AnalyzeCapturedScreenshotCoreAsync(request, token), cancellationToken);

    private async Task<OperationResult<AiAnalysis>> AnalyzeCapturedScreenshotCoreAsync(AnalyzeCapturedScreenshotRequest request, CancellationToken cancellationToken)
    {
        var aiEnabledForAnalysis = false;
        string? aiFailureDetail = null;
        var operation = await MutateAsync(async () =>
        {
            var capture = request.Capture;
            if (capture is null)
            {
                return OperationResult<AiAnalysis>.Failure("ai.capture.invalid", "AiConfigurationInvalid");
            }

            try
            {
                var settings = _settingsSnapshot.Value;
                if (!settings.OpenAiEnabled)
                {
                    return OperationResult<AiAnalysis>.Failure("ai.disabled", "AiDisabled");
                }

                aiEnabledForAnalysis = true;

                if (IsCurrentContextPrivate(settings))
                {
                    return OperationResult<AiAnalysis>.Failure("privacy.blocked", "PrivacyBlocked");
                }

                if (!TryValidateOpenAiConfiguration(settings, requireImageInput: true, out var validatedSettings, out var validationIssue))
                {
                    return AiConfigurationFailure<AiAnalysis>(validationIssue!);
                }

                settings = validatedSettings;
                var gate = BuildRequestGate(settings);
                if (!gate.Allowed)
                {
                    return OperationResult<AiAnalysis>.Failure("ai.daily_limit", "AiDailyLimit");
                }

                try
                {
                    var origin = NormalizeAnalysisOrigin(request.Origin);
                    var pipeline = await AnalyzeLiveCaptureAsync(
                        capture,
                        request.KeepCapture,
                        origin,
                        cancellationToken);
                    return OperationResult<AiAnalysis>.Success("ai.analyzed", "AiAnalyzed", pipeline.Analysis);
                }
                catch (OperationCanceledException)
                {
                    return OperationResult<AiAnalysis>.Failure("operation.cancelled", "OperationCancelled");
                }
                catch (AiProviderRequestException exception)
                {
                    LogAiProviderFailure("screenshot.analyze", exception);
                    aiFailureDetail = BuildAiProviderFailureDetail(exception);
                    return OperationResult<AiAnalysis>.Failure(ProviderFailureCode(exception), ProviderFailureMessage(exception));
                }
                catch (AiDailyAnalysisQuotaReachedException)
                {
                    return OperationResult<AiAnalysis>.Failure("ai.daily_limit", "AiDailyLimit");
                }
                catch (AiLiveAnalysisPreflightException exception)
                {
                    return OperationResult<AiAnalysis>.Failure(exception.Code, exception.MessageKey);
                }
                catch (InvalidOperationException)
                {
                    return OperationResult<AiAnalysis>.Failure("ai.configuration.invalid", "AiConfigurationInvalid");
                }
                catch (Exception exception)
                {
                    // Provider/network errors remain stable to callers; the capture service owns cleanup behavior.
                    _logger.LogWarning("Deferred snapshot AI analysis failed. ExceptionType={ExceptionType}", exception.GetType().Name);
                    return OperationResult<AiAnalysis>.Failure("ai.provider.failed", "AiProviderFailed");
                }
            }
            finally
            {
                // Every preflight outcome owns cleanup, including disabled AI, privacy, configuration and request limits.
                CleanupCaptureArtifacts(capture, request.KeepCapture);
            }
        }, cancellationToken);

        if (!operation.Succeeded && aiEnabledForAnalysis && ShouldNotifyAiAnalysisFailure(operation.Code))
        {
            // The queue is the cross-process fallback: a connected UI drains it, while headless runtimes only retain bounded notices.
            EnqueueAiAnalysisFailure(operation.Code, aiFailureDetail);
        }

        return operation;
    }

    /// <inheritdoc />
    public async Task<OperationResult<SearchResponse>> SearchAsync(SearchRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var response = await _search.SearchAsync(request, cancellationToken).ConfigureAwait(false);
            return OperationResult<SearchResponse>.Success("search.completed", "SearchCompleted", response);
        }
        catch (OperationCanceledException)
        {
            return OperationResult<SearchResponse>.Failure("operation.cancelled", "OperationCancelled");
        }
        catch (ArgumentException)
        {
            return OperationResult<SearchResponse>.Failure(
                "search.query.invalid",
                "SearchQueryInvalid",
                new ValidationIssue("query", "invalid", "SearchQueryInvalid"));
        }
        catch (Exception exception)
        {
            _logger.LogWarning("Local search failed. ExceptionType={ExceptionType}", exception.GetType().Name);
            return OperationResult<SearchResponse>.Failure("search.failed", "SearchFailed");
        }
    }

    /// <inheritdoc />
    public async Task<OperationResult<SearchAvailability>> GetSearchAvailabilityAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var today = DateOnly.FromDateTime(DateTime.Now);
        // Opening search only needs counts. Enumerate retained artifacts once off the UI/pipe thread;
        // loading every gallery projection would also query activity per screenshot.
        var counts = await Task.Run(
            () => _store.GetScreenshotAvailabilityCounts(today, cancellationToken),
            cancellationToken).ConfigureAwait(false);
        return OperationResult<SearchAvailability>.Success(
            "search.availability.loaded",
            "SearchAvailabilityLoaded",
            new SearchAvailability(counts.TotalSnapshotCount, counts.TodaySnapshotCount));
    }

    /// <inheritdoc />
    public async Task<OperationResult<int>> RebuildSearchIndexAsync(CancellationToken cancellationToken)
    {
        try
        {
            // Lucene can perform substantial synchronous setup before its first asynchronous yield.
            // Keep that work off WinUI and IPC dispatch threads while preserving cooperative cancellation.
            var count = await Task.Run(
                () => _search.RebuildAsync(cancellationToken),
                cancellationToken).ConfigureAwait(false);
            return OperationResult<int>.Success("search.index.rebuilt", "SearchIndexRebuilt", count);
        }
        catch (OperationCanceledException)
        {
            return OperationResult<int>.Failure("operation.cancelled", "OperationCancelled");
        }
        catch (Exception exception)
        {
            _logger.LogWarning("Local search index rebuild failed. ExceptionType={ExceptionType}", exception.GetType().Name);
            return OperationResult<int>.Failure("search.index.failed", "SearchIndexFailed");
        }
    }

    /// <inheritdoc />
    public Task<OperationResult<IReadOnlyList<ApplicationNotification>>> DrainApplicationNotificationsAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var drained = new List<ApplicationNotification>(MaximumPendingNotifications);
        while (drained.Count < MaximumPendingNotifications && _notifications.TryDequeue(out var notification))
        {
            drained.Add(notification);
        }

        return Task.FromResult(OperationResult<IReadOnlyList<ApplicationNotification>>.Success(
            "notifications.drained",
            "ApplicationNotificationsDrained",
            drained));
    }

    /// <inheritdoc />
    public Task<OperationResult<string?>> GetLatestScreenshotAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(OperationResult<string?>.Success("screenshot.latest.loaded", "LatestScreenshotLoaded", _store.LoadLatestPrimaryScreenshot()));
    }

    /// <inheritdoc />
    public Task<OperationResult<string>> DeleteScreenshotAsync(string screenshotPath, CancellationToken cancellationToken) => MutateVisualStateAsync(async () =>
    {
        cancellationToken.ThrowIfCancellationRequested();
        var deletion = _screenshotDeletions.Begin(screenshotPath, deleteAnalysis: true);
        if (deletion is null)
        {
            return OperationResult<string>.Failure("screenshot.not_found", "ScreenshotNotFound", new ValidationIssue("screenshotPath", "not_found", "ScreenshotNotFound"));
        }

        try
        {
            _screenshotNotifications?.Clear();
            _screenshotDeletions.Execute(deletion);
        }
        catch (IOException)
        {
            // A locked or unavailable artifact is reported without hiding a partial deletion from the caller.
            return OperationResult<string>.Failure("screenshot.delete.failed", "ScreenshotDeleteFailed");
        }
        catch (UnauthorizedAccessException)
        {
            // Permission failures remain explicit so the user can retry after the file becomes writable.
            return OperationResult<string>.Failure("screenshot.delete.failed", "ScreenshotDeleteFailed");
        }

        await _search.SynchronizeAsync(cancellationToken).ConfigureAwait(false);
        _screenshotDeletions.Complete(deletion);
        return OperationResult<string>.Success("screenshot.deleted", "ScreenshotDeleted", screenshotPath);
    }, cancellationToken);

    /// <inheritdoc />
    public Task<OperationResult<string>> DeleteScreenshotAnalysisAsync(string screenshotPath, CancellationToken cancellationToken) => MutateVisualStateAsync(async () =>
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!ScreenCaptureService.IsOwnedArtifact(screenshotPath) || !Path.IsPathFullyQualified(screenshotPath))
        {
            return OperationResult<string>.Failure("screenshot.analysis.invalid", "ScreenshotAnalysisInvalid", new ValidationIssue("screenshotPath", "invalid", "ScreenshotAnalysisInvalid"));
        }

        if (_store.FindScreenshotArtifacts(screenshotPath).Count == 0)
        {
            // Analysis rows are addressable only through a currently retained artifact inside the configured store.
            // An owned-looking path elsewhere must never authorize deletion by matching filename identity alone.
            return OperationResult<string>.Failure("screenshot.analysis.not_found", "ScreenshotAnalysisNotFound", new ValidationIssue("screenshotPath", "not_found", "ScreenshotAnalysisNotFound"));
        }

        // Delete screenshot-specific rows first, then finish with the analysis-artifact upsert trigger.
        // Local search uses the final mutation per artifact, so the retained image remains searchable
        // with its non-analysis fields after this operation.
        var deletedCount = checked(
            _store.DeleteScreenshotIntervalTelemetry(screenshotPath)
            + _store.DeleteAiAnalysesReferencingScreenshot(screenshotPath));
        await _search.SynchronizeAsync(cancellationToken).ConfigureAwait(false);
        if (deletedCount == 0)
        {
            return OperationResult<string>.Failure("screenshot.analysis.not_found", "ScreenshotAnalysisNotFound", new ValidationIssue("screenshotPath", "not_found", "ScreenshotAnalysisNotFound"));
        }

        await Task.CompletedTask;
        return OperationResult<string>.Success("screenshot.analysis.deleted", "ScreenshotAnalysisDeleted", screenshotPath);
    }, cancellationToken);

    /// <inheritdoc />
    public async Task<OperationResult<ScreenshotGallery>> GetScreenshotGalleryAsync(DateOnly date, CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            // Gallery projection performs directory enumeration and SQLite reads;
            // keep all of that work off the WinUI/pipe caller and cancel superseded date requests.
            var gallery = await Task.Run(
                () => _store.GetScreenshotGallery(date, cancellationToken),
                cancellationToken).ConfigureAwait(false);
            ReportIncompleteScreenshotGallery(gallery);
            return OperationResult<ScreenshotGallery>.Success(
                "screenshot.gallery.loaded",
                "ScreenshotGalleryLoaded",
                gallery);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                "Screenshot gallery projection failed. Date={Date} ExceptionType={ExceptionType}",
                date,
                exception.GetType().Name);
            return OperationResult<ScreenshotGallery>.Failure(
                "screenshot.gallery.failed",
                "ScreenshotGalleryFailed");
        }
    }

    /// <inheritdoc />
    public async Task<OperationResult<ScreenshotGallery>> GetLatestScreenshotGalleryAsync(CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            // The latest-gallery lookup shares the same potentially large local projection path.
            var gallery = await Task.Run(
                () => _store.GetLatestScreenshotGallery(cancellationToken),
                cancellationToken).ConfigureAwait(false);
            ReportIncompleteScreenshotGallery(gallery);
            return OperationResult<ScreenshotGallery>.Success(
                "screenshot.gallery.latest.loaded",
                "LatestScreenshotGalleryLoaded",
                gallery);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                "Latest screenshot gallery projection failed. ExceptionType={ExceptionType}",
                exception.GetType().Name);
            return OperationResult<ScreenshotGallery>.Failure(
                "screenshot.gallery.latest.failed",
                "LatestScreenshotGalleryFailed");
        }
    }

    /// <inheritdoc />
    public async Task<OperationResult<ScreenshotImageContent>> GetScreenshotImageAsync(
        ScreenshotImageRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(request.ScreenshotPath)
            || !Path.IsPathFullyQualified(request.ScreenshotPath)
            || !ScreenCaptureService.IsOwnedArtifact(request.ScreenshotPath))
        {
            return OperationResult<ScreenshotImageContent>.Failure(
                "screenshot.image.invalid",
                "ScreenshotImageInvalid",
                new ValidationIssue("screenshotPath", "invalid", "ScreenshotImageInvalid"));
        }

        string requestedFullPath;
        try
        {
            requestedFullPath = Path.GetFullPath(request.ScreenshotPath);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return OperationResult<ScreenshotImageContent>.Failure(
                "screenshot.image.invalid",
                "ScreenshotImageInvalid",
                new ValidationIssue("screenshotPath", "invalid", "ScreenshotImageInvalid"));
        }

        var artifactIdentity = LocalStore.ScreenshotIdentity(Path.GetFileName(requestedFullPath));
        string? retainedPath;
        try
        {
            // Root validation and directory enumeration are synchronous; keep them off the WinUI/pipe caller.
            retainedPath = await Task.Run(
                () => _store.FindScreenshotArtifacts(requestedFullPath)
                    .FirstOrDefault(path => string.Equals(
                        Path.GetFullPath(path),
                        requestedFullPath,
                        StringComparison.OrdinalIgnoreCase)),
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(
                "Retained screenshot image resolution failed. ArtifactIdentity={ArtifactIdentity} ExceptionType={ExceptionType}",
                artifactIdentity,
                exception.GetType().Name);
            return OperationResult<ScreenshotImageContent>.Failure(
                "screenshot.image.read_failed",
                "ScreenshotImageReadFailed");
        }

        if (retainedPath is null)
        {
            return OperationResult<ScreenshotImageContent>.Failure(
                "screenshot.image.not_found",
                "ScreenshotImageNotFound",
                new ValidationIssue("screenshotPath", "not_found", "ScreenshotImageNotFound"));
        }

        try
        {
            await using var stream = new FileStream(
                retainedPath,
                new FileStreamOptions
                {
                    Mode = FileMode.Open,
                    Access = FileAccess.Read,
                    Share = FileShare.Read | FileShare.Delete,
                    BufferSize = 64 * 1024,
                    Options = FileOptions.Asynchronous | FileOptions.SequentialScan
                });
            var length = stream.Length;
            if (length <= 0)
            {
                return OperationResult<ScreenshotImageContent>.Failure(
                    "screenshot.image.empty",
                    "ScreenshotImageEmpty");
            }

            if (length > MaximumScreenshotImageBytes)
            {
                return OperationResult<ScreenshotImageContent>.Failure(
                    "screenshot.image.too_large",
                    "ScreenshotImageTooLarge");
            }

            // Read exactly the size validated on this open handle so a concurrently growing artifact cannot cause an unbounded allocation.
            var content = new byte[checked((int)length)];
            await stream.ReadExactlyAsync(content, cancellationToken).ConfigureAwait(false);
            var trailingByte = new byte[1];
            if (await stream.ReadAsync(trailingByte, cancellationToken).ConfigureAwait(false) != 0)
            {
                _logger.LogWarning(
                    "Retained screenshot image read failed. ArtifactIdentity={ArtifactIdentity} ExceptionType={ExceptionType}",
                    artifactIdentity,
                    "ArtifactChangedDuringRead");
                return OperationResult<ScreenshotImageContent>.Failure(
                    "screenshot.image.read_failed",
                    "ScreenshotImageReadFailed");
            }

            return OperationResult<ScreenshotImageContent>.Success(
                "screenshot.image.loaded",
                "ScreenshotImageLoaded",
                new ScreenshotImageContent(artifactIdentity, content));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
        {
            return OperationResult<ScreenshotImageContent>.Failure(
                "screenshot.image.not_found",
                "ScreenshotImageNotFound",
                new ValidationIssue("screenshotPath", "not_found", "ScreenshotImageNotFound"));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(
                "Retained screenshot image read failed. ArtifactIdentity={ArtifactIdentity} ExceptionType={ExceptionType}",
                artifactIdentity,
                exception.GetType().Name);
            return OperationResult<ScreenshotImageContent>.Failure(
                "screenshot.image.read_failed",
                "ScreenshotImageReadFailed");
        }
    }

    /// <inheritdoc />
    public async Task<OperationResult<IReadOnlyList<InstallationProfile>>> GetInstallationProfilesAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            var profiles = await Task.Run(_store.GetInstallationProfiles, cancellationToken).ConfigureAwait(false);
            return OperationResult<IReadOnlyList<InstallationProfile>>.Success(
                "installation.profiles.loaded",
                "InstallationProfilesLoaded",
                profiles);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Installation profiles could not be loaded. ExceptionType={ExceptionType}", exception.GetType().Name);
            return OperationResult<IReadOnlyList<InstallationProfile>>.Failure(
                "installation.profiles.load_failed",
                "InstallationProfilesLoadFailed");
        }
    }

    /// <inheritdoc />
    public Task<OperationResult<InstallationProfile>> UpdateInstallationProfileAsync(
        UpdateInstallationProfileRequest request,
        CancellationToken cancellationToken) =>
        MutateVisualStateAsync(async () =>
        {
            try
            {
                var existing = _store.GetInstallationProfile(request.InstallationId);
                if (existing is null)
                {
                    return OperationResult<InstallationProfile>.Failure(
                        "installation.profile.not_found",
                        "InstallationProfileNotFound");
                }

                var applied = InstallationProfileCatalog.Apply(existing, request, DateTimeOffset.UtcNow);
                if (!applied.Succeeded || applied.Value is null)
                {
                    return applied;
                }

                var saved = await Task.Run(
                    () => _store.SaveInstallationProfile(applied.Value, existing.Revision),
                    cancellationToken).ConfigureAwait(false);
                return OperationResult<InstallationProfile>.Success(
                    "installation.profile.updated",
                    "InstallationProfileUpdated",
                    saved);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Installation profile update failed. ExceptionType={ExceptionType}", exception.GetType().Name);
                return OperationResult<InstallationProfile>.Failure(
                    "installation.profile.update_failed",
                    "InstallationProfileUpdateFailed");
            }
        }, cancellationToken);

    /// <inheritdoc />
    public Task<OperationResult<DataArchiveExportResult>> ExportDataArchiveAsync(
        DataArchiveExportRequest request,
        CancellationToken cancellationToken) =>
        MutateVisualStateAsync(async () =>
        {
            // Check the current entitlement inside the mutation lock before writing archive files.
            if (!FeatureCatalog.IsAllowed(ProductFeature.DataTransfer, _featureAccess.Snapshot))
            {
                return OperationResult<DataArchiveExportResult>.Failure("feature.premium_required", "Premium.Required");
            }

            try
            {
                using var progress = _archiveProgress.Begin(request.OperationId);
                var result = await Task.Run(
                    () => _archives.Export(request, cancellationToken, progress.Report),
                    cancellationToken).ConfigureAwait(false);
                _logger.LogInformation(
                    "Private data archive exported. Installations={InstallationCount} ActivitySamples={ActivitySampleCount} Screenshots={ScreenshotCount}",
                    result.InstallationCount,
                    result.ActivitySampleCount,
                    result.ScreenshotFileCount);
                return OperationResult<DataArchiveExportResult>.Success(
                    "archive.export.completed",
                    "DataArchiveExportCompleted",
                    result);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Data archive export failed. ExceptionType={ExceptionType}", exception.GetType().Name);
                return OperationResult<DataArchiveExportResult>.Failure(
                    "archive.export.failed",
                    "DataArchiveExportFailed");
            }
        }, cancellationToken);

    /// <inheritdoc />
    public async Task<OperationResult<DataArchiveImportPlan>> PreviewDataArchiveImportAsync(
        DataArchiveImportPreviewRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            using var progress = _archiveProgress.Begin(request.OperationId);
            var plan = await Task.Run(
                () => _archives.PreviewImport(request, cancellationToken, progress.Report),
                cancellationToken).ConfigureAwait(false);
            return OperationResult<DataArchiveImportPlan>.Success(
                "archive.import.previewed",
                "DataArchiveImportPreviewed",
                plan);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Data archive import preview failed. ExceptionType={ExceptionType}", exception.GetType().Name);
            return OperationResult<DataArchiveImportPlan>.Failure(
                "archive.import.preview_failed",
                "DataArchiveImportPreviewFailed");
        }
    }

    /// <inheritdoc />
    public Task<OperationResult<DataArchiveImportResult>> ImportDataArchiveAsync(
        DataArchiveImportRequest request,
        CancellationToken cancellationToken) =>
        MutateVisualStateAsync(async () =>
        {
            // Free can preview a plan, but cannot commit it, including after a Premium downgrade.
            if (!FeatureCatalog.IsAllowed(ProductFeature.DataTransfer, _featureAccess.Snapshot))
            {
                return OperationResult<DataArchiveImportResult>.Failure("feature.premium_required", "Premium.Required");
            }

            var wasTracking = _tracking.IsTracking;
            if (wasTracking)
            {
                PauseScheduledSnapshots();
                _tracking.Stop();
            }

            try
            {
                using var progress = _archiveProgress.Begin(request.OperationId);
                var result = await Task.Run(
                    () => _archives.Import(request.PlanId, cancellationToken, progress.Report),
                    cancellationToken).ConfigureAwait(false);
                try
                {
                    // The merge is already durable at this point. Rebuild the derived index without
                    // allowing late caller cancellation to misreport the committed import as canceled.
                    progress.Report(new DataArchiveProgress(DataArchivePhase.RebuildingIndex));
                    _ = await _search.RebuildAsync(CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception exception)
                {
                    _logger.LogError(
                        exception,
                        "Data archive merged, but the derived search index could not be rebuilt. ExceptionType={ExceptionType}",
                        exception.GetType().Name);
                }

                _logger.LogInformation(
                    "Private data archive merged. AddedActivitySamples={ActivitySampleCount} AddedScreenshots={ScreenshotCount}",
                    result.AddedActivitySampleCount,
                    result.AddedScreenshotFileCount);
                return OperationResult<DataArchiveImportResult>.Success(
                    "archive.import.completed",
                    "DataArchiveImportCompleted",
                    result);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Data archive import failed. ExceptionType={ExceptionType}", exception.GetType().Name);
                return OperationResult<DataArchiveImportResult>.Failure(
                    "archive.import.failed",
                    "DataArchiveImportFailed");
            }
            finally
            {
                if (wasTracking)
                {
                    try
                    {
                        _tracking.Start();
                        ResumeScheduledSnapshots();
                    }
                    catch (Exception exception)
                    {
                        _logger.LogError(
                            exception,
                            "Tracking could not resume after archive import. ExceptionType={ExceptionType}",
                            exception.GetType().Name);
                        EnqueueTrackingUnavailable(exception);
                    }
                }
            }
        }, cancellationToken);

    /// <inheritdoc />
    public Task<OperationResult<AiScreenshotReprocessPlan>> PreviewAiScreenshotReprocessingAsync(
        AiScreenshotReprocessRequest request,
        CancellationToken cancellationToken) =>
        _screenshotReprocessing.PreviewAsync(request, cancellationToken);

    /// <inheritdoc />
    public Task<OperationResult<AiScreenshotReprocessJobSnapshot>> StartAiScreenshotReprocessingAsync(
        Guid planId,
        CancellationToken cancellationToken) =>
        _screenshotReprocessing.StartAsync(planId, cancellationToken);

    /// <inheritdoc />
    public Task<OperationResult<AiScreenshotReprocessJobSnapshot>> GetAiScreenshotReprocessingJobAsync(
        Guid jobId,
        CancellationToken cancellationToken) =>
        _screenshotReprocessing.GetAsync(jobId, cancellationToken);

    /// <inheritdoc />
    public Task<OperationResult<AiScreenshotReprocessJobSnapshot>> PauseAiScreenshotReprocessingAsync(
        Guid jobId,
        CancellationToken cancellationToken) =>
        _screenshotReprocessing.PauseAsync(jobId, cancellationToken);

    /// <inheritdoc />
    public Task<OperationResult<AiScreenshotReprocessJobSnapshot>> ResumeAiScreenshotReprocessingAsync(
        Guid jobId,
        CancellationToken cancellationToken) =>
        _screenshotReprocessing.ResumeAsync(jobId, cancellationToken);

    /// <inheritdoc />
    public Task<OperationResult<string>> SaveScreenshotAsync(string screenshotPath, string destinationPath, CancellationToken cancellationToken) => MutateAsync(async () =>
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!ScreenCaptureService.IsOwnedArtifact(screenshotPath) || !File.Exists(screenshotPath))
        {
            return OperationResult<string>.Failure("screenshot.invalid", "ScreenshotInvalid");
        }

        if (string.IsNullOrWhiteSpace(destinationPath))
        {
            return OperationResult<string>.Failure("screenshot.destination.invalid", "ScreenshotDestinationInvalid");
        }

        var destination = Path.GetFullPath(destinationPath);
        File.Copy(screenshotPath, destination, overwrite: true);
        await Task.CompletedTask;
        return OperationResult<string>.Success("screenshot.saved", "ScreenshotSaved", destination);
    }, cancellationToken);

    /// <inheritdoc />
    public Task<OperationResult<string>> ShareScreenshotAsync(string screenshotPath, long windowHandle, CancellationToken cancellationToken) => MutateAsync(async () =>
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!ScreenCaptureService.IsOwnedArtifact(screenshotPath) || !File.Exists(screenshotPath))
        {
            return OperationResult<string>.Failure("screenshot.invalid", "ScreenshotInvalid");
        }

        if (windowHandle == 0)
        {
            return OperationResult<string>.Failure("screenshot.share.window.invalid", "ScreenshotShareWindowInvalid");
        }

        var result = _screenshotShare.Share(screenshotPath, new IntPtr(windowHandle));
        await Task.CompletedTask;
        return OperationResult<string>.Success("screenshot.share.opened", "ScreenshotShareOpened", result);
    }, cancellationToken);

    /// <inheritdoc />
    public Task<OperationResult<bool>> OpenApplicationLogAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            _ = _applicationLogs.OpenLatestLog();
            _logger.LogInformation("Application log opened through the diagnostics facade.");
            return Task.FromResult(OperationResult<bool>.Success("diagnostics.log.opened", "ApplicationLogOpened", true));
        }
        catch (Exception exception) when (
            exception is IOException or
                UnauthorizedAccessException or
                InvalidOperationException or
                NotSupportedException or
                System.Runtime.InteropServices.COMException or
                System.ComponentModel.Win32Exception)
        {
            _logger.LogWarning("Application log could not be opened. ExceptionType={ExceptionType}", exception.GetType().Name);
            return Task.FromResult(OperationResult<bool>.Failure("diagnostics.log.unavailable", "ApplicationLogUnavailable"));
        }
    }

    /// <inheritdoc />
    public Task<OperationResult<bool>> OpenApplicationLogFolderAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            _ = _applicationLogs.OpenLogDirectory();
            _logger.LogInformation("Application log directory opened through the diagnostics facade.");
            return Task.FromResult(OperationResult<bool>.Success("diagnostics.log.folder.opened", "ApplicationLogFolderOpened", true));
        }
        catch (Exception exception) when (
            exception is IOException or
                UnauthorizedAccessException or
                InvalidOperationException or
                NotSupportedException or
                System.Runtime.InteropServices.COMException or
                System.ComponentModel.Win32Exception)
        {
            _logger.LogWarning("Application log directory could not be opened. ExceptionType={ExceptionType}", exception.GetType().Name);
            return Task.FromResult(OperationResult<bool>.Failure("diagnostics.log.unavailable", "ApplicationLogUnavailable"));
        }
    }

    /// <inheritdoc />
    public Task<OperationResult<bool>> ShareApplicationLogAsync(long windowHandle, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (windowHandle == 0)
        {
            return Task.FromResult(OperationResult<bool>.Failure("diagnostics.log.share.window.invalid", "ApplicationLogShareWindowInvalid"));
        }

        try
        {
            _ = _applicationLogs.ShareLatestRedactedLog(new IntPtr(windowHandle));
            _logger.LogInformation("Redacted application log prepared for sharing.");
            return Task.FromResult(OperationResult<bool>.Success("diagnostics.log.share.opened", "ApplicationLogShareOpened", true));
        }
        catch (Exception exception) when (
            exception is IOException or
                UnauthorizedAccessException or
                InvalidOperationException or
                ArgumentException or
                NotSupportedException or
                System.Runtime.InteropServices.COMException or
                System.ComponentModel.Win32Exception)
        {
            _logger.LogWarning("Redacted application log could not be shared. ExceptionType={ExceptionType}", exception.GetType().Name);
            return Task.FromResult(OperationResult<bool>.Failure("diagnostics.log.unavailable", "ApplicationLogUnavailable"));
        }
    }

    /// <inheritdoc />
    public Task<OperationResult<string>> OpenScreenshotFolderAsync(CancellationToken cancellationToken) => OpenFolderAsync(_settingsSnapshot.Value.ScreenshotDirectory, "screenshot.folder.opened", "ScreenshotFolderOpened", cancellationToken);

    /// <inheritdoc />
    public Task<OperationResult<string>> OpenScreenshotFolderAsync(string directory, CancellationToken cancellationToken)
        => string.IsNullOrWhiteSpace(directory)
            ? Task.FromResult(OperationResult<string>.Failure("screenshot.folder.path.required", "ScreenshotFolderPathRequired", new ValidationIssue("directory", "required", "ScreenshotFolderPathRequired")))
            : OpenFolderAsync(directory, "screenshot.folder.opened", "ScreenshotFolderOpened", cancellationToken);

    /// <inheritdoc />
    public Task<OperationResult<AiStatus>> GetAiStatusAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(OperationResult<AiStatus>.Success("ai.status.loaded", "AiStatusLoaded", BuildAiStatus()));
    }

    /// <inheritdoc />
    public Task<OperationResult<string>> GetAiKeyAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var settings = _settingsSnapshot.Value;
        var key = settings.AiProvider == "codex-cli" ? string.Empty : _store.LoadApiKey(settings.AiApiKeyName) ?? string.Empty;
        return Task.FromResult(OperationResult<string>.Success("ai.key.loaded", "AiKeyLoaded", key));
    }

    /// <inheritdoc />
    public async Task<OperationResult<AiConnectionTestResult>> TestAiConnectionAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _aiConnectionVerification.Invalidate();
        var settings = _settingsSnapshot.Value;
        if (!TryValidateOpenAiConfiguration(settings, requireImageInput: false, out var validatedSettings, out var validationIssue))
        {
            return AiConfigurationFailure<AiConnectionTestResult>(validationIssue!, "ai.connection.configuration.invalid", "AiConnectionTestConfigurationInvalid");
        }

        var usesCli = validatedSettings.AiProvider == "codex-cli";
        var apiKey = usesCli ? string.Empty : _store.LoadApiKey(validatedSettings.AiApiKeyName);
        if (!usesCli && !AiApiKeyPolicy.LooksPlausible(validatedSettings.AiProvider, validatedSettings.AiApiKeyName, apiKey))
        {
            return OperationResult<AiConnectionTestResult>.Failure(
                "ai.connection.key.missing",
                "AiConnectionTestKeyMissing",
                new ValidationIssue("ai.key_variable", "required", "AiConnectionTestKeyMissing"));
        }

        return await TestAiCredentialAsync(validatedSettings, apiKey!, cancellationToken).ConfigureAwait(false);
    }

    private async Task<OperationResult<AiConnectionTestResult>> TestAiCredentialAsync(
        AppSettings validatedSettings, string apiKey, CancellationToken cancellationToken)
    {
        _aiConnectionVerification.Invalidate();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        var stopwatch = Stopwatch.StartNew();
        try
        {
            // This intentionally uses a tiny text-only request: no screen or device data leaves this PC during a connection check.
            var providerResult = await AIDecoderFactory.Create(validatedSettings).DecodeAsync(
                AiConnectionTestProtocol.Prompt,
                Array.Empty<string>(),
                validatedSettings,
                apiKey!,
                Guid.NewGuid().ToString("N"),
                requestOptions: new AiProviderRequestOptions(MaxOutputTokens: 256),
                cancellationToken: timeout.Token).ConfigureAwait(false);
            stopwatch.Stop();
            timeout.Token.ThrowIfCancellationRequested();
            _aiConnectionVerification.Record(validatedSettings, apiKey);
            _logger.LogInformation(
                "AI connection test succeeded. Provider={Provider} Model={Model} LatencyMs={LatencyMs}",
                validatedSettings.AiProvider,
                validatedSettings.Model,
                stopwatch.ElapsedMilliseconds);
            return OperationResult<AiConnectionTestResult>.Success(
                "ai.connection.succeeded",
                "AiConnectionTestSucceeded",
                new AiConnectionTestResult(validatedSettings.AiProvider,
                    validatedSettings.AiProvider == "codex-cli" ? providerResult.ReturnedModel ?? "Codex default" : validatedSettings.Model,
                    providerResult.Text, stopwatch.ElapsedMilliseconds));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (AiProviderRequestException exception)
        {
            stopwatch.Stop();
            _logger.LogWarning("AI connection test failed. Provider={Provider} Model={Model} FailureCategory={FailureCategory} HttpStatus={HttpStatus}", validatedSettings.AiProvider, validatedSettings.Model, exception.Failure.FailureCode, exception.Failure.HttpStatusCode);
            if (validatedSettings.AiProvider == "codex-cli")
                return OperationResult<AiConnectionTestResult>.Failure(exception.Failure.FailureCode, CodexCliDecoder.MessageKey(exception.Failure.FailureCode));
            var messageKey = exception.Failure.HttpStatusCode switch
            {
                401 => "ProviderSetup.Error.Authentication",
                403 or 404 => "ProviderSetup.Error.Access",
                429 => "ProviderSetup.Error.Limits",
                _ => "ProviderSetup.Error.Unavailable"
            };
            return OperationResult<AiConnectionTestResult>.Failure("ai.connection.failed", messageKey);
        }
        catch (OperationCanceledException)
        {
            return validatedSettings.AiProvider == "codex-cli"
                ? OperationResult<AiConnectionTestResult>.Failure("codex.timeout", "Options.Codex.Timeout")
                : OperationResult<AiConnectionTestResult>.Failure("ai.connection.failed", "ProviderSetup.Error.Unavailable");
        }
        catch (Exception exception)
        {
            stopwatch.Stop();
            _logger.LogWarning("AI connection test failed unexpectedly. Provider={Provider} Model={Model} ExceptionType={ExceptionType}", validatedSettings.AiProvider, validatedSettings.Model, exception.GetType().Name);
            return OperationResult<AiConnectionTestResult>.Failure("ai.connection.failed", "ProviderSetup.Error.Unavailable");
        }
    }

    /// <inheritdoc />
    public Task<OperationResult<AiModelCatalogSnapshot>> GetAiModelCatalogAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(OperationResult<AiModelCatalogSnapshot>.Success("ai.models.loaded", "AiModelsLoaded", _aiModelCatalog));
    }

    /// <inheritdoc />
    public Task<OperationResult<AiStatus>> SetAiEnabledAsync(bool enabled, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!enabled) CancelLiveWork();
        // Disabling is a priority policy write; it never waits for the remote visual-analysis gate.
        return enabled ? SetAiEnabledCoreAsync(enabled, cancellationToken)
            : MutateAsync(async () =>
            {
                PersistSettings(_settingsSnapshot.Value with { OpenAiEnabled = false });
                await Task.CompletedTask;
                return OperationResult<AiStatus>.Success("ai.disabled", "AiDisabled", BuildAiStatus());
            }, cancellationToken);
    }

    private Task<OperationResult<AiStatus>> SetAiEnabledCoreAsync(bool enabled, CancellationToken cancellationToken) => MutateVisualStateAsync(async () =>
    {
        var settings = _settingsSnapshot.Value with { OpenAiEnabled = enabled };
        var validatedSettings = settings;
        if (enabled
            && !TryValidateOpenAiConfiguration(settings, requireImageInput: false, out validatedSettings, out var validationIssue))
        {
            return AiConfigurationFailure<AiStatus>(validationIssue!);
        }

        PersistSettings(validatedSettings);
        await Task.CompletedTask;
        return OperationResult<AiStatus>.Success(enabled ? "ai.enabled" : "ai.disabled", enabled ? "AiEnabled" : "AiDisabled", BuildAiStatus());
    }, cancellationToken);

    /// <inheritdoc />
    public async Task<OperationResult<AppSettings>> ConfigureAiAsync(SettingsPatch patch, CancellationToken cancellationToken)
    {
        var aiKeys = SettingsCatalog.Definitions
            .Select(definition => definition.Key)
            .Where(key => key.StartsWith("ai.", StringComparison.OrdinalIgnoreCase) && !key.Equals("ai.key_variable", StringComparison.OrdinalIgnoreCase))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var values = patch.Values
            .Where(pair => aiKeys.Contains(pair.Key))
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
        return await PatchSettingsAsync(new SettingsPatch(values), cancellationToken);
    }

    /// <inheritdoc />
    public Task<OperationResult<string>> SetAiKeyAsync(string keyVariable, string secret, CancellationToken cancellationToken) => MutateVisualStateAsync(async () =>
    {
        var normalizedKeyVariable = keyVariable ?? string.Empty;
        var secretValue = secret ?? string.Empty;
        if (!SettingsCatalog.IsAllowedApiKeyVariable(normalizedKeyVariable)
            || !AiApiKeyPolicy.LooksPlausibleForVariable(normalizedKeyVariable, secretValue))
        {
            return OperationResult<string>.Failure("ai.key.invalid", "AiKeyInvalid", new ValidationIssue("key", "invalid", "AiKeyInvalid"));
        }

        var settings = _settingsSnapshot.Value;
        var candidate = settings with { AiApiKeyName = normalizedKeyVariable, OpenAiEnabled = false };
        if (!AiApiKeyPolicy.LooksPlausible(candidate.AiProvider, normalizedKeyVariable, secretValue)
            || !TryValidateOpenAiConfiguration(candidate, requireImageInput: false, out var validated, out _))
        {
            return OperationResult<string>.Failure("ai.key.invalid", "ProviderSetup.Error.Configuration");
        }

        // Verify the candidate before replacing a working credential. No activity or screenshots are sent.
        var verification = await TestAiCredentialAsync(validated, secretValue, cancellationToken).ConfigureAwait(false);
        if (!verification.Succeeded)
        {
            return OperationResult<string>.Failure(verification.Code, verification.MessageKey);
        }

        cancellationToken.ThrowIfCancellationRequested();
        // Only a verified credential reaches the user environment store; it is never put in settings or logs.
        _utilities.SetApiKey(normalizedKeyVariable, secretValue);
        secretValue = string.Empty;
        PersistSettings(settings with { AiApiKeyName = normalizedKeyVariable, Model = validated.Model });
        await Task.CompletedTask;
        return OperationResult<string>.Success("ai.key.stored", "AiKeyStored", normalizedKeyVariable);
    }, cancellationToken);

    /// <inheritdoc />
    public Task<OperationResult<AiAnalysis>> AnalyzeCurrentActivityAsync(AnalyzeCurrentActivityRequest request, CancellationToken cancellationToken) =>
        RunCancellableLiveWorkAsync(token => AnalyzeCurrentActivityCoreAsync(request, token), cancellationToken);

    private Task<OperationResult<AiAnalysis>> AnalyzeCurrentActivityCoreAsync(AnalyzeCurrentActivityRequest request, CancellationToken cancellationToken) => MutateAsync(async () =>
    {
        var settings = _settingsSnapshot.Value;
        if (!settings.OpenAiEnabled)
        {
            return OperationResult<AiAnalysis>.Failure("ai.disabled", "AiDisabled");
        }

        if (IsCurrentContextPrivate(settings))
        {
            return OperationResult<AiAnalysis>.Failure("privacy.blocked", "PrivacyBlocked");
        }

        if (!TryValidateOpenAiConfiguration(
                settings,
                request.AllowCapture && settings.ScreenshotsEnabled,
                out var validatedSettings,
                out var validationIssue))
        {
            return AiConfigurationFailure<AiAnalysis>(validationIssue!);
        }

        settings = validatedSettings;

        var gate = BuildRequestGate(settings);
        if (!gate.Allowed)
        {
            return OperationResult<AiAnalysis>.Failure("ai.daily_limit", "AiDailyLimit");
        }

        try
        {
            var origin = NormalizeAnalysisOrigin(request.Origin);
            ScreenshotCaptureResult? capture = null;
            try
            {
                AiAnalysis result;
                if (request.AllowCapture && settings.ScreenshotsEnabled)
                {
                    var captureRoot = settings.KeepScreenshots ? settings.ScreenshotDirectory : _screenshotPublications.TransientRoot;
                    if (TryGetScreenshotStorageWarning(captureRoot))
                    {
                        return OperationResult<AiAnalysis>.Failure("screenshot.storage.low", "ScreenshotStorageLow");
                    }

                    var telemetryIntervalStartedAt = ResolveScreenshotTelemetryIntervalStart(settings.ScreenshotIntervalMinutes);
                    var deviceContext = await CaptureScreenshotContextAsync(cancellationToken).ConfigureAwait(false);

                    try
                    {
                        capture = await RunCaptureWorkAsync(
                            () => _capture.CaptureByMode(
                                captureRoot,
                                settings.ScreenshotCaptureMode,
                                captureOrigin: string.Equals(origin, "snapshot.scheduled", StringComparison.Ordinal)
                                    ? ScreenshotCaptureOrigins.Scheduled
                                    : ScreenshotCaptureOrigins.Manual,
                                authorizeCapture: context => string.Equals(origin, "snapshot.scheduled", StringComparison.Ordinal)
                                    ? EvaluateScheduledCaptureDecision(context)
                                    : EvaluateCaptureDecision(context)),
                            cancellationToken).ConfigureAwait(false);
                    }
                    catch (ScreenshotCapturePreconditionException exception)
                    {
                        return CapturePreconditionFailure<AiAnalysis>(exception.Decision);
                    }
                    catch (ScreenshotIdlePolicyException exception)
                    {
                        _logger.LogWarning("Scheduled screen capture skipped because the idle policy was unavailable. ExceptionType={ExceptionType}", exception.InnerException?.GetType().Name);
                        return CapturePreconditionFailure<AiAnalysis>(ScreenshotCaptureDecision.UserIdle);
                    }
                    catch (Exception exception)
                    {
                        _logger.LogWarning(exception, "AI analysis screenshot capture failed. ExceptionType={ExceptionType}", exception.GetType().Name);
                        EnqueueScreenshotCaptureFailure(exception);
                        return OperationResult<AiAnalysis>.Failure("screenshot.capture.failed", "ScreenshotCaptureFailed");
                    }
                    capture = capture with { DeviceContext = deviceContext };
                    try
                    {
                        _screenshotPublications.Publish(capture, captureRoot, settings.InstallationId, settings.KeepScreenshots);
                        if (settings.KeepScreenshots)
                            PersistScreenshotIntervalTelemetry(capture, telemetryIntervalStartedAt, DateTimeOffset.UtcNow);
                    }
                    catch
                    {
                        // Persist rollback even when publication failed between individual monitor images.
                        CleanupAbandonedCapture(capture);
                        throw;
                    }

                    await NotifyScreenshotCaptureAsync(capture, cancellationToken).ConfigureAwait(false);
                    var pipeline = await AnalyzeLiveCaptureAsync(
                        capture,
                        settings.KeepScreenshots,
                        origin,
                        cancellationToken);
                    capture = pipeline.Capture;
                    result = pipeline.Analysis;
                }
                else
                {
                    var deviceContext = await CaptureScreenshotContextAsync(cancellationToken).ConfigureAwait(false);
                    result = await RunLiveAnalysisOutsideMutationAsync(async () =>
                    {
                        _ = await LoadValidatedAiSettingsAtVisualBoundaryAsync(requireImageInput: false, cancellationToken);
                        return await _analysis.AnalyzeCurrentScreenAsync(
                            (_tracking.LatestAnalysisContext ?? new AnalysisContextSnapshot(
                                "not available", "not available", "not available", "active", null)) with
                            { DeviceContext = deviceContext },
                            allowCapture: false,
                            origin,
                            cancellationToken).ConfigureAwait(false);
                    }, cancellationToken);
                }

                return OperationResult<AiAnalysis>.Success("ai.analyzed", "AiAnalyzed", result);
            }
            finally
            {
                if (capture is not null)
                {
                    CleanupCaptureArtifacts(capture, settings.KeepScreenshots);
                }
            }
        }
        catch (OperationCanceledException)
        {
            return OperationResult<AiAnalysis>.Failure("operation.cancelled", "OperationCancelled");
        }
        catch (AiProviderRequestException exception)
        {
            LogAiProviderFailure("ai.analyze", exception);
            return OperationResult<AiAnalysis>.Failure(ProviderFailureCode(exception), ProviderFailureMessage(exception));
        }
        catch (AiDailyAnalysisQuotaReachedException)
        {
            return OperationResult<AiAnalysis>.Failure("ai.daily_limit", "AiDailyLimit");
        }
        catch (AiLiveAnalysisPreflightException exception)
        {
            return OperationResult<AiAnalysis>.Failure(exception.Code, exception.MessageKey);
        }
        catch (InvalidOperationException)
        {
            return OperationResult<AiAnalysis>.Failure("ai.configuration.invalid", "AiConfigurationInvalid");
        }
        catch (Exception exception)
        {
            // Provider/network errors are intentionally not serialized into diagnostics or IPC payloads.
            _logger.LogWarning("AI analysis failed. ExceptionType={ExceptionType}", exception.GetType().Name);
            return OperationResult<AiAnalysis>.Failure("ai.provider.failed", "AiProviderFailed");
        }
    }, cancellationToken);

    /// <inheritdoc />
    public Task<OperationResult<string>> OpenUserInterfaceAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var executable = Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "WorkTrail.exe");
            var startInfo = new ProcessStartInfo
            {
                FileName = executable,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            startInfo.ArgumentList.Add("--ui");
            Process.Start(startInfo);
            return Task.FromResult(OperationResult<string>.Success("ui.opened", "UiOpened", "WorkTrail UI"));
        }
        catch (Exception exception)
        {
            _logger.LogWarning("WinUI frontend launch failed. ExceptionType={ExceptionType}", exception.GetType().Name);
            return Task.FromResult(OperationResult<string>.Failure("shell.open.failed", "UiOpenFailed"));
        }
    }

    /// <inheritdoc />
    public Task<OperationResult<IReadOnlyList<PrivacyRule>>> GetPrivacyRulesAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(OperationResult<IReadOnlyList<PrivacyRule>>.Success("privacy.list.loaded", "PrivacyRulesLoaded", ReadPrivacyRules(_settingsSnapshot.Value)));
    }

    /// <inheritdoc />
    public Task<OperationResult<PrivacyRule>> AddPrivacyRuleAsync(string type, string value, CancellationToken cancellationToken) => MutateVisualStateAsync(async () =>
    {
        if (type is not ("process" or "title" or "hint") || string.IsNullOrWhiteSpace(value))
        {
            return OperationResult<PrivacyRule>.Failure("privacy.rule.invalid", "PrivacyRuleInvalid", new ValidationIssue("rule", "invalid", "PrivacyRuleInvalid"));
        }

        var rule = new PrivacyRule(Guid.NewGuid().ToString("N"), type, value.Trim());
        var settings = _settingsSnapshot.Value;
        var all = ReadPrivacyRules(settings).Append(rule).ToArray();
        SavePrivacyRules(settings, all);
        await Task.CompletedTask;
        return OperationResult<PrivacyRule>.Success("privacy.rule.added", "PrivacyRuleAdded", rule);
    }, cancellationToken);

    /// <inheritdoc />
    public Task<OperationResult<bool>> RemovePrivacyRuleAsync(string id, CancellationToken cancellationToken) => MutateVisualStateAsync(async () =>
    {
        var settings = _settingsSnapshot.Value;
        var rules = ReadPrivacyRules(settings);
        var filtered = rules.Where(x => !string.Equals(x.Id, id, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (filtered.Length == rules.Count)
        {
            return OperationResult<bool>.Failure("privacy.rule.not_found", "PrivacyRuleNotFound", new ValidationIssue("id", "not_found", "PrivacyRuleNotFound"));
        }

        SavePrivacyRules(settings, filtered);
        await Task.CompletedTask;
        return OperationResult<bool>.Success("privacy.rule.removed", "PrivacyRuleRemoved", true);
    }, cancellationToken);

    /// <inheritdoc />
    public Task<OperationResult<bool>> TestCurrentPrivacyAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(OperationResult<bool>.Success("privacy.test.completed", "PrivacyTestCompleted", IsCurrentContextPrivate(_settingsSnapshot.Value)));
    }

    /// <inheritdoc />
    public async Task<OperationResult<RetentionStatus>> GetRetentionStatusAsync(CancellationToken cancellationToken) =>
        OperationResult<RetentionStatus>.Success("retention.status.loaded", "RetentionStatusLoaded",
            await Task.Run(() => BuildRetentionStatus(DateTimeOffset.Now), cancellationToken).ConfigureAwait(false));

    // now supplies the local date used to decide whether the daily cleanup is due.
    // InvalidOperationException reports a missing durable installation profile.
    private RetentionStatus BuildRetentionStatus(DateTimeOffset now)
    {
        var settings = _settingsSnapshot.Value;
        var tier = _featureAccess.Snapshot.Tier;
        var installation = _store.GetInstallationProfile(settings.InstallationId)
            ?? throw new InvalidOperationException("The current installation has no durable activation profile.");
        var first = DateOnly.FromDateTime(installation.FirstSeenAt.LocalDateTime);
        var last = settings.LastRetentionCleanupAt is { } completed
            ? DateOnly.FromDateTime(completed.LocalDateTime) : (DateOnly?)null;
        var next = RetentionPolicy.NextCleanupDate(first, last);
        return new RetentionStatus(
            RetentionPolicy.EffectiveMonths(settings.DataRetentionDays, tier) * 30,
            RetentionPolicy.EffectiveMonths(settings.ScreenshotRetentionDays, tier) * 30,
            settings.ScreenshotDirectory, RetentionPolicy.MaximumMonths(tier), first, last, next,
            DateOnly.FromDateTime(now.LocalDateTime) >= next, Volatile.Read(ref _retentionCleanupProgress), settings.KeepScreenshots,
            settings.ScreenshotsEnabled, settings.ScreenshotCaptureMode,
            MeasureHistoryStorage(settings.ScreenshotDirectory), settings.HistoryWarningMegabytes);
    }

    /// <inheritdoc />
    public async Task<OperationResult<RetentionPreview>> PreviewRetentionAsync(CancellationToken cancellationToken, HistoryDateRange? range = null) =>
        range is { IsValid: false } ? InvalidHistoryRange() : OperationResult<RetentionPreview>.Success(
            "retention.preview.loaded",
            "RetentionPreviewLoaded",
            await Task.Run(() => BuildRetentionPreview(cancellationToken, range: range), cancellationToken).ConfigureAwait(false));

    /// <inheritdoc />
    /// <param name="request">Confirmed manual cleanup or a daily scheduled check.</param>
    /// <param name="cancellationToken">Stops cleanup without advancing its durable checkpoint.</param>
    public Task<OperationResult<RetentionPreview>> RunRetentionAsync(RetentionRequest request, CancellationToken cancellationToken) => MutateVisualStateAsync(() => Task.Run(async () =>
    {
        if (request.Range is { IsValid: false } || (request.Scheduled && request.Range is not null))
            return InvalidHistoryRange();
        if (!request.Execute || (!request.Confirmed && !request.Scheduled))
        {
            return OperationResult<RetentionPreview>.Failure("retention.confirmation.required", "RetentionConfirmationRequired", new ValidationIssue("confirmation", "required", "RetentionConfirmationRequired"));
        }

        var now = DateTimeOffset.Now;
        if (request.Scheduled && !BuildRetentionStatus(now).IsCleanupDue)
            return OperationResult<RetentionPreview>.Success("retention.not_due", "RetentionCompleted", new RetentionPreview(0, 0, []));

        await RecoverScreenshotDeletionsAsync(cancellationToken).ConfigureAwait(false);
        var settings = _settingsSnapshot.Value;
        var tier = _featureAccess.Snapshot.Tier;
        var screenshotCutoff = request.Range?.EndExclusive ?? now.AddMonths(-RetentionPolicy.EffectiveMonths(settings.ScreenshotRetentionDays, tier));
        var dataCutoff = request.Range?.EndExclusive ?? now.AddMonths(-RetentionPolicy.EffectiveMonths(settings.DataRetentionDays, tier));
        if (request.Range is null)
        {
            _screenshotPublications.CleanupUnclaimedStages(settings.ScreenshotDirectory, screenshotCutoff, cancellationToken);
            _screenshotPublications.CleanupUnclaimedStages(_screenshotPublications.TransientRoot, now.AddDays(-1), cancellationToken);
        }
        var deletionBatch = new ScreenshotDeletionJournal.Batch();
        var ownedArtifacts = deletionBatch.Inventory(ScreenshotStorageLayout.NormalizeRoot(settings.ScreenshotDirectory), cancellationToken)
            .Values.SelectMany(paths => paths).ToArray();
        var preview = BuildRetentionPreview(cancellationToken, now, tier, ownedArtifacts, request.Range);
        if (preview.Paths.Any(ScreenCaptureService.IsOwnedArtifact)) _screenshotNotifications?.Clear();
        var operationId = request.OperationId == Guid.Empty ? Guid.NewGuid() : request.OperationId;
        long completedItems = 0;
        // phase identifies the current cleanup step for the passive progress surface.
        void Report(string phase) => Volatile.Write(ref _retentionCleanupProgress,
            new RetentionCleanupProgress(operationId, completedItems, preview.FileCount, phase));
        Report("Screenshots");
        var deletions = new List<ScreenshotDeletionJournal.Plan>();
        var completedArtifacts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in preview.Paths.Where(ScreenCaptureService.IsOwnedArtifact))
        {
            cancellationToken.ThrowIfCancellationRequested();
            // A plan removes both stored and analysis variants; do not replay it for the next preview path.
            if (completedArtifacts.Contains(path)) continue;
            var deletion = _screenshotDeletions.Begin(path, deleteAnalysis: false, deletionBatch, cancellationToken);
            if (deletion is null) continue;
            _screenshotDeletions.Execute(deletion, deletionBatch, cancellationToken);
            deletions.Add(deletion);
            completedArtifacts.UnionWith(deletion.Artifacts);
            completedItems += deletion.Artifacts.Length;
            Report("Screenshots");
        }

        cancellationToken.ThrowIfCancellationRequested();
        Report("Records");
        var deletedScreenshots = completedItems;
        _store.ApplyRetention(dataCutoff,
            removedRecords =>
            {
                completedItems = deletedScreenshots + Math.Min(removedRecords, preview.RecordCount);
                Report("Records");
            }, request.Range?.Start, cancellationToken);
        completedItems = deletedScreenshots + preview.RecordCount;
        Report("Search");
        _store.PruneTerminalAiReprocessJobs(screenshotCutoff, request.Range?.Start, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        _store.PruneOrphanedScreenshotCaptures(screenshotCutoff, request.Range?.Start, cancellationToken);

        await _search.SynchronizeAsync(cancellationToken).ConfigureAwait(false);
        foreach (var deletion in deletions) _screenshotDeletions.Complete(deletion);
        cancellationToken.ThrowIfCancellationRequested();
        // Advance the daily schedule only after deletion and derived-index synchronization succeed.
        if (request.Range is null) PersistSettings(_settingsSnapshot.Value with { LastRetentionCleanupAt = now });
        InvalidateHistoryStorageMeasurement();
        Report("Completed");
        return OperationResult<RetentionPreview>.Success("retention.completed", "RetentionCompleted", preview);
    }, cancellationToken), cancellationToken);

    /// <inheritdoc />
    public Task<OperationResult<AtomicResetPlan>> PrepareAtomicResetAsync(
        AtomicResetRequest request,
        CancellationToken cancellationToken) => MutateVisualStateAsync(async () =>
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!request.FirstConfirmation || !request.FinalConfirmation)
        {
            return OperationResult<AtomicResetPlan>.Failure(
                "app.atomic_reset.confirmation_required",
                "AtomicResetConfirmationRequired",
                new ValidationIssue("confirmation", "required_twice", "AtomicResetConfirmationRequired"));
        }

        AtomicResetPlan plan;
        try
        {
            var settings = _settingsSnapshot.Value;
            plan = _atomicReset.CreatePlan(_store.DataDirectory, settings.ScreenshotDirectory);
        }
        catch (Exception exception) when (
            exception is ArgumentException or
                InvalidOperationException or
                IOException or
                UnauthorizedAccessException)
        {
            _logger.LogWarning("Atomic reset preparation failed. ExceptionType={ExceptionType}", exception.GetType().Name);
            return OperationResult<AtomicResetPlan>.Failure("app.atomic_reset.unavailable", "AtomicResetUnavailable");
        }

        if (!await _startup.SetEnabledAsync(false, cancellationToken).ConfigureAwait(false))
        {
            return OperationResult<AtomicResetPlan>.Failure("app.atomic_reset.startup_cleanup_failed", "AtomicResetStartupCleanupFailed");
        }

        _atomicResetPrepared = true;
        _scheduledSnapshotsEnabled = false;
        _nextScheduledSnapshotAt = null;
        _pausedScheduledSnapshotRemaining = null;
        _runtimeTimerCancellation.Cancel();
        _tracking.Stop();
        _logger.LogWarning("Atomic reset prepared after two explicit confirmations.");
        await Task.CompletedTask;
        return OperationResult<AtomicResetPlan>.Success("app.atomic_reset.prepared", "AtomicResetPrepared", plan);
    }, cancellationToken);

    /// <inheritdoc />
    public Task<OperationResult<IReadOnlyList<PluginInfo>>> GetPluginsAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(OperationResult<IReadOnlyList<PluginInfo>>.Success("plugins.list.loaded", "PluginsLoaded", BuildPlugins(_settingsSnapshot.Value)));
    }

    /// <inheritdoc />
    public Task<OperationResult<PluginInfo>> GetPluginAsync(string id, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var plugin = BuildPlugins(_settingsSnapshot.Value).SingleOrDefault(x => string.Equals(x.Id, id, StringComparison.OrdinalIgnoreCase));
        return Task.FromResult(plugin is null
            ? OperationResult<PluginInfo>.Failure("plugins.not_found", "PluginNotFound", new ValidationIssue("id", "not_found", "PluginNotFound"))
            : OperationResult<PluginInfo>.Success("plugins.loaded", "PluginLoaded", plugin));
    }

    /// <inheritdoc />
    public Task<OperationResult<PluginInfo>> SetPluginEnabledAsync(string id, bool enabled, CancellationToken cancellationToken) => MutateAsync(async () =>
    {
        var settings = _settingsSnapshot.Value;
        var updated = id.ToLowerInvariant() switch
        {
            "word" => settings with { EnableWordDetailPlugin = enabled },
            "excel" => settings with { EnableExcelDetailPlugin = enabled },
            "vscode" => settings with { EnableVsCodeDetailPlugin = enabled },
            "browser" => settings with { EnableBrowserDetailPlugin = enabled },
            _ => null
        };
        if (updated is null)
        {
            return OperationResult<PluginInfo>.Failure("plugins.not_found", "PluginNotFound", new ValidationIssue("id", "not_found", "PluginNotFound"));
        }

        PersistSettings(updated);
        var plugin = BuildPlugins(updated).Single(x => x.Id == id.ToLowerInvariant());
        await Task.CompletedTask;
        return OperationResult<PluginInfo>.Success(enabled ? "plugins.enabled" : "plugins.disabled", enabled ? "PluginEnabled" : "PluginDisabled", plugin);
    }, cancellationToken);

    /// <inheritdoc />
    public Task<OperationResult<AppSettings>> GetSettingsAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(OperationResult<AppSettings>.Success("settings.loaded", "SettingsLoaded", _settingsSnapshot.Value));
    }

    /// <inheritdoc />
    public Task<OperationResult<WorldClockCityCatalog>> GetWorldClockCityCatalogAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var result = _worldClockOperations.GetCatalog();
        if (!result.Succeeded || result.Value is null) return Task.FromResult(result);
        var count = WorldClockSelection.NormalizePersisted(_settingsSnapshot.Value.WorldClockCityIds).Count;
        return Task.FromResult(result with { Value = result.Value with { AddDeniedMessageKey = _featureAccess.DeniedWorldClockCreation(count)?.MessageKey } });
    }

    /// <inheritdoc />
    public async Task<OperationResult<WorldClockSnapshot>> GetWorldClocksAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return await _worldClockOperations.GetCurrentAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task<OperationResult<WorldClockSnapshot>> GetCelestialReferenceAsync(CancellationToken cancellationToken) =>
        _worldClockOperations.GetCelestialReferenceAsync(cancellationToken);

    /// <inheritdoc />
    public async Task<OperationResult<CelestialSnapshot>> GetCelestialAsync(CelestialRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        // Ephemeris searches run off the presentation dispatcher; failures propagate without fabricated events.
        var snapshot = await Task.Run(() =>
        {
            var catalog = _worldClockOperations.GetCatalog().Value
                ?? throw new InvalidDataException("The celestial city catalog is unavailable.");
            return CelestialService.Build(request, catalog, cancellationToken);
        }, cancellationToken).ConfigureAwait(false);
        if (request.IncludeSatellites)
            snapshot = snapshot with
            {
                Satellites = await CelestialSatelliteService.GetPositionsAsync(snapshot, cancellationToken).ConfigureAwait(false)
            };
        var zone = TimeZoneInfo.FindSystemTimeZoneById(snapshot.TimeZoneId);
        var calendar = CelestialCalendarCatalog.Current;
        await _mutations.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // The same activity database owns calendar rows; keep its asset refresh serialized with other writes.
            _store.EnsureCelestialCalendar(calendar);
        }
        finally
        {
            _mutations.Release();
        }

        var preferences = _settingsSnapshot.Value;
        var firstDate = DateOnly.FromDateTime(snapshot.LocalTime.DateTime);
        var lastDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(snapshot.InstantUtc.AddDays(2), zone).DateTime);
        var holidays = firstDate <= calendar.CoverageEnd && lastDate >= calendar.CoverageStart
            ? _store.LoadCalendarHolidays(firstDate, lastDate)
            : [];
        var saints = preferences.AstronomyAgendaShowSaints && firstDate <= calendar.CoverageEnd && lastDate >= calendar.CoverageStart
            ? Enumerable.Range(0, lastDate.DayNumber - firstDate.DayNumber + 1)
                .SelectMany(offset => _store.LoadCalendarSaints(firstDate.AddDays(offset))).ToArray()
            : [];
        var calendarEvents = CelestialCalendarEventService.Build(snapshot, zone, holidays, saints,
            preferences.AstronomyAgendaCountryCodes ?? [], preferences.AstronomyAgendaShowSaints, cancellationToken);
        var spaceWeather = await CelestialSpaceWeatherService.GetAsync(snapshot.InstantUtc, zone, cancellationToken).ConfigureAwait(false);
        snapshot = snapshot with
        {
            SpaceWeather = spaceWeather.Snapshot,
            Agenda = Array.AsReadOnly(snapshot.Agenda.Concat(calendarEvents).Concat(spaceWeather.Agenda)
                .Select(item => item with
                {
                    IsAtReferenceInstant = item.StartUtc <= snapshot.InstantUtc && item.EndUtc is { } end && snapshot.InstantUtc < end
                })
                .OrderBy(item => item.StartUtc).ThenBy(item => item.Kind).ToArray())
        };
        return OperationResult<CelestialSnapshot>.Success("celestial.loaded", "WorldClocksLoaded", snapshot);
    }

    /// <inheritdoc />
    public async Task<OperationResult<CelestialMapImage>> GetCelestialMapAsync(CelestialMapRequest request, CancellationToken cancellationToken)
    {
        var image = await _celestialMap.RenderAsync(request, cancellationToken).ConfigureAwait(false);
        return OperationResult<CelestialMapImage>.Success("celestial.map.loaded", "WorldClocksLoaded", image);
    }

    /// <inheritdoc />
    public Task<OperationResult<WorldClockSnapshot>> ConvertWorldClocksAsync(
        WorldClockConversionRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(_worldClockOperations.Convert(request));
    }

    /// <inheritdoc />
    public Task<OperationResult<WorldClockSelectionState>> AddWorldClockAsync(
        string cityId,
        CancellationToken cancellationToken)
    {
        var normalizedId = _worldClockOperations.NormalizeAndValidateCityId(cityId);
        return MutateAsync(
            () =>
            {
                var selection = WorldClockSelection.NormalizePersisted(_settingsSnapshot.Value.WorldClockCityIds);
                // Recheck under the same mutation lock as persistence; a previously opened picker is not an entitlement.
                if (!selection.Contains(normalizedId) && _featureAccess.DeniedWorldClockCreation(selection.Count) is { } denied)
                    return Task.FromResult(OperationResult<WorldClockSelectionState>.Failure("feature.clock_limit", denied.MessageKey, denied));
                return Task.FromResult(_worldClockOperations.AddValidated(normalizedId));
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<OperationResult<WorldClockSelectionState>> RemoveWorldClockAsync(
        string cityId,
        CancellationToken cancellationToken) => MutateAsync(
            () => Task.FromResult(_worldClockOperations.Remove(cityId)),
            cancellationToken);

    /// <inheritdoc />
    public Task<OperationResult<WorldClockSelectionState>> MoveWorldClockAsync(
        string cityId,
        WorldClockMoveDirection direction,
        CancellationToken cancellationToken)
    {
        var normalizedId = _worldClockOperations.NormalizeAndValidateCityId(cityId);
        return MutateAsync(
            () => Task.FromResult(_worldClockOperations.MoveValidated(normalizedId, direction)),
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<OperationResult<string>> SetWorldClockWeatherKeyAsync(
        string secret,
        CancellationToken cancellationToken) => MutateVisualStateAsync(
            () => _worldClockOperations.SetWeatherKeyAsync(secret, cancellationToken),
            cancellationToken);

    /// <inheritdoc />
    public async Task<OperationResult<AppSettings>> ApplyQuickSetupProfileAsync(
        QuickSetupProfileRequest request,
        CancellationToken cancellationToken)
    {
        var profile = QuickSetupProfileCatalog.CreatePatch(request);
        if (!profile.Succeeded || profile.Value is null)
        {
            return OperationResult<AppSettings>.Failure(
                profile.Code,
                profile.MessageKey,
                profile.Issues.ToArray());
        }

        return await PatchSettingsAsync(profile.Value, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task<OperationResult<FeatureAccessSnapshot>> GetFeatureAccessAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(OperationResult<FeatureAccessSnapshot>.Success("features.loaded", "Premium.Status", _featureAccess.Snapshot));
    }

#if DEBUG
    /// <inheritdoc />
    public Task<OperationResult<FeatureAccessSnapshot>> SimulateFeatureAccessAsync(ProductTier tier, CancellationToken cancellationToken) => MutateVisualStateAsync(async () =>
    {
        if (!Enum.IsDefined(tier))
            return OperationResult<FeatureAccessSnapshot>.Failure("features.invalid_tier", "Premium.InvalidTier");
        _featureAccess.Simulate(tier);
        await Task.CompletedTask;
        return OperationResult<FeatureAccessSnapshot>.Success("features.simulated", "Premium.Status", _featureAccess.Snapshot);
    }, cancellationToken);
#endif

    /// <inheritdoc />
    public Task<OperationResult<AppSettings>> PatchSettingsAsync(SettingsPatch patch, CancellationToken cancellationToken) => MutateVisualStateAsync(async () =>
    {
        var settings = _settingsSnapshot.Value;
        if (_featureAccess.DeniedSetting(patch, settings) is { } denied)
            return OperationResult<AppSettings>.Failure("feature.premium_required", "Premium.Required", denied);
        var validation = SettingsCatalog.Apply(settings, patch);
        if (!validation.Succeeded || validation.Value is null)
        {
            return validation;
        }

        var current = validation.Value;
        if (patch.Values.Count == 1
            && (patch.Values.ContainsKey("screenshots.notifications") || patch.Values.ContainsKey("notifications.enabled")))
        {
            // Notification preferences must work even when unrelated AI or startup configuration is unavailable.
            PersistSettings(current);
            return OperationResult<AppSettings>.Success("settings.saved", "SettingsSaved", current);
        }

        if (patch.Values.Keys.Any(key => string.Equals(key, "astronomy.agenda.city_id", StringComparison.OrdinalIgnoreCase))
            && current.AstronomyAgendaCityId.Length > 0)
        {
            var selectedCity = current.AstronomyAgendaCityId;
            try
            {
                _worldClockOperations.NormalizeAndValidateCityId(selectedCity);
            }
            catch (ArgumentException)
            {
                return OperationResult<AppSettings>.Failure("settings.validation.failed", "SettingsValidationFailed",
                    new ValidationIssue("astronomy.agenda.city_id", "unknown_city", "SettingsFieldInvalid"));
            }

            if (!WorldClockSelection.NormalizePersisted(current.WorldClockCityIds).Contains(selectedCity, StringComparer.Ordinal))
            {
                return OperationResult<AppSettings>.Failure("settings.validation.failed", "SettingsValidationFailed",
                    new ValidationIssue("astronomy.agenda.city_id", "city_not_selected", "SettingsFieldInvalid"));
            }
        }
        if (_featureAccess.DeniedSettingsChange(settings, current) is { } quotaIssue)
            return OperationResult<AppSettings>.Failure("feature." + quotaIssue.Code, quotaIssue.MessageKey, quotaIssue);
        if (!TryValidateOpenAiConfiguration(current, requireImageInput: false, out var validatedSettings, out var validationIssue))
        {
            return OperationResult<AppSettings>.Failure(
                "settings.validation.failed",
                "SettingsValidationFailed",
                validationIssue!);
        }

        current = validatedSettings;
        if (current.OpenAiEnabled
            && patch.Values.TryGetValue("quick_setup.completed", out var completed)
            && string.Equals(completed, "true", StringComparison.OrdinalIgnoreCase)
            && !_aiConnectionVerification.Matches(current, current.AiProvider == "codex-cli" ? string.Empty : _store.LoadApiKey(current.AiApiKeyName)))
        {
            return OperationResult<AppSettings>.Failure(
                "quick_setup.ai.verification_required", "ProviderSetup.Error.VerificationRequired",
                new ValidationIssue("ai.enabled", "connection_required", "ProviderSetup.Error.VerificationRequired"));
        }
        var startupChanged = current.StartWithWindows != settings.StartWithWindows;
        var startupNeedsRepair = current.StartWithWindows
            && !await _startup.IsEnabledAsync(cancellationToken).ConfigureAwait(false);
        var startupUpdated = StartupRegistrationPolicy.RequiresUpdate(
            settings.StartWithWindows,
            current.StartWithWindows,
            !startupNeedsRepair);
        if (startupUpdated
            && !await _startup.SetEnabledAsync(current.StartWithWindows, cancellationToken).ConfigureAwait(false))
        {
            return OperationResult<AppSettings>.Failure(
                "startup.update.failed",
                "StartupUpdateFailed",
                new ValidationIssue("startup.enabled", "os_update_failed", "StartupUpdateFailed"));
        }

        try
        {
            PersistSettings(current);
        }
        catch
        {
            if (startupChanged && current.StartWithWindows != _settingsSnapshot.Value.StartWithWindows
                && !await _startup.SetEnabledAsync(_settingsSnapshot.Value.StartWithWindows, CancellationToken.None).ConfigureAwait(false))
            {
                _logger.LogError("Startup state rollback failed after settings persistence error.");
            }

            throw;
        }

        if (startupUpdated)
        {
            _logger.LogInformation(
                "Windows startup state updated. Enabled={Enabled}; Repaired={Repaired}",
                current.StartWithWindows,
                startupNeedsRepair);
        }
        ConfigureScheduledSnapshots(
            current,
            restartCountdown: current.ScreenshotIntervalMinutes != settings.ScreenshotIntervalMinutes
                || current.ScreenshotsEnabled != settings.ScreenshotsEnabled);
        await Task.CompletedTask;
        return OperationResult<AppSettings>.Success("settings.saved", "SettingsSaved", current);
    }, cancellationToken);

    /// <inheritdoc />
    public Task<OperationResult<WindowState?>> RestoreWindowStateAsync(string windowKey, long windowHandle, CancellationToken cancellationToken) => MutateAsync(async () =>
    {
        var state = _windowState.Restore(windowKey, windowHandle);
        await Task.CompletedTask;
        return OperationResult<WindowState?>.Success(
            state is null ? "window.state.not_found" : "window.state.restored",
            state is null ? "WindowStateNotFound" : "WindowStateRestored",
            state);
    }, cancellationToken);

    /// <inheritdoc />
    public Task<OperationResult<int>> RevealOpenWindowsAsync(WindowRevealRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var count = new OpenWindowVisibilityService().Reveal(request, cancellationToken);
            return Task.FromResult(OperationResult<int>.Success("window.reveal.completed", "WindowRevealCompleted", count));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            // A failed reveal is reported without retrying activation or reopening persisted workspace windows.
            _logger.LogWarning("Open windows could not be revealed. ExceptionType={ExceptionType}", exception.GetType().Name);
            return Task.FromResult(OperationResult<int>.Failure("window.reveal.failed", "Window.RevealFailed"));
        }
    }

    /// <inheritdoc />
    public Task<OperationResult<WindowState>> SaveWindowStateAsync(string windowKey, long windowHandle, CancellationToken cancellationToken) => MutateAsync(async () =>
    {
        var (state, settings) = _windowState.Save(windowKey, windowHandle);
        // Publish the committed settings before another serialized mutation can overwrite the saved placement.
        _settingsSnapshot.Replace(settings);
        if (!settings.NotificationsEnabled || !settings.ScreenshotNotificationsEnabled) _screenshotNotifications?.Clear();
        await Task.CompletedTask;
        return OperationResult<WindowState>.Success("window.state.saved", "WindowStateSaved", state);
    }, cancellationToken);

    /// <inheritdoc />
    public Task<OperationResult<bool>> SetWindowOpenStateAsync(string windowKey, bool isOpen, CancellationToken cancellationToken) => MutateAsync(async () =>
    {
        var settings = _windowState.SetOpenState(windowKey, isOpen);
        _settingsSnapshot.Replace(settings);
        await Task.CompletedTask;
        return OperationResult<bool>.Success("window.open_state.saved", "WindowOpenStateSaved", isOpen);
    }, cancellationToken);

    /// <inheritdoc />
    public async Task<OperationResult<bool>> GetStartupStatusAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var enabled = await _startup.IsEnabledAsync(cancellationToken).ConfigureAwait(false);
            return OperationResult<bool>.Success("startup.status.loaded", "StartupStatusLoaded", enabled);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                "Windows startup status could not be read. ExceptionType={ExceptionType}",
                exception.GetType().Name);
            return OperationResult<bool>.Failure("startup.status.unavailable", "StartupStatusUnavailable");
        }
    }

    /// <inheritdoc />
    public Task<OperationResult<bool>> SetStartupEnabledAsync(bool enabled, CancellationToken cancellationToken) => MutateAsync(async () =>
    {
        var settings = _settingsSnapshot.Value;
        bool success;
        try
        {
            success = await _startup.SetEnabledAsync(enabled, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                "Windows startup registration could not be updated. Enabled={Enabled} ExceptionType={ExceptionType}",
                enabled,
                exception.GetType().Name);
            return OperationResult<bool>.Failure("startup.unavailable", "StartupUpdateFailed");
        }

        if (!success)
        {
            return OperationResult<bool>.Failure("startup.failed", "StartupUpdateFailed");
        }

        try
        {
            PersistSettings(settings with { StartWithWindows = enabled });
        }
        catch
        {
            if (!await _startup.SetEnabledAsync(settings.StartWithWindows, CancellationToken.None).ConfigureAwait(false))
            {
                _logger.LogError("Startup state rollback failed after settings persistence error.");
            }

            throw;
        }
        await Task.CompletedTask;
        return OperationResult<bool>.Success(enabled ? "startup.enabled" : "startup.disabled", enabled ? "StartupEnabled" : "StartupDisabled", enabled);
    }, cancellationToken);

    private DashboardState LoadDashboardState() => EnrichDashboardState(_tracking.LoadCurrentDashboardState());

    private DashboardState EnrichDashboardState(DashboardState state)
    {
        var settings = _settingsSnapshot.Value;
        return state with
        {
            ScheduledSnapshotRemaining = GetScheduledSnapshotRemaining(),
            PendingManualScreenshot = GetPendingManualScreenshotState(),
            IsWithinActiveHours = ActiveHoursSchedule.IsWithinActiveHours(settings.ActiveHours, DateTimeOffset.Now),
            FeatureAccess = _featureAccess.Snapshot,
            SpanLabel = settings.SpanLabel
        };
    }

    // settings contains the validated preferences being committed to local storage and the runtime snapshot.
    private void PersistSettings(AppSettings settings)
    {
        var previous = _settingsSnapshot.Value;
        _store.SaveSettings(settings);
        _settingsSnapshot.Replace(settings);
        if (!settings.NotificationsEnabled || !settings.ScreenshotNotificationsEnabled) _screenshotNotifications?.Clear();
        if (previous.OpenAiEnabled && !settings.OpenAiEnabled)
        {
            // Also cancel work that registered while the priority disable command was acquiring its lease.
            CancelLiveWork();
        }
        if (!string.Equals(previous.SearchLanguage, settings.SearchLanguage, StringComparison.OrdinalIgnoreCase)
            || previous.SearchSynonymsEnabled != settings.SearchSynonymsEnabled
            || previous.SearchTypoToleranceEnabled != settings.SearchTypoToleranceEnabled
            || !string.Equals(previous.ScreenshotDirectory, settings.ScreenshotDirectory, StringComparison.OrdinalIgnoreCase))
        {
            _store.MarkSearchSourceRebuild("settings-search-projection");
        }
    }

    private PendingManualScreenshotState? GetPendingManualScreenshotState()
    {
        var registration = Volatile.Read(ref _pendingManualScreenshot);
        return registration?.Capture.StoredScreenshotPaths.FirstOrDefault() is { } screenshotPath
            && registration.ExpiresAt > DateTimeOffset.Now
                ? new PendingManualScreenshotState(screenshotPath, registration.ExpiresAt)
                : null;
    }

    private TimeSpan? GetScheduledSnapshotRemaining()
    {
        if (_tracking.IsTracking && _nextScheduledSnapshotAt is { } nextSnapshotAt)
        {
            return nextSnapshotAt > DateTimeOffset.Now
                ? nextSnapshotAt - DateTimeOffset.Now
                : TimeSpan.Zero;
        }

        return _pausedScheduledSnapshotRemaining;
    }

    private void LogAiProviderFailure(string operation, AiProviderRequestException exception)
    {
        _logger.LogWarning(
            "AI provider request failed at the application boundary. Operation={Operation} HttpStatus={HttpStatus} FailureCategory={FailureCategory} LatencyMs={LatencyMs} ProviderRequestId={ProviderRequestId}",
            operation,
            exception.Failure.HttpStatusCode,
            exception.Failure.FailureCode,
            exception.Failure.ElapsedMilliseconds,
            AiProviderTelemetry.SafeToken(exception.Failure.ProviderRequestId, 80));
    }

    // capture carries the owned worker destinations whose lifecycle this operation controls.
    // keepStoredArtifacts preserves retained images while removing analysis-only variants.
    private bool CleanupCaptureArtifacts(ScreenshotCaptureResult capture, bool keepStoredArtifacts)
    {
        if (keepStoredArtifacts)
        {
            try { _screenshotPublications.CleanupTransientArtifacts(capture.CaptureId); }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or Microsoft.Data.Sqlite.SqliteException)
            {
                _logger.LogWarning("Screenshot destination cleanup remains journaled. ExceptionType={ExceptionType}", exception.GetType().Name);
                return false;
            }
        }
        if (!keepStoredArtifacts)
        {
            try { _screenshotPublications.Discard(capture.CaptureId); }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                _logger.LogWarning("Transient screenshot cleanup remains pending. ExceptionType={ExceptionType}", exception.GetType().Name);
                // The durable rollback disposition must exist before any further file or metadata removal.
                return false;
            }
        }
        var storedPaths = capture.StoredScreenshotPaths.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var cleanupPaths = keepStoredArtifacts
            ? capture.AnalysisScreenshotPaths.Where(path => !storedPaths.Contains(path))
            : capture.AllScreenshotPaths;
        var removedAll = true;
        foreach (var path in cleanupPaths.SelectMany(path => new[] { path, ScreenshotPublicationJournal.StagingPath(path) })
                     .Distinct(StringComparer.OrdinalIgnoreCase).Where(File.Exists))
        {
            try
            {
                File.Delete(path);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // Image cleanup is best effort; provider failures remain the primary operation outcome.
                _logger.LogWarning("Screenshot cleanup failed. ExceptionType={ExceptionType}", exception.GetType().Name);
                removedAll = false;
            }
        }
        return removedAll;
    }

    // capture is abandoned only after its files have been removed or assigned a durable rollback intent.
    private void CleanupAbandonedCapture(ScreenshotCaptureResult capture)
    {
        _screenshotNotifications?.Clear();
        if (!CleanupCaptureArtifacts(capture, keepStoredArtifacts: false)) return;
        try
        {
            foreach (var storedPath in capture.StoredScreenshotPaths)
            {
                _store.DeleteScreenshotIntervalTelemetry(storedPath);
            }
        }
        catch (Exception exception)
        {
            // Files are already removed; stale derived metadata is reported but cannot become a new capture owner.
            _logger.LogWarning("Abandoned screenshot metadata cleanup failed. ExceptionType={ExceptionType}", exception.GetType().Name);
        }
    }

    private ScreenshotCaptureDecision EvaluateCaptureDecision(ScreenshotCaptureContext context) =>
        TrackingDomainService.EvaluateScreenshotCapture(_settingsSnapshot.Value, context);

    private ScreenshotCaptureDecision EvaluateScheduledCaptureDecision(ScreenshotCaptureContext context)
    {
        var settings = _settingsSnapshot.Value;
        var decision = TrackingDomainService.EvaluateScreenshotCapture(settings, context);
        if (decision != ScreenshotCaptureDecision.Allowed) return decision;
        // Recheck scheduling gates inside the capture worker after any queue wait, without affecting manual captures.
        return _tracking.IsTracking && ActiveHoursSchedule.IsWithinActiveHours(settings.ActiveHours, DateTimeOffset.Now)
            ? ScreenshotCaptureDecision.Allowed
            : ScreenshotCaptureDecision.UserIdle;
    }

    private static OperationResult<T> CapturePreconditionFailure<T>(ScreenshotCaptureDecision decision) => decision switch
    {
        ScreenshotCaptureDecision.ScreenshotsDisabled => OperationResult<T>.Failure("screenshot.disabled", "ScreenshotsDisabled"),
        ScreenshotCaptureDecision.PrivacyBlocked => OperationResult<T>.Failure("privacy.blocked", "PrivacyBlocked"),
        ScreenshotCaptureDecision.UserIdle => OperationResult<T>.Failure("screenshot.idle", "SnapshotScheduleNotDue"),
        _ => throw new ArgumentOutOfRangeException(nameof(decision), decision, "Allowed capture cannot be mapped to a failure.")
    };

    private DateTimeOffset ResolveScreenshotTelemetryIntervalStart(int fallbackIntervalMinutes)
    {
        var now = DateTimeOffset.UtcNow;
        var persistedBoundary = _store.LoadLatestScreenshotTelemetryCapturedAt();
        return persistedBoundary is { } boundary && boundary < now
            ? boundary
            : now.AddMinutes(-Math.Max(1, fallbackIntervalMinutes));
    }

    private void PersistScreenshotIntervalTelemetry(
        ScreenshotCaptureResult capture,
        DateTimeOffset intervalStartedAt,
        DateTimeOffset capturedAt)
    {
        var retainedPath = capture.StoredScreenshotPaths.FirstOrDefault(File.Exists);
        var provenanceCapturedAt = (capture.CapturedAt
            ?? (retainedPath is null
                ? capturedAt
                : new DateTimeOffset(File.GetLastWriteTimeUtc(retainedPath), TimeSpan.Zero)))
            .ToUniversalTime();
        var telemetry = _tracking.BuildScreenshotIntervalTelemetry(intervalStartedAt, provenanceCapturedAt);
        // Persist the activity interval with capture provenance, without collecting device sensors.
        _store.UpsertScreenshotIntervalTelemetry(capture.CaptureId, capture.StoredScreenshotPaths, telemetry);
    }

    private Task<(ScreenshotCaptureResult Capture, AiAnalysis Analysis)> AnalyzeLiveCaptureAsync(
        ScreenshotCaptureResult capture,
        bool keepCapture,
        string origin,
        CancellationToken cancellationToken) =>
        RunLiveAnalysisOutsideMutationAsync(async () =>
        {
            // Enforce the daily allowance immediately before the visual description request.
            if (!BuildRequestGate(_settingsSnapshot.Value).Allowed)
            {
                throw new AiDailyAnalysisQuotaReachedException();
            }

            cancellationToken.ThrowIfCancellationRequested();
            _ = await LoadValidatedAiSettingsAtVisualBoundaryAsync(requireImageInput: true, cancellationToken);
            var analysis = await _analysis.AnalyzeCapturedScreenAsync(
                _tracking.LatestAnalysisContext,
                capture,
                keepCapture,
                origin,
                cancellationToken).ConfigureAwait(false);
            return (capture, analysis);
        }, cancellationToken);

    private async Task<AppSettings> LoadValidatedAiSettingsAtVisualBoundaryAsync(bool requireImageInput, CancellationToken cancellationToken)
    {
        await _mutations.WaitAsync(cancellationToken).ConfigureAwait(false);
        try { return LoadValidatedAiSettingsAtVisualBoundary(requireImageInput); }
        finally { _mutations.Release(); }
    }

    private AppSettings LoadValidatedAiSettingsAtVisualBoundary(bool requireImageInput)
    {
        var current = _settingsSnapshot.Value;
        if (!current.OpenAiEnabled)
        {
            throw new AiLiveAnalysisPreflightException("ai.disabled", "AiDisabled");
        }

        if (IsCurrentContextPrivate(current))
        {
            throw new AiLiveAnalysisPreflightException("privacy.blocked", "PrivacyBlocked");
        }

        if (!TryValidateOpenAiConfiguration(
                current,
                requireImageInput,
                out var validated,
                out _))
        {
            throw new AiLiveAnalysisPreflightException("ai.configuration.invalid", "AiConfigurationInvalid");
        }

        if (!string.Equals(current.Model, validated.Model, StringComparison.Ordinal))
        {
            // The historical-job fingerprint canonicalizes model aliases, so this persisted cleanup cannot
            // invalidate a job between two durable items while the visual boundary is held.
            PersistSettings(validated);
        }

        return validated;
    }

    private async Task ProcessScheduledSnapshotAsync()
    {
        try
        {
            var expiredManualCapture = await MutateAsync(async () =>
            {
                var registration = Volatile.Read(ref _pendingManualScreenshot);
                if (registration is null || registration.ExpiresAt > DateTimeOffset.Now)
                {
                    return OperationResult<ScreenshotCaptureResult?>.Success("snapshot.pending.not_due", "PendingManualSnapshotNotDue");
                }

                Volatile.Write(ref _pendingManualScreenshot, null);
                await Task.CompletedTask;
                return OperationResult<ScreenshotCaptureResult?>.Success("snapshot.pending.expired", "PendingManualSnapshotExpired", registration.Capture);
            }, CancellationToken.None);

            if (expiredManualCapture.Succeeded && expiredManualCapture.Value is { } capture)
            {
                // Analysis begins only after the runtime closes the deletion window.
                await AnalyzeCapturedScreenshotAsync(
                    new AnalyzeCapturedScreenshotRequest(capture, KeepCapture: true, Origin: "snapshot.manual"),
                    CancellationToken.None);
            }

            var due = await MutateAsync(async () =>
            {
                if (!_tracking.IsTracking || _nextScheduledSnapshotAt is not { } nextSnapshotAt || DateTimeOffset.Now < nextSnapshotAt)
                {
                    return OperationResult<bool>.Success("snapshot.schedule.not_due", "SnapshotScheduleNotDue", false);
                }

                _nextScheduledSnapshotAt = DateTimeOffset.Now.AddMinutes(_scheduledSnapshotIntervalMinutes);
                await Task.CompletedTask;
                return OperationResult<bool>.Success("snapshot.schedule.due", "SnapshotScheduleDue", true);
            }, CancellationToken.None);

            if (!due.Succeeded || due.Value != true)
            {
                return;
            }

            var settings = _settingsSnapshot.Value;
            if (!ActiveHoursSchedule.IsWithinActiveHours(settings.ActiveHours, DateTimeOffset.Now))
            {
                return;
            }

            if (settings.ScreenshotsEnabled)
            {
                // The retained image is the primary result. AI enrichment is attempted only after local capture succeeds.
                var scheduledCapture = await CaptureScreenshotAsync(
                    new CaptureScreenshotRequest(
                        Mode: null,
                        Keep: true,
                        CaptureOrigin: ScreenshotCaptureOrigins.Scheduled,
                        DeferAiAnalysis: true),
                    CancellationToken.None);
                if (!scheduledCapture.Succeeded || scheduledCapture.Value is not { } retainedCapture)
                {
                    if (scheduledCapture.Code == "screenshot.idle") return;
                    _logger.LogWarning("Scheduled screen capture failed. Code={Code}", scheduledCapture.Code);
                    return;
                }

                if (retainedCapture.AnalysisScreenshotPaths.Count == 0) return;

                var analysis = await AnalyzeCapturedScreenshotAsync(
                    new AnalyzeCapturedScreenshotRequest(retainedCapture, KeepCapture: true, Origin: "snapshot.scheduled"),
                    CancellationToken.None);
                if (!analysis.Succeeded && analysis.Code != "ai.disabled")
                {
                    // Missing keys, request limits, and provider failures never remove the already retained snapshot.
                    _logger.LogWarning("Scheduled snapshot retained without AI analysis. Code={Code}", analysis.Code);
                }
            }
        }
        catch (ScreenshotIdlePolicyException exception)
        {
            _logger.LogWarning("Scheduled screen capture skipped because the idle policy was unavailable. ExceptionType={ExceptionType}", exception.InnerException?.GetType().Name);
        }
        catch (Exception exception)
        {
            // Timer failures are logged and the next configured interval remains available for a later attempt.
            _logger.LogWarning("Scheduled snapshot processing failed. ExceptionType={ExceptionType}", exception.GetType().Name);
        }
    }

    private Task<OperationResult<string>> OpenFolderAsync(string directory, string code, string messageKey, CancellationToken cancellationToken) => MutateAsync(async () =>
    {
        try
        {
            var normalized = _utilities.NormalizeDirectory(directory);
            // A valid configured screenshot path may not exist until the first capture is saved.
            Directory.CreateDirectory(normalized);
            var shellDirectory = ResolveShellVisibleDirectory(normalized);
            Directory.CreateDirectory(shellDirectory);
            // Shell invocation remains behind the application boundary, never in a UI or CLI renderer.
            Process.Start(new ProcessStartInfo { FileName = shellDirectory, UseShellExecute = true });
            await Task.CompletedTask;
            return OperationResult<string>.Success(code, messageKey, normalized);
        }
        catch (Exception)
        {
            return OperationResult<string>.Failure("shell.open.failed", "FolderOpenFailed");
        }
    }, cancellationToken);

    // normalizedDirectory is the canonical configured path seen from the application process.
    private static string ResolveShellVisibleDirectory(string normalizedDirectory)
    {
        var localAppData = Path.GetFullPath(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
        var relativeDirectory = Path.GetRelativePath(localAppData, normalizedDirectory);
        if (Path.IsPathRooted(relativeDirectory) ||
            relativeDirectory.Equals("..", StringComparison.Ordinal) ||
            relativeDirectory.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
        {
            return normalizedDirectory;
        }

        try
        {
            // MSIX redirects LocalAppData writes under LocalCache\Local, but Explorer runs outside that redirect.
            var localCache = Windows.Storage.ApplicationData.Current.LocalCacheFolder.Path;
            return Path.GetFullPath(Path.Combine(localCache, "Local", relativeDirectory));
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or
                UnauthorizedAccessException or
                System.Runtime.InteropServices.COMException)
        {
            return normalizedDirectory;
        }
    }

    private AiStatus BuildAiStatus()
    {
        var settings = _settingsSnapshot.Value;
        var usesCli = settings.AiProvider == "codex-cli";
        var installed = CodexCliInstallation.Find() is not null;
        var key = usesCli ? null : _store.LoadApiKey(settings.AiApiKeyName);
        var hasKey = !string.IsNullOrWhiteSpace(key);
        var canEnable = usesCli ? installed : AiApiKeyPolicy.LooksPlausible(settings.AiProvider, settings.AiApiKeyName, key);
        key = string.Empty;
        return new AiStatus(settings.OpenAiEnabled, settings.AiProvider, usesCli ? "codex-default" : settings.Model,
            usesCli ? string.Empty : settings.AiEndpoint, settings.AiApiKeyName, hasKey, canEnable, BuildRequestGate(settings))
        {
            CodexCliInstalled = installed
        };
    }

    private static bool ShouldNotifyAiAnalysisFailure(string code) =>
        code is not "operation.cancelled" and not "privacy.blocked" and not "ai.disabled";

    private void EnqueueAiAnalysisFailure(string code, string? detail = null)
    {
        if (string.Equals(code, "ai.daily_limit", StringComparison.Ordinal))
        {
            EnqueueAiDailyLimitReached();
            return;
        }

        EnqueueNotification(new ApplicationNotification(
            Guid.NewGuid(),
            DateTimeOffset.UtcNow,
            ApplicationNotificationSeverity.Error,
            "Notification.AiAnalysisFailed.Title",
            code.StartsWith("codex.", StringComparison.Ordinal) ? CodexCliDecoder.MessageKey(code) : "Notification.AiAnalysisFailed.Message",
            code,
            detail));
    }

    private void EnqueueAiDailyLimitReached()
    {
        var today = DateTime.Now;
        var dateStamp = checked((today.Year * 10_000) + (today.Month * 100) + today.Day);
        if (Interlocked.Exchange(ref _lastAiDailyLimitNotificationDateStamp, dateStamp) == dateStamp)
        {
            return;
        }

        var settings = _settingsSnapshot.Value;
        var gate = BuildRequestGate(settings);
        EnqueueNotification(new ApplicationNotification(
            Guid.NewGuid(),
            DateTimeOffset.UtcNow,
            ApplicationNotificationSeverity.Warning,
            "Notification.AiDailyLimitReached.Title",
            "Notification.AiDailyLimitReached.Message",
            "ai.daily_limit",
            $"{gate.DailyAnalysisCount} / {settings.OpenAiDailyLimit}"));
    }

    // exception describes the failed attempt, whose full details remain in the local log on every retry.
    private void EnqueueScreenshotCaptureFailure(Exception exception)
    {
        // Keep retrying capture, but show only the first failure until a complete capture succeeds.
        if (Interlocked.Exchange(ref _screenshotCaptureFailureNotificationActive, 1) != 0) return;
        var detail = $"{exception.GetType().Name}: {exception.Message}";
        EnqueueNotification(new ApplicationNotification(
            Guid.NewGuid(),
            DateTimeOffset.UtcNow,
            ApplicationNotificationSeverity.Error,
            "Notification.ScreenshotCaptureFailed.Title",
            "Notification.ScreenshotCaptureFailed.Message",
            "screenshot.capture.failed",
            detail));
    }

    private void EnqueueTrackingUnavailable(Exception exception) =>
        EnqueueNotification(new ApplicationNotification(
            Guid.NewGuid(),
            DateTimeOffset.UtcNow,
            ApplicationNotificationSeverity.Error,
            "Notification.TrackingUnavailable.Title",
            "Notification.TrackingUnavailable.Message",
            "tracking.start.failed",
            $"{exception.GetType().Name}: {exception.Message}"));

    private bool TryGetScreenshotStorageWarning(string directory)
    {
        try
        {
            var root = Path.GetPathRoot(directory);
            if (string.IsNullOrWhiteSpace(root))
            {
                return false;
            }

            var drive = new DriveInfo(root);
            if (!drive.IsReady || drive.AvailableFreeSpace >= MinimumScreenshotFreeBytes)
            {
                return false;
            }

            if (DateTimeOffset.UtcNow - _lastScreenshotStorageWarningAt >= ScreenshotStorageNotificationInterval)
            {
                _lastScreenshotStorageWarningAt = DateTimeOffset.UtcNow;
                var localizedDetail = new LocalizedNotificationDetail(
                    "Notification.ScreenshotStorageLow.Detail",
                    [
                        drive.AvailableFreeSpace / (1024m * 1024m),
                        MinimumScreenshotFreeBytes / (1024m * 1024m)
                    ]);
                EnqueueNotification(new ApplicationNotification(
                    Guid.NewGuid(),
                    DateTimeOffset.UtcNow,
                    ApplicationNotificationSeverity.Warning,
                    "Notification.ScreenshotStorageLow.Title",
                    "Notification.ScreenshotStorageLow.Message",
                    "screenshot.storage.low",
                    LocalizedDetail: localizedDetail));
            }

            return true;
        }
        catch (Exception exception)
        {
            // Storage inspection is advisory; an unavailable DriveInfo must not block a capture that may still succeed.
            _logger.LogDebug(exception, "Screenshot storage availability could not be inspected. ExceptionType={ExceptionType}", exception.GetType().Name);
            return false;
        }
    }

    private void EnqueueNotification(ApplicationNotification notification)
    {
        while (_notifications.Count >= MaximumPendingNotifications && _notifications.TryDequeue(out _))
        {
            // Oldest notifications are discarded first so a headless runtime remains memory-bounded.
        }

        _notifications.Enqueue(notification);
    }

    private static OperationResult<T> AiConfigurationFailure<T>(ValidationIssue issue,
        string code = "ai.configuration.invalid", string messageKey = "AiConfigurationInvalid") =>
        issue.Code.StartsWith("codex.", StringComparison.Ordinal)
            ? OperationResult<T>.Failure(issue.Code, issue.MessageKey, issue)
            : OperationResult<T>.Failure(code, messageKey, issue);

    private static string ProviderFailureCode(AiProviderRequestException exception) =>
        exception.Failure.FailureCode.StartsWith("codex.", StringComparison.Ordinal) ? exception.Failure.FailureCode : "ai.provider.failed";

    private static string ProviderFailureMessage(AiProviderRequestException exception) =>
        exception.Failure.FailureCode.StartsWith("codex.", StringComparison.Ordinal) ? CodexCliDecoder.MessageKey(exception.Failure.FailureCode) : "AiProviderFailed";

    private static string? BuildAiProviderFailureDetail(AiProviderRequestException exception)
    {
        var lines = new List<string>();
        if (exception.Failure.HttpStatusCode is { } statusCode)
        {
            lines.Add($"HTTP status: {statusCode}");
        }

        if (!string.IsNullOrWhiteSpace(exception.Failure.FailureCode))
        {
            lines.Add($"Failure: {exception.Failure.FailureCode}");
        }

        lines.Add($"Latency: {exception.Failure.ElapsedMilliseconds} ms");
        if (exception.Failure.ProviderProcessingMilliseconds is { } providerProcessingMilliseconds)
        {
            lines.Add($"Provider processing: {providerProcessingMilliseconds} ms");
        }

        var providerRequestId = AiProviderTelemetry.SafeToken(exception.Failure.ProviderRequestId, 80);
        if (providerRequestId is not null)
        {
            lines.Add($"Provider request id: {providerRequestId}");
        }

        var providerResponseId = AiProviderTelemetry.SafeToken(exception.Failure.ProviderResponseId, 80);
        if (providerResponseId is not null)
        {
            lines.Add($"Provider response id: {providerResponseId}");
        }

        return lines.Count == 0 ? null : string.Join(Environment.NewLine, lines);
    }

    private AnalysisRequestGate BuildRequestGate(AppSettings settings)
    {
        var count = _store.GetTodayAnalysisCount();
        var allowed = count < settings.OpenAiDailyLimit;
        return new AnalysisRequestGate(allowed, allowed ? null : "daily_limit", count);
    }

    private bool CanAnalyzeHistoricalImages(AppSettings settings)
    {
        if (!settings.OpenAiEnabled ||
            !TryValidateOpenAiConfiguration(settings, requireImageInput: true, out var validated, out _))
        {
            return false;
        }

        if (validated.AiProvider == "codex-cli") return true;
        var apiKey = _store.LoadApiKey(validated.AiApiKeyName);
        var plausible = AiApiKeyPolicy.LooksPlausible(validated.AiProvider, validated.AiApiKeyName, apiKey);
        apiKey = string.Empty;
        return plausible;
    }

    private bool IsCurrentContextPrivate(AppSettings settings)
    {
        var context = _tracking.LatestAnalysisContext;
        if (context is null)
        {
            // With configured rules, missing current metadata cannot prove that provider disclosure is safe.
            return TrackingDomainService.HasConfiguredPrivacyRules(settings);
        }

        return TrackingDomainService.IsHistoricalContextPrivate(
            settings,
            _tracking.LatestProcessName ?? string.Empty,
            context);
    }

    private static IReadOnlyList<PrivacyRule> ReadPrivacyRules(AppSettings settings) =>
        ReadPrivacyRules("process", settings.PrivacyProcessNames)
            .Concat(ReadPrivacyRules("title", settings.PrivacyWindowTitles))
            .Concat(ReadPrivacyRules("hint", settings.PrivacyWindowHints))
            .ToArray();

    private static IEnumerable<PrivacyRule> ReadPrivacyRules(string type, string raw) => raw.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Select(value => value.Split('|', 2))
        .Where(parts => parts.Length == 2)
        .Select(parts => new PrivacyRule(parts[0], type, parts[1]));

    private void SavePrivacyRules(AppSettings settings, IReadOnlyCollection<PrivacyRule> rules)
    {
        static string Serialize(IEnumerable<PrivacyRule> values) => string.Join('\n', values.Select(x => $"{x.Id}|{x.Value.Replace("|", "", StringComparison.Ordinal)}"));
        PersistSettings(settings with
        {
            PrivacyProcessNames = Serialize(rules.Where(x => x.Type == "process")),
            PrivacyWindowTitles = Serialize(rules.Where(x => x.Type == "title")),
            PrivacyWindowHints = Serialize(rules.Where(x => x.Type == "hint"))
        });
    }

    // cancellationToken stops the preview's filesystem and database projection.
    // referenceTime fixes the cutoff for one cleanup operation.
    // effectiveTier fixes the commercial retention limit for the same operation.
    // screenshotArtifacts reuses the bulk deletion inventory instead of scanning the root again.
    // range overrides automatic retention with the explicitly selected local calendar days.
    private RetentionPreview BuildRetentionPreview(CancellationToken cancellationToken, DateTimeOffset? referenceTime = null,
        ProductTier? effectiveTier = null, IReadOnlyList<string>? screenshotArtifacts = null, HistoryDateRange? range = null)
    {
        var settings = _settingsSnapshot.Value;
        var now = referenceTime ?? DateTimeOffset.Now;
        var tier = effectiveTier ?? _featureAccess.Snapshot.Tier;
        var screenshotCutoff = range?.EndExclusive ?? now.AddMonths(-RetentionPolicy.EffectiveMonths(settings.ScreenshotRetentionDays, tier));
        var dataCutoff = range?.EndExclusive ?? now.AddMonths(-RetentionPolicy.EffectiveMonths(settings.DataRetentionDays, tier));
        var retainedScreenshotPaths = screenshotArtifacts?.ToArray() ?? (Directory.Exists(settings.ScreenshotDirectory)
            ? ScreenshotStorageLayout.EnumerateOwnedArtifacts(settings.ScreenshotDirectory).ToArray()
            : []);
        var capturedAtByPath = _store.LoadScreenshotCaptureTimes(retainedScreenshotPaths, cancellationToken);
        var screenshotPaths = retainedScreenshotPaths
            .Where(path =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                // Persisted provenance is authoritative. Filesystem time is reserved for owned
                // orphan artifacts that have no surviving capture row.
                var capturedAt = capturedAtByPath.TryGetValue(path, out var persistedCapturedAt)
                    ? persistedCapturedAt
                    : new DateTimeOffset(File.GetLastWriteTimeUtc(path), TimeSpan.Zero);
                return capturedAt < screenshotCutoff && (range is null || capturedAt >= range.Start);
            })
            .ToArray();
        var dataPreview = _store.GetRetentionPreview(dataCutoff, range?.Start);
        var paths = screenshotPaths.Concat(dataPreview.Paths).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var screenshotBytes = screenshotPaths.Sum(path => new FileInfo(path).Length);
        return new RetentionPreview(screenshotPaths.Length + dataPreview.RecordCount, screenshotBytes + dataPreview.TotalBytes, paths,
            dataPreview.RecordCount, screenshotPaths.Length, dataPreview.TotalBytes, screenshotBytes);
    }

    private static IReadOnlyList<PluginInfo> BuildPlugins(AppSettings settings) =>
    [
        new PluginInfo("word", "Microsoft Word", settings.EnableWordDetailPlugin, "Extracts safe Word activity context."),
        new PluginInfo("excel", "Microsoft Excel", settings.EnableExcelDetailPlugin, "Extracts safe Excel activity context."),
        new PluginInfo("vscode", "Visual Studio Code", settings.EnableVsCodeDetailPlugin, "Extracts safe IDE activity context."),
        new PluginInfo("browser", "Browser", settings.EnableBrowserDetailPlugin, "Extracts safe browser activity context.")
    ];

    private AiModelDescriptor? ResolveAiModel(string identifier) => _aiModelCatalog.Models.FirstOrDefault(model =>
        string.Equals(model.Key, identifier, StringComparison.OrdinalIgnoreCase) ||
        model.Aliases.Contains(identifier, StringComparer.OrdinalIgnoreCase));

    private bool TryValidateOpenAiConfiguration(
        AppSettings settings,
        bool requireImageInput,
        out AppSettings validatedSettings,
        out ValidationIssue? issue)
    {
        validatedSettings = settings;
        issue = null;
        if (settings.AiProvider == "codex-cli")
        {
            if (settings.OpenAiEnabled && CodexCliInstallation.Find() is null)
            {
                issue = new ValidationIssue("ai.provider", "codex.not_installed", "Options.Codex.Missing");
                return false;
            }
            return true;
        }
        if (settings.OpenAiEnabled
            && !AiApiKeyPolicy.LooksPlausible(
                settings.AiProvider,
                settings.AiApiKeyName,
                _store.LoadApiKey(settings.AiApiKeyName)))
        {
            issue = new ValidationIssue("ai.enabled", "api_key_required", "AiKeyInvalid");
            return false;
        }

        if (!string.Equals(settings.AiProvider, "openai", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var selectedModel = ResolveAiModel(settings.Model);
        if (selectedModel is null)
        {
            issue = new ValidationIssue("ai.model", "unsupported", "AiModelUnsupported");
            return false;
        }

        if (requireImageInput && !selectedModel.SupportsImageInput)
        {
            issue = new ValidationIssue("ai.model", "image_input_unsupported", "AiModelImageInputUnsupported");
            return false;
        }

        if (!selectedModel.SupportedThinkingEfforts.Contains(settings.AiReasoningEffort, StringComparer.Ordinal))
        {
            issue = new ValidationIssue("ai.reasoning_effort", "unsupported", "AiThinkingEffortUnsupported");
            return false;
        }

        validatedSettings = settings with { Model = selectedModel.Key };
        return true;
    }

    private static string NormalizeAnalysisOrigin(string? origin) => origin?.Trim().ToLowerInvariant() switch
    {
        "winui.operations" => "winui.operations",
        "cli.ai" => "cli.ai",
        "snapshot.manual" => "snapshot.manual",
        "snapshot.scheduled" => "snapshot.scheduled",
        "snapshot.reprocess" => "snapshot.reprocess",
        "runtime.ai" => "runtime.ai",
        _ => "manual"
    };

    private sealed record PendingManualScreenshotRegistration(
        ScreenshotCaptureResult Capture,
        DateTimeOffset ExpiresAt);

    private sealed class AiLiveAnalysisPreflightException : InvalidOperationException
    {
        internal AiLiveAnalysisPreflightException(string code, string messageKey)
        {
            Code = code;
            MessageKey = messageKey;
        }

        internal string Code { get; }

        internal string MessageKey { get; }
    }
}
