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
using System.Linq;
using WorkTrail.Services;
using Xunit;

namespace WorkTrail.Presentation.Tests;

public sealed class HardwareSettingsProjectionTests
{
    [Fact]
    public void SamplingSlider_UsesCanonicalOrderedProfilesAndLocalizedHalfSecondIntervals()
    {
        var strings = new LocalizationService("it-IT");
        var keys = Enumerable.Range(0, HardwareSamplingProfiles.All.Count).Select(index => HardwareSettingsProjection.ProfileKeyAt(index)).ToArray();
        Assert.Equal(new[] { "slow", "normal", "fast", "fastest" }, keys);
        Assert.Equal(3, HardwareSettingsProjection.ProfileIndex("fastest"));

        var projection = HardwareSettingsProjection.Create("fastest", strings.Culture, strings.Translate);

        Assert.Equal("Rapidissimo", projection.Label);
        Assert.Equal("Processore ogni 0,5 s · Memoria 2 s · Batteria 5 s", projection.Summary);
        Assert.Equal("Grafica ogni 0,5 s · Dischi 30 s · Rete 0,5 s", projection.Detail);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0.5)]
    [InlineData(4)]
    [InlineData(double.NaN)]
    public void SamplingSlider_RejectsInvalidPositions(double position)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => HardwareSettingsProjection.ProfileKeyAt(position));
    }

    [Fact]
    public void DisabledSnapshot_IsLocalizedWithoutExposingTheOptionalDriverName()
    {
        var strings = new LocalizationService("it-IT");
        var snapshot = new SystemSnapshot(DateTimeOffset.UtcNow, "disabled", [], "disabled");

        var result = HardwareSnapshotProjection.Create(snapshot, strings.Culture, strings.Translate);

        Assert.Equal("Sensori disattivati", result.Status);
        Assert.Equal("Sensori avanzati: Sensori disattivati", result.DriverStatus);
        Assert.DoesNotContain("PawnIO", result.DriverStatus, StringComparison.Ordinal);
        Assert.Empty(result.Details);
    }
}
