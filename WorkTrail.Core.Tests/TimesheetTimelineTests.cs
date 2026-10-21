// SPDX-License-Identifier: MIT
using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using WorkTrail.Application;
using WorkTrail.Services;
using Xunit;

namespace WorkTrail.Core.Tests;

/// <summary>Checks offline calendar projections against synthetic saved drafts, with forbidden provider transport.</summary>
public sealed class TimesheetTimelineTests : IDisposable
{
    private readonly string _directory = Path.Combine(AppContext.BaseDirectory, "timeline-" + Guid.NewGuid().ToString("N"));
    private readonly DateOnly _date = new(2026, 10, 21);
    private readonly LocalStore _store;
    private readonly HttpClient _http = new(new RejectTransport());

    /// <summary>Creates one isolated source of saved synthetic drafts.</summary>
    public TimesheetTimelineTests() => _store = new LocalStore(_directory);

    /// <summary>Opening an empty calendar neither calls the provider nor invents work.</summary>
    [Fact]
    public async Task EmptyHistory_ReturnsExplicitMissingDayWithoutProviderWork()
    {
        var view = await Read();
        Assert.Empty(view.Timeline!);
        Assert.Equal("not_generated", Assert.Single(view.Days!).State);
        Assert.False(Directory.Exists(Path.Combine(_directory, "timesheet-batches")));
    }

    /// <summary>The latest completed draft supplies the current local corrections without mutating the source.</summary>
    [Fact]
    public async Task LatestDraft_UsesEditedTitleAndReviewStateWithoutChangingSavedFiles()
    {
        Save(Job(_date, "Old title", 0));
        var latest = Job(_date, "Corrected title", 1);
        latest.Revision = 4; latest.Confirmed = true;
        latest.Segments[0] = latest.Segments[0] with { Basis = "manual", ReviewState = "reviewed" };
        var path = Save(latest);
        var before = File.ReadAllText(path);
        var span = Assert.Single((await Read()).Timeline!);
        Assert.Equal(latest.Id, span.JobId);
        Assert.Equal("Corrected title", span.Title);
        Assert.Equal("manual", span.Basis);
        Assert.Equal("reviewed", span.ReviewState);
        Assert.True(span.Confirmed);
        Assert.Equal(before, File.ReadAllText(path));
    }

    /// <summary>An unfinished or failed regeneration leaves the last saved reconstruction visible.</summary>
    [Theory]
    [InlineData("pending", false)]
    [InlineData("failed", true)]
    public async Task FailedOrPendingNewDraft_DoesNotHideCompletedIntervals(string state, bool saved)
    {
        var completed = Job(_date, "Retained", 0); Save(completed);
        var retry = Job(_date, "Retry", 1);
        retry.ResultsSaved = saved; retry.State = state; retry.Segments.Clear();
        retry.Rows[0].Row = retry.Rows[0].Row with { State = state }; Save(retry);
        Assert.Equal(completed.Id, Assert.Single((await Read()).Timeline!).JobId);
    }

    /// <summary>Seven days can use independent drafts without duplicating overlapping regenerations.</summary>
    [Fact]
    public async Task Week_SelectsOneReconstructionPerDay()
    {
        for (var i = 0; i < 7; i++)
        {
            Save(Job(_date.AddDays(i), "Earlier", 0));
            Save(Job(_date.AddDays(i), "Current", 1));
        }
        var view = await Read(_date.AddDays(6));
        Assert.Equal(7, view.Timeline!.Count);
        Assert.All(view.Timeline, span => Assert.Equal("Current", span.Title));
        Assert.Equal(7, view.Days!.Count);
    }

    /// <summary>A reviewed interval crossing midnight is clipped to both display days without losing duration.</summary>
    [Fact]
    public async Task Midnight_ClipsSavedSpanAndPreservesElapsedTime()
    {
        var job = Job(_date, "Late work", 0);
        var start = Local(_date, 23).AddMinutes(30);
        var end = Local(_date.AddDays(1), 0).AddMinutes(30);
        job.Options = job.Options with
        {
            Sources = job.Options.Sources with
            { Options = job.Options.Sources.Options with { ToInclusive = _date.AddDays(1) } }
        };
        job.Segments[0] = job.Segments[0] with { Start = start, End = end };
        Save(job);
        var spans = (await Read(_date.AddDays(1))).Timeline!;
        Assert.Equal(2, spans.Count);
        Assert.Equal(spans[0].End, spans[1].Start);
        Assert.Equal((end - start).TotalSeconds, spans.Sum(s => (s.End - s.Start).TotalSeconds));
        Assert.All(spans, s => { Assert.True(s.Start >= s.DayStart); Assert.True(s.End <= s.DayEnd); });
    }

    /// <summary>Large days use bounded pages and a revision marker for consistent client assembly.</summary>
    [Fact]
    public async Task ManyIntervals_ArePagedWithAnEditSensitiveRevision()
    {
        var job = Job(_date, "Work", 0);
        var first = Local(_date, 0);
        job.Segments = Enumerable.Range(0, 230).Select(i => new TimesheetSegment(i.ToString(),
            first.AddMinutes(i * 2), first.AddMinutes(i * 2 + 1), "task", "Project", [], "continuous")).ToList();
        Save(job);
        var page0 = await Read(); var page1 = await Read(page: 1);
        Assert.Equal(200, page0.Timeline!.Count); Assert.Equal(30, page1.Timeline!.Count);
        Assert.Equal(230, page0.SegmentCount); Assert.Equal(page0.TimelineRevision, page1.TimelineRevision);
        Assert.Empty(page0.Timeline.Select(s => s.SegmentId).Intersect(page1.Timeline.Select(s => s.SegmentId)));
        job.Revision++; Save(job);
        Assert.NotEqual(page0.TimelineRevision, (await Read()).TimelineRevision);
    }

    /// <summary>Legacy input-based reports never appear as reconstructed work.</summary>
    [Fact]
    public async Task HistoricalSnapshots_AreExcluded()
    {
        var job = Job(_date, "Legacy", 0); job.Version = 2; Save(job);
        Assert.Empty((await Read()).Timeline!);
    }

    /// <summary>Timeline reads reject unbounded date ranges before consulting saved data.</summary>
    [Fact]
    public async Task MoreThanOneWeek_IsRejected() =>
        await Assert.ThrowsAsync<ArgumentException>(() => Read(_date.AddDays(7)));

    private Task<TimesheetBatchView> Read(DateOnly? to = null, int page = 0) =>
        new TimesheetBatchService(_store, _http).ExecuteAsync(new(TimesheetBatchAction.Timeline,
            From: _date, ToInclusive: to ?? _date, SegmentPage: page), _store.LoadSettings(),
            (_, _) => throw new InvalidOperationException("Timeline must not reserve usage."),
            (_, _) => throw new InvalidOperationException("Timeline must not save usage."), default);

    private TimesheetJob Job(DateOnly date, string title, int generation) => new()
    {
        Id = Guid.NewGuid(),
        CreatedAt = Local(_date, 0).AddMinutes(generation),
        Options = new(new(new(date, date, TimeZoneInfo.Local.Id))),
        ResultsSaved = true,
        UsageSaved = true,
        State = "completed",
        Tasks = [new("task", title, "Project", "", [])],
        Segments = [new("segment", Local(date, 9), Local(date, 10), "task", "Project", [], "continuous")],
        Rows = [new() { Row = new("day", date, "day", "", Local(date, 9), Local(date, 10), 0, 0, "", "", 2, State: "completed") }]
    };

    private static DateTimeOffset Local(DateOnly date, int hour)
    {
        var time = date.ToDateTime(new TimeOnly(hour, 0));
        return new(time, TimeZoneInfo.Local.GetUtcOffset(time));
    }

    // The fixture owns these JSON files and never reads the user's activity history.
    private string Save(TimesheetJob job)
    {
        var directory = Path.Combine(_directory, "timesheet-batches"); Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, job.Id.ToString("N") + ".json");
        File.WriteAllText(path, JsonSerializer.Serialize(job, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        return path;
    }

    private sealed class RejectTransport : HttpMessageHandler
    {
        /// <inheritdoc />
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Timeline must stay offline.");
    }

    /// <summary>Removes only this fixture's local disposable history.</summary>
    public void Dispose()
    {
        _http.Dispose(); SqliteConnection.ClearAllPools();
        var root = Path.GetFullPath(AppContext.BaseDirectory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var path = Path.GetFullPath(_directory);
        if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new IOException("Unsafe fixture path.");
        if (Directory.Exists(path)) Directory.Delete(path, true);
    }
}
