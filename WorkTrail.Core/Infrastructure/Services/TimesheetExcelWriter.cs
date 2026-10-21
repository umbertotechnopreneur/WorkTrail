// SPDX-License-Identifier: MIT
using System.Globalization;
using WorkTrail.Application;

namespace WorkTrail.Services;

/// <summary>Exports exactly the saved task intervals, including local corrections and visible coverage gaps.</summary>
internal static class TimesheetExcelWriter
{
    internal static string Write(TimesheetJob job, string destination, bool overwrite, CancellationToken token,
        int? rowLimit = null, ExportDocument? archive = null)
    {
        if (job.Version < 3) throw new ReportExportValidationException("Timesheet.Historical");
        if (!job.ResultsSaved) throw new ReportExportValidationException("Timesheet.NotReady");
        if (!Path.IsPathFullyQualified(destination) || !string.Equals(Path.GetExtension(destination), ".xlsx", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("A fully qualified XLSX destination is required.");
        var options = job.Options;
        var strings = new LocalizationService(options.Sources.Options.Language);
        string T(string key) => strings.Translate("Timesheet." + key);
        var workbook = new ReportExcelWorkbook(strings, options.Sources.Options, rowLimit);
        var tasks = job.Tasks.ToDictionary(t => t.Id, StringComparer.Ordinal);
        var details = Details(job);
        var billing = options.HourlyRate.HasValue;
        var state = T(job.Confirmed ? "Confirmed" : "Draft");
        var note = state + ". " + T("EstimateHelp") + (billing && !job.Confirmed ? " " + T("Provisional") : "");
        var metadata = T("Consultant") + ": " + options.Consultant + " · " + T("Client") + ": " + options.Client;
        workbook.Metadata = metadata;
        var rateReference = ReportExcelWorkbook.Location(workbook.T("SourceGuide"), "$B$8");
        var rateAvailable = $"AND(ISNUMBER({rateReference}),{rateReference}>=0,{rateReference}<=1000000)";
        var detailColumns = new List<ExcelReportColumn>
        {
            new(T("Reference"), 32), new(T("Date"), 18, ExcelFormat.Date), new(T("Project"), 25), new(T("Task"), 40),
            new(T("Start"), 27, ExcelFormat.DateTime), new(T("End"), 27, ExcelFormat.DateTime),
            new(T("IntervalDuration"), 18, ExcelFormat.Duration), new(T("EstimatedWork"), 18, ExcelFormat.Duration),
            new(T("Status"), 22), new(T("Basis"), 24), new(T("References"), 55), new(T("Notes"), 30)
        };
        if (billing) detailColumns.Add(new(T("Amount") + " (" + options.Currency + ")", 20, ExcelFormat.Money));
        workbook.Add(new(T("DetailsSheet"), T("DetailsSheet"), note, detailColumns,
            () => details.Select((d, i) => Detail(d, i)), metadata, 8, billing ? 13 : null,
            limit => (details.Take(limit ?? details.Count).Where(d => d.Segment.Allocated).Sum(d => d.Seconds) / 86400d,
                details.Take(limit ?? details.Count).Sum(d => Amount(d))), AmountCondition: rateAvailable));
        AddSummary(T("Sheet"), details.GroupBy(d => (Period: TimesheetProjection.GroupDate(d.Date, options.Grouping).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), d.Segment.Project)).ToArray(),
            options.Grouping == TimesheetGrouping.Week ? workbook.T("WeekOf") : T("Date"));
        var months = details.GroupBy(d => (Period: d.Date.ToString("yyyy-MM", CultureInfo.InvariantCulture), d.Segment.Project)).ToArray();
        AddSummary(workbook.T("MonthlyTotals"), months, workbook.T("Month"));
        if (options.SeparateMonths)
            foreach (var month in months.GroupBy(m => m.Key.Period)) AddSummary(month.Key, month.ToArray(), workbook.T("Month"));
        workbook.Add(new(workbook.T("SourceGuide"), workbook.T("SourceGuide"), note,
            [new(workbook.T("Field"), 32), new(workbook.T("Value"), 90)], () => new object?[][]
            {
                [T("Rate") + " (" + options.Currency + ")", options.HourlyRate is { } rate ? new ExcelReportCell(rate, ExcelFormat.Editable) : T("NoBilling")],
                [T("Consultant"), options.Consultant], [T("Client"), options.Client], [T("Status"), state],
                [T("Reference"), job.Id.ToString("N")], [T("Revision"), job.Revision], [T("Rules"), job.RuleRevision],
                [T("TimeZone"), options.Sources.Options.TimeZoneId],
                [T("ObservationSpan"), ObservationSpan(job)], [T("EstimateHelp"), T("EstimateHelp")]
            }, metadata));
        workbook.Add(new(T("EvidenceSheet"), T("EvidenceSheet"), T("EvidenceHelp"),
            [new(T("Reference"), 42), new(T("Start"), 27, ExcelFormat.DateTime), new(T("Installation"), 38),
                new(T("Capture"), 38), new(T("Analysis"), 38), new(T("SourceKind"), 25), new(workbook.T("Apps"), 30), new(T("Cadence"), 22), new(T("AssumedCadence"), 24)],
            () => job.Rows.SelectMany(r => r.Evidence).Select(o => new object?[] { o.Id,
                TimeZoneInfo.ConvertTime(o.ObservedAt, TimeZoneInfo.FindSystemTimeZoneById(options.Sources.Options.TimeZoneId)), o.InstallationId,
                o.CaptureId, o.AnalysisId, o.SourceKind, o.Application, o.CaptureIntervalSeconds, o.AssumedCadence ? T("AssumedCadence") : "" }), metadata));
        workbook.Add(new(workbook.T("BatchDetails"), workbook.T("BatchDetails"), T("DailyHelp"),
            [new(T("Date"), 20, ExcelFormat.Date), new(T("Status"), 25), new(T("Sources"), 16, ExcelFormat.Number), new(T("Failure"), 45)],
            () => job.Rows.Select(r => new object?[] { r.Row.Date, T("State." + r.Row.State), r.Evidence.Count,
                r.FailureCode is null ? "" : strings.Translate(r.FailureCode) }), metadata));
        ReportExportWriter.AtomicWrite(Path.GetFullPath(destination), overwrite, stream => workbook.Write(stream, token), token);
        return Path.GetFullPath(destination);

        decimal Amount(TimesheetExportDetail d) => DetailAmount(d, options.HourlyRate);
        object?[] Detail(TimesheetExportDetail d, int index)
        {
            var segment = d.Segment;
            var zone = TimeZoneInfo.FindSystemTimeZoneById(options.Sources.Options.TimeZoneId);
            var values = new List<object?> { segment.Id, d.Date, segment.Project.Length == 0 ? T("Unassigned") : segment.Project,
                segment.TaskId is { } taskId && tasks.TryGetValue(taskId, out var task) ? task.Title : T("Basis." + segment.Basis),
                TimeZoneInfo.ConvertTime(d.Start, zone), TimeZoneInfo.ConvertTime(d.End, zone), d.Seconds / 86400d,
                segment.Allocated ? d.Seconds / 86400d : 0d, T("Review." + segment.ReviewState), T("Basis." + segment.Basis),
                string.Join("; ", segment.SourceObservationIds), (segment.UserNote ?? "")
                    + (segment.EstimatedBoundaries?.Count > 0 ? " " + T("Basis.midpoint") + ": "
                        + string.Join("; ", segment.EstimatedBoundaries.Select(b => TimeZoneInfo.ConvertTime(b, zone).ToString("O"))) : "") };
            if (billing) values.Add(new ExcelReportCell(Amount(d), Formula: $"IF({rateAvailable},ROUND(H{index + 8}*24*{rateReference},2),\"\")"));
            return values.ToArray();
        }
        void AddSummary(string name, IGrouping<(string Period, string Project), TimesheetExportDetail>[] groups, string periodLabel)
        {
            var columns = new List<ExcelReportColumn> { new(periodLabel, 22), new(T("Project"), 26), new(T("EstimatedWork"), 20, ExcelFormat.Duration),
                new(T("Basis.unallocated"), 20, ExcelFormat.Duration), new(T("Basis.unavailable"), 20, ExcelFormat.Duration), new(T("Basis.overlap"), 20, ExcelFormat.Duration), new(T("Status"), 22) };
            if (billing) columns.Add(new(T("Amount") + " (" + options.Currency + ")", 20, ExcelFormat.Money));
            workbook.Add(new(name, name, note, columns, () => groups.Select((g, index) =>
            {
                var start = g.Key.Period.Length == 7 ? new DateOnly(int.Parse(g.Key.Period[..4], CultureInfo.InvariantCulture), int.Parse(g.Key.Period[5..], CultureInfo.InvariantCulture), 1)
                    : DateOnly.ParseExact(g.Key.Period, "yyyy-MM-dd", CultureInfo.InvariantCulture);
                var end = g.Key.Period.Length == 7 ? start.AddMonths(1) : start.AddDays(options.Grouping == TimesheetGrouping.Week ? 7 : 1);
                var firstDate = start.ToDateTime(TimeOnly.MinValue).ToOADate();
                var lastDate = end.ToDateTime(TimeOnly.MinValue).ToOADate();
                var dateRange = ReportExcelWorkbook.Location(T("DetailsSheet"), "$B$8:$B$" + (details.Count + 7));
                var projectRange = ReportExcelWorkbook.Location(T("DetailsSheet"), "$C$8:$C$" + (details.Count + 7));
                var amountRange = ReportExcelWorkbook.Location(T("DetailsSheet"), "$M$8:$M$" + (details.Count + 7));
                var project = $"SUBSTITUTE(SUBSTITUTE(SUBSTITUTE(B{index + 8},\"~\",\"~~\"),\"*\",\"~*\"),\"?\",\"~?\")";
                var values = new List<object?> { g.Key.Period, g.Key.Project.Length == 0 ? T("Unassigned") : g.Key.Project,
                    g.Where(d => d.Segment.Allocated).Sum(d => d.Seconds) / 86400d,
                    g.Where(d => d.Segment.Basis == "unallocated").Sum(d => d.Seconds) / 86400d,
                    g.Where(d => d.Segment.Basis == "unavailable").Sum(d => d.Seconds) / 86400d,
                    g.Where(d => d.Segment.Basis == "overlap").Sum(d => d.Seconds) / 86400d, state };
                if (billing) values.Add(new ExcelReportCell(g.Sum(d => Amount(d)), Formula: rowLimit is not null ? null
                    : $"IF({rateAvailable},SUMIFS({amountRange},{dateRange},\">={firstDate.ToString(CultureInfo.InvariantCulture)}\",{dateRange},\"<{lastDate.ToString(CultureInfo.InvariantCulture)}\",{projectRange},\"=\"&{project}),\"\")"));
                return values.ToArray();
            }), metadata, 3, billing ? 8 : null,
                limit => (groups.Take(limit ?? groups.Length).SelectMany(g => g).Where(d => d.Segment.Allocated).Sum(d => d.Seconds) / 86400d,
                    groups.Take(limit ?? groups.Length).SelectMany(g => g).Sum(d => Amount(d))), AmountCondition: rateAvailable));
        }
    }

    internal static decimal DetailAmount(TimesheetExportDetail detail, decimal? rate) => detail.Segment.Allocated && rate is { } hourly
        ? decimal.Round((decimal)detail.Seconds / 3600m * hourly, 2, MidpointRounding.AwayFromZero) : 0m;

    internal static List<TimesheetExportDetail> Details(TimesheetJob job)
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById(job.Options.Sources.Options.TimeZoneId);
        return job.Segments.SelectMany(s => ReportAggregationService.SplitIntoLocalHourSegments(s.Start.UtcTicks, s.End.UtcTicks, zone)
            .GroupBy(part => part.Bucket.Date).Select(day => new TimesheetExportDetail(s, day.Key,
                new(day.Min(p => p.StartTicks), TimeSpan.Zero), new(day.Max(p => p.EndTicks), TimeSpan.Zero))))
            .OrderBy(d => d.Start).ToList();
    }

    private static string ObservationSpan(TimesheetJob job)
    {
        var evidence = job.Rows.SelectMany(r => r.Evidence).ToArray();
        if (evidence.Length == 0) return "";
        var zone = TimeZoneInfo.FindSystemTimeZoneById(job.Options.Sources.Options.TimeZoneId);
        return TimeZoneInfo.ConvertTime(evidence.Min(o => o.ObservedAt), zone).ToString("O") + " — "
            + TimeZoneInfo.ConvertTime(evidence.Max(o => o.ObservedAt), zone).ToString("O");
    }
}

internal sealed record TimesheetExportDetail(TimesheetSegment Segment, DateOnly Date, DateTimeOffset Start, DateTimeOffset End)
{
    internal double Seconds => (End - Start).TotalSeconds;
}
