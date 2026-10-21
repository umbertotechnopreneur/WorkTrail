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


using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace WorkTrail.Services;

/// <summary>Runs AI analysis for either a newly captured or caller-supplied snapshot.</summary>
public interface IAiAnalysisService
{
    /// <summary>Captures when allowed, then analyzes the current screen context.</summary>
    /// <param name="activity">Current context sample used to build the analysis prompt.</param>
    /// <param name="allowCapture">Whether this invocation may capture screenshots when globally enabled.</param>
    /// <param name="origin">Stable analysis origin recorded with local usage and history.</param>
    /// <param name="cancellationToken">Cancels capture, device-context collection, and the provider request.</param>
    /// <returns>The AI summary record persisted in the local history store.</returns>
    Task<AiAnalysis> AnalyzeCurrentScreenAsync(
        AnalysisContextSnapshot? activity,
        bool allowCapture = true,
        string origin = "manual",
        CancellationToken cancellationToken = default);

    /// <summary>Analyzes an already captured snapshot without creating a second capture.</summary>
    /// <param name="activity">Current context sample used to build the analysis prompt.</param>
    /// <param name="captureResult">The completed snapshot capture to submit to the configured provider.</param>
    /// <param name="keepCapture">Whether the retained snapshot files should remain on disk after analysis.</param>
    /// <param name="origin">Stable analysis origin recorded with local usage and history.</param>
    /// <param name="cancellationToken">Cancels device-context collection and the provider request.</param>
    /// <returns>The AI summary record persisted in the local history store.</returns>
    Task<AiAnalysis> AnalyzeCapturedScreenAsync(
        AnalysisContextSnapshot? activity,
        ScreenshotCaptureResult captureResult,
        bool keepCapture,
        string origin,
        CancellationToken cancellationToken = default);

    /// <summary>Analyzes retained historical images without attaching current device or system context.</summary>
    /// <param name="activity">Historical context reconstructed for the original capture interval.</param>
    /// <param name="captureResult">Retained images associated with one capture.</param>
    /// <param name="origin">Stable analysis origin recorded with local usage and history.</param>
    /// <param name="cancellationToken">Cancels the provider request during runtime shutdown.</param>
    /// <returns>The AI summary record persisted in the local history store.</returns>
    Task<AiAnalysis> AnalyzeHistoricalCapturedScreenAsync(
        AnalysisContextSnapshot activity,
        ScreenshotCaptureResult captureResult,
        string origin,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Orchestrates screenshot capture, prompt assembly, AI provider call, and persistence.
/// </summary>
public sealed class OpenAiAnalysisService : IAiAnalysisService
{
    private readonly LocalStore _store;
    private readonly IScreenCaptureService _capture;
    private readonly IAIDecoder? _decoder;
    private readonly ILogger<OpenAiAnalysisService> _logger;
    private readonly ScreenshotPublicationJournal _screenshotPublications;

    /// <summary>
    /// Creates a new AI analysis service.
    /// </summary>
    /// <param name="store">Application data store used for settings and persistence.</param>
    /// <param name="capture">Screenshot capture service for the selected capture mode.</param>
    /// <param name="decoder">Optional decoder override for testing.</param>
    /// <param name="logger">Optional structured application logger.</param>
    public OpenAiAnalysisService(
        LocalStore store,
        IScreenCaptureService capture,
        IAIDecoder? decoder = null,
        ILogger<OpenAiAnalysisService>? logger = null)
    {
        _store = store;
        _capture = capture;
        _decoder = decoder;
        _logger = logger ?? NullLogger<OpenAiAnalysisService>.Instance;
        _screenshotPublications = new ScreenshotPublicationJournal(store);
    }

    /// <summary>
    /// Runs a single AI analysis for the current foreground context and saves the result locally.
    /// </summary>
    /// <param name="activity">Current context sample used to build the analysis prompt.</param>
    /// <param name="allowCapture">Whether this invocation may capture screenshots when globally enabled.</param>
    /// <param name="origin">Stable analysis origin recorded with local usage and history.</param>
    /// <param name="cancellationToken">Cancels capture, device-context collection, and the provider request.</param>
    /// <returns>The AI summary record persisted in the local history store.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when OpenAI integration is disabled or the API key is missing.
    /// </exception>
    public async Task<AiAnalysis> AnalyzeCurrentScreenAsync(
        AnalysisContextSnapshot? activity,
        bool allowCapture = true,
        string origin = "manual",
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var settings = _store.LoadSettings();
        var apiKey = LoadRequiredApiKey(settings);
        var captureRoot = settings.KeepScreenshots ? settings.ScreenshotDirectory : _screenshotPublications.TransientRoot;

        // Keep analysis possible even when screenshots are disabled. In that case, run with empty image context.
        var captureResult = allowCapture && settings.ScreenshotsEnabled
            ? _capture.CaptureByMode(
                captureRoot,
                settings.ScreenshotCaptureMode,
                captureOrigin: origin == "snapshot.scheduled" ? ScreenshotCaptureOrigins.Scheduled : ScreenshotCaptureOrigins.Manual,
                authorizeCapture: context => TrackingDomainService.EvaluateScreenshotCapture(settings, context))
            : new ScreenshotCaptureResult(
                Guid.NewGuid().ToString("N"),
                Array.Empty<string>(),
                Array.Empty<string>(),
                origin == "snapshot.scheduled" ? ScreenshotCaptureOrigins.Scheduled : ScreenshotCaptureOrigins.Manual);

        var published = false;
        try
        {
            _screenshotPublications.Publish(captureResult, captureRoot, settings.InstallationId, settings.KeepScreenshots);
            published = true;
            (_capture as IScreenshotCapturePublicationObserver)?.CompletePublication(captureResult.CaptureId, settings.KeepScreenshots);
            return await AnalyzeCapturedScreenCoreAsync(
                settings,
                apiKey,
                activity,
                captureResult,
                settings.KeepScreenshots,
                origin,
                includeCurrentSystemContext: true,
                cancellationToken);
        }
        finally
        {
            if (!published)
                (_capture as IScreenshotCapturePublicationObserver)?.CompletePublication(captureResult.CaptureId, retained: false);
            if (captureResult.AllScreenshotPaths.Count > 0 && (!settings.KeepScreenshots || !published))
            {
                try
                {
                    _screenshotPublications.Discard(captureResult.CaptureId);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    _logger.LogWarning("Screenshot publication cleanup deferred. ExceptionType={ExceptionType}", exception.GetType().Name);
                }
            }
        }
    }

    /// <summary>
    /// Analyzes an already captured snapshot so capture and AI processing share the same correlation ID and files.
    /// </summary>
    /// <param name="activity">Current context sample used to build the analysis prompt.</param>
    /// <param name="captureResult">The completed snapshot capture to submit to the configured provider.</param>
    /// <param name="keepCapture">Whether the captured files should remain on disk after analysis.</param>
    /// <param name="origin">Stable analysis origin recorded with local usage and history.</param>
    /// <param name="cancellationToken">Cancels device-context collection and the provider request.</param>
    /// <returns>The AI summary record persisted in the local history store.</returns>
    public async Task<AiAnalysis> AnalyzeCapturedScreenAsync(
        AnalysisContextSnapshot? activity,
        ScreenshotCaptureResult captureResult,
        bool keepCapture,
        string origin,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(captureResult);
        var coreOwnsCleanup = false;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            captureResult = captureResult with
            {
                AnalysisScreenshotPaths = _store.SelectAnalysisScreenshotPaths(captureResult.AnalysisScreenshotPaths, captureResult.Images)
            };
            if (captureResult.AnalysisScreenshotPaths.Count == 0 ||
                captureResult.AnalysisScreenshotPaths.Any(path => !ScreenCaptureService.IsOwnedArtifact(path) || !File.Exists(path)))
            {
                throw new InvalidOperationException("The captured snapshot does not contain valid analysis files.");
            }

            var settings = _store.LoadSettings();
            var apiKey = LoadRequiredApiKey(settings);
            coreOwnsCleanup = true;
            return await AnalyzeCapturedScreenCoreAsync(
                settings,
                apiKey,
                activity,
                captureResult,
                keepCapture,
                origin,
                includeCurrentSystemContext: true,
                cancellationToken);
        }
        finally
        {
            if (!coreOwnsCleanup)
            {
                CleanupCapture(captureResult, keepCapture);
            }
        }
    }

    /// <inheritdoc />
    public async Task<AiAnalysis> AnalyzeHistoricalCapturedScreenAsync(
        AnalysisContextSnapshot activity,
        ScreenshotCaptureResult captureResult,
        string origin,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(activity);
        ArgumentNullException.ThrowIfNull(captureResult);
        var coreOwnsCleanup = false;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            captureResult = captureResult with
            {
                AnalysisScreenshotPaths = _store.SelectAnalysisScreenshotPaths(captureResult.AnalysisScreenshotPaths, captureResult.Images)
            };
            if (captureResult.AnalysisScreenshotPaths.Count == 0 ||
                captureResult.AnalysisScreenshotPaths.Any(path => !ScreenCaptureService.IsOwnedArtifact(path) || !File.Exists(path)))
            {
                throw new InvalidOperationException("The historical snapshot does not contain valid retained analysis files.");
            }

            var settings = _store.LoadSettings();
            var apiKey = LoadRequiredApiKey(settings);
            coreOwnsCleanup = true;
            return await AnalyzeCapturedScreenCoreAsync(
                settings,
                apiKey,
                activity,
                captureResult,
                keepCapture: true,
                origin,
                includeCurrentSystemContext: false,
                cancellationToken);
        }
        finally
        {
            if (!coreOwnsCleanup)
            {
                CleanupCapture(captureResult, keepCapture: true, ownsPublication: false);
            }
        }
    }

    private async Task<AiAnalysis> AnalyzeCapturedScreenCoreAsync(
        AppSettings settings,
        string apiKey,
        AnalysisContextSnapshot? activity,
        ScreenshotCaptureResult captureResult,
        bool keepCapture,
        string origin,
        bool includeCurrentSystemContext,
        CancellationToken cancellationToken)
    {
        try
        {
            if (!includeCurrentSystemContext && activity is null)
            {
                throw new InvalidOperationException("Historical analysis context is required.");
            }

            if (captureResult.AllScreenshotPaths.Count > 0 && captureResult.AnalysisScreenshotPaths.Count == 0)
                throw new InvalidOperationException("The capture rules selected gallery images only; no image is eligible for AI analysis.");

            // Locale context belongs to the capture boundary; historical analysis never polls the current machine.
            var deviceContext = captureResult.DeviceContext ?? activity?.DeviceContext;
            if (!settings.IncludeDeviceLocation && deviceContext is not null)
            {
                // Revocation applies to every outgoing request, including saved historical context.
                // Clone only the outbound projection so durable capture-time evidence stays unchanged.
                deviceContext = deviceContext with
                {
                    Location = new DeviceLocationSnapshot(null, null, null, "windows-geolocator", "disabled_by_setting")
                };
            }
            var context = (activity ?? new AnalysisContextSnapshot(
                "not available", "not available", "not available", "unknown", null)) with
            {
                DeviceContext = deviceContext,
                InformationalSchedule = activity?.InformationalSchedule
                    ?? (includeCurrentSystemContext ? ActiveHoursSchedule.BuildInformationalNote(
                        settings.ActiveHours, captureResult.CapturedAt ?? DateTimeOffset.UtcNow) : null)
            };
            // Explicit capture intent outweighs application attention presets, including on reanalysis.
            // Override only this request; keep the saved preference and capture/privacy policy unchanged.
            var userRequestedCapture = captureResult.CaptureOrigin == ScreenshotCaptureOrigins.Manual;
            if (userRequestedCapture)
            {
                settings = settings with { AiOutputDetail = "complete" };
            }
            var images = captureResult.Images ?? _store.LoadScreenshotImages(captureResult.AllScreenshotPaths);
            if (userRequestedCapture)
            {
                images = images.Select(image => image with { Attention = "high" }).ToArray();
            }
            context = PrepareImageContext(context, captureResult.FocusMetadata, images, captureResult.AnalysisScreenshotPaths);

            var prompt = AiPromptCatalog.RenderScreenshotAnalysis(
                settings.AiOutputDetail,
                context,
                customPrompt: settings.AiCustomPrompt,
                language: settings.UiLanguage,
                images: images,
                imagePaths: captureResult.AnalysisScreenshotPaths,
                userRequestedCapture: userRequestedCapture);
            var decoder = _decoder ?? AIDecoderFactory.Create(settings);
            var attemptId = Guid.NewGuid().ToString("N");
            var attemptedAt = DateTimeOffset.UtcNow;
            var attemptToken = CorrelationToken(attemptId);
            var captureToken = CorrelationToken(captureResult.CaptureId);
            var endpointHost = decoder.Provider == "codex-cli" ? "chatgpt.com" : AiProviderTelemetry.EndpointHost(settings.AiEndpoint);
            var requestedModel = decoder.Provider == "codex-cli" ? "codex-default" : settings.Model;
            _logger.LogInformation(
                "AI analysis attempt started. Attempt={Attempt} Correlation={Correlation} Origin={Origin} Provider={Provider} Model={Model} EndpointHost={EndpointHost} ImageCount={ImageCount}",
                attemptToken,
                captureToken,
                origin,
                decoder.Provider,
                requestedModel,
                endpointHost,
                captureResult.AnalysisScreenshotPaths.Count);
            // Record packet shape and measured input, never prompt text, titles, paths or image contents.
            _logger.LogInformation(
                "AI analysis package prepared. Attempt={Attempt} Correlation={Correlation} Detail={Detail} PromptCharacters={PromptCharacters} GalleryImages={GalleryImages} AnalysisImages={AnalysisImages} ActivityState={ActivityState} InputSampleAt={InputSampleAt} InputSampleSeconds={InputSampleSeconds} KeyPresses={KeyPresses} MouseClicks={MouseClicks} ContextAttributes={ContextAttributes} HasDeviceContext={HasDeviceContext} HasSchedule={HasSchedule}",
                attemptToken, captureToken, settings.AiOutputDetail, prompt.Length,
                captureResult.StoredScreenshotPaths.Count, captureResult.AnalysisScreenshotPaths.Count,
                context.State, context.InputEvidence?.SampledAt, context.InputEvidence?.DurationSeconds,
                context.InputEvidence?.KeyPresses, context.InputEvidence?.MouseClicks,
                context.Attributes?.Count ?? 0, context.DeviceContext is not null, context.InformationalSchedule is not null);
            for (var index = 0; index < captureResult.AnalysisScreenshotPaths.Count; index++)
            {
                var path = captureResult.AnalysisScreenshotPaths[index];
                var image = images.FirstOrDefault(candidate => string.Equals(candidate.Path, path, StringComparison.OrdinalIgnoreCase));
                _logger.LogInformation(
                    "AI analysis package image. Attempt={Attempt} Correlation={Correlation} ImageIndex={ImageIndex} Role={Role} Screen={Screen} Width={Width} Height={Height} Attention={Attention} Rule={Rule} MicroPrompts={MicroPrompts} HasApplication={HasApplication} HasWindowTitle={HasWindowTitle}",
                    attemptToken, captureToken, index + 1, image?.Role ?? "unknown", image?.ScreenIndex,
                    image?.ImageWidth, image?.ImageHeight, image?.Attention, image?.RuleReason,
                    image?.MicroPromptIds?.Count ?? 0, !string.IsNullOrEmpty(image?.ApplicationName), !string.IsNullOrEmpty(image?.WindowTitle));
            }
            // Submit the caller-owned analysis paths; live and historical callers have different retention behavior.
            AiProviderResult? providerResult = null;
            ScreenshotAnalysisResponse response;
            try
            {
                providerResult = await decoder.DecodeAsync(
                    prompt,
                    captureResult.AnalysisScreenshotPaths,
                    settings,
                    apiKey,
                    captureResult.CaptureId,
                    cancellationToken: cancellationToken);
                // A provider that completes after policy cancellation must not persist returned content.
                AiPolicyCancellation.ThrowIfRevoked();
                response = ScreenshotAnalysisResponse.Parse(providerResult.Text);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                _logger.LogInformation(
                    "AI analysis attempt completed. Attempt={Attempt} Correlation={Correlation} Origin={Origin} Provider={Provider} Model={Model} EndpointHost={EndpointHost} Outcome={Outcome}",
                    attemptToken,
                    captureToken,
                    origin,
                    decoder.Provider,
                    requestedModel,
                    endpointHost,
                    "cancelled");
                throw;
            }
            catch (AiProviderRequestException exception)
            {
                _logger.LogWarning(
                    "AI analysis attempt completed. Attempt={Attempt} Correlation={Correlation} Origin={Origin} Provider={Provider} Model={Model} EndpointHost={EndpointHost} HttpStatus={HttpStatus} FailureCategory={FailureCategory} LatencyMs={LatencyMs} ProviderRequestId={ProviderRequestId} Outcome={Outcome}",
                    attemptToken,
                    captureToken,
                    origin,
                    decoder.Provider,
                    requestedModel,
                    endpointHost,
                    exception.Failure.HttpStatusCode,
                    exception.Failure.FailureCode,
                    exception.Failure.ElapsedMilliseconds,
                    AiProviderTelemetry.SafeToken(exception.Failure.ProviderRequestId, 80),
                    "provider_failed");
                AppendFailedUsageOrThrow(CreateUsageRecord(
                    attemptId,
                    captureResult.CaptureId,
                    attemptedAt,
                    origin,
                    decoder.Provider,
                    settings,
                    captureResult.AnalysisScreenshotPaths.Count,
                    prompt.Length,
                    0, // No application-imposed output cap for screenshot descriptions.
                    null,
                    exception.Failure),
                    exception);
                throw;
            }
            catch (Exception exception)
            {
                var elapsed = (long)Math.Max(0, (DateTimeOffset.UtcNow - attemptedAt).TotalMilliseconds);
                _logger.LogWarning(
                    "AI analysis attempt completed. Attempt={Attempt} Correlation={Correlation} Origin={Origin} Provider={Provider} Model={Model} EndpointHost={EndpointHost} FailureCategory={FailureCategory} LatencyMs={LatencyMs} Outcome={Outcome} ExceptionType={ExceptionType}",
                    attemptToken,
                    captureToken,
                    origin,
                    decoder.Provider,
                    requestedModel,
                    endpointHost,
                    providerResult is null ? "unexpected" : "invalid-response",
                    elapsed,
                    "unexpected_failed",
                    exception.GetType().Name);
                AppendFailedUsageOrThrow(CreateUsageRecord(
                    attemptId,
                    captureResult.CaptureId,
                    attemptedAt,
                    origin,
                    decoder.Provider,
                    settings,
                    captureResult.AnalysisScreenshotPaths.Count,
                    prompt.Length,
                    0,
                    providerResult,
                    new AiProviderFailure(providerResult is null ? "unexpected" : "invalid-response", providerResult?.HttpStatusCode, elapsed)),
                    exception);
                throw;
            }

            // Once the provider has returned a successful response, persist it even if shutdown was
            // requested in the meantime. Recovery can then reconcile the durable job without billing a retry.
            var result = new AiAnalysis(
                DateTimeOffset.Now,
                context.Application,
                context.Context,
                response.Summary,
                settings.InstallationId,
                keepCapture ? string.Join(";", captureResult.StoredScreenshotPaths) : null,
                context.DeviceContext,
                captureResult.CaptureId,
                origin,
                context.InformationalSchedule,
                response.ActivityContinuity);
            var usage = CreateUsageRecord(
                attemptId,
                captureResult.CaptureId,
                attemptedAt,
                origin,
                decoder.Provider,
                settings,
                captureResult.AnalysisScreenshotPaths.Count,
                prompt.Length,
                0,
                providerResult,
                null);
            try
            {
                _store.AppendAiAnalysisAndUsage(usage, result);
            }
            catch (Exception exception)
            {
                _logger.LogError(
                    "AI analysis attempt persistence failed. Attempt={Attempt} Correlation={Correlation} Origin={Origin} Provider={Provider} Model={Model} EndpointHost={EndpointHost} HttpStatus={HttpStatus} LatencyMs={LatencyMs} Outcome={Outcome} ExceptionType={ExceptionType}",
                    attemptToken,
                    captureToken,
                    origin,
                    decoder.Provider,
                    requestedModel,
                    endpointHost,
                    providerResult.HttpStatusCode,
                    providerResult.ElapsedMilliseconds,
                    "persistence_failed",
                    exception.GetType().Name);
                throw;
            }

            _logger.LogInformation(
                "AI analysis attempt completed. Attempt={Attempt} Correlation={Correlation} Origin={Origin} Provider={Provider} Model={Model} EndpointHost={EndpointHost} HttpStatus={HttpStatus} LatencyMs={LatencyMs} ProviderRequestId={ProviderRequestId} Outcome={Outcome}",
                attemptToken,
                captureToken,
                origin,
                decoder.Provider,
                requestedModel,
                endpointHost,
                providerResult.HttpStatusCode,
                providerResult.ElapsedMilliseconds,
                AiProviderTelemetry.SafeToken(providerResult.ProviderRequestId, 80),
                "succeeded");
            _logger.LogInformation(
                "AI activity assessment persisted. Attempt={Attempt} Correlation={Correlation} SummaryCharacters={SummaryCharacters} ContinuesConfidence={ContinuesConfidence} EndedConfidence={EndedConfidence}",
                attemptToken, captureToken, response.Summary.Length,
                response.ActivityContinuity.ContinuesConfidence, response.ActivityContinuity.EndedConfidence);

            if (includeCurrentSystemContext)
                (_capture as IScreenshotActivityContinuityObserver)?.ObserveActivityContinuity(captureResult, response.ActivityContinuity);
            return result;
        }
        finally
        {
            CleanupCapture(captureResult, keepCapture, ownsPublication: includeCurrentSystemContext);
        }
    }

    private void CleanupCapture(ScreenshotCaptureResult captureResult, bool keepCapture, bool ownsPublication = true)
    {
        // Reprocessing borrows retained images; it must never complete another live capture's journal.
        if (!ownsPublication) return;
        try
        {
            if (keepCapture) _screenshotPublications.CleanupTransientArtifacts(captureResult.CaptureId);
            else _screenshotPublications.Discard(captureResult.CaptureId);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or Microsoft.Data.Sqlite.SqliteException)
        {
            // The durable journal retries transient cleanup on restart without hiding the primary error.
            _logger.LogWarning("Screenshot destination cleanup remains journaled. ExceptionType={ExceptionType}", exception.GetType().Name);
        }
        var retainedPaths = captureResult.StoredScreenshotPaths.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var cleanupPaths = keepCapture
            ? captureResult.AnalysisScreenshotPaths.Where(path => !retainedPaths.Contains(path))
            : captureResult.AllScreenshotPaths;

        foreach (var screenshot in cleanupPaths
                     .Where(ScreenCaptureService.IsOwnedArtifact)
                     .Distinct(StringComparer.OrdinalIgnoreCase)
                     .Where(File.Exists))
        {
            try
            {
                File.Delete(screenshot);
            }
            catch
            {
                // Cleanup is best effort: retained history remains valid and provider/persistence errors stay primary.
            }
        }
    }

    /// <summary>Prevents excluded foreground details from leaking through text beside otherwise eligible images.</summary>
    internal static AnalysisContextSnapshot PrepareImageContext(AnalysisContextSnapshot context,
        ScreenshotFocusMetadata? focus, IReadOnlyList<ScreenshotImageMetadata> images, IReadOnlyList<string> paths)
    {
        if (images.Count == 0) return ApplyCaptureFocusMetadata(context, focus);
        var eligible = images.Where(image => image.ForAnalysis && paths.Contains(image.Path, StringComparer.OrdinalIgnoreCase)).ToArray();
        var active = eligible.FirstOrDefault(image => image.Role == "active-window")
            ?? eligible.FirstOrDefault(image => image.Role == "monitor-context" && image.IsActiveScreen);
        // Activity samples may name an excluded app. Rebuild foreground evidence only from eligible capture facts.
        var safeContext = context with
        {
            Application = "not available",
            Context = "not available",
            WindowTitle = string.Empty,
            Attributes = null
        };
        if (active is null) return safeContext;
        return ApplyCaptureFocusMetadata(safeContext, new ScreenshotFocusMetadata(
            active.ScreenName, active.ScreenIndex, active.ScreenLeft, active.ScreenTop, active.ScreenWidth, active.ScreenHeight,
            active.ApplicationName, active.WindowTitle, active.Role == "active-window" ? "active-window" : $"monitor-{active.ScreenIndex}"));
    }

    private static AnalysisContextSnapshot ApplyCaptureFocusMetadata(
        AnalysisContextSnapshot context,
        ScreenshotFocusMetadata? focus)
    {
        if (focus is null)
        {
            return context;
        }

        var attributes = context.Attributes is null
            ? new Dictionary<string, string>(StringComparer.Ordinal)
            : new Dictionary<string, string>(context.Attributes, StringComparer.Ordinal);
        attributes["FocusedScreen"] = focus.ScreenName;
        attributes["FocusedScreenBounds"] = string.Format(
            CultureInfo.InvariantCulture,
            "{0},{1} {2}x{3}",
            focus.ScreenLeft,
            focus.ScreenTop,
            focus.ScreenWidth,
            focus.ScreenHeight);
        attributes["FocusedCapture"] = focus.ArtifactStem;

        return context with
        {
            Application = string.IsNullOrWhiteSpace(focus.ApplicationName)
                ? context.Application
                : focus.ApplicationName,
            WindowTitle = string.IsNullOrWhiteSpace(focus.WindowTitle)
                ? context.WindowTitle
                : focus.WindowTitle,
            Attributes = attributes
        };
    }

    private string LoadRequiredApiKey(AppSettings settings)
    {
        if (!settings.OpenAiEnabled)
        {
            throw new InvalidOperationException("Enable AI integration from the app options before running analysis.");
        }

        // Codex authenticates through its own ChatGPT login; never load or forward an API key.
        if (settings.AiProvider == "codex-cli") return string.Empty;
        var apiKey = _store.LoadApiKey(settings.AiApiKeyName);
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException($"Set the environment variable {settings.AiApiKeyName} for {settings.AiProvider}.");
        }

        return apiKey;
    }

    private void AppendFailedUsageOrThrow(AiRequestUsageRecord usage, Exception providerException)
    {
        try
        {
            _store.AppendAiUsage(usage);
        }
        catch (Exception persistenceException)
        {
            throw new AggregateException(
                "The AI provider request and its local usage persistence both failed.",
                providerException,
                persistenceException);
        }
    }

    private static string CorrelationToken(string value) =>
        string.IsNullOrEmpty(value) ? "unavailable" : value[..Math.Min(12, value.Length)];

    private static AiRequestUsageRecord CreateUsageRecord(
        string attemptId,
        string correlationId,
        DateTimeOffset attemptedAt,
        string origin,
        string provider,
        AppSettings settings,
        int imageCount,
        int promptCharacters,
        int maxOutputTokens,
        AiProviderResult? result,
        AiProviderFailure? failure)
    {
        var completedAt = DateTimeOffset.UtcNow;
        return new AiRequestUsageRecord(
            attemptId,
            correlationId,
            attemptedAt,
            completedAt,
            origin,
            "screen_analysis",
            provider,
            provider == "codex-cli" ? "chatgpt.com" : AiProviderTelemetry.EndpointHost(settings.AiEndpoint),
            provider == "codex-cli" ? "codex-default" : settings.Model,
            result?.ReturnedModel,
            result?.ProviderResponseId ?? failure?.ProviderResponseId,
            result?.ProviderRequestId ?? failure?.ProviderRequestId,
            result?.HttpStatusCode ?? failure?.HttpStatusCode,
            result?.ElapsedMilliseconds ?? failure?.ElapsedMilliseconds,
            result?.ProviderProcessingMilliseconds ?? failure?.ProviderProcessingMilliseconds,
            imageCount,
            promptCharacters,
            provider == "codex-cli" ? 0 : maxOutputTokens,
            result?.Usage ?? failure?.Usage ?? new AiUsageMetrics(),
            result?.FinishReason ?? failure?.FinishReason,
            result is not null && failure is null,
            failure?.FailureCode);
    }

}
