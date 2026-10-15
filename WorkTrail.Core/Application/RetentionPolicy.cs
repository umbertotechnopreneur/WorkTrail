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


namespace WorkTrail.Application;

/// <summary>Defines calendar-month retention limits and daily cleanup dates.</summary>
public static class RetentionPolicy
{
    /// <summary>Gets the maximum retained calendar months for the verified product tier.</summary>
    /// <param name="tier">Verified product access state.</param>
    public static int MaximumMonths(ProductTier tier) => tier == ProductTier.Premium ? 12 : 1;

    /// <summary>Maps persisted day selections to one through twelve calendar months.</summary>
    /// <param name="days">Stored duration selection, capped to the supported monthly choices.</param>
    public static int NormalizeDays(int days) => Math.Clamp((int)Math.Ceiling(days / 30d), 1, 12) * 30;

    /// <summary>Gets the effective calendar-month duration, including after a Premium downgrade.</summary>
    /// <param name="days">Stored duration selection.</param>
    /// <param name="tier">Verified product access state.</param>
    public static int EffectiveMonths(int days, ProductTier tier) => Math.Min(NormalizeDays(days) / 30, MaximumMonths(tier));

    /// <summary>Checks retention on the first ready day and daily after successful cleanup.</summary>
    /// <param name="firstActivation">Durable installation date used before the first cleanup.</param>
    /// <param name="lastCleanup">Local date of the last successfully completed cleanup, if any.</param>
    public static DateOnly NextCleanupDate(DateOnly firstActivation, DateOnly? lastCleanup)
    {
        return lastCleanup is { } last ? last.AddDays(1) : firstActivation;
    }
}
