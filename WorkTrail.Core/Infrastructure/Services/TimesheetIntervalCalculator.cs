// SPDX-License-Identifier: MIT
using WorkTrail.Application;

namespace WorkTrail.Services;

/// <summary>Pure deterministic interval reconstruction; no transport, input-state or persistence dependency.</summary>
internal static class TimesheetIntervalCalculator
{
    internal const string RuleRevision = "observations-1";

    internal static List<TimesheetSegment> Calculate(IReadOnlyList<TimesheetObservation> observations,
        TimesheetInference inference, IReadOnlyList<TimesheetSessionEvent> events, TimeZoneInfo zone)
    {
        var assigned = inference.Assignments.ToDictionary(a => a.ObservationId, a => a.TaskId, StringComparer.Ordinal);
        var tasks = inference.Tasks.ToDictionary(t => t.Id, StringComparer.Ordinal);
        var relations = inference.Transitions.ToDictionary(t => (t.FromObservationId, t.ToObservationId), t => t.Relation);
        var segments = new List<TimesheetSegment>();
        foreach (var installation in observations.DistinctBy(o => o.Id).GroupBy(o => o.InstallationId))
        {
            var timeline = installation.OrderBy(o => o.ObservedAt).ThenBy(o => o.Id, StringComparer.Ordinal).ToArray();
            var signals = events.Where(e => e.InstallationId == installation.Key).OrderBy(e => e.At).ToArray();
            for (var i = 1; i < timeline.Length; i++)
            {
                var left = timeline[i - 1];
                var right = timeline[i];
                if (right.ObservedAt <= left.ObservedAt) continue;
                if (DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(left.ObservedAt, zone).DateTime)
                    != DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(right.ObservedAt, zone).DateTime)) continue;
                var start = left.ObservedAt.ToUniversalTime();
                var end = right.ObservedAt.ToUniversalTime();
                var sources = new[] { left.Id, right.Id };
                var boundaries = signals.Where(e => e.At > start && e.At <= end).ToArray();
                var state = new HashSet<string>(StringComparer.Ordinal);
                foreach (var signal in signals.Where(e => e.At <= start)) ApplySignal(state, signal.Kind);
                if (boundaries.Length > 0 || state.Count > 0)
                {
                    var cursor = start;
                    foreach (var signal in boundaries)
                    {
                        if (signal.At > cursor) Add(cursor, signal.At, null, state.Count > 0 ? "unavailable" : "unallocated");
                        cursor = signal.At;
                        ApplySignal(state, signal.Kind);
                    }
                    if (cursor < end) Add(cursor, end, null, state.Count > 0 ? "unavailable" : "unallocated");
                    continue;
                }
                var limit = 2L * Math.Max(left.CaptureIntervalSeconds, right.CaptureIntervalSeconds) + 30;
                var relation = relations.GetValueOrDefault((left.Id, right.Id), "uncertain");
                var leftTask = assigned.GetValueOrDefault(left.Id);
                var rightTask = assigned.GetValueOrDefault(right.Id);
                if ((end - start).TotalSeconds > limit || leftTask is null || rightTask is null || relation == "uncertain")
                    Add(start, end, null, "unallocated");
                else if (relation == "continuous" && leftTask == rightTask) Add(start, end, leftTask, "continuous");
                else if (relation == "switch" && leftTask != rightTask)
                {
                    var midpoint = start.AddTicks((end - start).Ticks / 2);
                    Add(start, midpoint, leftTask, "midpoint");
                    Add(midpoint, end, rightTask, "midpoint");
                }
                else throw new InvalidDataException("Invalid validated timesheet transition.");

                void Add(DateTimeOffset a, DateTimeOffset b, string? task, string basis) => segments.Add(new(
                    $"{left.Id}.{right.Id}.{a.UtcTicks}", a, b, task,
                    task is null ? "" : tasks[task].Project, sources, basis,
                    EstimatedBoundaries: basis == "midpoint" ? [start.AddTicks((end - start).Ticks / 2)] : null));
            }
        }
        return Combine(segments);
    }

    private static void ApplySignal(HashSet<string> state, string kind)
    {
        switch (kind)
        {
            case "runtime_start": state.Clear(); state.Add("tracking"); break;
            case "paused": case "stopped": case "runtime_stop": state.Add("tracking"); break;
            case "started": state.Remove("tracking"); break;
            case "locked": state.Add("lock"); break;
            case "unlocked": state.Remove("lock"); break;
            case "suspended": state.Add("power"); break;
            case "resumed": state.Remove("power"); break;
        }
    }

    // Each installation is calculated first. The sweep counts simultaneous UTC spans only once.
    internal static List<TimesheetSegment> Combine(IReadOnlyList<TimesheetSegment> segments)
    {
        var edges = segments.SelectMany(s => new[] { (At: s.Start, Segment: s, Add: true), (At: s.End, Segment: s, Add: false) })
            .OrderBy(e => e.At).GroupBy(e => e.At);
        var live = new Dictionary<string, TimesheetSegment>(StringComparer.Ordinal);
        var result = new List<TimesheetSegment>();
        DateTimeOffset? previous = null;
        foreach (var edge in edges)
        {
            if (previous is { } start && start < edge.Key && live.Count > 0)
            {
                var work = live.Values.Where(s => s.Allocated).ToArray();
                var choice = work.Length == 1 ? work[0] : live.Values.FirstOrDefault(s => s.Basis == "unallocated") ?? live.Values.First();
                var sources = live.Values.SelectMany(s => s.SourceObservationIds).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
                var span = work.Length > 1
                    ? new TimesheetSegment($"overlap.{start.UtcTicks}", start, edge.Key, null, "", sources,
                        "overlap", "needs_review", Alternatives: work.Select(s => s with { Start = start, End = edge.Key }).ToArray())
                    : choice with { Id = choice.Id + "." + start.UtcTicks, Start = start, End = edge.Key, SourceObservationIds = sources };
                result.Add(span);
            }
            foreach (var item in edge.Where(e => !e.Add)) live.Remove(item.Segment.Id);
            foreach (var item in edge.Where(e => e.Add)) live.Add(item.Segment.Id, item.Segment);
            previous = edge.Key;
        }
        return MergeAdjacent(result);
    }

    internal static List<TimesheetSegment> MergeAdjacent(IEnumerable<TimesheetSegment> segments)
    {
        var result = new List<TimesheetSegment>();
        foreach (var segment in segments.OrderBy(s => s.Start))
        {
            if (result.LastOrDefault() is { } prior && prior.End == segment.Start && prior.TaskId == segment.TaskId
                && prior.Project == segment.Project && prior.ReviewState == segment.ReviewState
                && (prior.Basis == segment.Basis || prior.Allocated && segment.Allocated)
                && prior.UserNote == segment.UserNote && prior.Alternatives is null && segment.Alternatives is null)
                result[^1] = prior with
                {
                    End = segment.End,
                    Basis = prior.Basis == "midpoint" || segment.Basis == "midpoint" ? "midpoint" : prior.Basis,
                    EstimatedBoundaries = (prior.EstimatedBoundaries ?? []).Concat(segment.EstimatedBoundaries ?? []).Distinct().ToArray(),
                    SourceObservationIds = prior.SourceObservationIds.Concat(segment.SourceObservationIds).Distinct(StringComparer.Ordinal).ToArray()
                };
            else result.Add(segment);
        }
        return result;
    }
}
