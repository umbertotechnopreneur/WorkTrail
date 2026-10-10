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


using WorkTrail.Application;
using Xunit;

namespace WorkTrail.Core.Tests;

public sealed class WindowStateCalculatorTests
{
    [Fact]
    public void WindowStateMinimumSizes_AreSharedByRestoreAndNativeSizing()
    {
        Assert.Equal(new WindowMinimumSize(470, 240), WindowStateService.GetMinimumSize(WindowStateKeys.Main));
        Assert.Equal(new WindowMinimumSize(760, 560), WindowStateService.GetMinimumSize(WindowStateKeys.ActivityCalendar));
        Assert.Equal(new WindowMinimumSize(640, 560), WindowStateService.GetMinimumSize(WindowStateKeys.AiScreenshotReprocessing));
        Assert.Equal(new WindowMinimumSize(760, 540), WindowStateService.GetMinimumSize(WindowStateKeys.Screenshots));
        Assert.Equal(new WindowMinimumSize(560, 360), WindowStateService.GetMinimumSize(WindowStateKeys.OcrText));
        Assert.Equal(new WindowMinimumSize(560, 156), WindowStateService.GetMinimumSize(WindowStateKeys.Search));
        Assert.Equal(new WindowMinimumSize(560, 420), WindowStateService.GetMinimumSize(WindowStateKeys.SearchIndexing));
        Assert.Equal(new WindowMinimumSize(620, 480), WindowStateService.GetMinimumSize(WindowStateKeys.Schedule));
        Assert.Equal(new WindowMinimumSize(320, 196), WindowStateService.GetMinimumSize(WindowStateKeys.Dialog));
        Assert.Equal(new WindowMinimumSize(480, 240), WindowStateService.GetMinimumSize(WindowStateKeys.WorldClocks));
        Assert.Equal(new WindowMinimumSize(192, 160), WindowStateService.GetMinimumSize(WindowStateKeys.WorldMap));
        Assert.Equal(new WindowMinimumSize(192, 192), WindowStateService.GetMinimumSize(WindowStateKeys.LunarPhase));
        Assert.Equal(new WindowMinimumSize(500, 560), WindowStateService.GetMinimumSize(WindowStateKeys.WorldClockCityPicker));
        Assert.Equal(new WindowMinimumSize(480, 480), WindowStateService.GetMinimumSize(WindowStateKeys.AiConnectionTest));
    }

    [Fact]
    public void WorldClockRestore_PreservesManualWidgetBoundsBelowTheFormerDetailedMinimum()
    {
        var saved = new WindowState(120, 160, 520, 260, @"\\.\DISPLAY1");
        var workArea = new WindowWorkArea(0, 0, 1920, 1080);
        var minimum = WindowStateService.GetMinimumSize(WindowStateKeys.WorldClocks);

        var restored = WindowStateCalculator.ClampToWorkArea(saved, workArea, @"\\.\DISPLAY1", minimum.Width, minimum.Height);

        Assert.Equal(saved, restored);
    }

    /// <summary>Compact astronomy widgets retain their user-selected bounds when reopened.</summary>
    [Theory]
    [InlineData(WindowStateKeys.WorldMap, 192, 160)]
    [InlineData(WindowStateKeys.LunarPhase, 192, 192)]
    public void AstronomyRestore_PreservesCompactWidgetBounds(string windowKey, int width, int height)
    {
        var saved = new WindowState(120, 160, width, height, @"\\.\DISPLAY1");
        var workArea = new WindowWorkArea(0, 0, 1920, 1080);
        var minimum = WindowStateService.GetMinimumSize(windowKey);

        var restored = WindowStateCalculator.ClampToWorkArea(saved, workArea, @"\\.\DISPLAY1", minimum.Width, minimum.Height);

        Assert.Equal(saved, restored);
    }

    /// <summary>Undersized saved widgets regain enough space for their caption controls.</summary>
    [Theory]
    [InlineData(WindowStateKeys.WorldMap, 192, 160)]
    [InlineData(WindowStateKeys.LunarPhase, 192, 192)]
    public void AstronomyRestore_ClampsToCompactMinimum(string windowKey, int width, int height)
    {
        var saved = new WindowState(1900, 1060, 100, 40, @"\\.\DISPLAY1");
        var workArea = new WindowWorkArea(0, 0, 1920, 1080);
        var minimum = WindowStateService.GetMinimumSize(windowKey);

        var restored = WindowStateCalculator.ClampToWorkArea(saved, workArea, @"\\.\DISPLAY1", minimum.Width, minimum.Height);

        Assert.Equal(width, restored.Width);
        Assert.Equal(height, restored.Height);
        Assert.Equal(workArea.Right - width, restored.X);
        Assert.Equal(workArea.Bottom - height, restored.Y);
    }

    [Fact]
    public void ClampToWorkArea_ExpandsTinyPersistedBoundsToWindowMinimum()
    {
        var saved = new WindowState(3800, 2000, 100, 40, @"\\.\DISPLAY1");
        var workArea = new WindowWorkArea(0, 0, 1920, 1080);

        var restored = WindowStateCalculator.ClampToWorkArea(saved, workArea, @"\\.\DISPLAY2", 760, 540);

        Assert.Equal(760, restored.Width);
        Assert.Equal(540, restored.Height);
        Assert.Equal(1160, restored.X);
        Assert.Equal(540, restored.Y);
        Assert.Equal(@"\\.\DISPLAY2", restored.MonitorDeviceName);
    }

    [Fact]
    public void ClampToWorkArea_BoundsMinimumToSmallWorkArea()
    {
        var saved = new WindowState(-400, -200, 100, 40, @"\\.\DISPLAY1");
        var workArea = new WindowWorkArea(10, 20, 300, 180);

        var restored = WindowStateCalculator.ClampToWorkArea(saved, workArea, @"\\.\DISPLAY2", 760, 540);

        Assert.Equal(300, restored.Width);
        Assert.Equal(180, restored.Height);
        Assert.Equal(10, restored.X);
        Assert.Equal(20, restored.Y);
        Assert.Equal(@"\\.\DISPLAY2", restored.MonitorDeviceName);
    }
}
