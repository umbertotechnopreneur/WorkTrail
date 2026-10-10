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

public sealed class ScreenshotDayTimelineProjectionTests
{
    [Fact]
    public void Create_ProjectsAdaptiveWindowPositionsActivityAndSelectedHalfHour()
    {
        var date = DateTime.Today;
        var items = new[]
        {
            CreateItem(new DateTimeOffset(date.AddHours(4).AddMinutes(20), TimeZoneInfo.Local.GetUtcOffset(date)), 25),
            CreateItem(new DateTimeOffset(date.AddHours(7).AddMinutes(9), TimeZoneInfo.Local.GetUtcOffset(date)), 80)
        };

        var state = ScreenshotDayTimelineProjection.Create(items, 1);

        Assert.Collection(
            state.Markers,
            marker =>
            {
                Assert.Equal(0, marker.Index);
                Assert.Equal(1d / 12d, marker.NormalizedPosition, 5);
                Assert.Equal(0.25d, marker.NormalizedActivity, 5);
                Assert.False(marker.IsSelected);
                Assert.Equal(1, marker.CaptureCount);
            },
            marker =>
            {
                Assert.Equal(1, marker.Index);
                Assert.Equal(0.7875d, marker.NormalizedPosition, 5);
                Assert.Equal(0.8d, marker.NormalizedActivity, 5);
                Assert.True(marker.IsSelected);
                Assert.Equal(1, marker.CaptureCount);
            });
        Assert.Equal(TimeSpan.FromHours(4), state.WindowStart);
        Assert.Equal(TimeSpan.FromHours(8), state.WindowEnd);
        Assert.Equal(
            [
                TimeSpan.FromHours(4),
                TimeSpan.FromHours(5),
                TimeSpan.FromHours(6),
                TimeSpan.FromHours(7),
                TimeSpan.FromHours(8)
            ],
            state.Ticks);
        Assert.Equal(TimeSpan.FromHours(6).Add(TimeSpan.FromMinutes(54)), state.SelectionStart);
        Assert.Equal(TimeSpan.FromHours(7).Add(TimeSpan.FromMinutes(24)), state.SelectionEnd);
    }

    [Fact]
    public void Create_GroupsSimultaneousCapturesAndKeepsSelectedIndex()
    {
        var date = DateTime.Today;
        var offset = TimeZoneInfo.Local.GetUtcOffset(date);
        var sharedTime = new DateTimeOffset(date.AddHours(6).AddMinutes(30), offset);
        var items = new[]
        {
            CreateItem(sharedTime, 25),
            CreateItem(sharedTime, 80),
            CreateItem(new DateTimeOffset(date.AddHours(7).AddMinutes(30), offset), 40)
        };

        var state = ScreenshotDayTimelineProjection.Create(items, 1);

        Assert.Collection(
            state.Markers,
            marker =>
            {
                Assert.Equal(1, marker.Index);
                Assert.Equal(2, marker.CaptureCount);
                Assert.Equal(0.8d, marker.NormalizedActivity, 5);
                Assert.True(marker.IsSelected);
            },
            marker =>
            {
                Assert.Equal(2, marker.Index);
                Assert.Equal(1, marker.CaptureCount);
                Assert.False(marker.IsSelected);
            });
    }

    [Fact]
    public void Create_ClampsMissingActivityAndSelectionAtDayBoundaries()
    {
        var date = DateTime.Today;
        var offset = TimeZoneInfo.Local.GetUtcOffset(date);
        var early = CreateItem(new DateTimeOffset(date.AddMinutes(5), offset), null);
        var late = CreateItem(new DateTimeOffset(date.AddDays(1).AddMinutes(-5), offset), 120);

        var earlyState = ScreenshotDayTimelineProjection.Create([early, late], 0);
        var lateState = ScreenshotDayTimelineProjection.Create([early, late], 1);

        Assert.Equal(TimeSpan.Zero, earlyState.SelectionStart);
        Assert.Equal(TimeSpan.FromMinutes(20), earlyState.SelectionEnd);
        Assert.Equal(TimeSpan.Zero, earlyState.WindowStart);
        Assert.Equal(TimeSpan.FromDays(1), earlyState.WindowEnd);
        Assert.Equal(0d, earlyState.Markers[0].NormalizedActivity);
        Assert.InRange(
            lateState.SelectionStart,
            TimeSpan.FromHours(23.6666666667d) - TimeSpan.FromMilliseconds(1),
            TimeSpan.FromHours(23.6666666667d) + TimeSpan.FromMilliseconds(1));
        Assert.Equal(TimeSpan.FromDays(1), lateState.SelectionEnd);
        Assert.Equal(TimeSpan.Zero, lateState.WindowStart);
        Assert.Equal(TimeSpan.FromDays(1), lateState.WindowEnd);
        Assert.Equal(1d, lateState.Markers[1].NormalizedActivity);
    }

    [Fact]
    public void Create_RequiresASelectionOnlyWhenItemsExist()
    {
        Assert.Same(ScreenshotDayTimelineState.Empty, ScreenshotDayTimelineProjection.Create([], -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => ScreenshotDayTimelineProjection.Create([], 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => ScreenshotDayTimelineProjection.Create([CreateItem(DateTimeOffset.Now, 1)], -1));
    }

    private static ScreenshotGalleryItem CreateItem(DateTimeOffset capturedAt, int? activityIndex) => new(
        capturedAt,
        $"C:\\captures\\{capturedAt:HHmm}.webp",
        "WorkTrail",
        "monitor",
        "scheduled",
        ActivityIndex: activityIndex);
}
