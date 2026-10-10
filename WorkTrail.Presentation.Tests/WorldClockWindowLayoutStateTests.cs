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
using Xunit;

namespace WorkTrail.Presentation.Tests;

public sealed class WorldClockWindowLayoutStateTests
{
    [Fact]
    public void Surface_DefaultsToClocksAndSwitchesWithoutLosingTheOwnerState()
    {
        var state = new WorldClockWindowLayoutState();

        Assert.Equal(WorldClockWindowSurface.Clocks, state.Surface);
        state.ShowSurface(WorldClockWindowSurface.Options);
        Assert.Equal(WorldClockWindowSurface.Options, state.Surface);
        state.ShowSurface(WorldClockWindowSurface.Clocks);
        Assert.Equal(WorldClockWindowSurface.Clocks, state.Surface);
    }

    [Fact]
    public void ShowSurface_RejectsUndefinedValues()
    {
        var state = new WorldClockWindowLayoutState();

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            state.ShowSurface((WorldClockWindowSurface)int.MaxValue));
    }

    [Theory]
    [InlineData("2026-08-30T12:34:00.0000000+00:00", 60.1d)]
    [InlineData("2026-08-30T12:34:15.2500000+00:00", 44.85d)]
    [InlineData("2026-08-30T12:34:59.9900000+00:00", 0.11d)]
    public void DelayUntilNextMinute_LandsJustAfterMinuteBoundary(string instantText, double expectedSeconds)
    {
        var instant = DateTimeOffset.Parse(instantText);
        var delay = WorldClockWindowLayoutState.DelayUntilNextMinute(instant, instant);

        Assert.Equal(expectedSeconds, delay.TotalSeconds, precision: 6);
    }

    /// <summary>Re-scheduling an unchanged snapshot retains its original minute deadline.</summary>
    [Fact]
    public void DelayUntilNextMinute_PreservesDeadlineAfterLayoutOrOtherElapsedWork()
    {
        var snapshot = DateTimeOffset.Parse("2026-09-16T12:00:01+00:00");
        var originalDelay = WorldClockWindowLayoutState.DelayUntilNextMinute(snapshot, snapshot);
        var later = snapshot.AddSeconds(49);
        var remainingDelay = WorldClockWindowLayoutState.DelayUntilNextMinute(snapshot, later);

        Assert.Equal(snapshot + originalDelay, later + remainingDelay);
        Assert.Equal(TimeSpan.FromSeconds(10.1), remainingDelay);
    }

    /// <summary>An overdue snapshot refreshes immediately instead of waiting another full minute.</summary>
    [Fact]
    public void DelayUntilNextMinute_RefreshesOverdueSnapshotImmediately()
    {
        var snapshot = DateTimeOffset.Parse("2026-09-16T12:00:01+00:00");

        Assert.Equal(TimeSpan.FromMilliseconds(1),
            WorldClockWindowLayoutState.DelayUntilNextMinute(snapshot, snapshot.AddMinutes(3)));
    }

    [Theory]
    [InlineData(1, 1200d, 320d, 1200d)]
    [InlineData(2, 500d, 640d, 640d)]
    [InlineData(3, 700d, 960d, 960d)]
    [InlineData(4, 1400d, 1280d, 1400d)]
    [InlineData(12, 1400d, 3840d, 3840d)]
    public void CalculateColumnsLayout_PreservesReadableEqualColumns(
        int clockCount,
        double viewportWidth,
        double expectedMinimumWidth,
        double expectedWidth)
    {
        var layout = WorldClockWindowLayoutState.CalculateColumnsLayout(clockCount, viewportWidth);

        Assert.Equal(expectedMinimumWidth, layout.MinimumWidth);
        Assert.Equal(expectedWidth, layout.Width);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(13)]
    public void CalculateColumnsLayout_RejectsUnsupportedClockCounts(int clockCount) =>
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            WorldClockWindowLayoutState.CalculateColumnsLayout(clockCount, 800d));

    [Theory]
    [InlineData(1, 480, 680, 480, 240)]
    [InlineData(2, 780, 680, 480, 240)]
    [InlineData(3, 1120, 680, 480, 240)]
    public void CalculateWindowSizing_UsesTheCityCount(
        int clockCount,
        int preferredWidth,
        int preferredHeight,
        int minimumWidth,
        int minimumHeight)
    {
        var sizing = WorldClockWindowLayoutState.CalculateWindowSizing(clockCount);

        Assert.Equal(preferredWidth, sizing.PreferredLogicalWidth);
        Assert.Equal(preferredHeight, sizing.PreferredLogicalHeight);
        Assert.Equal(minimumWidth, sizing.MinimumLogicalWidth);
        Assert.Equal(minimumHeight, sizing.MinimumLogicalHeight);
    }

    [Theory]
    [InlineData(true, true, true, true)]
    [InlineData(false, false, false, false)]
    public void ResolveConversionFailure_RestoresOnlyATransitionThatStartedLive(
        bool transitionStartedFromLive,
        bool expectedLive,
        bool expectedValid,
        bool expectedRestoreControls)
    {
        var state = WorldClockWindowLayoutState.ResolveConversionFailure(transitionStartedFromLive);

        Assert.Equal(expectedLive, state.IsLive);
        Assert.Equal(expectedValid, state.CustomProjectionValid);
        Assert.Equal(expectedRestoreControls, state.RestoreLastSnapshotControls);
    }
}
