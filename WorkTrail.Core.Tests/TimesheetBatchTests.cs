// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using Microsoft.Data.Sqlite;
using WorkTrail.Application;
using WorkTrail.Services;
using Xunit;

namespace WorkTrail.Core.Tests;

/// <summary>Checks measured time, actual batch protocol, recovery and bounded Excel output with synthetic data.</summary>
public sealed class TimesheetBatchTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "WorkTrail-timesheet-tests-" + Guid.NewGuid().ToString("N"));
    private static readonly DateTimeOffset Day = new(2026, 9, 21, 0, 0, 0, TimeSpan.Zero);
    private static TimesheetOptions Options => new(new(new(DateOnly.FromDateTime(Day.DateTime), DateOnly.FromDateTime(Day.DateTime), "UTC")), HourlyRate: 40m);

    public TimesheetBatchTests() => Directory.CreateDirectory(_directory);

    // start and minutes define an exact UTC interval, while project and state define attribution.
    private static ActivitySample Sample(DateTimeOffset start, int minutes, string project = "Project A", string state = "active") =>
        new(start.AddMinutes(minutes), minutes * 60, state, "editor", "Editor", "", "private title", "device", 0, 0,
            new Dictionary<string, string> { [ActivityAttributeKeys.SpanLabel] = project });

    [Fact]
    public void DayParts_SplitAtNoon_UnionOverlaps_AndExcludeUnobservedGaps()
    {
        var samples = new[] { Sample(Day.AddHours(11.5), 60), Sample(Day.AddHours(11.75), 30),
            Sample(Day.AddHours(13), 30, state: "idle"), Sample(Day.AddHours(14), 60) };
        var split = TimesheetProjection.Aggregate(samples, Day, Day.AddDays(1), TimeZoneInfo.Utc, false, false, CancellationToken.None);
        Assert.Equal(2, split.Count);
        Assert.Equal(1800, split.Single(row => row.Part == "morning").ActiveSeconds);
        Assert.Equal(5400, split.Single(row => row.Part == "afternoon").ActiveSeconds);
        Assert.Equal(1800, split.Sum(row => row.IdleSeconds));
        Assert.All(split, row => Assert.Empty(row.References));
        var merged = Assert.Single(TimesheetProjection.Aggregate(samples, Day, Day.AddDays(1), TimeZoneInfo.Utc, true, true, CancellationToken.None));
        Assert.Equal(split.Sum(row => row.ActiveSeconds), merged.ActiveSeconds);
        Assert.Equal(split.Sum(row => row.IdleSeconds), merged.IdleSeconds);
        Assert.Equal("private title", merged.References);
    }

    [Fact]
    public void MidnightAndDaylightSaving_AllocateActualElapsedSeconds()
    {
        var midnight = TimesheetProjection.Aggregate([Sample(Day.AddHours(23.5), 60)], Day, Day.AddDays(2), TimeZoneInfo.Utc, true, false, CancellationToken.None);
        Assert.Equal(new[] { 1800d, 1800d }, midnight.Select(row => row.ActiveSeconds));
        var zone = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");
        var dst = new DateTimeOffset(2026, 11, 1, 5, 30, 0, TimeSpan.Zero);
        var repeated = Assert.Single(TimesheetProjection.Aggregate([Sample(dst, 120)], dst.AddHours(-5), dst.AddHours(20), zone, false, false, CancellationToken.None));
        Assert.Equal(7200, repeated.ActiveSeconds);
        Assert.Equal("morning", repeated.Part);
    }

    [Fact]
    public void ConflictingProjects_AreRejectedInsteadOfDoubleBilled()
    {
        var error = Assert.Throws<ReportExportValidationException>(() => TimesheetProjection.Aggregate(
            [Sample(Day.AddHours(9), 60, "A"), Sample(Day.AddHours(9.5), 60, "B")], Day, Day.AddDays(1), TimeZoneInfo.Utc, true, false, CancellationToken.None));
        Assert.Equal("Timesheet.Overlap", error.MessageKey);
    }

    [Fact]
    public void BatchResults_MapUnorderedCustomIds_AndKeepPartialFailuresVisible()
    {
        var job = Job(3);
        TimesheetBatchService.ReadResults(job, Result("row-00002", "Second") + "\n" + Result("row-00001", "First"),
            "{\"custom_id\":\"row-00003\",\"response\":null,\"error\":{\"code\":\"batch_expired\"}}");
        Assert.Equal("First", job.Rows[0].Row.Description);
        Assert.Equal("Second", job.Rows[1].Row.Description);
        Assert.Equal("failed", job.Rows[2].Row.State);
        Assert.Equal(12, job.Rows[0].Usage!.InputTokens);
        Assert.Throws<InvalidDataException>(() => TimesheetBatchService.ReadResults(Job(1), Result("unknown", "Wrong"), ""));
        Assert.Throws<InvalidDataException>(() => TimesheetBatchService.ReadResults(Job(1), Result("row-00001", "A") + "\n" + Result("row-00001", "B"), ""));
    }

    [Fact]
    public void IncompleteOrRefusedResponses_DoNotBecomeCompletedDescriptions()
    {
        var job = Job(2);
        TimesheetBatchService.ReadResults(job, Result("row-00001", "Cut off").Replace("\"completed\"", "\"incomplete\"", StringComparison.Ordinal), "");
        Assert.All(job.Rows, row => { Assert.Equal("failed", row.Row.State); Assert.Empty(row.Row.Description); });
    }

    [Fact]
    public async Task Client_SubmitsActualBatchJsonl_AndDoesNotUseSynchronousResponses()
    {
        var handler = new Transport();
        using var http = new HttpClient(handler);
        var client = new OpenAiTimesheetBatchClient("synthetic-test-key", http);
        var job = Job(2);
        var file = await client.UploadAsync(job.Rows, CancellationToken.None);
        using var batch = await client.CreateAsync(file, job.Id, CancellationToken.None);
        Assert.Equal("batch_test", batch.RootElement.GetProperty("id").GetString());
        Assert.Equal(new[] { "/v1/files", "/v1/batches" }, handler.Paths);
        Assert.Contains("\"custom_id\":\"row-00001\"", handler.Bodies[0]);
        Assert.Contains("\"model\":\"gpt-6-luna\"", handler.Bodies[0]);
        Assert.Contains("\"store\":false", handler.Bodies[0]);
        Assert.Contains("\"completion_window\":\"24h\"", handler.Bodies[1]);
        Assert.DoesNotContain("synthetic-test-key", string.Join("", handler.Bodies));
    }

    [Fact]
    public async Task RefreshAfterRestart_RecoversLostSubmission_AndPersistsBeforeRemoteCleanup()
    {
        var store = new LocalStore(Path.Combine(_directory, "data"));
        var keyName = "WORKTRAIL_TEST_" + Guid.NewGuid().ToString("N");
        Environment.SetEnvironmentVariable(keyName, "synthetic-test-key", EnvironmentVariableTarget.Process);
        try
        {
            var job = Job(2); job.State = "submitting"; job.InputFile = "file_input"; job.ApiKeyName = keyName; job.Reserved = true;
            job.Rows[0].Prompt = "Private selected source text";
            var snapshot = JsonSerializer.Serialize(job, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            Assert.DoesNotContain("Private selected source text", snapshot);
            Assert.DoesNotContain("\"prompt\"", snapshot);
            var jobs = Path.Combine(store.DataDirectory, "timesheet-batches"); Directory.CreateDirectory(jobs);
            File.WriteAllText(Path.Combine(jobs, job.Id.ToString("N") + ".json"), JsonSerializer.Serialize(job, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
            var handler = new Transport(job.Id);
            using var http = new HttpClient(handler);
            var reconciled = 0;
            Task Reserve(TimesheetJob saved, CancellationToken token) => throw new InvalidOperationException("Recovery must never reserve new requests.");
            Task Reconcile(TimesheetJob saved, CancellationToken token) { Assert.True(saved.ResultsSaved); reconciled++; return Task.CompletedTask; }
            var service = new TimesheetBatchService(store, http);
            var result = await service.ExecuteAsync(new(TimesheetBatchAction.Refresh, JobId: job.Id), new AppSettings(), Reserve, Reconcile, CancellationToken.None);
            Assert.Equal(2, result.Selected!.CompletedCount);
            Assert.Equal(1, reconciled);
            Assert.True(service.Load(job.Id).ResultsSaved);
            Assert.DoesNotContain("POST", handler.Methods);
            Assert.Contains("/v1/files/file_output/content", handler.Paths);
            Assert.Equal(2, handler.Methods.Count(method => method == "DELETE"));
            await new TimesheetBatchService(store, http).ExecuteAsync(new(TimesheetBatchAction.Refresh, JobId: job.Id), new AppSettings(), Reserve, Reconcile, CancellationToken.None);
            Assert.Equal(2, handler.Methods.Count(method => method == "DELETE"));
        }
        finally { Environment.SetEnvironmentVariable(keyName, null, EnvironmentVariableTarget.Process); }
    }

    [Fact]
    public void Excel_UsesNumericDurations_TrustedFormulas_InertText_AndTenRowsPerPreviewSheet()
    {
        var job = Job(14); job.Options = Options with { Client = "=HYPERLINK(\"bad\")", SeparateMonths = true };
        job.Rows[0].Row = job.Rows[0].Row with { Description = "=1+1", State = "completed" };
        var path = TimesheetExcelWriter.Write(job, Path.Combine(_directory, "preview.xlsx"), false, CancellationToken.None, 10);
        using var zip = ZipFile.OpenRead(path);
        XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        foreach (var entry in zip.Entries.Where(entry => entry.FullName.StartsWith("xl/worksheets/", StringComparison.Ordinal)))
        {
            using var stream = entry.Open(); var xml = XDocument.Load(stream);
            var filter = xml.Descendants(ns + "autoFilter").Single().Attribute("ref")!.Value;
            Assert.EndsWith("20", filter);
            Assert.DoesNotContain(xml.Descendants(ns + "f"), formula => formula.Value.Contains("HYPERLINK", StringComparison.Ordinal));
        }
        using var sheet = zip.GetEntry("xl/worksheets/sheet1.xml")!.Open();
        var cells = XDocument.Load(sheet).Descendants(ns + "c").ToDictionary(cell => cell.Attribute("r")!.Value);
        Assert.Equal("inlineStr", cells["D11"].Attribute("t")!.Value);
        Assert.Equal("=1+1", cells["F11"].Descendants(ns + "t").Single().Value);
        Assert.Equal(1d / 24d, double.Parse(cells["C11"].Element(ns + "v")!.Value, System.Globalization.CultureInfo.InvariantCulture), 10);
        Assert.Equal("SUM(C11:C20)", cells["A7"].Element(ns + "f")!.Value);
        Assert.Equal("A7*3", cells["F7"].Element(ns + "f")!.Value);
        Assert.Equal("ROUND(C11*24*$C$5,2)", cells["I11"].Element(ns + "f")!.Value);
    }

    [Fact]
    public void TemporaryAnalyticalPreview_IsExcel_Bounded_AndDoesNotOverwriteAUserFile()
    {
        var store = new LocalStore(Path.Combine(_directory, "data"));
        var options = Options.Sources.Options with { ToInclusive = Options.Sources.Options.From.AddDays(20), Format = ReportExportFormat.Json, IncludeCaptures = false };
        var result = ReportFilePreviewService.Create(store, new(options), CancellationToken.None);
        try
        {
            Assert.EndsWith(".xlsx", result.Path);
            using var zip = ZipFile.OpenRead(result.Path);
            XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            foreach (var entry in zip.Entries.Where(entry => entry.FullName.StartsWith("xl/worksheets/", StringComparison.Ordinal)))
            {
                using var stream = entry.Open();
                Assert.True(XDocument.Load(stream).Descendants(ns + "row").Count() <= 11);
            }
        }
        finally { File.Delete(result.Path); }
    }

    [Fact]
    public void ProviderGate_RefusesOtherHostsAndDisabledAiBeforePaidSubmission()
    {
        Assert.Throws<ReportExportValidationException>(() => TimesheetBatchService.ValidateProvider(new AppSettings(), true));
        Assert.Throws<ReportExportValidationException>(() => TimesheetBatchService.ValidateProvider(new AppSettings(OpenAiEnabled: true, AiEndpoint: "https://other.example/v1/responses"), true));
        TimesheetBatchService.ValidateProvider(new AppSettings(OpenAiEnabled: true), true);
    }

    /// <summary>A definite create rejection releases reservations and permits an explicit fresh submission.</summary>
    /// <param name="status">A client error that confirms the create request was rejected.</param>
    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    public async Task RejectedSubmission_IsFailedAndReleasesQuotaBeforeRetry(HttpStatusCode status)
    {
        var store = SourceStore();
        using var credential = new TestCredential();
        var reject = true;
        using var http = new HttpClient(new Transport(overrideResponse: (request, _) =>
            reject && request.Method == HttpMethod.Post && request.RequestUri!.AbsolutePath == "/v1/batches"
                ? new HttpResponseMessage(status) : null));
        var service = new TimesheetBatchService(store, http);
        var settings = new AppSettings(OpenAiEnabled: true, AiApiKeyName: credential.Name);
        Task Reserve(TimesheetJob job, CancellationToken token)
        {
            store.SaveTimesheetUsage(job.Rows.Select(row => Usage(row.Row.Id) with
            { AttemptId = job.Id.ToString("N") + "." + row.Row.Id, OccurredAt = job.CreatedAt }).ToArray(), token);
            return Task.CompletedTask;
        }
        Task Reconcile(TimesheetJob job, CancellationToken token)
        {
            Assert.Null(job.BatchId);
            store.ReleaseTimesheetReservations(job.Rows.Select(row => job.Id.ToString("N") + "." + row.Row.Id).ToArray(), token);
            return Task.CompletedTask;
        }
        await Assert.ThrowsAsync<ReportExportValidationException>(() => service.ExecuteAsync(
            new(TimesheetBatchAction.Start, TextSources()), settings, Reserve, Reconcile, CancellationToken.None));
        var listed = await service.ExecuteAsync(new(TimesheetBatchAction.List), settings, Reserve, Reconcile, CancellationToken.None);
        Assert.Equal("failed", listed.Selected!.State);
        Assert.True(listed.Selected.ResultsSaved);
        Assert.Empty(ReadUsage(store));
        reject = false;
        var retried = await service.ExecuteAsync(new(TimesheetBatchAction.Start, TextSources()), settings, Reserve, Reconcile, CancellationToken.None);
        Assert.Equal("completed", retried.Selected!.State);
        Assert.Equal(2, retried.Jobs.Count);
        Assert.Single(ReadUsage(store));
    }

    /// <summary>An uncertain create result retains its identity and blocks duplicate paid submissions.</summary>
    /// <param name="status">An ambiguous timeout or server response.</param>
    [Theory]
    [InlineData(HttpStatusCode.RequestTimeout)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task UncertainSubmission_RemainsRecoverableWithoutResubmission(HttpStatusCode status)
    {
        var store = SourceStore();
        using var credential = new TestCredential();
        var handler = new Transport(overrideResponse: (request, _) =>
            request.Method == HttpMethod.Post && request.RequestUri!.AbsolutePath == "/v1/batches"
                ? new HttpResponseMessage(status) : null);
        using var http = new HttpClient(handler);
        var service = new TimesheetBatchService(store, http);
        var settings = new AppSettings(OpenAiEnabled: true, AiApiKeyName: credential.Name);
        await Assert.ThrowsAsync<ReportExportValidationException>(() => service.ExecuteAsync(
            new(TimesheetBatchAction.Start, TextSources()), settings, NoUsageChanges, NoUsageChanges, CancellationToken.None));
        var listed = await service.ExecuteAsync(new(TimesheetBatchAction.List), settings, NoUsageChanges, NoUsageChanges, CancellationToken.None);
        Assert.Equal("submitting", listed.Selected!.State);
        Assert.False(listed.Selected.ResultsSaved);
        var error = await Assert.ThrowsAsync<ReportExportValidationException>(() => service.ExecuteAsync(
            new(TimesheetBatchAction.Start, TextSources()), settings, NoUsageChanges, NoUsageChanges, CancellationToken.None));
        Assert.Equal("Timesheet.Unconfirmed", error.MessageKey);
        Assert.Single(handler.Paths.Where((path, index) => path == "/v1/batches" && handler.Methods[index] == "POST"));
    }

    /// <summary>Every archived report remains reachable after the old two-hundred-job limit.</summary>
    [Fact]
    public async Task Archive_PagesAllJobsAndBoundsTheWirePayload()
    {
        var store = new LocalStore(Path.Combine(_directory, "data"));
        for (var index = 0; index < 215; index++)
        {
            var job = Job(1); job.CreatedAt = Day.AddSeconds(index); job.State = "failed";
            job.ResultsSaved = true; job.UsageSaved = true;
            SaveJob(store, job);
        }
        var service = new TimesheetBatchService(store);
        var seen = new HashSet<Guid>();
        var page = 0;
        TimesheetBatchView view;
        do
        {
            view = await service.ExecuteAsync(new(TimesheetBatchAction.List, Page: page), new(), NoUsageChanges, NoUsageChanges, CancellationToken.None);
            Assert.InRange(view.Jobs.Count, 1, 30);
            Assert.Equal(page > 0, view.HasPreviousPage);
            Assert.Equal(view.Jobs[0].Id, view.Selected!.Id);
            Assert.All(view.Jobs, job => Assert.True(seen.Add(job.Id)));
            using var wire = JsonDocument.Parse(JsonSerializer.Serialize(view, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
            Assert.False(wire.RootElement.TryGetProperty("rows", out _));
            page++;
        } while (view.HasNextPage);
        Assert.Equal(215, seen.Count);
        Assert.Equal(5, view.Jobs.Count);
        var clamped = await service.ExecuteAsync(new(TimesheetBatchAction.List, Page: 10_000), new(), NoUsageChanges, NoUsageChanges, CancellationToken.None);
        Assert.Equal(view.Page, clamped.Page);
        Assert.NotNull(clamped.Selected);
    }

    /// <summary>Recovery includes jobs outside the visible page and rotates past repeated failures.</summary>
    [Fact]
    public async Task Recovery_IsIndependentOfUiSelectionAndRotatesTheBacklog()
    {
        var store = new LocalStore(Path.Combine(_directory, "data"));
        var pending = new HashSet<Guid>();
        for (var index = 0; index < 47; index++)
        {
            var job = Job(1); job.CreatedAt = Day.AddSeconds(index);
            if (index < 12) pending.Add(job.Id);
            else { job.State = "failed"; job.ResultsSaved = true; job.UsageSaved = true; }
            SaveJob(store, job);
        }
        var handler = new Transport();
        using var http = new HttpClient(handler);
        var service = new TimesheetBatchService(store, http);
        var visible = await service.ExecuteAsync(new(TimesheetBatchAction.List), new(), NoUsageChanges, NoUsageChanges, CancellationToken.None);
        Assert.DoesNotContain(visible.Jobs, job => pending.Contains(job.Id));
        var first = service.RecoveryCandidates(CancellationToken.None);
        var second = service.RecoveryCandidates(CancellationToken.None);
        Assert.Equal(8, first.Count);
        Assert.Equal(12, first.Concat(second).Distinct().Count());
        Assert.All(first.Concat(second), id => Assert.Contains(id, pending));
        // A prepared snapshot cannot have submitted paid work and can be reconciled without a configured API key.
        var recovered = await service.ExecuteAsync(new(TimesheetBatchAction.Refresh, JobId: first[0]), new(), NoUsageChanges, NoUsageChanges, CancellationToken.None);
        Assert.True(recovered.Selected!.ResultsSaved);
        Assert.False(recovered.Selected.CleanupPending);
        Assert.Empty(handler.Paths);
    }

    /// <summary>The application recovers saved jobs while no export window or UI poll exists.</summary>
    [Fact]
    public async Task RuntimeRecovery_ReconcilesWithoutAnExportWindow()
    {
        var store = new LocalStore(Path.Combine(_directory, "data"));
        var screenshots = Path.Combine(_directory, "screenshots");
        Directory.CreateDirectory(screenshots);
        store.SaveSettings(store.LoadSettings() with { ScreenshotDirectory = screenshots });
        var job = Job(1); job.Reserved = true;
        SaveJob(store, job);
        store.SaveTimesheetUsage([Usage(job.Id.ToString("N") + "." + job.Rows[0].Row.Id)], CancellationToken.None);
        await using var application = new WorkTrailApplication(store, new UtilityService(), new TrackingDomainService(store),
            DispatchProxy.Create<IScreenCaptureService, ReportExportTests.NoCalls>(), new FakeHardwareTelemetryService(),
            DispatchProxy.Create<IAiAnalysisService, ReportExportTests.NoCalls>(), new StartupService(), new BuildInformationService());
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        TimesheetJobInfo? recovered;
        do
        {
            var result = await application.ManageTimesheetBatchAsync(new(TimesheetBatchAction.List, JobId: job.Id), deadline.Token);
            Assert.True(result.Succeeded);
            recovered = result.Value!.Selected;
            if (recovered is not { ResultsSaved: true, CleanupPending: false }) await Task.Delay(20, deadline.Token);
        } while (recovered is not { ResultsSaved: true, CleanupPending: false });
        Assert.Equal("failed", recovered.State);
        Assert.Empty(ReadUsage(store));
    }

    /// <summary>A terminal remote status alone cannot enable export when result download failed.</summary>
    [Fact]
    public async Task DownloadFailure_KeepsTerminalJobPendingUntilResultsAreDurable()
    {
        var store = new LocalStore(Path.Combine(_directory, "data"));
        using var credential = new TestCredential();
        var job = Job(2); job.State = "in_progress"; job.BatchId = "batch_test";
        job.InputFile = "file_input"; job.ApiKeyName = credential.Name; SaveJob(store, job);
        var failDownload = true;
        using var http = new HttpClient(new Transport(overrideResponse: (request, _) =>
            failDownload && request.RequestUri!.AbsolutePath.EndsWith("/content", StringComparison.Ordinal)
                ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) : null));
        var service = new TimesheetBatchService(store, http);
        await Assert.ThrowsAsync<ReportExportValidationException>(() => service.ExecuteAsync(
            new(TimesheetBatchAction.Refresh, JobId: job.Id), new(), NoUsageChanges, NoUsageChanges, CancellationToken.None));
        var saved = service.Load(job.Id);
        Assert.Equal("completed", saved.State);
        Assert.False(saved.ResultsSaved);
        var destination = Path.Combine(_directory, "not-ready.xlsx");
        var error = await Assert.ThrowsAsync<ReportExportValidationException>(() => service.ExecuteAsync(
            new(TimesheetBatchAction.Export, JobId: job.Id, DestinationPath: destination), new(), NoUsageChanges, NoUsageChanges, CancellationToken.None));
        Assert.Equal("Timesheet.NotReady", error.MessageKey);
        Assert.False(File.Exists(destination));
        failDownload = false;
        var recovered = await new TimesheetBatchService(store, http).ExecuteAsync(
            new(TimesheetBatchAction.Refresh, JobId: job.Id), new(), NoUsageChanges, NoUsageChanges, CancellationToken.None);
        Assert.True(recovered.Selected!.ResultsSaved);
        Assert.Equal(2, recovered.Selected.CompletedCount);
    }

    /// <summary>Remote cleanup failures do not hide paid results or repeat durable usage accounting.</summary>
    [Fact]
    public async Task CleanupFailure_PreservesOfflineExportAndRetriesOnlyOutstandingFiles()
    {
        var store = new LocalStore(Path.Combine(_directory, "data"));
        using var credential = new TestCredential();
        var job = Job(2); job.State = "in_progress"; job.BatchId = "batch_test";
        job.InputFile = "file_input"; job.ApiKeyName = credential.Name; SaveJob(store, job);
        var failCleanup = true;
        var handler = new Transport(overrideResponse: (request, _) => failCleanup && request.Method == HttpMethod.Delete
            ? new HttpResponseMessage(HttpStatusCode.Forbidden) : null);
        using var http = new HttpClient(handler);
        var reconciled = 0;
        Task Reconcile(TimesheetJob saved, CancellationToken token) { reconciled++; return Task.CompletedTask; }
        var service = new TimesheetBatchService(store, http);
        var ready = await service.ExecuteAsync(new(TimesheetBatchAction.Refresh, JobId: job.Id), new(), NoUsageChanges, Reconcile, CancellationToken.None);
        Assert.Equal("Timesheet.Credentials", ready.WarningKey);
        Assert.True(ready.Selected!.ResultsSaved);
        Assert.True(ready.Selected.CleanupPending);
        credential.Dispose();
        var destination = Path.Combine(_directory, "ready.xlsx");
        var exported = await service.ExecuteAsync(new(TimesheetBatchAction.Export, JobId: job.Id, DestinationPath: destination), new(), NoUsageChanges, Reconcile, CancellationToken.None);
        Assert.Equal(destination, exported.ExportedPath);
        Assert.True(File.Exists(destination));
        credential.Restore(); failCleanup = false;
        var cleaned = await new TimesheetBatchService(store, http).ExecuteAsync(
            new(TimesheetBatchAction.Refresh, JobId: job.Id), new(), NoUsageChanges, Reconcile, CancellationToken.None);
        Assert.False(cleaned.Selected!.CleanupPending);
        Assert.Equal(1, reconciled);
        Assert.Equal(3, handler.Methods.Count(method => method == "DELETE"));
    }

    /// <summary>Usage reconciliation rolls back all rows on validation failure and remains idempotent on replay.</summary>
    [Fact]
    public void Usage_ReconcilesAtomicallyAndDoesNotDuplicateReplayedRows()
    {
        var store = new LocalStore(Path.Combine(_directory, "data"));
        var original = Usage("first");
        store.SaveTimesheetUsage([original], CancellationToken.None);
        var updated = original with { Usage = new(InputTokens: 25) };
        Assert.Throws<ArgumentException>(() => store.SaveTimesheetUsage(
            [updated, Usage("second") with { RequestedModel = "" }], CancellationToken.None));
        Assert.Null(Assert.Single(ReadUsage(store)).Usage.InputTokens);
        store.SaveTimesheetUsage([updated, Usage("second")], CancellationToken.None);
        store.SaveTimesheetUsage([updated, Usage("second")], CancellationToken.None);
        var saved = ReadUsage(store);
        Assert.Equal(2, saved.Count);
        Assert.Equal(25L, saved.Single(row => row.AttemptId == "first").Usage.InputTokens);
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        Assert.Throws<OperationCanceledException>(() => store.SaveTimesheetUsage([original], cancelled.Token));
        Assert.Equal(25L, ReadUsage(store).Single(row => row.AttemptId == "first").Usage.InputTokens);
    }

    /// <summary>Only unfinished tokenless reservations for the exact batch identities can be released.</summary>
    [Fact]
    public void ReservationRelease_PreservesPaidUsageCompletedRowsAndOtherRequests()
    {
        var store = new LocalStore(Path.Combine(_directory, "data"));
        store.SaveTimesheetUsage([Usage("reserved"), Usage("completed") with { CompletedAt = Day.AddSeconds(1) },
            Usage("consumed") with { Usage = new(InputTokens: 3) }, Usage("other-batch"),
            Usage("cached") with { Usage = new(CachedInputTokens: 3) }, Usage("response") with { ProviderResponseId = "resp_test" }], CancellationToken.None);
        store.AppendAiUsage(Usage("summary") with { RequestKind = "report_summary" });
        store.ReleaseTimesheetReservations(["reserved", "completed", "consumed", "summary", "cached", "response"], CancellationToken.None);
        Assert.Equal(new[] { "cached", "completed", "consumed", "other-batch", "response", "summary" }, ReadUsage(store).Select(row => row.AttemptId).Order());
    }

    /// <summary>Incomplete responses still contribute their consumed tokens at the batch price.</summary>
    [Fact]
    public void IncompleteBatchUsage_IncludesDiscountedTokenCost()
    {
        var store = new LocalStore(Path.Combine(_directory, "data"));
        store.ReplaceAiModelPricing(AiPricingProviders.OpenAi,
            [new("openai", "gpt-test", AiPricingServiceTiers.Standard, AiPricingContextWindows.Short, "usd",
                1m, null, null, 2m, "https://developers.openai.com/api/docs/pricing.md", Day)]);
        store.SaveTimesheetUsage([Usage("incomplete") with
        { CompletedAt = Day.AddSeconds(1), Usage = new(InputTokens: 100, OutputTokens: 50, TotalTokens: 150), FinishReason = "incomplete" }], CancellationToken.None);
        var result = new ReportAggregationService(store).Build(new(DateOnly.FromDateTime(Day.DateTime), DateOnly.FromDateTime(Day.DateTime), "UTC"), CancellationToken.None);
        Assert.True(result.Succeeded);
        Assert.Equal(0.0001m, result.Value!.AiUsage.EstimatedCostUsd);
        Assert.Equal(1, result.Value.AiUsage.EstimatedCostRequestCount);
    }

    /// <summary>Indexed capture sources preserve project and morning/afternoon boundaries.</summary>
    [Fact]
    public void SourceProjection_DoesNotMixProjectOrDayPartText()
    {
        var store = SourceStore();
        store.AppendSample(Sample(Day.AddHours(11), 60, "Project B") with { WindowTitle = "Other project observation", InstallationId = store.LoadSettings().InstallationId });
        AddCapture(store, Day.AddHours(11.5));
        store.AppendSample(Sample(Day.AddHours(14), 60) with { WindowTitle = "Afternoon observation", InstallationId = store.LoadSettings().InstallationId });
        AddCapture(store, Day.AddHours(14.5));
        var rows = TimesheetProjection.Build(store, TextSources() with { MergeDayParts = false }, CancellationToken.None);
        Assert.Equal(3, rows.Count);
        var morning = rows.Single(row => row.Row.Project == "Project A" && row.Row.Part == "morning");
        Assert.Contains("private title", morning.Prompt);
        Assert.DoesNotContain("Other project observation", morning.Prompt);
        Assert.DoesNotContain("Afternoon observation", morning.Prompt);
        Assert.Contains("Other project observation", rows.Single(row => row.Row.Project == "Project B").Prompt);
        Assert.Contains("Afternoon observation", rows.Single(row => row.Row.Part == "afternoon").Prompt);
        Assert.All(rows, row => Assert.Equal(1, row.Row.SourceCount));
    }

    private LocalStore SourceStore()
    {
        var store = new LocalStore(Path.Combine(_directory, "data"));
        store.SaveSettings(store.LoadSettings() with { ScreenshotDirectory = Path.Combine(_directory, "screenshots") });
        store.AppendSample(Sample(Day.AddHours(9), 60) with { InstallationId = store.LoadSettings().InstallationId });
        AddCapture(store, Day.AddHours(10));
        return store;
    }

    // store supplies synthetic provenance; capturedAt determines the owned calendar directory.
    private static void AddCapture(LocalStore store, DateTimeOffset capturedAt)
    {
        var settings = store.LoadSettings();
        var id = Guid.NewGuid().ToString("N");
        store.RegisterScreenshotCapture(id, settings.InstallationId, capturedAt, "manual");
        var directory = ScreenshotStorageLayout.GetDayDirectory(settings.ScreenshotDirectory, capturedAt);
        Directory.CreateDirectory(directory);
        File.WriteAllBytes(Path.Combine(directory, id + "_1.0.0_manual_active-window.webp"), [0]);
    }

    private static TimesheetOptions TextSources() => Options with
    { Sources = Options.Sources with { IncludeDescriptionExcerpt = false, IncludeWindowTitles = true } };

    // store receives a synthetic durable job with prompts deliberately excluded by its JSON contract.
    // job contains only fixture rows and identifiers.
    private static void SaveJob(LocalStore store, TimesheetJob job)
    {
        var directory = Path.Combine(store.DataDirectory, "timesheet-batches");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, job.Id.ToString("N") + ".json"), JsonSerializer.Serialize(job, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
    }

    // id identifies a synthetic quota reservation; no real provider traffic or response is used.
    private static AiRequestUsageRecord Usage(string id) => new(id, "fixture", Day, null, "report.timesheet", "report_timesheet_batch",
        "openai", "api.openai.com", "gpt-test", null, null, null, null, null, null, 0, 0, 1000, new(), null, false, null);

    // store is the test-owned database whose usage is read without an activity scan.
    private static List<AiRequestUsageRecord> ReadUsage(LocalStore store)
    {
        var rows = new List<AiRequestUsageRecord>();
        store.VisitAiUsage(Day.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1), rows.Add, CancellationToken.None);
        return rows;
    }

    // job and token intentionally produce no quota mutations in transport-only scenarios.
    private static Task NoUsageChanges(TimesheetJob job, CancellationToken token) => Task.CompletedTask;

    private sealed class TestCredential : IDisposable
    {
        internal string Name { get; } = "WORKTRAIL_TEST_" + Guid.NewGuid().ToString("N");
        internal TestCredential() => Restore();
        internal void Restore() => Environment.SetEnvironmentVariable(Name, "synthetic-test-key", EnvironmentVariableTarget.Process);
        /// <inheritdoc />
        public void Dispose() => Environment.SetEnvironmentVariable(Name, null, EnvironmentVariableTarget.Process);
    }

    // count defines deterministic fake rows with no real activity or private provider data.
    private static TimesheetJob Job(int count) => new()
    {
        Id = Guid.NewGuid(),
        CreatedAt = Day,
        Options = Options,
        Rows = Enumerable.Range(1, count).Select(i => new TimesheetWorkRow
        {
            Row = new($"row-{i:D5}", DateOnly.FromDateTime(Day.DateTime), "day", "Project A", Day.AddHours(9), Day.AddHours(10), 3600, 0, "Editor", "", 1),
            Prompt = "Summarize synthetic test observations."
        }).ToList()
    };

    // id identifies a fake request; text supplies its synthetic Responses output.
    private static string Result(string id, string text) => JsonSerializer.Serialize(new
    {
        custom_id = id,
        response = new
        {
            status_code = 200,
            body = new
            {
                id = "resp_test",
                status = "completed",
                output = new[] { new { type = "message", content = new[] { new { type = "output_text", text } } } },
                usage = new { input_tokens = 12, output_tokens = 8, total_tokens = 20 }
            }
        }
    });

    private sealed class Transport(Guid? recoveryId = null, Func<HttpRequestMessage, int, HttpResponseMessage?>? overrideResponse = null) : HttpMessageHandler
    {
        internal List<string> Paths { get; } = [];
        internal List<string> Bodies { get; } = [];
        internal List<string> Methods { get; } = [];

        // request is intercepted in memory; token never reaches a real network connection.
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            var path = request.RequestUri!.AbsolutePath; Paths.Add(path); Methods.Add(request.Method.Method);
            Bodies.Add(request.Content is null ? "" : await request.Content.ReadAsStringAsync(token));
            if (overrideResponse?.Invoke(request, Paths.Count) is { } overridden) return overridden;
            var batch = JsonSerializer.Serialize(new
            {
                id = "batch_test",
                status = "completed",
                output_file_id = "file_output",
                error_file_id = (string?)null,
                metadata = new { worktrail_job = recoveryId?.ToString("N") }
            });
            var body = request.Method == HttpMethod.Delete ? "{\"deleted\":true}"
                : path.EndsWith("/content", StringComparison.Ordinal) ? Result("row-00002", "Second") + "\n" + Result("row-00001", "First")
                : request.Method == HttpMethod.Get && path == "/v1/batches" ? "{\"data\":[" + batch + "],\"has_more\":false}"
                : path == "/v1/files" ? "{\"id\":\"file_input\"}" : batch;
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        }
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        Directory.Delete(_directory, recursive: true);
    }
}
