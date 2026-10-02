// SPDX-License-Identifier: MIT

namespace WorkTrail.Application;

/// <summary>Identifies an explicit operation on a durable timesheet batch.</summary>
public enum TimesheetBatchAction { Start = 1, List, Refresh, Cancel, Export }

/// <summary>Contains the selected text sources, grouping and optional billing metadata.</summary>
public sealed record TimesheetOptions(ReportSummaryRequest Sources, bool MergeDayParts = true,
    string Consultant = "", string Client = "", decimal? HourlyRate = null, string Currency = "EUR",
    bool SeparateMonths = false);

/// <summary>Requests a batch operation. Only Start sends source text for paid processing.</summary>
public sealed record TimesheetBatchCommand(TimesheetBatchAction Action, TimesheetOptions? Options = null,
    Guid? JobId = null, string? DestinationPath = null, bool Overwrite = false, int Page = 0);

/// <summary>Contains measured time and a separately generated description for one day/project/fascia.</summary>
public sealed record TimesheetRow(string Id, DateOnly Date, string Part, string Project,
    DateTimeOffset FirstObserved, DateTimeOffset LastObserved, double ActiveSeconds, double IdleSeconds,
    string Applications, string References, int SourceCount, string Description = "", string State = "pending");

/// <summary>Contains safe job metadata without source prompts, credentials or remote file identifiers.</summary>
public sealed record TimesheetJobInfo(Guid Id, DateTimeOffset CreatedAt, DateOnly From, DateOnly ToInclusive,
    string State, int RowCount, int CompletedCount, int FailedCount, bool ResultsSaved, bool CleanupPending);

/// <summary>Returns a bounded page of durable jobs; report rows remain in the Core snapshot.</summary>
public sealed record TimesheetBatchView(IReadOnlyList<TimesheetJobInfo> Jobs, TimesheetJobInfo? Selected,
    string? ExportedPath = null, int Page = 0, bool HasPreviousPage = false, bool HasNextPage = false, string? WarningKey = null);
