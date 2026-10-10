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


using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using WorkTrail.Application;
using WorkTrail.Ocr;

namespace WorkTrail.Services;

/// <summary>Attaches local OCR output to screenshot snapshots and persists the reconstructible text projection.</summary>
public sealed class ScreenshotTextExtractionCoordinator
{
    private readonly LocalStore _store;
    private readonly IScreenshotOcrService _ocr;
    private readonly ILogger<ScreenshotTextExtractionCoordinator> _logger;

    /// <summary>Creates the runtime-owned screenshot text extraction coordinator.</summary>
    internal ScreenshotTextExtractionCoordinator(
        LocalStore store,
        IScreenshotOcrService ocr,
        ILogger<ScreenshotTextExtractionCoordinator>? logger = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _ocr = ocr ?? throw new ArgumentNullException(nameof(ocr));
        _logger = logger ?? NullLogger<ScreenshotTextExtractionCoordinator>.Instance;
    }

    /// <summary>Gets whether the runtime-owned local text reader is currently enabled.</summary>
    internal bool IsEnabled => _ocr.IsEnabled;

    /// <summary>Extracts text from every raw analysis artifact and returns a snapshot carrying the complete raw result.</summary>
    internal async Task<ScreenshotCaptureResult> AttachAsync(
        ScreenshotCaptureResult capture,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(capture);
        if (!_ocr.IsEnabled)
        {
            // Disabled OCR leaves the capture contract untouched and performs no image or persistence I/O.
            return capture;
        }

        long startedTimestamp = Stopwatch.GetTimestamp();
        var snapshots = new List<ScreenshotTextSnapshot>(capture.AnalysisScreenshotPaths.Count);
        foreach (var sourcePath in capture.AnalysisScreenshotPaths.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            ScreenshotTextSnapshot snapshot;
            try
            {
                // The OCR module owns image decoding and Windows interop; Core only projects its immutable result.
                var result = await _ocr.ExtractAsync(sourcePath, cancellationToken).ConfigureAwait(false);
                snapshot = new ScreenshotTextSnapshot(sourcePath, Project(result));
            }
            catch (ScreenshotOcrLanguageUnavailableException exception)
            {
                snapshot = Failed(sourcePath, "ocr.language.unavailable");
                LogFailure(capture.CaptureId, "language_unavailable", exception);
            }
            catch (ScreenshotOcrInteropException exception)
            {
                snapshot = Failed(sourcePath, $"ocr.interop.{exception.Stage.ToString().ToLowerInvariant()}");
                LogFailure(capture.CaptureId, $"interop_{exception.Stage.ToString().ToLowerInvariant()}", exception);
            }
            catch (ArgumentException exception)
            {
                snapshot = Failed(sourcePath, "ocr.configuration.invalid");
                LogFailure(capture.CaptureId, "configuration_invalid", exception);
            }

            // OCR is optional enrichment: a typed failure is retained in the snapshot instead of discarding the capture.
            _store.UpsertScreenshotTextSnapshot(capture.CaptureId, snapshot);
            snapshots.Add(snapshot);
        }

        _logger.LogInformation(
            "Local screenshot OCR completed. Capture={Capture} Artifacts={ArtifactCount} Failed={FailedCount} ElapsedMilliseconds={ElapsedMilliseconds}",
            CorrelationToken(capture.CaptureId),
            snapshots.Count,
            snapshots.Count(static snapshot => snapshot.Ocr.Status == ScreenshotTextExtractionStatus.Failed),
            (long)Stopwatch.GetElapsedTime(startedTimestamp).TotalMilliseconds);

        return capture with { TextSnapshots = snapshots };
    }

    private static OcrRawSnapshot Project(ScreenshotOcrResult result) => new(
        result.Status switch
        {
            OcrExtractionStatus.Disabled => ScreenshotTextExtractionStatus.Disabled,
            OcrExtractionStatus.NoText => ScreenshotTextExtractionStatus.NoText,
            OcrExtractionStatus.Succeeded => ScreenshotTextExtractionStatus.Succeeded,
            _ => throw new InvalidDataException($"Unsupported OCR status '{result.Status}'.")
        },
        result.RawText,
        result.EffectiveLanguageTag,
        result.TextAngleDegrees,
        result.CompletedAtUtc,
        result.EngineName,
        result.PixelWidth,
        result.PixelHeight,
        result.Lines.Select(line => new OcrLineSnapshot(
            line.Text,
            line.Words.Select(word => new OcrWordSnapshot(
                word.Text,
                word.BoundingRectangle.X,
                word.BoundingRectangle.Y,
                word.BoundingRectangle.Width,
                word.BoundingRectangle.Height)).ToArray())).ToArray());

    private static ScreenshotTextSnapshot Failed(
        string sourcePath,
        string failureCode,
        uint? pixelWidth = null,
        uint? pixelHeight = null) =>
        new(
            sourcePath,
            new OcrRawSnapshot(
                ScreenshotTextExtractionStatus.Failed,
                string.Empty,
                null,
                null,
                DateTimeOffset.UtcNow,
                WindowsScreenshotOcrService.EngineName,
                pixelWidth,
                pixelHeight,
                Array.Empty<OcrLineSnapshot>(),
                failureCode));

    private void LogFailure(string captureId, string failureCode, Exception exception) =>
        _logger.LogWarning(
            "Local screenshot OCR did not complete. Capture={Capture} FailureCategory={FailureCategory} ExceptionType={ExceptionType}",
            CorrelationToken(captureId),
            failureCode,
            exception.GetType().Name);

    private static string CorrelationToken(string value) =>
        string.IsNullOrEmpty(value) ? "unavailable" : value[..Math.Min(12, value.Length)];
}
