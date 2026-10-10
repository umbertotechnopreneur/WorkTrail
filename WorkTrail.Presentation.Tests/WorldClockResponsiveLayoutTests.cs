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
using WorkTrail.Application;
using Xunit;

namespace WorkTrail.Presentation.Tests;

public sealed class WorldClockResponsiveLayoutTests
{
    /// <summary>Growing taller stops magnifying the scene at each display scale, while wide columns still fill their width.</summary>
    [Theory]
    [InlineData(640d, 1d, 720d)]
    [InlineData(640d, 1.5d, 480d)]
    [InlineData(640d, 2d, 360d)]
    [InlineData(1600d, 1d, 900d)]
    [InlineData(960d, 2d, 540d)]
    public void Scene_StopsGrowingAtItsResolutionLimitAndRecoversAfterShrinking(double width, double scale, double limit)
    {
        Assert.Equal(limit - 1d, WorldClockWindowLayoutState.CalculateSceneHeight(width, limit - 1d, scale));
        Assert.Equal(limit, WorldClockWindowLayoutState.CalculateSceneHeight(width, limit, scale));
        Assert.Equal(limit, WorldClockWindowLayoutState.CalculateSceneHeight(width, limit + 1d, scale));
        Assert.Equal(limit, WorldClockWindowLayoutState.CalculateSceneHeight(width, 3000d, scale));
        Assert.Equal(120d, WorldClockWindowLayoutState.CalculateSceneHeight(width, 120d, scale));
    }

    [Theory]
    [InlineData(480d, 240d)]
    [InlineData(640d, 320d)]
    [InlineData(1250d, 850d)]
    [InlineData(320d, 480d)]
    public void ReferenceEditor_FitsBothRootAxesIncludingItsBorderAndInsets(double width, double height)
    {
        var bounds = WorldClockWindowLayoutState.CalculateReferenceFlyoutBounds(width, height);

        Assert.InRange(bounds.ContentWidth, 1d, 478d);
        Assert.InRange(bounds.ContentMaxHeight, 1d, 718d);
        Assert.True(bounds.ContentWidth + 2d + 24d <= width);
        Assert.True(bounds.ContentMaxHeight + 2d + 24d <= height);
    }

    [Fact]
    public void ReferenceEditor_RecalculatesAfterShrinkingAndGrowingTheWindow()
    {
        var large = WorldClockWindowLayoutState.CalculateReferenceFlyoutBounds(1280d, 900d);
        var small = WorldClockWindowLayoutState.CalculateReferenceFlyoutBounds(480d, 240d);

        Assert.True(small.ContentWidth < large.ContentWidth);
        Assert.True(small.ContentMaxHeight < large.ContentMaxHeight);
        Assert.Equal(large, WorldClockWindowLayoutState.CalculateReferenceFlyoutBounds(1280d, 900d));
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(-1d)]
    [InlineData(0d)]
    public void ReferenceEditor_RejectsUnusableRootBounds(double value)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => WorldClockWindowLayoutState.CalculateReferenceFlyoutBounds(value, 480d));
        Assert.Throws<ArgumentOutOfRangeException>(() => WorldClockWindowLayoutState.CalculateReferenceFlyoutBounds(480d, value));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void PreferredSize_HasNoUnusedWidthAndSharesTheRestoreMinimum(int count)
    {
        var sizing = WorldClockWindowLayoutState.CalculateWindowSizing(count);
        var columns = WorldClockWindowLayoutState.CalculateColumnsLayout(count, sizing.PreferredLogicalWidth);
        var minimum = WindowStateService.GetMinimumSize(WindowStateKeys.WorldClocks);

        Assert.Equal(sizing.PreferredLogicalWidth, columns.Width);
        Assert.Equal(minimum.Width, sizing.MinimumLogicalWidth);
        Assert.Equal(minimum.Height, sizing.MinimumLogicalHeight);
    }

    [Fact]
    public void ScaledTimeText_ExpandsColumnsEquallyAndScrollsInsteadOfClipping()
    {
        var columns = WorldClockWindowLayoutState.CalculateColumnsLayout(2, 640d, minimumColumnWidth: 410d);
        Assert.Equal(820d, columns.Width);
        Assert.Equal(820d, columns.MinimumWidth);
    }

    [Theory]
    [InlineData(610d, 610d, 290d, WorldClockDetailLevel.Expanded)]
    [InlineData(609d, 610d, 290d, WorldClockDetailLevel.Summary)]
    [InlineData(290d, 610d, 290d, WorldClockDetailLevel.Summary)]
    [InlineData(289d, 610d, 290d, WorldClockDetailLevel.Essential)]
    [InlineData(610d, 850d, 410d, WorldClockDetailLevel.Summary)]
    [InlineData(1000d, 610d, 290d, WorldClockDetailLevel.Expanded)]
    [InlineData(240d, 610d, 290d, WorldClockDetailLevel.Essential)]
    public void Disclosure_UsesMeasuredContentAndAvailableHeight(
        double viewport, double expandedHeight, double summaryHeight,
        WorldClockDetailLevel expected)
    {
        Assert.Equal(expected, WorldClockWindowLayoutState.CalculateDetailLevel(viewport, expandedHeight, summaryHeight));
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(-1d)]
    public void Measurement_RejectsInvalidDimensions(double value)
    {
        Assert.ThrowsAny<ArgumentException>(() => WorldClockWindowLayoutState.CalculateColumnsLayout(1, value));
        Assert.ThrowsAny<ArgumentException>(() => WorldClockWindowLayoutState.CalculateDetailLevel(value, 600, 300));
    }

    [Fact]
    public void RestoredBounds_AreRetainedOnFirstSnapshotAndSubsequentRefreshes()
    {
        var state = new WorldClockWindowLayoutState();
        state.SetPlacementRestored(true);
        var initial = Assert.IsType<WorldClockWindowResizeRequest>(state.GetWindowResizeRequest(2));
        Assert.False(initial.ResizeToPreferred);
        state.AcceptWindowResizeRequest(initial);
        Assert.Null(state.GetWindowResizeRequest(2));

        Assert.True(Assert.IsType<WorldClockWindowResizeRequest>(state.GetWindowResizeRequest(3)).ResizeToPreferred);
    }

    [Theory]
    [InlineData(1, 2)]
    [InlineData(2, 1)]
    [InlineData(2, 3)]
    public void CityChangeInOptions_IsDeferredUntilReturningWithoutALiveRefresh(int before, int after)
    {
        var state = new WorldClockWindowLayoutState();
        state.AcceptWindowResizeRequest(Assert.IsType<WorldClockWindowResizeRequest>(state.GetWindowResizeRequest(before)));
        state.ShowSurface(WorldClockWindowSurface.Options);
        Assert.Null(state.GetWindowResizeRequest(after));

        state.ShowSurface(WorldClockWindowSurface.Clocks);
        var pending = Assert.IsType<WorldClockWindowResizeRequest>(state.GetWindowResizeRequest(after));
        Assert.True(pending.ResizeToPreferred);
        Assert.Equal(WorldClockWindowLayoutState.CalculateWindowSizing(after), pending.Sizing);
        // A failed resize is not acknowledged and remains retryable.
        Assert.Equal(pending, state.GetWindowResizeRequest(after));
        state.AcceptWindowResizeRequest(pending);
        Assert.Null(state.GetWindowResizeRequest(after));
    }

    [Fact]
    public void EmptyCitySet_ResetsTheSizingCycle()
    {
        var state = new WorldClockWindowLayoutState();
        state.AcceptWindowResizeRequest(Assert.IsType<WorldClockWindowResizeRequest>(state.GetWindowResizeRequest(1)));
        state.ResetWindowSizing();
        Assert.True(Assert.IsType<WorldClockWindowResizeRequest>(state.GetWindowResizeRequest(1)).ResizeToPreferred);
    }
}
