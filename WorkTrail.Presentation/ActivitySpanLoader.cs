// SPDX-License-Identifier: MIT
using WorkTrail.Application;

namespace WorkTrail.Presentation;

/// <summary>Contains a consistent local timeline and the reconstruction state of each requested day.</summary>
public sealed record ActivitySpanSnapshot(IReadOnlyList<ActivitySpan> Spans, IReadOnlyList<TimesheetDayStatus> Days);

/// <summary>Collects bounded local timeline pages through the shared application facade.</summary>
public static class ActivitySpanLoader
{
    /// <summary>Reads saved intervals without invoking inference, remote refresh, or export.</summary>
    public static async Task<ActivitySpanSnapshot> LoadAsync(IWorkTrailApplication application,
        DateOnly from, DateOnly to, CancellationToken token)
    {
        var spans = new List<ActivitySpan>();
        string? revision = null;
        for (var page = 0; ; page++)
        {
            token.ThrowIfCancellationRequested();
            var result = await application.ManageTimesheetBatchAsync(new(TimesheetBatchAction.Timeline,
                SegmentPage: page, From: from, ToInclusive: to), token).ConfigureAwait(false);
            if (!result.Succeeded || result.Value is not { } view)
                throw new InvalidOperationException(result.Code);
            if (view.SegmentPage != page) throw new InvalidOperationException("The timeline changed while loading.");
            if (page == 0) revision = view.TimelineRevision;
            else if (revision != view.TimelineRevision) throw new InvalidOperationException("The timeline changed while loading.");
            spans.AddRange(view.Timeline ?? []);
            if (spans.Count >= view.SegmentCount) return new(spans, view.Days ?? []);
            if (view.Timeline is not { Count: > 0 }) throw new InvalidOperationException("Incomplete timeline page.");
        }
    }
}
