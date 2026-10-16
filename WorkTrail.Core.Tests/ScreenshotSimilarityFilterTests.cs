// SPDX-License-Identifier: MIT
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using WorkTrail.Services;
using Xunit;

namespace WorkTrail.Core.Tests;

public sealed class ScreenshotSimilarityFilterTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 17, 12, 0, 0, TimeSpan.Zero);
    private static readonly ScreenshotSimilarityOptions Options = new(SampleWidth: 16, SampleHeight: 16);

    [Fact]
    public void Filter_RequiresPublishedBaseline()
    {
        var filter = new ScreenshotSimilarityFilter();
        var fingerprint = Solid(Color.Black);

        Assert.False(filter.IsUnchanged("window", fingerprint, Start, Options, _ => true));
        Assert.False(filter.IsUnchanged("window", fingerprint, Start.AddSeconds(1), Options, _ => true));
        filter.Remember("window", fingerprint, Start.AddSeconds(1), Options, "retained.webp");
        Assert.True(filter.IsUnchanged("window", fingerprint, Start.AddSeconds(2), Options, _ => true));
    }

    [Fact]
    public void Filter_DoesNotRenewForceRefreshWhenSkipping()
    {
        var filter = new ScreenshotSimilarityFilter();
        var fingerprint = Solid(Color.Black);
        filter.Remember("window", fingerprint, Start, Options, "retained.webp");

        Assert.True(filter.IsUnchanged("window", fingerprint, Start.AddMinutes(14), Options, _ => true));
        Assert.False(filter.IsUnchanged("window", fingerprint, Start.AddMinutes(15), Options, _ => true));
        Assert.False(filter.IsUnchanged("window", fingerprint, Start.AddMinutes(16), Options, _ => true));
        filter.Remember("window", fingerprint, Start.AddMinutes(16), Options, "retained.webp");
        Assert.True(filter.IsUnchanged("window", fingerprint, Start.AddMinutes(17), Options, _ => true));
    }

    [Fact]
    public void Filter_AccumulatesSmallChangesAgainstRetainedBaseline()
    {
        var options = Options with { MaxMeanDifference = 2, PixelDifferenceThreshold = 6 };
        var filter = new ScreenshotSimilarityFilter();
        filter.Remember("window", Solid(Color.FromArgb(20, 20, 20)), Start, options, "retained.webp");

        Assert.True(filter.IsUnchanged("window", Solid(Color.FromArgb(21, 21, 21)), Start.AddSeconds(1), options, _ => true));
        Assert.True(filter.IsUnchanged("window", Solid(Color.FromArgb(22, 22, 22)), Start.AddSeconds(2), options, _ => true));
        Assert.False(filter.IsUnchanged("window", Solid(Color.FromArgb(23, 23, 23)), Start.AddSeconds(3), options, _ => true));
    }

    [Fact]
    public void Filter_ContextKeyChangesForceRetention()
    {
        var filter = new ScreenshotSimilarityFilter();
        var fingerprint = Solid(Color.Black);
        filter.Remember("spotify:track-one", fingerprint, Start, Options, "retained.webp");

        Assert.False(filter.IsUnchanged("spotify:track-two", fingerprint, Start.AddSeconds(1), Options, _ => true));
        Assert.False(filter.IsUnchanged("Spotify:track-one", fingerprint, Start.AddSeconds(1), Options, _ => true));
        Assert.True(filter.IsUnchanged("spotify:track-one", fingerprint, Start.AddSeconds(1), Options, _ => true));
    }

    [Fact]
    public void Filter_EvictsOldestRetainedEntryAtCapacity()
    {
        var options = Options with { MaxEntries = 2 };
        var filter = new ScreenshotSimilarityFilter();
        var fingerprint = Solid(Color.Black);
        filter.Remember("one", fingerprint, Start, options, "retained.webp");
        filter.Remember("two", fingerprint, Start.AddSeconds(1), options, "retained.webp");
        Assert.True(filter.IsUnchanged("one", fingerprint, Start.AddSeconds(2), options, _ => true));
        filter.Remember("three", fingerprint, Start.AddSeconds(3), options, "retained.webp");

        Assert.False(filter.IsUnchanged("one", fingerprint, Start.AddSeconds(4), options, _ => true));
        Assert.True(filter.IsUnchanged("two", fingerprint, Start.AddSeconds(4), options, _ => true));
        Assert.True(filter.IsUnchanged("three", fingerprint, Start.AddSeconds(4), options, _ => true));
    }

    [Fact]
    public void Filter_ClockRollbackAndDisabledPolicyForceRetention()
    {
        var filter = new ScreenshotSimilarityFilter();
        var fingerprint = Solid(Color.Black);
        filter.Remember("window", fingerprint, Start, Options, "retained.webp");

        Assert.False(filter.IsUnchanged("window", fingerprint, Start.AddSeconds(-1), Options, _ => true));
        Assert.False(filter.IsUnchanged("window", fingerprint, Start.AddSeconds(1), Options with { Enabled = false }, _ => true));
        filter.Clear();
        Assert.False(filter.IsUnchanged("window", fingerprint, Start.AddSeconds(1), Options, _ => true));
    }

    [Fact]
    public void Filter_DoesNotReplaceNewerPublishedBaselineWithOlderCandidate()
    {
        var filter = new ScreenshotSimilarityFilter();
        var black = Solid(Color.Black);
        var white = Solid(Color.White);
        filter.Remember("window", white, Start.AddSeconds(2), Options, "retained.webp");
        filter.Remember("window", black, Start, Options, "retained.webp");

        Assert.True(filter.IsUnchanged("window", white, Start.AddSeconds(3), Options, _ => true));
        Assert.False(filter.IsUnchanged("window", black, Start.AddSeconds(3), Options, _ => true));
    }

    [Fact]
    public void Fingerprint_RetainsLocalizedChangesEvenWhenGlobalMeanIsSmall()
    {
        var options = Options with { MaxMeanDifference = 2, MaxChangedPixelFraction = 1 };
        using var before = NewBitmap(16, 16, Color.Black);
        using var after = NewBitmap(16, 16, Color.Black);
        after.SetPixel(8, 8, Color.White);

        Assert.False(ScreenshotVisualFingerprint.Create(before, options)
            .IsSimilarTo(ScreenshotVisualFingerprint.Create(after, options), options));
    }

    [Fact]
    public void Fingerprint_RetainsBroadSubtleChanges()
    {
        var before = Solid(Color.FromArgb(20, 20, 20));
        var after = Solid(Color.FromArgb(21, 21, 21));

        Assert.False(before.IsSimilarTo(after, Options));
    }

    [Fact]
    public void Fingerprint_CanAllowExplicitlyConfiguredSmallNoise()
    {
        var options = Options with { MaxMeanDifference = 1.1 };
        var before = Solid(Color.FromArgb(20, 20, 20));
        var after = Solid(Color.FromArgb(21, 21, 21));

        Assert.True(before.IsSimilarTo(after, options));
    }

    [Fact]
    public void Fingerprint_UsesColorNotOnlyBrightness()
    {
        Assert.False(Solid(Color.Red).IsSimilarTo(Solid(Color.Blue), Options));
    }

    [Fact]
    public void Fingerprint_DimensionOrSamplePolicyChangesForceRetention()
    {
        using var small = NewBitmap(16, 16, Color.Black);
        using var large = NewBitmap(32, 16, Color.Black);
        var first = ScreenshotVisualFingerprint.Create(small, Options);
        var resized = ScreenshotVisualFingerprint.Create(large, Options);
        var options = Options with { SampleWidth = 32 };
        var resampled = ScreenshotVisualFingerprint.Create(small, options);

        Assert.False(first.IsSimilarTo(resized, Options));
        Assert.False(first.IsSimilarTo(resampled, options));
        Assert.False(first.IsSimilarTo(first, options));
    }

    [Fact]
    public void Options_RejectInvalidLimits()
    {
        Assert.Throws<ArgumentException>(() => (Options with { SampleWidth = 0 }).Validate());
        Assert.Throws<ArgumentException>(() => (Options with { SampleHeight = 513 }).Validate());
        Assert.Throws<ArgumentException>(() => (Options with { MaxMeanDifference = double.NaN }).Validate());
        Assert.Throws<ArgumentException>(() => (Options with { MaxChangedPixelFraction = double.PositiveInfinity }).Validate());
        Assert.Throws<ArgumentException>(() => (Options with { MaxChangedPixelFraction = 1.1 }).Validate());
        Assert.Throws<ArgumentException>(() => (Options with { PixelDifferenceThreshold = 256 }).Validate());
        Assert.Throws<ArgumentException>(() => (Options with { MaxPixelDifference = -1 }).Validate());
        Assert.Throws<ArgumentException>(() => (Options with { ForceRefreshMinutes = 0 }).Validate());
        Assert.Throws<ArgumentException>(() => (Options with { MaxEntries = 0 }).Validate());
    }

    [Fact]
    public void SimilarityKey_IncludesTargetIdentityGeometryAndDestinations()
    {
        var target = Target();
        var key = Key(target);
        var shifted = new NativeMethods.Rect { Left = 1, Top = 0, Right = 17, Bottom = 16 };
        var smallerMask = new NativeMethods.Rect { Left = 0, Top = 0, Right = 8, Bottom = 16 };
        CaptureImageTarget[] changedTargets =
        [
            target with { WindowHandle = new IntPtr(11) },
            target with { Bounds = shifted },
            target with { Display = new CaptureDisplay(2, target.Display.Bounds) },
            target with { Display = new CaptureDisplay(1, shifted) },
            target with { VisibleRegions = [smallerMask] },
            target with { ForAnalysis = false },
            target with { ForGallery = false },
            target with { Role = "secondary-window" },
            target with { Attention = "high" },
            target with { RuleReason = "title-hint:changed" },
            target with { MicroPromptIds = ["titles-only"] }
        ];

        Assert.Equal(key, Key(target with { }));
        Assert.All(changedTargets, changed => Assert.NotEqual(key, Key(changed)));
        Assert.NotEqual(key, Key(target, "another-process"));
        Assert.NotEqual(key, ScreenCaptureService.CreateSimilarityKey(
            Path.Combine(Path.GetTempPath(), "WorkTrail-other-capture-root"), target, "Spotify", []));
    }

    [Fact]
    public void SimilarityKey_TitleChangeRetainsIdenticalPixels()
    {
        var target = Target();
        var filter = new ScreenshotSimilarityFilter();
        var fingerprint = Solid(Color.Black);
        filter.Remember(Key(target), fingerprint, Start, Options, "retained.webp");

        Assert.True(filter.IsUnchanged(Key(target), fingerprint, Start.AddSeconds(1), Options, _ => true));
        Assert.False(filter.IsUnchanged(Key(target with { WindowTitle = "Track two" }),
            fingerprint, Start.AddSeconds(1), Options, _ => true));
    }

    [Fact]
    public void Filter_DeletedRetainedFileInvalidatesBaselineUntilRepublished()
    {
        var filter = new ScreenshotSimilarityFilter();
        var fingerprint = Solid(Color.Black);
        filter.Remember("window", fingerprint, Start, Options, "retained.webp");

        Assert.False(filter.IsUnchanged("window", fingerprint, Start.AddSeconds(1), Options, path =>
        {
            Assert.Equal("retained.webp", path);
            return false;
        }));
        Assert.False(filter.IsUnchanged("window", fingerprint, Start.AddSeconds(2), Options, _ => true));
        filter.Remember("window", fingerprint, Start.AddSeconds(3), Options, "new-retained.webp");
        Assert.True(filter.IsUnchanged("window", fingerprint, Start.AddSeconds(4), Options, path => path == "new-retained.webp"));
    }

    [Fact]
    public void SimilarityKey_MonitorIncludesVisibleWindowContext()
    {
        var target = Target() with { WindowHandle = IntPtr.Zero, Role = "active-monitor" };
        var first = new ScreenCaptureService.VisibleCaptureWindow(target.Bounds,
            new ScreenshotCaptureContext("Spotify", "Spotify", "Music", "Track one"), new IntPtr(10));
        var changed = first with { Context = first.Context with { WindowTitle = "Track two" } };
        string directory = Path.Combine(Path.GetTempPath(), "WorkTrail-similarity-tests");

        Assert.NotEqual(
            ScreenCaptureService.CreateSimilarityKey(directory, target, null, [first]),
            ScreenCaptureService.CreateSimilarityKey(directory, target, null, [changed]));
    }

    [Fact]
    public void SimilarityPolicy_SpotifyPermitsSmallProgressChangesButWorkAppsStayConservative()
    {
        var rules = ScreenshotCaptureRules.Load();
        var spotify = rules.ResolveWindowPolicy("Spotify", "Track one").Similarity;
        var editor = rules.ResolveWindowPolicy("Code", "Project").Similarity;
        var unknown = rules.ResolveWindowPolicy("unknown-app", "Window").Similarity;
        Assert.Equal(rules.Similarity, editor);
        Assert.Equal(rules.Similarity, unknown);
        Assert.True(spotify.MaxMeanDifference > editor.MaxMeanDifference);
        Assert.True(spotify.MaxChangedPixelFraction > editor.MaxChangedPixelFraction);
        Assert.Equal(255, spotify.MaxPixelDifference);

        using var before = NewBitmap(128, 96, Color.Black);
        using var after = NewBitmap(128, 96, Color.Black);
        using (var graphics = Graphics.FromImage(after))
            graphics.FillRectangle(Brushes.White, 10, 80, 20, 1);
        var baseline = ScreenshotVisualFingerprint.Create(before, spotify);
        var candidate = ScreenshotVisualFingerprint.Create(after, spotify);

        Assert.True(candidate.IsSimilarTo(baseline, spotify));
        Assert.False(candidate.IsSimilarTo(baseline, editor));
    }

    [Fact]
    public void SimilarityPolicy_ContextOverridesProfileAndProfileOverridesGlobal()
    {
        var global = Options with { MaxMeanDifference = 0.2 };
        var profile = Options with { MaxMeanDifference = 0.5 };
        var context = Options with { Enabled = false };
        var rules = MinimalRules() with
        {
            Similarity = global,
            ApplicationProfiles = [new("editor", "Editor", ["editor"], "high", "normal", Similarity: profile)],
            ContextRules = [new("debugging", true, 100, ["editor"], ["Debug"], Similarity: context)]
        };
        rules.Validate();

        Assert.Equal(context, rules.ResolveWindowPolicy("editor", "Debug session").Similarity);
        Assert.Equal(profile, rules.ResolveWindowPolicy("editor", "Project").Similarity);
        Assert.Equal(global, rules.ResolveWindowPolicy("unknown-app", "Debug session").Similarity);
    }

    [Fact]
    public void SimilarityPolicy_RejectsInvalidGlobalProfileAndContextSettings()
    {
        var invalid = Options with { MaxChangedPixelFraction = -1 };
        Assert.Throws<ArgumentException>(() => (MinimalRules() with { Similarity = invalid }).Validate());
        Assert.Throws<ArgumentException>(() => (MinimalRules() with
        {
            ApplicationProfiles = [new("editor", "Editor", ["editor"], "high", "normal", Similarity: invalid)]
        }).Validate());
        Assert.Throws<ArgumentException>(() => (MinimalRules() with
        {
            ContextRules = [new("debugging", true, 100, ["editor"], ["Debug"], Similarity: invalid)]
        }).Validate());
    }

    private static string Key(CaptureImageTarget target, string processName = "Spotify") =>
        ScreenCaptureService.CreateSimilarityKey(Path.Combine(Path.GetTempPath(), "WorkTrail-similarity-tests"),
            target, processName, []);

    private static CaptureImageTarget Target()
    {
        var bounds = new NativeMethods.Rect { Left = 0, Top = 0, Right = 16, Bottom = 16 };
        return new CaptureImageTarget("active-window", "active-window", new CaptureDisplay(1, bounds),
            bounds, [bounds], [1], new IntPtr(10), "Spotify", "Track one", "primary", true, true);
    }

    private static ScreenshotCaptureRules MinimalRules() => new(
        [new("active", true, "active-window", "window", "primary", "both")]);

    private static ScreenshotVisualFingerprint Solid(Color color)
    {
        using var bitmap = NewBitmap(16, 16, color);
        return ScreenshotVisualFingerprint.Create(bitmap, Options);
    }

    private static Bitmap NewBitmap(int width, int height, Color color)
    {
        var bitmap = new Bitmap(width, height, PixelFormat.Format24bppRgb);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.Clear(color);
        return bitmap;
    }
}
