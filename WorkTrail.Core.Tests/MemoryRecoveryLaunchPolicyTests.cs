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

public sealed class MemoryRecoveryLaunchPolicyTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Recovery_PreservesActualTrackingStateInsteadOfStartupPreference(bool isTracking)
    {
        var original = LaunchOptions.Parse(["--start-tracking"]);
        var arguments = MemoryRecoveryLaunchPolicy.CreateArguments(original, isTracking);
        var recovery = LaunchOptions.Parse(arguments.Split(' ', StringSplitOptions.RemoveEmptyEntries));

        Assert.True(recovery.MemoryRecovery);
        Assert.True(recovery.NoSplash);
        Assert.Equal(isTracking, recovery.StartTracking);
        Assert.Equal(!isTracking, recovery.Paused);
        Assert.Empty(recovery.RemainingArguments);
    }

    [Fact]
    public void Recovery_PreservesBackgroundModeSafetyLanguageAndHiddenStartup()
    {
        var original = LaunchOptions.Parse(["--background", "--safe-mode", "--start-with-windows", "--language", "it-IT", "ignored-command"]);
        var arguments = MemoryRecoveryLaunchPolicy.CreateArguments(original, isTracking: true);
        var recovery = LaunchOptions.Parse(arguments.Split(' ', StringSplitOptions.RemoveEmptyEntries));

        Assert.Equal(LaunchMode.Background, recovery.Mode);
        Assert.True(recovery.SafeMode);
        Assert.True(recovery.StartWithWindows);
        Assert.True(recovery.Paused);
        Assert.False(recovery.StartTracking);
        Assert.Equal("it-IT", recovery.Language);
        Assert.Empty(recovery.RemainingArguments);
    }

    [Fact]
    public void Recovery_RejectsUnvalidatedLanguageBeforeCreatingCommandLineArguments()
    {
        var original = LaunchOptions.Parse([]) with { Language = "invalid --cli" };

        Assert.Throws<ArgumentException>(() => MemoryRecoveryLaunchPolicy.CreateArguments(original, isTracking: false));
    }
}
