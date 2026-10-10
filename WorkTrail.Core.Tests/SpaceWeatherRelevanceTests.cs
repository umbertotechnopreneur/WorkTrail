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
using WorkTrail.Services;
using Xunit;

namespace WorkTrail.Core.Tests;

public sealed class SpaceWeatherRelevanceTests
{
    private static readonly DateTimeOffset Instant = new(2026, 3, 20, 12, 0, 0, TimeSpan.Zero);

    /// <summary>Shows valid significant NOAA storms globally, regardless of the selected city's coordinates.</summary>
    [Fact]
    public void SignificantConditions_AreNotFilteredByLocation()
    {
        var alert = new SpaceWeatherAlert("synthetic", Instant.AddHours(-1), Instant.AddHours(-1), Instant.AddHours(1),
            SpaceWeatherEventKind.GeomagneticStorm, 1, 5, "Synthetic storm");
        var forecast = new SpaceWeatherKpForecast(Instant, 5, "predicted", 1);

        Assert.True(CelestialSpaceWeatherService.IsRelevantAlert(alert, Instant));
        Assert.True(CelestialSpaceWeatherService.IsRelevantForecast(forecast, Instant));
    }

    /// <summary>Continues to reject expired, invalid, or out-of-window NOAA conditions.</summary>
    [Fact]
    public void Conditions_KeepValidityAndSignificanceBounds()
    {
        var alert = new SpaceWeatherAlert("synthetic", Instant.AddHours(-1), Instant.AddHours(-1), Instant,
            SpaceWeatherEventKind.GeomagneticStorm, 1, 5, "Expired storm");
        var forecast = new SpaceWeatherKpForecast(Instant.AddHours(48), 5, "predicted", 1);

        Assert.False(CelestialSpaceWeatherService.IsRelevantAlert(alert, Instant));
        Assert.False(CelestialSpaceWeatherService.IsRelevantForecast(forecast, Instant));
    }
}
