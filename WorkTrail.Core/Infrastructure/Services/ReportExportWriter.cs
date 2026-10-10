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
        var workbook = new ReportExcelWorkbook(new LocalizationService(document.Options.Language), document.Options, document.RowLimit);
        ReportExcelData.Add(workbook, document);
        return workbook.Write(stream, cancellationToken);
    }

    // zip owns completed worksheets.
    // names defines their stable package order.
    // recalculateOnOpen asks Excel to recompute trusted arithmetic formulas in editable reports.
    // zip owns the atomic workbook package.
    // names lists the unique sheets in workbook order.
    // recalculateOnOpen refreshes trusted formulas when Excel opens the report.
    // printAreas bounds each printed sheet to its generated rows.
    // includeBrandMark declares the embedded cover artwork.
    // includeTheme declares the native Office theme part.
    // repeatedColumns preserves identifiers only on horizontally paged tables.
    internal static void WriteWorkbookPackage(ZipArchive zip, IReadOnlyList<string> names, bool recalculateOnOpen = false,
        IReadOnlyList<string>? printAreas = null, bool includeBrandMark = false, bool includeTheme = false,
        IReadOnlyList<string>? repeatedColumns = null)
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
            if (includeBrandMark)
            {
                Element(xml, "Default", types, ("Extension", "png"), ("ContentType", "image/png"));
                Element(xml, "Override", types, ("PartName", "/xl/drawings/brandmark.xml"), ("ContentType", "application/vnd.openxmlformats-officedocument.drawing+xml"));
            }
            Element(xml, "Override", types, ("PartName", "/xl/workbook.xml"), ("ContentType", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"));
            Element(xml, "Override", types, ("PartName", "/xl/styles.xml"), ("ContentType", "application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml"));
            if (includeTheme)
                Element(xml, "Override", types, ("PartName", "/xl/theme/theme1.xml"), ("ContentType", "application/vnd.openxmlformats-officedocument.theme+xml"));
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
            if (includeTheme)
                Element(xml, "Relationship", Relationships, ("Id", "theme"), ("Type", OfficeRelationships + "/theme"), ("Target", "theme/theme1.xml"));
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
            if (printAreas is not null)
            {
                xml.WriteStartElement("definedNames", Spreadsheet);
                for (var index = 0; index < names.Count; index++)
                {
                    xml.WriteStartElement("definedName", Spreadsheet);
                    xml.WriteAttributeString("name", "_xlnm.Print_Area"); xml.WriteAttributeString("localSheetId", index.ToString(CultureInfo.InvariantCulture));
                    xml.WriteString(ReportExcelWorkbook.Location(names[index], printAreas[index])); xml.WriteEndElement();
                    if (index == 0) continue;
                    xml.WriteStartElement("definedName", Spreadsheet);
                    xml.WriteAttributeString("name", "_xlnm.Print_Titles"); xml.WriteAttributeString("localSheetId", index.ToString(CultureInfo.InvariantCulture));
                    var titles = ReportExcelWorkbook.Location(names[index], "$1:$7");
                    // Excel counts repeated columns twice when fitting a sheet to one page across.
                    if (repeatedColumns?[index] is { Length: > 0 } columns) titles += "," + ReportExcelWorkbook.Location(names[index], columns);
                    xml.WriteString(titles); xml.WriteEndElement();
                }
                xml.WriteEndElement();
            }
            if (recalculateOnOpen) Element(xml, "calcPr", Spreadsheet, ("calcId", "191029"), ("fullCalcOnLoad", "1"));
            xml.WriteEndElement();
        });
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
