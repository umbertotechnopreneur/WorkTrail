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
using WorkTrail.Presentation;
using Xunit;

namespace WorkTrail.Presentation.Tests;

public sealed class WorldMapLightingProjectionTests
{
    [Fact]
    public void Project_MapsGeographicExtentsIntoUnitCoordinates()
    {
        Assert.Equal(new WorldMapPoint(0d, 0d), WorldMapLightingProjection.Project(90d, -180d));
        Assert.Equal(new WorldMapPoint(0.5d, 0.5d), WorldMapLightingProjection.Project(0d, 0d));
        Assert.Equal(new WorldMapPoint(1d, 1d), WorldMapLightingProjection.Project(-90d, 180d));
    }

    [Fact]
    public void Sample_DistinguishesNightDawnDayAndSunset()
    {
        Assert.Equal(WorldMapLightBand.Day, WorldMapLightingProjection.Sample(0d, 0d, 0d, 0d).Band);
        Assert.Equal(WorldMapLightBand.Night, WorldMapLightingProjection.Sample(0d, 180d, 0d, 0d).Band);
        Assert.Equal(WorldMapLightBand.Dawn, WorldMapLightingProjection.Sample(0d, -95d, 0d, 0d).Band);
        Assert.Equal(WorldMapLightBand.Sunset, WorldMapLightingProjection.Sample(0d, 95d, 0d, 0d).Band);
    }

    [Fact]
    public void Sample_BlendsBothTexturesContinuouslyAcrossTwilight()
    {
        var deepNight = WorldMapLightingProjection.Sample(0d, -120d, 0d, 0d);
        var nauticalDawn = WorldMapLightingProjection.Sample(0d, -102d, 0d, 0d);
        var civilDawn = WorldMapLightingProjection.Sample(0d, -96d, 0d, 0d);
        var horizon = WorldMapLightingProjection.Sample(0d, -90d, 0d, 0d);
        var fullDay = WorldMapLightingProjection.Sample(0d, -80d, 0d, 0d);

        Assert.Equal(0d, deepNight.DayTextureBlend);
        Assert.Equal(0d, deepNight.TwilightBlend);
        Assert.True(nauticalDawn.DayTextureBlend < civilDawn.DayTextureBlend);
        Assert.True(civilDawn.DayTextureBlend < horizon.DayTextureBlend);
        Assert.InRange(nauticalDawn.TwilightBlend, 0d, 1d);
        Assert.InRange(civilDawn.TwilightBlend, 0d, 1d);
        Assert.InRange(horizon.TwilightBlend, 0d, 1d);
        Assert.Equal(1d, fullDay.DayTextureBlend);
        Assert.Equal(0d, fullDay.TwilightBlend);
    }

    [Theory]
    [InlineData(double.NaN, 0d)]
    [InlineData(91d, 0d)]
    [InlineData(0d, 181d)]
    public void Project_RejectsInvalidCoordinates(double latitude, double longitude)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => WorldMapLightingProjection.Project(latitude, longitude));
    }
}
