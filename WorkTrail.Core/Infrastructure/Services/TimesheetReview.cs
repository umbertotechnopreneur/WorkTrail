// SPDX-License-Identifier: MIT
using WorkTrail.Application;

namespace WorkTrail.Services;

/// <summary>Applies optimistic local corrections to the saved disjoint timeline without any provider calls.</summary>
internal static class TimesheetReview
{
    internal static void Apply(TimesheetJob job, TimesheetBatchCommand command)
    {
        if (!job.ResultsSaved) throw new ReportExportValidationException("Timesheet.NotReady");
        if (command.ExpectedRevision != job.Revision) throw new ReportExportValidationException("Timesheet.EditConflict");
        var segments = job.Segments.ToList();
        var tasks = job.Tasks.ToList();
        if (command.Action == TimesheetBatchAction.Confirm)
        {
            if (segments.Any(s => s.Basis == "overlap")) throw new ReportExportValidationException("Timesheet.ResolveOverlap");
            job.Segments = segments.Select(s => s with { ReviewState = "confirmed" }).ToList();
            job.Confirmed = true;
            job.Revision++;
            return;
        }
        var edit = command.Edit ?? throw new ArgumentException("A local correction is required.");
        if (edit.Title.Length > 160 || edit.Project.Length > 120 || edit.UserNote?.Length > 1000)
            throw new ReportExportValidationException("Timesheet.InvalidEdit");
        var current = edit.SegmentId is null ? null : segments.SingleOrDefault(s => s.Id == edit.SegmentId)
            ?? throw new ReportExportValidationException("Timesheet.EditConflict");
        switch (command.Action)
        {
            case TimesheetBatchAction.Edit:
                if (edit.Basis is not ("manual" or "unallocated" or "unavailable")) throw new ReportExportValidationException("Timesheet.InvalidEdit");
                var basis = edit.Basis;
                string? taskId = null;
                IReadOnlyList<string>? selectedSources = null;
                var project = "";
                if (basis == "manual")
                {
                    if (edit.AlternativeId is { } alternativeId)
                    {
                        var alternative = current?.Alternatives?.SingleOrDefault(s => s.Id == alternativeId)
                            ?? throw new ReportExportValidationException("Timesheet.InvalidEdit");
                        taskId = alternative.TaskId; project = alternative.Project;
                        selectedSources = alternative.SourceObservationIds;
                    }
                    else
                    {
                        if (string.IsNullOrWhiteSpace(edit.Title)) throw new ReportExportValidationException("Timesheet.InvalidEdit");
                        taskId = "manual-" + Guid.NewGuid().ToString("N"); project = edit.Project.Trim();
                        tasks.Add(new(taskId, edit.Title.Trim(), project, "", current?.SourceObservationIds ?? []));
                    }
                }
                if (current is not null) segments.Remove(current);
                var sources = selectedSources ?? current?.SourceObservationIds ?? job.Rows.SelectMany(r => r.Evidence)
                    .Where(o => o.ObservedAt >= edit.Start && o.ObservedAt <= edit.End).Select(o => o.Id).ToArray();
                segments.Add(new(current?.Id ?? Guid.NewGuid().ToString("N"), edit.Start.ToUniversalTime(), edit.End.ToUniversalTime(),
                    taskId, project, sources, basis, "reviewed", edit.UserNote));
                break;
            case TimesheetBatchAction.Split:
                if (current is null || edit.Start <= current.Start || edit.Start >= current.End)
                    throw new ReportExportValidationException("Timesheet.InvalidEdit");
                segments.Remove(current);
                segments.Add(current with { End = edit.Start.ToUniversalTime(), ReviewState = "reviewed" });
                segments.Add(current with { Id = Guid.NewGuid().ToString("N"), Start = edit.Start.ToUniversalTime(), ReviewState = "reviewed" });
                break;
            case TimesheetBatchAction.Merge:
                var next = segments.SingleOrDefault(s => s.Id == edit.OtherSegmentId);
                if (current is null || next is null || current.End != next.Start || current.TaskId != next.TaskId
                    || current.Project != next.Project || current.ReviewState != next.ReviewState
                    || current.UserNote != next.UserNote
                    || current.Alternatives is not null || next.Alternatives is not null)
                    throw new ReportExportValidationException("Timesheet.InvalidMerge");
                segments.Remove(current); segments.Remove(next);
                segments.Add(current with
                {
                    End = next.End,
                    ReviewState = "reviewed",
                    EstimatedBoundaries = (current.EstimatedBoundaries ?? []).Concat(next.EstimatedBoundaries ?? []).Distinct().ToArray(),
                    SourceObservationIds = current.SourceObservationIds.Concat(next.SourceObservationIds).Distinct(StringComparer.Ordinal).ToArray()
                });
                break;
            default: throw new ArgumentException("Unknown local timesheet correction.");
        }
        var zone = TimeZoneInfo.FindSystemTimeZoneById(job.Options.Sources.Options.TimeZoneId);
        var from = ReportAggregationService.ConvertLocalBoundaryToUtc(job.Options.Sources.Options.From, zone);
        var to = ReportAggregationService.ConvertLocalBoundaryToUtc(job.Options.Sources.Options.ToInclusive.AddDays(1), zone);
        var ordered = segments.OrderBy(s => s.Start).ThenBy(s => s.Id, StringComparer.Ordinal).ToList();
        for (var i = 0; i < ordered.Count; i++)
        {
            var segment = ordered[i];
            if (segment.End <= segment.Start || segment.Start < from || segment.End > to)
                throw new ReportExportValidationException("Timesheet.InvalidEdit");
            if (i > 0 && ordered[i - 1].End > segment.Start) throw new ReportExportValidationException("Timesheet.EditOverlap");
        }
        job.Segments = ordered; job.Tasks = tasks; job.Confirmed = false; job.Revision++;
    }
}
