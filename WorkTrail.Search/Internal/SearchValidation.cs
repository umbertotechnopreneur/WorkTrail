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
using System.Text;
using Lucene.Net.Index;

namespace WorkTrail.Search.Internal;

internal static class SearchValidation
{
    internal static int MaxIndexedTermUtf8Bytes => IndexWriter.MAX_TERM_LENGTH;

    internal static string ValidateOptions(SearchOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.IndexRootPath);

        if (!Path.IsPathFullyQualified(options.IndexRootPath))
        {
            throw new ArgumentException("The search index root path must be absolute.", nameof(options));
        }

        if (options.DefaultResultLimit <= 0 || options.DefaultResultLimit > options.MaxResultLimit)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "DefaultResultLimit must be positive and no greater than MaxResultLimit.");
        }

        if (options.MaxResultLimit <= 0 || options.MaxResultWindow < options.MaxResultLimit)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "MaxResultLimit must be positive and MaxResultWindow must be at least that large.");
        }

        if (options.FuzzyMinimumTermLength < 3)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "FuzzyMinimumTermLength must be at least three.");
        }

        if (options.FuzzyMaxEdits is < 1 or > 2)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "FuzzyMaxEdits must be one or two.");
        }

        if (options.FuzzyPrefixLength < 0 || options.FuzzyMaxExpansions <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "FuzzyPrefixLength cannot be negative and FuzzyMaxExpansions must be positive.");
        }

        if (options.MaxSynonymExpansions <= 0 || options.MaxTextFieldLength <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "Synonym and text-length limits must be positive.");
        }

        if (options.SynonymSets.IsDefault)
        {
            throw new ArgumentException("SynonymSets must be initialized.", nameof(options));
        }

        foreach (var set in options.SynonymSets)
        {
            if (set is null)
            {
                throw new ArgumentException("SynonymSets cannot contain null entries.", nameof(options));
            }

            ArgumentException.ThrowIfNullOrWhiteSpace(set.Language);
            if (set.Terms.IsDefault || set.Terms.Length < 2)
            {
                throw new ArgumentException("Each synonym set must contain at least two terms.", nameof(options));
            }

            ValidateLength(set.Language, nameof(set.Language), options);
            foreach (var term in set.Terms)
            {
                ValidateLength(term, nameof(set.Terms), options);
            }

            var normalizedTerms = set.Terms
                .Select(term => string.IsNullOrWhiteSpace(term) ? null : TextNormalization.ForAnalysis(term))
                .ToImmutableHashSet(StringComparer.Ordinal);
            if (normalizedTerms.Contains(null) || normalizedTerms.Count < 2)
            {
                throw new ArgumentException(
                    "Synonym terms must be non-empty and distinct after normalization.",
                    nameof(options));
            }
        }

        return Path.GetFullPath(options.IndexRootPath);
    }

    internal static void ValidateDocument(SearchDocument document, SearchOptions options)
    {
        ArgumentNullException.ThrowIfNull(document);
        ValidateMutationId(document.Id, options);
        ArgumentException.ThrowIfNullOrWhiteSpace(document.Kind);

        if (document.Timestamp == default)
        {
            throw new ArgumentException("Timestamp is required.", nameof(document));
        }

        if (document.AttributesRaw is null)
        {
            throw new ArgumentException("AttributesRaw must be initialized.", nameof(document));
        }

        if (document.SpanLabels.IsDefault)
        {
            throw new ArgumentException("SpanLabels must be initialized.", nameof(document));
        }

        foreach (var key in document.AttributesRaw.Keys)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(key);
        }

        ValidateLength(document.Kind, nameof(document.Kind), options);
        ValidateLength(document.Language, nameof(document.Language), options);
        ValidateLength(document.Application, nameof(document.Application), options);
        ValidateLength(document.ProcessName, nameof(document.ProcessName), options);
        ValidateLength(document.Context, nameof(document.Context), options);
        ValidateLength(document.WindowTitle, nameof(document.WindowTitle), options);
        ValidateLength(document.CaptureKind, nameof(document.CaptureKind), options);
        ValidateLength(document.CaptureOrigin, nameof(document.CaptureOrigin), options);
        ValidateLength(document.CapturePath, nameof(document.CapturePath), options);
        ValidateLength(document.OcrRawText, nameof(document.OcrRawText), options);
        ValidateLength(document.OcrCorrectedText, nameof(document.OcrCorrectedText), options);
        ValidateLength(document.OcrStructuredSummary, nameof(document.OcrStructuredSummary), options);
        ValidateLength(document.AiDescription, nameof(document.AiDescription), options);

        foreach (var pair in document.AttributesRaw)
        {
            ValidateLength(pair.Key, nameof(document.AttributesRaw), options);
            ValidateLength(pair.Value, nameof(document.AttributesRaw), options);
            ValidateExactTerm(pair.Key, nameof(document.AttributesRaw));
            ValidateExactTerm(pair.Value, nameof(document.AttributesRaw));
        }

        foreach (var label in document.SpanLabels)
        {
            if (label is null)
            {
                throw new ArgumentException("SpanLabels cannot contain null values.", nameof(document));
            }

            ValidateLength(label, nameof(document.SpanLabels), options);
            ValidateExactTerm(label, nameof(document.SpanLabels));
        }

        foreach (var field in SearchFields.Text.Where(field => field.ExactName is not null))
        {
            var value = field.ReadValue(document);
            if (field.Name == "language" && !string.IsNullOrWhiteSpace(value))
            {
                value = TextNormalization.NormalizeLanguage(value);
            }

            ValidateExactTerm(value, field.Name);
        }

        ValidateCombinedLength(
            document.AttributesRaw.SelectMany(static pair => new[] { pair.Key, pair.Value }),
            nameof(document.AttributesRaw),
            options);
        ValidateCombinedLength(document.SpanLabels, nameof(document.SpanLabels), options);
    }

    internal static void ValidateMutationId(string id, SearchOptions options)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ValidateLength(id, nameof(id), options);
        ValidateExactTerm(id, nameof(id), normalize: false);
    }

    internal static int ValidateRequest(SearchRequest request, SearchOptions options)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Text is null)
        {
            throw new ArgumentException("Text must be initialized.", nameof(request));
        }

        if (request.Kinds is null || request.Languages is null)
        {
            throw new ArgumentException("Filter collections must be initialized.", nameof(request));
        }

        if (string.IsNullOrWhiteSpace(request.Text) &&
            request.Kinds.Count == 0 &&
            request.Languages.Count == 0 &&
            request.FromInclusive is null &&
            request.ToExclusive is null)
        {
            throw new ArgumentException("A search request must contain text or at least one filter.", nameof(request));
        }

        if (request.Offset < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(request), "Offset cannot be negative.");
        }

        var limit = request.Limit ?? options.DefaultResultLimit;
        if (limit <= 0 || limit > options.MaxResultLimit)
        {
            throw new ArgumentOutOfRangeException(nameof(request), "Limit is outside the configured result range.");
        }

        if ((long)request.Offset + limit > options.MaxResultWindow)
        {
            throw new ArgumentOutOfRangeException(nameof(request), "Offset plus limit exceeds MaxResultWindow.");
        }

        if (request.FromInclusive is not null && request.ToExclusive is not null &&
            request.FromInclusive >= request.ToExclusive)
        {
            throw new ArgumentException("FromInclusive must be earlier than ToExclusive.", nameof(request));
        }

        ValidateFilterValues(request.Kinds, "Kinds", request, options);
        ValidateFilterValues(request.Languages, "Languages", request, options, normalizeLanguage: true);
        ValidateLength(request.Text, nameof(request.Text), options);
        ValidateLength(request.QueryLanguage, nameof(request.QueryLanguage), options);

        return limit;
    }

    internal static bool FitsIndexedTerm(string value) =>
        Encoding.UTF8.GetByteCount(value) <= MaxIndexedTermUtf8Bytes;

    private static void ValidateFilterValues(
        IEnumerable<string> values,
        string name,
        SearchRequest request,
        SearchOptions options,
        bool normalizeLanguage = false)
    {
        foreach (var value in values)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException($"{name} cannot contain empty values.", nameof(request));
            }

            ValidateLength(value, name, options);
            ValidateExactTerm(
                normalizeLanguage ? TextNormalization.NormalizeLanguage(value) : value,
                name);
        }
    }

    private static void ValidateCombinedLength(
        IEnumerable<string?> values,
        string name,
        SearchOptions options)
    {
        long length = 0;
        var hasValue = false;
        foreach (var value in values)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            length += value.Length + (hasValue ? 1 : 0);
            if (length > options.MaxTextFieldLength)
            {
                throw new ArgumentOutOfRangeException(
                    name,
                    $"Combined text exceeds {options.MaxTextFieldLength} UTF-16 characters.");
            }

            hasValue = true;
        }
    }

    private static void ValidateExactTerm(string? value, string name, bool normalize = true)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        var indexedValue = normalize ? TextNormalization.ForAnalysis(value) : value;
        if (indexedValue.Length > 0 && !FitsIndexedTerm(indexedValue))
        {
            throw new ArgumentOutOfRangeException(
                name,
                $"Exact search term exceeds Lucene's {MaxIndexedTermUtf8Bytes}-byte UTF-8 limit.");
        }
    }

    private static void ValidateLength(string? value, string name, SearchOptions options)
    {
        if (value is not null && value.Length > options.MaxTextFieldLength)
        {
            throw new ArgumentOutOfRangeException(name, $"Text exceeds {options.MaxTextFieldLength} UTF-16 characters.");
        }
    }
}
