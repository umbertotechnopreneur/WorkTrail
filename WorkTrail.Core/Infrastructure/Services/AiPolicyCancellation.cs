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


namespace WorkTrail.Services;

/// <summary>Separates revoked collection permission from ordinary caller/shutdown cancellation.</summary>
internal static class AiPolicyCancellation
{
    private static readonly AsyncLocal<CancellationToken> Current = new();

    internal static async Task<T> RunAsync<T>(Func<Task<T>> operation, CancellationToken policyToken)
    {
        var previous = Current.Value;
        Current.Value = policyToken;
        try { return await operation().ConfigureAwait(false); }
        finally { Current.Value = previous; }
    }

    internal static void ThrowIfRevoked() => Current.Value.ThrowIfCancellationRequested();
}
