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


using System;

namespace WorkTrail.Services;

/// <summary>
/// Creates a decoder implementation for the selected AI provider.
/// </summary>
public static class AIDecoderFactory
{
    /// <summary>
    /// Builds decoder for configured provider.
    /// </summary>
    /// <param name="settings">Current app settings.</param>
    /// <returns>Decoder instance.</returns>
    /// <exception cref="InvalidOperationException">Thrown when provider is unknown.</exception>
    public static IAIDecoder Create(AppSettings settings)
    {
        var provider = (settings.AiProvider ?? "openai").ToLowerInvariant();
        return provider switch
        {
            "openai" or "open-ai" => new OpenAiDecoder(),
            "openrouter" => new OpenRouterDecoder(),
            "anthropic" => new AnthropicDecoder(),
            _ => throw new InvalidOperationException($"Provider AI '{provider}' non supportato. Valori validi: openai, openrouter, anthropic."),
        };
    }
}
