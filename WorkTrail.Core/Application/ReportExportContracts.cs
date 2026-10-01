// SPDX-License-Identifier: MIT

namespace WorkTrail.Application;

/// <summary>Identifies the supported analytical file formats.</summary>
public enum ReportExportFormat { Excel, Csv, Json }

/// <summary>Chooses whether saved descriptions are exported as excerpts, complete text, or both.</summary>
public enum ReportDescriptionMode { Brief, Complete, Both }

/// <summary>Contains explicit range, field and privacy choices shared by preview and export.</summary>
public sealed record ReportExportOptions(
    DateOnly From,
    DateOnly ToInclusive,
    string TimeZoneId,
    ReportExportFormat Format = ReportExportFormat.Excel,
    string? InstallationId = null,
    bool IncludeDays = true,
    bool IncludeApplications = true,
    bool IncludeCaptures = true,
    bool IncludeDescriptions = true,
    ReportDescriptionMode DescriptionMode = ReportDescriptionMode.Both,
    bool IncludeOcr = false,
    bool IncludeWindowTitles = false,
    bool IncludeDevices = false,
    bool IncludePaths = false,
    bool IncludeTelemetry = false,
    bool IncludeAiUsage = false,
    string CsvSeparator = ";",
    string Language = "en-US");

/// <summary>Contains a bounded table preview; raw complete text never crosses IPC for ordinary previews.</summary>
public sealed record ReportExportTablePreview(string Name, IReadOnlyList<string> Columns, IReadOnlyList<IReadOnlyList<string>> Rows, int RowCount);

/// <summary>Describes the actual local data selected by an export request.</summary>
public sealed record ReportExportPreview(
    string SuggestedFileName,
    string Extension,
    long ActiveSeconds,
    int CaptureCount,
    int DescriptionCount,
    double CoverageRatio,
    IReadOnlyList<ReportExportTablePreview> Tables);

/// <summary>Returns persisted field preferences and the current default date range from the runtime.</summary>
public sealed record ReportExportSetup(ReportExportOptions Options, IReadOnlyList<InstallationProfile> Installations);

/// <summary>Requests an atomic file write; overwrite requires explicit destination selection by the caller.</summary>
public sealed record ReportExportRequest(ReportExportOptions Options, string DestinationPath, string? Summary = null, bool Overwrite = false);

/// <summary>Reports only successfully finalized exports.</summary>
public sealed record ReportExportResult(string Path, long Bytes, int TableCount);

/// <summary>Chooses the grouping for a new text-only AI summary.</summary>
public enum ReportSummaryGrouping { Day, Application, Period }

/// <summary>Explicitly selects the text sources to send to the configured AI provider.</summary>
public sealed record ReportSummaryRequest(
    ReportExportOptions Options,
    ReportSummaryGrouping Grouping = ReportSummaryGrouping.Day,
    bool Detailed = false,
    bool IncludeDescriptionExcerpt = true,
    bool IncludeCompleteDescription = false,
    bool IncludeOcr = false,
    bool IncludeWindowTitles = false);

/// <summary>Selects the dedicated OpenAI model for text-only export summaries.</summary>
public static class ReportSummaryModelPolicy
{
    /// <summary>The lowest-priced GPT-6 model for OpenAI text requests.</summary>
    public const string OpenAiModel = "gpt-6-luna";

    /// <summary>Keeps non-OpenAI providers on their configured model.</summary>
    public static string Resolve(string provider, string configuredModel) =>
        string.Equals(provider, "openai", StringComparison.OrdinalIgnoreCase)
        || string.Equals(provider, "open-ai", StringComparison.OrdinalIgnoreCase)
            ? OpenAiModel : configuredModel;
}

/// <summary>Returns editable generated text without persisting it as historical activity.</summary>
public sealed record ReportSummaryResult(string Text, int SourceCount, string Provider, string Model);
