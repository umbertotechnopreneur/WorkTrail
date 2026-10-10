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
using SkiaSharp;
using WorkTrail.Services;
using Xunit;

namespace WorkTrail.Core.Tests;

public sealed class ScreenCaptureServiceTests
{
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
