// SPDX-License-Identifier: MIT

using System.Globalization;
using WorkTrail.Application;

namespace WorkTrail.Services;

/// <summary>Builds a client report with measured detail and explicitly dated archive sources.</summary>
internal static class TimesheetExcelWriter
{
    // job contains saved measured time and batch descriptions.
    // destination is the absolute XLSX path selected by the user.
    // overwrite records permission to replace that destination.
    // token cancels before atomic publication.
    // rowLimit bounds the preview independently on every data sheet.
    // archive contains optional sources still retained at export, never persisted in the job.
    internal static string Write(TimesheetJob job, string destination, bool overwrite, CancellationToken token,
        int? rowLimit = null, ExportDocument? archive = null)
    {
        if (!Path.IsPathFullyQualified(destination) || !string.Equals(Path.GetExtension(destination), ".xlsx", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("A fully qualified XLSX destination is required.");
        var path = Path.GetFullPath(destination);
        var strings = new LocalizationService(job.Options.Sources.Options.Language);
        var options = job.Options;
        string T(string key) => strings.Translate("Timesheet." + key);
        var workbook = new ReportExcelWorkbook(strings, options.Sources.Options, rowLimit);
        var detailName = T("DetailsSheet");
        var sourceName = workbook.T("SourceGuide");
        var rows = job.Rows.Select(work => work.Row).ToArray();
        var details = job.Rows.SelectMany(work => work.Observations.Select(observation => (Parent: work.Row, Observation: observation))).ToArray();
        var detailIndexes = details.Select((item, index) => (item.Parent.Id, Index: index)).ToLookup(item => item.Id, item => item.Index);
        var hasParts = options.Grouping == TimesheetGrouping.Day && !options.MergeDayParts;
        var rateReference = ReportExcelWorkbook.Location(sourceName, "$B$8");
        var rateAvailable = $"AND(ISNUMBER({rateReference}),{rateReference}>=0,{rateReference}<=1000000)";
        var metadata = T("Consultant") + ": " + options.Consultant + "  ·  " + T("Client") + ": " + options.Client;
        workbook.Metadata = metadata;
        if (job.Rows.Any(row => row.Observations.Count == 0)) throw new InvalidDataException("Missing measured timesheet detail.");
        var columns = new List<ExcelReportColumn> { new(options.Grouping == TimesheetGrouping.Week ? workbook.T("WeekOf") : T("Date"), 21, ExcelFormat.Date) };
        if (hasParts) columns.Add(new(T("Part"), 16));
        columns.AddRange([new(T("Active"), 12, ExcelFormat.Duration), new(workbook.T("Projects"), 22), new(T("Description"), 52), new(workbook.T("Apps"), 26)]);
        if (options.HourlyRate.HasValue) columns.Add(new(T("Amount") + " (" + options.Currency + ")", 17, ExcelFormat.Money));
        columns.Add(new(T("Notes"), 27, ExcelFormat.Editable));
        workbook.Add(new(T("Sheet"), T("Title"), T("ReviewNote"), columns, MainRows, metadata,
            hasParts ? 3 : 2, options.HourlyRate.HasValue ? columns.Count - 1 : null,
            limit => (rows.Take(limit ?? rows.Length).Sum(row => row.ActiveSeconds) / 86400d, rows.Take(limit ?? rows.Length).Sum(Amount)),
            AmountCondition: rateAvailable));
        workbook.Add(new(sourceName, sourceName, workbook.T("SourceHelp"),
            [new(workbook.T("Field"), 30), new(workbook.T("Value"), 80)], () => new object?[][]
            {
                [T("Rate") + " (" + options.Currency + ")", options.HourlyRate is { } hourlyRate ? new ExcelReportCell(hourlyRate, ExcelFormat.Editable) : T("NoBilling")],
                [T("Consultant"), options.Consultant], [T("Client"), options.Client],
                [workbook.T("Grouping"), options.Grouping == TimesheetGrouping.Week ? workbook.T("WeekOf") : T("Date")],
                [workbook.T("CreatedAt"), new ExcelReportCell(job.CreatedAt, ExcelFormat.DateTime)],
                [workbook.T("ExportedAt"), new ExcelReportCell(DateTimeOffset.Now, ExcelFormat.DateTime)],
                [workbook.T("Status"), T("State." + job.State)], [workbook.T("Reference"), job.Id.ToString("N")],
                [workbook.T("Sources"), string.Join(" / ", new[]
                {
                    options.Sources.IncludeDescriptionExcerpt || options.Sources.IncludeCompleteDescription ? strings.Translate("Export.SavedDescriptions") : null,
                    options.Sources.IncludeOcr ? "OCR" : null,
                    options.Sources.IncludeWindowTitles ? strings.Translate("Export.Titles") : null
                }.OfType<string>())],
                [workbook.T("DataHelp"), workbook.T("ArchiveHelp")], [workbook.T("Coverage"), workbook.T("CoverageHelp")],
                [strings.Translate("Export.ExcelTheme.Header"), workbook.T("ThemeHelp")]
            }, metadata));
        var detailColumns = new List<ExcelReportColumn>
        {
            new(T("Reference"), 20), new(T("Date"), 23, ExcelFormat.Date), new(T("Part"), 16), new(T("Project"), 26),
            new(T("Active"), 16, ExcelFormat.Duration), new(T("Idle"), 16, ExcelFormat.Duration),
            new(T("Start"), 26, ExcelFormat.DateTime), new(T("End"), 26, ExcelFormat.DateTime),
            new(workbook.T("Apps"), 38), new(T("References"), 46)
        };
        if (options.HourlyRate.HasValue) detailColumns.Add(new(T("Amount") + " (" + options.Currency + ")", 20, ExcelFormat.Money));
        workbook.Add(new(detailName, detailName, workbook.T("MeasuredHelp"), detailColumns,
            () => details.Select((item, index) => DetailRow(item.Parent, item.Observation, index)), metadata));
        var totalColumns = new List<ExcelReportColumn> { new(workbook.T("Month"), 20), new(workbook.T("Projects"), 28), new(T("Active"), 18, ExcelFormat.Duration), new(T("Idle"), 18, ExcelFormat.Duration) };
        if (options.HourlyRate.HasValue) totalColumns.Add(new(T("Amount") + " (" + options.Currency + ")", 20, ExcelFormat.Money));
        var months = details.GroupBy(item => (Month: item.Observation.Date.ToString("yyyy-MM", CultureInfo.InvariantCulture), item.Observation.Project)).ToArray();
        workbook.Add(new(workbook.T("MonthlyTotals"), workbook.T("MonthlyTotals"), workbook.T("MonthHelp"), totalColumns,
            () => months.Select((month, index) => MonthRow(month, index)), metadata));
        if (options.SeparateMonths)
            foreach (var month in months.GroupBy(group => group.Key.Month))
                workbook.Add(new(month.Key, month.Key, workbook.T("MonthHelp"), totalColumns, () => month.Select((group, index) => MonthRow(group, index)), metadata));
        workbook.Add(new(workbook.T("BatchDetails"), workbook.T("BatchDetails"), workbook.T("BatchHelp"),
            [new(T("Reference"), 20), new(T("Date"), 23, ExcelFormat.Date), new(T("Sources"), 16, ExcelFormat.Number), new(T("Status"), 28),
                new("input_tokens", 18, ExcelFormat.Number), new("output_tokens", 18, ExcelFormat.Number), new("response_id", 32)],
            () => job.Rows.Select(work => new object?[] { work.Row.Id, work.Row.Date, work.Row.SourceCount, T("State." + work.Row.State), work.Usage?.InputTokens, work.Usage?.OutputTokens, work.ResponseId }), metadata));
        if (archive is not null) ReportExcelData.Add(workbook, archive, workbook.T("ArchiveHelp"));
        ReportExportWriter.AtomicWrite(path, overwrite, stream => workbook.Write(stream, token), token);
        return path;

        // Source-detail rows are the same rounding unit in every report view.
        decimal DetailAmount(TimesheetRow row) => options.HourlyRate is { } rate ? decimal.Round((decimal)row.ActiveSeconds / 3600m * rate, 2, MidpointRounding.AwayFromZero) : 0m;
        decimal Amount(TimesheetRow row) => detailIndexes[row.Id].Sum(index => DetailAmount(details[index].Observation));

        IEnumerable<object?[]> MainRows() => rows.Select(row =>
        {
            var indexes = detailIndexes[row.Id].ToArray();
            var first = indexes.FirstOrDefault(-1);
            var last = indexes.LastOrDefault(-1);
            var output = new List<object?> { new ExcelReportCell(row.Date, Location: first >= 0 && (rowLimit is null || first < rowLimit) ? ReportExcelWorkbook.Location(detailName, "A" + (first + 8)) : null) };
            if (hasParts) output.Add(T("Part." + row.Part));
            output.AddRange([row.ActiveSeconds / 86400d, row.Project.Length > 0 ? row.Project : T("Unassigned"), row.State == "completed" ? row.Description : T("State." + row.State), row.Applications]);
            if (options.HourlyRate.HasValue)
                output.Add(new ExcelReportCell(Amount(row), Formula: first >= 0 && (rowLimit is null || last < rowLimit)
                    ? $"IF({rateAvailable},SUM({ReportExcelWorkbook.Location(detailName, $"K{first + 8}:K{last + 8}")}),\"\")" : null));
            output.Add(""); return output.ToArray();
        });

        object?[] DetailRow(TimesheetRow parent, TimesheetRow row, int index)
        {
            var values = new List<object?> { parent.Id, row.Date, T("Part." + row.Part), row.Project, row.ActiveSeconds / 86400d, row.IdleSeconds / 86400d, row.FirstObserved, row.LastObserved, row.Applications,
                options.Sources.Options.IncludeWindowTitles ? row.References : "" };
            if (options.HourlyRate.HasValue) values.Add(new ExcelReportCell(DetailAmount(row), Formula: $"IF({rateAvailable},ROUND(E{index + 8}*24*{rateReference},2),\"\")"));
            return values.ToArray();
        }

        object?[] MonthRow(IGrouping<(string Month, string Project), (TimesheetRow Parent, TimesheetRow Observation)> month, int index)
        {
            var values = new List<object?> { month.Key.Month, month.Key.Project, month.Sum(item => item.Observation.ActiveSeconds) / 86400d, month.Sum(item => item.Observation.IdleSeconds) / 86400d };
            if (options.HourlyRate.HasValue)
            {
                var number = index + 8;
                var amountRange = ReportExcelWorkbook.Location(detailName, $"$K$8:$K${details.Length + 7}");
                var dateRange = ReportExcelWorkbook.Location(detailName, $"$B$8:$B${details.Length + 7}");
                var projectRange = ReportExcelWorkbook.Location(detailName, $"$D$8:$D${details.Length + 7}");
                var start = $"DATE(LEFT(A{number},4),RIGHT(A{number},2),1)";
                var project = $"SUBSTITUTE(SUBSTITUTE(SUBSTITUTE(B{number},\"~\",\"~~\"),\"*\",\"~*\"),\"?\",\"~?\")";
                var formula = rowLimit is null ? $"IF({rateAvailable},SUMIFS({amountRange},{dateRange},\">=\"&{start},{dateRange},\"<\"&EDATE({start},1),{projectRange},\"=\"&{project}),\"\")" : null;
                values.Add(new ExcelReportCell(month.Sum(item => DetailAmount(item.Observation)), Formula: formula));
            }
            return values.ToArray();
        }
    }
}
