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

public sealed class LunarPhaseProjectionTests
{
    /// <summary>Verifies the localization key and illumination projected for each principal lunar phase.</summary>
    [Theory]
    [InlineData(0d, "WorldClock.MoonPhase.New", 0d)]
    [InlineData(45d, "WorldClock.MoonPhase.WaxingCrescent", 15d)]
    [InlineData(90d, "WorldClock.MoonPhase.FirstQuarter", 50d)]
    [InlineData(135d, "WorldClock.MoonPhase.WaxingGibbous", 85d)]
    [InlineData(180d, "WorldClock.MoonPhase.Full", 100d)]
    [InlineData(225d, "WorldClock.MoonPhase.WaningGibbous", 85d)]
    [InlineData(270d, "WorldClock.MoonPhase.LastQuarter", 50d)]
    [InlineData(315d, "WorldClock.MoonPhase.WaningCrescent", 15d)]
    [InlineData(360d, "WorldClock.MoonPhase.New", 0d)]
    public void Create_ProjectsEightPhasesAndIllumination(
        double angle,
        string expectedKey,
        double expectedPercentage)
    {
        var presentation = LunarPhaseProjection.Create(angle);

        Assert.Equal(expectedKey, presentation.LocalizationKey);
        Assert.Equal(expectedPercentage, presentation.IlluminatedPercentage);
        Assert.False(string.IsNullOrWhiteSpace(presentation.Glyph));
    }

    /// <summary>Verifies that lunar phase projection rejects non-finite phase angles.</summary>
    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void Create_RejectsNonFiniteAngles(double angle)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => LunarPhaseProjection.Create(angle));
    }
}
