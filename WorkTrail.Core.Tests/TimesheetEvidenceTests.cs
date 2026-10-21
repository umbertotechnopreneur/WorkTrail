// SPDX-License-Identifier: MIT
using System;
using System.IO;
using System.Linq;
using Microsoft.Data.Sqlite;
using WorkTrail.Application;
using WorkTrail.Services;
using Xunit;

namespace WorkTrail.Core.Tests;

/// <summary>Exercises semantic provenance and checkpoints with local synthetic history only.</summary>
public sealed class TimesheetEvidenceTests : IDisposable
{
    private readonly string _directory = Path.Combine(AppContext.BaseDirectory, "timesheet-evidence-" + Guid.NewGuid().ToString("N"));
    private static readonly DateTimeOffset At = new(2026, 10, 21, 9, 0, 0, TimeSpan.Zero);
    private static TimesheetOptions Options => new(new(new(new(2026, 10, 21), new(2026, 10, 21), "UTC"), IncludeCompleteDescription: true));

    /// <summary>Checks that one capture with multiple files remains one observation even without active input.</summary>
    [Fact]
    public void MultipleImagesAndIdleSamples_ProduceOneSemanticObservation()
    {
        var store = Store();
        var source = Capture(store, 3);
        var settings = store.LoadSettings();
        store.AppendSample(new(At, 300, "idle", "editor", "Editor", "Reading", "Specification", settings.InstallationId, 0, 0));
        var observation = Assert.Single(store.ReadTimesheetObservations(Options, default));
        Assert.Equal("obs-" + source.Id, observation.Id);
        Assert.True(observation.AssumedCadence);
        store.SaveTimesheetCaptureContext(source.Id, 420, "WorkTrail");
        observation = Assert.Single(store.ReadTimesheetObservations(Options, default));
        Assert.False(observation.AssumedCadence); Assert.Equal(420, observation.CaptureIntervalSeconds);
        Assert.Equal("WorkTrail", observation.ProjectHint);
    }

    /// <summary>Retries reuse the same attempt identity without duplicating files, observations or vision work.</summary>
    [Fact]
    public void UnchangedCheckpoint_PreservesCurrentTimeAndSavedDescriptionIdempotently()
    {
        var store = Store(); var source = Capture(store, 1); var settings = store.LoadSettings();
        var checkpoint = new TimesheetUnchangedEvidence(Guid.NewGuid().ToString("N"), At.AddMinutes(5), source.Path, "Editor", "Specification");
        store.SaveTimesheetCheckpoint(checkpoint, settings); store.SaveTimesheetCheckpoint(checkpoint, settings);
        var evidence = store.ReadTimesheetObservations(Options, default);
        Assert.Equal(2, evidence.Count);
        var reused = Assert.Single(evidence, o => o.SourceKind == "unchanged_checkpoint");
        Assert.Equal(At.AddMinutes(5), reused.ObservedAt); Assert.Equal(source.Id, reused.AnalysisId);
        Assert.Equal(evidence[0].Description, reused.Description);
        Assert.Single(Directory.EnumerateFiles(settings.ScreenshotDirectory, "*.webp", SearchOption.AllDirectories));
        var requests = 0;
        store.VisitAiUsage(At.AddDays(-1), At.AddDays(1), _ => requests++, default);
        Assert.Equal(1, requests);
    }

    /// <summary>A capture without a successful semantic result cannot become reused evidence.</summary>
    [Fact]
    public void MissingAnalysisAndPrivateContext_DoNotCreateUsableReusedEvidence()
    {
        var store = Store(); var source = Capture(store, 1, analyzed: false);
        store.SaveTimesheetCheckpoint(new(Guid.NewGuid().ToString("N"), At.AddMinutes(5), source.Path, "Editor", "Specification"), store.LoadSettings());
        Assert.Empty(store.ReadTimesheetObservations(Options, default));
        var allowed = Capture(store, 1);
        store.SaveTimesheetCheckpoint(new(Guid.NewGuid().ToString("N"), At.AddMinutes(5), allowed.Path, "Editor", "Private secret"), store.LoadSettings());
        store.SaveSettings(store.LoadSettings() with { PrivacyWindowTitles = "Private" });
        Assert.DoesNotContain(store.ReadTimesheetObservations(Options, default), o => o.SourceKind == "unchanged_checkpoint");
        Assert.Null(new ScreenshotCapturePreconditionException(ScreenshotCaptureDecision.PrivacyBlocked).UnchangedEvidence);
        Assert.Null(new ScreenshotCapturePreconditionException(ScreenshotCaptureDecision.Unstable).UnchangedEvidence);
    }

    /// <summary>The existing daily request path accepts saved reading evidence regardless of active samples.</summary>
    [Fact]
    public void DailyProjectionRequiresNoInputSamples_AndWeeklyDisplayDoesNotChangeRequests()
    {
        var store = Store(); Capture(store, 1);
        var options = Options with
        {
            Grouping = TimesheetGrouping.Week,
            Sources = Options.Sources with { Options = Options.Sources.Options with { ToInclusive = new(2026, 10, 22) } }
        };
        var rows = TimesheetProjection.Build(store, options, default);
        Assert.Equal(2, rows.Count); Assert.NotNull(rows[0].Prompt);
        Assert.Equal("no_sources", rows[1].Row.State); Assert.Null(rows[1].Prompt);
        Assert.All(rows, r => Assert.Equal(0, r.Row.ActiveSeconds));
    }

    /// <summary>A full ordinary day fits a single bounded request without losing repeated observation timestamps.</summary>
    [Fact]
    public void FullDayAtFiveMinuteCadence_FitsOneRequestWithAll288Observations()
    {
        var store = Store();
        var midnight = new DateTimeOffset(2026, 10, 21, 0, 0, 0, TimeSpan.Zero);
        var source = Capture(store, 1, at: midnight);
        for (var i = 1; i < 288; i++) store.SaveTimesheetCheckpoint(new(Guid.NewGuid().ToString("N"), midnight.AddMinutes(i * 5),
            source.Path, "Editor", "Specification"), store.LoadSettings());
        var row = Assert.Single(TimesheetProjection.Build(store, Options, default));
        Assert.Equal(288, row.Evidence.Count); Assert.NotNull(row.Prompt); Assert.Null(row.FailureCode);
        Assert.True(row.InputBytes < TimesheetProjection.MaximumInputBytes);
        Assert.True(row.EstimatedInputTokens + TimesheetProjection.OutputTokens < 1_050_000);
    }

    /// <summary>Reading checkpoints preserve thirty-five minutes even when every input sample is idle.</summary>
    [Fact]
    public void ReadingCheckpointsWithOnlyIdleSamples_PreserveThirtyFiveMinutes()
    {
        var store = Store(); var source = Capture(store, 1); var settings = store.LoadSettings();
        for (var i = 1; i < 8; i++) store.SaveTimesheetCheckpoint(new(Guid.NewGuid().ToString("N"), At.AddMinutes(i * 5), source.Path,
            "Editor", "Specification"), settings);
        store.AppendSample(new(At.AddMinutes(35), 2100, "idle", "editor", "Editor", "Reading", "Specification", settings.InstallationId, 0, 0));
        var evidence = store.ReadTimesheetObservations(Options, default);
        var inference = new TimesheetInference([new("reading", "Read specification", "", "Observed reading", evidence.Select(o => o.Id).ToArray())],
            evidence.Select(o => new TimesheetAssignment(o.Id, "reading")).ToArray(), evidence.Zip(evidence.Skip(1))
                .Select(p => new TimesheetTransition(p.First.Id, p.Second.Id, "continuous")).ToArray());
        Assert.Equal(2100, Assert.Single(TimesheetIntervalCalculator.Calculate(evidence, inference, [], TimeZoneInfo.Utc)).Seconds);
    }

    private LocalStore Store()
    {
        var store = new LocalStore(_directory);
        store.SaveSettings(store.LoadSettings() with { ScreenshotDirectory = Path.Combine(_directory, "screenshots"), KeepScreenshots = true, ScreenshotIntervalMinutes = 5 });
        return store;
    }

    private static (string Id, string Path) Capture(LocalStore store, int images, bool analyzed = true, DateTimeOffset? at = null)
    {
        var capturedAt = at ?? At;
        var settings = store.LoadSettings(); var id = Guid.NewGuid().ToString("N");
        store.RegisterScreenshotCapture(id, settings.InstallationId, capturedAt, "scheduled");
        var directory = ScreenshotStorageLayout.GetDayDirectory(settings.ScreenshotDirectory, capturedAt);
        Directory.CreateDirectory(directory);
        var paths = Enumerable.Range(0, images).Select(i => Path.Combine(directory, id + "_1.0.0_scheduled_" + (i == 0 ? "active-window" : "monitor-" + i) + ".webp")).ToArray();
        foreach (var path in paths) { File.WriteAllBytes(path, [0]); File.SetLastWriteTimeUtc(path, capturedAt.UtcDateTime); }
        if (analyzed)
        {
            var usage = new AiRequestUsageRecord("attempt-" + id, id, capturedAt, capturedAt, "snapshot.scheduled", "analysis", "openai", "api.openai.com", "model",
                null, null, null, 200, null, null, images, 0, 100, new(), "completed", true, null);
            store.AppendAiAnalysisAndUsage(usage, new(capturedAt, "Editor", "Reading", "Reading a specification.", settings.InstallationId,
                string.Join(";", paths), CorrelationId: id));
        }
        return (id, paths[0]);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (!Directory.Exists(_directory)) return;
        var root = Path.GetFullPath(AppContext.BaseDirectory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var path = Path.GetFullPath(_directory);
        if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase) || (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new IOException("Unsafe timesheet fixture cleanup path.");
        Directory.Delete(path, true);
    }
}
