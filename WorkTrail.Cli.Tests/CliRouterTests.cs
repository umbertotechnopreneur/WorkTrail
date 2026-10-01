// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using WorkTrail.Application;
using WorkTrail.Cli;
using WorkTrail.Search;
using WorkTrail.Services;
using Xunit;

namespace WorkTrail.Cli.Tests;

public sealed class CliRouterTests
{
    [Fact]
    public async Task SlashStatus_RoutesToSharedApplicationFacade()
    {
        var application = new RecordingApplication();
        var router = CreateRouter(application);

        var exitCode = await router.RunAsync(["/status"], CancellationToken.None);

        Assert.Equal(0, exitCode);
        Assert.Equal(1, application.DashboardReads);
    }

    [Fact]
    public async Task AiOnQuickSwitch_RoutesToSharedApplicationFacade()
    {
        var application = new RecordingApplication();
        var router = CreateRouter(application);

        var exitCode = await router.RunAsync(["--ai-on"], CancellationToken.None);

        Assert.Equal(0, exitCode);
        Assert.Equal(true, application.LastAiEnabled);
    }

    [Fact]
    public async Task RuntimeHealth_RoutesToSharedApplicationFacade()
    {
        var application = new RecordingApplication();
        var router = CreateRouter(application);

        var exitCode = await router.RunAsync(["/runtime", "health"], CancellationToken.None);

        Assert.Equal(0, exitCode);
        Assert.Equal(1, application.RuntimeHealthReads);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("61")]
    [InlineData("later")]
    public async Task StatusWatch_RejectsInvalidIntervalBeforeCallingApplication(string interval)
    {
        var application = new RecordingApplication();
        var router = CreateRouter(application);

        var exitCode = await router.RunAsync(["/status", "--watch", "--interval", interval], CancellationToken.None);

        Assert.Equal(2, exitCode);
        Assert.Equal(0, application.TotalCalls);
    }

    [Fact]
    public async Task StatusWatch_RejectsMissingIntervalInsteadOfUsingTheDefault()
    {
        var application = new RecordingApplication();
        var router = CreateRouter(application);

        var exitCode = await router.RunAsync(["/status", "--watch", "--interval"], CancellationToken.None);

        Assert.Equal(2, exitCode);
        Assert.Equal(0, application.TotalCalls);
    }

    [Fact]
    public async Task Status_RejectsIntervalWhenWatchIsNotEnabled()
    {
        var application = new RecordingApplication();
        var router = CreateRouter(application);

        var exitCode = await router.RunAsync(["/status", "--interval", "5"], CancellationToken.None);

        Assert.Equal(2, exitCode);
        Assert.Equal(0, application.TotalCalls);
    }

    [Fact]
    public async Task SystemSnapshot_RejectsUnknownOptionsBeforeCallingApplication()
    {
        var application = new RecordingApplication();
        var router = CreateRouter(application);

        var exitCode = await router.RunAsync(["/system", "snapshot", "--watc"], CancellationToken.None);

        Assert.Equal(2, exitCode);
        Assert.Equal(0, application.TotalCalls);
    }

    [Fact]
    public async Task Diagnostics_RunsReadOnlyFacadeChecks()
    {
        var application = new RecordingApplication();
        var router = CreateRouter(application);

        var exitCode = await router.RunAsync(["/diagnostics"], CancellationToken.None);

        Assert.Equal(0, exitCode);
        Assert.Equal(1, application.RuntimeHealthReads);
        Assert.Equal(1, application.DashboardReads);
        Assert.Equal(1, application.AiStatusReads);
        Assert.Equal(1, application.RetentionStatusReads);
        Assert.Equal(1, application.StartupStatusReads);
        Assert.Equal(1, application.PluginReads);
    }

    [Fact]
    public async Task ConfigGet_UsesPublicKeyAndReadsThroughFacade()
    {
        var application = new RecordingApplication { Settings = new AppSettings(Theme: "dark") };
        var router = CreateRouter(application);

        var exitCode = await router.RunAsync(["/config", "get", "theme"], CancellationToken.None);

        Assert.Equal(0, exitCode);
        Assert.Equal(1, application.SettingsReads);
    }

    [Fact]
    public async Task ConfigGet_RejectsInternalPropertyNameBeforeFacadeCall()
    {
        var application = new RecordingApplication();
        var router = CreateRouter(application);

        var exitCode = await router.RunAsync(["/config", "get", nameof(AppSettings.InstallationId)], CancellationToken.None);

        Assert.Equal(2, exitCode);
        Assert.Equal(0, application.SettingsReads);
    }

    [Fact]
    public async Task ConfigSet_ForwardsCanonicalPublicKeyToFacade()
    {
        var application = new RecordingApplication();
        var router = CreateRouter(application);

        var exitCode = await router.RunAsync(["/settings", "set", "theme", "dark"], CancellationToken.None);

        Assert.Equal(0, exitCode);
        Assert.NotNull(application.LastPatch);
        Assert.Equal("dark", application.LastPatch!.Values["theme"]);
    }

    [Fact]
    public async Task ConfigSet_ForwardsAiReasoningSettingFromCoreCatalog()
    {
        var application = new RecordingApplication();
        var router = CreateRouter(application);

        var exitCode = await router.RunAsync(["/config", "set", "ai.reasoning_effort", "high"], CancellationToken.None);

        Assert.Equal(0, exitCode);
        Assert.NotNull(application.LastPatch);
        Assert.Equal("high", application.LastPatch!.Values["ai.reasoning_effort"]);
    }

    [Fact]
    public async Task AiConfigure_ForwardsOutputAndReasoningProfiles()
    {
        var application = new RecordingApplication();
        var router = CreateRouter(application);

        var exitCode = await router.RunAsync(["/ai", "configure", "--output-detail", "compact", "--reasoning-effort", "low"], CancellationToken.None);

        Assert.Equal(0, exitCode);
        Assert.NotNull(application.LastPatch);
        Assert.Equal("compact", application.LastPatch!.Values["ai.output_detail"]);
        Assert.Equal("low", application.LastPatch.Values["ai.reasoning_effort"]);
    }

    [Fact]
    public async Task AiConfigure_RejectsAnOptionWhoseValueIsAnotherOption()
    {
        var application = new RecordingApplication();
        var router = CreateRouter(application);

        var exitCode = await router.RunAsync(["/ai", "configure", "--model", "--provider", "openai"], CancellationToken.None);

        Assert.Equal(2, exitCode);
        Assert.Null(application.LastPatch);
        Assert.Equal(0, application.TotalCalls);
    }

    [Fact]
    public async Task ExactCommand_RejectsTrailingArgumentsBeforeCallingApplication()
    {
        var application = new RecordingApplication();
        var router = CreateRouter(application);

        var exitCode = await router.RunAsync(["/runtime", "health", "unexpected"], CancellationToken.None);

        Assert.Equal(2, exitCode);
        Assert.Equal(0, application.TotalCalls);
    }

    [Fact]
    public async Task OpenUi_RoutesThroughSharedApplicationFacade()
    {
        var application = new RecordingApplication();
        var router = CreateRouter(application);

        var exitCode = await router.RunAsync(["/open", "ui"], CancellationToken.None);

        Assert.Equal(0, exitCode);
        Assert.Equal(1, application.UiOpenCalls);
    }

    [Fact]
    public async Task ScreenshotCaptureWithoutMode_ForwardsPersistedDefaultSentinel()
    {
        var application = new RecordingApplication();
        var router = CreateRouter(application);

        var exitCode = await router.RunAsync(["/screenshot", "capture", "--keep"], CancellationToken.None);

        Assert.Equal(0, exitCode);
        Assert.NotNull(application.LastCaptureRequest);
        Assert.Null(application.LastCaptureRequest!.Mode);
    }

    [Fact]
    public async Task ScreenshotCaptureWithInvalidMode_PropagatesApplicationValidationFailure()
    {
        var application = new RecordingApplication
        {
            ScreenshotCaptureResponse = OperationResult<ScreenshotCaptureResult>.Failure(
                "screenshot.mode.invalid",
                "ScreenshotModeUnsupported",
                new ValidationIssue("mode", "unsupported", "ScreenshotModeUnsupported"))
        };
        var router = CreateRouter(application);

        var exitCode = await router.RunAsync(["/screenshot", "capture", "--mode", "unsupported-mode"], CancellationToken.None);

        Assert.Equal(3, exitCode);
        Assert.NotNull(application.LastCaptureRequest);
        Assert.Equal("unsupported-mode", application.LastCaptureRequest!.Mode);
    }

    [Fact]
    public async Task ScreenshotCaptureWithMissingModeValue_FailsBeforeCallingApplication()
    {
        var application = new RecordingApplication();
        var router = CreateRouter(application);

        var exitCode = await router.RunAsync(["/screenshot", "capture", "--mode"], CancellationToken.None);

        Assert.Equal(2, exitCode);
        Assert.Null(application.LastCaptureRequest);
        Assert.Equal(0, application.TotalCalls);
    }

    [Fact]
    public async Task ScreenshotCaptureWithRemovedWatermarkOption_FailsBeforeCallingApplication()
    {
        var application = new RecordingApplication();
        var router = CreateRouter(application);

        var exitCode = await router.RunAsync(["/screenshot", "capture", "--watermark"], CancellationToken.None);

        Assert.Equal(2, exitCode);
        Assert.Null(application.LastCaptureRequest);
        Assert.Equal(0, application.TotalCalls);
    }

    [Fact]
    public async Task SlashCommandHelp_DoesNotCallApplicationFacade()
    {
        var application = new RecordingApplication();
        var router = CreateRouter(application);

        var exitCode = await router.RunAsync(["/help", "/config"], CancellationToken.None);

        Assert.Equal(0, exitCode);
        Assert.Equal(0, application.TotalCalls);
    }

    [Fact]
    public void TryTokenize_RejectsAnUnterminatedQuotedValue()
    {
        Assert.False(CliRouter.TryTokenize("/privacy add --type hint --value \"C:\\Private", out var tokens));
        Assert.Empty(tokens);
    }

    [Fact]
    public void TryTokenize_PreservesWhitespaceInsideBalancedQuotes()
    {
        Assert.True(CliRouter.TryTokenize("/privacy add --type hint --value \"C:\\Private Notes\"", out var tokens));
        Assert.Equal(["/privacy", "add", "--type", "hint", "--value", "C:\\Private Notes"], tokens);
    }

    private static CliRouter CreateRouter(RecordingApplication application)
    {
        var options = new CliOptions(CliFormat.Plain, "en-US", true, false, 5, false, []);
        return new CliRouter(application, new CliOutput(options), options);
    }

    private sealed class RecordingApplication : IWorkTrailApplication
    {
        /// <inheritdoc />
        public Task<OperationResult<DataArchiveProgress?>> GetDataArchiveProgressAsync(DataArchiveProgressRequest request, CancellationToken cancellationToken) => Unsupported<DataArchiveProgress?>();
        public IWindowSnappingRegistration RegisterWindowSnapping(long windowHandle, Action<Exception> reportFailure) =>
            throw new NotSupportedException("The CLI does not register native windows.");

        public void ConfigureWindowSnapping(bool enabled) =>
            throw new NotSupportedException("The CLI does not configure native windows.");

        /// <inheritdoc />
        public Task<OperationResult<FeatureAccessSnapshot>> GetFeatureAccessAsync(CancellationToken cancellationToken) =>
            Success(new FeatureAccessSnapshot(ProductTier.Premium, false, false), "features.loaded");
#if DEBUG
        /// <inheritdoc />
        public Task<OperationResult<FeatureAccessSnapshot>> SimulateFeatureAccessAsync(ProductTier tier, CancellationToken cancellationToken) =>
            throw new NotSupportedException("The CLI cannot grant itself Premium access.");
#endif

        public event EventHandler<RuntimeStateChangedEventArgs>? RuntimeStateChanged
        {
            add { }
            remove { }
        }

        internal AppSettings Settings { get; set; } = new();
        internal SettingsPatch? LastPatch { get; private set; }
        internal int DashboardReads { get; private set; }
        internal int RuntimeHealthReads { get; private set; }
        internal int AiStatusReads { get; private set; }
        internal int RetentionStatusReads { get; private set; }
        internal int StartupStatusReads { get; private set; }
        internal int PluginReads { get; private set; }
        internal int SettingsReads { get; private set; }
        internal int UiOpenCalls { get; private set; }
        internal int TotalCalls { get; private set; }
        internal bool? LastAiEnabled { get; private set; }
        internal CaptureScreenshotRequest? LastCaptureRequest { get; private set; }
        internal OperationResult<ScreenshotCaptureResult>? ScreenshotCaptureResponse { get; init; }

        public Task<OperationResult<DashboardState>> GetDashboardAsync(CancellationToken cancellationToken)
        {
            TotalCalls++;
            DashboardReads++;
            var utcNow = new DateTimeOffset(2026, 8, 5, 15, 0, 0, TimeSpan.Zero);
            return Success(new DashboardState("PAUSED", "Ready", 0, 0, 0, 0, false, null, utcNow.ToLocalTime(), utcNow), "dashboard.loaded");
        }

        public Task<OperationResult<AppSettings>> GetSettingsAsync(CancellationToken cancellationToken)
        {
            TotalCalls++;
            SettingsReads++;
            return Success(Settings, "settings.loaded");
        }

        public Task<OperationResult<AppSettings>> ApplyQuickSetupProfileAsync(QuickSetupProfileRequest request, CancellationToken cancellationToken)
        {
            TotalCalls++;
            return Success(Settings, "quick_setup.applied");
        }

        public Task<OperationResult<AppSettings>> PatchSettingsAsync(SettingsPatch patch, CancellationToken cancellationToken)
        {
            TotalCalls++;
            LastPatch = patch;
            if (patch.Values.TryGetValue("theme", out var theme) && theme is not null)
            {
                Settings = Settings with { Theme = theme };
            }
            return Success(Settings, "settings.saved");
        }

        public Task<OperationResult<WindowState?>> RestoreWindowStateAsync(string windowKey, long windowHandle, CancellationToken cancellationToken) => Unsupported<WindowState?>();
        /// <inheritdoc />
        public Task<OperationResult<int>> RevealOpenWindowsAsync(WindowRevealRequest request, CancellationToken cancellationToken) => Unsupported<int>();
        public Task<OperationResult<WindowState>> SaveWindowStateAsync(string windowKey, long windowHandle, CancellationToken cancellationToken) => Unsupported<WindowState>();
        public Task<OperationResult<bool>> SetWindowOpenStateAsync(string windowKey, bool isOpen, CancellationToken cancellationToken) => Unsupported<bool>();
        public Task<OperationResult<OcrTextWindowSource>> SetOcrTextWindowSourceAsync(string screenshotPath, DateTimeOffset capturedAt, CancellationToken cancellationToken) => Unsupported<OcrTextWindowSource>();
        public Task<OperationResult<WorldClockSnapshot>> GetWorldClocksAsync(CancellationToken cancellationToken) => Unsupported<WorldClockSnapshot>();

        public Task<OperationResult<WorldClockSnapshot>> GetCelestialReferenceAsync(CancellationToken cancellationToken) => Unsupported<WorldClockSnapshot>();

        public Task<OperationResult<CelestialSnapshot>> GetCelestialAsync(CelestialRequest request, CancellationToken cancellationToken) => Unsupported<CelestialSnapshot>();

        public Task<OperationResult<CelestialMapImage>> GetCelestialMapAsync(CelestialMapRequest request, CancellationToken cancellationToken) => Unsupported<CelestialMapImage>();
        public Task<OperationResult<WorldClockSnapshot>> ConvertWorldClocksAsync(WorldClockConversionRequest request, CancellationToken cancellationToken) => Unsupported<WorldClockSnapshot>();
        public Task<OperationResult<WorldClockCityCatalog>> GetWorldClockCityCatalogAsync(CancellationToken cancellationToken) => Unsupported<WorldClockCityCatalog>();
        public Task<OperationResult<WorldClockSelectionState>> AddWorldClockAsync(string cityId, CancellationToken cancellationToken) => Unsupported<WorldClockSelectionState>();
        public Task<OperationResult<WorldClockSelectionState>> RemoveWorldClockAsync(string cityId, CancellationToken cancellationToken) => Unsupported<WorldClockSelectionState>();
        public Task<OperationResult<WorldClockSelectionState>> MoveWorldClockAsync(string cityId, WorldClockMoveDirection direction, CancellationToken cancellationToken) => Unsupported<WorldClockSelectionState>();
        public Task<OperationResult<string>> SetWorldClockWeatherKeyAsync(string secret, CancellationToken cancellationToken) => Unsupported<string>();

        public Task<OperationResult<RuntimeHealth>> GetRuntimeHealthAsync(CancellationToken cancellationToken)
        {
            TotalCalls++;
            RuntimeHealthReads++;
            return Success(
                new RuntimeHealth(
                    "1.0.0",
                    1,
                    "test-installation",
                    true,
                    ["settings.get"],
                    new TrackingRuntimeHealth(false, null, null, "tracking.persistence.healthy")),
                "runtime.healthy");
        }
        public Task<OperationResult<DashboardState>> StartTrackingAsync(StartTrackingRequest request, CancellationToken cancellationToken) => Unsupported<DashboardState>();
        public Task<OperationResult<DashboardState>> PauseTrackingAsync(CancellationToken cancellationToken) => Unsupported<DashboardState>();
        public Task<OperationResult<DashboardState>> ToggleTrackingAsync(CancellationToken cancellationToken) => Unsupported<DashboardState>();
        public Task<OperationResult<LastSessionState?>> GetLastSessionAsync(CancellationToken cancellationToken) => Unsupported<LastSessionState?>();
        public Task<OperationResult<DailySummary>> GetTodaySummaryAsync(CancellationToken cancellationToken) => Unsupported<DailySummary>();
        public Task<OperationResult<SearchResponse>> SearchAsync(SearchRequest request, CancellationToken cancellationToken) => Unsupported<SearchResponse>();
        public Task<OperationResult<SearchAvailability>> GetSearchAvailabilityAsync(CancellationToken cancellationToken) => Unsupported<SearchAvailability>();
        public Task<OperationResult<int>> RebuildSearchIndexAsync(CancellationToken cancellationToken) => Unsupported<int>();
        public Task<OperationResult<ReportSnapshot>> GetReportAsync(ReportQuery query, CancellationToken cancellationToken) => Unsupported<ReportSnapshot>();
        /// <inheritdoc />
        public Task<OperationResult<ReportExportSetup>> GetReportExportSetupAsync(CancellationToken cancellationToken) => Unsupported<ReportExportSetup>();
        /// <inheritdoc />
        public Task<OperationResult<ReportExportPreview>> PreviewReportExportAsync(ReportExportOptions options, CancellationToken cancellationToken) => Unsupported<ReportExportPreview>();
        /// <inheritdoc />
        public Task<OperationResult<bool>> SaveReportExportPreferencesAsync(ReportExportOptions options, CancellationToken cancellationToken) => Unsupported<bool>();
        /// <inheritdoc />
        public Task<OperationResult<ReportExportResult>> ExportReportAsync(ReportExportRequest request, CancellationToken cancellationToken) => Unsupported<ReportExportResult>();
        /// <inheritdoc />
        public Task<OperationResult<ReportSummaryResult>> GenerateReportSummaryAsync(ReportSummaryRequest request, CancellationToken cancellationToken) => Unsupported<ReportSummaryResult>();
        public Task<OperationResult<SystemSnapshot>> CaptureHardwareSnapshotAsync(CancellationToken cancellationToken) => CaptureSystemSnapshotAsync(cancellationToken);

        public Task<OperationResult<SystemSnapshot>> CaptureSystemSnapshotAsync(CancellationToken cancellationToken) => Unsupported<SystemSnapshot>();

        public Task<OperationResult<SystemSnapshot>> EnableAdvancedHardwareTelemetryAsync(CancellationToken cancellationToken) => Unsupported<SystemSnapshot>();
        public Task<OperationResult<ScreenshotCaptureResult>> CaptureScreenshotAsync(CaptureScreenshotRequest request, CancellationToken cancellationToken)
        {
            TotalCalls++;
            LastCaptureRequest = request;
            return Task.FromResult(ScreenshotCaptureResponse ?? OperationResult<ScreenshotCaptureResult>.Success(
                "screenshot.captured",
                "ScreenshotCaptured",
                new ScreenshotCaptureResult("test-capture", ["test.webp"], ["test.webp"], ScreenshotCaptureOrigins.Manual)));
        }
        /// <inheritdoc />
        public Task<OperationResult<PendingManualScreenshotState>> CaptureManualScreenshotAsync(CancellationToken cancellationToken) => Unsupported<PendingManualScreenshotState>();
        /// <inheritdoc />
        public Task<OperationResult<bool>> DeletePendingManualScreenshotAsync(CancellationToken cancellationToken) => Unsupported<bool>();
        public Task<OperationResult<AiAnalysis>> AnalyzeCapturedScreenshotAsync(AnalyzeCapturedScreenshotRequest request, CancellationToken cancellationToken) => Unsupported<AiAnalysis>();
        public Task<OperationResult<string>> DeleteScreenshotAsync(string screenshotPath, CancellationToken cancellationToken) => Unsupported<string>();
        /// <inheritdoc />
        public Task<OperationResult<string>> DeleteScreenshotAnalysisAsync(string screenshotPath, CancellationToken cancellationToken) => Unsupported<string>();
        public Task<OperationResult<string?>> GetLatestScreenshotAsync(CancellationToken cancellationToken) => Unsupported<string?>();
        public Task<OperationResult<ScreenshotGallery>> GetScreenshotGalleryAsync(DateOnly date, CancellationToken cancellationToken) => Unsupported<ScreenshotGallery>();
        public Task<OperationResult<ScreenshotGallery>> GetLatestScreenshotGalleryAsync(CancellationToken cancellationToken) => Unsupported<ScreenshotGallery>();
        public Task<OperationResult<ScreenshotImageContent>> GetScreenshotImageAsync(ScreenshotImageRequest request, CancellationToken cancellationToken) => Unsupported<ScreenshotImageContent>();
        public Task<OperationResult<IReadOnlyList<InstallationProfile>>> GetInstallationProfilesAsync(CancellationToken cancellationToken) => Unsupported<IReadOnlyList<InstallationProfile>>();
        public Task<OperationResult<InstallationProfile>> UpdateInstallationProfileAsync(UpdateInstallationProfileRequest request, CancellationToken cancellationToken) => Unsupported<InstallationProfile>();
        public Task<OperationResult<DataArchiveExportResult>> ExportDataArchiveAsync(DataArchiveExportRequest request, CancellationToken cancellationToken) => Unsupported<DataArchiveExportResult>();
        public Task<OperationResult<DataArchiveImportPlan>> PreviewDataArchiveImportAsync(DataArchiveImportPreviewRequest request, CancellationToken cancellationToken) => Unsupported<DataArchiveImportPlan>();
        public Task<OperationResult<DataArchiveImportResult>> ImportDataArchiveAsync(DataArchiveImportRequest request, CancellationToken cancellationToken) => Unsupported<DataArchiveImportResult>();
        public Task<OperationResult<AiScreenshotReprocessPlan>> PreviewAiScreenshotReprocessingAsync(AiScreenshotReprocessRequest request, CancellationToken cancellationToken) => Unsupported<AiScreenshotReprocessPlan>();
        public Task<OperationResult<AiScreenshotReprocessJobSnapshot>> StartAiScreenshotReprocessingAsync(Guid planId, CancellationToken cancellationToken) => Unsupported<AiScreenshotReprocessJobSnapshot>();
        public Task<OperationResult<AiScreenshotReprocessJobSnapshot>> GetAiScreenshotReprocessingJobAsync(Guid jobId, CancellationToken cancellationToken) => Unsupported<AiScreenshotReprocessJobSnapshot>();
        public Task<OperationResult<AiScreenshotReprocessJobSnapshot>> PauseAiScreenshotReprocessingAsync(Guid jobId, CancellationToken cancellationToken) => Unsupported<AiScreenshotReprocessJobSnapshot>();
        public Task<OperationResult<AiScreenshotReprocessJobSnapshot>> ResumeAiScreenshotReprocessingAsync(Guid jobId, CancellationToken cancellationToken) => Unsupported<AiScreenshotReprocessJobSnapshot>();
        public Task<OperationResult<string>> SaveScreenshotAsync(string screenshotPath, string destinationPath, CancellationToken cancellationToken) => Unsupported<string>();
        public Task<OperationResult<string>> ShareScreenshotAsync(string screenshotPath, long windowHandle, CancellationToken cancellationToken) => Unsupported<string>();
        public Task<OperationResult<bool>> OpenApplicationLogAsync(CancellationToken cancellationToken) => Unsupported<bool>();
        public Task<OperationResult<bool>> OpenApplicationLogFolderAsync(CancellationToken cancellationToken) => Unsupported<bool>();
        public Task<OperationResult<bool>> ShareApplicationLogAsync(long windowHandle, CancellationToken cancellationToken) => Unsupported<bool>();
        public Task<OperationResult<string>> OpenScreenshotFolderAsync(CancellationToken cancellationToken) => Unsupported<string>();
        public Task<OperationResult<string>> OpenScreenshotFolderAsync(string directory, CancellationToken cancellationToken) => Unsupported<string>();
        public Task<OperationResult<IReadOnlyList<ApplicationNotification>>> DrainApplicationNotificationsAsync(CancellationToken cancellationToken) => Unsupported<IReadOnlyList<ApplicationNotification>>();
        public Task<OperationResult<AiStatus>> GetAiStatusAsync(CancellationToken cancellationToken)
        {
            TotalCalls++;
            AiStatusReads++;
            return Success(new AiStatus(false, "openai", "gpt-5.6", "https://api.openai.com/v1/responses", "OPENAI_API_KEY", false, false, new AnalysisCostGate(true, null, 0m, 0, 0m)), "ai.status.loaded");
        }
        public Task<OperationResult<string>> GetAiKeyAsync(CancellationToken cancellationToken) => Unsupported<string>();
        public Task<OperationResult<AiPricingOverview>> GetAiPricingOverviewAsync(CancellationToken cancellationToken) => Unsupported<AiPricingOverview>();
        public Task<OperationResult<AiConnectionTestResult>> TestAiConnectionAsync(CancellationToken cancellationToken) => Unsupported<AiConnectionTestResult>();
        public Task<OperationResult<AiModelCatalogSnapshot>> GetAiModelCatalogAsync(CancellationToken cancellationToken) => Unsupported<AiModelCatalogSnapshot>();
        public Task<OperationResult<AiStatus>> SetAiEnabledAsync(bool enabled, CancellationToken cancellationToken)
        {
            TotalCalls++;
            LastAiEnabled = enabled;
            return Success(new AiStatus(enabled, "test-provider", "test-model", "https://example.invalid", "TEST_AI_KEY", enabled, true, new AnalysisCostGate(true, null, 0m, 0, 0m)), enabled ? "ai.enabled" : "ai.disabled");
        }
        public Task<OperationResult<AppSettings>> ConfigureAiAsync(SettingsPatch patch, CancellationToken cancellationToken)
        {
            TotalCalls++;
            LastPatch = patch;
            return Success(Settings, "ai.configured");
        }
        public Task<OperationResult<string>> SetAiKeyAsync(string keyVariable, string secret, CancellationToken cancellationToken) => Unsupported<string>();
        public Task<OperationResult<AiAnalysis>> AnalyzeCurrentActivityAsync(AnalyzeCurrentActivityRequest request, CancellationToken cancellationToken) => Unsupported<AiAnalysis>();
        public Task<OperationResult<string>> OpenUserInterfaceAsync(CancellationToken cancellationToken)
        {
            TotalCalls++;
            UiOpenCalls++;
            return Success("WorkTrail UI", "ui.opened");
        }
        public Task<OperationResult<IReadOnlyList<PrivacyRule>>> GetPrivacyRulesAsync(CancellationToken cancellationToken) => Unsupported<IReadOnlyList<PrivacyRule>>();
        public Task<OperationResult<PrivacyRule>> AddPrivacyRuleAsync(string type, string value, CancellationToken cancellationToken) => Unsupported<PrivacyRule>();
        public Task<OperationResult<bool>> RemovePrivacyRuleAsync(string id, CancellationToken cancellationToken) => Unsupported<bool>();
        public Task<OperationResult<bool>> TestCurrentPrivacyAsync(CancellationToken cancellationToken) => Unsupported<bool>();
        public Task<OperationResult<RetentionStatus>> GetRetentionStatusAsync(CancellationToken cancellationToken)
        {
            TotalCalls++;
            RetentionStatusReads++;
            return Success(new RetentionStatus(30, 30, "private-test-path"), "retention.status.loaded");
        }
        public Task<OperationResult<RetentionPreview>> PreviewRetentionAsync(CancellationToken cancellationToken) => Unsupported<RetentionPreview>();
        public Task<OperationResult<RetentionPreview>> RunRetentionAsync(RetentionRequest request, CancellationToken cancellationToken) => Unsupported<RetentionPreview>();

        public Task<OperationResult<AtomicResetPlan>> PrepareAtomicResetAsync(AtomicResetRequest request, CancellationToken cancellationToken) => Unsupported<AtomicResetPlan>();
        public Task<OperationResult<IReadOnlyList<PluginInfo>>> GetPluginsAsync(CancellationToken cancellationToken)
        {
            TotalCalls++;
            PluginReads++;
            return Success<IReadOnlyList<PluginInfo>>([new PluginInfo("word", "Word", true, "test")], "plugins.loaded");
        }
        public Task<OperationResult<PluginInfo>> GetPluginAsync(string id, CancellationToken cancellationToken) => Unsupported<PluginInfo>();
        public Task<OperationResult<PluginInfo>> SetPluginEnabledAsync(string id, bool enabled, CancellationToken cancellationToken) => Unsupported<PluginInfo>();
        public Task<OperationResult<bool>> GetStartupStatusAsync(CancellationToken cancellationToken)
        {
            TotalCalls++;
            StartupStatusReads++;
            return Success(false, "startup.status.loaded");
        }
        public Task<OperationResult<bool>> SetStartupEnabledAsync(bool enabled, CancellationToken cancellationToken) => Unsupported<bool>();
        public Task<OperationResult<ProductInformation>> GetProductInformationAsync(CancellationToken cancellationToken) => Unsupported<ProductInformation>();
        public Task<OperationResult<bool>> OpenProductLinkAsync(string linkKey, CancellationToken cancellationToken) => Unsupported<bool>();
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        private static Task<OperationResult<T>> Success<T>(T value, string code) => Task.FromResult(OperationResult<T>.Success(code, code, value));
        private Task<OperationResult<T>> Unsupported<T>()
        {
            TotalCalls++;
            return Task.FromResult(OperationResult<T>.Failure("test.unsupported", "TestUnsupported"));
        }
    }
}
