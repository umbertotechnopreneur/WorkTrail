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
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SkiaSharp;
using WorkTrail.Application;
using WorkTrail.Providers;

namespace WorkTrail.Services;

/// <summary>Captures one validated snapshot pass for application-layer orchestration.</summary>
public interface IScreenCaptureService
{
    /// <summary>Captures only after the application authorizes the foreground context at the pixel boundary.</summary>
    /// <param name="directory">Root directory below which calendar-based capture folders are created.</param>
    /// <param name="captureMode">Capture mode: all-screens or active-window.</param>
    /// <param name="captureOrigin">Stable manual or scheduled capture origin.</param>
    /// <param name="authorizeCapture">Fail-closed application policy evaluated immediately before capture.</param>
    /// <returns>The final analysis and retained paths; images remain staged until Core publishes their provenance.</returns>
    ScreenshotCaptureResult CaptureByMode(
        string directory,
        string captureMode,
        string captureOrigin,
        Func<ScreenshotCaptureContext, ScreenshotCaptureDecision> authorizeCapture);
}

/// <summary>Commits comparison baselines only after the application publishes a retained capture.</summary>
internal interface IScreenshotCapturePublicationObserver
{
    void CompletePublication(string captureId, bool retained);
}

/// <summary>Classifies the application-owned policy decision made immediately before screenshot pixels are read.</summary>
public enum ScreenshotCaptureDecision
{
    /// <summary>The current foreground context may be captured.</summary>
    Allowed,

    /// <summary>Screenshot capture was disabled while the operation was waiting to run.</summary>
    ScreenshotsDisabled,

    /// <summary>The current foreground context is private or cannot be proven safe.</summary>
    PrivacyBlocked,

    /// <summary>The scheduled capture exceeded its configured input-inactivity timeout.</summary>
    UserIdle,

    /// <summary>Every selected automatic image is visually unchanged since its last retained capture.</summary>
    Unchanged,

    /// <summary>The desktop did not settle before an automatic capture could safely complete.</summary>
    Unstable,

    /// <summary>Capture rules exclude every selected image before any pixels are read.</summary>
    ContentExcluded,

    /// <summary>No selected window has enough coherent visible content for a useful image.</summary>
    InsufficientVisibleContent
}

/// <summary>Describes the foreground target observed at the capture boundary.</summary>
/// <param name="ProcessName">Foreground process name, or empty when Windows cannot resolve it.</param>
/// <param name="ApplicationName">Provider-normalized application label.</param>
/// <param name="Context">Provider-normalized activity context.</param>
/// <param name="WindowTitle">Foreground window title, or empty when unavailable.</param>
public sealed record ScreenshotCaptureContext(
    string ProcessName,
    string ApplicationName,
    string Context,
    string WindowTitle)
{
    /// <summary>Represents foreground metadata that could not be resolved by an alternate capture implementation.</summary>
    public static ScreenshotCaptureContext Unavailable { get; } = new(string.Empty, string.Empty, string.Empty, string.Empty);
}

/// <summary>Stops screenshot acquisition when application policy changes before pixels are read.</summary>
public sealed class ScreenshotCapturePreconditionException : InvalidOperationException
{
    internal TimesheetUnchangedEvidence? UnchangedEvidence { get; init; }
    /// <summary>Creates a capture precondition failure for the supplied policy decision.</summary>
    /// <param name="decision">Non-allowed decision returned by the application policy.</param>
    public ScreenshotCapturePreconditionException(ScreenshotCaptureDecision decision)
        : base($"Screenshot capture was rejected by the runtime policy: {decision}.")
    {
        if (decision == ScreenshotCaptureDecision.Allowed)
        {
            throw new ArgumentOutOfRangeException(nameof(decision), decision, "An allowed capture cannot produce a precondition failure.");
        }

        Decision = decision;
    }

    /// <summary>Gets the policy decision that rejected the capture.</summary>
    public ScreenshotCaptureDecision Decision { get; }
}

/// <summary>Signals a transient desktop transition, without treating manual retries as silent skips.</summary>
internal sealed class ScreenshotSceneChangedException(string message) : InvalidOperationException(message)
{
}

/// <summary>
/// Captures desktop images to WEBP for local history and AI context.
/// </summary>
public sealed class ScreenCaptureService : IScreenCaptureService, IScreenshotCapturePublicationObserver
{
    private const int WebpQuality = 90;
    private static readonly Regex OwnedArtifactName = new(
        "^[0-9a-f]{32}_[0-9]+\\.[0-9]+\\.[0-9]+_(?:manual|scheduled)_(?:monitor-[1-9][0-9]*|secondary-window-[1-9][0-9]*|active-window)(?:-raw)?\\.(?:webp|png)$",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase | RegexOptions.NonBacktracking);
    private readonly string _appVersion;
    private readonly SettingsSnapshot? _settingsSnapshot;
    private readonly ILogger<ScreenCaptureService> _logger;
    private readonly ScreenshotSimilarityFilter _similarity = new();
    private readonly object _pendingGate = new();
    private readonly Dictionary<string, List<PendingFingerprint>> _pendingFingerprints = new(StringComparer.Ordinal);

    private sealed record PendingFingerprint(string Key, ScreenshotVisualFingerprint Fingerprint,
        DateTimeOffset CapturedAt, ScreenshotSimilarityOptions Options, string Path);

    void IScreenshotCapturePublicationObserver.CompletePublication(string captureId, bool retained)
    {
        lock (_pendingGate)
        {
            if (!_pendingFingerprints.Remove(captureId, out var pending) || !retained) return;
            foreach (var candidate in pending)
            {
                // Publication and retention can fail independently. Missing files cannot become baselines.
                if (File.Exists(candidate.Path))
                    _similarity.Remember(candidate.Key, candidate.Fingerprint, candidate.CapturedAt, candidate.Options, candidate.Path);
            }
        }
    }

    /// <summary>
    /// Creates capture service.
    /// </summary>
    /// <param name="appVersion">Optional version suffix used in file naming.</param>
    /// <param name="settingsSnapshot">Current runtime settings used at the capture boundary.</param>
    /// <param name="logger">Durable diagnostics for skipped automatic images.</param>
    public ScreenCaptureService(string? appVersion = null, SettingsSnapshot? settingsSnapshot = null,
        ILogger<ScreenCaptureService>? logger = null)
    {
        _appVersion = string.IsNullOrWhiteSpace(appVersion) ? "0.0.0" : appVersion;
        _settingsSnapshot = settingsSnapshot;
        _logger = logger ?? NullLogger<ScreenCaptureService>.Instance;
    }

    /// <inheritdoc />
    public ScreenshotCaptureResult CaptureByMode(
        string directory,
        string captureMode,
        string captureOrigin,
        Func<ScreenshotCaptureContext, ScreenshotCaptureDecision> authorizeCapture)
    {
        ArgumentNullException.ThrowIfNull(authorizeCapture);
        var captureId = Guid.NewGuid().ToString("N");
        var validatedOrigin = ScreenshotCaptureOrigins.Validate(captureOrigin);
        if (captureMode is not "active-window" and not "all-screens")
        {
            throw new ArgumentException("Screenshot capture mode must be 'all-screens' or 'active-window'.", nameof(captureMode));
        }

        var scheduled = validatedOrigin == ScreenshotCaptureOrigins.Scheduled;
        var correlation = captureId[..12];
        _logger.LogInformation("Screenshot capture started. Correlation={Correlation} Origin={Origin} Mode={Mode}",
            correlation, validatedOrigin, captureMode);
        try
        {
            // The configured timer is sufficient: reading without input and normal window
            // changes do not veto screenshots. Explicit privacy still surrounds acquisition.
            var foreground = CaptureForegroundTarget();
            EnsureCaptureAllowed(authorizeCapture(foreground.ToCaptureContext()));
            // Timestamp the selected scene, including midnight boundaries.
            var capturedAt = DateTimeOffset.Now;
            return CaptureTargets(directory, captureId, validatedOrigin, capturedAt, foreground, captureMode == "active-window", authorizeCapture);
        }
        catch (ScreenshotSceneChangedException exception) when (scheduled)
        {
            _logger.LogInformation("Scheduled screenshot scene changed. Correlation={Correlation} Reason={Reason}", correlation, exception.Message);
            throw new ScreenshotCapturePreconditionException(ScreenshotCaptureDecision.Unstable);
        }
        catch (ScreenshotCapturePreconditionException exception)
        {
            _logger.LogInformation("Screenshot capture rejected. Correlation={Correlation} Decision={Decision}", correlation, exception.Decision);
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogWarning("Screenshot capture failed. Correlation={Correlation} ExceptionType={ExceptionType}", correlation, exception.GetType().Name);
            throw;
        }
    }

    /// <summary>Returns whether a path matches the versioned naming contract of a WorkTrail screenshot artifact.</summary>
    /// <param name="path">Candidate file path.</param>
    /// <returns>True only for files that WorkTrail capture code can create.</returns>
    public static bool IsOwnedArtifact(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        return OwnedArtifactName.IsMatch(Path.GetFileName(path));
    }

    private ScreenshotCaptureResult CaptureTargets(
        string directory,
        string captureId,
        string captureOrigin,
        DateTimeOffset capturedAt,
        ForegroundCaptureTarget foreground,
        bool activeWindowOnly,
        Func<ScreenshotCaptureContext, ScreenshotCaptureDecision> authorizeCapture)
    {
        var validatedOrigin = ScreenshotCaptureOrigins.Validate(captureOrigin);
        var captureDirectory = ScreenshotStorageLayout.GetDayDirectory(directory, capturedAt);
        ScreenshotStorageLayout.RejectLinks(captureDirectory);
        var displays = EnumerateDisplays();
        var visibleWindows = ReadVisibleWindows();
        var windows = visibleWindows.Select(window => new CaptureWindow(window.WindowHandle, window.Bounds,
            window.Context.ProcessName, window.Context.ApplicationName,
            _settingsSnapshot is null || ActivityContextProviderRegistry.IsDetailEnabled(window.Context.ProcessName, _settingsSnapshot.Value)
                ? window.Context.WindowTitle : string.Empty,
            window.IsApplicationWindow, window.IsCaptureExcluded)).ToArray();
        var policy = ScreenshotCaptureRules.Load();
        var targets = policy.Plan(displays, foreground, windows, activeWindowOnly);
        var correlation = captureId[..12];
        _logger.LogInformation(
            "Screenshot selection completed. Correlation={Correlation} Displays={Displays} VisibleWindows={VisibleWindows} Targets={Targets}",
            correlation, displays.Count, visibleWindows.Count, targets.Count);
        var foregroundAuxiliary = visibleWindows.Any(window => window.WindowHandle == foreground.WindowHandle && window.IsCaptureExcluded);
        var foregroundExcluded = foregroundAuxiliary || policy.IsAnalysisExcluded(foreground.ProcessName);
        var activeTarget = targets.FirstOrDefault(target => target.Role == "active-window");
        var focusedDisplay = activeTarget?.Display ?? ResolveFocusedDisplay(displays, foreground.WindowBounds);
        var focusMetadata = CreateFocusMetadata(focusedDisplay, foreground, activeTarget?.Stem ?? focusedDisplay.Stem, policy, foregroundAuxiliary);
        // A fully excluded pass must not leave an empty day folder behind.
        Directory.CreateDirectory(captureDirectory);

        var storage = new List<string>(displays.Count);
        var analysis = new List<string>(displays.Count);
        var images = new List<ScreenshotImageMetadata>(targets.Count);
        var pending = new List<PendingFingerprint>(targets.Count);
        var unstableTargets = 0;
        TimesheetUnchangedEvidence? unchangedEvidence = null;
        try
        {
            foreach (var target in targets)
            {
                _logger.LogInformation(
                    "Screenshot target selected. Correlation={Correlation} Role={Role} Rule={Rule} Attention={Attention} ForGallery={ForGallery} ForAnalysis={ForAnalysis} VisibleRegions={VisibleRegions}",
                    correlation, target.Role, target.RuleReason, target.Attention, target.ForGallery, target.ForAnalysis, target.VisibleRegions.Count);
                var processName = windows.FirstOrDefault(window => window.WindowHandle == target.WindowHandle)?.ProcessName;
                var similarityOptions = target.WindowHandle == IntPtr.Zero
                    ? policy.Similarity ?? new ScreenshotSimilarityOptions()
                    : policy.ResolveWindowPolicy(processName ?? string.Empty, target.WindowTitle).Similarity;
                var similarityKey = validatedOrigin == ScreenshotCaptureOrigins.Scheduled && similarityOptions.Enabled
                    ? CreateSimilarityKey(directory, target, processName,
                        visibleWindows.Where(window => !window.IsCaptureExcluded && !policy.IsCaptureExcluded(window.Context.ProcessName)).ToArray()) : string.Empty;
                (IReadOnlyList<string> Analysis, IReadOnlyList<string> Stored, int Width, int Height) paths;
                try
                {
                    paths = CaptureRect(
                        captureDirectory,
                        target.Bounds,
                        target.WindowHandle,
                        target.Stem,
                        captureId,
                        validatedOrigin,
                        capturedAt,
                        authorizeCapture,
                        target.VisibleRegions,
                        policy,
                        target.ForAnalysis,
                        similarityKey,
                        similarityOptions,
                        target.ForGallery,
                        pending,
                        out var matchedPath);
                    if (matchedPath is not null && target.ForAnalysis && target.Role == "active-window"
                        && target.WindowHandle == foreground.WindowHandle)
                    {
                        var current = CaptureForegroundTarget();
                        if (current.WindowHandle == foreground.WindowHandle && current.ProcessName == foreground.ProcessName
                            && current.WindowTitle == foreground.WindowTitle)
                        {
                            var retainCheckpointTitle = _settingsSnapshot is null || ActivityContextProviderRegistry.IsDetailEnabled(current.ProcessName, _settingsSnapshot.Value);
                            unchangedEvidence = new(captureId, capturedAt, matchedPath, current.ApplicationName,
                                retainCheckpointTitle ? current.WindowTitle : null);
                        }
                    }
                }
                catch (ScreenshotSceneChangedException exception) when (
                    validatedOrigin == ScreenshotCaptureOrigins.Scheduled && target.Role != "active-window")
                {
                    // A closed, minimized or resized optional surface does not erase valid images.
                    AuthorizeCurrentForeground(authorizeCapture);
                    unstableTargets++;
                    _logger.LogInformation(
                        "Scheduled screenshot target skipped. Correlation={Correlation} Role={Role} Code={Code} Reason={Reason}",
                        correlation, target.Role, "screenshot.unstable", exception.Message);
                    continue;
                }

                if (paths.Stored.Count == 0)
                {
                    _logger.LogInformation("Screenshot target skipped. Correlation={Correlation} Role={Role} Code={Code}",
                        correlation, target.Role, "screenshot.unchanged");
                    continue;
                }

                if (target.ForAnalysis) analysis.AddRange(paths.Analysis);
                if (target.ForGallery) storage.AddRange(paths.Stored);
                _logger.LogInformation(
                    "Screenshot target acquired. Correlation={Correlation} Role={Role} Width={Width} Height={Height} GalleryImages={GalleryImages} AnalysisImages={AnalysisImages}",
                    correlation, target.Role, paths.Width, paths.Height,
                    target.ForGallery ? paths.Stored.Count : 0, target.ForAnalysis ? paths.Analysis.Count : 0);
                var screen = target.Display;
                var isWindow = target.WindowHandle != IntPtr.Zero;
                var retainTitle = _settingsSnapshot is null || string.IsNullOrWhiteSpace(processName)
                    || ActivityContextProviderRegistry.IsDetailEnabled(processName, _settingsSnapshot.Value);
                images.Add(new ScreenshotImageMetadata(paths.Stored[0], target.Role, screen.Index, screen.Name,
                    screen.Bounds.Left, screen.Bounds.Top, screen.Bounds.Right - screen.Bounds.Left, screen.Bounds.Bottom - screen.Bounds.Top,
                    target.Bounds.Left, target.Bounds.Top, paths.Width, paths.Height,
                    screen.Index == focusedDisplay.Index,
                    isWindow ? target.ApplicationName : screen.Index == focusedDisplay.Index && !foregroundExcluded ? focusMetadata.ApplicationName : string.Empty,
                    isWindow ? retainTitle ? target.WindowTitle : string.Empty : screen.Index == focusedDisplay.Index && !foregroundExcluded ? focusMetadata.WindowTitle : string.Empty,
                    target.RelatedScreenIndices, target.Priority, target.ForAnalysis, target.ForGallery, target.Attention, target.RuleReason, target.MicroPromptIds));
            }
            if (targets.Count > 0 && images.Count == 0)
                throw new ScreenshotCapturePreconditionException(unstableTargets > 0
                    ? ScreenshotCaptureDecision.Unstable : ScreenshotCaptureDecision.Unchanged)
                { UnchangedEvidence = unstableTargets == 0 ? unchangedEvidence : null };
        }
        catch
        {
            DeletePartialArtifacts(analysis.Concat(storage));
            throw;
        }

        if (pending.Count > 0)
        {
            lock (_pendingGate)
            {
                // Normally one pass is in flight. Bound orphaned candidates if an owner abandons publication.
                while (_pendingFingerprints.Count >= 8)
                    _pendingFingerprints.Remove(_pendingFingerprints.Keys.First());
                _pendingFingerprints[captureId] = pending;
            }
        }

        return new ScreenshotCaptureResult(
            captureId,
            analysis.AsReadOnly(),
            storage.AsReadOnly(),
            validatedOrigin,
            FocusMetadata: focusMetadata,
            CapturedAt: capturedAt,
            Images: images.AsReadOnly())
        { ContinuityForeground = foreground, UnchangedEvidence = unchangedEvidence };
    }

    internal static string CreateSimilarityKey(string directory, CaptureImageTarget target, string? processName,
        IReadOnlyList<VisibleCaptureWindow> visibleWindows)
    {
        static int[] Bounds(NativeMethods.Rect value) => [value.Left, value.Top, value.Right, value.Bottom];
        // Titles are hints for identity, not proof of unchanged pixels. Hash transient metadata; never log it.
        var identity = JsonSerializer.Serialize(new
        {
            Directory = Path.GetFullPath(directory),
            target.Role,
            Handle = target.WindowHandle.ToInt64(),
            processName,
            target.WindowTitle,
            Screen = target.Display.Name,
            ScreenBounds = Bounds(target.Display.Bounds),
            Bounds = Bounds(target.Bounds),
            Regions = target.VisibleRegions.Select(Bounds),
            target.ForAnalysis,
            target.ForGallery,
            target.Attention,
            target.RuleReason,
            target.MicroPromptIds,
            Scene = target.WindowHandle == IntPtr.Zero
                ? visibleWindows.Where(window => IntersectionArea(target.Bounds, window.Bounds) > 0)
                    .Select(window => new { Handle = window.WindowHandle.ToInt64(), window.Context, Bounds = Bounds(window.Bounds) }).ToArray()
                : null
        });
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)));
    }

    /// <summary>Returns the zero-based display index that contains the largest foreground-window area.</summary>
    /// <param name="displays">Physical display bounds returned by Windows monitor enumeration.</param>
    /// <param name="windowBounds">Physical foreground window bounds.</param>
    /// <returns>The index of the display with the largest intersection.</returns>
    internal static int SelectFocusedDisplayIndex(IReadOnlyList<NativeMethods.Rect> displays, NativeMethods.Rect windowBounds)
    {
        if (displays.Count == 0)
        {
            throw new ArgumentException("At least one display is required to resolve the focused screen.", nameof(displays));
        }

        var bestDisplayIndex = 0;
        var bestArea = -1L;
        for (var index = 0; index < displays.Count; index++)
        {
            var area = IntersectionArea(displays[index], windowBounds);
            if (area > bestArea)
            {
                bestDisplayIndex = index;
                bestArea = area;
            }
        }

        return bestDisplayIndex;
    }

    private static IReadOnlyList<CaptureDisplay> EnumerateDisplays()
    {
        var displays = new List<CaptureDisplay>();
        NativeMethods.MonitorEnumProc callback = (IntPtr monitor, IntPtr deviceContext, ref NativeMethods.Rect rect, IntPtr data) =>
        {
            displays.Add(new CaptureDisplay(displays.Count + 1, rect));
            return true;
        };

        if (!NativeMethods.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, callback, IntPtr.Zero) || displays.Count == 0)
        {
            // Without monitor bounds the snapshot cannot name or prioritize the focused screen.
            throw new InvalidOperationException("Unable to enumerate display bounds for screenshot capture.");
        }

        return displays;
    }

    private static CaptureDisplay ResolveFocusedDisplay(IReadOnlyList<CaptureDisplay> displays, NativeMethods.Rect windowBounds)
    {
        var selectedIndex = SelectFocusedDisplayIndex(displays.Select(display => display.Bounds).ToArray(), windowBounds);
        return displays[selectedIndex];
    }

    private static ForegroundCaptureTarget CaptureForegroundTarget()
    {
        var window = NativeMethods.GetForegroundWindow();
        if (window == IntPtr.Zero)
        {
            throw new ScreenshotSceneChangedException("No active foreground window found.");
        }

        if (!WindowsGraphicsCaptureService.TryReadWindowBounds(window, out var bounds))
        {
            throw new ScreenshotSceneChangedException("Unable to read active window bounds.");
        }

        var width = bounds.Right - bounds.Left;
        var height = bounds.Bottom - bounds.Top;
        if (width <= 0 || height <= 0)
        {
            throw new ScreenshotSceneChangedException("Invalid active window size.");
        }

        var processName = ReadProcessName(window);
        var title = ReadWindowTitle(window);
        var context = new ActivityContextProviderRegistry().Resolve(new ForegroundWindowInfo(processName, title));
        return new ForegroundCaptureTarget(window, bounds, processName, context.Application, context.Context, title);
    }

    private static string ReadProcessName(IntPtr window)
    {
        NativeMethods.GetWindowThreadProcessId(window, out var processId);
        if (processId == 0)
        {
            return string.Empty;
        }

        try
        {
            using var process = Process.GetProcessById((int)processId);
            return string.IsNullOrWhiteSpace(process.ProcessName) ? string.Empty : process.ProcessName;
        }
        catch (ArgumentException)
        {
            return string.Empty;
        }
        catch (InvalidOperationException)
        {
            return string.Empty;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return string.Empty;
        }
    }

    private static string ReadWindowTitle(IntPtr window)
    {
        var builder = new StringBuilder(512);
        var length = NativeMethods.GetWindowText(window, builder, builder.Capacity);
        return length <= 0 ? string.Empty : builder.ToString().Trim();
    }

    internal ScreenshotFocusMetadata CreateFocusMetadata(
        CaptureDisplay display,
        ForegroundCaptureTarget foreground,
        string artifactStem,
        ScreenshotCaptureRules policy,
        bool excludedWindow = false)
        => new(
            display.Name,
            display.Index,
            display.Bounds.Left,
            display.Bounds.Top,
            display.Bounds.Right - display.Bounds.Left,
            display.Bounds.Bottom - display.Bounds.Top,
            excludedWindow || policy.IsAnalysisExcluded(foreground.ProcessName) ? string.Empty : foreground.ApplicationName,
            excludedWindow || policy.IsAnalysisExcluded(foreground.ProcessName)
                || (_settingsSnapshot is not null && !ActivityContextProviderRegistry.IsDetailEnabled(foreground.ProcessName, _settingsSnapshot.Value))
                ? string.Empty : foreground.WindowTitle,
            artifactStem);

    private static long IntersectionArea(NativeMethods.Rect first, NativeMethods.Rect second)
    {
        var left = Math.Max(first.Left, second.Left);
        var top = Math.Max(first.Top, second.Top);
        var right = Math.Min(first.Right, second.Right);
        var bottom = Math.Min(first.Bottom, second.Bottom);
        var width = Math.Max(0, right - left);
        var height = Math.Max(0, bottom - top);
        return (long)width * height;
    }

    private (IReadOnlyList<string> Analysis, IReadOnlyList<string> Stored, int Width, int Height) CaptureRect(
        string directory,
        NativeMethods.Rect rect,
        IntPtr windowHandle,
        string stem,
        string captureId,
        string captureOrigin,
        DateTimeOffset capturedAt,
        Func<ScreenshotCaptureContext, ScreenshotCaptureDecision> authorizeCapture,
        IReadOnlyList<NativeMethods.Rect> visibleRegions,
        ScreenshotCaptureRules policy,
        bool forAnalysis,
        string similarityKey,
        ScreenshotSimilarityOptions similarityOptions,
        bool retainBaseline,
        List<PendingFingerprint> pending,
        out string? matchedPath)
    {
        matchedPath = null;
        var width = rect.Right - rect.Left;
        var height = rect.Bottom - rect.Top;
        var validatedOrigin = ScreenshotCaptureOrigins.Validate(captureOrigin);
        var versionedStem = $"{captureId}_{_appVersion}_{validatedOrigin}_{stem}";
        var screenshotPath = Path.Combine(directory, $"{versionedStem}.webp");
        var stagingPath = ScreenshotPublicationJournal.StagingPath(screenshotPath);

        try
        {
            // Authorization surrounds both acquisition paths and precedes encoding, disk and AI access.
            AuthorizeCurrentForeground(authorizeCapture);
            var before = ReadVisibleWindows();
            AuthorizeCaptureSources(rect, visibleRegions, before, windowHandle, policy, forAnalysis, authorizeCapture);
            using var bitmap = windowHandle != IntPtr.Zero
                ? WindowsGraphicsCaptureService.Capture(windowHandle)
                : CaptureDesktopRegions(rect, visibleRegions);
            width = bitmap.Width;
            height = bitmap.Height;
            var after = ReadVisibleWindows();
            AuthorizeCaptureSources(rect, visibleRegions, after, windowHandle, policy, forAnalysis, authorizeCapture);
            AuthorizeCurrentForeground(authorizeCapture);

            ScreenshotVisualFingerprint? fingerprint = null;
            if (validatedOrigin == ScreenshotCaptureOrigins.Scheduled && similarityOptions.Enabled)
            {
                // Compare only after privacy checks, before encoding, disk writes or provider submission.
                fingerprint = ScreenshotVisualFingerprint.Create(bitmap, similarityOptions);
                matchedPath = _similarity.FindMatch(similarityKey, fingerprint, capturedAt, similarityOptions, File.Exists);
                if (matchedPath is not null)
                    return (Array.Empty<string>(), Array.Empty<string>(), width, height);
            }

            // Staging names are excluded from the gallery until Core commits installation provenance.
            ScreenshotStorageLayout.RejectLinks(stagingPath);
            EncodeBitmapAsWebp(bitmap, stagingPath);
            File.SetLastWriteTimeUtc(stagingPath, capturedAt.UtcDateTime);
            if (fingerprint is not null && retainBaseline)
                pending.Add(new PendingFingerprint(similarityKey, fingerprint, capturedAt, similarityOptions, screenshotPath));
        }
        catch
        {
            DeletePartialArtifacts([screenshotPath]);
            throw;
        }

        return (new[] { screenshotPath }, new[] { screenshotPath }, width, height);
    }

    private static void AuthorizeCaptureSources(NativeMethods.Rect area, IReadOnlyList<NativeMethods.Rect> regions,
        IReadOnlyList<VisibleCaptureWindow> windows, IntPtr windowHandle, ScreenshotCaptureRules policy, bool forAnalysis,
        Func<ScreenshotCaptureContext, ScreenshotCaptureDecision> authorizeCapture)
    {
        if (windowHandle != IntPtr.Zero)
        {
            var source = windows.FirstOrDefault(window => window.WindowHandle == windowHandle)
                ?? throw new ScreenshotSceneChangedException("The selected window is no longer available.");
            EnsureCaptureAllowed(authorizeCapture(source.Context));
            if (source.IsCaptureExcluded || policy.IsCaptureExcluded(source.Context.ProcessName)
                || forAnalysis && policy.IsAnalysisExcluded(source.Context.ProcessName))
                throw new ScreenshotCapturePreconditionException(ScreenshotCaptureDecision.ContentExcluded);
            return;
        }

        AuthorizeVisibleWindows(area, windows, authorizeCapture);
        var current = windows.Select(window => new CaptureWindow(window.WindowHandle, window.Bounds,
            window.Context.ProcessName, window.Context.ApplicationName, window.Context.WindowTitle,
            window.IsApplicationWindow, window.IsCaptureExcluded)).ToArray();
        if (policy.HasExcludedPixels(area, regions, current, forAnalysis))
            throw new ScreenshotCapturePreconditionException(ScreenshotCaptureDecision.ContentExcluded);
    }

    private static Bitmap CaptureDesktopRegions(NativeMethods.Rect rect, IReadOnlyList<NativeMethods.Rect> visibleRegions)
    {
        var bitmap = new Bitmap(rect.Right - rect.Left, rect.Bottom - rect.Top, PixelFormat.Format32bppRgb);
        try
        {
            using var graphics = Graphics.FromImage(bitmap);
            graphics.Clear(Color.Black);
            // Panoramas keep pre-acquisition privacy masks. Excluded auxiliary windows are omitted by Windows.
            foreach (var region in visibleRegions)
                graphics.CopyFromScreen(region.Left, region.Top, region.Left - rect.Left, region.Top - rect.Top,
                    new Size(region.Right - region.Left, region.Bottom - region.Top));
            return bitmap;
        }
        catch
        {
            bitmap.Dispose();
            throw;
        }
    }

    private static void AuthorizeCurrentForeground(
        Func<ScreenshotCaptureContext, ScreenshotCaptureDecision> authorizeCapture)
    {
        ScreenshotCaptureContext context;
        try
        {
            context = CaptureForegroundTarget().ToCaptureContext();
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            // Missing foreground metadata is represented explicitly so configured privacy rules fail closed.
            context = ScreenshotCaptureContext.Unavailable;
        }

        EnsureCaptureAllowed(authorizeCapture(context));
    }

    private static void EnsureCaptureAllowed(ScreenshotCaptureDecision decision)
    {
        if (decision != ScreenshotCaptureDecision.Allowed)
        {
            throw new ScreenshotCapturePreconditionException(decision);
        }
    }

    internal sealed record VisibleCaptureWindow(NativeMethods.Rect Bounds, ScreenshotCaptureContext Context,
        IntPtr WindowHandle = default, bool IsApplicationWindow = true, bool IsCaptureExcluded = false);

    /// <summary>Rejects the entire capture when any potentially visible intersecting window is private.</summary>
    internal static void AuthorizeVisibleWindows(NativeMethods.Rect area,
        IEnumerable<VisibleCaptureWindow> windows,
        Func<ScreenshotCaptureContext, ScreenshotCaptureDecision> authorizeCapture)
    {
        foreach (var window in windows)
        {
            if (!window.IsCaptureExcluded && IntersectionArea(area, window.Bounds) > 0)
            {
                EnsureCaptureAllowed(authorizeCapture(window.Context));
            }
        }
    }

    private static IReadOnlyList<VisibleCaptureWindow> ReadVisibleWindows()
    {
        var windows = new List<VisibleCaptureWindow>();
        Exception? failure = null;
        NativeMethods.WindowEnumProc callback = (window, _) =>
        {
            try
            {
                if (!NativeMethods.IsWindowVisible(window) || NativeMethods.IsIconic(window)) return true;
                if (NativeMethods.DwmGetWindowAttribute(window, 14, out var cloaked, sizeof(int)) != 0)
                    throw new InvalidOperationException("Unable to resolve window visibility for screenshot privacy.");
                if (cloaked != 0) return true;
                if (!WindowsGraphicsCaptureService.TryReadWindowBounds(window, out var bounds))
                    throw new InvalidOperationException("Unable to resolve visible window bounds for screenshot privacy.");
                var process = ReadProcessName(window);
                var title = ReadWindowTitle(window);
                var className = new StringBuilder(256);
                if (NativeMethods.GetClassName(window, className, className.Capacity) == 0)
                    throw new InvalidOperationException("Unable to identify a visible window for screenshot capture.");
                var isApplication = !string.IsNullOrWhiteSpace(title)
                    && className.ToString() is not ("Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd");
                // Raw metadata is transient policy input, not retained focus metadata.
                var context = new ActivityContextProviderRegistry().Resolve(new ForegroundWindowInfo(process, title));
                windows.Add(new VisibleCaptureWindow(bounds, new ScreenshotCaptureContext(process, context.Application, context.Context, title),
                    window, isApplication, AuxiliaryWindowCaptureService.IsExcluded(window)));
                return true;
            }
            catch (Exception exception)
            {
                // Never propagate managed exceptions through the native enumeration callback.
                failure = exception;
                return false;
            }
        };
        if (!NativeMethods.EnumWindows(callback, IntPtr.Zero) || failure is not null)
            throw new InvalidOperationException("Screenshot window enumeration failed; no image will be retained.", failure);
        return windows;
    }

    private static void DeletePartialArtifacts(IEnumerable<string> paths)
    {
        foreach (var path in paths.SelectMany(path => new[] { path, ScreenshotPublicationJournal.StagingPath(path) })
                     .Distinct(StringComparer.OrdinalIgnoreCase).Where(File.Exists))
        {
            try
            {
                File.Delete(path);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // Preserve the original capture failure while cleanup remains best effort.
            }
        }
    }

    /// <summary>Encodes a GDI bitmap directly as WEBP without an intermediate image file or decode.</summary>
    /// <param name="bitmap">Opaque BGRA-compatible source bitmap.</param>
    /// <param name="outputWebp">Destination WEBP path.</param>
    internal static void EncodeBitmapAsWebp(Bitmap bitmap, string outputWebp)
    {
        ArgumentNullException.ThrowIfNull(bitmap);

        var bounds = new Rectangle(0, 0, bitmap.Width, bitmap.Height);
        var bitmapData = bitmap.LockBits(bounds, ImageLockMode.ReadOnly, PixelFormat.Format32bppRgb);
        try
        {
            if (bitmapData.Stride <= 0)
            {
                // Captures created by this service are top-down; another layout cannot be encoded safely in place.
                throw new InvalidOperationException("Screenshot bitmap has an unsupported pixel layout.");
            }

            var imageInfo = new SKImageInfo(
                bitmap.Width,
                bitmap.Height,
                SKColorType.Bgra8888,
                SKAlphaType.Opaque);
            using var image = SKImage.FromPixels(imageInfo, bitmapData.Scan0, bitmapData.Stride)
                ?? throw new InvalidOperationException("Unable to create an in-memory screenshot image.");
            using var data = image.Encode(SKEncodedImageFormat.Webp, WebpQuality)
                ?? throw new InvalidOperationException("Unable to encode screenshot as WEBP.");
            using var output = File.Create(outputWebp);
            data.SaveTo(output);
            output.Flush(flushToDisk: true);
        }
        finally
        {
            bitmap.UnlockBits(bitmapData);
        }
    }

}

/// <summary>
/// Result of a screenshot pass used by AI and local storage separately.
/// </summary>
public sealed record ScreenshotCaptureResult(
    string CaptureId,
    IReadOnlyList<string> AnalysisScreenshotPaths,
    IReadOnlyList<string> StoredScreenshotPaths,
    string CaptureOrigin,
    ScreenshotFocusMetadata? FocusMetadata = null,
    DateTimeOffset? CapturedAt = null,
    DeviceContextSnapshot? DeviceContext = null,
    IReadOnlyList<ScreenshotImageMetadata>? Images = null)
{
    // Live-only provenance never crosses IPC or survives a historical/recovery roundtrip.
    internal ForegroundCaptureTarget? ContinuityForeground { get; init; }
    internal TimesheetUnchangedEvidence? UnchangedEvidence { get; init; }

    /// <summary>
    /// Returns all generated files, used for retention and cleanup.
    /// </summary>
    public IReadOnlyList<string> AllScreenshotPaths
        => AnalysisScreenshotPaths.Concat(StoredScreenshotPaths).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
}

internal sealed record TimesheetUnchangedEvidence(string AttemptId, DateTimeOffset At, string SourcePath,
    string Application, string? WindowTitle);

/// <summary>Describes the screen and foreground app associated with one screenshot pass.</summary>
/// <param name="ScreenName">Stable display label resolved during monitor enumeration.</param>
/// <param name="ScreenIndex">One-based display index resolved during monitor enumeration.</param>
/// <param name="ScreenLeft">Physical left edge of the focused display.</param>
/// <param name="ScreenTop">Physical top edge of the focused display.</param>
/// <param name="ScreenWidth">Physical focused display width.</param>
/// <param name="ScreenHeight">Physical focused display height.</param>
/// <param name="ApplicationName">Foreground application name resolved from process metadata.</param>
/// <param name="WindowTitle">Foreground window titlebar text captured at snapshot time.</param>
/// <param name="ArtifactStem">Artifact stem that should represent the focused target in the capture result.</param>
public sealed record ScreenshotFocusMetadata(
    string ScreenName,
    int ScreenIndex,
    int ScreenLeft,
    int ScreenTop,
    int ScreenWidth,
    int ScreenHeight,
    string ApplicationName,
    string WindowTitle,
    string ArtifactStem);

internal readonly record struct CaptureDisplay(int Index, NativeMethods.Rect Bounds)
{
    internal string Stem => $"monitor-{Index}";

    internal string Name => $"Monitor {Index}";
}

internal sealed record ForegroundCaptureTarget(
    IntPtr WindowHandle,
    NativeMethods.Rect WindowBounds,
    string ProcessName,
    string ApplicationName,
    string Context,
    string WindowTitle)
{
    internal ScreenshotCaptureContext ToCaptureContext() =>
        new(ProcessName, ApplicationName, Context, WindowTitle);
}
