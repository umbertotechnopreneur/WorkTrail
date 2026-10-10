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


using System.Diagnostics;
using WorkTrail.Application;
using Windows.ApplicationModel.DataTransfer;

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
            job.Options = job.Options with { Sources = job.Options.Sources with { Options = job.Options.Sources.Options with { Theme = request.Options.Theme } } };
            var archive = new ReportExportService(store).Build(job.Options.Sources.Options, null, token);
            TimesheetExcelWriter.Write(job, path, false, token, rowLimit: 10, archive: archive);
            using var package = System.IO.Compression.ZipFile.OpenRead(path);
            return new(path, new FileInfo(path).Length, package.Entries.Count(entry => entry.FullName.StartsWith("xl/worksheets/sheet", StringComparison.Ordinal) && entry.FullName.EndsWith(".xml", StringComparison.Ordinal)));
        }
        var document = new ReportExportService(store).Build(request.Options with { Format = ReportExportFormat.Excel }, request.Summary, token);
        return ReportExportWriter.Write(document with { RowLimit = 10 }, path, false, token);
    }

    // path is the completed temporary workbook created by this service, never an arbitrary shell command.
    internal static void Open(string path) => Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });

    // path comes from a previous preview result; only files owned by this feature can reach the shell or clipboard.
    internal static string ValidatePath(string? path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var directory = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "WorkTrail", "report-previews"));
        if (!Path.IsPathFullyQualified(path))
            throw new ArgumentException("The preview path must be absolute.", nameof(path));
        var fullPath = Path.GetFullPath(path);
        if (!string.Equals(Path.GetDirectoryName(fullPath), directory, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(Path.GetExtension(fullPath), ".xlsx", StringComparison.OrdinalIgnoreCase)
            || !Guid.TryParseExact(Path.GetFileNameWithoutExtension(fullPath), "N", out _))
            throw new ArgumentException("The file is not a WorkTrail preview.", nameof(path));
        if (!File.Exists(fullPath)) throw new FileNotFoundException("The preview file no longer exists.", fullPath);
        if ((File.GetAttributes(fullPath) & FileAttributes.ReparsePoint) != 0
            || (File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
            throw new IOException("Preview links cannot be opened or copied.");
        return fullPath;
    }

    // path is the validated absolute workbook path, copied as text rather than file contents.
    // token cancels before the clipboard is changed; an already completed copy is not rolled back.
    internal static Task CopyPathAsync(string path, CancellationToken token)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        // Windows clipboard calls need an STA; the runtime's worker threads do not own a UI apartment.
        var thread = new Thread(() =>
        {
            try
            {
                token.ThrowIfCancellationRequested();
                var content = new DataPackage();
                content.SetText(path);
                Clipboard.SetContent(content);
                Clipboard.Flush();
                completion.TrySetResult();
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { completion.TrySetCanceled(token); }
            catch (Exception exception) { completion.TrySetException(exception); }
        })
        { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task;
    }
}
