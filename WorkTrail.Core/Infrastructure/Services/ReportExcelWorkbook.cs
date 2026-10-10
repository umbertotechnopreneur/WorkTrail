// SPDX-License-Identifier: MIT
/* VBWR B
 *
 * Project: WorkTrail
 * Repository: https://github.com/umbertotechnopreneur/WorkTrail
 * Creator: Umberto Giacobbi | https://umbertogiacobbi.biz
 *
 * VibeWare: Human intent, AI execution, and plenty of tokens
 * Manifesto: https://umbertogiacobbi.biz/vibeware/manifesto
 *
 * Modified with AI: OpenAI Codex; added this header on 2026-10-10.
 * Human guidance: Umberto Giacobbi; requested VibeWare branding.
 *
 * Copyright (c) 2026 Umberto Giacobbi
 * License: MIT - see LICENSE
 *
 * VBWR E */


using System.Globalization;
using System.IO.Compression;
using System.Xml;
using WorkTrail.Application;

namespace WorkTrail.Services;

/// <summary>Applies the shared printed-report layout without changing the selected data.</summary>
internal sealed class ReportExcelWorkbook(LocalizationService strings, ReportExportOptions options, int? rowLimit)
{
    private const string Ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private readonly List<ExcelReportSheet> _sheets = [];
    private readonly List<object?[]> _textParts = [];
    internal string T(string key) => strings.Translate("Workbook." + key);
    internal string CoverName => T("Cover");
    internal string PartsName => T("TextParts");
    internal string Metadata { get; set; } = "";
    internal int? RowLimit => rowLimit;

    // sheet supplies a bounded source projection and its presentation metadata.
    internal void Add(ExcelReportSheet sheet) => _sheets.Add(sheet);

    // stream owns the atomic output file.
    // token cancels every data row before publication.
    internal int Write(Stream stream, CancellationToken token)
    {
        using var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true);
        var printAreas = new List<string> { "" };
        for (var index = 0; index < _sheets.Count; index++)
            printAreas.Add("$A$1:$" + ReportExportWriter.ColumnName(_sheets[index].Columns.Count) + "$" + I(WriteSheet(zip, index + 2, _sheets[index], token)));
        if (_textParts.Count > 0)
        {
            var parts = new ExcelReportSheet(PartsName, PartsName, T("LongTextHelp"),
                [new("sheet", 22), new("row", 10, ExcelFormat.Number), new("column", 26), new("part", 10, ExcelFormat.Number), new("text", 100)],
                () => _textParts);
            _sheets.Add(parts);
            printAreas.Add("$A$1:$E$" + I(WriteSheet(zip, _sheets.Count + 1, parts, token)));
        }
        WriteCover(zip);
        var names = new[] { CoverName }.Concat(_sheets.Select(sheet => sheet.Name)).ToArray();
        printAreas[0] = "$A$1:$H$" + I(21 + (_sheets.Count + 1) / 2);
        var repeatedColumns = new[] { "" }.Concat(_sheets.Select(sheet => HasHorizontalPages(sheet) ? "$A:$B" : "")).ToArray();
        ReportExportWriter.WriteWorkbookPackage(zip, names, recalculateOnOpen: true, printAreas: printAreas,
            includeBrandMark: true, includeTheme: true, repeatedColumns: repeatedColumns);
        ReportExcelTheme.Write(zip, options.Theme);
        WriteStyles(zip);
        WriteBrandMark(zip);
        return names.Length;
    }

    // zip receives one worksheet.
    // index is its stable package identifier.
    // sheet contains trusted layout choices and inert source cells.
    // token cancels streaming generation.
    private int WriteSheet(ZipArchive zip, int index, ExcelReportSheet sheet, CancellationToken token)
    {
        var lastRow = 7;
        ReportExportWriter.WriteXml(zip, $"xl/worksheets/sheet{index}.xml", xml =>
        {
            var links = new List<(string Cell, string Location)>();
            var columns = sheet.Columns;
            var lastColumn = ReportExportWriter.ColumnName(columns.Count);
            xml.WriteStartElement("worksheet", Ns);
            xml.WriteStartElement("sheetPr", Ns);
            E(xml, "pageSetUpPr", ("fitToPage", "1")); xml.WriteEndElement();
            Views(xml, 7);
            xml.WriteStartElement("cols", Ns);
            for (var column = 0; column < columns.Count; column++)
                E(xml, "col", ("min", I(column + 1)), ("max", I(column + 1)), ("width", N(columns[column].Width)), ("customWidth", "1"));
            xml.WriteEndElement();
            xml.WriteStartElement("sheetData", Ns);
            Row(xml, 1, 32, () => Cell(xml, "A1", "WORKTRAIL  /  " + sheet.Title, (int)ExcelFormat.Title));
            Row(xml, 3, Math.Max(38, Height(sheet.Description, columns.Sum(column => column.Width), false)), () => Cell(xml, "A3", sheet.Description, (int)ExcelFormat.Muted));
            Row(xml, 4, 24, () => Cell(xml, "A4", $"{options.From:yyyy-MM-dd} — {options.ToInclusive:yyyy-MM-dd}  ·  {options.TimeZoneId}", (int)ExcelFormat.Muted));
            Row(xml, 5, Math.Max(36, Height(sheet.Metadata, columns.Sum(column => column.Width), false)), () => Cell(xml, "A5", sheet.Metadata, (int)ExcelFormat.Text));
            Row(xml, 6, 22, () => Cell(xml, "A6", CoverName, (int)ExcelFormat.Link));
            links.Add(("A6", Location(CoverName, "A1")));
            Row(xml, 7, 30, () =>
            {
                for (var column = 0; column < columns.Count; column++)
                    Cell(xml, ReportExportWriter.ColumnName(column + 1) + "7", columns[column].Header, (int)ExcelFormat.Header);
            });
            var rows = rowLimit is { } limit ? sheet.Rows().Take(limit) : sheet.Rows();
            foreach (var values in rows)
            {
                token.ThrowIfCancellationRequested();
                if (values.Length != columns.Count) throw new InvalidDataException("Excel row does not match its declared columns.");
                if (++lastRow >= 1_048_576) throw new ReportExportValidationException("Export.RangeTooLarge");
                var height = Math.Max(30, values.Select((value, column) => Height(value, columns[column].Width, sheet.Name != PartsName)).DefaultIfEmpty(30).Max());
                Row(xml, lastRow, height, () =>
                {
                    for (var column = 0; column < values.Length; column++)
                    {
                        var value = values[column];
                        var cell = value as ExcelReportCell;
                        value = cell is null ? value : cell.Value;
                        var reference = ReportExportWriter.ColumnName(column + 1) + I(lastRow);
                        var link = cell?.Location;
                        // Keep the reading view compact; the appendix preserves every character.
                        if (sheet.Name != PartsName && value is string text && text.Length > 900)
                        {
                            link = rowLimit is null || _textParts.Count < rowLimit ? Location(PartsName, "A" + I(8 + _textParts.Count)) : null;
                            var offset = 0;
                            var part = 0;
                            while (offset < text.Length)
                            {
                                token.ThrowIfCancellationRequested();
                                var length = Math.Min(1200, text.Length - offset);
                                if (char.IsHighSurrogate(text[offset + length - 1])) length--;
                                _textParts.Add([sheet.Name, lastRow, columns[column].Header, ++part, text.Substring(offset, length)]);
                                offset += length;
                            }
                            value = ReportExportService.Excerpt(text, 400) + "\n→ " + PartsName;
                        }
                        if (link is not null) links.Add((reference, link));
                        var format = cell?.Format ?? columns[column].Format;
                        var style = (int)format + (lastRow % 2 == 0 && format <= ExcelFormat.Link ? 15 : 0);
                        Cell(xml, reference, value, style, cell?.Formula);
                    }
                });
            }
            var lastDataRow = lastRow;
            if (sheet.DurationColumn is { } durationColumn && lastDataRow > 7)
            {
                var totals = sheet.Totals?.Invoke(rowLimit) ?? (0d, 0m);
                Row(xml, ++lastRow, 30, () =>
                {
                    Cell(xml, "A" + I(lastRow), T("Total"), (int)ExcelFormat.Total);
                    var duration = ReportExportWriter.ColumnName(durationColumn);
                    Cell(xml, duration + I(lastRow), totals.Item1, (int)ExcelFormat.TotalDuration, $"SUM({duration}8:{duration}{lastDataRow})");
                    if (sheet.AmountColumn is { } amountColumn)
                    {
                        var amount = ReportExportWriter.ColumnName(amountColumn);
                        var sum = $"SUM({amount}8:{amount}{lastDataRow})";
                        var formula = sheet.AmountCondition is { } condition ? $"IF({condition},{sum},\"\")" : sum;
                        Cell(xml, amount + I(lastRow), totals.Item2, (int)ExcelFormat.Money, formula);
                    }
                });
            }
            xml.WriteEndElement();
            if (lastDataRow > 7) E(xml, "autoFilter", ("ref", $"A7:{lastColumn}{lastDataRow}"));
            xml.WriteStartElement("mergeCells", Ns);
            if (columns.Count > 1)
                foreach (var row in new[] { 1, 3, 4, 5 }) E(xml, "mergeCell", ("ref", $"A{row}:{lastColumn}{row}"));
            xml.WriteEndElement();
            Hyperlinks(xml, links);
            Print(xml, portrait: columns.Count <= 3 && columns.Sum(column => column.Width) <= 70, wide: HasHorizontalPages(sheet));
            xml.WriteEndElement();
        });
        return lastRow;
    }

    // zip receives the single-page A4 introduction and navigation links.
    private void WriteCover(ZipArchive zip) => ReportExportWriter.WriteXml(zip, "xl/worksheets/sheet1.xml", xml =>
    {
        xml.WriteStartElement("worksheet", Ns);
        xml.WriteAttributeString("xmlns", "r", null, "http://schemas.openxmlformats.org/officeDocument/2006/relationships");
        xml.WriteStartElement("sheetPr", Ns); E(xml, "pageSetUpPr", ("fitToPage", "1")); xml.WriteEndElement();
        Views(xml, 0);
        xml.WriteStartElement("cols", Ns); E(xml, "col", ("min", "1"), ("max", "8"), ("width", "11"), ("customWidth", "1")); xml.WriteEndElement();
        xml.WriteStartElement("sheetData", Ns);
        Row(xml, 1, 38, () => Cell(xml, "A1", "WORKTRAIL", (int)ExcelFormat.Title));
        Row(xml, 3, 28, () => Cell(xml, "A3", "Track your time. Find what you worked on.", (int)ExcelFormat.Muted));
        Row(xml, 5, 32, () => Cell(xml, "A5", T("CoverTitle") + (rowLimit.HasValue ? "  ·  " + strings.Translate("Export.Preview") : ""), (int)ExcelFormat.Title));
        Row(xml, 7, 24, () => Cell(xml, "A7", $"{options.From:yyyy-MM-dd} — {options.ToInclusive:yyyy-MM-dd}  ·  {options.TimeZoneId}"));
        Row(xml, 8, 32, () => Cell(xml, "A8", Metadata));
        Row(xml, 9, 24, () => Cell(xml, "A9", "https://umbertogiacobbi.biz/", (int)ExcelFormat.Link));
        Row(xml, 11, 28, () => Cell(xml, "A11", T("HowTo"), (int)ExcelFormat.Total));
        var instructions = T("Instructions") + "\n" + T("ThemeHelp");
        Row(xml, 12, Math.Max(76, Height(instructions, 88, false)), () => Cell(xml, "A12", instructions));
        Row(xml, 15, 28, () => Cell(xml, "A15", T("Provenance"), (int)ExcelFormat.Total));
        Row(xml, 16, 86, () => Cell(xml, "A16", T("ProvenanceHelp")));
        Row(xml, 19, 32, () => Cell(xml, "A19", rowLimit.HasValue ? strings.Translate("Timesheet.PreviewHelp") : T("PrivacyHelp"), (int)ExcelFormat.Muted));
        Row(xml, 21, 26, () => Cell(xml, "A21", T("Contents"), (int)ExcelFormat.Total));
        for (var index = 0; index < _sheets.Count; index += 2)
        {
            var first = index;
            Row(xml, 22 + index / 2, 26, () =>
            {
                Cell(xml, "A" + I(22 + first / 2), _sheets[first].Title, (int)ExcelFormat.Link);
                if (first + 1 < _sheets.Count) Cell(xml, "E" + I(22 + first / 2), _sheets[first + 1].Title, (int)ExcelFormat.Link);
            });
        }
        xml.WriteEndElement();
        xml.WriteStartElement("mergeCells", Ns);
        foreach (var row in new[] { 1, 3, 5, 7, 8, 9, 11, 12, 15, 16, 19, 21 })
            E(xml, "mergeCell", ("ref", $"A{row}:{(row <= 3 ? "F" : "H")}{row}"));
        for (var index = 0; index < _sheets.Count; index++)
        {
            var row = I(22 + index / 2);
            E(xml, "mergeCell", ("ref", index % 2 == 0 ? $"A{row}:D{row}" : $"E{row}:H{row}"));
        }
        xml.WriteEndElement();
        Hyperlinks(xml, _sheets.Select((sheet, index) => ((index % 2 == 0 ? "A" : "E") + I(22 + index / 2), Location(sheet.Name, "A1"))), includeWebsite: true);
        Print(xml, portrait: true, onePage: true);
        xml.WriteStartElement("drawing", Ns); xml.WriteAttributeString("r", "id", "http://schemas.openxmlformats.org/officeDocument/2006/relationships", "brand"); xml.WriteEndElement();
        xml.WriteEndElement();
    });

    // zip owns the package; the artwork is an embedded project brandmark, never an app icon or screenshot.
    private static void WriteBrandMark(ZipArchive zip)
    {
        using (var source = typeof(ReportExcelWorkbook).Assembly.GetManifestResourceStream("WorkTrail.Report.brandmark.png")
            ?? throw new InvalidDataException("The report brandmark is missing."))
        using (var image = zip.CreateEntry("xl/media/brandmark.png").Open()) source.CopyTo(image);
        const string rel = "http://schemas.openxmlformats.org/package/2006/relationships";
        const string office = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        ReportExportWriter.WriteXml(zip, "xl/worksheets/_rels/sheet1.xml.rels", xml =>
        {
            xml.WriteStartElement("Relationships", rel);
            ReportExportWriter.Element(xml, "Relationship", rel, ("Id", "brand"), ("Type", office + "/drawing"), ("Target", "../drawings/brandmark.xml"));
            ReportExportWriter.Element(xml, "Relationship", rel, ("Id", "website"), ("Type", office + "/hyperlink"), ("Target", "https://umbertogiacobbi.biz/"), ("TargetMode", "External"));
            xml.WriteEndElement();
        });
        ReportExportWriter.WriteXml(zip, "xl/drawings/_rels/brandmark.xml.rels", xml =>
        {
            xml.WriteStartElement("Relationships", rel);
            ReportExportWriter.Element(xml, "Relationship", rel, ("Id", "image"), ("Type", office + "/image"), ("Target", "../media/brandmark.png")); xml.WriteEndElement();
        });
        var drawing = zip.CreateEntry("xl/drawings/brandmark.xml");
        using var writer = new StreamWriter(drawing.Open());
        writer.Write("""
            <?xml version="1.0" encoding="utf-8"?>
            <xdr:wsDr xmlns:xdr="http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing" xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
              <xdr:oneCellAnchor><xdr:from><xdr:col>6</xdr:col><xdr:colOff>0</xdr:colOff><xdr:row>0</xdr:row><xdr:rowOff>0</xdr:rowOff></xdr:from><xdr:ext cx="762000" cy="762000"/><xdr:pic><xdr:nvPicPr><xdr:cNvPr id="1" name="WorkTrail brandmark" descr="WorkTrail timeline and recall brand artwork"/><xdr:cNvPicPr><a:picLocks noChangeAspect="1"/></xdr:cNvPicPr></xdr:nvPicPr><xdr:blipFill><a:blip r:embed="image"/><a:stretch><a:fillRect/></a:stretch></xdr:blipFill><xdr:spPr><a:xfrm><a:off x="0" y="0"/><a:ext cx="762000" cy="762000"/></a:xfrm><a:prstGeom prst="rect"><a:avLst/></a:prstGeom></xdr:spPr></xdr:pic><xdr:clientData/></xdr:oneCellAnchor>
            </xdr:wsDr>
            """);
    }

    // zip receives theme-based styles and locale-aware numeric formats.
    private void WriteStyles(ZipArchive zip) => ReportExportWriter.WriteXml(zip, "xl/styles.xml", xml =>
    {
        xml.WriteStartElement("styleSheet", Ns);
        xml.WriteStartElement("numFmts", Ns);
        var locale = strings.Culture.LCID.ToString("X", CultureInfo.InvariantCulture);
        var formats = new[] { "[h]:mm", $"[$-{locale}]ddd, dd mmm yyyy", $"[$-{locale}]dd mmm yyyy hh:mm", "#,##0.00", "0.0%" };
        for (var index = 0; index < formats.Length; index++) E(xml, "numFmt", ("numFmtId", I(164 + index)), ("formatCode", formats[index]));
        xml.WriteEndElement();
        xml.WriteStartElement("fonts", Ns);
        for (var index = 0; index < 5; index++)
        {
            xml.WriteStartElement("font", Ns); E(xml, "name", ("val", "Arial")); E(xml, "sz", ("val", index == 2 ? "19" : "11"));
            // SpreadsheetML uses 0 for light1 and 1 for dark1, independently of clrScheme order.
            if (index == 3) E(xml, "color", ("theme", "1"), ("tint", "0.3"));
            else E(xml, "color", ("theme", index == 1 ? "0" : index is 2 or 4 ? "4" : "1"));
            E(xml, "scheme", ("val", index == 2 ? "major" : "minor"));
            if (index is 1 or 2) E(xml, "b"); if (index == 4) E(xml, "u"); xml.WriteEndElement();
        }
        xml.WriteEndElement(); xml.WriteStartElement("fills", Ns);
        for (var fill = 0; fill < 6; fill++)
        {
            xml.WriteStartElement("fill", Ns); xml.WriteStartElement("patternFill", Ns);
            xml.WriteAttributeString("patternType", fill == 0 ? "none" : fill == 1 ? "gray125" : "solid");
            if (fill == 4) E(xml, "fgColor", ("rgb", "FFFFF3CD"));
            else if (fill == 2) E(xml, "fgColor", ("theme", "4"));
            else if (fill is 3 or 5) E(xml, "fgColor", ("theme", "4"), ("tint", fill == 3 ? "0.9" : "0.97"));
            xml.WriteEndElement(); xml.WriteEndElement();
        }
        xml.WriteEndElement(); xml.WriteStartElement("borders", Ns); E(xml, "border"); xml.WriteEndElement();
        xml.WriteStartElement("cellStyleXfs", Ns); E(xml, "xf", ("numFmtId", "0"), ("fontId", "0"), ("fillId", "0"), ("borderId", "0")); xml.WriteEndElement();
        xml.WriteStartElement("cellXfs", Ns);
        for (var index = 0; index < 30; index++)
        {
            var format = (ExcelFormat)(index % 15);
            var font = format == ExcelFormat.Header ? 1 : format == ExcelFormat.Title ? 2 : format == ExcelFormat.Muted ? 3 : format == ExcelFormat.Link ? 4 : 0;
            var fill = format == ExcelFormat.Header ? 2 : format is ExcelFormat.Total or ExcelFormat.TotalDuration ? 3 : format == ExcelFormat.Editable ? 4 : index >= 15 ? 5 : 0;
            var number = format is ExcelFormat.Duration or ExcelFormat.TotalDuration ? 164 : format == ExcelFormat.Date ? 165 : format == ExcelFormat.DateTime ? 166 : format == ExcelFormat.Money ? 167 : format == ExcelFormat.Percent ? 168 : format == ExcelFormat.Number ? 3 : 0;
            xml.WriteStartElement("xf", Ns); xml.WriteAttributeString("numFmtId", I(number)); xml.WriteAttributeString("fontId", I(font));
            xml.WriteAttributeString("fillId", I(fill)); xml.WriteAttributeString("borderId", "0"); xml.WriteAttributeString("xfId", "0");
            xml.WriteAttributeString("applyNumberFormat", "1"); xml.WriteAttributeString("applyAlignment", "1");
            E(xml, "alignment", ("vertical", "center"), ("wrapText", "1")); xml.WriteEndElement();
        }
        xml.WriteEndElement(); xml.WriteStartElement("cellStyles", Ns); E(xml, "cellStyle", ("name", "Normal"), ("xfId", "0"), ("builtinId", "0")); xml.WriteEndElement(); xml.WriteEndElement();
    });

    // name identifies an internally generated sheet.
    // reference is an internally generated cell address.
    internal static string Location(string name, string reference) => "'" + name.Replace("'", "''", StringComparison.Ordinal) + "'!" + reference;

    // xml receives only typed values or inert inline text.
    // reference is an internally generated cell address.
    // value contains selected source data.
    // style identifies a declared cell format.
    // formula is trusted arithmetic constructed by the writer.
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
            if (value is bool) xml.WriteAttributeString("t", "b");
            if (formula is not null) xml.WriteElementString("f", Ns, formula);
            var number = value is DateOnly date ? date.ToDateTime(TimeOnly.MinValue).ToOADate()
                : value is DateTimeOffset time ? time.DateTime.ToOADate() : value;
            xml.WriteElementString("v", Ns, number is bool flag ? flag ? "1" : "0" : Convert.ToString(number, CultureInfo.InvariantCulture));
        }
        xml.WriteEndElement();
    }

    // value supplies visible text.
    // width estimates wrapped lines in the selected column.
    // compactText limits the reading view while the appendix preserves complete text.
    private static double Height(object? value, double width, bool compactText)
    {
        var text = (value as ExcelReportCell)?.Value as string ?? value as string;
        if (text is null) return 30;
        var length = compactText && text.Length > 900 ? 420 : text.Length;
        return Math.Clamp((Math.Ceiling(length / Math.Max(12, width - 3)) + Math.Min(6, text.Count(c => c == '\n'))) * 14 + 12, 30, 300);
    }

    // xml receives rows with a deliberate, printable height.
    // number is the one-based row address.
    // height is in points.
    // write emits the row's cells.
    private static void Row(XmlWriter xml, int number, double height, Action write)
    {
        xml.WriteStartElement("row", Ns); xml.WriteAttributeString("r", I(number)); xml.WriteAttributeString("ht", N(height)); xml.WriteAttributeString("customHeight", "1"); write(); xml.WriteEndElement();
    }

    // xml writes gridless views.
    // frozen is the number of fixed title/header rows.
    private static void Views(XmlWriter xml, int frozen)
    {
        xml.WriteStartElement("sheetViews", Ns); xml.WriteStartElement("sheetView", Ns); xml.WriteAttributeString("workbookViewId", "0"); xml.WriteAttributeString("showGridLines", "0");
        if (frozen > 0) E(xml, "pane", ("ySplit", I(frozen)), ("topLeftCell", "A" + I(frozen + 1)), ("activePane", "bottomLeft"), ("state", "frozen")); xml.WriteEndElement(); xml.WriteEndElement();
    }

    // xml writes navigation without turning source text into executable formulas.
    // links contains internally constructed sheet/cell locations.
    // includeWebsite adds the fixed public WorkTrail site on the cover only.
    private static void Hyperlinks(XmlWriter xml, IEnumerable<(string Cell, string Location)> links, bool includeWebsite = false)
    {
        xml.WriteStartElement("hyperlinks", Ns);
        foreach (var link in links) E(xml, "hyperlink", ("ref", link.Cell), ("location", link.Location));
        if (includeWebsite)
        {
            xml.WriteStartElement("hyperlink", Ns);
            xml.WriteAttributeString("ref", "A9");
            xml.WriteAttributeString("r", "id", "http://schemas.openxmlformats.org/officeDocument/2006/relationships", "website");
            xml.WriteEndElement();
        }
        xml.WriteEndElement();
    }

    // xml receives A4 settings.
    // portrait selects the orientation for narrow sheets and the cover.
    // wide allows horizontal paging instead of shrinking technical text.
    // onePage limits only the cover to a single page vertically.
    private static void Print(XmlWriter xml, bool portrait, bool wide = false, bool onePage = false)
    {
        E(xml, "pageMargins", ("left", "0.25"), ("right", "0.25"), ("top", "0.4"), ("bottom", "0.4"), ("header", "0.2"), ("footer", "0.2"));
        E(xml, "pageSetup", ("orientation", portrait ? "portrait" : "landscape"), ("paperSize", "9"), ("fitToWidth", wide ? "0" : "1"), ("fitToHeight", onePage ? "1" : "0"));
        xml.WriteStartElement("headerFooter", Ns); xml.WriteElementString("oddFooter", Ns, "&LWorkTrail&R&P / &N"); xml.WriteEndElement();
    }

    // sheet identifies wide technical tables whose columns need separate horizontal pages.
    private static bool HasHorizontalPages(ExcelReportSheet sheet) => sheet.Columns.Count >= 9 && sheet.Columns.Sum(column => column.Width) > 200;

    // xml writes the SpreadsheetML element.
    // name identifies the element.
    // attributes provides its trusted layout attributes.
    private static void E(XmlWriter xml, string name, params (string Name, string Value)[] attributes) => ReportExportWriter.Element(xml, name, Ns, attributes);
    private static string I(int value) => value.ToString(CultureInfo.InvariantCulture);
    private static string N(double value) => value.ToString(CultureInfo.InvariantCulture);
}

internal enum ExcelFormat { Text, Header, Title, Duration, Date, DateTime, Number, Money, Editable, Total, TotalDuration, Percent, Muted, Link }
internal sealed record ExcelReportColumn(string Header, double Width, ExcelFormat Format = ExcelFormat.Text);
internal sealed record ExcelReportCell(object? Value, ExcelFormat? Format = null, string? Formula = null, string? Location = null);
internal sealed record ExcelReportSheet(string Name, string Title, string Description, IReadOnlyList<ExcelReportColumn> Columns,
    Func<IEnumerable<object?[]>> Rows, string Metadata = "", int? DurationColumn = null, int? AmountColumn = null,
    Func<int?, (double, decimal)>? Totals = null, string? AmountCondition = null);
