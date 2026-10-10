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

namespace WorkTrail.Search;

/// <summary>Names optional structured attributes shared by local-search producers and consumers.</summary>
public static class SearchAttributeKeys
{
    /// <summary>Stores the number of mouse clicks observed in the screenshot capture interval.</summary>
    public const string MouseClicks = "activity.mouse-clicks";

    /// <summary>Stores average CPU usage observed since the previous screenshot.</summary>
    public const string CpuUsagePercent = "telemetry.cpu-usage-percent";

    /// <summary>Stores average GPU usage observed since the previous screenshot.</summary>
    public const string GpuUsagePercent = "telemetry.gpu-usage-percent";

    /// <summary>Stores the durable installation identifier that produced the record.</summary>
    public const string InstallationId = "installation.id";

    /// <summary>Stores the user-selected installation name rendered as provenance.</summary>
    public const string InstallationFriendlyName = "installation.friendly-name";

    /// <summary>Stores the originating Windows machine name.</summary>
    public const string InstallationMachineName = "installation.machine-name";

    /// <summary>Stores the validated installation color identifier.</summary>
    public const string InstallationColor = "installation.color";

    /// <summary>Stores the validated installation icon identifier.</summary>
    public const string InstallationIcon = "installation.icon";
}

/// <summary>
/// Represents every searchable value associated with an activity or screenshot.
/// </summary>
public sealed record SearchDocument
{
    /// <summary>Gets the stable, case-sensitive document identifier.</summary>
    public required string Id { get; init; }

    /// <summary>Gets the caller-defined document kind, such as activity or screenshot.</summary>
    public required string Kind { get; init; }

    /// <summary>Gets the instant represented by the document.</summary>
    public required DateTimeOffset Timestamp { get; init; }

    /// <summary>Gets the optional BCP 47 language tag associated with the text.</summary>
    public string? Language { get; init; }

    /// <summary>Gets the foreground application display name.</summary>
    public string? Application { get; init; }

    /// <summary>Gets the foreground process name.</summary>
    public string? ProcessName { get; init; }

    /// <summary>Gets the activity context.</summary>
    public string? Context { get; init; }

    /// <summary>Gets the foreground window title.</summary>
    public string? WindowTitle { get; init; }

    /// <summary>Gets raw activity attributes without requiring an AI description.</summary>
    public ImmutableDictionary<string, string?> AttributesRaw { get; init; } = [];

    /// <summary>Gets the ordered span labels associated with the document.</summary>
    public ImmutableArray<string> SpanLabels { get; init; } = [];

    /// <summary>Gets the screenshot capture kind.</summary>
    public string? CaptureKind { get; init; }

    /// <summary>Gets the screenshot capture origin.</summary>
    public string? CaptureOrigin { get; init; }

    /// <summary>Gets the screenshot path.</summary>
    public string? CapturePath { get; init; }

    /// <summary>Gets the unmodified text returned by OCR.</summary>
    public string? OcrRawText { get; init; }

    /// <summary>Gets optional AI-corrected OCR text.</summary>
    public string? OcrCorrectedText { get; init; }

    /// <summary>Gets an optional structured summary derived from OCR text.</summary>
    public string? OcrStructuredSummary { get; init; }

    /// <summary>Gets an optional AI description of the screenshot or activity.</summary>
    public string? AiDescription { get; init; }
}

/// <summary>Describes one stable-document mutation in an atomic local index batch.</summary>
public sealed record SearchIndexMutation
{
    /// <summary>Gets the stable identifier to update or delete.</summary>
    public required string Id { get; init; }

    /// <summary>Gets the replacement document, or <see langword="null"/> to delete the identifier.</summary>
    public SearchDocument? Document { get; init; }

    /// <summary>Creates an upsert mutation for a complete validated document.</summary>
    public static SearchIndexMutation Upsert(SearchDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return new SearchIndexMutation { Id = document.Id, Document = document };
    }

    /// <summary>Creates a delete mutation for a stable identifier.</summary>
    public static SearchIndexMutation Delete(string id) => new() { Id = id };
}

/// <summary>
/// Describes one configurable group of equivalent search expressions.
/// </summary>
public sealed record SearchSynonymSet
{
    /// <summary>Normalizes a catalog term exactly as query-time synonym matching does.</summary>
    public static string NormalizeTerm(string term) => Internal.TextNormalization.ForAnalysis(term);

    /// <summary>Gets the language tag to which this synonym set applies.</summary>
    public required string Language { get; init; }

    /// <summary>Gets two or more equivalent terms or phrases.</summary>
    public ImmutableArray<string> Terms { get; init; } = [];
}

/// <summary>
/// Configures a local search index. Search itself is always enabled.
/// </summary>
public sealed record SearchOptions
{
    /// <summary>Gets the absolute directory under which the versioned index is stored.</summary>
    public required string IndexRootPath { get; init; }

    /// <summary>Gets query-time synonym groups. No built-in synonym dictionary is used.</summary>
    public ImmutableArray<SearchSynonymSet> SynonymSets { get; init; } = [];

    /// <summary>Gets the result count used when a request does not specify a limit.</summary>
    public int DefaultResultLimit { get; init; } = 25;

    /// <summary>Gets the maximum result count accepted for one request.</summary>
    public int MaxResultLimit { get; init; } = 200;

    /// <summary>Gets the maximum supported value of offset plus limit.</summary>
    public int MaxResultWindow { get; init; } = 2_000;

    /// <summary>Gets whether controlled fuzzy clauses are added to text queries.</summary>
    public bool EnableFuzzyMatching { get; init; } = true;

    /// <summary>Gets the minimum token length eligible for fuzzy matching.</summary>
    public int FuzzyMinimumTermLength { get; init; } = 5;

    /// <summary>Gets the maximum Damerau-Levenshtein edit distance, from one to two.</summary>
    public int FuzzyMaxEdits { get; init; } = 1;

    /// <summary>Gets the exact prefix length required by fuzzy clauses.</summary>
    public int FuzzyPrefixLength { get; init; } = 1;

    /// <summary>Gets the maximum term expansions generated by one fuzzy clause.</summary>
    public int FuzzyMaxExpansions { get; init; } = 50;

    /// <summary>Gets the maximum number of synonym query variants generated per request.</summary>
    public int MaxSynonymExpansions { get; init; } = 32;

    /// <summary>Gets the maximum UTF-16 length accepted for any individual text field.</summary>
    public int MaxTextFieldLength { get; init; } = 1_000_000;
}

/// <summary>
/// Defines a programmatic search request.
/// </summary>
public sealed record SearchRequest
{
    /// <summary>Gets the free text to search. It may be empty when at least one filter is present.</summary>
    public string Text { get; init; } = string.Empty;

    /// <summary>Gets the optional language used for stemming and synonym expansion.</summary>
    public string? QueryLanguage { get; init; }

    /// <summary>Gets whether configured query-time synonym expansion is enabled.</summary>
    public bool EnableSynonyms { get; init; } = true;

    /// <summary>Gets whether controlled typo-tolerant clauses are enabled.</summary>
    public bool EnableFuzzyMatching { get; init; } = true;

    /// <summary>Gets whether result documents include searchable text bodies and raw attributes.</summary>
    public bool IncludeTextContent { get; init; } = true;

    /// <summary>Gets the optional exact document-kind filters.</summary>
    public ImmutableHashSet<string> Kinds { get; init; } = [];

    /// <summary>Gets the optional exact document-language filters.</summary>
    public ImmutableHashSet<string> Languages { get; init; } = [];

    /// <summary>Gets the optional inclusive lower timestamp bound.</summary>
    public DateTimeOffset? FromInclusive { get; init; }

    /// <summary>Gets the optional exclusive upper timestamp bound.</summary>
    public DateTimeOffset? ToExclusive { get; init; }

    /// <summary>Gets the number of matching documents to skip.</summary>
    public int Offset { get; init; }

    /// <summary>Gets the requested result count, or <see langword="null"/> for the configured default.</summary>
    public int? Limit { get; init; }
}

/// <summary>
/// Represents one ranked local-search match.
/// </summary>
public sealed record SearchHit
{
    /// <summary>Gets the complete stored search document.</summary>
    public required SearchDocument Document { get; init; }

    /// <summary>Gets the Lucene relevance score.</summary>
    public required float Score { get; init; }
}

/// <summary>
/// Represents a page of ranked search results.
/// </summary>
public sealed record SearchResponse
{
    /// <summary>Gets the result page.</summary>
    public ImmutableArray<SearchHit> Hits { get; init; } = [];

    /// <summary>Gets the total number of matches before pagination.</summary>
    public required int TotalCount { get; init; }

    /// <summary>Gets the offset applied to this page.</summary>
    public required int Offset { get; init; }
}
