// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
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

    private sealed class Transport(Guid? recoveryId = null) : HttpMessageHandler
    {
        internal List<string> Paths { get; } = [];
        internal List<string> Bodies { get; } = [];
        internal List<string> Methods { get; } = [];

        // request is intercepted in memory; token never reaches a real network connection.
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            var path = request.RequestUri!.AbsolutePath; Paths.Add(path); Methods.Add(request.Method.Method);
            Bodies.Add(request.Content is null ? "" : await request.Content.ReadAsStringAsync(token));
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
