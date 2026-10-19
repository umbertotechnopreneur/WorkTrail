// SPDX-License-Identifier: MIT
namespace WorkTrail.Services;

/// <summary>Bounds AI-supported passive observation without changing measured input or recorded active time.</summary>
internal sealed class ScreenshotActivityContinuityPolicy
{
    private readonly object _gate = new();
    private DateTimeOffset _latestAssessmentAt = DateTimeOffset.MinValue;
    private ForegroundCaptureTarget? _foreground;
    private DateTimeOffset _expiresAt;

    internal void Observe(ScreenshotCaptureResult capture, ActivityContinuityAssessment assessment)
    {
        if (capture.CapturedAt is not { } capturedAt || capture.ContinuityForeground is not { } foreground) return;
        lock (_gate)
        {
            // An older in-flight response cannot revive an activity after a newer ended/uncertain result.
            if (capturedAt < _latestAssessmentAt) return;
            _latestAssessmentAt = capturedAt;
            _foreground = null;
            if (!assessment.IsValid || assessment.ContinuesConfidence < 0.8 || assessment.EndedConfidence > 0.2
                || string.IsNullOrWhiteSpace(foreground.ProcessName) || string.IsNullOrWhiteSpace(foreground.WindowTitle)
                || capture.Images?.Any(image => image.Role == "active-window" && image.ForAnalysis
                    && capture.AnalysisScreenshotPaths.Contains(image.Path, StringComparer.OrdinalIgnoreCase)) != true)
                return;
            _foreground = foreground;
            // Anchor to capture time, so slow provider calls cannot grant five fresh minutes retrospectively.
            _expiresAt = capturedAt.AddMinutes(5);
        }
    }

    internal bool Allows(IntPtr window, string processName, string title, uint idleMilliseconds, DateTimeOffset now)
    {
        lock (_gate)
        {
            // Repeated similar screenshots never extend an input-free episode past ten minutes.
            return idleMilliseconds < 600_000 && now >= _latestAssessmentAt && now < _expiresAt && _foreground is { } foreground
                && foreground.WindowHandle == window
                && string.Equals(foreground.ProcessName, processName, StringComparison.OrdinalIgnoreCase)
                && string.Equals(foreground.WindowTitle, title, StringComparison.Ordinal);
        }
    }
}

/// <summary>Accepts only successfully persisted, live screenshot assessments for the existing capture service.</summary>
internal interface IScreenshotActivityContinuityObserver
{
    void ObserveActivityContinuity(ScreenshotCaptureResult capture, ActivityContinuityAssessment assessment);
}
