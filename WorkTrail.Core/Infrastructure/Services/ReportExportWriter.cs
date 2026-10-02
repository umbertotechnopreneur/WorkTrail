// SPDX-License-Identifier: MIT

using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Xml;
using WorkTrail.Application;

namespace WorkTrail.Services;

/// <summary>Writes selected analytical tables with bounded memory and atomic destination replacement.</summary>
internal static class ReportExportWriter
{
    private const string Spreadsheet = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private const string Relationships = "http://schemas.openxmlformats.org/package/2006/relationships";
    private const string OfficeRelationships = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

    internal static ReportExportResult Write(ExportDocument document, string destination, bool overwrite, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(destination) || !Path.IsPathFullyQualified(destination)
            || !string.Equals(Path.GetExtension(destination), ReportExportFileNames.Extension(document.Options.Format), StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("The export destination or extension is invalid.");
        var path = Path.GetFullPath(destination);
        var tableCount = ReportExportService.Tables(document).Count;
        AtomicWrite(path, overwrite, stream =>
        {
            tableCount = document.Options.Format switch
            {
                ReportExportFormat.Excel => WriteExcel(stream, document, cancellationToken),
                ReportExportFormat.Csv => WriteCsv(stream, document, cancellationToken),
                ReportExportFormat.Json => WriteJson(stream, document, cancellationToken),
                _ => throw new ArgumentOutOfRangeException(nameof(document))
            };
        }, cancellationToken);
        return new(path, new FileInfo(path).Length, tableCount);
    }

    internal static void AtomicWrite(string destination, bool overwrite, Action<Stream> write, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var path = Path.GetFullPath(destination);
        var directory = Path.GetDirectoryName(path) ?? throw new ArgumentException("A destination directory is required.");
        if (!Directory.Exists(directory)) throw new DirectoryNotFoundException("The destination directory does not exist.");
        if (!overwrite && File.Exists(path)) throw new IOException("The destination already exists.");
        var temporary = Path.Combine(directory, $".worktrail-export-{Guid.NewGuid():N}.tmp");
        try
        {
            // A sibling temporary file keeps failures/cancellation from replacing an existing user document.
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, FileOptions.SequentialScan))
            {
                write(stream);
                stream.Flush(flushToDisk: true);
            }
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporary, path, overwrite);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private static int WriteJson(Stream stream, ExportDocument document, CancellationToken cancellationToken)
    {
        using var json = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true });
        json.WriteStartObject();
        json.WriteNumber("schemaVersion", 1);
        json.WriteString("createdAt", DateTimeOffset.UtcNow);
        json.WriteString("from", document.Options.From.ToString("yyyy-MM-dd"));
        json.WriteString("toInclusive", document.Options.ToInclusive.ToString("yyyy-MM-dd"));
        json.WriteString("timeZoneId", document.Options.TimeZoneId);
        json.WriteStartObject("tables");
        var tables = ReportExportService.Tables(document);
        foreach (var table in tables)
        {
            json.WriteStartArray(table.Name);
            foreach (var row in table.Rows())
            {
                cancellationToken.ThrowIfCancellationRequested();
                json.WriteStartObject();
                for (var index = 0; index < table.Columns.Count; index++)
                {
                    json.WritePropertyName(table.Columns[index]);
                    JsonSerializer.Serialize(json, row[index]);
                }
                json.WriteEndObject();
            }
            json.WriteEndArray();
        }
        json.WriteEndObject();
        json.WriteEndObject();
        return tables.Count;
    }

    private static int WriteCsv(Stream stream, ExportDocument document, CancellationToken cancellationToken)
    {
        using var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true);
        var tables = ReportExportService.Tables(document);
        foreach (var table in tables)
        {
            using var entry = zip.CreateEntry(table.Name + ".csv").Open();
            using var writer = new StreamWriter(entry, new UTF8Encoding(true)) { NewLine = "\r\n" };
            writer.WriteLine(string.Join(document.Options.CsvSeparator, table.Columns.Select(CsvText)));
            foreach (var row in table.Rows())
            {
                cancellationToken.ThrowIfCancellationRequested();
                writer.WriteLine(string.Join(document.Options.CsvSeparator, row.Select(value =>
                    value is string text ? CsvText(text) : CsvQuoted(Convert.ToString(value, CultureInfo.InvariantCulture) ?? ""))));
            }
        }
        return tables.Count;
    }

    internal static string CsvText(string text)
    {
        // Treat exported titles/OCR/descriptions as inert text, even after whitespace before formula triggers.
        var leading = text.AsSpan().TrimStart();
        if ((!leading.IsEmpty && leading[0] is '=' or '+' or '-' or '@') || text.StartsWith('\t') || text.StartsWith('\r') || text.StartsWith('\n'))
            text = "'" + text;
        return CsvQuoted(text);
    }

    private static string CsvQuoted(string text) => "\"" + text.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";

    private static int WriteExcel(Stream stream, ExportDocument document, CancellationToken cancellationToken)
    {
        using var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true);
        var tables = ReportExportService.Tables(document).ToList();
        var overflow = new List<object?[]>();
        for (var index = 0; index < tables.Count; index++)
            WriteSheet(zip, index + 1, tables[index], overflow, cancellationToken);
        if (overflow.Count > 0)
        {
            if (document.RowLimit is { } limit) overflow = overflow.Take(limit).ToList();
            // Excel limits cell text to 32,767 characters. Preserve complete source text in ordered parts.
            tables.Add(new("Text parts", ["sheet", "row", "column", "part", "text"], () => overflow, overflow.Count));
            WriteSheet(zip, tables.Count, tables[^1], [], cancellationToken);
        }
        WriteWorkbookPackage(zip, tables.Select(table => table.Name).ToArray());
        WriteStyles(zip);
        return tables.Count;
    }

    // zip owns completed worksheets.
    // names defines their stable package order.
    // recalculateOnOpen asks Excel to recompute trusted arithmetic formulas in editable reports.
    internal static void WriteWorkbookPackage(ZipArchive zip, IReadOnlyList<string> names, bool recalculateOnOpen = false)
    {
        if (names.Count == 0 || names.Distinct(StringComparer.OrdinalIgnoreCase).Count() != names.Count
            || names.Any(name => string.IsNullOrWhiteSpace(name) || name.Length > 31 || name.IndexOfAny(['[', ']', ':', '*', '?', '/', '\\']) >= 0))
            throw new ArgumentException("Invalid Excel sheet names.");
        WriteXml(zip, "[Content_Types].xml", xml =>
        {
            const string types = "http://schemas.openxmlformats.org/package/2006/content-types";
            xml.WriteStartElement("Types", types);
            Element(xml, "Default", types, ("Extension", "rels"), ("ContentType", "application/vnd.openxmlformats-package.relationships+xml"));
            Element(xml, "Default", types, ("Extension", "xml"), ("ContentType", "application/xml"));
            Element(xml, "Override", types, ("PartName", "/xl/workbook.xml"), ("ContentType", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"));
            Element(xml, "Override", types, ("PartName", "/xl/styles.xml"), ("ContentType", "application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml"));
            for (var index = 1; index <= names.Count; index++)
                Element(xml, "Override", types, ("PartName", $"/xl/worksheets/sheet{index}.xml"), ("ContentType", "application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"));
            xml.WriteEndElement();
        });
        WriteXml(zip, "_rels/.rels", xml =>
        {
            xml.WriteStartElement("Relationships", Relationships);
            Element(xml, "Relationship", Relationships, ("Id", "rId1"), ("Type", OfficeRelationships + "/officeDocument"), ("Target", "xl/workbook.xml"));
            xml.WriteEndElement();
        });
        WriteXml(zip, "xl/_rels/workbook.xml.rels", xml =>
        {
            xml.WriteStartElement("Relationships", Relationships);
            for (var index = 1; index <= names.Count; index++)
                Element(xml, "Relationship", Relationships, ("Id", $"rId{index}"), ("Type", OfficeRelationships + "/worksheet"), ("Target", $"worksheets/sheet{index}.xml"));
            Element(xml, "Relationship", Relationships, ("Id", "styles"), ("Type", OfficeRelationships + "/styles"), ("Target", "styles.xml"));
            xml.WriteEndElement();
        });
        WriteXml(zip, "xl/workbook.xml", xml =>
        {
            xml.WriteStartElement("workbook", Spreadsheet);
            xml.WriteAttributeString("xmlns", "r", null, OfficeRelationships);
            xml.WriteStartElement("sheets", Spreadsheet);
            for (var index = 0; index < names.Count; index++)
            {
                xml.WriteStartElement("sheet", Spreadsheet);
                xml.WriteAttributeString("name", names[index]);
                xml.WriteAttributeString("sheetId", (index + 1).ToString(CultureInfo.InvariantCulture));
                xml.WriteAttributeString("r", "id", OfficeRelationships, $"rId{index + 1}");
                xml.WriteEndElement();
            }
            xml.WriteEndElement();
            if (recalculateOnOpen) Element(xml, "calcPr", Spreadsheet, ("calcId", "191029"), ("fullCalcOnLoad", "1"));
            xml.WriteEndElement();
        });
    }

    private static void WriteSheet(ZipArchive zip, int index, ExportTable table, List<object?[]> overflow, CancellationToken cancellationToken) =>
        WriteXml(zip, $"xl/worksheets/sheet{index}.xml", xml =>
        {
            xml.WriteStartElement("worksheet", Spreadsheet);
            xml.WriteStartElement("sheetViews", Spreadsheet);
            xml.WriteStartElement("sheetView", Spreadsheet);
            xml.WriteAttributeString("workbookViewId", "0");
            Element(xml, "pane", Spreadsheet, ("ySplit", "1"), ("topLeftCell", "A2"), ("activePane", "bottomLeft"), ("state", "frozen"));
            xml.WriteEndElement();
            xml.WriteEndElement();
            xml.WriteStartElement("cols", Spreadsheet);
            for (var column = 1; column <= table.Columns.Count; column++)
                Element(xml, "col", Spreadsheet, ("min", column.ToString()), ("max", column.ToString()), ("width", "26"), ("customWidth", "1"));
            xml.WriteEndElement();
            xml.WriteStartElement("sheetData", Spreadsheet);
            WriteRow(xml, table.Columns.Cast<object?>().ToArray(), 1, table, overflow, header: true);
            var rowNumber = 1;
            foreach (var row in table.Rows())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (++rowNumber > 1_048_576) throw new ReportExportValidationException("Export.RangeTooLarge");
                WriteRow(xml, row, rowNumber, table, overflow, header: false);
            }
            xml.WriteEndElement();
            Element(xml, "autoFilter", Spreadsheet, ("ref", $"A1:{ColumnName(table.Columns.Count)}{rowNumber}"));
            xml.WriteEndElement();
        });

    private static void WriteRow(XmlWriter xml, object?[] values, int rowNumber, ExportTable table, List<object?[]> overflow, bool header)
    {
        xml.WriteStartElement("row", Spreadsheet);
        xml.WriteAttributeString("r", rowNumber.ToString(CultureInfo.InvariantCulture));
        for (var index = 0; index < values.Length; index++)
        {
            var value = values[index];
            if (value is null) continue;
            xml.WriteStartElement("c", Spreadsheet);
            xml.WriteAttributeString("r", ColumnName(index + 1) + rowNumber.ToString(CultureInfo.InvariantCulture));
            xml.WriteAttributeString("s", header ? "1" : "0");
            if (value is string text)
            {
                if (text.Length > 32767)
                {
                    var offset = 0;
                    var part = 0;
                    while (offset < text.Length)
                    {
                        var length = Math.Min(30000, text.Length - offset);
                        if (char.IsHighSurrogate(text[offset + length - 1])) length--;
                        overflow.Add([table.Name, rowNumber, table.Columns[index], ++part, text.Substring(offset, length)]);
                        offset += length;
                    }
                    text = ReportExportService.Excerpt(text, 240) + " [Text parts]";
                }
                xml.WriteAttributeString("t", "inlineStr");
                xml.WriteStartElement("is", Spreadsheet);
                xml.WriteStartElement("t", Spreadsheet);
                xml.WriteAttributeString("xml", "space", "http://www.w3.org/XML/1998/namespace", "preserve");
                xml.WriteString(ExcelText(text));
                xml.WriteEndElement();
                xml.WriteEndElement();
            }
            else
            {
                if (value is bool) xml.WriteAttributeString("t", "b");
                xml.WriteElementString("v", Spreadsheet, value is bool flag ? (flag ? "1" : "0") : Convert.ToString(value, CultureInfo.InvariantCulture));
            }
            xml.WriteEndElement();
        }
        xml.WriteEndElement();
    }

    internal static string ExcelText(string text)
    {
        // SpreadsheetML escapes XML control characters and literal escape sequences without losing source text.
        var result = new StringBuilder(text.Length);
        for (var index = 0; index < text.Length; index++)
        {
            var character = text[index];
            if (character == '_' && index + 6 < text.Length && text[index + 1] == 'x' && text[index + 6] == '_'
                && text.AsSpan(index + 2, 4).ToString().All(Uri.IsHexDigit)) result.Append("_x005F_");
            else if (character < ' ' && character is not ('\t' or '\r' or '\n')) result.Append($"_x{(int)character:X4}_");
            else result.Append(character);
        }
        return result.ToString();
    }

    internal static string ColumnName(int number)
    {
        var result = "";
        while (number > 0) { number--; result = (char)('A' + number % 26) + result; number /= 26; }
        return result;
    }

    private static void WriteStyles(ZipArchive zip) => WriteXml(zip, "xl/styles.xml", xml =>
    {
        xml.WriteStartElement("styleSheet", Spreadsheet);
        xml.WriteStartElement("fonts", Spreadsheet); xml.WriteAttributeString("count", "2");
        for (var index = 0; index < 2; index++)
        {
            xml.WriteStartElement("font", Spreadsheet);
            Element(xml, "sz", Spreadsheet, ("val", "11")); Element(xml, "name", Spreadsheet, ("val", "Calibri"));
            if (index == 1) { Element(xml, "b", Spreadsheet); Element(xml, "color", Spreadsheet, ("rgb", "FFF4511E")); }
            xml.WriteEndElement();
        }
        xml.WriteEndElement();
        xml.WriteStartElement("fills", Spreadsheet); xml.WriteAttributeString("count", "2");
        foreach (var pattern in new[] { "none", "gray125" })
        { xml.WriteStartElement("fill", Spreadsheet); Element(xml, "patternFill", Spreadsheet, ("patternType", pattern)); xml.WriteEndElement(); }
        xml.WriteEndElement();
        xml.WriteStartElement("borders", Spreadsheet); xml.WriteAttributeString("count", "1"); Element(xml, "border", Spreadsheet); xml.WriteEndElement();
        xml.WriteStartElement("cellStyleXfs", Spreadsheet); xml.WriteAttributeString("count", "1");
        Element(xml, "xf", Spreadsheet, ("numFmtId", "0"), ("fontId", "0"), ("fillId", "0"), ("borderId", "0")); xml.WriteEndElement();
        xml.WriteStartElement("cellXfs", Spreadsheet); xml.WriteAttributeString("count", "2");
        for (var index = 0; index < 2; index++)
            Element(xml, "xf", Spreadsheet, ("numFmtId", "0"), ("fontId", index.ToString()), ("fillId", "0"), ("borderId", "0"), ("xfId", "0"), ("applyFont", "1"));
        xml.WriteEndElement();
        xml.WriteStartElement("cellStyles", Spreadsheet); xml.WriteAttributeString("count", "1");
        Element(xml, "cellStyle", Spreadsheet, ("name", "Normal"), ("xfId", "0"), ("builtinId", "0")); xml.WriteEndElement();
        xml.WriteEndElement();
    });

    internal static void WriteXml(ZipArchive zip, string name, Action<XmlWriter> write)
    {
        using var stream = zip.CreateEntry(name).Open();
        using var xml = XmlWriter.Create(stream, new XmlWriterSettings { Encoding = new UTF8Encoding(false), CloseOutput = false });
        xml.WriteStartDocument(); write(xml); xml.WriteEndDocument();
    }

    internal static void Element(XmlWriter xml, string name, string ns, params (string Name, string Value)[] attributes)
    {
        xml.WriteStartElement(name, ns);
        foreach (var (key, value) in attributes) xml.WriteAttributeString(key, value);
        xml.WriteEndElement();
    }
}
