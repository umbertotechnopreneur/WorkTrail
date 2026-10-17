// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using WorkTrail.Services;
using WorkTrail.Providers;
using Xunit;

namespace WorkTrail.Core.Tests;

public sealed class ScreenshotTitleOnlyCaptureTests
{
    private static readonly NativeMethods.Rect Full = Rect(0, 0, 100, 100);

    /// <summary>Image-only and desktop modes both reject an entirely title-only selection.</summary>
    /// <param name="activeWindowOnly">Whether capture is restricted to the foreground crop.</param>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void SpotifyOnly_IsExcludedBeforeAcquisition(bool activeWindowOnly)
    {
        var spotify = Window(1, Full, "sPoTiFy");
        var failure = Assert.Throws<ScreenshotCapturePreconditionException>(() =>
            ScreenshotCaptureRules.Load().Plan([new CaptureDisplay(1, Full)], Focus(spotify), [spotify], activeWindowOnly));

        Assert.Equal(ScreenshotCaptureDecision.ContentExcluded, failure.Decision);
    }

    /// <summary>Attention and destination unions cannot restore images for a title-only process.</summary>
    [Fact]
    public void TitleOnly_CannotBeOverriddenByDestinationsOrAttention()
    {
        var spotify = Window(1, Full, "Spotify");
        var rules = Rules() with
        {
            Rules = [
                new("gallery", true, "active-window", "window", "primary", "gallery"),
                new("ai", true, "active-window", "window", "primary", "analysis")],
            ApplicationProfiles = [new("spotify", "Spotify", ["Spotify"], "high", "high")],
            ContextRules = [new("important", true, 100, ["Spotify"], ["window"], CaptureSecondary: true)]
        };

        Assert.Equal(ScreenshotCaptureDecision.ContentExcluded,
            Assert.Throws<ScreenshotCapturePreconditionException>(() =>
                rules.Plan([new CaptureDisplay(1, Full)], Focus(spotify), [spotify], true)).Decision);
    }

    /// <summary>Visible title-only pixels are absent from both crops and monitor acquisition regions.</summary>
    /// <param name="spotifyIsForeground">Whether the title-only application has foreground focus.</param>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void VisibleSpotify_IsMaskedInPanoramasAndNeverGetsACrop(bool spotifyIsForeground)
    {
        var spotify = Window(1, Rect(0, 0, 40, 100), "Spotify");
        var word = Window(2, Full, "winword");
        var targets = Rules().Plan([new CaptureDisplay(1, Full)], Focus(spotifyIsForeground ? spotify : word), [spotify, word], false);

        Assert.DoesNotContain(targets, target => target.WindowHandle == spotify.WindowHandle);
        Assert.Contains(targets, target => target.WindowHandle == word.WindowHandle);
        var panorama = Assert.Single(targets, target => target.Role == "monitor-context");
        Assert.True(panorama.ForGallery);
        Assert.True(panorama.ForAnalysis);
        Assert.All(targets, target => Assert.All(target.VisibleRegions, region => Assert.True(region.Left >= 40)));
        Assert.Equal(6000, Area(panorama.VisibleRegions));
    }

    /// <summary>Occlusion does not remove eligible pixels from the window above Spotify.</summary>
    [Fact]
    public void HiddenSpotify_DoesNotMaskTheWindowCoveringIt()
    {
        var word = Window(1, Full, "winword");
        var spotify = Window(2, Full, "Spotify");
        var targets = Rules().Plan([new CaptureDisplay(1, Full)], Focus(word), [word, spotify], false);

        Assert.All(targets, target => Assert.Equal(10000, Area(target.VisibleRegions)));
        Assert.DoesNotContain(targets, target => target.WindowHandle == spotify.WindowHandle);
    }

    /// <summary>A fully excluded display leaves the other display identity and content intact.</summary>
    [Fact]
    public void SpotifyOnSecondMonitor_DoesNotRemoveOrRenumberTheWorkMonitor()
    {
        var second = Rect(100, 0, 200, 100);
        var word = Window(1, Full, "winword");
        var spotify = Window(2, second, "Spotify");
        var targets = Rules().Plan([new CaptureDisplay(1, Full), new CaptureDisplay(2, second)],
            Focus(word), [word, spotify], false);

        Assert.NotEmpty(targets);
        Assert.All(targets, target => Assert.Equal(1, target.Display.Index));
        Assert.All(targets, target => Assert.Equal(10000, Area(target.VisibleRegions)));
    }

    /// <summary>Spanning excluded regions are removed independently on each display.</summary>
    [Fact]
    public void SpanningSpotify_IsMaskedOnEachMonitorWithoutReadingBehindIt()
    {
        var second = Rect(100, 0, 200, 100);
        var spotify = Window(1, Rect(50, 0, 150, 100), "Spotify");
        var word = Window(2, Rect(0, 0, 50, 100), "winword");
        var targets = Rules().Plan([new CaptureDisplay(1, Full), new CaptureDisplay(2, second)],
            Focus(word), [spotify, word], false);

        Assert.Contains(targets, target => target.Display.Index == 1);
        Assert.Contains(targets, target => target.Display.Index == 2);
        Assert.All(targets, target => Assert.All(target.VisibleRegions,
            region => Assert.True(region.Right <= 50 || region.Left >= 150)));
    }

    /// <summary>Helper windows from a title-only process remain image exclusions.</summary>
    [Fact]
    public void SpotifyHelperWindow_StillMasksItsVisiblePixels()
    {
        var helper = Window(1, Rect(0, 0, 20, 100), "Spotify") with { IsApplicationWindow = false };
        var word = Window(2, Full, "winword");
        var targets = Rules().Plan([new CaptureDisplay(1, Full)], Focus(word), [helper, word], false);

        Assert.All(targets, target => Assert.All(target.VisibleRegions, region => Assert.True(region.Left >= 20)));
    }

    /// <summary>Image focus metadata does not misattribute another screenshot to Spotify.</summary>
    [Fact]
    public void FocusMetadata_DoesNotRetainExcludedAppOrTrackTitle()
    {
        var spotify = Window(1, Full, "Spotify") with { WindowTitle = "Private track title" };
        var metadata = new ScreenCaptureService().CreateFocusMetadata(new CaptureDisplay(2, Full),
            Focus(spotify), "monitor-2", Rules());

        Assert.Equal(2, metadata.ScreenIndex);
        Assert.Equal("monitor-2", metadata.ArtifactStem);
        Assert.Empty(metadata.ApplicationName);
        Assert.Empty(metadata.WindowTitle);
    }

    /// <summary>Only configured exact aliases are excluded; generic shared hosts stay eligible.</summary>
    [Fact]
    public void TitleOnly_RejectsEmptyAliasesAndDoesNotBlockSharedHosts()
    {
        Assert.Throws<InvalidDataException>(() => (Rules() with { TitleOnlyProcesses = [" "] }).Validate());
        var rules = ScreenshotCaptureRules.Load();
        Assert.True(rules.IsAnalysisExcluded("SPOTIFY"));
        Assert.False(rules.IsCaptureExcluded("msedge"));
        Assert.False(rules.IsCaptureExcluded("ApplicationFrameHost"));
        Assert.False(rules.IsCaptureExcluded("msedgewebview2"));
    }

    private static ScreenshotCaptureRules Rules() => new([
        new("active", true, "active-window", "window", "primary", "both"),
        new("secondary", true, "secondary-windows", "window", "secondary", "both"),
        new("overview", true, "active-monitor", "monitor", "context", "both"),
        new("other-overview", true, "secondary-monitors", "monitor", "context", "both")], TitleOnlyProcesses: ["Spotify"]);

    /// <summary>The title-only activity path preserves the literal title without media inference.</summary>
    [Fact]
    public void TitleOnly_KeepsTheLiteralForegroundTitleInExistingActivityFields()
    {
        const string title = "Artist - Track title";
        var rules = ScreenshotCaptureRules.Load();
        Assert.True(rules.IsTitleOnly("SPOTIFY"));
        var context = new ActivityContextProviderRegistry().Resolve(new ForegroundWindowInfo("Spotify", title));

        Assert.Equal("Spotify", context.Application);
        Assert.Equal(title, context.Context);
        Assert.Equal(title, context.Attributes!["Title"]);
        Assert.Equal("Spotify", context.Attributes["Process"]);
        Assert.Equal(2, context.Attributes.Count);
    }

    /// <summary>Every supported locale explains an empty title-only capture selection.</summary>
    [Fact]
    public void CaptureExclusionMessage_IsLocalizedForEverySupportedLanguage()
    {
        foreach (var language in LocalizationService.SupportedLanguages)
            Assert.False(string.IsNullOrWhiteSpace(new LocalizationService(language).Translate("ScreenshotCaptureExcluded")));
    }

    private static CaptureWindow Window(int id, NativeMethods.Rect bounds, string process) =>
        new(new IntPtr(id), bounds, process, process, "window");

    private static ForegroundCaptureTarget Focus(CaptureWindow window) =>
        new(window.WindowHandle, window.Bounds, window.ProcessName, window.ApplicationName, "context", window.WindowTitle);

    private static NativeMethods.Rect Rect(int left, int top, int right, int bottom) =>
        new() { Left = left, Top = top, Right = right, Bottom = bottom };

    private static long Area(IReadOnlyList<NativeMethods.Rect> regions) =>
        regions.Sum(region => (long)(region.Right - region.Left) * (region.Bottom - region.Top));
}
