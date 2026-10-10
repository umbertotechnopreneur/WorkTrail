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


using System.Globalization;
using WorkTrail.Application;

namespace WorkTrail.Services;

/// <summary>Presents selected export fields as readable supporting sheets.</summary>
internal static class ReportExcelData
{
    // workbook owns layout and full-text preservation.
    // document contains explicitly selected archive fields.
    // archiveNote identifies sources read at export time beside a saved batch snapshot.
    internal static void Add(ReportExcelWorkbook workbook, ExportDocument document, string? archiveNote = null)
    {
        var tables = ReportExportService.Tables(document with { RowLimit = null });
        foreach (var table in tables.Where(table => table.Name != "Captures"))
            workbook.Add(new(table.Name, table.Name, archiveNote ?? workbook.T("DataHelp"),
                table.Columns.Select(column => Column(workbook, column)).ToArray(),
                () => table.Rows().Select(row => row.Select((value, index) => Typed(table.Columns[index], value)).ToArray())));

        var captures = tables.SingleOrDefault(table => table.Name == "Captures");
        if (captures is not null)
        {
            var metadata = captures.Columns.Select((name, index) => (name, index))
                .Where(column => !IsText(column.name)).ToArray();
            var descriptionCount = captures.Columns.Count(name => name.StartsWith("description_", StringComparison.Ordinal));
            var ocrCount = captures.Columns.Count(name => name.StartsWith("ocr_", StringComparison.Ordinal));
            var textSheet = descriptionCount > 0 ? "Saved text" : ocrCount > 0 ? "OCR text" : null;
            var fieldsPerRecord = descriptionCount > 0 ? descriptionCount : ocrCount;
            workbook.Add(new("Captures", workbook.T("Captures"), archiveNote ?? workbook.T("CapturesHelp"),
                metadata.Select(column => Column(workbook, column.name)).ToArray(),
                () => captures.Rows().Select((row, index) => metadata.Select(column => column.name == "record"
                    ? new ExcelReportCell(row[column.index], Location: textSheet is not null && (workbook.RowLimit is null || index * fieldsPerRecord < workbook.RowLimit)
                        ? ReportExcelWorkbook.Location(textSheet, "A" + (8 + index * fieldsPerRecord)) : null)
                    : Typed(column.name, row[column.index])).ToArray())));
            foreach (var kind in new[] { "Saved text", "OCR text" })
            {
                var selected = captures.Columns.Select((name, index) => (name, index))
                    .Where(column => kind == "OCR text" ? column.name.StartsWith("ocr_", StringComparison.Ordinal) : column.name.StartsWith("description_", StringComparison.Ordinal)).ToArray();
                if (selected.Length == 0) continue;
                workbook.Add(new(kind, kind, archiveNote ?? workbook.T("LongTextHelp"),
                    [new("record", 10, ExcelFormat.Number), new("source", 24), new("text", 100)],
                    () => captures.Rows().SelectMany((row, index) => selected.Select(column => new object?[]
                    {
                        new ExcelReportCell(row[0], Location: workbook.RowLimit is null || index < workbook.RowLimit ? ReportExcelWorkbook.Location("Captures", "A" + (8 + index)) : null),
                        column.name, row[column.index]
                    }))));
            }
        }
        workbook.Add(new("Field guide", workbook.T("FieldGuide"), workbook.T("FieldHelp"),
            [new(workbook.T("Sheet"), 24), new(workbook.T("Field"), 30), new(workbook.T("Display"), 44)],
            () => tables.SelectMany(table => table.Columns.Select(column => new object?[] { table.Name, column, Column(workbook, column).Header }))));
    }

    // name identifies a complete text field, separated from the compact capture metadata.
    private static bool IsText(string name) => name.StartsWith("description_", StringComparison.Ordinal) || name.StartsWith("ocr_", StringComparison.Ordinal);

    // workbook supplies localized labels.
    // name identifies an exact source-contract column; technical fields retain their own identifiers.
    private static ExcelReportColumn Column(ReportExcelWorkbook workbook, string name)
    {
        var (label, width, format) = name switch
        {
            "active_seconds" => (workbook.T("Active"), 17d, ExcelFormat.Duration),
            "idle_seconds" => (workbook.T("Idle"), 17d, ExcelFormat.Duration),
            "tracked_seconds" => (workbook.T("Tracked"), 17d, ExcelFormat.Duration),
            "date" or "from" or "to_inclusive" => (name.Replace('_', ' '), 23d, ExcelFormat.Date),
            "captured_at" => (workbook.T("CapturedAt"), 26d, ExcelFormat.DateTime),
            "coverage_ratio" => (workbook.T("Coverage"), 18d, ExcelFormat.Percent),
            "cpu_percent" or "gpu_percent" => (name.StartsWith("cpu", StringComparison.Ordinal) ? "CPU (%)" : "GPU (%)", 14d, ExcelFormat.Percent),
            "actual_cost_usd" or "estimated_cost_usd" => (name.Replace('_', ' '), 23d, ExcelFormat.Money),
            "application" => (workbook.T("Apps"), 36d, ExcelFormat.Text),
            "text" => (workbook.T("Text"), 100d, ExcelFormat.Text),
            "window_title" or "screenshot_path" => (name.Replace('_', ' '), 60d, ExcelFormat.Text),
            "labels" => (workbook.T("Projects"), 32d, ExcelFormat.Text),
            "key_presses" or "mouse_clicks" or "sample_count" or "requests" or "record" or "input_tokens" or "output_tokens" => (name.Replace('_', ' '), 17d, ExcelFormat.Number),
            _ => (name.Replace('_', ' '), 25d, ExcelFormat.Text)
        };
        return new(label, width, format);
    }

    // name supplies the declared measurement type.
    // value remains numeric or inert text; unavailable readings remain blank.
    private static object? Typed(string name, object? value)
    {
        if (value is null) return null;
        if (name.EndsWith("_seconds", StringComparison.Ordinal)) return Convert.ToDouble(value, CultureInfo.InvariantCulture) / 86400d;
        if (name is "cpu_percent" or "gpu_percent") return Convert.ToDouble(value, CultureInfo.InvariantCulture) / 100d;
        if (name is "date" or "from" or "to_inclusive") return DateOnly.ParseExact((string)value, "yyyy-MM-dd", CultureInfo.InvariantCulture);
        if (name == "captured_at") return DateTimeOffset.ParseExact((string)value, "O", CultureInfo.InvariantCulture);
        return value;
    }
}
