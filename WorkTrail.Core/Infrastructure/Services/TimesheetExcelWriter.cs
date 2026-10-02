// SPDX-License-Identifier: MIT

using System.Globalization;
using System.IO.Compression;
using System.Xml;
using WorkTrail.Application;

namespace WorkTrail.Services;

/// <summary>Produces styled, editable Excel timesheets from measured time and saved batch results.</summary>
internal static class TimesheetExcelWriter
{
    private const string Ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    // job is the immutable report snapshot selected for export.
    // destination is the fully qualified path chosen by the user.
    // overwrite records explicit replacement approval from the file picker.
    // token cancels before the atomic destination replacement.
    internal static string Write(TimesheetJob job, string destination, bool overwrite, CancellationToken token, int? rowLimit = null)
    {
        if (!Path.IsPathFullyQualified(destination) || !string.Equals(Path.GetExtension(destination), ".xlsx", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("A fully qualified XLSX destination is required.");
        var path = Path.GetFullPath(destination);
        var strings = new LocalizationService(job.Options.Sources.Options.Language);
        var rows = job.Rows.Select(row => row.Row).ToArray();
        var sheets = new List<(string Name, TimesheetRow[] Rows, bool Details)>();
        sheets.Add((strings.Translate("Timesheet.Sheet"), rows, false));
        if (job.Options.SeparateMonths)
            foreach (var month in rows.GroupBy(row => row.Date.ToString("yyyy-MM", CultureInfo.InvariantCulture)))
                sheets.Add((month.Key, month.ToArray(), false));
        sheets.Add((strings.Translate("Timesheet.DetailsSheet"), rows, true));
        ReportExportWriter.AtomicWrite(path, overwrite, stream =>
        {
            using var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true);
            for (var index = 0; index < sheets.Count; index++)
                WriteSheet(zip, index + 1, rowLimit is { } limit ? sheets[index].Rows.Take(limit).ToArray() : sheets[index].Rows,
                    sheets[index].Details, job.Options, strings, rowLimit.HasValue, token);
            ReportExportWriter.WriteWorkbookPackage(zip, sheets.Select(sheet => sheet.Name).ToArray(), recalculateOnOpen: true);
            Styles(zip);
        }, token);
        return path;
    }

    // zip owns the output package; index is the one-based sheet identifier.
    // rows contains the exact saved observations, including explicit failures.
    // details chooses the additional provenance columns.
    // options and strings supply user metadata and localization.
    // token cancels worksheet generation.
    private static void WriteSheet(ZipArchive zip, int index, TimesheetRow[] rows, bool details,
        TimesheetOptions options, LocalizationService strings, bool preview, CancellationToken token) =>
        ReportExportWriter.WriteXml(zip, $"xl/worksheets/sheet{index}.xml", xml =>
        {
            string T(string key) => strings.Translate("Timesheet." + key);
            var headers = details
                ? new[] { "Reference", "Start", "End", "Idle", "Apps", "References", "Sources", "Status" }
                : new[] { "Date", "Part", "Active", "Client", "Project", "Description", "Notes", "Reference" }
                    .Concat(options.HourlyRate.HasValue ? new[] { "Amount" } : []).ToArray();
            var widths = details ? new[] { 20, 19, 19, 16, 34, 48, 40, 24 } : new[] { 14, 18, 16, 24, 24, 60, 32, 22, 20 };
            var lastColumn = ReportExportWriter.ColumnName(headers.Length);
            var first = 11;
            var last = first + rows.Length - 1;
            xml.WriteStartElement("worksheet", Ns);
            xml.WriteStartElement("sheetViews", Ns); xml.WriteStartElement("sheetView", Ns);
            xml.WriteAttributeString("workbookViewId", "0"); xml.WriteAttributeString("showGridLines", "0");
            E(xml, "pane", ("ySplit", "10"), ("topLeftCell", "A11"), ("activePane", "bottomLeft"), ("state", "frozen"));
            xml.WriteEndElement(); xml.WriteEndElement();
            xml.WriteStartElement("cols", Ns);
            for (var col = 0; col < headers.Length; col++)
                E(xml, "col", ("min", I(col + 1)), ("max", I(col + 1)), ("width", I(widths[col])), ("customWidth", "1"));
            xml.WriteEndElement();
            xml.WriteStartElement("sheetData", Ns);
            Row(xml, 1, 36, () => Cell(xml, "A1", "WORKTRAIL · " + T(details ? "DetailsSheet" : "Title")
                + (preview ? " · " + strings.Translate("Export.Preview") : ""), 2));
            Row(xml, 3, 24, () => Cell(xml, "A3", T("Consultant") + ": " + options.Consultant + "    ·    " + T("Client") + ": " + options.Client));
            Row(xml, 4, 24, () => Cell(xml, "A4", $"{options.Sources.Options.From:yyyy-MM-dd} — {options.Sources.Options.ToInclusive:yyyy-MM-dd}    ·    {options.Sources.Options.TimeZoneId}"));
            Row(xml, 5, 24, () =>
            {
                Cell(xml, "A5", T("Rate")); Cell(xml, "C5", options.HourlyRate, 6);
                Cell(xml, "E5", options.HourlyRate.HasValue ? options.Currency : T("NoBilling"));
            });
            Row(xml, 6, 24, () => { Cell(xml, "A6", T("Active")); Cell(xml, "D6", T("Idle")); Cell(xml, "F6", T("Days")); });
            var active = rows.Sum(row => row.ActiveSeconds) / 86400d;
            Row(xml, 7, 28, () =>
            {
                Cell(xml, "A7", active, 9, !details && rows.Length > 0 ? $"SUM(C{first}:C{last})" : null);
                Cell(xml, "D7", rows.Sum(row => row.IdleSeconds) / 86400d, 9);
                Cell(xml, "F7", active * 3, 6, "A7*3");
            });
            Row(xml, 9, 40, () => Cell(xml, "A9", T("ReviewNote")));
            Row(xml, 10, 30, () =>
            {
                for (var column = 0; column < headers.Length; column++)
                    Cell(xml, ReportExportWriter.ColumnName(column + 1) + "10", T(headers[column])
                        + (headers[column] == "Amount" ? " (" + options.Currency + ")" : ""), 1);
            });
            for (var i = 0; i < rows.Length; i++)
            {
                token.ThrowIfCancellationRequested();
                var row = rows[i]; var number = first + i;
                Row(xml, number, details ? 60 : 75, () =>
                {
                    object?[] values = details
                        ? [row.Id, row.FirstObserved.DateTime.ToOADate(), row.LastObserved.DateTime.ToOADate(), row.IdleSeconds / 86400d,
                            row.Applications, row.References, SourceLabel(options, row, strings), T("State." + row.State)]
                        : [row.Date.ToDateTime(TimeOnly.MinValue).ToOADate(), T("Part." + row.Part), row.ActiveSeconds / 86400d,
                            options.Client, string.IsNullOrEmpty(row.Project) ? T("Unassigned") : row.Project,
                            row.State == "completed" ? row.Description : T("State." + row.State), "", row.Id,
                            options.HourlyRate.HasValue ? decimal.Round((decimal)row.ActiveSeconds / 3600m * options.HourlyRate.Value, 2) : null];
                    for (var column = 0; column < headers.Length; column++)
                    {
                        var style = details ? column is 1 or 2 ? 5 : column == 3 ? 3 : 0
                            : column == 0 ? 4 : column == 2 ? 3 : column == 6 ? 7 : column == 8 ? 6 : 0;
                        Cell(xml, ReportExportWriter.ColumnName(column + 1) + I(number), values[column], style,
                            !details && column == 8 ? $"ROUND(C{number}*24*$C$5,2)" : null);
                    }
                });
            }
            if (!details && rows.Length > 0)
                Row(xml, last + 1, 30, () =>
                {
                    Cell(xml, "A" + I(last + 1), T("Total"), 8);
                    Cell(xml, "C" + I(last + 1), active, 9, $"SUM(C{first}:C{last})");
                    if (options.HourlyRate is { } rate)
                        Cell(xml, "I" + I(last + 1), rows.Sum(row => decimal.Round((decimal)row.ActiveSeconds / 3600m * rate, 2)), 6, $"SUM(I{first}:I{last})");
                });
            xml.WriteEndElement();
            if (rows.Length > 0) E(xml, "autoFilter", ("ref", $"A10:{lastColumn}{last}"));
            xml.WriteStartElement("mergeCells", Ns);
            foreach (var number in new[] { 1, 3, 4, 9 }) E(xml, "mergeCell", ("ref", $"A{number}:{lastColumn}{number}"));
            xml.WriteEndElement();
            E(xml, "pageMargins", ("left", "0.25"), ("right", "0.25"), ("top", "0.4"), ("bottom", "0.4"), ("header", "0.2"), ("footer", "0.2"));
            E(xml, "pageSetup", ("orientation", "landscape"), ("paperSize", "9"), ("fitToWidth", "1"), ("fitToHeight", "0"));
            xml.WriteEndElement();
        });

    // options identifies the allowed source categories; row supplies the measured source count.
    // strings localizes labels without changing recorded values.
    private static string SourceLabel(TimesheetOptions options, TimesheetRow row, LocalizationService strings) =>
        row.SourceCount.ToString(CultureInfo.InvariantCulture) + " · " + string.Join(" / ", new[]
        {
            options.Sources.IncludeDescriptionExcerpt || options.Sources.IncludeCompleteDescription ? strings.Translate("Export.SavedDescriptions") : null,
            options.Sources.IncludeOcr ? "OCR" : null,
            options.Sources.IncludeWindowTitles ? strings.Translate("Export.Titles") : null
        }.OfType<string>());

    // zip receives a compact style palette with genuine numeric date/time/currency formats.
    private static void Styles(ZipArchive zip) => ReportExportWriter.WriteXml(zip, "xl/styles.xml", xml =>
    {
        xml.WriteStartElement("styleSheet", Ns);
        xml.WriteStartElement("numFmts", Ns);
        E(xml, "numFmt", ("numFmtId", "164"), ("formatCode", "[h]:mm"));
        E(xml, "numFmt", ("numFmtId", "165"), ("formatCode", "yyyy-mm-dd hh:mm"));
        xml.WriteEndElement();
        xml.WriteStartElement("fonts", Ns);
        for (var i = 0; i < 3; i++)
        {
            xml.WriteStartElement("font", Ns); E(xml, "sz", ("val", i == 2 ? "20" : "11")); E(xml, "name", ("val", "Calibri"));
            E(xml, "color", ("rgb", i == 1 ? "FFFFFFFF" : i == 2 ? "FF6038A0" : "FF252233"));
            if (i > 0) E(xml, "b"); xml.WriteEndElement();
        }
        xml.WriteEndElement(); xml.WriteStartElement("fills", Ns);
        foreach (var fill in new[] { "none", "gray125", "FF58417F", "FFFFF8DB", "FFF0EAF8" })
        {
            xml.WriteStartElement("fill", Ns); xml.WriteStartElement("patternFill", Ns);
            xml.WriteAttributeString("patternType", fill.StartsWith("FF", StringComparison.Ordinal) ? "solid" : fill);
            if (fill.StartsWith("FF", StringComparison.Ordinal)) { E(xml, "fgColor", ("rgb", fill)); E(xml, "bgColor", ("indexed", "64")); }
            xml.WriteEndElement(); xml.WriteEndElement();
        }
        xml.WriteEndElement(); xml.WriteStartElement("borders", Ns); E(xml, "border"); xml.WriteEndElement();
        xml.WriteStartElement("cellStyleXfs", Ns); E(xml, "xf", ("numFmtId", "0"), ("fontId", "0"), ("fillId", "0"), ("borderId", "0")); xml.WriteEndElement();
        xml.WriteStartElement("cellXfs", Ns);
        foreach (var style in new[] { (0, 0, 0), (0, 1, 2), (0, 2, 0), (164, 0, 0), (14, 0, 0), (165, 0, 0), (4, 0, 0), (0, 0, 3), (0, 0, 4), (164, 0, 4) })
        {
            xml.WriteStartElement("xf", Ns); xml.WriteAttributeString("numFmtId", I(style.Item1)); xml.WriteAttributeString("fontId", I(style.Item2));
            xml.WriteAttributeString("fillId", I(style.Item3)); xml.WriteAttributeString("borderId", "0"); xml.WriteAttributeString("xfId", "0");
            xml.WriteAttributeString("applyNumberFormat", "1"); xml.WriteAttributeString("applyAlignment", "1");
            E(xml, "alignment", ("vertical", "center"), ("wrapText", "1")); xml.WriteEndElement();
        }
        xml.WriteEndElement(); xml.WriteStartElement("cellStyles", Ns); E(xml, "cellStyle", ("name", "Normal"), ("xfId", "0"), ("builtinId", "0")); xml.WriteEndElement();
        xml.WriteEndElement();
    });

    // xml writes a cell; reference is constructed internally, never from source text.
    // value remains inert inline text or a typed number; style selects the fixed palette.
    // formula is exclusively an internally constructed arithmetic expression with a cached value.
    private static void Cell(XmlWriter xml, string reference, object? value, int style = 0, string? formula = null)
    {
        if (value is null) return;
        xml.WriteStartElement("c", Ns); xml.WriteAttributeString("r", reference); xml.WriteAttributeString("s", I(style));
        if (value is string text)
        {
            xml.WriteAttributeString("t", "inlineStr"); xml.WriteStartElement("is", Ns); xml.WriteStartElement("t", Ns);
            xml.WriteAttributeString("xml", "space", "http://www.w3.org/XML/1998/namespace", "preserve");
            xml.WriteString(ReportExportWriter.ExcelText(text)); xml.WriteEndElement(); xml.WriteEndElement();
        }
        else
        {
            if (formula is not null) xml.WriteElementString("f", Ns, formula);
            xml.WriteElementString("v", Ns, Convert.ToString(value, CultureInfo.InvariantCulture));
        }
        xml.WriteEndElement();
    }

    // xml and number identify the row; height is in points and write emits its cells.
    private static void Row(XmlWriter xml, int number, int height, Action write)
    {
        xml.WriteStartElement("row", Ns); xml.WriteAttributeString("r", I(number)); xml.WriteAttributeString("ht", I(height)); xml.WriteAttributeString("customHeight", "1");
        write(); xml.WriteEndElement();
    }

    // xml, name and attributes describe one SpreadsheetML element.
    private static void E(XmlWriter xml, string name, params (string Name, string Value)[] attributes) => ReportExportWriter.Element(xml, name, Ns, attributes);
    // value is an internal counter, formatted independently of the UI culture.
    private static string I(int value) => value.ToString(CultureInfo.InvariantCulture);
}
