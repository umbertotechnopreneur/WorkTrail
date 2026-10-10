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


using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace WorkTrail.Services;

/// <summary>Overrides request-shaping details for a single provider call without changing persisted settings.</summary>
public sealed record AiProviderRequestOptions(
    bool OmitOutputTokenLimitWhenSupported = false,
    string? ReasoningEffort = null,
    int? MaxOutputTokens = null);

/// <summary>
/// Contract for provider adapters that can enrich productivity analysis from context and screenshots.
/// </summary>
public interface IAIDecoder
{
    /// <summary>
    /// Provider identifier ("openai", "openrouter", "anthropic").
    /// </summary>
    string Provider { get; }

    /// <summary>
    /// Runs a provider request and returns text plus allowlisted usage and transport metadata.
    /// </summary>
    /// <param name="prompt">Fully rendered prompt to send.</param>
    /// <param name="screenshotPaths">Local image paths selected for the request.</param>
    /// <param name="settings">Current normalized AI settings.</param>
    /// <param name="apiKey">Resolved secret, used only for the outgoing request.</param>
    /// <param name="correlationId">Business correlation identifier for this snapshot.</param>
    /// <param name="requestOptions">Optional per-request provider shaping that is never persisted.</param>
    /// <param name="cancellationToken">Cancels local file reads and the provider request.</param>
    /// <returns>Provider text and nullable usage metadata.</returns>
    Task<AiProviderResult> DecodeAsync(
        string prompt,
        IReadOnlyList<string> screenshotPaths,
        AppSettings settings,
        string apiKey,
        string correlationId,
        AiProviderRequestOptions? requestOptions = null,
        CancellationToken cancellationToken = default);
}
