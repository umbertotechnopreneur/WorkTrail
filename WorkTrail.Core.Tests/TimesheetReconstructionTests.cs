// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using WorkTrail.Application;
using WorkTrail.Services;
using Xunit;

namespace WorkTrail.Core.Tests;

/// <summary>Deterministic synthetic evidence tests; no provider, native capture or input hooks are used.</summary>
public sealed class TimesheetReconstructionTests
{
    private static readonly DateTimeOffset Day = new(2026, 10, 21, 9, 0, 0, TimeSpan.Zero);
    private static List<TimesheetObservation> Observations(params int[] minutes) => minutes.Select((minute, index) =>
        new TimesheetObservation("obs-" + index, "device", Day.AddMinutes(minute), "cap-" + index, "analysis-" + index,
            "capture", "Reading a technical document.", index % 3 == 0 ? "Editor" : index % 3 == 1 ? "Browser" : "Terminal", null, "")).ToList();

    private static TimesheetInference Inference(IReadOnlyList<TimesheetObservation> observations, params string?[] identities)
    {
        var assigned = identities.Length == 0 ? observations.Select(_ => (string?)"A").ToArray() : identities;
        return new(assigned.OfType<string>().Distinct().Select(id => new TimesheetTask(id, "Objective " + id, "Project", "Observed work",
                observations.Where((_, i) => assigned[i] == id).Select(o => o.Id).ToArray())).ToArray(),
            observations.Select((o, i) => new TimesheetAssignment(o.Id, assigned[i])).ToArray(),
            observations.Zip(observations.Skip(1)).Select((pair, i) => new TimesheetTransition(pair.First.Id, pair.Second.Id,
                assigned[i] is null || assigned[i + 1] is null ? "uncertain" : assigned[i] == assigned[i + 1] ? "continuous" : "switch")).ToArray());
    }

    /// <summary>Counts only elapsed time between the first and last semantic observations.</summary>
    [Fact]
    public void EightReadingObservations_EarnThirtyFiveMinutesAcrossApplications()
    {
        var evidence = Observations(0, 5, 10, 15, 20, 25, 30, 35);
        var segments = TimesheetIntervalCalculator.Calculate(evidence, Inference(evidence), [], TimeZoneInfo.Utc);
        var segment = Assert.Single(segments);
        Assert.Equal(2100, segment.Seconds);
        Assert.Equal(Day, segment.Start); Assert.Equal(Day.AddMinutes(35), segment.End);
        Assert.Equal(8, segment.SourceObservationIds.Count);
    }

    /// <summary>Keeps the inferred midpoint explicit while merging continuous work on either side.</summary>
    [Fact]
    public void MidpointSwitch_PreservesTwoSevenMinuteThirtySecondSegmentsAndUncertainBoundary()
    {
        var evidence = Observations(0, 5, 10, 15);
        var segments = TimesheetIntervalCalculator.Calculate(evidence, Inference(evidence, "A", "A", "B", "B"), [], TimeZoneInfo.Utc);
        Assert.Equal(2, segments.Count);
        Assert.All(segments, s => { Assert.Equal(450, s.Seconds); Assert.Contains(Day.AddMinutes(7.5), s.EstimatedBoundaries!); });
        Assert.Equal(segments[0].End, segments[1].Start);
        Assert.Equal(900, segments.Sum(s => s.Seconds));
    }

    /// <summary>Separates objectives sharing an application and retains interruptions in resumed work.</summary>
    [Fact]
    public void OneApplicationCanContainTwoObjectives_AndAResumedTaskKeepsSeparateSegments()
    {
        var evidence = Observations(0, 5, 10, 15, 20).Select(o => o with { Application = "Editor" }).ToList();
        var segments = TimesheetIntervalCalculator.Calculate(evidence, Inference(evidence, "A", "B", "B", "A", "A"), [], TimeZoneInfo.Utc);
        Assert.Equal(2, segments.Count(s => s.TaskId == "A"));
        Assert.Equal(1200, segments.Sum(s => s.Seconds));
        Assert.True(segments.Where(s => s.TaskId == "A").Sum(s => s.Seconds) < 1200);
    }

    /// <summary>Applies the cadence-based interpolation limit without consulting input activity.</summary>
    /// <param name="minutes">Elapsed time between the two observations.</param>
    /// <param name="allocated">Whether the separation is eligible for estimated work.</param>
    [Theory]
    [InlineData(10.5, true)]
    [InlineData(10.5001, false)]
    public void InterpolationLimit_HasNoIdleDependency(double minutes, bool allocated)
    {
        var evidence = Observations(0, 5); evidence[1] = evidence[1] with { ObservedAt = Day.AddMinutes(minutes) };
        var segment = Assert.Single(TimesheetIntervalCalculator.Calculate(evidence, Inference(evidence), [], TimeZoneInfo.Utc));
        Assert.Equal(allocated, segment.Allocated);
        Assert.Equal(allocated ? "continuous" : "unallocated", segment.Basis);
        Assert.Equal(minutes * 60, segment.Seconds, 5);
    }

    /// <summary>Retains unknown evidence as visible coverage rather than inventing work.</summary>
    [Fact]
    public void UnknownAndUncertainPairs_AreVisibleUnallocatedCoverage()
    {
        var evidence = Observations(0, 5, 10);
        var inference = Inference(evidence, "A", null, "B");
        var segment = Assert.Single(TimesheetIntervalCalculator.Calculate(evidence, inference, [], TimeZoneInfo.Utc));
        Assert.Equal("unallocated", segment.Basis); Assert.Equal(600, segment.Seconds);
    }

    /// <summary>Does not attach a default capture interval to a point of evidence.</summary>
    [Fact]
    public void SingleOrDuplicateObservation_DoesNotInventTimeBeforeOrAfter()
    {
        var evidence = Observations(0);
        Assert.Empty(TimesheetIntervalCalculator.Calculate(evidence, Inference(evidence), [], TimeZoneInfo.Utc));
        Assert.Empty(TimesheetIntervalCalculator.Calculate([evidence[0], evidence[0]], Inference(evidence), [], TimeZoneInfo.Utc));
    }

    /// <summary>Separates known unavailable time and uncovered time across a recorded session break.</summary>
    /// <param name="unavailable">The persisted signal ending availability.</param>
    /// <param name="resume">The subsequent availability or runtime boundary signal.</param>
    [Theory]
    [InlineData("locked", "unlocked")]
    [InlineData("suspended", "resumed")]
    [InlineData("paused", "started")]
    [InlineData("runtime_stop", "runtime_start")]
    public void SessionBreaks_DoNotAttributeEitherSideOfAStalePair(string unavailable, string resume)
    {
        var evidence = Observations(0, 10);
        var events = new[] { new TimesheetSessionEvent("a", "device", Day.AddMinutes(2), unavailable),
            new TimesheetSessionEvent("b", "device", Day.AddMinutes(8), resume) };
        var segments = TimesheetIntervalCalculator.Calculate(evidence, Inference(evidence), events, TimeZoneInfo.Utc);
        Assert.DoesNotContain(segments, s => s.Allocated);
        Assert.Equal(resume == "runtime_start" ? 480 : 360, segments.Where(s => s.Basis == "unavailable").Sum(s => s.Seconds));
        Assert.Equal(600, segments.Sum(s => s.Seconds));
    }

    /// <summary>Maintains independent tracking, lock and power availability reasons.</summary>
    [Fact]
    public void UnlockDoesNotCancelASeparateTrackingPause()
    {
        var evidence = Observations(0, 5);
        var events = new[] { new TimesheetSessionEvent("p", "device", Day.AddMinutes(-2), "paused"),
            new TimesheetSessionEvent("l", "device", Day.AddMinutes(-1), "locked"), new TimesheetSessionEvent("u", "device", Day.AddMinutes(2), "unlocked") };
        Assert.All(TimesheetIntervalCalculator.Calculate(evidence, Inference(evidence), events, TimeZoneInfo.Utc), s => Assert.Equal("unavailable", s.Basis));
    }

    /// <summary>Counts a simultaneous span once and requires an explicit device attribution.</summary>
    [Fact]
    public void CompetingInstallations_CountTheUtcSpanOnceAndRetainAlternatives()
    {
        var evidence = Observations(0, 5);
        var second = evidence.Select(o => o with { Id = o.Id + "b", InstallationId = "other" }).ToList();
        var a = TimesheetIntervalCalculator.Calculate(evidence, Inference(evidence), [], TimeZoneInfo.Utc);
        var b = TimesheetIntervalCalculator.Calculate(second, Inference(second, "B", "B"), [], TimeZoneInfo.Utc);
        var overlap = Assert.Single(TimesheetIntervalCalculator.Combine(a.Concat(b).ToArray()));
        Assert.Equal(300, overlap.Seconds); Assert.Equal("needs_review", overlap.ReviewState);
        Assert.False(overlap.Allocated); Assert.Equal(2, overlap.Alternatives!.Count);
    }

    /// <summary>Uses UTC duration through daylight-saving changes and splits daily reconstruction boundaries.</summary>
    [Fact]
    public void DaylightSavingAndMidnight_UseUtcElapsedTimeWithoutCrossDayInterpolation()
    {
        var evidence = Observations(0, 5);
        var fall = new DateTimeOffset(2026, 11, 1, 5, 58, 0, TimeSpan.Zero);
        evidence[0] = evidence[0] with { ObservedAt = fall };
        evidence[1] = evidence[1] with { ObservedAt = fall.AddMinutes(5) };
        Assert.Equal(300, Assert.Single(TimesheetIntervalCalculator.Calculate(evidence, Inference(evidence), [],
            TimeZoneInfo.FindSystemTimeZoneById("America/New_York"))).Seconds);
        evidence[0] = evidence[0] with { ObservedAt = new DateTimeOffset(2026, 10, 21, 23, 58, 0, TimeSpan.Zero) };
        evidence[1] = evidence[1] with { ObservedAt = evidence[0].ObservedAt.AddMinutes(5) };
        Assert.Empty(TimesheetIntervalCalculator.Calculate(evidence, Inference(evidence), [], TimeZoneInfo.Utc));
    }

    /// <summary>Rejects incomplete or inconsistent provider output without fabricating assignments.</summary>
    /// <param name="mutation">The deliberate defect introduced into the otherwise valid graph.</param>
    [Theory]
    [InlineData("missing_assignment")]
    [InlineData("missing_transition")]
    [InlineData("duplicate_task")]
    [InlineData("unknown_reference")]
    [InlineData("incompatible_relation")]
    [InlineData("extra_property")]
    public void ParserRejectsIncompleteOrInconsistentGraphs(string mutation)
    {
        var evidence = Observations(0, 5);
        var root = JsonSerializer.SerializeToNode(new
        {
            tasks = new[] { new { id = "A", title = "Read specification", project = "", summary = "Read a technical specification.", evidenceObservationIds = new[] { "obs-0", "obs-1" } } },
            assignments = new[] { new { observationId = "obs-0", taskId = "A" }, new { observationId = "obs-1", taskId = "A" } },
            transitions = new[] { new { fromObservationId = "obs-0", toObservationId = "obs-1", relation = "continuous" } }
        })!;
        var good = root.ToJsonString();
        Assert.Single(TimesheetInferenceParser.Parse(good, evidence, "day.").Tasks);
        switch (mutation)
        {
            case "missing_assignment": root["assignments"]!.AsArray().RemoveAt(1); break;
            case "missing_transition": root["transitions"]!.AsArray().Clear(); break;
            case "duplicate_task": root["tasks"]!.AsArray().Add(root["tasks"]![0]!.DeepClone()); break;
            case "unknown_reference": root["assignments"]![0]!["taskId"] = "unknown"; break;
            case "incompatible_relation": root["transitions"]![0]!["relation"] = "switch"; break;
            case "extra_property": root["hours"] = 8; break;
        }
        Assert.Throws<System.IO.InvalidDataException>(() => TimesheetInferenceParser.Parse(root.ToJsonString(), evidence, "day."));
    }

    /// <summary>Persists optimistic edits separately from the original AI assignment graph.</summary>
    [Fact]
    public void EditsSplitConfirmAndStaleWrite_DoNotChangeAiDraftAssignments()
    {
        var evidence = Observations(0, 5, 10);
        var inference = Inference(evidence);
        var job = Job(evidence, inference);
        var segment = Assert.Single(job.Segments);
        TimesheetReview.Apply(job, new(TimesheetBatchAction.Split, ExpectedRevision: 0, Edit: new(segment.Id, Day.AddMinutes(4), segment.End)));
        Assert.Equal(2, job.Segments.Count);
        Assert.Throws<ReportExportValidationException>(() => TimesheetReview.Apply(job, new(TimesheetBatchAction.Confirm, ExpectedRevision: 0)));
        TimesheetReview.Apply(job, new(TimesheetBatchAction.Merge, ExpectedRevision: 1,
            Edit: new(job.Segments[0].Id, Day, Day.AddMinutes(10), OtherSegmentId: job.Segments[1].Id)));
        Assert.Single(job.Segments);
        TimesheetReview.Apply(job, new(TimesheetBatchAction.Confirm, ExpectedRevision: 2));
        Assert.True(job.Confirmed); Assert.Equal(3, job.Revision);
        Assert.Same(inference, job.Rows[0].Inference);
    }

    /// <summary>Allows explicit missing work and rejects overlapping corrections atomically.</summary>
    [Fact]
    public void ManualIntervalCanExtendEvidence_AndConflictingEditsLeaveTheDraftUntouched()
    {
        var evidence = Observations(0);
        var job = Job(evidence, Inference(evidence));
        TimesheetReview.Apply(job, new(TimesheetBatchAction.Edit, ExpectedRevision: 0,
            Edit: new(null, Day.AddMinutes(-5), Day.AddMinutes(10), "Read a specification", "WorkTrail")));
        Assert.Equal(900, Assert.Single(job.Segments).Seconds); Assert.Equal("manual", job.Segments[0].Basis);
        var error = Assert.Throws<ReportExportValidationException>(() => TimesheetReview.Apply(job, new(TimesheetBatchAction.Edit,
            ExpectedRevision: 1, Edit: new(null, Day, Day.AddMinutes(15), "Conflicting work"))));
        Assert.Equal("Timesheet.EditOverlap", error.MessageKey); Assert.Equal(1, job.Revision); Assert.Single(job.Segments);
    }

    /// <summary>Reconciles the same exported intervals and rounded detail billing amounts.</summary>
    [Fact]
    public void ExportBoundariesAndRoundedDetailAmounts_ReconcileExactly()
    {
        var evidence = Observations(0, 5, 10, 15);
        var job = Job(evidence, Inference(evidence, "A", "A", "B", "B"));
        var details = TimesheetExcelWriter.Details(job);
        Assert.Equal(new[] { 450d, 450d }, details.Select(d => d.Seconds));
        Assert.Equal(2.52m, details.Sum(d => TimesheetExcelWriter.DetailAmount(d, 10.07m)));
    }

    private static TimesheetJob Job(List<TimesheetObservation> evidence, TimesheetInference inference) => new()
    {
        Id = Guid.NewGuid(),
        CreatedAt = Day,
        Options = new(new(new(new(2026, 10, 21), new(2026, 10, 21), "UTC"))),
        ResultsSaved = true,
        UsageSaved = true,
        State = "completed",
        Tasks = inference.Tasks.ToList(),
        Segments = TimesheetIntervalCalculator.Calculate(evidence, inference, [], TimeZoneInfo.Utc),
        Rows = [new() { Row = new("day", new(2026, 10, 21), "day", "", Day, Day.AddMinutes(15), 0, 0, "", "", evidence.Count, State: "completed"), Evidence = evidence, Inference = inference }]
    };
}
