// SPDX-License-Identifier: MIT
using WorkTrail.Application;

namespace WorkTrail.Services;

internal sealed partial class TimesheetBatchService
{
    private TimesheetBatchView ReadTimeline(TimesheetBatchCommand command, CancellationToken token)
    {
        if (command.From is not { } from || command.ToInclusive is not { } to
            || to < from || to.DayNumber - from.DayNumber > 6 || to == DateOnly.MaxValue || from == DateOnly.MinValue)
            throw new ArgumentException("A timeline query must contain one to seven local days.");

        var zone = TimeZoneInfo.Local;
        var spans = new List<ActivitySpan>();
        var days = new List<TimesheetDayStatus>();
        var revisions = new List<string>();
        // Read saved snapshots only. Opening a calendar never polls a provider or changes a draft.
        var candidates = List(token).Where(j => j.State != "historical"
            && j.ToInclusive >= from.AddDays(-1) && j.From <= to.AddDays(1))
            .Select(j => Load(j.Id)).Where(j => j.Version >= 3).ToArray();
        for (var date = from; date <= to; date = date.AddDays(1))
        {
            token.ThrowIfCancellationRequested();
            var start = LocalMidnight(date, zone);
            var end = LocalMidnight(date.AddDays(1), zone);
            // A failed or pending regeneration cannot hide an older completed reconstruction.
            // Select one source per display day so overlapping drafts are never double counted.
            var job = candidates.FirstOrDefault(j => j.ResultsSaved && (j.Segments.Any(s => s.Start < end && s.End > start)
                || j.Rows.Any(r => r.Row.Date == date && r.Row.State == "completed")));
            var latestRow = candidates.SelectMany(j => j.Rows).FirstOrDefault(r => r.Row.Date == date);
            days.Add(new(date, job is null ? latestRow?.Row.State ?? "not_generated" : "completed", 0));
            if (job is null) continue;
            revisions.Add($"{date:yyyyMMdd}:{job.Id:N}:{job.Revision}");
            var tasks = job.Tasks.ToDictionary(t => t.Id, StringComparer.Ordinal);
            foreach (var segment in job.Segments.Where(s => s.Start < end && s.End > start).OrderBy(s => s.Start))
            {
                var title = segment.TaskId is { } id && tasks.TryGetValue(id, out var task) ? task.Title : "";
                spans.Add(new(job.Id, segment.Id, date,
                    TimeZoneInfo.ConvertTime(segment.Start < start ? start : segment.Start, zone),
                    TimeZoneInfo.ConvertTime(segment.End > end ? end : segment.End, zone),
                    title, segment.Project, segment.Basis, segment.ReviewState, job.Confirmed, job.CreatedAt, start, end));
            }
        }
        const int size = 200;
        var page = Math.Min(command.SegmentPage, spans.Count == 0 ? 0 : (spans.Count - 1) / size);
        return new([], null, TimeZoneId: zone.Id, Days: days, SegmentPage: page,
            SegmentCount: spans.Count, Timeline: spans.Skip(page * size).Take(size).ToArray(),
            TimelineRevision: string.Join("|", revisions));
    }

    private static DateTimeOffset LocalMidnight(DateOnly date, TimeZoneInfo zone)
    {
        var local = date.ToDateTime(TimeOnly.MinValue);
        // Some zones advance at midnight; the first valid instant is the day's boundary.
        while (zone.IsInvalidTime(local)) local = local.AddMinutes(1);
        return new(local, zone.GetUtcOffset(local));
    }
}
