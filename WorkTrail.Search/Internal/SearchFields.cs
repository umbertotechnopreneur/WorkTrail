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

namespace WorkTrail.Search.Internal;

internal sealed record SearchTextField(
    string Name,
    string StoredName,
    string? ExactName,
    float Boost,
    Func<SearchDocument, string?> ReadValue);

internal static class SearchFields
{
    internal const string IdKey = "_id_key";
    internal const string IdStored = "id";
    internal const string KindStored = "kind";
    internal const string Timestamp = "timestamp_ticks";
    internal const string TimestampStored = "timestamp";
    internal const string LanguageStored = "language";
    internal const string AttributesStored = "attributes_raw_json";
    internal const string SpanLabelsStored = "span_labels_json";

    internal const string IdExact = "id_exact";
    internal const string KindExact = "kind_exact";
    internal const string LanguageExact = "language_exact";
    internal const string AttributesExact = "attributes_exact";
    internal const string SpanLabelExact = "span_label_exact";

    internal static readonly ImmutableArray<string> SupportedLanguages =
        ["it", "en", "fr", "de", "es", "vi", "zh", "ko", "pt", "pt-br"];

    internal static readonly ImmutableArray<SearchTextField> Text =
    [
        new("id", IdStored, IdExact, 3.5f, document => document.Id),
        new("kind", KindStored, KindExact, 1.0f, document => document.Kind),
        new("language", LanguageStored, LanguageExact, 0.4f, document => document.Language),
        new("application", "application", "application_exact", 3.5f, document => document.Application),
        new("process_name", "process_name", "process_name_exact", 2.5f, document => document.ProcessName),
        new("context", "context", "context_exact", 3.0f, document => document.Context),
        new("window_title", "window_title", "window_title_exact", 3.0f, document => document.WindowTitle),
        new("attributes_raw", AttributesStored, null, 1.4f, FlattenAttributes),
        new("span_labels", SpanLabelsStored, null, 2.0f, FlattenSpanLabels),
        new("capture_kind", "capture_kind", "capture_kind_exact", 1.2f, document => document.CaptureKind),
        new("capture_origin", "capture_origin", "capture_origin_exact", 1.2f, document => document.CaptureOrigin),
        new("capture_path", "capture_path", "capture_path_exact", 0.6f, document => document.CapturePath),
        new("ocr_raw_text", "ocr_raw_text", null, 1.0f, document => document.OcrRawText),
        new("ocr_corrected_text", "ocr_corrected_text", null, 1.5f, document => document.OcrCorrectedText),
        new("ocr_structured_summary", "ocr_structured_summary", null, 1.8f, document => document.OcrStructuredSummary),
        new("ai_description", "ai_description", null, 1.6f, document => document.AiDescription),
    ];

    internal static string GenericText(string name) => $"{name}_text";

    internal static string LanguageText(string name, string language) => $"{name}_text_{language}";

    private static string? FlattenAttributes(SearchDocument document)
    {
        if (document.AttributesRaw.Count == 0)
        {
            return null;
        }

        return string.Join(
            ' ',
            document.AttributesRaw
                .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .SelectMany(pair => new[] { pair.Key, pair.Value })
                .Where(value => !string.IsNullOrWhiteSpace(value)));
    }

    private static string? FlattenSpanLabels(SearchDocument document)
    {
        return document.SpanLabels.IsDefaultOrEmpty
            ? null
            : string.Join(' ', document.SpanLabels.Where(label => !string.IsNullOrWhiteSpace(label)));
    }
}
