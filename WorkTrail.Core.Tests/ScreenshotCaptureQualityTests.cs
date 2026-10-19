// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using WorkTrail.Services;
using Xunit;

namespace WorkTrail.Core.Tests;

public sealed class ScreenshotCaptureQualityTests
{
    private static readonly NativeMethods.Rect Desktop = Rect(0, 0, 1000, 800);

    /// <summary>Excluded WorkTrail panels neither become targets nor hide an eligible secondary window.</summary>
    [Fact]
    public void AuxiliaryPanel_IsAbsentFromTargetsAndSecondaryVisibility()
    {
        var foreground = Window(1, Rect(1000, 0, 1500, 800));
        var word = Window(2, Desktop, "winword");
        var panel = Window(3, Desktop, "WorkTrail") with { IsCaptureExcluded = true };
        var targets = ScreenshotCaptureRules.Load().Plan(
            [new CaptureDisplay(1, Desktop), new CaptureDisplay(2, foreground.Bounds)],
            Focus(foreground), [panel, foreground, word], false);

        Assert.DoesNotContain(targets, target => target.WindowHandle == panel.WindowHandle);
        Assert.Equal(800000L, VisibleArea(Assert.Single(targets, target => target.WindowHandle == word.WindowHandle)));
    }

    /// <summary>An excluded foreground panel vetoes direct and all-screen capture.</summary>
    /// <param name="activeWindowOnly">Whether capture is restricted to the active window.</param>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AuxiliaryForeground_IsExcludedBeforeAcquisition(bool activeWindowOnly)
    {
        var panel = Window(1, Desktop, "WorkTrail") with { IsCaptureExcluded = true };
        var error = Assert.Throws<ScreenshotCapturePreconditionException>(() => ScreenshotCaptureRules.Load().Plan(
            [new CaptureDisplay(1, Desktop), new CaptureDisplay(2, Rect(1000, 0, 2000, 800))],
            Focus(panel), [panel, Window(2, Rect(1000, 0, 2000, 800), "winword")], activeWindowOnly));
        Assert.Equal(ScreenshotCaptureDecision.ContentExcluded, error.Decision);
    }

    /// <summary>Background windows must still satisfy visibility selection before reading their surfaces.</summary>
    [Fact]
    public void SecondaryWindow_StillRejectsMostlyCoveredContent()
    {
        var foreground = Window(1, Rect(0, 0, 900, 800));
        var word = Window(2, Desktop, "winword");
        var targets = ScreenshotCaptureRules.Load().Plan([new CaptureDisplay(1, Desktop)],
            Focus(foreground), [foreground, word], false);
        Assert.DoesNotContain(targets, target => target.WindowHandle == word.WindowHandle);
    }

    /// <summary>The active compositor surface remains complete despite overlapping desktop windows.</summary>
    /// <param name="coveredWidth">The width covered by a higher z-order window.</param>
    [Theory]
    [InlineData(500)]
    [InlineData(980)]
    [InlineData(1000)]
    public void ShippedActive_CapturesCompleteSurfaceBehindOverlaps(int coveredWidth)
    {
        var active = Window(1, Desktop);
        var overlay = Window(2, Rect(0, 0, coveredWidth, 800), "overlay", application: false);

        var target = Assert.Single(ScreenshotCaptureRules.Load().Plan(
            [new CaptureDisplay(1, Desktop)], Focus(active), [overlay, active], true));
        Assert.Equal(800000L, VisibleArea(target));
        Assert.Single(target.VisibleRegions);
    }

    /// <summary>A partially offscreen active window keeps its entire compositor surface.</summary>
    [Fact]
    public void ShippedActive_PreservesOffscreenContent()
    {
        var active = Window(1, Rect(-3000, 0, 1000, 800));

        var target = Assert.Single(ScreenshotCaptureRules.Load().Plan(
            [new CaptureDisplay(1, Desktop)], Focus(active), [active], true));
        Assert.Equal(-3000, target.Bounds.Left);
        Assert.Equal(3200000L, VisibleArea(target));
    }

    /// <summary>A central overlay cannot leave a hole in a direct window surface.</summary>
    [Fact]
    public void ShippedActive_CentralOverlayDoesNotPunchAHole()
    {
        var active = Window(1, Desktop);
        var overlay = Window(2, Rect(200, 300, 800, 500), "overlay", application: false);

        Assert.Equal(800000L, VisibleArea(Assert.Single(ScreenshotCaptureRules.Load().Plan(
            [new CaptureDisplay(1, Desktop)], Focus(active), [overlay, active], true))));
    }

    /// <summary>A thin desktop overlay cannot split the active compositor surface.</summary>
    [Fact]
    public void ShippedActive_ThinOverlayDoesNotSplitTheImage()
    {
        var active = Window(1, Desktop);
        var overlay = Window(2, Rect(490, 0, 510, 800), "overlay", application: false);

        Assert.Equal(800000L, VisibleArea(Assert.Single(ScreenshotCaptureRules.Load().Plan(
            [new CaptureDisplay(1, Desktop)], Focus(active), [overlay, active], true))));
    }

    /// <summary>Small overlaps do not remove any pixels belonging to the selected window.</summary>
    /// <param name="central">Whether the small occluder lies inside the crop instead of along its edge.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ShippedActive_AcceptsModestOverlapWithoutCropping(bool central)
    {
        var active = Window(1, Desktop);
        var overlay = Window(2, central ? Rect(450, 350, 550, 450) : Rect(0, 0, 100, 800),
            "overlay", application: false);
        var target = Assert.Single(ScreenshotCaptureRules.Load().Plan(
            [new CaptureDisplay(1, Desktop)], Focus(active), [overlay, active], true));

        Assert.Equal("active-window", target.Role);
        Assert.True(target.ForAnalysis);
        Assert.True(target.ForGallery);
        Assert.Single(target.VisibleRegions);
        Assert.Equal(800000L, VisibleArea(target));
    }

    /// <summary>Fully visible dialogs still need enough physical pixels to produce useful crops.</summary>
    /// <param name="width">The physical width of the candidate.</param>
    /// <param name="height">The physical height of the candidate.</param>
    /// <param name="accepted">Whether both minimum dimensions are satisfied.</param>
    [Theory]
    [InlineData(239, 200, false)]
    [InlineData(300, 159, false)]
    [InlineData(240, 160, true)]
    public void ShippedActive_RequiresMinimumDimensions(int width, int height, bool accepted)
    {
        var active = Window(1, Rect(100, 100, 100 + width, 100 + height));
        IReadOnlyList<CaptureImageTarget> Plan() => ScreenshotCaptureRules.Load().Plan(
            [new CaptureDisplay(1, Desktop)], Focus(active), [active], true);

        if (!accepted)
        {
            AssertInsufficient(Plan);
            return;
        }

        var target = Assert.Single(Plan());
        Assert.Equal(width, target.Bounds.Right - target.Bounds.Left);
        Assert.Equal(height, target.Bounds.Bottom - target.Bounds.Top);
    }

    /// <summary>Real shared monitor edges connect fragments even with negative and staggered origins.</summary>
    [Fact]
    public void ShippedActive_AcceptsAdjacentStaggeredMonitorsWithoutRenumbering()
    {
        var displays = new[]
        {
            new CaptureDisplay(1, Rect(-1280, -200, 0, 824)),
            new CaptureDisplay(2, Rect(0, 0, 1920, 1080))
        };
        var active = Window(1, Rect(-400, -100, 800, 700));
        var target = Assert.Single(ScreenshotCaptureRules.Load().Plan(displays, Focus(active), [active], true));

        Assert.Equal(2, target.Display.Index);
        Assert.Equal(new[] { 1, 2 }, target.RelatedScreenIndices);
        Assert.Single(target.VisibleRegions);
        Assert.Equal(-400, target.Bounds.Left);
        Assert.Equal(-100, target.Bounds.Top);
        Assert.Equal(960000L, VisibleArea(target));
    }

    /// <summary>Monitor gaps do not create gaps in the active window's own surface.</summary>
    /// <param name="gap">The physical space between two monitor rectangles.</param>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(20)]
    public void ShippedActive_CapturesAcrossMonitorGaps(int gap)
    {
        var displays = new[]
        {
            new CaptureDisplay(1, Rect(0, 0, 500, 800)),
            new CaptureDisplay(2, Rect(500 + gap, 0, 1000 + gap, 800))
        };
        var active = Window(1, Rect(0, 0, 1000 + gap, 800));
        IReadOnlyList<CaptureImageTarget> Plan() => ScreenshotCaptureRules.Load().Plan(displays, Focus(active), [active], true);

        Assert.Equal((1000L + gap) * 800, VisibleArea(Assert.Single(Plan())));
    }

    /// <summary>Corner-only monitor contact cannot split a direct active-window surface.</summary>
    [Fact]
    public void ActiveSurface_CornerOnlyMonitorContactDoesNotCrop()
    {
        var active = Window(1, Rect(0, 0, 1000, 1000));
        var policy = Policy(new("active", true, "active-window", "window", "primary", "both",
            MinimumContiguousFraction: 0.90));

        Assert.Equal(1000000L, VisibleArea(Assert.Single(policy.Plan(
            [new CaptureDisplay(1, Rect(0, 0, 500, 500)), new CaptureDisplay(2, Rect(500, 500, 1000, 1000))],
            Focus(active), [active], true))));
    }

    /// <summary>The dominant connected component must meet the configured fraction of visible pixels.</summary>
    /// <param name="dominantWidth">The width of the larger of two disconnected visible components.</param>
    /// <param name="accepted">Whether its area reaches the inclusive ninety-percent threshold.</param>
    [Theory]
    [InlineData(899, false)]
    [InlineData(900, true)]
    public void ContiguousQuality_UsesLargestComponentAndInclusiveThreshold(int dominantWidth, bool accepted)
    {
        var active = Window(1, Rect(0, 0, 1001, 800));
        var foreground = Window(2, Rect(2000, 0, 2100, 100));
        var policy = Policy(new("secondary", true, "secondary-windows", "window", "secondary", "both",
            MinimumContiguousFraction: 0.90));
        IReadOnlyList<CaptureImageTarget> Plan() => policy.Plan(
            [new CaptureDisplay(1, Rect(0, 0, dominantWidth, 800)),
             new CaptureDisplay(2, Rect(dominantWidth + 1, 0, 1001, 800)),
             new CaptureDisplay(3, foreground.Bounds)], Focus(foreground), [foreground, active], false);

        if (!accepted)
        {
            AssertInsufficient(Plan);
            return;
        }

        Assert.Equal(800800L, VisibleArea(Assert.Single(Plan())));
    }

    /// <summary>A gallery panorama survives an unusable active crop but is not promoted to AI input.</summary>
    [Fact]
    public void ShippedAllScreens_RetainsGalleryOverviewWhenWindowQualityFails()
    {
        var active = Window(1, Rect(100, 100, 300, 300));
        var target = Assert.Single(ScreenshotCaptureRules.Load().Plan(
            [new CaptureDisplay(1, Desktop)], Focus(active), [active], false));

        Assert.Equal("monitor-context", target.Role);
        Assert.True(target.ForGallery);
        Assert.False(target.ForAnalysis);
        Assert.Equal(800000L, VisibleArea(target));
    }

    /// <summary>Gallery-only active windows also use complete compositor surfaces.</summary>
    [Fact]
    public void GalleryOnlyWindow_KeepsCoveredContent()
    {
        var active = Window(1, Desktop);
        var overlay = Window(2, Rect(0, 0, 500, 800), "overlay", application: false);
        var shipped = ScreenshotCaptureRules.Load();
        var policy = shipped with { Rules = [shipped.Rules[0] with { Destination = "gallery" }] };

        Assert.Equal(800000L, VisibleArea(Assert.Single(policy.Plan(
            [new CaptureDisplay(1, Desktop)], Focus(active), [overlay, active], true))));
    }

    /// <summary>A title-only overlay is not part of the active window's compositor surface.</summary>
    [Fact]
    public void ShippedQuality_TitleOnlyOverlayDoesNotRemoveTargetContent()
    {
        var active = Window(1, Desktop);
        var spotify = Window(2, Rect(0, 0, 100, 800), "Spotify");
        var target = Assert.Single(ScreenshotCaptureRules.Load().Plan(
            [new CaptureDisplay(1, Desktop)], Focus(active), [spotify, active], true));

        Assert.Equal(active.WindowHandle, target.WindowHandle);
        Assert.Equal(0, target.Bounds.Left);
        Assert.True(target.ForAnalysis);
        Assert.Single(target.VisibleRegions);
        Assert.Equal(800000L, VisibleArea(target));
    }

    /// <summary>Invalid floating-point thresholds cannot silently disable quality gates.</summary>
    /// <param name="fraction">An invalid visible-area ratio.</param>
    [Theory]
    [InlineData(-0.01)]
    [InlineData(1.01)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void Validate_RejectsInvalidQualityFractions(double fraction)
    {
        var rule = new ScreenshotCaptureRule("active", true, "active-window", "window", "primary", "both");

        Assert.Throws<InvalidDataException>(Policy(rule with { MinimumVisibleFraction = fraction }).Validate);
        Assert.Throws<InvalidDataException>(Policy(rule with { MinimumVisibleBoundsFraction = fraction }).Validate);
        Assert.Throws<InvalidDataException>(Policy(rule with { MinimumContiguousFraction = fraction }).Validate);
    }

    /// <summary>Dimension limits reject negative and excessive configuration values.</summary>
    /// <param name="pixels">An invalid physical dimension.</param>
    [Theory]
    [InlineData(-1)]
    [InlineData(32769)]
    public void Validate_RejectsInvalidQualityDimensions(int pixels)
    {
        var rule = new ScreenshotCaptureRule("active", true, "active-window", "window", "primary", "both");

        Assert.Throws<InvalidDataException>(Policy(rule with { MinimumWidthPixels = pixels }).Validate);
        Assert.Throws<InvalidDataException>(Policy(rule with { MinimumHeightPixels = pixels }).Validate);
    }

    /// <summary>Inclusive limits allow disabled gates and exact full-visibility requirements.</summary>
    /// <param name="fraction">A supported inclusive fraction bound.</param>
    /// <param name="pixels">A supported inclusive dimension bound.</param>
    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 32768)]
    public void Validate_AcceptsInclusiveQualityBounds(double fraction, int pixels)
    {
        Policy(new("active", true, "active-window", "window", "primary", "both",
            MinimumVisibleFraction: fraction, MinimumWidthPixels: pixels, MinimumHeightPixels: pixels,
            MinimumVisibleBoundsFraction: fraction, MinimumContiguousFraction: fraction)).Validate();
    }

    /// <summary>Window-content gates cannot be attached to monitor panorama rules by mistake.</summary>
    [Fact]
    public void Validate_RejectsWindowQualityOnMonitorRules()
    {
        var rule = new ScreenshotCaptureRule("monitor", true, "active-monitor", "monitor", "context", "gallery");

        Assert.Throws<InvalidDataException>(Policy(rule with { MinimumVisibleFraction = 0.1 }).Validate);
        Assert.Throws<InvalidDataException>(Policy(rule with { MinimumWidthPixels = 1 }).Validate);
        Assert.Throws<InvalidDataException>(Policy(rule with { MinimumHeightPixels = 1 }).Validate);
        Assert.Throws<InvalidDataException>(Policy(rule with { MinimumVisibleBoundsFraction = 0.1 }).Validate);
        Assert.Throws<InvalidDataException>(Policy(rule with { MinimumContiguousFraction = 0.1 }).Validate);
    }

    private static ScreenshotCaptureRules Policy(ScreenshotCaptureRule rule) => new([rule]);

    private static void AssertInsufficient(Func<IReadOnlyList<CaptureImageTarget>> plan)
    {
        var failure = Assert.Throws<ScreenshotCapturePreconditionException>(() => plan());
        Assert.Equal(ScreenshotCaptureDecision.InsufficientVisibleContent, failure.Decision);
    }

    private static CaptureWindow Window(int id, NativeMethods.Rect bounds, string process = "Code", bool application = true) =>
        new((IntPtr)id, bounds, process, process, "Project", application);

    private static ForegroundCaptureTarget Focus(CaptureWindow window) =>
        new(window.WindowHandle, window.Bounds, window.ProcessName, window.ApplicationName, "Context", window.WindowTitle);

    private static long VisibleArea(CaptureImageTarget target) =>
        target.VisibleRegions.Sum(region => (long)(region.Right - region.Left) * (region.Bottom - region.Top));

    private static NativeMethods.Rect Rect(int left, int top, int right, int bottom) =>
        new() { Left = left, Top = top, Right = right, Bottom = bottom };
}
