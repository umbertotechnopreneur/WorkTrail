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

public sealed class MainWindowLayoutStateTests
{
    /// <summary>Checks that resizing on any page keeps the same viewport through navigation and content updates.</summary>
    [Theory]
    [InlineData(MainWindowSurface.Player)]
    [InlineData(MainWindowSurface.Options)]
    [InlineData(MainWindowSurface.Operations)]
    public void ManualViewport_SurvivesContentChangesAndSurfaceSwitches(MainWindowSurface resizedSurface)
    {
        var state = new MainWindowLayoutState();
        state.ShowSurface(resizedSurface);
        state.RecordManualSize(820, 630);

        foreach (var surface in Enum.GetValues<MainWindowSurface>())
        {
            state.ShowSurface(surface);
            var preferredWidth = surface == MainWindowSurface.Player ? 576 : 760;
            foreach (var contentHeight in new[] { 1600d, 240d, 0d })
            {
                state.RecordMeasuredHeight(contentHeight);
                Assert.Equal(820, state.ResolveLogicalWidth(1200, preferredWidth));
                Assert.Equal(630, state.ResolveLogicalHeight(900, 20));
            }
        }

        // A later resize on a secondary page becomes the viewport when navigating back as well.
        state.RecordManualSize(900, 710);
        state.ShowSurface(MainWindowSurface.Player);
        Assert.Equal(900, state.ResolveLogicalWidth(1200, 576));
        Assert.Equal(710, state.ResolveLogicalHeight(900, 20));
    }

    /// <summary>Checks that a smaller display constrains bounds without losing the preferred viewport.</summary>
    [Fact]
    public void ManualViewport_ClampsToTheDisplayAndRecoversWhenSpaceReturns()
    {
        var state = new MainWindowLayoutState();
        state.RecordManualSize(820, 630);
        state.ShowSurface(MainWindowSurface.Operations);
        Assert.Equal(500, state.ResolveLogicalWidth(500.8, 576));
        Assert.Equal(400, state.ResolveLogicalHeight(400.9, 20));
        state.ShowSurface(MainWindowSurface.Options);
        Assert.Equal(820, state.ResolveLogicalWidth(1200, 576));
        Assert.Equal(630, state.ResolveLogicalHeight(900, 20));
    }

    /// <summary>Checks that invalid window geometry fails before replacing the last valid size.</summary>
    [Fact]
    public void ManualViewport_RejectsInvalidGeometry()
    {
        var state = new MainWindowLayoutState();
        state.RecordManualSize(510, 390);
        Assert.Throws<ArgumentOutOfRangeException>(() => state.RecordManualSize(double.NaN, 390));
        Assert.Throws<ArgumentOutOfRangeException>(() => state.RecordManualSize(510, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => state.ResolveLogicalWidth(double.PositiveInfinity, 576));
        Assert.Equal(510, state.ResolveLogicalWidth(1200, 576));
        Assert.Equal(390, state.ResolveLogicalHeight(900, 20));
    }

    [Fact]
    public void State_TracksAllPlayerSectionsWithoutResettingThemWhenChangingSurface()
    {
        var state = new MainWindowLayoutState();

        Assert.Equal(MainWindowSurface.Player, state.Surface);
        Assert.True(state.IsActivityScoreVisible);
        Assert.False(state.IsLastSessionVisible);
        Assert.False(state.IsPendingSnapshotVisible);
        Assert.False(state.IsOutsideActiveHoursWarningVisible);

        Assert.True(state.ToggleSection(MainWindowLayoutSection.LastSession));
        Assert.True(state.SetSectionVisibility(MainWindowLayoutSection.PendingSnapshot, true));
        Assert.True(state.SetSectionVisibility(MainWindowLayoutSection.OutsideActiveHoursWarning, true));
        state.ShowSurface(MainWindowSurface.Options);
        state.ShowSurface(MainWindowSurface.Player);

        Assert.True(state.IsLastSessionVisible);
        Assert.True(state.IsPendingSnapshotVisible);
        Assert.True(state.IsOutsideActiveHoursWarningVisible);
    }

    [Fact]
    public void RecordMeasuredHeight_RoundsUpAndPreservesTheLastValidMeasurementForTransientLayoutPasses()
    {
        var state = new MainWindowLayoutState();

        Assert.Equal(489, state.RecordMeasuredHeight(488.1));
        Assert.Equal(489, state.RecordMeasuredHeight(0));
        Assert.Equal(489, state.RecordMeasuredHeight(double.NaN));
    }

    [Fact]
    public void ResolveLogicalHeight_CapsSecondarySurfacesAndStillFitsSmallerDisplays()
    {
        var state = new MainWindowLayoutState();
        state.RecordMeasuredHeight(1400);

        state.ShowSurface(MainWindowSurface.Options);
        Assert.Equal(520, state.ResolveLogicalHeight(1200, 0));
        Assert.Equal(520, state.ResolveLogicalHeight(620, 0));

        state.ShowSurface(MainWindowSurface.Operations);
        Assert.Equal(520, state.ResolveLogicalHeight(1200, 0));
        Assert.Equal(520, state.ResolveLogicalHeight(620, 0));

        state.ShowSurface(MainWindowSurface.Player);
        Assert.Equal(1200, state.ResolveLogicalHeight(1200, 0));
    }

    [Fact]
    public void ResolveLogicalHeight_AddsOuterPaddingWithoutExceedingTheDisplay()
    {
        var state = new MainWindowLayoutState();
        state.RecordMeasuredHeight(304);

        Assert.Equal(324, state.ResolveLogicalHeight(900, 20));
        Assert.Equal(310, state.ResolveLogicalHeight(310, 20));
    }
}
