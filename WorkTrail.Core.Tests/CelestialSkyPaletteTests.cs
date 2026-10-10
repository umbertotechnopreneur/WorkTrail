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
using WorkTrail.Application;
using Xunit;

namespace WorkTrail.Core.Tests;

public sealed class CelestialSkyPaletteTests
{
    /// <summary>Night, twilight and daylight remain distinct even at locations with unusual civil-time sunrise.</summary>
    [Fact]
    public void SolarElevation_DrivesSkyInsteadOfCivilHour()
    {
        Assert.Equal(24, CelestialSkyPalette.PaletteCount);
        var night = CelestialSkyPalette.Create(-25);
        var twilight = CelestialSkyPalette.Create(-4);
        var day = CelestialSkyPalette.Create(45);
        Assert.Equal(1, night.StarOpacity);
        Assert.InRange(twilight.StarOpacity, 0.01, 0.5);
        Assert.Equal(0, day.StarOpacity);
        Assert.True(twilight.SunGlowOpacity > day.SunGlowOpacity);
        Assert.NotEqual(night.HorizonColor, twilight.HorizonColor);
        Assert.NotEqual(twilight.ZenithColor, day.ZenithColor);
    }

    /// <summary>Palette boundaries do not jump as the solar altitude crosses a keyframe.</summary>
    [Theory]
    [InlineData(-18)]
    [InlineData(-6)]
    [InlineData(0)]
    [InlineData(25)]
    public void KeyframeBoundaries_AreContinuous(double altitude)
    {
        var before = CelestialSkyPalette.Create(altitude - 0.000001);
        var after = CelestialSkyPalette.Create(altitude + 0.000001);
        Assert.Equal(before.ZenithColor, after.ZenithColor);
        Assert.Equal(before.HorizonColor, after.HorizonColor);
        Assert.InRange(Math.Abs(before.StarOpacity - after.StarOpacity), 0, 0.00001);
    }

    /// <summary>Both physical poles of the elevation range work, and invalid inputs are rejected.</summary>
    [Fact]
    public void ElevationRange_IsBounded()
    {
        Assert.Equal(1, CelestialSkyPalette.Create(-90).StarOpacity);
        Assert.Equal(0, CelestialSkyPalette.Create(90).StarOpacity);
        Assert.Throws<ArgumentOutOfRangeException>(() => CelestialSkyPalette.Create(double.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() => CelestialSkyPalette.Create(91));
    }
}
