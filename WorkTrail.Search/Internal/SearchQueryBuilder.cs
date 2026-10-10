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


using Lucene.Net.Index;
using Lucene.Net.Search;

namespace WorkTrail.Search.Internal;

internal sealed class SearchQueryBuilder
{
    private readonly SearchOptions _options;
    private readonly LanguageAnalyzerCatalog _analyzers;
    private readonly SynonymCatalog _synonyms;

    internal SearchQueryBuilder(
        SearchOptions options,
        LanguageAnalyzerCatalog analyzers,
        SynonymCatalog synonyms)
    {
        _options = options;
        _analyzers = analyzers;
        _synonyms = synonyms;
    }

    internal Query Build(SearchRequest request)
    {
        var root = new BooleanQuery();
        var normalizedText = TextNormalization.NormalizeOptional(request.Text);

        if (normalizedText is not null)
        {
            root.Add(BuildTextQuery(request, normalizedText), Occur.MUST);
        }
        else
        {
            root.Add(new MatchAllDocsQuery(), Occur.MUST);
        }

        AddExactSetFilter(root, SearchFields.KindExact, request.Kinds);
        AddExactSetFilter(root, SearchFields.LanguageExact, request.Languages, normalizeLanguage: true);

        if (request.FromInclusive is not null || request.ToExclusive is not null)
        {
            var range = NumericRangeQuery.NewInt64Range(
                SearchFields.Timestamp,
                request.FromInclusive?.UtcDateTime.Ticks,
                request.ToExclusive?.UtcDateTime.Ticks,
                minInclusive: true,
                maxInclusive: false);
            range.Boost = 0f;
            root.Add(range, Occur.MUST);
        }

        return root;
    }

    private Query BuildTextQuery(SearchRequest request, string normalizedText)
    {
        var textQuery = new BooleanQuery { MinimumNumberShouldMatch = 1 };

        var exact = BuildExactQuery(normalizedText);
        if (exact is not null)
        {
            textQuery.Add(exact, Occur.SHOULD);
        }

        var analyzed = BuildAnalyzedVariant(normalizedText, request.QueryLanguage, 1f);
        if (analyzed is not null)
        {
            textQuery.Add(analyzed, Occur.SHOULD);
        }

        var synonymLanguage = request.QueryLanguage;
        if (string.IsNullOrWhiteSpace(synonymLanguage) && request.Languages.Count == 1)
        {
            synonymLanguage = request.Languages.Single();
        }

        IEnumerable<string> synonymExpansions = request.EnableSynonyms
            ? _synonyms.Expand(normalizedText, synonymLanguage)
            : Array.Empty<string>();
        // Equivalent variants compete for one score rather than rewarding repeated aliases.
        var synonymQueries = new List<Query>();
        foreach (var expansion in synonymExpansions)
        {
            var synonymQuery = BuildAnalyzedVariant(expansion, synonymLanguage, 0.3f);
            if (synonymQuery is not null)
            {
                synonymQueries.Add(synonymQuery);
            }
        }

        if (synonymQueries.Count > 0)
        {
            textQuery.Add(new DisjunctionMaxQuery(synonymQueries, 0f), Occur.SHOULD);
        }

        if (_options.EnableFuzzyMatching
            && request.EnableFuzzyMatching
            && !TextNormalization.IsPathLike(request.Text))
        {
            var fuzzy = BuildFuzzyVariant(normalizedText, request.QueryLanguage);
            if (fuzzy is not null)
            {
                textQuery.Add(fuzzy, Occur.SHOULD);
            }
        }

        return textQuery;
    }

    private static Query? BuildExactQuery(string normalizedText)
    {
        if (!SearchValidation.FitsIndexedTerm(normalizedText))
        {
            return null;
        }

        var exact = new BooleanQuery { MinimumNumberShouldMatch = 1 };
        var added = false;
        foreach (var field in SearchFields.Text)
        {
            if (field.ExactName is null)
            {
                continue;
            }

            exact.Add(
                new TermQuery(new Term(field.ExactName, normalizedText))
                {
                    Boost = 16f * field.Boost,
                },
                Occur.SHOULD);
            added = true;
        }

        exact.Add(
            new TermQuery(new Term(SearchFields.AttributesExact, normalizedText)) { Boost = 10f },
            Occur.SHOULD);
        exact.Add(
            new TermQuery(new Term(SearchFields.SpanLabelExact, normalizedText)) { Boost = 14f },
            Occur.SHOULD);

        return added ? exact : null;
    }

    private Query? BuildAnalyzedVariant(string text, string? queryLanguage, float scale)
    {
        var variant = new BooleanQuery { MinimumNumberShouldMatch = 1 };
        var added = false;

        foreach (var target in GetTargets(queryLanguage))
        {
            var terms = _analyzers.Analyze(text, target.AnalyzerLanguage);
            if (terms.Count == 0)
            {
                continue;
            }

            foreach (var field in SearchFields.Text)
            {
                var fieldName = target.IsGeneric
                    ? SearchFields.GenericText(field.Name)
                    : SearchFields.LanguageText(field.Name, target.FieldLanguage);

                var conjunction = BuildTermConjunction(fieldName, terms);
                conjunction.Boost = 4f * field.Boost * target.Boost * scale;
                variant.Add(conjunction, Occur.SHOULD);

                if (terms.Count > 1)
                {
                    var phrase = new PhraseQuery
                    {
                        Boost = 8f * field.Boost * target.Boost * scale,
                    };
                    foreach (var term in terms)
                    {
                        phrase.Add(new Term(fieldName, term));
                    }

                    variant.Add(phrase, Occur.SHOULD);
                }

                added = true;
            }
        }

        return added ? variant : null;
    }

    private Query? BuildFuzzyVariant(string text, string? queryLanguage)
    {
        var variant = new BooleanQuery { MinimumNumberShouldMatch = 1 };
        var added = false;

        foreach (var target in GetTargets(queryLanguage))
        {
            var terms = _analyzers.Analyze(text, target.AnalyzerLanguage);
            if (!terms.Any(term => TextNormalization.IsFuzzyEligible(term, _options.FuzzyMinimumTermLength)))
            {
                continue;
            }

            foreach (var field in SearchFields.Text)
            {
                var fieldName = target.IsGeneric
                    ? SearchFields.GenericText(field.Name)
                    : SearchFields.LanguageText(field.Name, target.FieldLanguage);
                var conjunction = new BooleanQuery
                {
                    Boost = 0.55f * field.Boost * target.Boost,
                };

                foreach (var term in terms)
                {
                    Query clause;
                    if (TextNormalization.IsFuzzyEligible(term, _options.FuzzyMinimumTermLength))
                    {
                        var maximumEdits = Math.Min(_options.FuzzyMaxEdits, term.Length < 8 ? 1 : 2);
                        clause = new FuzzyQuery(
                            new Term(fieldName, term),
                            maximumEdits,
                            _options.FuzzyPrefixLength,
                            _options.FuzzyMaxExpansions,
                            transpositions: true);
                    }
                    else
                    {
                        clause = new TermQuery(new Term(fieldName, term));
                    }

                    conjunction.Add(clause, Occur.MUST);
                }

                variant.Add(conjunction, Occur.SHOULD);
                added = true;
            }
        }

        return added ? variant : null;
    }

    private static BooleanQuery BuildTermConjunction(string fieldName, IReadOnlyList<string> terms)
    {
        var query = new BooleanQuery();
        foreach (var term in terms)
        {
            query.Add(new TermQuery(new Term(fieldName, term)), Occur.MUST);
        }

        return query;
    }

    private static IReadOnlyList<QueryTarget> GetTargets(string? queryLanguage)
    {
        var targets = new List<QueryTarget>
        {
            new(TextNormalization.FallbackLanguage, TextNormalization.FallbackLanguage, true, 1f),
        };

        if (string.IsNullOrWhiteSpace(queryLanguage))
        {
            return targets;
        }

        var analyzerLanguage = TextNormalization.AnalyzerLanguage(queryLanguage);
        var fieldLanguage = SearchFields.SupportedLanguages.Contains(analyzerLanguage, StringComparer.Ordinal)
            ? analyzerLanguage
            : TextNormalization.FallbackLanguage;
        targets.Add(new QueryTarget(fieldLanguage, analyzerLanguage, false, 1.15f));
        return targets;
    }

    private static void AddExactSetFilter(
        BooleanQuery root,
        string field,
        IEnumerable<string> values,
        bool normalizeLanguage = false)
    {
        var normalizedValues = values
            .Select(value => normalizeLanguage
                ? TextNormalization.NormalizeLanguage(value)
                : TextNormalization.ForAnalysis(value))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (normalizedValues.Length == 0)
        {
            return;
        }

        var filter = new BooleanQuery { MinimumNumberShouldMatch = 1, Boost = 0f };
        foreach (var value in normalizedValues)
        {
            filter.Add(new TermQuery(new Term(field, value)), Occur.SHOULD);
        }

        root.Add(filter, Occur.MUST);
    }

    private sealed record QueryTarget(
        string FieldLanguage,
        string AnalyzerLanguage,
        bool IsGeneric,
        float Boost);
}
