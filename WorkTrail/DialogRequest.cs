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


namespace WorkTrail;

/// <summary>Describes the localized copy for one standard WinUI confirmation or acknowledgement dialog.</summary>
internal sealed record DialogRequest(
    string Title,
    string Message,
    string PrimaryButtonText,
    string? CloseButtonText)
{
    /// <summary>Creates a one-button acknowledgement request.</summary>
    internal static DialogRequest Informative(string title, string message, string primaryButtonText) =>
        new(title, message, primaryButtonText, CloseButtonText: null);

    /// <summary>Creates a safe-default confirmation request with an explicit cancel action.</summary>
    internal static DialogRequest Confirmation(
        string title,
        string message,
        string primaryButtonText,
        string closeButtonText) =>
        new(title, message, primaryButtonText, closeButtonText);
}
