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
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using SkiaSharp;
using WorkTrail.Services;
using Xunit;

namespace WorkTrail.Core.Tests;

public sealed class ScreenCaptureServiceTests
{
    [Fact]
    public void CaptureRules_PrioritizeActiveCropWithoutRenumberingThreeScreens()
    {
        var displays = new[]
        {
            new CaptureDisplay(1, new NativeMethods.Rect { Left = -100, Top = 0, Right = 0, Bottom = 100 }),
            new CaptureDisplay(2, new NativeMethods.Rect { Left = 0, Top = 0, Right = 100, Bottom = 100 }),
            new CaptureDisplay(3, new NativeMethods.Rect { Left = 100, Top = 0, Right = 200, Bottom = 100 })
        };
        var targets = Plan(ScreenshotCaptureRules.Load(), displays,
            new NativeMethods.Rect { Left = 110, Top = 10, Right = 190, Bottom = 90 }, false);

        Assert.Equal(4, targets.Count);
        Assert.Equal("active-window", targets[0].Role);
        Assert.Equal(3, targets[0].Display.Index);
        Assert.Equal(3, targets[1].Display.Index);
        Assert.Equal(1, targets[2].Display.Index);
        Assert.Equal(2, targets[3].Display.Index);
    }

    [Fact]
    public void CaptureRules_SpanningWindowIsOneCropWithOnlyVisibleRegions()
    {
        var displays = new[]
        {
            new CaptureDisplay(1, new NativeMethods.Rect { Left = 0, Top = 0, Right = 100, Bottom = 100 }),
            new CaptureDisplay(2, new NativeMethods.Rect { Left = 120, Top = 0, Right = 220, Bottom = 100 })
        };
        var targets = Plan(ScreenshotCaptureRules.Load(), displays,
            new NativeMethods.Rect { Left = 90, Top = -10, Right = 200, Bottom = 80 }, true);

        var target = Assert.Single(targets);
        Assert.Equal(2, target.Display.Index);
        Assert.Equal(new[] { 1, 2 }, target.RelatedScreenIndices);
        Assert.Equal(2, target.VisibleRegions.Count);
        Assert.Equal(0, target.Bounds.Top);
        Assert.Equal(90, target.Bounds.Left);
        Assert.Equal(200, target.Bounds.Right);
    }

    [Fact]
    public void CaptureRules_DoNotDuplicateFullscreenCropAsMonitorContext()
    {
        var bounds = new NativeMethods.Rect { Left = 0, Top = 0, Right = 100, Bottom = 100 };
        var targets = Plan(ScreenshotCaptureRules.Load(), [new CaptureDisplay(1, bounds)], bounds, false);

        Assert.Equal("active-window", Assert.Single(targets).Role);
    }

    [Fact]
    public void CaptureRules_RejectWindowOutsideDesktop()
    {
        var display = new CaptureDisplay(1, new NativeMethods.Rect { Left = 0, Top = 0, Right = 100, Bottom = 100 });
        Assert.Throws<InvalidOperationException>(() => Plan(ScreenshotCaptureRules.Load(), [display],
            new NativeMethods.Rect { Left = 200, Top = 0, Right = 300, Bottom = 100 }, false));
    }

    [Fact]
    public void CaptureRules_LoadShippedPolicy()
    {
        var rules = ScreenshotCaptureRules.Load();
        Assert.Equal(4, rules.Rules.Count);
        Assert.Equal("active-window", rules.Rules[0].Subject);
        Assert.Equal("secondary-windows", rules.Rules[1].Subject);
        Assert.All(rules.Rules.Skip(2), rule => Assert.Equal("gallery", rule.Destination));
        Assert.True(rules.Rules[1].RequireSecondaryMatch);
        Assert.Equal(0.15, rules.Rules[1].MinimumVisibleFraction);
        var profiles = rules.ApplicationProfiles!;
        foreach (var id in new[] { "word", "excel", "teams", "visual-studio", "visual-studio-code", "notepad",
            "windows-terminal", "console-host", "ssms", "oracle-sql-developer", "datagrip", "jetbrains" })
            Assert.Equal("high", Assert.Single(profiles.Where(profile => profile.Id == id)).ActiveAttention);
        foreach (var id in new[] { "spotify", "explorer", "settings" })
            Assert.Equal("low", Assert.Single(profiles.Where(profile => profile.Id == id)).ActiveAttention);
        Assert.Contains("Code - Insiders", Assert.Single(profiles.Where(profile => profile.Id == "visual-studio-code")).ProcessNames);
        Assert.Contains("WindowsTerminal", Assert.Single(profiles.Where(profile => profile.Id == "windows-terminal")).ProcessNames);
        Assert.Contains("Ssms", Assert.Single(profiles.Where(profile => profile.Id == "ssms")).ProcessNames);
        Assert.Contains("sqldeveloper64W", Assert.Single(profiles.Where(profile => profile.Id == "oracle-sql-developer")).ProcessNames);
        Assert.DoesNotContain(profiles, profile => profile.ProcessNames.Contains("java", StringComparer.OrdinalIgnoreCase)
            || profile.ProcessNames.Contains("javaw", StringComparer.OrdinalIgnoreCase));
        Assert.DoesNotContain("ApplicationFrameHost", rules.AnalysisBlacklist!);
        Assert.DoesNotContain("msedgewebview2", rules.AnalysisBlacklist!);
    }

    [Theory]
    [InlineData("winword", "high")]
    [InlineData("WINWORD", "high")]
    [InlineData("Spotify", "low")]
    [InlineData("unknown-editor", "normal")]
    public void CaptureRules_ApplicationAttentionDoesNotDisableAnalysis(string processName, string attention)
    {
        var bounds = new NativeMethods.Rect { Left = 0, Top = 0, Right = 100, Bottom = 100 };
        var foreground = new ForegroundCaptureTarget((IntPtr)1, bounds, processName, "Application", "Topic", "Window");
        var target = Assert.Single(ScreenshotCaptureRules.Load().Plan([new CaptureDisplay(1, bounds)], foreground,
            [new CaptureWindow((IntPtr)1, bounds, processName, "Application", "Window")], true));
        Assert.Equal(attention, target.Attention);
        Assert.True(target.ForAnalysis);
    }

    [Fact]
    public void CaptureRules_BackgroundHighApplicationUsesNormalAttention()
    {
        var full = new NativeMethods.Rect { Left = 0, Top = 0, Right = 100, Bottom = 100 };
        var front = new NativeMethods.Rect { Left = 0, Top = 0, Right = 50, Bottom = 100 };
        var foreground = new ForegroundCaptureTarget((IntPtr)1, front, "chat", "Chat", "Topic", "Chat");
        var targets = ScreenshotCaptureRules.Load().Plan([new CaptureDisplay(1, full)], foreground,
            [new CaptureWindow((IntPtr)1, front, "chat", "Chat", "Chat"),
             new CaptureWindow((IntPtr)2, full, "winword", "Word", "Document")], false);
        Assert.Equal("normal", Assert.Single(targets.Where(target => target.Role == "secondary-window")).Attention);
    }

    [Fact]
    public void CaptureRules_BlacklistVetoSurvivesRuleUnionAndKeepsGallery()
    {
        var bounds = new NativeMethods.Rect { Left = 0, Top = 0, Right = 100, Bottom = 100 };
        var rules = new ScreenshotCaptureRules([
            new("gallery", true, "active-window", "window", "primary", "gallery"),
            new("analysis", true, "active-window", "window", "primary", "analysis")], AnalysisBlacklist: ["TEST"]);
        var target = Assert.Single(Plan(rules, [new CaptureDisplay(1, bounds)], bounds, true));
        Assert.False(target.ForAnalysis);
        Assert.True(target.ForGallery);
    }

    [Fact]
    public void CaptureRules_BlacklistRejectsAnalysisOnlySelection()
    {
        var bounds = new NativeMethods.Rect { Left = 0, Top = 0, Right = 100, Bottom = 100 };
        var rules = new ScreenshotCaptureRules([
            new("analysis", true, "active-window", "window", "primary", "analysis")], AnalysisBlacklist: ["test"]);
        Assert.Throws<InvalidOperationException>(() => Plan(rules, [new CaptureDisplay(1, bounds)], bounds, true));
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void CaptureRules_OverviewVetoDependsOnVisibleBlacklistedPixels(bool blacklistOnTop, bool expectedAnalysis)
    {
        var full = new NativeMethods.Rect { Left = 0, Top = 0, Right = 100, Bottom = 100 };
        var blockedBounds = new NativeMethods.Rect { Left = 0, Top = 0, Right = 10, Bottom = 10 };
        var foreground = new ForegroundCaptureTarget((IntPtr)1, full, "chat", "Chat", "Topic", "Chat");
        var active = new CaptureWindow((IntPtr)1, full, "chat", "Chat", "Chat");
        var blocked = new CaptureWindow((IntPtr)2, blockedBounds, "blocked", "Blocked", "Overlay", false);
        var rules = new ScreenshotCaptureRules([
            new("overview", true, "active-monitor", "monitor", "context", "both")], AnalysisBlacklist: ["blocked"]);
        var target = Assert.Single(rules.Plan([new CaptureDisplay(1, full)], foreground,
            blacklistOnTop ? [blocked, active] : [active, blocked], false));
        Assert.Equal(expectedAnalysis, target.ForAnalysis);
        Assert.True(target.ForGallery);
    }

    [Fact]
    public void CaptureRules_BlacklistedOverlayDoesNotBlockMaskedActiveCrop()
    {
        var full = new NativeMethods.Rect { Left = 0, Top = 0, Right = 100, Bottom = 100 };
        var overlay = new NativeMethods.Rect { Left = 0, Top = 0, Right = 10, Bottom = 100 };
        var foreground = new ForegroundCaptureTarget((IntPtr)1, full, "chat", "Chat", "Topic", "Chat");
        var rules = new ScreenshotCaptureRules([
            new("active", true, "active-window", "window", "primary", "both")], AnalysisBlacklist: ["blocked"]);
        var target = Assert.Single(rules.Plan([new CaptureDisplay(1, full)], foreground,
            [new CaptureWindow((IntPtr)2, overlay, "blocked", "Blocked", "Overlay", false),
             new CaptureWindow((IntPtr)1, full, "chat", "Chat", "Chat")], true));
        Assert.True(target.ForAnalysis);
        Assert.Equal(10, target.Bounds.Left);
    }

    [Fact]
    public void CaptureRules_RejectAmbiguousApplicationAliases()
    {
        var rules = new ScreenshotCaptureRules([
            new("active", true, "active-window", "window", "primary", "both")], [
            new("first", "First", ["app"], "high", "normal"),
            new("second", "Second", ["APP"], "low", "low")]);
        Assert.Throws<InvalidDataException>(rules.Validate);
    }

    private static System.Collections.Generic.IReadOnlyList<CaptureImageTarget> Plan(ScreenshotCaptureRules rules,
        System.Collections.Generic.IReadOnlyList<CaptureDisplay> displays, NativeMethods.Rect bounds, bool activeWindowOnly)
    {
        var foreground = new ForegroundCaptureTarget((IntPtr)1, bounds, "test", "Test", "Test", "Test");
        return rules.Plan(displays, foreground, [new CaptureWindow((IntPtr)1, bounds, "test", "Test", "Test")], activeWindowOnly);
    }

    [Fact]
    public void CaptureRules_SecondaryWindowExcludesOccludedPixels()
    {
        var full = new NativeMethods.Rect { Left = 0, Top = 0, Right = 100, Bottom = 100 };
        var front = new NativeMethods.Rect { Left = 20, Top = 20, Right = 80, Bottom = 80 };
        var foreground = new ForegroundCaptureTarget((IntPtr)1, front, "chat", "Chat", "Topic", "Chat");
        var targets = ScreenshotCaptureRules.Load().Plan([new CaptureDisplay(1, full)], foreground,
            [new CaptureWindow((IntPtr)1, front, "chat", "Chat", "Chat"), new CaptureWindow((IntPtr)2, full, "winword", "Word", "Document")], false);
        var secondary = Assert.Single(targets.Where(target => target.Role == "secondary-window"));
        Assert.Equal(4, secondary.VisibleRegions.Count);
        Assert.Equal(6400, secondary.VisibleRegions.Sum(region => (region.Right - region.Left) * (region.Bottom - region.Top)));
        Assert.Equal("Word", secondary.ApplicationName);
        Assert.True(secondary.ForAnalysis);
    }

    [Fact]
    public void CaptureRules_ProcessFilterDoesNotRemoveOccluders()
    {
        var full = new NativeMethods.Rect { Left = 0, Top = 0, Right = 100, Bottom = 100 };
        var front = new NativeMethods.Rect { Left = 0, Top = 0, Right = 50, Bottom = 100 };
        var foreground = new ForegroundCaptureTarget((IntPtr)1, front, "chat", "Chat", "Topic", "Chat");
        var rules = new ScreenshotCaptureRules([new("word-only", true, "secondary-windows", "window", "secondary", "analysis", ["WORD"])]);
        var target = Assert.Single(rules.Plan([new CaptureDisplay(1, full)], foreground,
            [new CaptureWindow((IntPtr)1, front, "chat", "Chat", "Chat"), new CaptureWindow((IntPtr)2, full, "word", "Word", "Document")], false));
        Assert.Equal(50, target.Bounds.Left);
        Assert.True(target.ForAnalysis);
        Assert.False(target.ForGallery);
    }

    [Fact]
    public void CaptureRules_SkipShellTargetsAndFullyOccludedApplications()
    {
        var full = new NativeMethods.Rect { Left = 0, Top = 0, Right = 100, Bottom = 100 };
        var overlay = new NativeMethods.Rect { Left = 0, Top = 0, Right = 100, Bottom = 10 };
        var foreground = new ForegroundCaptureTarget((IntPtr)1, full, "chat", "Chat", "Topic", "Chat");
        var targets = ScreenshotCaptureRules.Load().Plan([new CaptureDisplay(1, full)], foreground,
            [new CaptureWindow((IntPtr)9, overlay, "shell", "Shell", "", false),
             new CaptureWindow((IntPtr)1, full, "chat", "Chat", "Chat"),
             new CaptureWindow((IntPtr)2, full, "winword", "Word", "Document")], false);
        Assert.DoesNotContain(targets, target => target.Role == "secondary-window");
        var active = Assert.Single(targets.Where(target => target.Role == "active-window"));
        Assert.Equal(10, active.Bounds.Top);
        Assert.Equal(9000, active.VisibleRegions.Sum(region => (region.Right - region.Left) * (region.Bottom - region.Top)));
    }

    [Fact]
    public void CaptureRules_DuplicateRulesUnionDestinationsAndPromotePriority()
    {
        var bounds = new NativeMethods.Rect { Left = 0, Top = 0, Right = 100, Bottom = 100 };
        var rules = new ScreenshotCaptureRules([
            new("gallery", true, "active-window", "window", "context", "gallery"),
            new("analysis", true, "active-window", "window", "primary", "analysis")]);
        var target = Assert.Single(Plan(rules, [new CaptureDisplay(1, bounds)], bounds, false));
        Assert.True(target.ForAnalysis);
        Assert.True(target.ForGallery);
        Assert.Equal("primary", target.Priority);
    }

    [Fact]
    public void CaptureRules_ActiveOnlyModeDoesNotExpandToOtherSubjects()
    {
        var bounds = new NativeMethods.Rect { Left = 10, Top = 10, Right = 90, Bottom = 90 };
        var target = Assert.Single(Plan(ScreenshotCaptureRules.Load(),
            [new CaptureDisplay(1, new NativeMethods.Rect { Left = 0, Top = 0, Right = 100, Bottom = 100 })], bounds, true));
        Assert.Equal("active-window", target.Role);
    }

    [Theory]
    [InlineData("active-window", "monitor", "primary", "both")]
    [InlineData("secondary-monitors", "window", "context", "gallery")]
    [InlineData("unknown", "window", "primary", "both")]
    [InlineData("active-window", "window", "highest", "both")]
    [InlineData("active-window", "window", "primary", "unknown")]
    public void CaptureRules_RejectInvalidRuleValues(string subject, string capture, string priority, string destination)
    {
        Assert.Throws<InvalidDataException>(() => new ScreenshotCaptureRules([new("invalid", true, subject, capture, priority, destination)]).Validate());
    }

    [Fact]
    public void CaptureRules_RejectDisabledOnlySelection()
    {
        var bounds = new NativeMethods.Rect { Left = 0, Top = 0, Right = 100, Bottom = 100 };
        var rules = new ScreenshotCaptureRules([new("disabled", false, "active-window", "window", "primary", "both")]);
        Assert.Throws<InvalidOperationException>(() => Plan(rules, [new CaptureDisplay(1, bounds)], bounds, false));
    }

    [Fact]
    public void SelectFocusedDisplayIndex_UsesDisplayWithLargestForegroundIntersection()
    {
        var displays = new[]
        {
            new NativeMethods.Rect { Left = 0, Top = 0, Right = 100, Bottom = 100 },
            new NativeMethods.Rect { Left = 100, Top = 0, Right = 220, Bottom = 100 }
        };
        var foregroundWindow = new NativeMethods.Rect { Left = 90, Top = 10, Right = 180, Bottom = 80 };

        var focusedDisplay = ScreenCaptureService.SelectFocusedDisplayIndex(displays, foregroundWindow);

        Assert.Equal(1, focusedDisplay);
    }

    [Fact]
    public void EncodeBitmapAsWebp_WritesOneCleanArtifactAndCreatesNoTemporaryPng()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"worktrail-capture-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var screenshotPath = Path.Combine(directory, "capture.webp");

        try
        {
            using var bitmap = new Bitmap(640, 180, PixelFormat.Format32bppRgb);
            using (var graphics = Graphics.FromImage(bitmap))
            {
                graphics.Clear(Color.FromArgb(24, 80, 160));
            }

            ScreenCaptureService.EncodeBitmapAsWebp(bitmap, screenshotPath);

            Assert.True(File.Exists(screenshotPath));
            Assert.Single(Directory.GetFiles(directory, "*.webp"));
            Assert.Empty(Directory.GetFiles(directory, "*.png"));

            using var decoded = SKBitmap.Decode(screenshotPath);
            Assert.NotNull(decoded);
            Assert.Equal(640, decoded.Width);
            Assert.Equal(180, decoded.Height);
            var pixel = decoded.GetPixel(32, 32);
            Assert.InRange(pixel.Red, (byte)16, (byte)32);
            Assert.InRange(pixel.Green, (byte)72, (byte)88);
            Assert.InRange(pixel.Blue, (byte)152, (byte)168);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void EncodeBitmapAsWebp_DoesNotMutateTheCapturedPixels()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"worktrail-capture-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var screenshotPath = Path.Combine(directory, "capture.webp");

        try
        {
            using var bitmap = new Bitmap(64, 32, PixelFormat.Format32bppRgb);
            using (var graphics = Graphics.FromImage(bitmap))
            {
                graphics.Clear(Color.CornflowerBlue);
            }

            var sourcePixel = bitmap.GetPixel(8, 8);
            ScreenCaptureService.EncodeBitmapAsWebp(bitmap, screenshotPath);

            Assert.Equal(sourcePixel, bitmap.GetPixel(8, 8));
            using var decoded = SKBitmap.Decode(screenshotPath);
            Assert.NotNull(decoded);
            Assert.Equal(64, decoded.Width);
            Assert.Equal(32, decoded.Height);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

}
