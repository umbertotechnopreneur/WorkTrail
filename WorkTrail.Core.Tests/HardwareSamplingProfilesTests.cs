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
using System.Collections.Generic;
using System.Linq;
using WorkTrail.Services;
using Xunit;

namespace WorkTrail.Core.Tests;

public sealed class HardwareSamplingProfilesTests
{
    [Theory]
    [InlineData("slow", 10, 30, 60, 120, 10)]
    [InlineData("normal", 2, 10, 30, 60, 2)]
    [InlineData("fast", 1, 5, 10, 30, 1)]
    [InlineData("fastest", 0.5, 2, 5, 30, 0.5)]
    public void Profiles_ApplyDistinctCategoryIntervals(string key, double cpu, double memory, double battery, double storage, double network)
    {
        var profile = HardwareSamplingProfiles.Get(key);
        foreach (var kind in new[] { "Cpu", "GpuNvidia", "GpuAmd", "GpuIntel" })
            Assert.Equal(TimeSpan.FromSeconds(cpu), profile.GetInterval(kind));
        Assert.Equal(TimeSpan.FromSeconds(memory), profile.GetInterval("Memory"));
        Assert.Equal(TimeSpan.FromSeconds(battery), profile.GetInterval("Battery"));
        Assert.Equal(TimeSpan.FromSeconds(storage), profile.GetInterval("Storage"));
        Assert.Equal(TimeSpan.FromSeconds(network), profile.GetInterval("Network"));
    }

    [Fact]
    public void Catalog_IsOrderedAndImmutable()
    {
        Assert.Equal(new[] { "slow", "normal", "fast", "fastest" }, HardwareSamplingProfiles.All.Select(profile => profile.Key));
        Assert.Throws<NotSupportedException>(() => ((IList<HardwareSamplingProfile>)HardwareSamplingProfiles.All).Clear());
    }

    [Theory]
    [InlineData("")]
    [InlineData("NORMAL")]
    [InlineData("unknown")]
    public void UnknownProfile_IsRejected(string key) => Assert.Throws<ArgumentException>(() => HardwareSamplingProfiles.Get(key));

    [Fact]
    public void UnknownCategory_IsRejected() => Assert.Throws<ArgumentException>(() => HardwareSamplingProfiles.Get("normal").GetInterval("Motherboard"));

    [Theory]
    [InlineData("Cpu")]
    [InlineData("GpuIntel")]
    [InlineData("Memory")]
    [InlineData("Battery")]
    [InlineData("Storage")]
    [InlineData("Network")]
    public void CachedDevice_BecomesDueAtExactCategoryBoundary(string kind)
    {
        var profile = HardwareSamplingProfiles.Get("normal");
        var sampledAt = new DateTimeOffset(2026, 9, 17, 0, 0, 0, TimeSpan.Zero);
        var due = sampledAt + profile.GetInterval(kind);
        Assert.False(profile.IsSampleDue(kind, sampledAt, due - TimeSpan.FromTicks(1)));
        Assert.True(profile.IsSampleDue(kind, sampledAt, due));
    }

    [Fact]
    public void ProfileChange_ReevaluatesCachedDeviceAgainstNewInterval()
    {
        var sampledAt = new DateTimeOffset(2026, 9, 17, 0, 0, 0, TimeSpan.Zero);
        var now = sampledAt + TimeSpan.FromSeconds(3);
        Assert.False(HardwareSamplingProfiles.Get("normal").IsSampleDue("Memory", sampledAt, now));
        Assert.True(HardwareSamplingProfiles.Get("fastest").IsSampleDue("Memory", sampledAt, now));
        Assert.False(HardwareSamplingProfiles.Get("fastest").IsSampleDue("Battery", sampledAt, now));
    }
}
