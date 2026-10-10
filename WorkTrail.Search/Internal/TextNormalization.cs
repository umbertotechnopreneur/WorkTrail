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

internal static class TextNormalization
{
    internal const string FallbackLanguage = "und";

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

    internal static string? NormalizeOptional(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : ForAnalysis(value);
    }

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

    internal static string NormalizeLanguage(string language)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(language);
        return language.Trim().Replace('_', '-').ToLowerInvariant();
    }

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

    internal static bool IsPathLike(string value)
    {
        return value.Contains('\\', StringComparison.Ordinal) ||
               value.Contains('/', StringComparison.Ordinal) ||
               value.Contains(':', StringComparison.Ordinal) ||
               value.Contains('@', StringComparison.Ordinal) ||
               value.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                   .Any(token => token.Contains('.', StringComparison.Ordinal));
    }

    internal static bool IsFuzzyEligible(string token, int minimumLength)
    {
        return token.Length >= minimumLength && token.All(char.IsLetter);
    }
}
