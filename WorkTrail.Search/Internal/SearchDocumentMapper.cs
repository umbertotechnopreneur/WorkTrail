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


using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;
using Lucene.Net.Documents;

namespace WorkTrail.Search.Internal;

internal static class SearchDocumentMapper
{
    internal static Document ToLucene(SearchDocument source)
    {
        var target = new Document
        {
            new StringField(SearchFields.IdKey, source.Id, Field.Store.NO),
            new StoredField(SearchFields.IdStored, source.Id),
            new StoredField(SearchFields.KindStored, source.Kind),
            new Int64Field(
                SearchFields.Timestamp,
                source.Timestamp.UtcDateTime.Ticks,
                Field.Store.NO),
            new StoredField(
                SearchFields.TimestampStored,
                source.Timestamp.ToString("O", CultureInfo.InvariantCulture)),
        };

        AddStoredOptional(target, SearchFields.LanguageStored, source.Language);

        if (source.AttributesRaw.Count > 0)
        {
            target.Add(new StoredField(
                SearchFields.AttributesStored,
                JsonSerializer.Serialize(source.AttributesRaw)));
        }

        if (!source.SpanLabels.IsDefaultOrEmpty)
        {
            target.Add(new StoredField(
                SearchFields.SpanLabelsStored,
                JsonSerializer.Serialize(source.SpanLabels)));
        }

        foreach (var field in SearchFields.Text)
        {
            var value = field.ReadValue(source);
            if (field.Name is not ("id" or "kind" or "language" or "attributes_raw" or "span_labels"))
            {
                AddStoredOptional(target, field.StoredName, value);
            }

            AddSearchableText(target, field, value, source.Language);
        }

        foreach (var pair in source.AttributesRaw)
        {
            AddExact(target, SearchFields.AttributesExact, pair.Key);
            AddExact(target, SearchFields.AttributesExact, pair.Value);
        }

        foreach (var label in source.SpanLabels)
        {
            AddExact(target, SearchFields.SpanLabelExact, label);
        }

        return target;
    }

    internal static SearchDocument FromLucene(Document source)
    {
        var id = source.Get(SearchFields.IdStored);
        var kind = source.Get(SearchFields.KindStored);
        var timestampValue = source.Get(SearchFields.TimestampStored);
        if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(kind) || timestampValue is null)
        {
            throw new InvalidDataException("The search index contains a document without required stored fields.");
        }

        if (!DateTimeOffset.TryParseExact(
                timestampValue,
                "O",
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out var timestamp))
        {
            throw new InvalidDataException("The search index contains an invalid stored timestamp.");
        }

        return new SearchDocument
        {
            Id = id,
            Kind = kind,
            Timestamp = timestamp,
            Language = source.Get(SearchFields.LanguageStored),
            Application = source.Get("application"),
            ProcessName = source.Get("process_name"),
            Context = source.Get("context"),
            WindowTitle = source.Get("window_title"),
            AttributesRaw = ReadAttributes(source.Get(SearchFields.AttributesStored)),
            SpanLabels = ReadSpanLabels(source.Get(SearchFields.SpanLabelsStored)),
            CaptureKind = source.Get("capture_kind"),
            CaptureOrigin = source.Get("capture_origin"),
            CapturePath = source.Get("capture_path"),
            OcrRawText = source.Get("ocr_raw_text"),
            OcrCorrectedText = source.Get("ocr_corrected_text"),
            OcrStructuredSummary = source.Get("ocr_structured_summary"),
            AiDescription = source.Get("ai_description"),
        };
    }

    private static void AddSearchableText(
        Document target,
        SearchTextField field,
        string? value,
        string? language)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        var normalized = TextNormalization.ForTokenization(value);
        if (normalized.Length == 0)
        {
            return;
        }

        target.Add(new TextField(SearchFields.GenericText(field.Name), normalized, Field.Store.NO));
        target.Add(new TextField(
            SearchFields.LanguageText(field.Name, TextNormalization.AnalyzerLanguage(language)),
            normalized,
            Field.Store.NO));

        if (field.ExactName is not null)
        {
            AddExact(
                target,
                field.ExactName,
                field.Name == "language" ? TextNormalization.NormalizeLanguage(value) : value);
        }
    }

    private static void AddStoredOptional(Document target, string name, string? value)
    {
        if (value is not null)
        {
            target.Add(new StoredField(name, value));
        }
    }

    private static void AddExact(Document target, string name, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        var normalized = TextNormalization.ForAnalysis(value);
        if (normalized.Length == 0)
        {
            return;
        }

        target.Add(new StringField(name, normalized, Field.Store.NO));
    }

    private static ImmutableDictionary<string, string?> ReadAttributes(string? json)
    {
        if (json is null)
        {
            return [];
        }

        try
        {
            var values = JsonSerializer.Deserialize<Dictionary<string, string?>>(json)
                ?? throw new JsonException("The stored attribute object is null.");
            return values.ToImmutableDictionary(StringComparer.Ordinal);
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("The search index contains invalid raw attributes.", exception);
        }
    }

    private static ImmutableArray<string> ReadSpanLabels(string? json)
    {
        if (json is null)
        {
            return [];
        }

        try
        {
            var values = JsonSerializer.Deserialize<string[]>(json)
                ?? throw new JsonException("The stored span-label array is null.");
            return [.. values];
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("The search index contains invalid span labels.", exception);
        }
    }
}
