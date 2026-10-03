// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using WorkTrail.Application;
using WorkTrail.Services;
using Xunit;

namespace WorkTrail.Core.Tests;

/// <summary>Checks cross-month grouping, printed layout and editable amounts with synthetic snapshots.</summary>
public sealed class ReportWorkbookLayoutTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "WorkTrail-workbook-tests-" + Guid.NewGuid().ToString("N"));
    private static readonly XNamespace Ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    /// <summary>Creates an isolated output directory.</summary>
    public ReportWorkbookLayoutTests() => Directory.CreateDirectory(_directory);

    /// <summary>A weekly row retains measured local dates across month boundaries.</summary>
    [Fact]
    public void WeekGrouping_CombinesProjectsAppsAndDates_WithoutChangingMeasuredTime()
    {
        var details = Observations();
        var weeks = TimesheetProjection.Group(details, TimesheetGrouping.Week);
        Assert.Equal(2, weeks.Count);
        Assert.Equal(new DateOnly(2026, 9, 28), weeks[0].Date);
        Assert.Equal("Project A; Project B", weeks[0].Project);
        Assert.Equal("Browser; Editor", weeks[0].Applications);
        Assert.Equal(details.Sum(row => row.ActiveSeconds), weeks.Sum(row => row.ActiveSeconds));
        Assert.Equal(details.Sum(row => row.IdleSeconds), weeks.Sum(row => row.IdleSeconds));
        Assert.Equal(10, TimesheetProjection.Group(details, TimesheetGrouping.Day).Count);
    }

    /// <summary>The print layout, cross-sheet formulas and source snapshot reconcile for every locale.</summary>
    /// <param name="language">A shipped locale with localized worksheet names and date formats.</param>
    [Theory]
    [InlineData("en-US")]
    [InlineData("it-IT")]
    [InlineData("fr-FR")]
    [InlineData("de-DE")]
    [InlineData("es-ES")]
    [InlineData("vi-VN")]
    [InlineData("zh-Hans")]
    [InlineData("ko-KR")]
    [InlineData("pt-PT")]
    [InlineData("pt-BR")]
    public void Workbook_HasA4Cover_SevenColumns_TypedTime_AndExactMonthlyAmounts(string language)
    {
        var job = Job(TimesheetGrouping.Week, language);
        var path = TimesheetExcelWriter.Write(job, Path.Combine(_directory, language + ".xlsx"), false, CancellationToken.None);
        using var zip = ZipFile.OpenRead(path);
        var cover = Xml(zip, "xl/worksheets/sheet1.xml");
        Assert.Equal("portrait", cover.Descendants(Ns + "pageSetup").Single().Attribute("orientation")!.Value);
        Assert.NotNull(zip.GetEntry("xl/media/brandmark.png"));
        var workbook = Xml(zip, "xl/workbook.xml");
        var names = workbook.Descendants(Ns + "sheet").Select(sheet => sheet.Attribute("name")!.Value).ToArray();
        Assert.Equal(names.Length * 2 - 1, workbook.Descendants(Ns + "definedName").Count());
        Assert.All(workbook.Descendants(Ns + "definedName"), name => Assert.DoesNotContain("XFD", name.Value));
        var report = Xml(zip, "xl/worksheets/sheet2.xml");
        Assert.Equal(7, report.Descendants(Ns + "row").Single(row => row.Attribute("r")!.Value == "7").Elements(Ns + "c").Count());
        Assert.Equal("7", report.Descendants(Ns + "pane").Single().Attribute("ySplit")!.Value);
        var cells = report.Descendants(Ns + "c").ToDictionary(cell => cell.Attribute("r")!.Value);
        Assert.Equal(job.Rows[0].Row.ActiveSeconds / 86400d, Number(cells["B8"]), 10);
        Assert.Contains("What I worked on", cells["D8"].Value);
        var details = Xml(zip, "xl/worksheets/sheet4.xml");
        var amounts = details.Descendants(Ns + "c").Where(cell => cell.Attribute("r")!.Value.StartsWith("K", StringComparison.Ordinal) && cell.Element(Ns + "v") is not null).Select(Number).Sum();
        Assert.Equal(amounts, Number(cells["F8"]) + Number(cells["F9"]), 8);
        var monthly = Xml(zip, "xl/worksheets/sheet5.xml");
        Assert.Equal(amounts, monthly.Descendants(Ns + "c").Where(cell => cell.Attribute("r")!.Value.StartsWith("E", StringComparison.Ordinal) && cell.Element(Ns + "v") is not null).Select(Number).Sum(), 8);
        Assert.All(monthly.Descendants(Ns + "f"), formula => Assert.Contains("SUMIFS(", formula.Value));
        Assert.All(details.Descendants(Ns + "f"), formula => Assert.Contains("$B$8", formula.Value));
        Assert.All(report.Descendants(Ns + "f"), formula => Assert.DoesNotContain("Client", formula.Value));
    }

    /// <summary>A half-cent amount matches Excel ROUND rather than banker's rounding.</summary>
    [Fact]
    public void Amounts_RoundAtMeasuredSourceRows_AndStayNumericWhenBillingIsDisabled()
    {
        var job = Job(TimesheetGrouping.Day);
        job.Options = job.Options with { HourlyRate = 0.2m };
        var detail = job.Rows[0].Observations[0] with { ActiveSeconds = 90 };
        job.Rows = [new() { Row = detail with { State = "completed" }, Observations = [detail] }];
        var path = TimesheetExcelWriter.Write(job, Path.Combine(_directory, "rounding.xlsx"), false, CancellationToken.None);
        using (var zip = ZipFile.OpenRead(path))
        {
            var cell = Xml(zip, "xl/worksheets/sheet4.xml").Descendants(Ns + "c").Single(cell => cell.Attribute("r")!.Value == "K8");
            Assert.Equal(0.01, Number(cell));
        }
        job.Options = job.Options with { HourlyRate = null };
        TimesheetExcelWriter.Write(job, Path.Combine(_directory, "no-billing.xlsx"), false, CancellationToken.None);
        using var plain = ZipFile.OpenRead(Path.Combine(_directory, "no-billing.xlsx"));
        Assert.Equal(6, Xml(plain, "xl/worksheets/sheet2.xml").Descendants(Ns + "row").Single(row => row.Attribute("r")!.Value == "7").Elements(Ns + "c").Count());
        Assert.Empty(Xml(plain, "xl/worksheets/sheet4.xml").Descendants(Ns + "f"));
    }

    /// <summary>V1 paid reports are migrated once without changing descriptions or making provider calls.</summary>
    [Fact]
    public void SavedVersionOneReport_IsExplicitlyMigratedWithItsOriginalMeasuredTime()
    {
        var store = new LocalStore(Path.Combine(_directory, "store"));
        var job = Job(TimesheetGrouping.Day);
        job.Version = 1;
        foreach (var row in job.Rows) row.Observations.Clear();
        var directory = Path.Combine(store.DataDirectory, "timesheet-batches");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, job.Id.ToString("N") + ".json");
        File.WriteAllText(path, JsonSerializer.Serialize(job, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        var migrated = new TimesheetBatchService(store).Load(job.Id);
        Assert.Equal(2, migrated.Version);
        Assert.Equal(job.Rows.Select(row => row.Row.Description), migrated.Rows.Select(row => row.Row.Description));
        Assert.All(migrated.Rows, row => Assert.Equal(row.Row.ActiveSeconds, Assert.Single(row.Observations).ActiveSeconds));
        using var saved = JsonDocument.Parse(File.ReadAllText(path));
        Assert.Equal(2, saved.RootElement.GetProperty("version").GetInt32());
        Assert.DoesNotContain("prompt", File.ReadAllText(path));
    }

    /// <summary>Titles used by the AI cannot bypass the independent export field preference.</summary>
    [Fact]
    public void Export_ExcludesWindowTitlesEvenWhenTheBatchUsedThem()
    {
        var job = Job(TimesheetGrouping.Day);
        job.Options = job.Options with { Sources = job.Options.Sources with { IncludeWindowTitles = true } };
        foreach (var row in job.Rows)
            row.Observations = row.Observations.Select(observation => observation with { References = "PRIVATE-TITLE" }).ToList();
        var path = TimesheetExcelWriter.Write(job, Path.Combine(_directory, "privacy.xlsx"), false, CancellationToken.None);
        using (var zip = ZipFile.OpenRead(path))
            Assert.DoesNotContain("PRIVATE-TITLE", Xml(zip, "xl/worksheets/sheet4.xml").ToString());
        job.Options = job.Options with { Sources = job.Options.Sources with { Options = job.Options.Sources.Options with { IncludeWindowTitles = true } } };
        TimesheetExcelWriter.Write(job, Path.Combine(_directory, "titles.xlsx"), false, CancellationToken.None);
        using var selected = ZipFile.OpenRead(Path.Combine(_directory, "titles.xlsx"));
        Assert.Contains("PRIVATE-TITLE", Xml(selected, "xl/worksheets/sheet4.xml").ToString());
    }

    /// <summary>Presentation presets use native themes and leave measured values and formulas unchanged.</summary>
    [Fact]
    public void Themes_ChangeNativeColors_WithoutChangingReportData()
    {
        var job = Job(TimesheetGrouping.Day);
        string? referenceReport = null;
        var palettes = new[]
        {
            (Theme: ReportWorkbookTheme.WorkTrail, Accent: "6038A0"),
            (Theme: ReportWorkbookTheme.SpreadsheetGreen, Accent: "217346"),
            (Theme: ReportWorkbookTheme.InkBlue, Accent: "244B75")
        };
        XNamespace drawing = "http://schemas.openxmlformats.org/drawingml/2006/main";
        foreach (var (theme, accent) in palettes)
        {
            job.Options = job.Options with { Sources = job.Options.Sources with { Options = job.Options.Sources.Options with { Theme = theme } } };
            var path = TimesheetExcelWriter.Write(job, Path.Combine(_directory, theme + ".xlsx"), false, CancellationToken.None);
            using var zip = ZipFile.OpenRead(path);
            Assert.Equal(accent, Xml(zip, "xl/theme/theme1.xml").Descendants(drawing + "accent1").Single().Element(drawing + "srgbClr")!.Attribute("val")!.Value);
            Assert.Contains("theme/theme1.xml", Xml(zip, "xl/_rels/workbook.xml.rels").ToString());
            var styles = Xml(zip, "xl/styles.xml");
            Assert.Contains(styles.Descendants(Ns + "fgColor"), color => color.Attribute("theme")?.Value == "4");
            Assert.DoesNotContain(styles.Descendants(Ns + "color"), color => color.Attribute("rgb") is not null);
            var report = Xml(zip, "xl/worksheets/sheet2.xml").ToString();
            if (referenceReport is null) referenceReport = report;
            else Assert.Equal(referenceReport, report);
        }
    }

    /// <summary>An invalid presentation value cannot publish a partial workbook.</summary>
    [Fact]
    public void UnsupportedTheme_FailsWithoutPublishingAFile()
    {
        var job = Job(TimesheetGrouping.Day);
        job.Options = job.Options with { Sources = job.Options.Sources with { Options = job.Options.Sources.Options with { Theme = (ReportWorkbookTheme)99 } } };
        var path = Path.Combine(_directory, "invalid-theme.xlsx");
        Assert.Throws<ArgumentOutOfRangeException>(() => TimesheetExcelWriter.Write(job, path, false, CancellationToken.None));
        Assert.False(File.Exists(path));
    }

    /// <summary>Changing a saved report's presentation does not mutate its snapshot or reserve AI usage.</summary>
    [Fact]
    public async Task SavedBatch_CanChangeTheme_ForPreviewAndExport_WithoutResubmission()
    {
        var store = new LocalStore(Path.Combine(_directory, "theme-store"));
        var job = Job(TimesheetGrouping.Day);
        job.Options = job.Options with { Sources = job.Options.Sources with { Options = job.Options.Sources.Options with { IncludeCaptures = false } } };
        var directory = Path.Combine(store.DataDirectory, "timesheet-batches");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, job.Id.ToString("N") + ".json");
        var snapshot = JsonSerializer.Serialize(job, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        File.WriteAllText(path, snapshot);
        var preview = ReportFilePreviewService.Create(store,
            new(job.Options.Sources.Options with { Theme = ReportWorkbookTheme.InkBlue }, TimesheetJobId: job.Id), CancellationToken.None);
        try
        {
            using var zip = ZipFile.OpenRead(preview.Path);
            Assert.Contains("244B75", Xml(zip, "xl/theme/theme1.xml").ToString());
        }
        finally { File.Delete(preview.Path); }
        var export = Path.Combine(_directory, "saved-green.xlsx");
        await new TimesheetBatchService(store).ExecuteAsync(
            new(TimesheetBatchAction.Export, JobId: job.Id, DestinationPath: export, Theme: ReportWorkbookTheme.SpreadsheetGreen),
            new AppSettings(), (_, _) => throw new InvalidOperationException("No reservation is allowed."),
            (_, _) => throw new InvalidOperationException("No AI usage change is allowed."), CancellationToken.None);
        using var exported = ZipFile.OpenRead(export);
        Assert.Contains("217346", Xml(exported, "xl/theme/theme1.xml").ToString());
        Assert.Equal(snapshot, File.ReadAllText(path));
    }

    /// <summary>Produces synthetic real-generator samples for visual inspection outside Git.</summary>
    [Fact]
    public void GenerateSyntheticWorkbooks_ForLayoutInspection()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "WorkTrail.slnx"))) directory = directory.Parent;
        Assert.NotNull(directory);
        var output = Path.Combine(directory!.FullName, "artifacts", "report-layout-qa");
        Directory.CreateDirectory(output);
        var store = new LocalStore(Path.Combine(_directory, "visual-store"));
        var captures = Path.Combine(_directory, "visual-captures");
        Directory.CreateDirectory(captures);
        store.SaveSettings(store.LoadSettings() with { ScreenshotDirectory = captures });
        var job = Job(TimesheetGrouping.Day);
        foreach (var row in job.Rows)
            store.AppendSample(new(row.Row.LastObserved, (int)row.Row.ActiveSeconds, "active", "editor", "Editor", "", "", store.LoadSettings().InstallationId, 15, 3));
        var archive = new ReportExportService(store).Build(job.Options.Sources.Options, null, CancellationToken.None) with
        {
            Captures = job.Rows.Select((row, index) => new ScreenshotGalleryItem(row.Row.LastObserved, "synthetic-" + index + ".webp", "Editor", "window", "manual", AiDescriptionMarkdown: "Synthetic saved description for the layout review.")).ToArray()
        };
        TimesheetExcelWriter.Write(job, Path.Combine(output, "WorkTrail_daily_sample.xlsx"), true, CancellationToken.None, archive: archive);
        TimesheetExcelWriter.Write(Job(TimesheetGrouping.Week), Path.Combine(output, "WorkTrail_weekly_sample.xlsx"), true, CancellationToken.None, archive: archive);
        ReportExportWriter.Write(archive, Path.Combine(output, "WorkTrail_archive_sample.xlsx"), true, CancellationToken.None);
        foreach (var theme in new[] { ReportWorkbookTheme.SpreadsheetGreen, ReportWorkbookTheme.InkBlue })
        {
            job.Options = job.Options with { Sources = job.Options.Sources with { Options = job.Options.Sources.Options with { Theme = theme } } };
            TimesheetExcelWriter.Write(job, Path.Combine(output, "WorkTrail_daily_" + theme + "_sample.xlsx"), true, CancellationToken.None, archive: archive);
        }
    }

    // grouping chooses the number of synthetic AI descriptions.
    // language supplies a shipped locale, not a machine-specific setting.
    private static TimesheetJob Job(TimesheetGrouping grouping, string language = "en-US")
    {
        var details = Observations();
        var rows = TimesheetProjection.Group(details, grouping);
        return new()
        {
            Id = Guid.NewGuid(),
            CreatedAt = new(2026, 10, 10, 12, 0, 0, TimeSpan.Zero),
            State = "completed",
            ResultsSaved = true,
            Options = new(new(new(new(2026, 9, 28), new(2026, 10, 9), "UTC", Language: language)), Consultant: "Example consultant", Client: "Example client", HourlyRate: 60m, Grouping: grouping),
            Rows = rows.Select(row => new TimesheetWorkRow
            {
                Row = row with { Description = "What I worked on: reviewed the integration, fixed a validation error, and checked the deployment. Used Editor for code and Browser for the documentation. All text in this sample is fictional.", State = "completed", SourceCount = 3 },
                Observations = details.Where(detail => TimesheetProjection.GroupDate(detail.Date, grouping) == row.Date).ToList()
            }).ToList()
        };
    }

    // Returns ten fictional working days spanning a month boundary and two Monday-based weeks.
    private static TimesheetRow[] Observations() => Enumerable.Range(0, 12).Select(index => new DateOnly(2026, 9, 28).AddDays(index))
        .Where(date => date.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday))
        .Select((date, index) => new TimesheetRow("source-" + index, date, "day", index % 2 == 0 ? "Project A" : "Project B",
            new(date.Year, date.Month, date.Day, 9, 0, 0, TimeSpan.Zero), new(date.Year, date.Month, date.Day, 16, 0, 0, TimeSpan.Zero),
            6 * 3600 + index * 60, 1800, "Browser; Editor", "", 0)).ToArray();

    // zip owns the workbook; name selects an existing package entry.
    private static XDocument Xml(ZipArchive zip, string name)
    {
        using var stream = zip.GetEntry(name)!.Open();
        return XDocument.Load(stream);
    }

    // cell is a genuine numeric cell with its invariant cached value.
    private static double Number(XElement cell) => double.Parse(cell.Element(Ns + "v")!.Value, CultureInfo.InvariantCulture);

    /// <inheritdoc />
    public void Dispose() => Directory.Delete(_directory, true);
}
