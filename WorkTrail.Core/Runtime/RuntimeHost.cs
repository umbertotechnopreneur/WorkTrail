// SPDX-License-Identifier: MIT

using System.IO.Pipes;
using System.Runtime.ExceptionServices;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using WorkTrail.Application;
using WorkTrail.Search;
using WorkTrail.Services;

namespace WorkTrail.Runtime;

/// <summary>Owns the single local runtime and serves versioned requests over a same-user named pipe.</summary>
public sealed class RuntimeHost : IAsyncDisposable
{
    private IWorkTrailApplication _application;
    private readonly Func<IWorkTrailApplication>? _applicationFactory;
    private readonly bool _ownsApplication;
    private readonly RuntimeEndpoint _endpoint;
    private readonly ILogger<RuntimeHost> _logger;
    private readonly CancellationTokenSource _shutdown = new();
    private RuntimeMutexLease? _mutexLease;
    private RuntimePipeServer? _server;
    private Task? _serverTask;
    private bool _disposed;

    /// <summary>Initializes a runtime host for a single application installation.</summary>
    public RuntimeHost(IWorkTrailApplication application, string installationId, ILogger<RuntimeHost>? logger = null)
    {
        _application = application ?? throw new ArgumentNullException(nameof(application));
        _endpoint = RuntimeProtocol.CreateEndpoint(installationId);
        _logger = logger ?? NullLogger<RuntimeHost>.Instance;
    }

    /// <summary>
    /// Initializes a host that creates its local application only after runtime ownership is acquired.
    /// </summary>
    public RuntimeHost(
        Func<IWorkTrailApplication> applicationFactory,
        string installationId,
        ILogger<RuntimeHost>? logger = null)
    {
        _application = null!;
        _applicationFactory = applicationFactory ?? throw new ArgumentNullException(nameof(applicationFactory));
        _ownsApplication = true;
        _endpoint = RuntimeProtocol.CreateEndpoint(installationId);
        _logger = logger ?? NullLogger<RuntimeHost>.Instance;
    }

    /// <summary>Gets the endpoint used by this host.</summary>
    public RuntimeEndpoint Endpoint => _endpoint;

    /// <summary>Gets the local application after this host has acquired runtime ownership.</summary>
    public IWorkTrailApplication Application =>
        _application ?? throw new InvalidOperationException("The runtime host does not own a local application.");

    /// <summary>Occurs after a successful reset-preparation response has been flushed to its caller.</summary>
    public event Action<AtomicResetPlan>? AtomicResetPrepared;

    /// <summary>Starts the host when this process successfully acquires runtime ownership.</summary>
    public bool TryStart()
    {
        ThrowIfDisposed();
        _mutexLease = new RuntimeMutexLease(_endpoint.MutexName);
        if (!_mutexLease.Acquired)
        {
            _logger.LogDebug("Runtime mutex is already owned by another process.");
            _mutexLease.Dispose();
            _mutexLease = null;
            return false;
        }

        try
        {
            _application ??= _applicationFactory?.Invoke()
                ?? throw new InvalidOperationException("The runtime application factory returned no application.");
        }
        catch
        {
            _mutexLease.Dispose();
            _mutexLease = null;
            throw;
        }

        var dispatcher = new RuntimeRequestDispatcher(_application, _logger);
        var server = new RuntimePipeServer(
            _endpoint,
            dispatcher,
            _logger,
            resetPlan => AtomicResetPrepared?.Invoke(resetPlan));
        _server = server;

        // One background loop owns pipe acceptance; mutations themselves remain serialized in the facade.
        _serverTask = Task.Run(() => server.ServeAsync(_shutdown.Token));
        _logger.LogInformation("Runtime host started. Pipe={PipeName}", _endpoint.PipeName);
        return true;
    }

    /// <summary>
    /// Stops the named-pipe server, drains requests and disposes a factory-owned application before
    /// releasing the runtime mutex.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Exception? shutdownFailure = null;
        try
        {
            _shutdown.Cancel();
            if (_serverTask is not null)
            {
                try
                {
                    await _serverTask;
                }
                catch (OperationCanceledException)
                {
                    // Expected during shutdown; no client-visible error is produced.
                }
                catch (Exception exception)
                {
                    shutdownFailure = exception;
                }
            }

            if (_server is not null)
            {
                try
                {
                    await _server.DrainRequestsAsync();
                }
                catch (Exception exception)
                {
                    shutdownFailure ??= exception;
                }
            }

            if (_ownsApplication && _application is not null)
            {
                try
                {
                    await _application.DisposeAsync();
                }
                catch (Exception exception)
                {
                    shutdownFailure ??= exception;
                }
            }
        }
        finally
        {
            // Runtime ownership is the last resource released. A successor cannot start while any
            // factory-owned writer from this host is still shutting down.
            _mutexLease?.Dispose();
            _mutexLease = null;
            _shutdown.Dispose();
        }

        if (shutdownFailure is not null)
        {
            ExceptionDispatchInfo.Capture(shutdownFailure).Throw();
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(RuntimeHost));
        }
    }

}

/// <summary>Provides a typed application facade backed by the local runtime pipe.</summary>
public sealed class RuntimeClient : IWorkTrailApplication
{
    private readonly WindowSnappingService _windowSnapping = new();

    /// <inheritdoc />
    public IWindowSnappingRegistration RegisterWindowSnapping(long windowHandle, Action<Exception> reportFailure) =>
        _windowSnapping.Register(windowHandle, reportFailure);

    /// <inheritdoc />
    public void ConfigureWindowSnapping(bool enabled) => _windowSnapping.Configure(enabled);

    private static readonly TimeSpan ReportQueryTimeout = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan ScreenshotAnalysisTimeout = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan ScreenshotReprocessPreviewTimeout = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan DataArchiveTimeout = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan SearchTimeout = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan StartupMutationTimeout = TimeSpan.FromMinutes(2);
    // Allow up to five minutes for driver setup, followed by consent, helper startup, and its first read.
    internal static readonly TimeSpan HardwareAdvancedTimeout = TimeSpan.FromMinutes(7);
    internal static readonly TimeSpan HardwareSnapshotTimeout = TimeSpan.FromSeconds(30);
    internal static readonly TimeSpan ScreenshotCaptureTimeout = TimeSpan.FromSeconds(60);
    internal static readonly TimeSpan ScreenshotImageTimeout = TimeSpan.FromSeconds(15);
    internal static readonly TimeSpan WorldClockQueryTimeout = TimeSpan.FromSeconds(15);
    internal static readonly TimeSpan WorldClockWeatherKeyTimeout = TimeSpan.FromSeconds(15);
    private readonly RuntimeEndpoint _endpoint;
    private readonly TimeSpan _timeout;
    private readonly ILogger<RuntimeClient> _logger;

    /// <summary>Initializes a client for the supplied installation identifier.</summary>
    public RuntimeClient(string installationId, TimeSpan timeout, ILogger<RuntimeClient>? logger = null)
    {
        _endpoint = RuntimeProtocol.CreateEndpoint(installationId);
        _timeout = timeout;
        _logger = logger ?? NullLogger<RuntimeClient>.Instance;
    }

    /// <inheritdoc />
    public event EventHandler<RuntimeStateChangedEventArgs>? RuntimeStateChanged
    {
        add { }
        remove { }
    }

    /// <inheritdoc />
    public Task<OperationResult<RuntimeHealth>> GetRuntimeHealthAsync(CancellationToken cancellationToken) => SendAsync<RuntimeHealth>(RuntimeOperation.RuntimeHealth, null, cancellationToken);
    /// <inheritdoc />
    public Task<OperationResult<DashboardState>> StartTrackingAsync(StartTrackingRequest request, CancellationToken cancellationToken) => SendAsync<DashboardState>(RuntimeOperation.TrackingStart, request, cancellationToken);
    /// <inheritdoc />
    public Task<OperationResult<DashboardState>> PauseTrackingAsync(CancellationToken cancellationToken) => SendAsync<DashboardState>(RuntimeOperation.TrackingPause, null, cancellationToken);
    /// <inheritdoc />
    public Task<OperationResult<DashboardState>> ToggleTrackingAsync(CancellationToken cancellationToken) => SendAsync<DashboardState>(RuntimeOperation.TrackingToggle, null, cancellationToken);
    /// <inheritdoc />
    public Task<OperationResult<DashboardState>> GetDashboardAsync(CancellationToken cancellationToken) => SendAsync<DashboardState>(RuntimeOperation.DashboardGet, null, cancellationToken);
    /// <inheritdoc />
    public Task<OperationResult<WorldClockSnapshot>> GetWorldClocksAsync(CancellationToken cancellationToken) =>
        SendAsync<WorldClockSnapshot>(RuntimeOperation.WorldClocksGetV3, null, cancellationToken, WorldClockQueryTimeout);

    /// <inheritdoc />
    public Task<OperationResult<WorldClockSnapshot>> GetCelestialReferenceAsync(CancellationToken cancellationToken) =>
        SendAsync<WorldClockSnapshot>(RuntimeOperation.CelestialReferenceV1, null, cancellationToken);

    /// <inheritdoc />
    public Task<OperationResult<CelestialSnapshot>> GetCelestialAsync(CelestialRequest request, CancellationToken cancellationToken) =>
        SendAsync<CelestialSnapshot>(RuntimeOperation.CelestialGetV1, request, cancellationToken);

    /// <inheritdoc />
    public Task<OperationResult<CelestialMapImage>> GetCelestialMapAsync(CelestialMapRequest request, CancellationToken cancellationToken) =>
        SendAsync<CelestialMapImage>(RuntimeOperation.CelestialMapV1, request, cancellationToken);
    /// <inheritdoc />
    public Task<OperationResult<WorldClockSnapshot>> ConvertWorldClocksAsync(WorldClockConversionRequest request, CancellationToken cancellationToken) =>
        SendAsync<WorldClockSnapshot>(RuntimeOperation.WorldClocksConvertV2, request, cancellationToken);
    /// <inheritdoc />
    public Task<OperationResult<WorldClockCityCatalog>> GetWorldClockCityCatalogAsync(CancellationToken cancellationToken) => SendAsync<WorldClockCityCatalog>(RuntimeOperation.WorldClocksCatalogV1, null, cancellationToken);
    /// <inheritdoc />
    public Task<OperationResult<WorldClockSelectionState>> AddWorldClockAsync(string cityId, CancellationToken cancellationToken) =>
        SendAsync<WorldClockSelectionState>(RuntimeOperation.WorldClocksAddV3, new { cityId }, cancellationToken);
    /// <inheritdoc />
    public Task<OperationResult<WorldClockSelectionState>> RemoveWorldClockAsync(string cityId, CancellationToken cancellationToken) =>
        SendAsync<WorldClockSelectionState>(RuntimeOperation.WorldClocksRemoveV3, new { cityId }, cancellationToken);
    /// <inheritdoc />
    public Task<OperationResult<WorldClockSelectionState>> MoveWorldClockAsync(
        string cityId,
        WorldClockMoveDirection direction,
        CancellationToken cancellationToken) =>
        SendAsync<WorldClockSelectionState>(
            RuntimeOperation.WorldClocksMoveV1,
            new WorldClockMoveRequest(cityId, direction),
            cancellationToken);
    /// <inheritdoc />
    public Task<OperationResult<string>> SetWorldClockWeatherKeyAsync(string secret, CancellationToken cancellationToken) =>
        SendAsync<string>(RuntimeOperation.WorldClocksWeatherKeySetV2, new { secret }, cancellationToken, WorldClockWeatherKeyTimeout);
    /// <inheritdoc />
    public Task<OperationResult<LastSessionState?>> GetLastSessionAsync(CancellationToken cancellationToken) => SendAsync<LastSessionState?>(RuntimeOperation.SessionLast, null, cancellationToken);
    /// <inheritdoc />
    public Task<OperationResult<DailySummary>> GetTodaySummaryAsync(CancellationToken cancellationToken) => SendAsync<DailySummary>(RuntimeOperation.SessionToday, null, cancellationToken);
    /// <inheritdoc />
    public Task<OperationResult<SearchResponse>> SearchAsync(SearchRequest request, CancellationToken cancellationToken) =>
        SendAsync<SearchResponse>(RuntimeOperation.SearchQueryV1, request, cancellationToken, SearchTimeout);
    /// <inheritdoc />
    public Task<OperationResult<SearchAvailability>> GetSearchAvailabilityAsync(CancellationToken cancellationToken) =>
        SendAsync<SearchAvailability>(RuntimeOperation.SearchAvailabilityV1, null, cancellationToken, SearchTimeout);
    /// <inheritdoc />
    public Task<OperationResult<int>> RebuildSearchIndexAsync(CancellationToken cancellationToken) =>
        SendAsync<int>(RuntimeOperation.SearchRebuildV1, null, cancellationToken, SearchTimeout);
    /// <inheritdoc />
    public Task<OperationResult<ReportSnapshot>> GetReportAsync(ReportQuery query, CancellationToken cancellationToken) =>
        SendAsync<ReportSnapshot>(RuntimeOperation.ReportQueryV1, query, cancellationToken, ReportQueryTimeout);
    /// <inheritdoc />
    public Task<OperationResult<ReportExportSetup>> GetReportExportSetupAsync(CancellationToken cancellationToken) =>
        SendAsync<ReportExportSetup>(RuntimeOperation.ReportExportSetupV1, null, cancellationToken, ReportQueryTimeout);
    /// <inheritdoc />
    public Task<OperationResult<ReportExportPreview>> PreviewReportExportAsync(ReportExportOptions options, CancellationToken cancellationToken) =>
        SendAsync<ReportExportPreview>(RuntimeOperation.ReportExportPreviewV1, options, cancellationToken, DataArchiveTimeout);
    /// <inheritdoc />
    public Task<OperationResult<bool>> SaveReportExportPreferencesAsync(ReportExportOptions options, CancellationToken cancellationToken) =>
        SendAsync<bool>(RuntimeOperation.ReportExportPreferencesV1, options, cancellationToken);
    /// <inheritdoc />
    public Task<OperationResult<ReportExportResult>> ExportReportAsync(ReportExportRequest request, CancellationToken cancellationToken) =>
        SendAsync<ReportExportResult>(RuntimeOperation.ReportExportWriteV1, request, cancellationToken, DataArchiveTimeout);
    /// <inheritdoc />
    public Task<OperationResult<ReportSummaryResult>> GenerateReportSummaryAsync(ReportSummaryRequest request, CancellationToken cancellationToken) =>
        SendAsync<ReportSummaryResult>(RuntimeOperation.ReportExportSummaryV1, request, cancellationToken, DataArchiveTimeout);
    /// <inheritdoc />
    public Task<OperationResult<SystemSnapshot>> CaptureHardwareSnapshotAsync(CancellationToken cancellationToken) =>
        SendAsync<SystemSnapshot>(RuntimeOperation.HardwareSnapshotV1, null, cancellationToken, HardwareSnapshotTimeout);

    /// <inheritdoc />
    public Task<OperationResult<SystemSnapshot>> CaptureSystemSnapshotAsync(CancellationToken cancellationToken) =>
        SendAsync<SystemSnapshot>(RuntimeOperation.SystemSnapshot, null, cancellationToken, HardwareSnapshotTimeout);

    /// <inheritdoc />
    public Task<OperationResult<SystemSnapshot>> EnableAdvancedHardwareTelemetryAsync(CancellationToken cancellationToken) =>
        SendAsync<SystemSnapshot>(RuntimeOperation.HardwareAdvancedEnableV1, null, cancellationToken, HardwareAdvancedTimeout);
    /// <inheritdoc />
    public Task<OperationResult<ScreenshotCaptureResult>> CaptureScreenshotAsync(CaptureScreenshotRequest request, CancellationToken cancellationToken) =>
        SendAsync<ScreenshotCaptureResult>(RuntimeOperation.ScreenshotCapture, request, cancellationToken, ScreenshotCaptureTimeout);
    /// <inheritdoc />
    public Task<OperationResult<PendingManualScreenshotState>> CaptureManualScreenshotAsync(CancellationToken cancellationToken) =>
        SendAsync<PendingManualScreenshotState>(RuntimeOperation.ScreenshotManualCapture, null, cancellationToken, ScreenshotCaptureTimeout);
    /// <inheritdoc />
    public Task<OperationResult<bool>> DeletePendingManualScreenshotAsync(CancellationToken cancellationToken) => SendAsync<bool>(RuntimeOperation.ScreenshotManualDelete, null, cancellationToken);
    /// <inheritdoc />
    public Task<OperationResult<AiAnalysis>> AnalyzeCapturedScreenshotAsync(AnalyzeCapturedScreenshotRequest request, CancellationToken cancellationToken) => SendAsync<AiAnalysis>(RuntimeOperation.ScreenshotAnalyze, request, cancellationToken, ScreenshotAnalysisTimeout);
    /// <inheritdoc />
    public Task<OperationResult<string?>> GetLatestScreenshotAsync(CancellationToken cancellationToken) => SendAsync<string?>(RuntimeOperation.ScreenshotLatest, null, cancellationToken);
    /// <inheritdoc />
    public Task<OperationResult<ScreenshotGallery>> GetScreenshotGalleryAsync(DateOnly date, CancellationToken cancellationToken) => SendAsync<ScreenshotGallery>(RuntimeOperation.ScreenshotGallery, new ScreenshotGalleryRequest(date), cancellationToken);
    /// <inheritdoc />
    public Task<OperationResult<ScreenshotGallery>> GetLatestScreenshotGalleryAsync(CancellationToken cancellationToken) => SendAsync<ScreenshotGallery>(RuntimeOperation.ScreenshotGalleryLatest, null, cancellationToken);
    /// <inheritdoc />
    public Task<OperationResult<ScreenshotImageContent>> GetScreenshotImageAsync(ScreenshotImageRequest request, CancellationToken cancellationToken) =>
        SendAsync<ScreenshotImageContent>(RuntimeOperation.ScreenshotImageGetV1, request, cancellationToken, ScreenshotImageTimeout);
    /// <inheritdoc />
    public Task<OperationResult<IReadOnlyList<InstallationProfile>>> GetInstallationProfilesAsync(CancellationToken cancellationToken) =>
        SendAsync<IReadOnlyList<InstallationProfile>>(RuntimeOperation.InstallationsListV1, null, cancellationToken);
    /// <inheritdoc />
    public Task<OperationResult<InstallationProfile>> UpdateInstallationProfileAsync(
        UpdateInstallationProfileRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<InstallationProfile>(RuntimeOperation.InstallationsUpdateV1, request, cancellationToken);
    /// <inheritdoc />
    public Task<OperationResult<DataArchiveProgress?>> GetDataArchiveProgressAsync(DataArchiveProgressRequest request, CancellationToken cancellationToken) =>
        SendAsync<DataArchiveProgress?>(RuntimeOperation.ArchiveProgressV1, request, cancellationToken);
    /// <inheritdoc />
    public Task<OperationResult<DataArchiveExportResult>> ExportDataArchiveAsync(
        DataArchiveExportRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<DataArchiveExportResult>(RuntimeOperation.ArchiveExportV1, request, cancellationToken, DataArchiveTimeout);
    /// <inheritdoc />
    public Task<OperationResult<DataArchiveImportPlan>> PreviewDataArchiveImportAsync(
        DataArchiveImportPreviewRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<DataArchiveImportPlan>(RuntimeOperation.ArchiveImportPreviewV1, request, cancellationToken, DataArchiveTimeout);
    /// <inheritdoc />
    public Task<OperationResult<DataArchiveImportResult>> ImportDataArchiveAsync(
        DataArchiveImportRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<DataArchiveImportResult>(RuntimeOperation.ArchiveImportMergeV1, request, cancellationToken, DataArchiveTimeout);
    /// <inheritdoc />
    public Task<OperationResult<string>> DeleteScreenshotAsync(string screenshotPath, CancellationToken cancellationToken) => SendAsync<string>(RuntimeOperation.ScreenshotDelete, new { screenshotPath }, cancellationToken);
    /// <inheritdoc />
    public Task<OperationResult<string>> DeleteScreenshotAnalysisAsync(string screenshotPath, CancellationToken cancellationToken) => SendAsync<string>(RuntimeOperation.ScreenshotAnalysisDeleteV1, new { screenshotPath }, cancellationToken);
    /// <inheritdoc />
    public Task<OperationResult<string>> SaveScreenshotAsync(string screenshotPath, string destinationPath, CancellationToken cancellationToken) => SendAsync<string>(RuntimeOperation.ScreenshotSave, new { screenshotPath, destinationPath }, cancellationToken);
    /// <inheritdoc />
    public Task<OperationResult<string>> ShareScreenshotAsync(string screenshotPath, long windowHandle, CancellationToken cancellationToken) => SendAsync<string>(RuntimeOperation.ScreenshotShare, new { screenshotPath, windowHandle }, cancellationToken);
    /// <inheritdoc />
    public Task<OperationResult<bool>> OpenApplicationLogAsync(CancellationToken cancellationToken) => SendAsync<bool>(RuntimeOperation.DiagnosticsLogOpen, null, cancellationToken);
    /// <inheritdoc />
    public Task<OperationResult<bool>> OpenApplicationLogFolderAsync(CancellationToken cancellationToken) => SendAsync<bool>(RuntimeOperation.DiagnosticsLogOpenFolder, null, cancellationToken);
    /// <inheritdoc />
    public Task<OperationResult<bool>> ShareApplicationLogAsync(long windowHandle, CancellationToken cancellationToken) => SendAsync<bool>(RuntimeOperation.DiagnosticsLogShare, new { windowHandle }, cancellationToken);
    /// <inheritdoc />
    public Task<OperationResult<string>> OpenScreenshotFolderAsync(CancellationToken cancellationToken) => SendAsync<string>(RuntimeOperation.ScreenshotOpenFolder, null, cancellationToken);
    /// <inheritdoc />
    public Task<OperationResult<string>> OpenScreenshotFolderAsync(string directory, CancellationToken cancellationToken) => SendAsync<string>(RuntimeOperation.ScreenshotOpenFolder, new { directory }, cancellationToken);
    /// <inheritdoc />
    public Task<OperationResult<IReadOnlyList<ApplicationNotification>>> DrainApplicationNotificationsAsync(CancellationToken cancellationToken) => SendAsync<IReadOnlyList<ApplicationNotification>>(RuntimeOperation.NotificationsDrain, null, cancellationToken);
    /// <inheritdoc />
    public Task<OperationResult<AiStatus>> GetAiStatusAsync(CancellationToken cancellationToken) => SendAsync<AiStatus>(RuntimeOperation.AiStatus, null, cancellationToken);
    /// <inheritdoc />
    public Task<OperationResult<string>> GetAiKeyAsync(CancellationToken cancellationToken) => SendAsync<string>(RuntimeOperation.AiKeyGet, null, cancellationToken);
    /// <inheritdoc />
    public Task<OperationResult<AiPricingOverview>> GetAiPricingOverviewAsync(CancellationToken cancellationToken) => SendAsync<AiPricingOverview>(RuntimeOperation.AiPricingOverview, null, cancellationToken, ReportQueryTimeout);
    /// <inheritdoc />
    public Task<OperationResult<AiConnectionTestResult>> TestAiConnectionAsync(CancellationToken cancellationToken) => SendAsync<AiConnectionTestResult>(RuntimeOperation.AiConnectionTest, null, cancellationToken, TimeSpan.FromSeconds(35));
    /// <inheritdoc />
    public Task<OperationResult<AiScreenshotReprocessPlan>> PreviewAiScreenshotReprocessingAsync(
        AiScreenshotReprocessRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<AiScreenshotReprocessPlan>(RuntimeOperation.AiScreenshotReprocessPreviewV1, request, cancellationToken, ScreenshotReprocessPreviewTimeout);
    /// <inheritdoc />
    public Task<OperationResult<AiScreenshotReprocessJobSnapshot>> StartAiScreenshotReprocessingAsync(Guid planId, CancellationToken cancellationToken) =>
        SendAsync<AiScreenshotReprocessJobSnapshot>(RuntimeOperation.AiScreenshotReprocessStartV1, new { planId }, cancellationToken);
    /// <inheritdoc />
    public Task<OperationResult<AiScreenshotReprocessJobSnapshot>> GetAiScreenshotReprocessingJobAsync(Guid jobId, CancellationToken cancellationToken) =>
        SendAsync<AiScreenshotReprocessJobSnapshot>(RuntimeOperation.AiScreenshotReprocessStatusV1, new { jobId }, cancellationToken);
    /// <inheritdoc />
    public Task<OperationResult<AiScreenshotReprocessJobSnapshot>> PauseAiScreenshotReprocessingAsync(Guid jobId, CancellationToken cancellationToken) =>
        SendAsync<AiScreenshotReprocessJobSnapshot>(RuntimeOperation.AiScreenshotReprocessPauseV1, new { jobId }, cancellationToken);
    /// <inheritdoc />
    public Task<OperationResult<AiScreenshotReprocessJobSnapshot>> ResumeAiScreenshotReprocessingAsync(Guid jobId, CancellationToken cancellationToken) =>
        SendAsync<AiScreenshotReprocessJobSnapshot>(RuntimeOperation.AiScreenshotReprocessResumeV1, new { jobId }, cancellationToken);
    /// <inheritdoc />
    public Task<OperationResult<AiModelCatalogSnapshot>> GetAiModelCatalogAsync(CancellationToken cancellationToken) => SendAsync<AiModelCatalogSnapshot>(RuntimeOperation.AiModels, null, cancellationToken);
    /// <inheritdoc />
    public Task<OperationResult<AiStatus>> SetAiEnabledAsync(bool enabled, CancellationToken cancellationToken) => SendAsync<AiStatus>(enabled ? RuntimeOperation.AiEnable : RuntimeOperation.AiDisable, null, cancellationToken);
    /// <inheritdoc />
    public Task<OperationResult<AppSettings>> ConfigureAiAsync(SettingsPatch patch, CancellationToken cancellationToken) => SendAsync<AppSettings>(RuntimeOperation.AiConfigure, patch, cancellationToken);
    /// <inheritdoc />
    public Task<OperationResult<string>> SetAiKeyAsync(string keyVariable, string secret, CancellationToken cancellationToken) => SendAsync<string>(RuntimeOperation.AiKeySet, new { keyVariable, secret }, cancellationToken, TimeSpan.FromSeconds(35));
    /// <inheritdoc />
    public Task<OperationResult<AiAnalysis>> AnalyzeCurrentActivityAsync(AnalyzeCurrentActivityRequest request, CancellationToken cancellationToken) => SendAsync<AiAnalysis>(RuntimeOperation.AiAnalyze, request, cancellationToken);
    /// <inheritdoc />
    public Task<OperationResult<string>> OpenUserInterfaceAsync(CancellationToken cancellationToken) => SendAsync<string>(RuntimeOperation.UiOpen, null, cancellationToken);
    /// <inheritdoc />
    public Task<OperationResult<IReadOnlyList<PrivacyRule>>> GetPrivacyRulesAsync(CancellationToken cancellationToken) => SendAsync<IReadOnlyList<PrivacyRule>>(RuntimeOperation.PrivacyList, null, cancellationToken);
    /// <inheritdoc />
    public Task<OperationResult<PrivacyRule>> AddPrivacyRuleAsync(string type, string value, CancellationToken cancellationToken) => SendAsync<PrivacyRule>(RuntimeOperation.PrivacyAdd, new { type, value }, cancellationToken);
    /// <inheritdoc />
    public Task<OperationResult<bool>> RemovePrivacyRuleAsync(string id, CancellationToken cancellationToken) => SendAsync<bool>(RuntimeOperation.PrivacyRemove, new { id }, cancellationToken);
    /// <inheritdoc />
    public Task<OperationResult<bool>> TestCurrentPrivacyAsync(CancellationToken cancellationToken) => SendAsync<bool>(RuntimeOperation.PrivacyTestCurrent, null, cancellationToken);
    /// <inheritdoc />
    public Task<OperationResult<RetentionStatus>> GetRetentionStatusAsync(CancellationToken cancellationToken) => SendAsync<RetentionStatus>(RuntimeOperation.RetentionStatus, null, cancellationToken);
    /// <inheritdoc />
    public Task<OperationResult<RetentionPreview>> PreviewRetentionAsync(CancellationToken cancellationToken) => SendAsync<RetentionPreview>(RuntimeOperation.RetentionPreview, null, cancellationToken);
    /// <inheritdoc />
    public Task<OperationResult<RetentionPreview>> RunRetentionAsync(RetentionRequest request, CancellationToken cancellationToken) => SendAsync<RetentionPreview>(RuntimeOperation.RetentionRun, request, cancellationToken);
    /// <inheritdoc />
    public Task<OperationResult<AtomicResetPlan>> PrepareAtomicResetAsync(AtomicResetRequest request, CancellationToken cancellationToken) =>
        SendAsync<AtomicResetPlan>(RuntimeOperation.AppAtomicResetV1, request, cancellationToken);
    /// <inheritdoc />
    public Task<OperationResult<IReadOnlyList<PluginInfo>>> GetPluginsAsync(CancellationToken cancellationToken) => SendAsync<IReadOnlyList<PluginInfo>>(RuntimeOperation.PluginsList, null, cancellationToken);
    /// <inheritdoc />
    public Task<OperationResult<PluginInfo>> GetPluginAsync(string id, CancellationToken cancellationToken) => SendAsync<PluginInfo>(RuntimeOperation.PluginsShow, new { id }, cancellationToken);
    /// <inheritdoc />
    public Task<OperationResult<PluginInfo>> SetPluginEnabledAsync(string id, bool enabled, CancellationToken cancellationToken) => SendAsync<PluginInfo>(enabled ? RuntimeOperation.PluginsEnable : RuntimeOperation.PluginsDisable, new { id }, cancellationToken);
    /// <inheritdoc />
    public Task<OperationResult<AppSettings>> GetSettingsAsync(CancellationToken cancellationToken) => SendAsync<AppSettings>(RuntimeOperation.SettingsGet, null, cancellationToken);
    /// <inheritdoc />
    public Task<OperationResult<FeatureAccessSnapshot>> GetFeatureAccessAsync(CancellationToken cancellationToken) =>
        SendAsync<FeatureAccessSnapshot>(RuntimeOperation.FeatureAccessGetV1, null, cancellationToken);
#if DEBUG
    /// <inheritdoc />
    public Task<OperationResult<FeatureAccessSnapshot>> SimulateFeatureAccessAsync(ProductTier tier, CancellationToken cancellationToken) =>
        SendAsync<FeatureAccessSnapshot>(RuntimeOperation.DebugFeatureSimulateV1, tier, cancellationToken);
#endif
    /// <inheritdoc />
    public Task<OperationResult<AppSettings>> ApplyQuickSetupProfileAsync(QuickSetupProfileRequest request, CancellationToken cancellationToken) => SendAsync<AppSettings>(RuntimeOperation.QuickSetupApplyV1, request, cancellationToken);
    /// <inheritdoc />
    public Task<OperationResult<AppSettings>> PatchSettingsAsync(SettingsPatch patch, CancellationToken cancellationToken) =>
        // A sensor preference may drain one bounded native read before reconfiguring its existing collector.
        SendAsync<AppSettings>(RuntimeOperation.SettingsPatch, patch, cancellationToken,
            patch.Values.Keys.Any(key => key.Trim().StartsWith("sensors.", StringComparison.OrdinalIgnoreCase)) ? HardwareSnapshotTimeout : null);
    /// <inheritdoc />
    public Task<OperationResult<WindowState?>> RestoreWindowStateAsync(string windowKey, long windowHandle, CancellationToken cancellationToken) => SendAsync<WindowState?>(RuntimeOperation.WindowStateRestore, new { windowKey, windowHandle }, cancellationToken);
    /// <inheritdoc />
    public Task<OperationResult<int>> RevealOpenWindowsAsync(WindowRevealRequest request, CancellationToken cancellationToken) => SendAsync<int>(RuntimeOperation.WindowRevealOpenV1, request, cancellationToken);
    /// <inheritdoc />
    public Task<OperationResult<WindowState>> SaveWindowStateAsync(string windowKey, long windowHandle, CancellationToken cancellationToken) => SendAsync<WindowState>(RuntimeOperation.WindowStateSave, new { windowKey, windowHandle }, cancellationToken);
    /// <inheritdoc />
    public Task<OperationResult<bool>> SetWindowOpenStateAsync(string windowKey, bool isOpen, CancellationToken cancellationToken) => SendAsync<bool>(RuntimeOperation.WindowOpenStateSet, new { windowKey, isOpen }, cancellationToken);
    /// <inheritdoc />
    public Task<OperationResult<OcrTextWindowSource>> SetOcrTextWindowSourceAsync(string screenshotPath, DateTimeOffset capturedAt, CancellationToken cancellationToken) => SendAsync<OcrTextWindowSource>(RuntimeOperation.WindowOcrSourceSet, new OcrTextWindowSource(screenshotPath, capturedAt), cancellationToken);
    /// <inheritdoc />
    public Task<OperationResult<bool>> GetStartupStatusAsync(CancellationToken cancellationToken) => SendAsync<bool>(RuntimeOperation.StartupStatus, null, cancellationToken);
    /// <inheritdoc />
    public Task<OperationResult<bool>> SetStartupEnabledAsync(bool enabled, CancellationToken cancellationToken) =>
        SendAsync<bool>(enabled ? RuntimeOperation.StartupEnable : RuntimeOperation.StartupDisable, null, cancellationToken, StartupMutationTimeout);
    /// <inheritdoc />
    public Task<OperationResult<ProductInformation>> GetProductInformationAsync(CancellationToken cancellationToken) => SendAsync<ProductInformation>(RuntimeOperation.ProductGet, null, cancellationToken);
    /// <inheritdoc />
    public Task<OperationResult<bool>> OpenProductLinkAsync(string linkKey, CancellationToken cancellationToken) => SendAsync<bool>(RuntimeOperation.ProductLinkOpen, new { linkKey }, cancellationToken);
    /// <inheritdoc />
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private async Task<OperationResult<T>> SendAsync<T>(
        RuntimeOperation operation,
        object? payload,
        CancellationToken cancellationToken,
        TimeSpan? requestTimeout = null)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(requestTimeout ?? _timeout);
        try
        {
            await using var pipe = new NamedPipeClientStream(".", _endpoint.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            await pipe.ConnectAsync(timeout.Token).ConfigureAwait(false);
            var wireName = RuntimeOperationCatalog.GetWireName(operation);
            var request = new RuntimeRequestEnvelope(RuntimeProtocol.ProtocolVersion, Guid.NewGuid(), wireName, JsonSerializer.SerializeToElement(payload, RuntimeProtocol.SerializerOptions), null, null);
            await RuntimeProtocol.WriteAsync(pipe, request, timeout.Token).ConfigureAwait(false);
            var response = await RuntimeProtocol.ReadAsync<RuntimeResponseEnvelope>(pipe, timeout.Token).ConfigureAwait(false);
            if (response.ProtocolVersion != RuntimeProtocol.ProtocolVersion)
            {
                return OperationResult<T>.Failure("ipc.protocol.unsupported", "IpcProtocolUnsupported");
            }

            var value = response.Payload is null ? default : JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(response.Payload, RuntimeProtocol.SerializerOptions), RuntimeProtocol.SerializerOptions);
            return new OperationResult<T>(response.Succeeded, response.Code, response.MessageKey, value, response.Issues);
        }
        catch (OperationCanceledException)
        {
            return OperationResult<T>.Failure(cancellationToken.IsCancellationRequested ? "operation.cancelled" : "runtime.unavailable", cancellationToken.IsCancellationRequested ? "OperationCancelled" : "RuntimeUnavailable");
        }
        catch (Exception exception)
        {
            _logger.LogDebug(
                exception,
                "Runtime pipe request was unavailable. Operation={Operation}",
                RuntimeOperationCatalog.GetWireName(operation));
            return OperationResult<T>.Failure("runtime.unavailable", "RuntimeUnavailable");
        }
    }
}
