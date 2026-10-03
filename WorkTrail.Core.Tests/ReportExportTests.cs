// SPDX-License-Identifier: MIT

using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using WorkTrail.Application;
using WorkTrail.Runtime;
using WorkTrail.Services;
using Xunit;

namespace WorkTrail.Core.Tests;

/// <summary>Exercises real serializers, synthetic local data, entitlement enforcement and source selection.</summary>
public sealed class ReportExportTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "WorkTrail-export-tests-" + Guid.NewGuid().ToString("N"));
    private static readonly DateOnly Day = new(2026, 9, 14);
    private static readonly DateTimeOffset Instant = new(2026, 9, 14, 10, 0, 0, TimeSpan.Zero);
    private static ReportExportOptions Options => new(Day, Day, "UTC");

    /// <summary>Creates a uniquely owned synthetic fixture directory.</summary>
    public ReportExportTests() => Directory.CreateDirectory(_root);

    private LocalStore Store()
    {
        var store = new LocalStore(Path.Combine(_root, "data"));
        var screenshots = Path.Combine(_root, "screenshots");
        Directory.CreateDirectory(screenshots);
        store.SaveSettings(store.LoadSettings() with { ScreenshotDirectory = screenshots });
        return store;
    }

    private static ScreenshotGalleryItem Capture(string? description = "Saved description") =>
        new(Instant, @"C:\synthetic-private-path\image.webp", "Editor", "window", "manual",
            AiDescriptionMarkdown: description, ForegroundWindowTitle: "PRIVATE-WINDOW-TITLE");

    private ExportDocument Document(ReportExportOptions? options = null, string? description = "Saved description") =>
        new ReportExportService(Store()).Build(options ?? Options, null, CancellationToken.None) with { Captures = [Capture(description)] };

    /// <summary>The Free service refuses writing even when a caller bypasses the desktop button.</summary>
    [Fact]
    public async Task Free_AllowsPreviewButDeniesFileWritesThroughFacadeAndIpc()
    {
        await using var application = CreateApplication(Store(), ProductTier.Free);
        var preview = await application.PreviewReportExportAsync(Options, CancellationToken.None);
        Assert.True(preview.Succeeded);
        var destination = Path.Combine(_root, "forbidden.xlsx");
        var request = new ReportExportRequest(Options, destination);
        var direct = await application.ExportReportAsync(request, CancellationToken.None);
        Assert.Equal("feature.premium_required", direct.Code);
        var timesheet = await application.ManageTimesheetBatchAsync(new(TimesheetBatchAction.Start, new(new(Options))), CancellationToken.None);
        Assert.False(timesheet.Succeeded);
        Assert.Equal("Premium.Required", timesheet.MessageKey);
        var dispatcher = new RuntimeRequestDispatcher(application, NullLogger.Instance);
        var wire = await dispatcher.DispatchAsync(new(RuntimeProtocol.ProtocolVersion, Guid.NewGuid(), "report.export.write.v1",
            JsonSerializer.SerializeToElement(request, RuntimeProtocol.SerializerOptions), "en-US", null), CancellationToken.None);
        Assert.Equal("feature.premium_required", wire.Code);
        Assert.False(File.Exists(destination));
        Assert.Empty(Directory.GetFiles(_root, "*.tmp"));
    }

    /// <summary>A trusted Full tier can produce a finalized file with the same selected projection.</summary>
    [Fact]
    public async Task Premium_WritesJsonAndRetainsTypedNumbers()
    {
        var store = Store();
        store.AppendSample(new(Instant, 60, "active", "editor", "Editor", "Work", "Private", store.LoadSettings().InstallationId, 8, 2));
        await using var application = CreateApplication(store, ProductTier.Premium);
        var path = Path.Combine(_root, "report.json");
        var result = await application.ExportReportAsync(new(Options with { Format = ReportExportFormat.Json }, path), CancellationToken.None);
        Assert.True(result.Succeeded);
        Assert.True(result.Value!.Bytes > 0);
        using var json = JsonDocument.Parse(File.ReadAllText(path));
        Assert.Equal(1, json.RootElement.GetProperty("schemaVersion").GetInt32());
        Assert.Equal(60, json.RootElement.GetProperty("tables").GetProperty("Summary")[0].GetProperty("active_seconds").GetInt64());
    }

#if DEBUG
    /// <summary>A preview made before downgrade cannot act as an export entitlement.</summary>
    [Fact]
    public async Task DowngradeAfterPreview_DeniesThePendingExport()
    {
        await using var application = CreateApplication(Store(), ProductTier.Premium);
        Assert.True((await application.PreviewReportExportAsync(Options, CancellationToken.None)).Succeeded);
        Assert.True((await application.SimulateFeatureAccessAsync(ProductTier.Free, CancellationToken.None)).Succeeded);
        var result = await application.ExportReportAsync(new(Options, Path.Combine(_root, "denied.xlsx")), CancellationToken.None);
        Assert.Equal("feature.premium_required", result.Code);
    }
#endif

    /// <summary>Unselected raw fields never leak into the serialized document.</summary>
    [Fact]
    public void Json_ExcludesPrivateFieldsAndPreservesCompleteDescriptions()
    {
        var text = "A description\n" + new string('x', 40000);
        var path = Path.Combine(_root, "private.json");
        ReportExportWriter.Write(Document(Options with { Format = ReportExportFormat.Json }, text), path, false, CancellationToken.None);
        var raw = File.ReadAllText(path);
        Assert.DoesNotContain("PRIVATE-WINDOW-TITLE", raw);
        Assert.DoesNotContain("synthetic-private-path", raw);
        using var json = JsonDocument.Parse(raw);
        var row = json.RootElement.GetProperty("tables").GetProperty("Captures")[0];
        Assert.Equal(text, row.GetProperty("description_markdown").GetString());
        Assert.False(row.TryGetProperty("window_title", out _));
        Assert.False(row.TryGetProperty("screenshot_path", out _));
    }

    /// <summary>Details contain missing telemetry as null rather than invented zero measurements.</summary>
    [Fact]
    public void Json_OptedInFieldsAndNullMeasurementsRemainExplicit()
    {
        var path = Path.Combine(_root, "details.json");
        ReportExportWriter.Write(Document(Options with { Format = ReportExportFormat.Json, IncludeWindowTitles = true, IncludeTelemetry = true }), path, false, CancellationToken.None);
        using var json = JsonDocument.Parse(File.ReadAllText(path));
        var row = json.RootElement.GetProperty("tables").GetProperty("Captures")[0];
        Assert.Equal("PRIVATE-WINDOW-TITLE", row.GetProperty("window_title").GetString());
        Assert.Equal(JsonValueKind.Null, row.GetProperty("cpu_percent").ValueKind);
    }

    /// <summary>The XLSX package has resolvable sheets, inert text cells and complete overflow text.</summary>
    [Fact]
    public void Excel_PreservesLongTextAndUsesNoFormulas()
    {
        var description = "=HYPERLINK(\"https://invalid.example\") " + new string('a', 40000);
        var path = Path.Combine(_root, "report.xlsx");
        var result = ReportExportWriter.Write(Document(description: description), path, false, CancellationToken.None);
        Assert.Equal(8, result.TableCount);
        using var zip = ZipFile.OpenRead(path);
        XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        foreach (var entry in zip.Entries.Where(entry => entry.FullName.EndsWith(".xml") || entry.FullName.EndsWith(".rels")))
        {
            using var stream = entry.Open();
            var xml = XDocument.Load(stream);
            Assert.Empty(xml.Descendants(ns + "f"));
        }
        using var parts = zip.GetEntry("xl/worksheets/sheet8.xml")!.Open();
        var rows = XDocument.Load(parts).Descendants(ns + "row").Where(row => int.Parse(row.Attribute("r")!.Value) >= 8).ToArray();
        var reconstructed = string.Concat(rows.Select(row => row.Elements(ns + "c").Last().Descendants(ns + "t").Single().Value));
        Assert.Equal(description, reconstructed);
        using var captureSheet = zip.GetEntry("xl/worksheets/sheet5.xml")!.Open();
        Assert.All(XDocument.Load(captureSheet).Descendants(ns + "t"), value => Assert.True(value.Value.Length <= 32767));
        Assert.NotNull(zip.GetEntry("[Content_Types].xml"));
        Assert.NotNull(zip.GetEntry("xl/_rels/workbook.xml.rels"));
    }

    /// <summary>Spreadsheet formula injection is neutralized, including leading whitespace and control characters.</summary>
    [Theory]
    [InlineData("=1+1")]
    [InlineData(" +SUM(A1)")]
    [InlineData("-2+3")]
    [InlineData("@SUM(1)")]
    [InlineData("\tvalue")]
    [InlineData("\rvalue")]
    public void Csv_FormulaLikeTextIsInert(string source) => Assert.StartsWith("\"'", ReportExportWriter.CsvText(source));

    /// <summary>CSV archives preserve UTF-8 accents, escaped quotes and separate data tables.</summary>
    [Fact]
    public void Csv_WritesBomAndEscapesQuotesAndNewlines()
    {
        var path = Path.Combine(_root, "csv.zip");
        ReportExportWriter.Write(Document(Options with { Format = ReportExportFormat.Csv }, "Attività; \"test\"\nseconda riga"), path, false, CancellationToken.None);
        using var zip = ZipFile.OpenRead(path);
        Assert.Equal(4, zip.Entries.Count);
        using var stream = zip.GetEntry("Captures.csv")!.Open();
        using var memory = new MemoryStream(); stream.CopyTo(memory);
        var bytes = memory.ToArray();
        Assert.Equal(new byte[] { 0xef, 0xbb, 0xbf }, bytes.Take(3));
        Assert.Contains("\"Attività; \"\"test\"\"\nseconda riga\"", Encoding.UTF8.GetString(bytes));
    }

    /// <summary>Cancellation after temporary output leaves an existing destination byte-for-byte unchanged.</summary>
    [Fact]
    public void CancelledWrite_PreservesExistingFileAndRemovesItsTemporaryFile()
    {
        var path = Path.Combine(_root, "existing.json");
        File.WriteAllText(path, "original");
        using var cancellation = new CancellationTokenSource();
        Assert.Throws<OperationCanceledException>(() => ReportExportWriter.AtomicWrite(path, true, stream =>
        { stream.WriteByte(42); cancellation.Cancel(); }, cancellation.Token));
        Assert.Equal("original", File.ReadAllText(path));
        Assert.Empty(Directory.GetFiles(_root, "*.tmp"));
    }

    /// <summary>Writer failures and unapproved replacement both preserve existing user output.</summary>
    [Fact]
    public void FailedWriteOrUnapprovedOverwrite_PreservesDestination()
    {
        var path = Path.Combine(_root, "existing.json");
        File.WriteAllText(path, "original");
        Assert.Throws<IOException>(() => ReportExportWriter.AtomicWrite(path, true, _ => throw new IOException("synthetic"), CancellationToken.None));
        Assert.Throws<IOException>(() => ReportExportWriter.AtomicWrite(path, false, _ => throw new Exception("must not run"), CancellationToken.None));
        Assert.Equal("original", File.ReadAllText(path));
        Assert.Empty(Directory.GetFiles(_root, "*.tmp"));
    }

    /// <summary>Preview does not send complete long descriptions over the named pipe.</summary>
    [Fact]
    public void Preview_ReportsTotalRowsButBoundsRowsAndText()
    {
        var document = Document(description: new string('x', 10000)) with { Captures = Enumerable.Range(0, 12).Select(_ => Capture(new string('x', 10000))).ToArray() };
        var preview = ReportExportService.Preview(document, CancellationToken.None);
        var captures = preview.Tables.Single(table => table.Name == "Captures");
        Assert.Equal(12, captures.RowCount);
        Assert.Equal(5, captures.Rows.Count);
        Assert.All(captures.Rows.SelectMany(row => row), text => Assert.True(text.Length <= 241));
    }

    /// <summary>AI prompts are bounded and exclude fields not explicitly selected as sources.</summary>
    [Fact]
    public void Summary_UsesOnlySelectedSourcesAndRejectsExcessText()
    {
        var request = new ReportSummaryRequest(Options);
        var prompt = ReportSummaryService.BuildPrompt(request, [Capture("A recorded edit")], out var count);
        Assert.Equal(1, count);
        Assert.Contains("A recorded edit", prompt);
        Assert.Contains("description_excerpt", prompt);
        Assert.DoesNotContain("description_markdown\":\"A recorded edit", prompt);
        Assert.DoesNotContain("PRIVATE-WINDOW-TITLE", prompt);
        Assert.DoesNotContain("synthetic-private-path", prompt);
        var completeRequest = request with { IncludeDescriptionExcerpt = false, IncludeCompleteDescription = true };
        var completePrompt = ReportSummaryService.BuildPrompt(completeRequest, [Capture("A recorded edit")], out _);
        Assert.Contains("description_markdown\":\"A recorded edit", completePrompt);
        Assert.DoesNotContain("description_excerpt\":\"A recorded edit", completePrompt);
        var tooLarge = Assert.Throws<ReportExportValidationException>(() =>
            ReportSummaryService.BuildPrompt(completeRequest, [Capture(new string('x', 170000))], out _));
        Assert.Equal(160000, tooLarge.Limit);
        Assert.True(tooLarge.ActualLength > tooLarge.Limit);
        Assert.Throws<ReportExportValidationException>(() => ReportSummaryService.BuildPrompt(request, [Capture(null)], out _));
    }

    /// <summary>Invalid ranges, identities and ambiguous cost attribution are rejected before export.</summary>
    [Fact]
    public void Validation_RejectsInvalidOptionsAndDeviceLocalAiCosts()
    {
        var store = Store(); var service = new ReportExportService(store);
        Assert.Throws<ArgumentException>(() => service.Validate(Options with { From = Day.AddDays(1) }));
        Assert.Throws<ArgumentException>(() => service.Validate(Options with { ToInclusive = Day.AddDays(366) }));
        Assert.Throws<ArgumentException>(() => service.Validate(Options with { Format = (ReportExportFormat)42 }));
        Assert.Throws<ArgumentException>(() => service.Validate(Options with { InstallationId = "unknown" }));
        Assert.Throws<ReportExportValidationException>(() => service.Validate(Options with { InstallationId = store.LoadSettings().InstallationId, IncludeAiUsage = true }));
    }

    /// <summary>Preferences survive reopening without persisting an old range or generated text.</summary>
    [Fact]
    public void Preferences_RoundTripFieldChoicesAndRejectCorruption()
    {
        var store = Store(); var service = new ReportExportService(store);
        service.SavePreferences(Options with { Format = ReportExportFormat.Csv, IncludeOcr = true }, CancellationToken.None);
        var setup = new ReportExportService(store).Setup();
        Assert.Equal(ReportExportFormat.Csv, setup.Options.Format);
        Assert.True(setup.Options.IncludeOcr);
        Assert.Equal(DateOnly.FromDateTime(DateTime.Today), setup.Options.ToInclusive);
        File.WriteAllText(Path.Combine(store.DataDirectory, "report-export-preferences.json"), "invalid");
        Assert.Throws<JsonException>(() => service.Setup());
    }

    private static WorkTrailApplication CreateApplication(LocalStore store, ProductTier tier) =>
        new(store, new UtilityService(), new TrackingDomainService(store), DispatchProxy.Create<IScreenCaptureService, NoCalls>(),
            new FakeHardwareTelemetryService(), DispatchProxy.Create<IAiAnalysisService, NoCalls>(), new StartupService(), new BuildInformationService(),
            startScheduledSnapshotTimer: false, featureLicenseSource: new License(tier));

    /// <summary>Text-only summary attempts count toward the same daily request limit as screenshot analysis.</summary>
    [Fact]
    public void SummaryUsage_CountsTowardTheDailyQuotaWithoutSavingText()
    {
        var store = Store();
        var id = Guid.NewGuid().ToString("N");
        store.AppendAiUsage(new(id, id, DateTimeOffset.Now, DateTimeOffset.Now, "report.summary", "report_summary",
            "openai", "api.openai.com", "test-model", null, null, null, 200, 10, null, 0, 100, 1000,
            new AiUsageMetrics(InputTokens: 25, OutputTokens: 10), "stop", true, null));
        Assert.Equal(1, store.GetTodayAnalysisCount());
    }

    /// <summary>Rejects accidental capture or provider work in synthetic export tests.</summary>
    public class NoCalls : DispatchProxy
    {
        /// <inheritdoc />
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => throw new InvalidOperationException("Unexpected external call in export test.");
    }

    private sealed class License(ProductTier tier) : IFeatureLicenseSource
    {
        /// <inheritdoc />
        public ProductTier Tier => tier;
    }

    /// <summary>Removes only the synthetic fixture owned by this test instance.</summary>
    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
