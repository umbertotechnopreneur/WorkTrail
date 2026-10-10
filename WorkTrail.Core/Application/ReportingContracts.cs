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


using System.Text.Json.Serialization;

namespace WorkTrail.Application;

/// <summary>Identifies the report visualization selected by a presentation client.</summary>
public enum ReportView
{
    /// <summary>Shows activity by local calendar date.</summary>
    Calendar,

    /// <summary>Shows activity by local weekday and hour.</summary>
    HourOfWeek,

    /// <summary>Shows a chronological activity trend.</summary>
    Trend,

    /// <summary>Shows aggregate active time by application.</summary>
    Applications
}

/// <summary>Requests an aggregate report for an inclusive local-date range.</summary>
public sealed record ReportQuery(
    DateOnly From,
    DateOnly ToInclusive,
    string TimeZoneId,
    ReportView View = ReportView.Calendar);

/// <summary>Describes the normalized local-date range used to build a report.</summary>
public sealed record ReportRange(
    DateOnly From,
    DateOnly ToInclusive,
    string TimeZoneId,
    int DayCount);

/// <summary>Contains aggregate counters for the complete report range.</summary>
public sealed record ReportTotals(
    long ActiveSeconds,
    long IdleSeconds,
    long TrackedSeconds,
    long KeyPresses,
    long MouseClicks,
    int ActiveDays);

/// <summary>Contains aggregate activity for one local calendar date.</summary>
/// <param name="ActivityScore">Normalized 0-100 daily activity intensity, or <see langword="null"/> when the date has no recorded samples.</param>
/// <param name="Installations">Distinct installation profiles that contributed samples to the date.</param>
public sealed record ReportCalendarCell(
    DateOnly Date,
    long ActiveSeconds,
    long IdleSeconds,
    long TrackedSeconds,
    long KeyPresses,
    long MouseClicks,
    int SampleCount,
    bool HasData,
    int? ActivityScore,
    IReadOnlyList<InstallationProfile>? Installations = null);

/// <summary>Contains mean activity for one weekday-and-hour bucket across observed local dates.</summary>
/// <remarks>The second counters are arithmetic means rounded to the nearest whole second; <see cref="ReportHourCell.ObservationDays"/> is their denominator.
/// Input and sample counters are totals across those dates. ActivityScore uses the total input and unioned observed duration.
/// A query covering one Monday-to-Sunday week therefore returns exact counters for each local date and hour.
/// Repeated daylight-saving hours are combined; skipped hours have no data.</remarks>
/// <param name="Installations">Distinct installation profiles that contributed samples to this weekday-and-hour bucket; empty when it has no data.</param>
public sealed record ReportHourCell(
    int DayOfWeek,
    int Hour,
    long ActiveSeconds,
    long IdleSeconds,
    long TrackedSeconds,
    int ObservationDays,
    bool HasData,
    long KeyPresses,
    long MouseClicks,
    int SampleCount,
    int? ActivityScore,
    [property: JsonRequired] IReadOnlyList<InstallationProfile> Installations);

/// <summary>Contains one chronological daily trend bucket.</summary>
public sealed record ReportTrendBucket(
    DateOnly Start,
    DateOnly EndInclusive,
    long ActiveSeconds,
    long IdleSeconds,
    long TrackedSeconds,
    long KeyPresses,
    long MouseClicks,
    bool HasData);

/// <summary>Contains active time attributed to one aggregate application slice.</summary>
public sealed record ReportApplicationSlice(string Application, long ActiveSeconds);

/// <summary>Describes source coverage without exposing raw activity records.</summary>
public sealed record ReportDataQuality(
    bool HasData,
    DateTimeOffset? FirstSampleAt,
    DateTimeOffset? LastSampleAt,
    int SampleCount,
    long CoveredSeconds,
    long RequestedSeconds,
    double CoverageRatio);

/// <summary>Contains privacy-safe AI usage totals for the selected report range.</summary>
public sealed record AiUsageSummary(
    int RequestCount,
    int SuccessfulRequestCount,
    int FailedRequestCount,
    long InputTokens,
    long OutputTokens,
    long TotalTokens,
    long CachedInputTokens,
    long ReasoningTokens,
    long ThinkingTokens,
    decimal? ActualCostUsd,
    int ActualCostRequestCount,
    decimal? EstimatedCostUsd,
    int EstimatedCostRequestCount,
    DateTimeOffset? EstimatedCostPricingUpdatedAt,
    IReadOnlyList<AiUsageSlice> ByProvider,
    IReadOnlyList<AiUsageSlice> ByOrigin)
{
    /// <summary>Gets an empty AI-usage result for ranges with no AI requests.</summary>
    public static AiUsageSummary Empty { get; } = new(
        0,
        0,
        0,
        0,
        0,
        0,
        0,
        0,
        0,
        null,
        0,
        null,
        0,
        null,
        Array.Empty<AiUsageSlice>(),
        Array.Empty<AiUsageSlice>());
}

/// <summary>Contains a privacy-safe usage subtotal by provider or request origin.</summary>
public sealed record AiUsageSlice(
    string Label,
    int RequestCount,
    long InputTokens,
    long OutputTokens,
    long TotalTokens,
    decimal? ActualCostUsd,
    decimal? EstimatedCostUsd);

/// <summary>Contains the complete privacy-safe aggregate report payload.</summary>
public sealed record ReportSnapshot(
    int ContractVersion,
    ReportRange Range,
    ReportTotals Totals,
    IReadOnlyList<ReportCalendarCell> Calendar,
    IReadOnlyList<ReportHourCell> HourOfWeek,
    IReadOnlyList<ReportTrendBucket> Trend,
    IReadOnlyList<ReportApplicationSlice> Applications,
    ReportDataQuality Quality,
    AiUsageSummary AiUsage);
