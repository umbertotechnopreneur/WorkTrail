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
using System.Text;

namespace WorkTrail.Search.Internal;

/// <summary>Matches original query spans and creates a bounded set of equivalent queries.</summary>
internal sealed class SynonymCatalog
{
    private readonly IReadOnlyDictionary<string, TrieNode> _byLanguage;
    private readonly int _maximumExpansions;

    /// <summary>Builds one ordinal prefix tree per language from caller-provided groups.</summary>
    internal SynonymCatalog(SearchOptions options)
    {
        _maximumExpansions = options.MaxSynonymExpansions;
        var languages = new Dictionary<string, TrieNode>(StringComparer.Ordinal);
        foreach (var set in options.SynonymSets)
        {
            var language = TextNormalization.NormalizeLanguage(set.Language);
            if (!languages.TryGetValue(language, out var root))
            {
                root = new TrieNode();
                languages.Add(language, root);
            }

            var terms = set.Terms.Select(TextNormalization.ForAnalysis).Distinct(StringComparer.Ordinal).ToArray();
            foreach (var term in terms)
            {
                var node = root;
                foreach (var character in term)
                {
                    if (!node.Children.TryGetValue(character, out var child))
                    {
                        child = new TrieNode();
                        node.Children.Add(character, child);
                    }

                    node = child;
                }

                foreach (var alternative in terms)
                {
                    if (alternative != term)
                    {
                        node.Alternatives.Add(alternative);
                    }
                }
            }
        }

        _byLanguage = languages;
    }

    /// <summary>Expands longest non-overlapping spans without re-expanding replacement text.</summary>
    internal ImmutableArray<string> Expand(string query, string? language)
    {
        if (string.IsNullOrWhiteSpace(language) || ResolveLanguage(language) is not { } root)
        {
            // Unconfigured languages intentionally receive no synonym expansion.
            return [];
        }

        var normalized = TextNormalization.ForAnalysis(query);
        var spans = FindSpans(normalized, root, TextNormalization.AnalyzerLanguage(language) == "zh");
        var variants = ImmutableArray.CreateBuilder<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal) { normalized };
        var pending = new Queue<ImmutableArray<Replacement>>();
        pending.Enqueue([]);

        // Breadth-first traversal favors fewer changes. Both generated states and output
        // stop at the same budget, so several matching spans cannot grow exponentially.
        while (pending.TryDequeue(out var replacements))
        {
            var first = replacements.IsEmpty ? 0 : replacements[^1].SpanIndex + 1;
            for (var index = first; index < spans.Count; index++)
            {
                foreach (var alternative in spans[index].Alternatives)
                {
                    var next = replacements.Add(new Replacement(index, alternative));
                    var variant = BuildVariant(normalized, spans, next);
                    if (!seen.Add(variant))
                    {
                        continue;
                    }

                    variants.Add(variant);
                    if (variants.Count == _maximumExpansions)
                    {
                        return variants.ToImmutable();
                    }

                    pending.Enqueue(next);
                }
            }
        }

        return variants.ToImmutable();
    }

    /// <summary>Resolves exact tags, caller-defined primary tags, and deliberate product aliases.</summary>
    private TrieNode? ResolveLanguage(string language)
    {
        var normalized = TextNormalization.NormalizeLanguage(language);
        if (_byLanguage.TryGetValue(normalized, out var exact))
        {
            return exact;
        }

        var primary = TextNormalization.AnalyzerLanguage(normalized);
        if (_byLanguage.TryGetValue(primary, out var generic))
        {
            return generic;
        }

        var alias = primary switch
        {
            "en" => "en-us",
            "it" => "it-it",
            "fr" => "fr-fr",
            "de" => "de-de",
            "es" => "es-es",
            "vi" => "vi-vn",
            "ko" => "ko-kr",
            "pt-br" => "pt-br",
            "pt" => "pt-pt",
            "zh" when normalized is "zh" or "zh-cn" or "zh-sg" or "zh-my"
                || normalized.StartsWith("zh-hans-", StringComparison.Ordinal) => "zh-hans",
            _ => null
        };

        // Traditional Chinese and other unconfigured scripts never borrow the simplified catalog.
        return alias is not null && _byLanguage.TryGetValue(alias, out var localized) ? localized : null;
    }

    /// <summary>Finds longest phrases with word boundaries and dictionary boundaries between Han characters.</summary>
    private List<MatchedSpan> FindSpans(string query, TrieNode root, bool allowHanBoundaries)
    {
        var matches = new List<MatchedSpan>();
        var protectedTokens = FindProtectedTokens(query);
        var protectedIndex = 0;
        for (var start = 0; start < query.Length && matches.Count < _maximumExpansions; start++)
        {
            while (protectedIndex < protectedTokens.Count && protectedTokens[protectedIndex].End <= start)
            {
                protectedIndex++;
            }

            var protectedStart = protectedIndex < protectedTokens.Count ? protectedTokens[protectedIndex].Start : query.Length;
            if (start >= protectedStart)
            {
                start = protectedTokens[protectedIndex].End - 1;
                continue;
            }

            if (!IsBoundary(query, start, allowHanBoundaries))
            {
                continue;
            }

            var node = root;
            MatchedSpan? longest = null;
            for (var end = start; end < protectedStart && node.Children.TryGetValue(query[end], out node); end++)
            {
                if (node.Alternatives.Count > 0 && IsBoundary(query, end + 1, allowHanBoundaries))
                {
                    longest = new MatchedSpan(start, end + 1, [.. node.Alternatives]);
                }
            }

            if (longest is not null)
            {
                matches.Add(longest);
                start = longest.End - 1;
            }
        }

        return matches;
    }

    /// <summary>Locates literal tokens before matching so a phrase cannot cross into an address or filename.</summary>
    private static List<(int Start, int End)> FindProtectedTokens(string query)
    {
        var result = new List<(int Start, int End)>();
        for (var start = 0; start < query.Length; start++)
        {
            if (char.IsWhiteSpace(query[start]))
            {
                continue;
            }

            var end = start;
            while (end < query.Length && !char.IsWhiteSpace(query[end]))
            {
                end++;
            }

            if (IsProtectedToken(query.AsSpan(start, end - start)))
            {
                result.Add((start, end));
            }

            start = end - 1;
        }

        return result;
    }

    /// <summary>Preserves paths, addresses, URLs, and dotted identifiers as literal query input.</summary>
    private static bool IsProtectedToken(ReadOnlySpan<char> token)
    {
        if (token.ContainsAny('\\', '/', '@') || token.Contains(':'))
        {
            return true;
        }

        for (var index = 1; index + 1 < token.Length; index++)
        {
            if (token[index] == '.' && char.IsLetterOrDigit(token[index - 1]) && char.IsLetterOrDigit(token[index + 1]))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Recognizes Unicode word edges without splitting surrogate pairs or Korean syllables.</summary>
    private static bool IsBoundary(string text, int position, bool allowHanBoundaries)
    {
        if (position == 0 || position == text.Length)
        {
            return true;
        }

        if (char.IsLowSurrogate(text[position]))
        {
            return false;
        }

        var leftIndex = position - 1;
        if (char.IsLowSurrogate(text[leftIndex]) && leftIndex > 0)
        {
            leftIndex--;
        }

        var left = Rune.GetRuneAt(text, leftIndex);
        var right = Rune.GetRuneAt(text, position);
        return !IsWordRune(left) || !IsWordRune(right)
            || (allowHanBoundaries && (IsHan(left) || IsHan(right)));
    }

    /// <summary>Treats letters, digits, combining marks, and connectors as one word.</summary>
    private static bool IsWordRune(Rune rune) => Rune.IsLetterOrDigit(rune)
        || Rune.GetUnicodeCategory(rune) is UnicodeCategory.NonSpacingMark
            or UnicodeCategory.SpacingCombiningMark or UnicodeCategory.ConnectorPunctuation;

    /// <summary>Identifies Han ideographs, including supplementary-plane extensions.</summary>
    private static bool IsHan(Rune rune) => rune.Value is >= 0x3400 and <= 0x4DBF
        or >= 0x4E00 and <= 0x9FFF or >= 0xF900 and <= 0xFAFF
        or >= 0x20000 and <= 0x2FA1F or >= 0x30000 and <= 0x323AF;

    /// <summary>Applies selected original spans while preserving every unmatched part of the query.</summary>
    private static string BuildVariant(string query, IReadOnlyList<MatchedSpan> spans, ImmutableArray<Replacement> replacements)
    {
        var builder = new StringBuilder(query.Length);
        var cursor = 0;
        foreach (var replacement in replacements)
        {
            var span = spans[replacement.SpanIndex];
            builder.Append(query, cursor, span.Start - cursor);
            // A Latin alias replacing a Han span needs separation from adjacent query text.
            if (builder.Length > 0 && EndsWithWord(builder) && IsWordRune(Rune.GetRuneAt(replacement.Text, 0)))
            {
                builder.Append(' ');
            }

            builder.Append(replacement.Text);
            if (span.End < query.Length && IsWordRune(Rune.GetRuneAt(query, span.End)) && EndsWithWord(builder))
            {
                builder.Append(' ');
            }

            cursor = span.End;
        }

        builder.Append(query, cursor, query.Length - cursor);
        return builder.ToString();
    }

    /// <summary>Checks the last complete Unicode scalar when separating an inserted alias.</summary>
    private static bool EndsWithWord(StringBuilder text)
    {
        var last = text[^1];
        var rune = char.IsLowSurrogate(last) && text.Length > 1
            ? new Rune(text[^2], last)
            : new Rune(last);
        return IsWordRune(rune);
    }

    private sealed class TrieNode
    {
        internal Dictionary<char, TrieNode> Children { get; } = [];
        internal SortedSet<string> Alternatives { get; } = new(StringComparer.Ordinal);
    }

    private sealed record MatchedSpan(int Start, int End, ImmutableArray<string> Alternatives);
    private sealed record Replacement(int SpanIndex, string Text);
}
