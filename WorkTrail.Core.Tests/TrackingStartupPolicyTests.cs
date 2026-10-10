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
using WorkTrail.Runtime;
using Xunit;

namespace WorkTrail.Core.Tests;

public sealed class TrackingStartupPolicyTests
{
    [Fact]
    public void PersistedStartOnLaunch_StartsWithoutCommandLineSwitch()
    {
        var options = LaunchOptions.Parse([]);
        var settings = new AppSettings(StartTrackingOnLaunch: true);

        Assert.True(TrackingStartupPolicy.ShouldStart(options, settings));
    }

    [Fact]
    public void ExplicitPausedSwitch_OverridesAutomaticAndExplicitStartRequests()
    {
        var options = LaunchOptions.Parse(["--start-tracking", "--paused"]);
        var settings = new AppSettings(StartTrackingOnLaunch: true);

        Assert.False(TrackingStartupPolicy.ShouldStart(options, settings));
    }

    [Fact]
    public void NoLaunchRequest_RemainsPaused()
    {
        var options = LaunchOptions.Parse([]);
        var settings = new AppSettings(StartTrackingOnLaunch: false);

        Assert.False(TrackingStartupPolicy.ShouldStart(options, settings));
    }

    [Fact]
    public void SafeMode_SuppressesPersistedStartOnLaunch()
    {
        var options = LaunchOptions.Parse(["--safe-mode"]);
        var settings = new AppSettings(StartTrackingOnLaunch: true);

        Assert.False(TrackingStartupPolicy.ShouldStart(options, settings));
    }
}
