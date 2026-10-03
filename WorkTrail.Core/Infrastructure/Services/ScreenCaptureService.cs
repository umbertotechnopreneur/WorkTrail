// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
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

/// <summary>Classifies the application-owned policy decision made immediately before screenshot pixels are read.</summary>
public enum ScreenshotCaptureDecision
{
    /// <summary>The current foreground context may be captured.</summary>
    Allowed,

    /// <summary>Screenshot capture was disabled while the operation was waiting to run.</summary>
    ScreenshotsDisabled,

    /// <summary>The current foreground context is private or cannot be proven safe.</summary>
    PrivacyBlocked
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

/// <summary>
/// Captures desktop images to WEBP for local history and AI context.
/// </summary>
public sealed class ScreenCaptureService : IScreenCaptureService
{
    private const int WebpQuality = 70;
    private static readonly Regex OwnedArtifactName = new(
        "^[0-9a-f]{32}_[0-9]+\\.[0-9]+\\.[0-9]+_(?:manual|scheduled)_(?:monitor-[1-9][0-9]*|active-window)(?:-raw)?\\.(?:webp|png)$",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase | RegexOptions.NonBacktracking);
    private readonly string _appVersion;
    private readonly SettingsSnapshot? _settingsSnapshot;

    /// <summary>
    /// Creates capture service.
    /// </summary>
    /// <param name="appVersion">Optional version suffix used in file naming.</param>
    public ScreenCaptureService(string? appVersion = null, SettingsSnapshot? settingsSnapshot = null)
    {
        _appVersion = string.IsNullOrWhiteSpace(appVersion) ? "0.0.0" : appVersion;
        _settingsSnapshot = settingsSnapshot;
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
        var capturedAt = DateTimeOffset.Now;
        if (captureMode is not "active-window" and not "all-screens")
        {
            throw new ArgumentException("Screenshot capture mode must be 'all-screens' or 'active-window'.", nameof(captureMode));
        }

        var foreground = CaptureForegroundTarget();
        EnsureCaptureAllowed(authorizeCapture(foreground.ToCaptureContext()));
        return captureMode switch
        {
            "active-window" => CaptureActiveWindow(directory, captureId, validatedOrigin, capturedAt, foreground, authorizeCapture),
            _ => CaptureAllScreens(directory, captureId, validatedOrigin, capturedAt, foreground, authorizeCapture)
        };
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

    private ScreenshotCaptureResult CaptureAllScreens(
        string directory,
        string captureId,
        string captureOrigin,
        DateTimeOffset capturedAt,
        ForegroundCaptureTarget foreground,
        Func<ScreenshotCaptureContext, ScreenshotCaptureDecision> authorizeCapture)
    {
        var validatedOrigin = ScreenshotCaptureOrigins.Validate(captureOrigin);
        var captureDirectory = ScreenshotStorageLayout.GetDayDirectory(directory, capturedAt);
        ScreenshotStorageLayout.RejectLinks(captureDirectory);
        // All artifacts from this capture pass share one resolved day directory; directory creation failures abort capture.
        Directory.CreateDirectory(captureDirectory);
        var displays = EnumerateDisplays();
        var focusedDisplay = ResolveFocusedDisplay(displays, foreground.WindowBounds);
        var focusMetadata = CreateFocusMetadata(focusedDisplay, foreground, focusedDisplay.Stem);
        var captureOrder = displays
            .Where(display => display.Index != focusedDisplay.Index)
            .Append(focusedDisplay);

        var storage = new List<string>(displays.Count);
        var analysis = new List<string>(displays.Count);
        try
        {
            foreach (var display in captureOrder)
            {
                var paths = CaptureRect(
                    captureDirectory,
                    display.Bounds,
                    display.Stem,
                    captureId,
                    validatedOrigin,
                    capturedAt,
                    authorizeCapture);

                if (display.Index == focusedDisplay.Index)
                {
                    analysis.InsertRange(0, paths.Analysis);
                    storage.InsertRange(0, paths.Stored);
                    continue;
                }

                analysis.AddRange(paths.Analysis);
                storage.AddRange(paths.Stored);
            }
        }
        catch
        {
            DeletePartialArtifacts(analysis.Concat(storage));
            throw;
        }

        return new ScreenshotCaptureResult(
            captureId,
            analysis.AsReadOnly(),
            storage.AsReadOnly(),
            validatedOrigin,
            FocusMetadata: focusMetadata,
            CapturedAt: capturedAt);
    }

    private ScreenshotCaptureResult CaptureActiveWindow(
        string directory,
        string captureId,
        string captureOrigin,
        DateTimeOffset capturedAt,
        ForegroundCaptureTarget foreground,
        Func<ScreenshotCaptureContext, ScreenshotCaptureDecision> authorizeCapture)
    {
        var validatedOrigin = ScreenshotCaptureOrigins.Validate(captureOrigin);
        var captureDirectory = ScreenshotStorageLayout.GetDayDirectory(directory, capturedAt);
        // The foreground capture uses the same durable calendar layout as multi-monitor capture.
        ScreenshotStorageLayout.RejectLinks(captureDirectory);
        Directory.CreateDirectory(captureDirectory);
        var focusedDisplay = ResolveFocusedDisplay(EnumerateDisplays(), foreground.WindowBounds);
        var focusMetadata = CreateFocusMetadata(focusedDisplay, foreground, "active-window");
        var paths = CaptureRect(
            captureDirectory,
            foreground.WindowBounds,
            "active-window",
            captureId,
            validatedOrigin,
            capturedAt,
            authorizeCapture);
        return new ScreenshotCaptureResult(
            captureId,
            paths.Analysis,
            paths.Stored,
            validatedOrigin,
            FocusMetadata: focusMetadata,
            CapturedAt: capturedAt);
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
            throw new InvalidOperationException("No active foreground window found.");
        }

        if (!NativeMethods.GetWindowRect(window, out var bounds))
        {
            throw new InvalidOperationException("Unable to read active window bounds.");
        }

        var width = bounds.Right - bounds.Left;
        var height = bounds.Bottom - bounds.Top;
        if (width <= 0 || height <= 0)
        {
            throw new InvalidOperationException("Invalid active window size.");
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

    private ScreenshotFocusMetadata CreateFocusMetadata(
        CaptureDisplay display,
        ForegroundCaptureTarget foreground,
        string artifactStem)
        => new(
            display.Name,
            display.Index,
            display.Bounds.Left,
            display.Bounds.Top,
            display.Bounds.Right - display.Bounds.Left,
            display.Bounds.Bottom - display.Bounds.Top,
            foreground.ApplicationName,
            _settingsSnapshot is not null && !ActivityContextProviderRegistry.IsDetailEnabled(foreground.ProcessName, _settingsSnapshot.Value)
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

    private (IReadOnlyList<string> Analysis, IReadOnlyList<string> Stored) CaptureRect(
        string directory,
        NativeMethods.Rect rect,
        string stem,
        string captureId,
        string captureOrigin,
        DateTimeOffset capturedAt,
        Func<ScreenshotCaptureContext, ScreenshotCaptureDecision> authorizeCapture)
    {
        var width = rect.Right - rect.Left;
        var height = rect.Bottom - rect.Top;
        var validatedOrigin = ScreenshotCaptureOrigins.Validate(captureOrigin);
        var versionedStem = $"{captureId}_{_appVersion}_{validatedOrigin}_{stem}";
        var screenshotPath = Path.Combine(directory, $"{versionedStem}.webp");
        var stagingPath = ScreenshotPublicationJournal.StagingPath(screenshotPath);

        try
        {
            using var bitmap = new Bitmap(width, height, PixelFormat.Format32bppRgb);
            using (var graphics = Graphics.FromImage(bitmap))
            {
                // Check every visible intersecting window, including other monitors. Unknown metadata
                // fails closed under configured rules. Recheck after acquisition before any file is written.
                AuthorizeCurrentForeground(authorizeCapture);
                AuthorizeVisibleWindows(rect, ReadVisibleWindows(), authorizeCapture);
                graphics.CopyFromScreen(rect.Left, rect.Top, 0, 0, new Size(width, height));
                AuthorizeVisibleWindows(rect, ReadVisibleWindows(), authorizeCapture);
                AuthorizeCurrentForeground(authorizeCapture);
            }

            // Staging names are excluded from the gallery until Core commits installation provenance.
            ScreenshotStorageLayout.RejectLinks(stagingPath);
            EncodeBitmapAsWebp(bitmap, stagingPath);
            File.SetLastWriteTimeUtc(stagingPath, capturedAt.UtcDateTime);
        }
        catch
        {
            DeletePartialArtifacts([screenshotPath]);
            throw;
        }

        return (new[] { screenshotPath }, new[] { screenshotPath });
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

    internal sealed record VisibleCaptureWindow(NativeMethods.Rect Bounds, ScreenshotCaptureContext Context);

    /// <summary>Rejects the entire capture when any potentially visible intersecting window is private.</summary>
    internal static void AuthorizeVisibleWindows(NativeMethods.Rect area,
        IEnumerable<VisibleCaptureWindow> windows,
        Func<ScreenshotCaptureContext, ScreenshotCaptureDecision> authorizeCapture)
    {
        foreach (var window in windows)
        {
            if (IntersectionArea(area, window.Bounds) > 0)
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
                if (!NativeMethods.GetWindowRect(window, out var bounds))
                    throw new InvalidOperationException("Unable to resolve visible window bounds for screenshot privacy.");
                var process = ReadProcessName(window);
                var title = ReadWindowTitle(window);
                // Raw metadata is transient policy input, not retained focus metadata.
                var context = new ActivityContextProviderRegistry().Resolve(new ForegroundWindowInfo(process, title));
                windows.Add(new VisibleCaptureWindow(bounds, new ScreenshotCaptureContext(process, context.Application, context.Context, title)));
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
    IReadOnlyList<WorkTrail.Application.ScreenshotTextSnapshot>? TextSnapshots = null,
    ScreenshotFocusMetadata? FocusMetadata = null,
    DateTimeOffset? CapturedAt = null,
    SystemSnapshot? HardwareSnapshot = null)
{
    /// <summary>
    /// Returns all generated files, used for retention and cleanup.
    /// </summary>
    public IReadOnlyList<string> AllScreenshotPaths
        => AnalysisScreenshotPaths.Concat(StoredScreenshotPaths).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
}

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
