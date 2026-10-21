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


namespace WorkTrail.Application;

/// <summary>Identifies an explicit operation on a durable timesheet batch.</summary>
public enum TimesheetBatchAction { Start = 1, List, Refresh, Cancel, Export, Edit, Split, Merge, Confirm, Evidence, Retry, Timeline }

/// <summary>Chooses the presentation grouping; reconstruction always uses independent local days.</summary>
public enum TimesheetGrouping { Day, Week }

/// <summary>Contains the selected text sources, grouping and optional billing metadata.</summary>
public sealed record TimesheetOptions(ReportSummaryRequest Sources,
    string Consultant = "", string Client = "", decimal? HourlyRate = null, string Currency = "EUR",
    bool SeparateMonths = false, TimesheetGrouping Grouping = TimesheetGrouping.Day);

/// <summary>Requests an operation. Only Start and explicit Retry can submit paid work.</summary>
public sealed record TimesheetBatchCommand(TimesheetBatchAction Action, TimesheetOptions? Options = null,
    Guid? JobId = null, string? DestinationPath = null, bool Overwrite = false, int Page = 0,
    ReportWorkbookTheme? Theme = null, TimesheetEdit? Edit = null, int? ExpectedRevision = null,
    string? SegmentId = null, int SegmentPage = 0, DateOnly? From = null, DateOnly? ToInclusive = null);

/// <summary>Daily request metadata; historical snapshots can retain input metrics that are never used as inferred work.</summary>
public sealed record TimesheetRow(string Id, DateOnly Date, string Part, string Project,
    DateTimeOffset FirstObserved, DateTimeOffset LastObserved, double ActiveSeconds, double IdleSeconds,
    string Applications, string References, int SourceCount, string Description = "", string State = "pending");

/// <summary>Contains safe job metadata without source prompts, credentials or remote file identifiers.</summary>
public sealed record TimesheetJobInfo(Guid Id, DateTimeOffset CreatedAt, DateOnly From, DateOnly ToInclusive,
    string State, int RowCount, int CompletedCount, int FailedCount, bool ResultsSaved, bool CleanupPending);

/// <summary>Returns a bounded page of durable jobs; report rows remain in the Core snapshot.</summary>
public sealed record TimesheetBatchView(IReadOnlyList<TimesheetJobInfo> Jobs, TimesheetJobInfo? Selected,
    string? ExportedPath = null, int Page = 0, bool HasPreviousPage = false, bool HasNextPage = false, string? WarningKey = null,
    IReadOnlyList<TimesheetSegment>? Segments = null, IReadOnlyList<TimesheetTask>? Tasks = null,
    int Revision = 0, bool Confirmed = false, string? TimeZoneId = null,
    IReadOnlyList<TimesheetObservation>? Evidence = null, IReadOnlyList<TimesheetDayStatus>? Days = null,
    bool Historical = false, int SegmentPage = 0, int SegmentCount = 0,
    IReadOnlyList<ScreenshotGalleryItem>? SupportingCaptures = null, TimesheetCoverage? Coverage = null,
    IReadOnlyList<ActivitySpan>? Timeline = null, string? TimelineRevision = null);

/// <summary>A saved, locally reviewed interval clipped to a display day, with explicit draft provenance.</summary>
public sealed record ActivitySpan(Guid JobId, string SegmentId, DateOnly Date, DateTimeOffset Start,
    DateTimeOffset End, string Title, string Project, string Basis, string ReviewState,
    bool Confirmed, DateTimeOffset GeneratedAt, DateTimeOffset DayStart, DateTimeOffset DayEnd);

/// <summary>Whole-draft coverage, independent of the currently displayed segment page.</summary>
public sealed record TimesheetCoverage(double WorkSeconds, double UnallocatedSeconds, double UnavailableSeconds,
    double OverlapSeconds, DateTimeOffset? FirstObservation, DateTimeOffset? LastObservation);

/// <summary>One privacy-approved semantic observation, independent of image count and input activity.</summary>
public sealed record TimesheetObservation(string Id, string InstallationId, DateTimeOffset ObservedAt,
    string CaptureId, string AnalysisId, string SourceKind, string Description, string Application,
    string? WindowTitle, string ProjectHint, int CaptureIntervalSeconds = 300, bool AssumedCadence = false);

/// <summary>Persisted availability signal from the existing tracking runtime.</summary>
public sealed record TimesheetSessionEvent(string Id, string InstallationId, DateTimeOffset At, string Kind);

/// <summary>A model-inferred work objective; IDs are scoped to the saved reconstruction.</summary>
public sealed record TimesheetTask(string Id, string Title, string Project, string Summary,
    IReadOnlyList<string> EvidenceObservationIds);

/// <summary>Maps each submitted observation to a task or an explicit unknown.</summary>
public sealed record TimesheetAssignment(string ObservationId, string? TaskId);

/// <summary>Describes identity continuity, without asking the model to measure time.</summary>
public sealed record TimesheetTransition(string FromObservationId, string ToObservationId, string Relation);

/// <summary>Locally validated structured model output for one local day.</summary>
public sealed record TimesheetInference(IReadOnlyList<TimesheetTask> Tasks,
    IReadOnlyList<TimesheetAssignment> Assignments, IReadOnlyList<TimesheetTransition> Transitions);

/// <summary>A disjoint half-open UTC interval. Work duration is derived only from its endpoints.</summary>
public sealed record TimesheetSegment(string Id, DateTimeOffset Start, DateTimeOffset End, string? TaskId,
    string Project, IReadOnlyList<string> SourceObservationIds, string Basis, string ReviewState = "draft",
    string? UserNote = null, IReadOnlyList<TimesheetSegment>? Alternatives = null,
    IReadOnlyList<DateTimeOffset>? EstimatedBoundaries = null)
{
    /// <summary>Elapsed UTC seconds, without per-observation rounding.</summary>
    public double Seconds => (End - Start).TotalSeconds;
    /// <summary>Whether the interval contributes to work and optional provisional billing.</summary>
    public bool Allocated => TaskId is not null && Basis is not ("unallocated" or "unavailable" or "overlap");
}

/// <summary>An optimistic local correction, split, merge, or overlap attribution.</summary>
public sealed record TimesheetEdit(string? SegmentId, DateTimeOffset Start, DateTimeOffset End,
    string Title = "", string Project = "", string Basis = "manual", string? UserNote = null,
    string? OtherSegmentId = null, string? AlternativeId = null);

/// <summary>Distinguishes missing, pending and failed days from zero estimated work.</summary>
public sealed record TimesheetDayStatus(DateOnly Date, string State, int ObservationCount,
    string? FailureCode = null, bool AssumedCadence = false);
