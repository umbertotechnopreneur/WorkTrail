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


using WorkTrail.Ocr;
using Xunit;

namespace WorkTrail.Ocr.Tests;

public sealed class WindowsScreenshotOcrServiceTests
{
    [Fact]
    public void Constructor_WithNullOptions_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new WindowsScreenshotOcrService(null!));
    }

    [Fact]
    public async Task ExtractAsync_WhenDisabled_ReturnsBeforePathValidationOrCancellation()
    {
        var service = new WindowsScreenshotOcrService(new OcrOptions { Enabled = false });
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        ScreenshotOcrResult result = await service.ExtractAsync("invalid\0path", cancellation.Token);

        Assert.False(service.IsEnabled);
        Assert.Equal(OcrExtractionStatus.Disabled, result.Status);
        Assert.Equal(string.Empty, result.RawText);
        Assert.Null(result.EffectiveLanguageTag);
        Assert.Null(result.TextAngleDegrees);
        Assert.Null(result.PixelWidth);
        Assert.Null(result.PixelHeight);
        Assert.Equal(WindowsScreenshotOcrService.EngineName, result.EngineName);
        Assert.Equal(TimeSpan.Zero, result.CompletedAtUtc.Offset);
        Assert.Empty(result.Lines);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public async Task ExtractAsync_WhenEnabledAndPathIsBlank_Throws(string imagePath)
    {
        var service = new WindowsScreenshotOcrService(new OcrOptions { Enabled = true });

        await Assert.ThrowsAsync<ArgumentException>(() => service.ExtractAsync(imagePath));
    }

    [Fact]
    public async Task ExtractAsync_WhenEnabledAndPathIsMissing_ThrowsFileNotFoundBeforeOcrInterop()
    {
        string missingPath = Path.Combine(
            Path.GetTempPath(),
            $"worktrail-ocr-missing-{Guid.NewGuid():N}.png");
        var service = new WindowsScreenshotOcrService(new OcrOptions { Enabled = true });

        FileNotFoundException exception = await Assert.ThrowsAsync<FileNotFoundException>(
            () => service.ExtractAsync(missingPath));

        Assert.Equal(Path.GetFullPath(missingPath), exception.FileName);
    }

    [Theory]
    [InlineData(" ")]
    [InlineData(" it-IT")]
    [InlineData("it-IT ")]
    public async Task ExtractAsync_WhenPreferredLanguageTagIsInvalid_ThrowsBeforePathAccess(
        string preferredLanguageTag)
    {
        var service = new WindowsScreenshotOcrService(new OcrOptions
        {
            Enabled = true,
            PreferredLanguageTag = preferredLanguageTag,
        });

        await Assert.ThrowsAsync<ArgumentException>(
            () => service.ExtractAsync("invalid\0path"));
    }

    [Fact]
    public async Task ExtractAsync_WhenEnabledAndCancelled_ThrowsBeforePathAccess()
    {
        var service = new WindowsScreenshotOcrService(new OcrOptions { Enabled = true });
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => service.ExtractAsync("invalid\0path", cancellation.Token));
    }

    [Theory]
    [InlineData(1920u, 1080u, 2560u, 1920u, 1080u)]
    [InlineData(2560u, 1440u, 2560u, 2560u, 1440u)]
    [InlineData(3840u, 2160u, 2560u, 2560u, 1440u)]
    [InlineData(2160u, 3840u, 2560u, 1440u, 2560u)]
    [InlineData(10000u, 1u, 2560u, 2560u, 1u)]
    public void CalculateRecognitionDimensions_BoundsTheLongEdgeWithoutUpscaling(
        uint sourceWidth,
        uint sourceHeight,
        uint maximumDimension,
        uint expectedWidth,
        uint expectedHeight)
    {
        (uint width, uint height) = WindowsScreenshotOcrService.CalculateRecognitionDimensions(
            sourceWidth,
            sourceHeight,
            maximumDimension);

        Assert.Equal(expectedWidth, width);
        Assert.Equal(expectedHeight, height);
    }

    [Theory]
    [InlineData(0u, 1080u, 2560u)]
    [InlineData(1920u, 0u, 2560u)]
    [InlineData(1920u, 1080u, 0u)]
    public void CalculateRecognitionDimensions_WithInvalidDimensions_Throws(
        uint sourceWidth,
        uint sourceHeight,
        uint maximumDimension)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => WindowsScreenshotOcrService.CalculateRecognitionDimensions(
                sourceWidth,
                sourceHeight,
                maximumDimension));
    }

    [Fact]
    public void ProjectRectangleToSource_RestoresSourceImageCoordinates()
    {
        OcrTextRectangle rectangle = WindowsScreenshotOcrService.ProjectRectangleToSource(
            100,
            50,
            200,
            80,
            1.5,
            1.5);

        Assert.Equal(new OcrTextRectangle(150, 75, 300, 120), rectangle);
    }
}
