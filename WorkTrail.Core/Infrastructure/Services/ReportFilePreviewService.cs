// SPDX-License-Identifier: MIT

using System.Diagnostics;
using WorkTrail.Application;

namespace WorkTrail.Services;

/// <summary>Owns temporary workbook creation and opening through the installed spreadsheet application.</summary>
internal static class ReportFilePreviewService
{
    // store supplies selected retained history and saved batch results.
    // request selects the report; previews never submit cloud work.
    // token cancels before file publication or launching the associated application.
    internal static ReportExportResult Create(LocalStore store, ReportFilePreviewRequest request, CancellationToken token)
    {
        var directory = Path.Combine(Path.GetTempPath(), "WorkTrail", "report-previews");
        Directory.CreateDirectory(directory);
        // Only generated UUID-named files in this owned directory are eligible for expiry.
        foreach (var file in Directory.EnumerateFiles(directory, "*.xlsx").Take(200))
        {
            if (!Guid.TryParseExact(Path.GetFileNameWithoutExtension(file), "N", out _)
                || File.GetLastWriteTimeUtc(file) >= DateTime.UtcNow.AddDays(-1)
                || (File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0) continue;
            try { File.Delete(file); }
            catch (IOException) { /* An open spreadsheet can retain its file until the next cleanup. */ }
        }
        var path = Path.Combine(directory, Guid.NewGuid().ToString("N") + ".xlsx");
        if (request.Timesheet is not null || request.TimesheetJobId is not null)
        {
            var job = request.TimesheetJobId is { } id ? new TimesheetBatchService(store).Load(id)
                : new TimesheetJob
                {
                    Id = Guid.NewGuid(),
                    CreatedAt = DateTimeOffset.UtcNow,
                    Options = request.Timesheet!,
                    Rows = TimesheetProjection.Build(store, request.Timesheet!, token)
                };
            TimesheetExcelWriter.Write(job, path, false, token, rowLimit: 10);
            return new(path, new FileInfo(path).Length, 2 + (job.Options.SeparateMonths
                ? job.Rows.Select(row => (row.Row.Date.Year, row.Row.Date.Month)).Distinct().Count() : 0));
        }
        var document = new ReportExportService(store).Build(request.Options with { Format = ReportExportFormat.Excel }, request.Summary, token);
        return ReportExportWriter.Write(document with { RowLimit = 10 }, path, false, token);
    }

    // path is the completed temporary workbook created by this service, never an arbitrary shell command.
    internal static void Open(string path) => Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
}
