// SPDX-License-Identifier: MIT

using System.Text.Json;
using Microsoft.Extensions.Logging;
using WorkTrail.Application;
using WorkTrail.Search;
using WorkTrail.Services;

namespace WorkTrail.Runtime;

/// <summary>Validates and dispatches versioned runtime request envelopes to the application facade.</summary>
internal sealed class RuntimeRequestDispatcher
{
    private readonly IWorkTrailApplication _application;
    private readonly ILogger _logger;

    internal RuntimeRequestDispatcher(IWorkTrailApplication application, ILogger logger)
    {
        _application = application ?? throw new ArgumentNullException(nameof(application));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>Dispatches one validated request without exposing application exceptions over IPC.</summary>
    internal async Task<RuntimeResponseEnvelope> DispatchAsync(
        RuntimeRequestEnvelope request,
        CancellationToken cancellationToken)
    {
        if (request.ProtocolVersion != RuntimeProtocol.ProtocolVersion)
        {
            return Failure(request, "ipc.protocol.unsupported", "IpcProtocolUnsupported");
        }

        if (!RuntimeOperationCatalog.TryResolve(request.Operation, out var operation))
        {
            return Failure(request, "command.invalid", "CommandInvalid");
        }

        try
        {
            return operation switch
            {
                RuntimeOperation.RuntimeHealth => ToResponse(request, await _application.GetRuntimeHealthAsync(cancellationToken)),
                RuntimeOperation.TrackingStart => ToResponse(request, await _application.StartTrackingAsync(Read<StartTrackingRequest>(request.Payload) ?? new StartTrackingRequest(), cancellationToken)),
                RuntimeOperation.TrackingPause => ToResponse(request, await _application.PauseTrackingAsync(cancellationToken)),
                RuntimeOperation.TrackingToggle => ToResponse(request, await _application.ToggleTrackingAsync(cancellationToken)),
                RuntimeOperation.DashboardGet => ToResponse(request, await _application.GetDashboardAsync(cancellationToken)),
                RuntimeOperation.WorldClocksGetV3 => ToResponse(request, await _application.GetWorldClocksAsync(cancellationToken)),
                RuntimeOperation.CelestialReferenceV1 => ToResponse(request, await _application.GetCelestialReferenceAsync(cancellationToken)),
                RuntimeOperation.CelestialGetV1 => ToResponse(request, await _application.GetCelestialAsync(
                    Read<CelestialRequest>(request.Payload) ?? throw new InvalidDataException("A celestial request is required."), cancellationToken)),
                RuntimeOperation.CelestialMapV1 => ToResponse(request, await _application.GetCelestialMapAsync(
                    Read<CelestialMapRequest>(request.Payload) ?? throw new InvalidDataException("An Earth projection request is required."), cancellationToken)),
                RuntimeOperation.WorldClocksConvertV2 => ToResponse(request, await _application.ConvertWorldClocksAsync(
                    Read<WorldClockConversionRequest>(request.Payload)
                        ?? throw new InvalidDataException("A world-clock conversion request is required."),
                    cancellationToken)),
                RuntimeOperation.WorldClocksCatalogV1 => ToResponse(request, await _application.GetWorldClockCityCatalogAsync(cancellationToken)),
                RuntimeOperation.WorldClocksAddV3 => ToResponse(request, await _application.AddWorldClockAsync(ReadString(request.Payload, "cityId"), cancellationToken)),
                RuntimeOperation.WorldClocksRemoveV3 => ToResponse(request, await _application.RemoveWorldClockAsync(ReadString(request.Payload, "cityId"), cancellationToken)),
                RuntimeOperation.WorldClocksMoveV1 => ToResponse(request, await DispatchWorldClockMoveAsync(request, cancellationToken)),
                RuntimeOperation.WorldClocksWeatherKeySetV2 => ToResponse(request, await _application.SetWorldClockWeatherKeyAsync(ReadString(request.Payload, "secret"), cancellationToken)),
                RuntimeOperation.SessionLast => ToResponse(request, await _application.GetLastSessionAsync(cancellationToken)),
                RuntimeOperation.SessionToday => ToResponse(request, await _application.GetTodaySummaryAsync(cancellationToken)),
                RuntimeOperation.SearchQueryV1 => await DispatchSearchAsync(request, cancellationToken),
                RuntimeOperation.SearchAvailabilityV1 => ToResponse(request, await _application.GetSearchAvailabilityAsync(cancellationToken)),
                RuntimeOperation.SearchRebuildV1 => ToResponse(request, await _application.RebuildSearchIndexAsync(cancellationToken)),
                RuntimeOperation.HardwareSnapshotV1 => ToResponse(request, await _application.CaptureHardwareSnapshotAsync(cancellationToken)),
                RuntimeOperation.SystemSnapshot => ToResponse(request, await _application.CaptureSystemSnapshotAsync(cancellationToken)),
                RuntimeOperation.HardwareAdvancedEnableV1 => ToResponse(request, await _application.EnableAdvancedHardwareTelemetryAsync(cancellationToken)),
                RuntimeOperation.ScreenshotCapture => await DispatchScreenshotCaptureAsync(request, cancellationToken),
                RuntimeOperation.ScreenshotManualCapture => ToResponse(request, await _application.CaptureManualScreenshotAsync(cancellationToken)),
                RuntimeOperation.ScreenshotManualDelete => ToResponse(request, await _application.DeletePendingManualScreenshotAsync(cancellationToken)),
                RuntimeOperation.ScreenshotAnalyze => await DispatchScreenshotAnalysisAsync(request, cancellationToken),
                RuntimeOperation.ScreenshotLatest => ToResponse(request, await _application.GetLatestScreenshotAsync(cancellationToken)),
                RuntimeOperation.ScreenshotGallery => ToResponse(request, await DispatchScreenshotGalleryAsync(request, cancellationToken)),
                RuntimeOperation.ScreenshotGalleryLatest => ToResponse(request, await _application.GetLatestScreenshotGalleryAsync(cancellationToken)),
                RuntimeOperation.ScreenshotImageGetV1 => ToResponse(request, await DispatchScreenshotImageAsync(request, cancellationToken)),
                RuntimeOperation.InstallationsListV1 => ToResponse(request, await _application.GetInstallationProfilesAsync(cancellationToken)),
                RuntimeOperation.InstallationsUpdateV1 => ToResponse(request, await _application.UpdateInstallationProfileAsync(
                    Read<UpdateInstallationProfileRequest>(request.Payload)
                        ?? throw new InvalidDataException("An installation profile update payload is required."),
                    cancellationToken)),
                RuntimeOperation.ArchiveProgressV1 => ToResponse(request, await _application.GetDataArchiveProgressAsync(
                    Read<DataArchiveProgressRequest>(request.Payload) ?? throw new InvalidDataException("An archive progress identity is required."), cancellationToken)),
                RuntimeOperation.ArchiveExportV1 => ToResponse(request, await _application.ExportDataArchiveAsync(
                    Read<DataArchiveExportRequest>(request.Payload)
                        ?? throw new InvalidDataException("An archive export payload is required."),
                    cancellationToken)),
                RuntimeOperation.ArchiveImportPreviewV1 => ToResponse(request, await _application.PreviewDataArchiveImportAsync(
                    Read<DataArchiveImportPreviewRequest>(request.Payload)
                        ?? throw new InvalidDataException("An archive import preview payload is required."),
                    cancellationToken)),
                RuntimeOperation.ArchiveImportMergeV1 => ToResponse(request, await _application.ImportDataArchiveAsync(
                    Read<DataArchiveImportRequest>(request.Payload)
                        ?? throw new InvalidDataException("An archive import payload is required."),
                    cancellationToken)),
                RuntimeOperation.ScreenshotDelete => ToResponse(request, await _application.DeleteScreenshotAsync(ReadString(request.Payload, "screenshotPath"), cancellationToken)),
                RuntimeOperation.ScreenshotAnalysisDeleteV1 => ToResponse(request, await _application.DeleteScreenshotAnalysisAsync(ReadString(request.Payload, "screenshotPath"), cancellationToken)),
                RuntimeOperation.ScreenshotSave => ToResponse(request, await _application.SaveScreenshotAsync(ReadString(request.Payload, "screenshotPath"), ReadString(request.Payload, "destinationPath"), cancellationToken)),
                RuntimeOperation.ScreenshotShare => ToResponse(request, await _application.ShareScreenshotAsync(ReadString(request.Payload, "screenshotPath"), ReadInt64(request.Payload, "windowHandle"), cancellationToken)),
                RuntimeOperation.DiagnosticsLogOpen => ToResponse(request, await _application.OpenApplicationLogAsync(cancellationToken)),
                RuntimeOperation.DiagnosticsLogOpenFolder => ToResponse(request, await _application.OpenApplicationLogFolderAsync(cancellationToken)),
                RuntimeOperation.DiagnosticsLogShare => ToResponse(request, await _application.ShareApplicationLogAsync(ReadInt64(request.Payload, "windowHandle"), cancellationToken)),
                RuntimeOperation.ScreenshotOpenFolder => ToResponse(request, await DispatchOpenScreenshotFolderAsync(request, cancellationToken)),
                RuntimeOperation.NotificationsDrain => ToResponse(request, await _application.DrainApplicationNotificationsAsync(cancellationToken)),
                RuntimeOperation.AiStatus => ToResponse(request, await _application.GetAiStatusAsync(cancellationToken)),
                RuntimeOperation.AiKeyGet => ToResponse(request, await _application.GetAiKeyAsync(cancellationToken)),
                RuntimeOperation.AiPricingOverview => ToResponse(request, await _application.GetAiPricingOverviewAsync(cancellationToken)),
                RuntimeOperation.AiConnectionTest => ToResponse(request, await _application.TestAiConnectionAsync(cancellationToken)),
                RuntimeOperation.AiScreenshotReprocessPreviewV1 => await DispatchAiScreenshotReprocessPreviewAsync(request, cancellationToken),
                RuntimeOperation.AiScreenshotReprocessStartV1 => ToResponse(request, await _application.StartAiScreenshotReprocessingAsync(ReadGuid(request.Payload, "planId"), cancellationToken)),
                RuntimeOperation.AiScreenshotReprocessStatusV1 => ToResponse(request, await _application.GetAiScreenshotReprocessingJobAsync(ReadGuid(request.Payload, "jobId"), cancellationToken)),
                RuntimeOperation.AiScreenshotReprocessPauseV1 => ToResponse(request, await _application.PauseAiScreenshotReprocessingAsync(ReadGuid(request.Payload, "jobId"), cancellationToken)),
                RuntimeOperation.AiScreenshotReprocessResumeV1 => ToResponse(request, await _application.ResumeAiScreenshotReprocessingAsync(ReadGuid(request.Payload, "jobId"), cancellationToken)),
                RuntimeOperation.AiModels => ToResponse(request, await _application.GetAiModelCatalogAsync(cancellationToken)),
                RuntimeOperation.AiEnable => ToResponse(request, await _application.SetAiEnabledAsync(true, cancellationToken)),
                RuntimeOperation.AiDisable => ToResponse(request, await _application.SetAiEnabledAsync(false, cancellationToken)),
                RuntimeOperation.AiConfigure => ToResponse(request, await _application.ConfigureAiAsync(Read<SettingsPatch>(request.Payload) ?? new SettingsPatch(new Dictionary<string, string?>()), cancellationToken)),
                RuntimeOperation.AiKeySet => ToResponse(request, await _application.SetAiKeyAsync(ReadString(request.Payload, "keyVariable"), ReadString(request.Payload, "secret"), cancellationToken)),
                RuntimeOperation.AiAnalyze => ToResponse(request, await _application.AnalyzeCurrentActivityAsync(Read<AnalyzeCurrentActivityRequest>(request.Payload) ?? new AnalyzeCurrentActivityRequest(), cancellationToken)),
                RuntimeOperation.ReportQueryV1 => await DispatchReportQueryAsync(request, cancellationToken),
                RuntimeOperation.ReportExportSetupV1 => ToResponse(request, await _application.GetReportExportSetupAsync(cancellationToken)),
                RuntimeOperation.ReportExportPreviewV1 => ToResponse(request, await _application.PreviewReportExportAsync(
                    Read<ReportExportOptions>(request.Payload) ?? throw new InvalidDataException("Export options are required."), cancellationToken)),
                RuntimeOperation.ReportExportPreferencesV1 => ToResponse(request, await _application.SaveReportExportPreferencesAsync(
                    Read<ReportExportOptions>(request.Payload) ?? throw new InvalidDataException("Export options are required."), cancellationToken)),
                RuntimeOperation.ReportExportWriteV1 => ToResponse(request, await _application.ExportReportAsync(
                    Read<ReportExportRequest>(request.Payload) ?? throw new InvalidDataException("An export request is required."), cancellationToken)),
                RuntimeOperation.ReportExportSummaryV1 => ToResponse(request, await _application.GenerateReportSummaryAsync(
                    Read<ReportSummaryRequest>(request.Payload) ?? throw new InvalidDataException("A summary request is required."), cancellationToken)),
                RuntimeOperation.UiOpen => ToResponse(request, await _application.OpenUserInterfaceAsync(cancellationToken)),
                RuntimeOperation.PrivacyList => ToResponse(request, await _application.GetPrivacyRulesAsync(cancellationToken)),
                RuntimeOperation.PrivacyAdd => ToResponse(request, await _application.AddPrivacyRuleAsync(ReadString(request.Payload, "type"), ReadString(request.Payload, "value"), cancellationToken)),
                RuntimeOperation.PrivacyRemove => ToResponse(request, await _application.RemovePrivacyRuleAsync(ReadString(request.Payload, "id"), cancellationToken)),
                RuntimeOperation.PrivacyTestCurrent => ToResponse(request, await _application.TestCurrentPrivacyAsync(cancellationToken)),
                RuntimeOperation.RetentionStatus => ToResponse(request, await _application.GetRetentionStatusAsync(cancellationToken)),
                RuntimeOperation.RetentionPreview => ToResponse(request, await _application.PreviewRetentionAsync(cancellationToken)),
                RuntimeOperation.RetentionRun => ToResponse(request, await _application.RunRetentionAsync(Read<RetentionRequest>(request.Payload) ?? new RetentionRequest(false, false), cancellationToken)),
                RuntimeOperation.AppAtomicResetV1 => ToResponse(request, await _application.PrepareAtomicResetAsync(
                    Read<AtomicResetRequest>(request.Payload) ?? new AtomicResetRequest(false, false),
                    cancellationToken)),
                RuntimeOperation.PluginsList => ToResponse(request, await _application.GetPluginsAsync(cancellationToken)),
                RuntimeOperation.PluginsShow => ToResponse(request, await _application.GetPluginAsync(ReadString(request.Payload, "id"), cancellationToken)),
                RuntimeOperation.PluginsEnable => ToResponse(request, await _application.SetPluginEnabledAsync(ReadString(request.Payload, "id"), true, cancellationToken)),
                RuntimeOperation.PluginsDisable => ToResponse(request, await _application.SetPluginEnabledAsync(ReadString(request.Payload, "id"), false, cancellationToken)),
                RuntimeOperation.SettingsGet => ToResponse(request, await _application.GetSettingsAsync(cancellationToken)),
                RuntimeOperation.FeatureAccessGetV1 => ToResponse(request, await _application.GetFeatureAccessAsync(cancellationToken)),
#if DEBUG
                RuntimeOperation.DebugFeatureSimulateV1 => ToResponse(request, await _application.SimulateFeatureAccessAsync(
                    Read<ProductTier?>(request.Payload) ?? throw new ArgumentException("An access tier is required."), cancellationToken)),
#endif
                RuntimeOperation.QuickSetupApplyV1 => ToResponse(request, await _application.ApplyQuickSetupProfileAsync(
                    Read<QuickSetupProfileRequest>(request.Payload) ?? new QuickSetupProfileRequest(string.Empty, false),
                    cancellationToken)),
                RuntimeOperation.SettingsPatch => ToResponse(request, await _application.PatchSettingsAsync(Read<SettingsPatch>(request.Payload) ?? new SettingsPatch(new Dictionary<string, string?>()), cancellationToken)),
                RuntimeOperation.WindowStateRestore => ToResponse(request, await _application.RestoreWindowStateAsync(ReadString(request.Payload, "windowKey"), ReadInt64(request.Payload, "windowHandle"), cancellationToken)),
                RuntimeOperation.WindowRevealOpenV1 => ToResponse(request, await _application.RevealOpenWindowsAsync(Read<WindowRevealRequest>(request.Payload) ?? throw new ArgumentException("A window reveal request is required."), cancellationToken)),
                RuntimeOperation.WindowStateSave => ToResponse(request, await _application.SaveWindowStateAsync(ReadString(request.Payload, "windowKey"), ReadInt64(request.Payload, "windowHandle"), cancellationToken)),
                RuntimeOperation.WindowOpenStateSet => ToResponse(request, await _application.SetWindowOpenStateAsync(ReadString(request.Payload, "windowKey"), ReadRequiredBool(request.Payload, "isOpen"), cancellationToken)),
                RuntimeOperation.WindowOcrSourceSet => ToResponse(request, await DispatchOcrTextWindowSourceAsync(request, cancellationToken)),
                RuntimeOperation.StartupStatus => ToResponse(request, await _application.GetStartupStatusAsync(cancellationToken)),
                RuntimeOperation.StartupEnable => ToResponse(request, await _application.SetStartupEnabledAsync(true, cancellationToken)),
                RuntimeOperation.StartupDisable => ToResponse(request, await _application.SetStartupEnabledAsync(false, cancellationToken)),
                RuntimeOperation.ProductGet => ToResponse(request, await _application.GetProductInformationAsync(cancellationToken)),
                RuntimeOperation.ProductLinkOpen => ToResponse(request, await _application.OpenProductLinkAsync(ReadString(request.Payload, "linkKey"), cancellationToken)),
                _ => throw new InvalidOperationException($"Runtime operation '{operation}' has no host dispatch handler.")
            };
        }
        catch (OperationCanceledException)
        {
            return Failure(request, "operation.cancelled", "OperationCancelled");
        }
        catch (Exception exception)
        {
            // Never serialize exception messages: they can disclose local paths or external provider details.
            _logger.LogWarning("Runtime operation failed. Operation={Operation} ExceptionType={ExceptionType}", request.Operation, exception.GetType().Name);
            return Failure(request, "runtime.operation.failed", "RuntimeOperationFailed");
        }
    }

    private static RuntimeResponseEnvelope ToResponse<T>(
        RuntimeRequestEnvelope request,
        OperationResult<T> result) =>
        new(
            RuntimeProtocol.ProtocolVersion,
            request.RequestId,
            result.Succeeded,
            result.Code,
            result.MessageKey,
            result.Value,
            result.Issues);

    private async Task<OperationResult<ScreenshotGallery>> DispatchScreenshotGalleryAsync(
        RuntimeRequestEnvelope request,
        CancellationToken cancellationToken)
    {
        var galleryRequest = Read<ScreenshotGalleryRequest>(request.Payload);
        return galleryRequest is null
            ? OperationResult<ScreenshotGallery>.Failure("screenshot.gallery.invalid", "ScreenshotGalleryRequestInvalid")
            : await _application.GetScreenshotGalleryAsync(galleryRequest.Date, cancellationToken);
    }

    private async Task<OperationResult<ScreenshotImageContent>> DispatchScreenshotImageAsync(
        RuntimeRequestEnvelope request,
        CancellationToken cancellationToken)
    {
        var imageRequest = Read<ScreenshotImageRequest>(request.Payload);
        return imageRequest is null
            ? OperationResult<ScreenshotImageContent>.Failure("screenshot.image.invalid", "ScreenshotImageInvalid")
            : await _application.GetScreenshotImageAsync(imageRequest, cancellationToken);
    }

    private async Task<RuntimeResponseEnvelope> DispatchSearchAsync(
        RuntimeRequestEnvelope request,
        CancellationToken cancellationToken)
    {
        var searchRequest = Read<SearchRequest>(request.Payload);
        return searchRequest is null
            ? Failure(request, "search.query.invalid", "SearchQueryInvalid")
            : ToResponse(request, await _application.SearchAsync(searchRequest, cancellationToken));
    }

    private async Task<RuntimeResponseEnvelope> DispatchScreenshotCaptureAsync(
        RuntimeRequestEnvelope request,
        CancellationToken cancellationToken)
    {
        var captureRequest = Read<CaptureScreenshotRequest>(request.Payload);
        return captureRequest is null
            ? Failure(request, "screenshot.capture.invalid", "ScreenshotCaptureRequestInvalid")
            : ToResponse(request, await _application.CaptureScreenshotAsync(captureRequest, cancellationToken));
    }

    private async Task<RuntimeResponseEnvelope> DispatchScreenshotAnalysisAsync(
        RuntimeRequestEnvelope request,
        CancellationToken cancellationToken)
    {
        var analysisRequest = Read<AnalyzeCapturedScreenshotRequest>(request.Payload);
        return analysisRequest is null
            ? Failure(request, "screenshot.analysis.invalid", "AiConfigurationInvalid")
            : ToResponse(request, await _application.AnalyzeCapturedScreenshotAsync(analysisRequest, cancellationToken));
    }

    private async Task<RuntimeResponseEnvelope> DispatchAiScreenshotReprocessPreviewAsync(
        RuntimeRequestEnvelope request,
        CancellationToken cancellationToken)
    {
        var previewRequest = Read<AiScreenshotReprocessRequest>(request.Payload);
        return previewRequest is null
            ? Failure(request, "ai.screenshot_reprocess.preview.invalid", "AiScreenshotReprocessInvalid")
            : ToResponse(request, await _application.PreviewAiScreenshotReprocessingAsync(previewRequest, cancellationToken));
    }

    private async Task<RuntimeResponseEnvelope> DispatchReportQueryAsync(
        RuntimeRequestEnvelope request,
        CancellationToken cancellationToken)
    {
        var query = Read<ReportQuery>(request.Payload);
        if (query is null)
        {
            return Failure(request, "command.arguments.invalid", "InvalidReportQuery");
        }

        return ToResponse(request, await _application.GetReportAsync(query, cancellationToken));
    }

    private async Task<OperationResult<WorldClockSelectionState>> DispatchWorldClockMoveAsync(
        RuntimeRequestEnvelope request,
        CancellationToken cancellationToken)
    {
        var move = Read<WorldClockMoveRequest>(request.Payload)
            ?? throw new InvalidDataException("A world-clock move request is required.");
        return await _application.MoveWorldClockAsync(move.CityId, move.Direction, cancellationToken);
    }

    private static RuntimeResponseEnvelope Failure(
        RuntimeRequestEnvelope request,
        string code,
        string messageKey) =>
        new(
            RuntimeProtocol.ProtocolVersion,
            request.RequestId,
            false,
            code,
            messageKey,
            null,
            Array.Empty<ValidationIssue>());

    private static T? Read<T>(JsonElement value) =>
        value.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null
            ? default
            : value.Deserialize<T>(RuntimeProtocol.SerializerOptions);

    private static string ReadString(JsonElement value, string name) =>
        ReadStringOrNull(value, name) ?? string.Empty;

    private static string? ReadStringOrNull(JsonElement value, string name) =>
        value.ValueKind == JsonValueKind.Object
        && value.TryGetProperty(name, out var property)
        && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;

    private static Guid ReadGuid(JsonElement value, string name) =>
        Guid.TryParse(ReadStringOrNull(value, name), out var parsed) ? parsed : Guid.Empty;

    private Task<OperationResult<string>> DispatchOpenScreenshotFolderAsync(
        RuntimeRequestEnvelope request,
        CancellationToken cancellationToken) =>
        ReadStringOrNull(request.Payload, "directory") is { } directory
            ? _application.OpenScreenshotFolderAsync(directory, cancellationToken)
            : _application.OpenScreenshotFolderAsync(cancellationToken);

    private static bool ReadBool(JsonElement value, string name) =>
        value.ValueKind == JsonValueKind.Object
        && value.TryGetProperty(name, out var property)
        && property.ValueKind is JsonValueKind.True or JsonValueKind.False
        && property.GetBoolean();

    private static bool ReadRequiredBool(JsonElement value, string name) =>
        value.ValueKind == JsonValueKind.Object
        && value.TryGetProperty(name, out var property)
        && property.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? property.GetBoolean()
            // A missing visibility flag must fail rather than silently close the saved window.
            : throw new InvalidOperationException($"A boolean '{name}' property is required.");

    private Task<OperationResult<OcrTextWindowSource>> DispatchOcrTextWindowSourceAsync(RuntimeRequestEnvelope request, CancellationToken cancellationToken)
    {
        var source = Read<OcrTextWindowSource>(request.Payload)
            ?? throw new InvalidOperationException("An OCR text window source is required.");
        return _application.SetOcrTextWindowSourceAsync(source.ScreenshotPath, source.CapturedAt, cancellationToken);
    }

    private static long ReadInt64(JsonElement value, string name) =>
        value.ValueKind == JsonValueKind.Object
        && value.TryGetProperty(name, out var property)
        && property.ValueKind == JsonValueKind.Number
        && property.TryGetInt64(out var result)
            ? result
            : 0L;
}
