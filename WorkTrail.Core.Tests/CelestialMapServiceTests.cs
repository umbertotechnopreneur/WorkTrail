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
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SkiaSharp;
using WorkTrail.Application;
using WorkTrail.Services;
using Xunit;

namespace WorkTrail.Core.Tests;

public sealed class CelestialMapServiceTests
{
    private static readonly DateTimeOffset Equinox = new(2026, 3, 20, 12, 0, 0, TimeSpan.Zero);

    /// <summary>Checks current solar illumination in both projections against the common astronomy engine.</summary>
    [Theory]
    [InlineData(CelestialMapProjection.Flat)]
    [InlineData(CelestialMapProjection.Globe)]
    public async Task Render_CentersOnSubsolarAndAntisolarPointsWithCorrectDayNight(CelestialMapProjection projection)
    {
        var service = SolidTextures();
        var sun = LocalAstronomy.CalculateGlobal(Equinox);
        var lit = await service.RenderAsync(new CelestialMapRequest(Equinox, projection, 129, 129,
            sun.SunLatitude, sun.SunLongitude));
        var oppositeLongitude = sun.SunLongitude > 0 ? sun.SunLongitude - 180 : sun.SunLongitude + 180;
        var dark = await service.RenderAsync(new CelestialMapRequest(Equinox, projection, 129, 129,
            -sun.SunLatitude, oppositeLongitude));
        using var litBitmap = SKBitmap.Decode(lit.PngBytes);
        using var darkBitmap = SKBitmap.Decode(dark.PngBytes);

        Assert.Equal(sun.SunLatitude, lit.SunLatitude);
        Assert.Equal(sun.SunLongitude, lit.SunLongitude);
        Assert.True(litBitmap.GetPixel(64, 64).Red > 240);
        Assert.Equal(0, litBitmap.GetPixel(64, 64).Blue);
        Assert.Equal(0, darkBitmap.GetPixel(64, 64).Red);
        Assert.True(darkBitmap.GetPixel(64, 64).Blue > 240);
    }

    /// <summary>Verifies that the globe exposes acrylic outside its atmosphere while flat mode covers every corner.</summary>
    [Fact]
    public async Task Render_GlobeHasTransparentCornersAndFlatDoesNot()
    {
        var service = SolidTextures();
        var globe = await service.RenderAsync(new CelestialMapRequest(Equinox, CelestialMapProjection.Globe, 128, 96));
        var flat = await service.RenderAsync(new CelestialMapRequest(Equinox, CelestialMapProjection.Flat, 128, 96));
        using var globeBitmap = SKBitmap.Decode(globe.PngBytes);
        using var flatBitmap = SKBitmap.Decode(flat.PngBytes);

        Assert.Equal(128, globeBitmap.Width);
        Assert.Equal(96, globeBitmap.Height);
        Assert.Equal(0, globeBitmap.GetPixel(0, 0).Alpha);
        Assert.Equal(255, globeBitmap.GetPixel(64, 48).Alpha);
        Assert.Equal(255, flatBitmap.GetPixel(0, 0).Alpha);
    }

    /// <summary>Checks geographic centering at the dateline without swapping east and west or exposing a seam.</summary>
    [Theory]
    [InlineData(CelestialMapProjection.Flat)]
    [InlineData(CelestialMapProjection.Globe)]
    public async Task Render_DatelineCentersSampleMatchingGeography(CelestialMapProjection projection)
    {
        var pixels = Enumerable.Range(0, 32).Select(index => index % 8 is 0 or 7 ? SKColors.Red : SKColors.Blue).ToArray();
        var service = new CelestialMapService(8, 4, pixels, pixels);
        var east = await service.RenderAsync(new CelestialMapRequest(Equinox, projection, 129, 129, 0, 180));
        var west = await service.RenderAsync(new CelestialMapRequest(Equinox, projection, 129, 129, 0, -180));
        using var eastBitmap = SKBitmap.Decode(east.PngBytes);
        using var westBitmap = SKBitmap.Decode(west.PngBytes);

        Assert.Equal(eastBitmap.GetPixel(64, 64), westBitmap.GetPixel(64, 64));
        Assert.True(eastBitmap.GetPixel(64, 64).Red > 150);
        Assert.Equal(0, eastBitmap.GetPixel(64, 64).Blue);
    }

    /// <summary>Ensures malformed input is rejected before nonexistent packaged assets can be read.</summary>
    [Fact]
    public void Render_RejectsUnsupportedInputBeforeAssetRead()
    {
        var service = new CelestialMapService("missing-day.png", "missing-night.png");
        Assert.Throws<ArgumentOutOfRangeException>(() =>
        {
            _ = service.RenderAsync(new CelestialMapRequest(Equinox, (CelestialMapProjection)99));
        });
        Assert.Throws<ArgumentOutOfRangeException>(() =>
        {
            _ = service.RenderAsync(new CelestialMapRequest(Equinox, CelestialMapProjection.Globe, CenterLatitude: double.NaN));
        });
        Assert.Throws<ArgumentOutOfRangeException>(() =>
        {
            _ = service.RenderAsync(new CelestialMapRequest(Equinox, CelestialMapProjection.Flat, PixelWidth: 2049));
        });
        Assert.Throws<ArgumentOutOfRangeException>(() =>
        {
            _ = service.RenderAsync(new CelestialMapRequest(Equinox.AddYears(100), CelestialMapProjection.Globe));
        });
    }

    /// <summary>Missing assets are surfaced and cancellation does not start a texture read.</summary>
    [Fact]
    public async Task Render_FailsExplicitlyForMissingAssetsAndHonorsCancellation()
    {
        var service = new CelestialMapService("missing-celestial-day.png", "missing-celestial-night.png");
        var request = new CelestialMapRequest(Equinox, CelestialMapProjection.Globe, 64, 64);
        await Assert.ThrowsAsync<FileNotFoundException>(() => service.RenderAsync(request));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.RenderAsync(request, new CancellationToken(true)));
    }

    private static CelestialMapService SolidTextures() => new(8, 4,
        Enumerable.Repeat(SKColors.Red, 32).ToArray(), Enumerable.Repeat(SKColors.Blue, 32).ToArray());
}
