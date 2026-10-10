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
using WorkTrail.Services;
using Xunit;

namespace WorkTrail.Presentation.Tests;

public sealed class ScreenshotDetailsProjectionTests
{
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
            "--",
            new LocalizationService("it-IT").Translate);

        Assert.Equal("ChatGPT", state.Application);
        Assert.Equal("--", state.WindowTitle);
        Assert.Equal("Schermo", state.Screen);
        Assert.Equal("Schermo", state.CaptureKind);
        Assert.Equal("Pianificata", state.Origin);
        Assert.Equal("72", state.ActivityIndex);
        Assert.Equal("Review", state.ActivityLabels);
        Assert.Equal("--", state.MouseClicks);
        Assert.Equal("--", state.CpuUsage);
        Assert.Equal("--", state.GpuUsage);
        Assert.Empty(state.AiDescription);
        Assert.Null(state.AnalysisTime);
        Assert.Null(state.OcrText);
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
            MouseClicks: 18,
            CpuUsagePercent: 42,
            GpuUsagePercent: 7);

        var state = CreateState(item);

        Assert.Equal("WorkTrail — ScreenshotWindow.xaml", state.WindowTitle);
        Assert.Equal("Monitor 2", state.Screen);
        Assert.Equal("18", state.MouseClicks);
        Assert.Equal("42%", state.CpuUsage);
        Assert.Equal("7%", state.GpuUsage);
    }

    [Fact]
    public void Create_PrefersAiCorrectedOcrTextOverRawText()
    {
        var capturedAt = new DateTimeOffset(2026, 8, 9, 2, 21, 0, TimeSpan.Zero);
        var item = CreateItemWithOcr(
            capturedAt,
            rawText: "Riunone proggeto",
            correctedText: "Riunione progetto");

        var state = CreateState(item);

        Assert.Equal("Riunione progetto", state.OcrText);
    }

    [Fact]
    public void Create_FallsBackToRawOcrTextWhenAiCorrectionIsUnavailable()
    {
        var capturedAt = new DateTimeOffset(2026, 8, 9, 2, 21, 0, TimeSpan.Zero);
        var item = CreateItemWithOcr(
            capturedAt,
            rawText: "Raw OCR text",
            correctedText: "   ");

        var state = CreateState(item);

        Assert.Equal("Raw OCR text", state.OcrText);
    }

    private static ScreenshotGalleryItem CreateItemWithOcr(
        DateTimeOffset capturedAt,
        string rawText,
        string? correctedText)
    {
        var ocr = new OcrRawSnapshot(
            ScreenshotTextExtractionStatus.Succeeded,
            rawText,
            "it-IT",
            TextAngleDegrees: null,
            capturedAt,
            "Windows.Media.Ocr",
            PixelWidth: 1920,
            PixelHeight: 1080,
            Lines: Array.Empty<OcrLineSnapshot>());
        var refinement = correctedText is null
            ? null
            : new OcrAiRefinement(
                correctedText,
                "it-IT",
                new OcrStructuredSummary(
                    "Overview",
                    Array.Empty<string>(),
                    Array.Empty<string>(),
                    Array.Empty<string>()),
                capturedAt.AddSeconds(1));

        return new ScreenshotGalleryItem(
            capturedAt,
            "C:\\captures\\frame.webp",
            "WorkTrail",
            "monitor",
            "scheduled",
            TextSnapshot: new ScreenshotTextSnapshot("C:\\captures\\frame.webp", ocr, refinement));
    }

    private static ScreenshotDetailsViewState CreateState(ScreenshotGalleryItem item)
    {
        return ScreenshotDetailsProjection.Create(
            item,
            CultureInfo.GetCultureInfo("it-IT"),
            "Schermo",
            "Pianificata",
            "--",
            new LocalizationService("it-IT").Translate);
    }
}
