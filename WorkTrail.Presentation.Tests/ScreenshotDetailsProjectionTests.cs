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
using System.Globalization;
using WorkTrail.Application;
using Xunit;

namespace WorkTrail.Presentation.Tests;

public sealed class ScreenshotDetailsProjectionTests
{
    [Fact]
    public void SelectScreenDescription_KeepsSummaryAndRequestedMonitorWithoutRenumbering()
    {
        const string markdown = "<!-- summary -->\nMain topic.\n<!-- screen:2 -->\n## Schermo 2\nChat content.\n<!-- screen:1 -->\n## Schermo 1\nDocument content.\n<!-- unmapped -->\nUnknown image.";
        var selected = ScreenshotDetailsProjection.SelectScreenDescription(markdown, 1);
        Assert.Contains("Main topic.", selected);
        Assert.Contains("Document content.", selected);
        Assert.DoesNotContain("Chat content.", selected);
        Assert.DoesNotContain("Unknown image.", selected);
        Assert.Equal(markdown, ScreenshotDetailsProjection.SelectScreenDescription(markdown, 3));
    }

    [Fact]
    public void SelectScreenDescription_PreservesUnmarkedOrAmbiguousOutput()
    {
        const string unmarked = "## Schermo 1\nA document.";
        const string duplicate = "<!-- screen:1 -->\nFirst.\n<!-- screen:1 -->\nSecond.";
        Assert.Equal(unmarked, ScreenshotDetailsProjection.SelectScreenDescription(unmarked, 1));
        Assert.Equal(duplicate, ScreenshotDetailsProjection.SelectScreenDescription(duplicate, 1));
    }

    [Fact]
    public void ParseMarkdown_DoesNotSilentlyTruncateCompleteDescriptions()
    {
        var text = new string('x', 13_000);
        Assert.Equal(text, Assert.Single(ScreenshotDetailsProjection.ParseMarkdown(text)).Text);
        var bullets = string.Join('\n', System.Linq.Enumerable.Repeat("- Visible detail", 100));
        Assert.Equal(100, ScreenshotDetailsProjection.ParseMarkdown(bullets).Count);
        Assert.Empty(ScreenshotDetailsProjection.ParseMarkdown("<!-- summary -->\n<!-- screen:2 -->"));
    }

    [Fact]
    public void ParseMarkdown_ProjectsHeadingsListsAndParagraphsWithoutInteractiveTargets()
    {
        var blocks = ScreenshotDetailsProjection.ParseMarkdown("""
            ## Focus
            Worked on **WorkTrail** and [reviewed the change](https://example.test/private).

            - Implemented the pane
            1. Verified <script>alert('no')</script> layout
            """);

        Assert.Collection(
            blocks,
            block => Assert.Equal(new SafeMarkdownBlock(SafeMarkdownBlockKind.Heading, "Focus"), block),
            block => Assert.Equal(new SafeMarkdownBlock(SafeMarkdownBlockKind.Paragraph, "Worked on WorkTrail and reviewed the change."), block),
            block => Assert.Equal(new SafeMarkdownBlock(SafeMarkdownBlockKind.Bullet, "Implemented the pane", "•"), block),
            block => Assert.Equal(new SafeMarkdownBlock(SafeMarkdownBlockKind.Numbered, "Verified alert('no') layout", "1."), block));
        Assert.DoesNotContain(blocks, block => block.Text.Contains("https://", StringComparison.Ordinal));
        Assert.DoesNotContain(blocks, block => block.Text.Contains("<script>", StringComparison.Ordinal));
    }

    [Fact]
    public void ToPlainTextPreview_RemovesMarkdownHeadingsFormattingAndTargets()
    {
        var preview = ScreenshotDetailsProjection.ToPlainTextPreview("""
            ## Activity

            The user is listening to **Spotify** while reviewing [liked songs](https://example.test/private).
            """);

        Assert.Equal("The user is listening to Spotify while reviewing liked songs.", preview);
        Assert.DoesNotContain("#", preview, StringComparison.Ordinal);
        Assert.DoesNotContain("**", preview, StringComparison.Ordinal);
        Assert.DoesNotContain("https://", preview, StringComparison.Ordinal);
    }

    [Fact]
    public void Create_FormatsOptionalSnapshotDetailsWithoutInventingMissingAiContent()
    {
        var capturedAt = new DateTimeOffset(2026, 8, 9, 2, 21, 0, TimeSpan.Zero);
        var item = new ScreenshotGalleryItem(
            capturedAt,
            "C:\\captures\\frame.webp",
            "ChatGPT",
            "monitor",
            "scheduled",
            [new ActivityLabelSample(capturedAt, "Review")],
            AiDescriptionMarkdown: null,
            AiAnalyzedAt: null,
            ActivityIndex: 72);

        var state = ScreenshotDetailsProjection.Create(
            item,
            CultureInfo.GetCultureInfo("it-IT"),
            "Schermo",
            "Pianificata",
            "--");

        Assert.Equal("ChatGPT", state.Application);
        Assert.Equal("--", state.WindowTitle);
        Assert.Equal("Schermo", state.Screen);
        Assert.Equal("Schermo", state.CaptureKind);
        Assert.Equal("Pianificata", state.Origin);
        Assert.Equal("72", state.ActivityIndex);
        Assert.Equal("Review", state.ActivityLabels);
        Assert.Equal("--", state.MouseClicks);
        Assert.Empty(state.AiDescription);
        Assert.Null(state.AnalysisTime);
    }

    [Fact]
    public void Create_ProjectsVerifiableWindowScreenAndIntervalTelemetry()
    {
        var capturedAt = new DateTimeOffset(2026, 8, 9, 2, 21, 0, TimeSpan.Zero);
        var item = new ScreenshotGalleryItem(
            capturedAt,
            "C:\\captures\\frame.webp",
            "Visual Studio Code",
            "monitor",
            "scheduled",
            ForegroundWindowTitle: "WorkTrail — ScreenshotWindow.xaml",
            ScreenIndex: 2,
            ScreenName: "Monitor 2",
            MouseClicks: 18);

        var state = CreateState(item);

        Assert.Equal("WorkTrail — ScreenshotWindow.xaml", state.WindowTitle);
        Assert.Equal("Monitor 2", state.Screen);
        Assert.Equal("18", state.MouseClicks);
    }

    private static ScreenshotDetailsViewState CreateState(ScreenshotGalleryItem item)
    {
        return ScreenshotDetailsProjection.Create(
            item,
            CultureInfo.GetCultureInfo("it-IT"),
            "Schermo",
            "Pianificata",
            "--");
    }
}
