// SPDX-License-Identifier: MIT

using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using WorkTrail.Application;

namespace WorkTrail.Services;

/// <summary>Builds analytical projections from durable history without changing source data.</summary>
internal sealed partial class ReportExportService(LocalStore store)
{
    internal const int MaximumCaptures = 50_000;
    internal const int MaximumTextCharacters = 32_000_000;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private string PreferencesPath => Path.Combine(store.DataDirectory, "report-export-preferences.json");

    internal ReportExportSetup Setup()
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        var options = new ReportExportOptions(today.AddDays(-6), today, TimeZoneInfo.Local.Id, Language: store.LoadSettings().UiLanguage);
        if (File.Exists(PreferencesPath))
        {
            // Corrupt or unsupported preferences are surfaced instead of silently discarding user choices.
            var saved = JsonSerializer.Deserialize<ExportPreferences>(File.ReadAllText(PreferencesPath), Json)
                ?? throw new InvalidDataException("Export preferences are empty.");
            if (saved.Version != 1 || saved.Options is null) throw new InvalidDataException("Unsupported export preferences.");
            options = saved.Options with { From = today.AddDays(-6), ToInclusive = today, TimeZoneId = TimeZoneInfo.Local.Id, Language = options.Language };
        }
        Validate(options);
        return new(options, store.GetInstallationProfiles());
    }

    internal void SavePreferences(ReportExportOptions options, CancellationToken cancellationToken)
    {
        Validate(options);
        // Only field choices are retained; dates, installation filters, paths and generated text are not preferences.
        var preferences = new ExportPreferences(1, options with { From = default, ToInclusive = default, InstallationId = null });
        ReportExportWriter.AtomicWrite(PreferencesPath, true, stream => JsonSerializer.Serialize(stream, preferences, Json), cancellationToken);
    }

    internal void Validate(ReportExportOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (!Enum.IsDefined(options.Format) || !Enum.IsDefined(options.DescriptionMode)
            || options.CsvSeparator is not ("," or ";") || string.IsNullOrWhiteSpace(options.Language)
            || options.From > options.ToInclusive || options.ToInclusive == DateOnly.MaxValue
            || options.ToInclusive.DayNumber - options.From.DayNumber >= ReportAggregationService.MaximumRangeDays)
            throw new ArgumentException("Invalid export options.");
        _ = TimeZoneInfo.FindSystemTimeZoneById(options.TimeZoneId);
        if (options.Language != "system" && !LocalizationService.SupportedLanguages.Contains(options.Language, StringComparer.OrdinalIgnoreCase))
            throw new ArgumentException("Unsupported export language.");
        _ = new LocalizationService(options.Language);
        if (options.InstallationId is not null && !store.GetInstallationProfiles().Any(profile => profile.InstallationId == options.InstallationId))
            throw new ArgumentException("Unknown installation.");
        // AI usage lacks an installation dimension in the report contract. Never label global costs as device-local.
        if (options.InstallationId is not null && options.IncludeAiUsage)
            throw new ReportExportValidationException("Export.AiUsageAllDevices");
    }

    internal ExportDocument Build(ReportExportOptions options, string? summary, CancellationToken cancellationToken)
    {
        Validate(options);
        if (summary?.Length > 60_000) throw new ReportExportValidationException("Export.TextTooLarge");
        var result = new ReportAggregationService(store).Build(
            new(options.From, options.ToInclusive, options.TimeZoneId), int.MaxValue, cancellationToken, options.InstallationId);
        if (!result.Succeeded || result.Value is null) throw new ReportExportValidationException(result.MessageKey);
        var captures = options.IncludeCaptures ? ReadCaptures(options, cancellationToken) : Array.Empty<ScreenshotGalleryItem>();
        return new(options, result.Value, captures, summary);
    }

    internal IReadOnlyList<ScreenshotGalleryItem> ReadCaptures(ReportExportOptions options, CancellationToken cancellationToken)
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById(options.TimeZoneId);
        var selected = new List<ScreenshotGalleryItem>();
        long characters = 0;
        // Include adjacent storage dates for the largest possible difference between source and selected time zones.
        // Only those galleries load OCR/descriptions; timestamps below provide the exact requested boundaries.
        store.VisitAllScreenshotGalleryItems(item =>
        {
            var date = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(item.CapturedAt, zone).DateTime);
            if (date < options.From || date > options.ToInclusive
                || (options.InstallationId is not null && item.Installation?.InstallationId != options.InstallationId)) return;
            characters += (item.AiDescriptionMarkdown?.Length ?? 0) + (item.TextSnapshot?.Ocr.RawText.Length ?? 0)
                + (item.TextSnapshot?.AiRefinement?.CorrectedText.Length ?? 0);
            if (selected.Count >= MaximumCaptures || characters > MaximumTextCharacters)
                throw new ReportExportValidationException("Export.RangeTooLarge");
            selected.Add(item);
        }, cancellationToken,
            DateOnly.FromDayNumber(Math.Max(DateOnly.MinValue.DayNumber, options.From.DayNumber - 2)),
            DateOnly.FromDayNumber(Math.Min(DateOnly.MaxValue.DayNumber, options.ToInclusive.DayNumber + 2)));
        return selected.OrderBy(item => item.CapturedAt).ThenBy(item => item.Path, StringComparer.Ordinal).ToArray();
    }

    internal static ReportExportPreview Preview(ExportDocument document, CancellationToken cancellationToken)
    {
        var tables = Tables(document).Select(table =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var rows = table.Rows().Take(5).Select(row => (IReadOnlyList<string>)row.Select(value =>
            {
                var text = Convert.ToString(value, CultureInfo.InvariantCulture) ?? "";
                return Excerpt(text, 240);
            }).ToArray()).ToArray();
            return new ReportExportTablePreview(table.Name, table.Columns, rows, table.RowCount);
        }).ToArray();
        return new(ReportExportFileNames.SuggestedFileName(document.Options),
            ReportExportFileNames.Extension(document.Options.Format), document.Report.Totals.ActiveSeconds, document.Captures.Count,
            document.Captures.Count(item => !string.IsNullOrWhiteSpace(item.AiDescriptionMarkdown)), document.Report.Quality.CoverageRatio, tables);
    }

    internal static IReadOnlyList<ExportTable> Tables(ExportDocument document)
    {
        var report = document.Report;
        var options = document.Options;
        var tables = new List<ExportTable>
        {
            new("Summary", ["from", "to_inclusive", "time_zone", "active_seconds", "idle_seconds", "tracked_seconds", "key_presses", "mouse_clicks", "coverage_ratio"],
                () => [new object?[] { options.From.ToString("yyyy-MM-dd"), options.ToInclusive.ToString("yyyy-MM-dd"), options.TimeZoneId,
                    report.Totals.ActiveSeconds, report.Totals.IdleSeconds, report.Totals.TrackedSeconds, report.Totals.KeyPresses, report.Totals.MouseClicks, report.Quality.CoverageRatio }], 1)
        };
        if (options.IncludeDays)
            tables.Add(new("Days", ["date", "has_data", "active_seconds", "idle_seconds", "tracked_seconds", "key_presses", "mouse_clicks", "sample_count"],
                () => report.Calendar.Select(day => new object?[] { day.Date.ToString("yyyy-MM-dd"), day.HasData, day.ActiveSeconds, day.IdleSeconds, day.TrackedSeconds, day.KeyPresses, day.MouseClicks, day.SampleCount }), report.Calendar.Count));
        if (options.IncludeApplications)
            tables.Add(new("Applications", ["application", "active_seconds"], () => report.Applications.Select(app => new object?[] { app.Application, app.ActiveSeconds }), report.Applications.Count));
        if (options.IncludeCaptures)
        {
            var columns = new List<string> { "record", "captured_at", "application", "capture_kind", "labels" };
            if (options.IncludeDescriptions && options.DescriptionMode != ReportDescriptionMode.Complete) columns.Add("description_excerpt");
            if (options.IncludeDescriptions && options.DescriptionMode != ReportDescriptionMode.Brief) columns.Add("description_markdown");
            if (options.IncludeOcr) columns.AddRange(["ocr_raw", "ocr_corrected", "ocr_overview", "ocr_key_points", "ocr_entities", "ocr_actions"]);
            if (options.IncludeWindowTitles) columns.Add("window_title");
            if (options.IncludeDevices) columns.AddRange(["installation_id", "device_name"]);
            if (options.IncludePaths) columns.Add("screenshot_path");
            if (options.IncludeTelemetry) columns.AddRange(["activity_index", "mouse_clicks", "cpu_percent", "gpu_percent"]);
            tables.Add(new("Captures", columns, () => CaptureRows(document), document.Captures.Count));
        }
        if (options.IncludeAiUsage)
            tables.Add(new("AI usage", ["provider", "requests", "input_tokens", "output_tokens", "actual_cost_usd", "estimated_cost_usd"],
                () => report.AiUsage.ByProvider.Select(usage => new object?[] { usage.Label, usage.RequestCount, usage.InputTokens, usage.OutputTokens, usage.ActualCostUsd, usage.EstimatedCostUsd }), report.AiUsage.ByProvider.Count));
        if (!string.IsNullOrWhiteSpace(document.Summary))
            tables.Add(new("AI summary", ["text"], () => [new object?[] { document.Summary }], 1));
        return document.RowLimit is { } limit
            ? tables.Select(table => table with { Rows = () => table.Rows().Take(limit), RowCount = Math.Min(table.RowCount, limit) }).ToArray()
            : tables;
    }

    private static IEnumerable<object?[]> CaptureRows(ExportDocument document)
    {
        var options = document.Options;
        var zone = TimeZoneInfo.FindSystemTimeZoneById(options.TimeZoneId);
        var ordinal = 0;
        foreach (var item in document.Captures)
        {
            var row = new List<object?> { ++ordinal, TimeZoneInfo.ConvertTime(item.CapturedAt, zone).ToString("O"), item.ForegroundApplication,
                item.CaptureKind, string.Join("; ", (item.SpanLabels ?? []).Select(label => label.Label)) };
            if (options.IncludeDescriptions && options.DescriptionMode != ReportDescriptionMode.Complete) row.Add(Excerpt(PlainText(item.AiDescriptionMarkdown), 240));
            if (options.IncludeDescriptions && options.DescriptionMode != ReportDescriptionMode.Brief) row.Add(item.AiDescriptionMarkdown);
            if (options.IncludeOcr)
            {
                var refinement = item.TextSnapshot?.AiRefinement;
                row.AddRange([item.TextSnapshot?.Ocr.RawText, refinement?.CorrectedText, refinement?.Summary.Overview,
                    refinement is null ? null : string.Join("\n", refinement.Summary.KeyPoints),
                    refinement is null ? null : string.Join("\n", refinement.Summary.Entities),
                    refinement is null ? null : string.Join("\n", refinement.Summary.Actions)]);
            }
            if (options.IncludeWindowTitles) row.Add(item.ForegroundWindowTitle);
            if (options.IncludeDevices) row.AddRange([item.Installation?.InstallationId, item.Installation?.FriendlyName]);
            if (options.IncludePaths) row.Add(item.Path);
            if (options.IncludeTelemetry) row.AddRange([item.ActivityIndex, item.MouseClicks, item.CpuUsagePercent, item.GpuUsagePercent]);
            yield return row.ToArray();
        }
    }

    internal static string PlainText(string? text) => string.IsNullOrWhiteSpace(text) ? "" :
        Whitespace().Replace(Markdown().Replace(text, "$1"), " ").Trim();

    internal static string Excerpt(string text, int length)
    {
        if (text.Length <= length) return text;
        var end = char.IsHighSurrogate(text[length - 1]) ? length - 1 : length;
        return text[..end].TrimEnd() + "…";
    }

    [GeneratedRegex(@"!?\[([^\]]+)\]\([^)]*\)|<[^>]*>|[*`#_]", RegexOptions.CultureInvariant)]
    private static partial Regex Markdown();
    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex Whitespace();
    private sealed record ExportPreferences(int Version, ReportExportOptions Options);
}

internal sealed record ExportDocument(ReportExportOptions Options, ReportSnapshot Report, IReadOnlyList<ScreenshotGalleryItem> Captures, string? Summary, int? RowLimit = null);
internal sealed record ExportTable(string Name, IReadOnlyList<string> Columns, Func<IEnumerable<object?[]>> Rows, int RowCount);
internal sealed class ReportExportValidationException(string messageKey, int? actualLength = null, int? limit = null) : ArgumentException(messageKey)
{
    internal string MessageKey { get; } = messageKey;
    internal int? ActualLength { get; } = actualLength;
    internal int? Limit { get; } = limit;
}
