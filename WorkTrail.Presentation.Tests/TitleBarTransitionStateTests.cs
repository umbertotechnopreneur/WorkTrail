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
using WorkTrail.Presentation;
using Xunit;

namespace WorkTrail.Presentation.Tests;

/// <summary>Verifies delayed title-bar transitions without relying on wall-clock waits.</summary>
public sealed class TitleBarTransitionStateTests
{
    /// <summary>Both reveal and hide wait exactly 300 milliseconds of stable intent.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void StableHoverCommitsAtTheDelayBoundary(bool desiredVisible)
    {
        var state = new TitleBarTransitionState();
        state.Update(!desiredVisible, TimeSpan.Zero, immediate: true);

        Assert.Equal(!desiredVisible, state.Update(desiredVisible, TimeSpan.Zero));
        Assert.True(state.HasPendingTransition);
        Assert.Equal(TimeSpan.FromMilliseconds(300), state.RemainingDelay);
        Assert.Equal(!desiredVisible, state.Update(desiredVisible, TimeSpan.FromMilliseconds(299)));
        Assert.Equal(TimeSpan.FromMilliseconds(1), state.RemainingDelay);
        Assert.Equal(desiredVisible, state.Update(desiredVisible, TimeSpan.FromMilliseconds(300)));
        Assert.False(state.HasPendingTransition);
        Assert.Equal(TimeSpan.Zero, state.RemainingDelay);
    }

    /// <summary>Frequent pointer polling never postpones the original transition deadline.</summary>
    [Fact]
    public void RepeatedPollingPreservesTheOriginalDeadline()
    {
        var state = new TitleBarTransitionState();
        for (var milliseconds = 10; milliseconds < 310; milliseconds += 10)
        {
            Assert.True(state.Update(false, TimeSpan.FromMilliseconds(milliseconds)));
        }

        Assert.False(state.Update(false, TimeSpan.FromMilliseconds(310)));
        Assert.False(state.HasPendingTransition);
    }

    /// <summary>A brief pointer crossing cancels the pending transition without flickering the title bar.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReturningToCurrentVisibilityCancelsPendingWork(bool desiredVisible)
    {
        var state = new TitleBarTransitionState();
        state.Update(!desiredVisible, TimeSpan.Zero, immediate: true);
        state.Update(desiredVisible, TimeSpan.FromMilliseconds(10));

        Assert.Equal(!desiredVisible, state.Update(!desiredVisible, TimeSpan.FromMilliseconds(200)));
        Assert.False(state.HasPendingTransition);
        Assert.Equal(TimeSpan.Zero, state.RemainingDelay);
        Assert.Equal(!desiredVisible, state.Update(!desiredVisible, TimeSpan.FromMilliseconds(500)));

        state.Update(desiredVisible, TimeSpan.FromMilliseconds(600));
        Assert.Equal(!desiredVisible, state.Update(desiredVisible, TimeSpan.FromMilliseconds(899)));
        Assert.Equal(desiredVisible, state.Update(desiredVisible, TimeSpan.FromMilliseconds(900)));
    }

    /// <summary>Explicit keyboard, touch, or settings changes bypass and clear a pending hover delay.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ImmediateUpdateCommitsWithoutWaiting(bool desiredVisible)
    {
        var state = new TitleBarTransitionState();
        state.Update(!desiredVisible, TimeSpan.Zero, immediate: true);
        state.Update(desiredVisible, TimeSpan.FromMilliseconds(10));

        Assert.Equal(desiredVisible, state.Update(desiredVisible, TimeSpan.FromMilliseconds(11), immediate: true));
        Assert.False(state.HasPendingTransition);
        Assert.Equal(TimeSpan.Zero, state.RemainingDelay);
    }

    /// <summary>Disabling auto-hide cancels a delayed hide and keeps the title bar visible.</summary>
    [Fact]
    public void ImmediateVisibleCancelsPendingHide()
    {
        var state = new TitleBarTransitionState();
        state.Update(false, TimeSpan.Zero);

        Assert.True(state.Update(true, TimeSpan.FromMilliseconds(100), immediate: true));
        Assert.False(state.HasPendingTransition);
        Assert.True(state.Update(true, TimeSpan.FromMilliseconds(400), immediate: true));
    }

    /// <summary>A delayed callback commits overdue work instead of restarting its deadline.</summary>
    [Fact]
    public void LateTimerCommitsThePendingTransition()
    {
        var state = new TitleBarTransitionState();
        state.Update(false, TimeSpan.FromMilliseconds(100));

        Assert.False(state.Update(false, TimeSpan.FromSeconds(2)));
        Assert.Equal(TimeSpan.Zero, state.RemainingDelay);
    }

    /// <summary>Invalid clock input fails without damaging an already scheduled transition.</summary>
    [Theory]
    [InlineData(-1)]
    [InlineData(99)]
    public void InvalidClockFailsWithoutChangingPendingState(int invalidMilliseconds)
    {
        var state = new TitleBarTransitionState();
        state.Update(false, TimeSpan.FromMilliseconds(100));

        Assert.Throws<ArgumentOutOfRangeException>(() => state.Update(true, TimeSpan.FromMilliseconds(invalidMilliseconds), immediate: true));
        Assert.True(state.IsVisible);
        Assert.True(state.HasPendingTransition);
        Assert.Equal(TimeSpan.FromMilliseconds(300), state.RemainingDelay);
        Assert.False(state.Update(false, TimeSpan.FromMilliseconds(400)));
    }
}
