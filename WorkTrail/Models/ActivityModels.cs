// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using WorkTrail.Services;

namespace WorkTrail;

public sealed record ActivitySample(
    DateTimeOffset Timestamp,
    int DurationSeconds,
    string State,
    string ProcessName,
    string Application,
    string Context,
    string WindowTitle,
    string InstallationId,
    long KeyPresses,
    long MouseClicks,
    IReadOnlyDictionary<string, string>? Attributes = null)
{
    /// <summary>Optional live 16×16 premultiplied BGRA icon; never serialized into activity history.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public byte[]? ApplicationIconPixels { get; init; }
}

/// <summary>Names the privacy-scoped attributes retained with local activity samples.</summary>
public static class ActivityAttributeKeys
{
    /// <summary>Stores the short activity label entered through the taskbar widget.</summary>
    public const string SpanLabel = "span_label";
}

/// <summary>Identifies how a screenshot capture was initiated.</summary>
public static class ScreenshotCaptureOrigins
{
    /// <summary>Capture explicitly requested by the user.</summary>
    public const string Manual = "manual";

    /// <summary>Capture initiated by the configured automatic schedule.</summary>
    public const string Scheduled = "scheduled";

    /// <summary>Validates an untrusted capture-origin value against the current contract.</summary>
    public static string Validate(string? origin) => origin?.Trim().ToLowerInvariant() switch
    {
        Manual => Manual,
        Scheduled => Scheduled,
        _ => throw new ArgumentException("Screenshot capture origin must be 'manual' or 'scheduled'.", nameof(origin))
    };
}

public static class FlyoutPositions
{
    public const string BottomCenter = "bottom-center";
    public const string BottomLeft = "bottom-left";
    public const string BottomRight = "bottom-right";
    public const string TopLeft = "top-left";
    public const string TopRight = "top-right";
}

/// <summary>Defines the supported anchors for the compact taskbar control.</summary>
public static class TaskbarWidgetPositions
{
    /// <summary>Places the control near the left edge of the taskbar.</summary>
    public const string Left = "left";

    /// <summary>Places the control before the notification area.</summary>
    public const string Right = "right";
}

/// <summary>Contains the informational working-time configuration for one day of the week.</summary>
/// <param name="Day">Lowercase English weekday identifier, for example <c>monday</c>.</param>
/// <param name="ActivePeriod">Optional active period in <c>HH:mm-HH:mm</c> format.</param>
/// <param name="BreakPeriods">Optional comma-separated breaks, each in <c>HH:mm-HH:mm</c> format.</param>
public sealed record ActiveHoursDay(
    string Day,
    string ActivePeriod = "",
    string BreakPeriods = "");

public sealed record AppSettings(
    string InstallationId = "",
    string Model = "gpt-5.6",
    bool KeepScreenshots = false,
    bool StartWithWindows = false,
    string ScreenshotDirectory = "",
    string ScreenshotCaptureMode = "all-screens",
    int ScreenshotIntervalMinutes = 15,
    string AiProvider = "openai",
    string AiEndpoint = "https://api.openai.com/v1/responses",
    string AiApiKeyName = "OPENAI_API_KEY",
    string AiOutputDetail = "balanced",
    string AiReasoningEffort = "auto",
    string FlyoutPosition = "bottom-center",
    string UiLanguage = "system",
    string Theme = "system",
    bool OpenAiEnabled = false,
    bool ScreenshotsEnabled = false,
    bool OcrEnabled = false,
    string OcrLanguage = "system",
    string SearchLanguage = "system",
    bool SearchSynonymsEnabled = true,
    bool SearchTypoToleranceEnabled = true,
    bool EnableWordDetailPlugin = true,
    bool EnableExcelDetailPlugin = true,
    bool EnableVsCodeDetailPlugin = true,
    bool EnableBrowserDetailPlugin = true,
    string PrivacyProcessNames = "",
    string PrivacyWindowTitles = "",
    string PrivacyWindowHints = "",
    int DataRetentionDays = 30,
    int ScreenshotRetentionDays = 30,
    int OpenAiDailyLimit = 20,
    decimal OpenAiDailyCostUsd = 0m,
    decimal EstimatedCostPerAnalysisUsd = 0.02m,
    decimal EstimatedCostPerScreenshotUsd = 0.003m,
    bool ShowCostGuardrailInStatus = true,
    bool StartTrackingOnLaunch = false,
    bool TaskbarWidgetVisible = false,
    string TaskbarWidgetPosition = TaskbarWidgetPositions.Left,
    string SpanLabel = "",
    string AiCustomPrompt = "",
    IReadOnlyList<ActiveHoursDay>? ActiveHours = null,
    bool IncludeDeviceLocation = false,
    bool QuickSetupCompleted = false,
    IReadOnlyDictionary<string, WorkTrail.Application.WindowState>? WindowStates = null,
    bool ScreenshotDetailsPaneOpen = false,
    IReadOnlyList<string>? WorldClockCityIds = null,
    bool ShowAiMonthlySpend = false,
    bool WorldClockWeatherEnabled = true,
    int MainWindowOpacityPercent = 100,
    bool MainWindowShowInTaskbar = true,
    int WorldClockWindowOpacityPercent = 100,
    bool WorldClockWindowShowInTaskbar = true,
    IReadOnlyDictionary<string, bool>? WindowOpenStates = null,
    WorkTrail.Application.OcrTextWindowSource? OcrTextWindowSource = null,
    bool AutoHideTitleBar = true,
    bool HardwareSensorsEnabled = true,
    bool HardwareUseAdvancedSensors = false,
    bool HardwareSaveSnapshots = true,
    string HardwareSamplingProfile = "normal",
    bool WorldMapWindowShowInTaskbar = true,
    bool LunarPhaseWindowShowInTaskbar = true,
    bool WindowSnappingEnabled = true,
    IReadOnlyList<WorkTrail.Application.ActivityLabelDefinition>? ActivityLabels = null,
    string AstronomyAgendaCityId = "",
    IReadOnlyList<string>? AstronomyAgendaCountryCodes = null,
    bool AstronomyAgendaShowSaints = true);

public sealed record AiAnalysis(
    DateTimeOffset Timestamp,
    string Application,
    string Context,
    string Summary,
    string InstallationId,
    string? ScreenshotPaths,
    SystemSnapshot? Snapshot = null,
    string? CorrelationId = null,
    string? Origin = null,
    string? InformationalSchedule = null,
    IReadOnlyList<WorkTrail.Application.ScreenshotTextSnapshot>? TextSnapshots = null);

public sealed record ApplicationSummary(string Application, long ActiveSeconds);

public sealed record DailySummary(long ActiveSeconds, long IdleSeconds, long KeyPresses, long MouseClicks, IReadOnlyList<ApplicationSummary> Applications);

/// <summary>Describes the local daily guardrail for visual requests sent to the configured AI provider.</summary>
public sealed record AnalysisCostGate(
    bool Allowed,
    string? Reason,
    decimal EstimatedCostUsd,
    int DailyAnalysisCount,
    decimal ProjectedDailyCostUsd);

/// <summary>Represents privacy-safe hourly activity levels for one complete trailing 24-hour window.</summary>
public sealed record ActivityTrendState(
    DateTimeOffset WindowStartUtc,
    DateTimeOffset WindowEndUtc,
    bool HasCompleteCoverage,
    IReadOnlyList<double> HourlyActivityLevels);

/// <summary>Describes one one-minute contribution to the live activity score.</summary>
public sealed record ActivityScoreMinute(
    DateTimeOffset MinuteUtc,
    int Score,
    long KeyPresses,
    long MouseClicks,
    int ActiveSeconds,
    int? CpuUsagePercent,
    int? GpuUsagePercent);

/// <summary>Summarizes input captured between one pair of scheduled screenshot boundaries.</summary>
public sealed record ActivityScoreInterval(long KeyPresses, long MouseClicks);

/// <summary>Provides the bounded live score series rendered by the compact player.</summary>
public sealed record ActivityScoreState(
    DateTimeOffset WindowStartUtc,
    DateTimeOffset WindowEndUtc,
    int SnapshotIntervalMinutes,
    IReadOnlyList<ActivityScoreMinute> Minutes,
    int CurrentScore,
    ActivityScoreInterval PreviousSnapshotInterval,
    ActivityScoreInterval LatestSnapshotInterval);

public sealed record DashboardState(
    string StatusLabel,
    string CurrentContext,
    long TotalKeyPresses,
    long TotalMouseClicks,
    long ActiveSeconds,
    double Intensity,
    bool IsTracking,
    DateTimeOffset? LastSampleTimestamp,
    DateTimeOffset LocalTime,
    DateTimeOffset UtcTime,
    ActivityTrendState? ActivityTrend = null,
    ActivityScoreState? ActivityScore = null,
    TimeSpan? ScheduledSnapshotRemaining = null,
    PendingManualScreenshotState? PendingManualScreenshot = null,
    bool IsWithinActiveHours = true,
    string SpanLabel = "")
{
    /// <summary>Optional 16×16 premultiplied BGRA icon for the application in CurrentContext.</summary>
    public byte[]? CurrentApplicationIconPixels { get; init; }

    /// <summary>Runtime-owned feature access, shared with subscribed frontends without persisting an entitlement.</summary>
    public WorkTrail.Application.FeatureAccessSnapshot? FeatureAccess { get; init; }

    /// <summary>Completed background measurement of the local WorkTrail data directory.</summary>
    public long? DataDirectorySizeBytes { get; init; }
}

/// <summary>Describes a retained manual screenshot that can be deleted before deferred analysis begins.</summary>
public sealed record PendingManualScreenshotState(string ScreenshotPath, DateTimeOffset ExpiresAt);

public sealed record LastSessionState(
    DateTimeOffset? Timestamp,
    string Application,
    string Context,
    string InstallationId,
    IReadOnlyDictionary<string, string>? Attributes,
    string? ScreenshotPath,
    DateTimeOffset? ScreenshotCapturedAt);

public sealed record AnalysisContextSnapshot(
    string Application,
    string Context,
    string WindowTitle,
    string State,
    IReadOnlyDictionary<string, string>? Attributes,
    SystemSnapshot? Snapshot = null,
    string? InformationalSchedule = null);
