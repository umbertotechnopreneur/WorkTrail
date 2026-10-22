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

/// <summary>Checks restored window bounds across monitor changes, DPI scaling, and minimum sizes.</summary>
public sealed class WindowStateCalculatorTests
{
    #region Implementation

    /// <summary>Rebases saved bounds using the previous monitor origin and DPI scale.</summary>
    [Fact]
    public void Restore_RebasesDpiAndPositionRelativeToTheSavedMonitor()
    {
        var saved = new WindowState(2070, 150, 900, 600, "display-old")
        {
            Dpi = 144,
            MonitorWorkArea = new WindowWorkArea(1920, 0, 2560, 1440),
            MonitorId = "monitor-identity"
        };
        var restored = WindowStateCalculator.RebaseToMonitor(saved,
            new WindowWorkArea(-1920, 0, 1920, 1080), "display-new", 96, new WindowMinimumSize(100, 100));

        Assert.Equal(-1820, restored.X);
        Assert.Equal(100, restored.Y);
        Assert.Equal(600, restored.Width);
        Assert.Equal(400, restored.Height);
        Assert.Equal(96u, restored.Dpi);
        Assert.Equal("monitor-identity", restored.MonitorId);
    }

    /// <summary>Retains exact saved bounds when the monitor and DPI are unchanged.</summary>
    [Fact]
    public void Restore_UnchangedMonitorAndDpiRetainsExactBoundsIncludingEdges()
    {
        var area = new WindowWorkArea(-1920, -200, 1920, 1080);
        var saved = new WindowState(-1920, -200, 900, 600, "display") { Dpi = 144, MonitorWorkArea = area };

        Assert.Equal(saved, WindowStateCalculator.RebaseToMonitor(saved, area, "display", 144, new WindowMinimumSize(100, 100)));
    }

    /// <summary>Keeps physical dimensions when an older placement does not record its original DPI.</summary>
    [Fact]
    public void Restore_LegacyPlacementKeepsPhysicalSizeWithoutGuessingItsOldDpi()
    {
        var saved = new WindowState(100, 80, 900, 600, "display");
        var restored = WindowStateCalculator.RebaseToMonitor(saved,
            new WindowWorkArea(0, 0, 1920, 1080), "display", 144, new WindowMinimumSize(100, 100));

        Assert.Equal(saved.Width, restored.Width);
        Assert.Equal(saved.Height, restored.Height);
        Assert.Equal(saved.X, restored.X);
        Assert.Equal(144u, restored.Dpi);
    }

    /// <summary>Moves placement from a removed display into the available work area after scaling.</summary>
    [Fact]
    public void Restore_RemovedDisplayClampsScaledBoundsToTheAvailableWorkArea()
    {
        var saved = new WindowState(3800, 1800, 1600, 1200, "missing")
        {
            Dpi = 96,
            MonitorWorkArea = new WindowWorkArea(1920, 0, 2560, 2160)
        };
        var restored = WindowStateCalculator.RebaseToMonitor(saved,
            new WindowWorkArea(0, 40, 1280, 680), "primary", 144, new WindowMinimumSize(470, 240));

        Assert.Equal(0, restored.X);
        Assert.Equal(40, restored.Y);
        Assert.Equal(1280, restored.Width);
        Assert.Equal(680, restored.Height);
    }

    /// <summary>Uses the same minimum dimensions for restored bounds and native window sizing.</summary>
    [Fact]
    public void WindowStateMinimumSizes_AreSharedByRestoreAndNativeSizing()
    {
        Assert.Equal(new WindowMinimumSize(470, 240), WindowStateService.GetMinimumSize(WindowStateKeys.Main));
        Assert.Equal(new WindowMinimumSize(320, 196), WindowStateService.GetMinimumSize(WindowStateKeys.Dialog));
        Assert.Equal(new WindowMinimumSize(480, 240), WindowStateService.GetMinimumSize(WindowStateKeys.WorldClocks));
        Assert.Equal(new WindowMinimumSize(192, 160), WindowStateService.GetMinimumSize(WindowStateKeys.WorldMap));
        Assert.Equal(new WindowMinimumSize(192, 192), WindowStateService.GetMinimumSize(WindowStateKeys.LunarPhase));
        Assert.Equal(new WindowMinimumSize(500, 560), WindowStateService.GetMinimumSize(WindowStateKeys.WorldClockCityPicker));
    }

    /// <summary>Preserves compact world-clock bounds below the former detailed-layout minimum.</summary>
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

    /// <summary>Expands undersized saved bounds to the supported window minimum.</summary>
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

    /// <summary>Limits even the minimum window dimensions to the available work area.</summary>
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

    #endregion
}
