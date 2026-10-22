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

namespace WorkTrail.Services;

/// <summary>Defines the canonical language contracts shared by every WorkTrail frontend.</summary>
public static class ProductLanguageCatalog
{

    #region Fields
    /// <summary>Identifies the Windows-derived language choice.</summary>
    public const string SystemLanguage = "system";

    /// <summary>Identifies the deterministic fallback locale.</summary>
    public const string EnglishLanguage = "en-US";

    private static readonly string[] UiLocaleValues =
    [
        EnglishLanguage,
        "it-IT",
        "fr-FR",
        "de-DE",
        "es-ES",
        "zh-Hans",
        "vi-VN",
        "ko-KR",
        "pt-PT",
        "pt-BR"
    ];

    private static readonly string[] UiChoiceValues = [SystemLanguage, .. UiLocaleValues];

    #endregion

    #region Methods and properties

    /// <summary>Gets every locale for which the product ships a complete UI catalog.</summary>
    public static IReadOnlyList<string> UiLocales { get; } = Array.AsReadOnly(UiLocaleValues);

    /// <summary>Gets persisted choices accepted by the UI language setting.</summary>
    public static IReadOnlyList<string> UiChoices { get; } = Array.AsReadOnly(UiChoiceValues);

    /// <summary>Resolves a canonical UI choice, consulting Windows only for <c>system</c>.</summary>
    public static string ResolveUiLanguage(string? requestedLanguage, CultureInfo systemCulture)
    {
        ArgumentNullException.ThrowIfNull(systemCulture);

        var requested = string.IsNullOrWhiteSpace(requestedLanguage)
            ? SystemLanguage
            : requestedLanguage.Trim();
        if (!requested.Equals(SystemLanguage, StringComparison.OrdinalIgnoreCase))
        {
            return Canonical(UiLocaleValues, requested)
                ?? throw new ArgumentException(
                    $"Unsupported WorkTrail UI locale '{requested}'.",
                    nameof(requestedLanguage));
        }

        return ResolveSystemUiLanguage(systemCulture);
    }

    /// <summary>Returns the canonical UI setting choice, or <see langword="null"/> when unsupported.</summary>
    public static string? CanonicalUiChoice(string? value) => Canonical(UiChoiceValues, value);

    #endregion

    #region Implementation

    /// <summary>Maps Windows language variants to a supported product locale.</summary>
    private static string ResolveSystemUiLanguage(CultureInfo systemCulture)
    {
        var name = systemCulture.Name;
        var canonical = Canonical(UiLocaleValues, name);
        if (canonical is not null)
        {
            return canonical;
        }

        var lowerName = name.ToLowerInvariant();
        if (lowerName is "zh-cn" or "zh-sg" or "zh-my" || lowerName.StartsWith("zh-hans-", StringComparison.Ordinal))
        {
            return "zh-Hans";
        }

        if (lowerName.StartsWith("pt-br", StringComparison.Ordinal))
        {
            return "pt-BR";
        }

        if (lowerName.StartsWith("pt-", StringComparison.Ordinal))
        {
            return "pt-PT";
        }

        return systemCulture.TwoLetterISOLanguageName.ToLowerInvariant() switch
        {
            "en" => EnglishLanguage,
            "it" => "it-IT",
            "fr" => "fr-FR",
            "de" => "de-DE",
            "es" => "es-ES",
            "vi" => "vi-VN",
            "ko" => "ko-KR",
            _ => EnglishLanguage
        };
    }

    /// <summary>Returns the supported spelling of a language choice after trimming.</summary>
    private static string? Canonical(IEnumerable<string> choices, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        return choices.FirstOrDefault(choice => choice.Equals(trimmed, StringComparison.OrdinalIgnoreCase));
    }

    #endregion

}
