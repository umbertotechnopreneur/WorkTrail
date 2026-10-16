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
using System.Text;

namespace WorkTrail.Search.Internal;

/// <summary>
/// Normalizes user text into the canonical forms used by the local-search index and queries.
/// </summary>
internal static class TextNormalization
{
    /// <summary>
    /// Identifies text whose language cannot be determined.
    /// </summary>
    internal const string FallbackLanguage = "und";

    /// <summary>
    /// Folds case, diacritics, and whitespace into a stable value for exact matching.
    /// </summary>
    internal static string ForAnalysis(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        var decomposed = value.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        var previousWasWhitespace = false;

        foreach (var character in decomposed)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(character);
            if (category is UnicodeCategory.NonSpacingMark or
                UnicodeCategory.SpacingCombiningMark or
                UnicodeCategory.EnclosingMark)
            {
                continue;
            }

            if (char.IsWhiteSpace(character))
            {
                if (builder.Length > 0 && !previousWasWhitespace)
                {
                    builder.Append(' ');
                }

                previousWasWhitespace = true;
                continue;
            }

            // Vietnamese d-with-stroke is a distinct base letter and is not removed by
            // Unicode decomposition, so fold it explicitly alongside other diacritics.
            builder.Append(character is 'đ' or 'Đ' ? 'd' : char.ToLowerInvariant(character));
            previousWasWhitespace = false;
        }

        if (builder.Length > 0 && builder[^1] == ' ')
        {
            builder.Length--;
        }

        return builder.ToString().Normalize(NormalizationForm.FormC);
    }

    /// <summary>
    /// Returns the normalized value when input contains text; otherwise returns <see langword="null"/>.
    /// </summary>
    internal static string? NormalizeOptional(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : ForAnalysis(value);
    }

    /// <summary>
    /// Replaces path-like separators with spaces before an analyzer tokenizes the value.
    /// </summary>
    internal static string ForTokenization(string value)
    {
        var normalized = ForAnalysis(value);
        if (normalized.Length == 0)
        {
            return normalized;
        }

        var builder = new StringBuilder(normalized.Length);
        var previousWasSeparator = false;
        foreach (var character in normalized)
        {
            var isSeparator = character is '.' or '-' or '_' or '\\' or '/' or ':';
            if (isSeparator)
            {
                if (builder.Length > 0 && !previousWasSeparator && builder[^1] != ' ')
                {
                    builder.Append(' ');
                }

                previousWasSeparator = true;
                continue;
            }

            builder.Append(character);
            previousWasSeparator = character == ' ';
        }

        if (builder.Length > 0 && builder[^1] == ' ')
        {
            builder.Length--;
        }

        return builder.ToString();
    }

    /// <summary>
    /// Converts a BCP 47 language tag to its trimmed, lowercase, hyphenated form.
    /// </summary>
    internal static string NormalizeLanguage(string language)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(language);
        return language.Trim().Replace('_', '-').ToLowerInvariant();
    }

    /// <summary>
    /// Resolves a language tag to the analyzer key supported by the local index.
    /// </summary>
    internal static string AnalyzerLanguage(string? language)
    {
        if (string.IsNullOrWhiteSpace(language))
        {
            return FallbackLanguage;
        }

        var normalized = NormalizeLanguage(language);
        if (normalized == "pt-br" || normalized.StartsWith("pt-br-", StringComparison.Ordinal))
        {
            return "pt-br";
        }

        if (normalized == "pt" || normalized.StartsWith("pt-", StringComparison.Ordinal))
        {
            return "pt";
        }

        if (normalized == "zh" || normalized.StartsWith("zh-", StringComparison.Ordinal))
        {
            return "zh";
        }

        var separator = normalized.IndexOf('-');
        return separator < 0 ? normalized : normalized[..separator];
    }

    /// <summary>
    /// Determines whether text resembles a path, address, URL, or dotted identifier that should remain literal.
    /// </summary>
    internal static bool IsPathLike(string value)
    {
        var containsPathSeparator = value.Contains('\\', StringComparison.Ordinal) ||
                                    value.Contains('/', StringComparison.Ordinal);
        var containsAddressSeparator = value.Contains(':', StringComparison.Ordinal) ||
                                       value.Contains('@', StringComparison.Ordinal);
        var containsDottedToken = value
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Any(token => token.Contains('.', StringComparison.Ordinal));

        return containsPathSeparator || containsAddressSeparator || containsDottedToken;
    }

    /// <summary>
    /// Determines whether a token is long enough and alphabetic enough for fuzzy matching.
    /// </summary>
    internal static bool IsFuzzyEligible(string token, int minimumLength)
    {
        return token.Length >= minimumLength && token.All(char.IsLetter);
    }
}
