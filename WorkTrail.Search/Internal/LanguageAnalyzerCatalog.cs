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


using Lucene.Net.Analysis;
using Lucene.Net.Analysis.Br;
using Lucene.Net.Analysis.Cjk;
using Lucene.Net.Analysis.De;
using Lucene.Net.Analysis.En;
using Lucene.Net.Analysis.Es;
using Lucene.Net.Analysis.Fr;
using Lucene.Net.Analysis.It;
using Lucene.Net.Analysis.Miscellaneous;
using Lucene.Net.Analysis.Pt;
using Lucene.Net.Analysis.Standard;
using Lucene.Net.Analysis.TokenAttributes;
using Lucene.Net.Util;

namespace WorkTrail.Search.Internal;

internal sealed class LanguageAnalyzerCatalog : IDisposable
{
    private const LuceneVersion Version = LuceneVersion.LUCENE_48;
    private readonly Analyzer _fallbackAnalyzer;
    private readonly IReadOnlyDictionary<string, Analyzer> _languageAnalyzers;

    internal LanguageAnalyzerCatalog()
    {
        _fallbackAnalyzer = new StandardAnalyzer(Version);
        _languageAnalyzers = new Dictionary<string, Analyzer>(StringComparer.Ordinal)
        {
            ["it"] = new ItalianAnalyzer(Version),
            ["en"] = new EnglishAnalyzer(Version),
            ["fr"] = new FrenchAnalyzer(Version),
            ["de"] = new GermanAnalyzer(Version),
            ["es"] = new SpanishAnalyzer(Version),
            ["vi"] = new StandardAnalyzer(Version),
            ["zh"] = new CJKAnalyzer(Version),
            ["ko"] = new CJKAnalyzer(Version),
            ["pt"] = new PortugueseAnalyzer(Version),
            ["pt-br"] = new BrazilianAnalyzer(Version),
        };

        var perField = new Dictionary<string, Analyzer>(StringComparer.Ordinal);
        foreach (var field in SearchFields.Text)
        {
            foreach (var pair in _languageAnalyzers)
            {
                perField[SearchFields.LanguageText(field.Name, pair.Key)] = pair.Value;
            }
        }

        IndexAnalyzer = new PerFieldAnalyzerWrapper(_fallbackAnalyzer, perField);
    }

    internal Analyzer IndexAnalyzer { get; }

    internal IReadOnlyList<string> Analyze(string value, string? language)
    {
        var normalized = TextNormalization.ForTokenization(value);
        if (normalized.Length == 0)
        {
            return [];
        }

        var analyzer = GetAnalyzer(language);
        using var reader = new StringReader(normalized);
        using var stream = analyzer.GetTokenStream("query", reader);
        var term = stream.AddAttribute<ICharTermAttribute>();
        var terms = new List<string>();

        stream.Reset();
        while (stream.IncrementToken())
        {
            terms.Add(term.ToString());
        }

        stream.End();
        return terms;
    }

    internal Analyzer GetAnalyzer(string? language)
    {
        var key = TextNormalization.AnalyzerLanguage(language);
        return _languageAnalyzers.TryGetValue(key, out var analyzer) ? analyzer : _fallbackAnalyzer;
    }

    public void Dispose()
    {
        IndexAnalyzer.Dispose();

        foreach (var analyzer in _languageAnalyzers.Values)
        {
            analyzer.Dispose();
        }

        _fallbackAnalyzer.Dispose();
    }
}
