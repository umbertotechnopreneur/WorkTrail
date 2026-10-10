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


namespace WorkTrail.Ocr;

/// <summary>
/// Configures local screenshot text extraction.
/// </summary>
public sealed record OcrOptions
{
    /// <summary>
    /// Gets whether local OCR is enabled. The default is <see langword="false"/>.
    /// </summary>
    public bool Enabled { get; init; }

    /// <summary>
    /// Gets the optional BCP-47 language tag requested from Windows OCR.
    /// When this value is <see langword="null"/>, Windows selects the first supported user-profile language.
    /// </summary>
    public string? PreferredLanguageTag { get; init; }
}
