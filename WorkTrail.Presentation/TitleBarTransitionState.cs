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


namespace WorkTrail.Presentation;

/// <summary>Debounces hover-driven title-bar transitions using a caller-supplied monotonic clock.</summary>
public sealed class TitleBarTransitionState
{
    private TimeSpan _lastUpdatedAt;
    private TimeSpan? _transitionStartedAt;

    /// <summary>The stable hover interval required before either showing or hiding the title bar.</summary>
    public static readonly TimeSpan HoverDelay = TimeSpan.FromMilliseconds(300);

    /// <summary>The duration used by the view to fade a committed visibility transition.</summary>
    public static readonly TimeSpan FadeDuration = TimeSpan.FromMilliseconds(150);

    /// <summary>Gets the committed title-bar visibility, initially visible.</summary>
    public bool IsVisible { get; private set; } = true;

    /// <summary>Gets whether a different visibility is waiting for the stable hover interval.</summary>
    public bool HasPendingTransition => _transitionStartedAt.HasValue;

    /// <summary>Gets the delay remaining at the latest update, or zero when no transition is pending.</summary>
    public TimeSpan RemainingDelay => _transitionStartedAt is { } startedAt
        ? HoverDelay - (_lastUpdatedAt - startedAt)
        : TimeSpan.Zero;

    /// <summary>Commits stable hover changes while allowing explicit interactions to bypass the delay.</summary>
    /// <param name="desiredVisible">The visibility requested by pointer and interaction state.</param>
    /// <param name="now">Nonnegative elapsed time from the same monotonic clock for every update.</param>
    /// <param name="immediate">Whether to apply the requested visibility immediately and cancel pending hover work.</param>
    /// <returns>The committed visibility to render.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The elapsed time is negative or precedes an earlier update.</exception>
    public bool Update(bool desiredVisible, TimeSpan now, bool immediate = false)
    {
        if (now < TimeSpan.Zero || now < _lastUpdatedAt)
        {
            // Reject invalid clocks rather than allowing pending transitions to stall or fire unpredictably.
            throw new ArgumentOutOfRangeException(nameof(now), "Title-bar transition time must be nonnegative and monotonic.");
        }

        _lastUpdatedAt = now;
        if (immediate || desiredVisible == IsVisible)
        {
            IsVisible = desiredVisible;
            _transitionStartedAt = null;
            return IsVisible;
        }

        // Repeated pointer polls keep the original deadline; returning to the current state cancels it above.
        _transitionStartedAt ??= now;
        if (now - _transitionStartedAt.Value >= HoverDelay)
        {
            IsVisible = desiredVisible;
            _transitionStartedAt = null;
        }

        return IsVisible;
    }
}
