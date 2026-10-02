// SPDX-License-Identifier: MIT

namespace WorkTrail.Application;

/// <summary>Defines calendar-month retention limits and installation-anchored cleanup dates.</summary>
public static class RetentionPolicy
{
    /// <summary>Gets the maximum retained calendar months for the verified product tier.</summary>
    /// <param name="tier">Verified product access state.</param>
    public static int MaximumMonths(ProductTier tier) => tier == ProductTier.Premium ? 3 : 1;

    /// <summary>Maps persisted day selections to the supported one-, two-, or three-month choices.</summary>
    /// <param name="days">Stored duration selection, capped to the supported monthly choices.</param>
    public static int NormalizeDays(int days) => Math.Clamp((int)Math.Ceiling(days / 30d), 1, 3) * 30;

    /// <summary>Gets the effective calendar-month duration, including after a Premium downgrade.</summary>
    /// <param name="days">Stored duration selection.</param>
    /// <param name="tier">Verified product access state.</param>
    public static int EffectiveMonths(int days, ProductTier tier) => Math.Min(NormalizeDays(days) / 30, MaximumMonths(tier));

    /// <summary>Finds the next monthly anniversary after the last completed cleanup, without end-of-month drift.</summary>
    /// <param name="firstActivation">Durable installation date that anchors each monthly anniversary.</param>
    /// <param name="lastCleanup">Local date of the last successfully completed cleanup, if any.</param>
    public static DateOnly NextCleanupDate(DateOnly firstActivation, DateOnly? lastCleanup)
    {
        var baseline = lastCleanup is { } last && last > firstActivation ? last : firstActivation;
        var months = Math.Max(1, (baseline.Year - firstActivation.Year) * 12 + baseline.Month - firstActivation.Month);
        var next = firstActivation.AddMonths(months);
        return next <= baseline ? firstActivation.AddMonths(months + 1) : next;
    }
}
