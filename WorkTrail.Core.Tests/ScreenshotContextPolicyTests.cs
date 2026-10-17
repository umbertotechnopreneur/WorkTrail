// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using WorkTrail.Services;
using Xunit;

namespace WorkTrail.Core.Tests;

public sealed class ScreenshotContextPolicyTests
{
    [Theory]
    [InlineData("ms-teams", "Meeting", 600)]
    [InlineData("TEAMS", "Chat", 600)]
    [InlineData("zoom", "Shared content", 900)]
    [InlineData("Code", "Project", 60)]
    [InlineData("unknown-app", "Meeting", 60)]
    public void IdleTimeout_UsesConfiguredTotalSeconds(string processName, string title, int seconds)
    {
        Assert.Equal(seconds, ScreenshotCaptureRules.Load().GetIdleTimeoutSeconds(processName, title));
    }

    [Fact]
    public void ContextPriority_HighestWinsAndEqualPriorityUsesStableIdentifier()
    {
        ScreenshotContextRule[] contexts =
        [
            new("z-match", true, 100, ["editor"], ["Project"], ActiveAttention: "low", IdleTimeoutMinutes: 3),
            new("low-priority", true, 10, ["editor"], ["Project"], ActiveAttention: "normal", IdleTimeoutMinutes: 1),
            new("a-match", true, 100, ["editor"], ["Project"], ActiveAttention: "high", IdleTimeoutMinutes: 2),
            new("disabled", false, 999, ["editor"], ["Project"], ActiveAttention: "low", IdleTimeoutMinutes: 4)
        ];
        var policy = CreatePolicy(contexts);
        var resolved = policy.ResolveWindowPolicy("EDITOR", "Project details");
        var reordered = (policy with { ContextRules = contexts.Reverse().ToArray() }).ResolveWindowPolicy("EDITOR", "Project details");

        Assert.Equal("title-hint:a-match", resolved.Reason);
        Assert.Equal("high", resolved.ActiveAttention);
        Assert.Equal(120, resolved.IdleTimeoutSeconds);
        Assert.Equal(resolved.Reason, reordered.Reason);
        Assert.Equal(resolved.ActiveAttention, reordered.ActiveAttention);
        Assert.Equal(resolved.IdleTimeoutSeconds, reordered.IdleTimeoutSeconds);
    }

    [Fact]
    public void ContextTitle_IsCaseInsensitiveAndRestrictedToItsProcess()
    {
        var policy = CreatePolicy([new("debug", true, 100, ["editor"], ["Debug Console"],
            BackgroundAttention: "high", CaptureSecondary: true)]);
        var matched = policy.ResolveWindowPolicy("EDITOR", "Project - debug console");
        var unrelated = policy.ResolveWindowPolicy("browser", "Project - Debug Console");

        Assert.Equal("title-hint:debug", matched.Reason);
        Assert.True(matched.CaptureSecondary);
        Assert.Equal("high", matched.BackgroundAttention);
        Assert.Equal("default", unrelated.Reason);
        Assert.False(unrelated.CaptureSecondary);
        Assert.Equal("normal", unrelated.BackgroundAttention);
    }

    [Fact]
    public void EmptyTitle_UsesApplicationProfileWithoutMatchingHints()
    {
        var policy = ScreenshotCaptureRules.Load();
        var teams = policy.ResolveWindowPolicy("ms-teams", string.Empty);
        var browser = policy.ResolveWindowPolicy("chrome", string.Empty);

        Assert.Equal("app-profile:teams", teams.Reason);
        Assert.Equal(600, teams.IdleTimeoutSeconds);
        Assert.Equal("normal", teams.BackgroundAttention);
        Assert.Equal(new[] { "collaboration" }, teams.MicroPromptIds);
        Assert.Equal("app-profile:chrome", browser.Reason);
        Assert.False(browser.CaptureSecondary);
    }

    [Fact]
    public void ContextOverridesOnlySpecifiedFieldsAndSelectsItsMicroPrompts()
    {
        var policy = ScreenshotCaptureRules.Load();
        var matched = policy.ResolveWindowPolicy("Code", "Project - Debug Console");
        var plain = policy.ResolveWindowPolicy("Code", "Project");

        Assert.Equal("high", matched.ActiveAttention);
        Assert.Equal("high", matched.BackgroundAttention);
        Assert.Equal(new[] { "code", "debugger" }, matched.MicroPromptIds);
        Assert.Equal(new[] { "code" }, plain.MicroPromptIds);
        Assert.Equal(60, matched.IdleTimeoutSeconds);
    }

    [Theory]
    [InlineData("chrome", "Change request - GitHub", true, "title-hint:web-code-review")]
    [InlineData("chrome", "Unclassified page", false, "app-profile:chrome")]
    [InlineData("unknown-browser", "Change request - GitHub", false, "default")]
    [InlineData("msedge", "Budget - Google Sheets", true, "title-hint:web-documents")]
    [InlineData("firefox", "The New York Times", false, "title-hint:web-news")]
    public void BrowserSecondarySelection_UsesScopedTitleHintsNotEveryBrowserWindow(
        string processName, string title, bool included, string reason)
    {
        var policy = ScreenshotCaptureRules.Load();
        var resolved = policy.ResolveWindowPolicy(processName, title);
        var secondary = PlanSecondary(policy, processName, title, 50).Where(target => target.Role == "secondary-window").ToArray();

        Assert.Equal(reason, resolved.Reason);
        Assert.Equal(included, resolved.CaptureSecondary);
        Assert.Equal(included ? 1 : 0, secondary.Length);
        if (included) Assert.Equal(reason, secondary[0].RuleReason);
    }

    [Theory]
    [InlineData("java", "Oracle SQL Developer - Worksheet", true)]
    [InlineData("JAVAW", "oracle sql developer", true)]
    [InlineData("java", "Unrelated desktop application", false)]
    [InlineData("javaw", "", false)]
    [InlineData("unrelated-app", "Oracle SQL Developer", false)]
    public void JavaOracleHint_DoesNotPromoteAllJavaWindows(string processName, string title, bool included)
    {
        var policy = ScreenshotCaptureRules.Load();
        var resolved = policy.ResolveWindowPolicy(processName, title);
        Assert.Equal(included, resolved.CaptureSecondary);
        Assert.Equal(included ? "high" : "normal", resolved.ActiveAttention);
        Assert.Equal(included ? "title-hint:java-sql-developer" : "default", resolved.Reason);
        Assert.Equal(included ? 1 : 0, PlanSecondary(policy, processName, title, 50).Count(target => target.Role == "secondary-window"));
    }

    [Theory]
    [InlineData(84, true)]
    [InlineData(85, true)]
    [InlineData(86, false)]
    [InlineData(100, false)]
    public void SecondarySelection_RequiresAtLeastFifteenPercentVisible(int foregroundWidth, bool included)
    {
        var secondary = PlanSecondary(ScreenshotCaptureRules.Load(), "Code", "Project", foregroundWidth)
            .Where(target => target.Role == "secondary-window").ToArray();

        Assert.Equal(included ? 1 : 0, secondary.Length);
        if (!included) return;
        var target = Assert.Single(secondary);
        Assert.Equal(foregroundWidth, target.Bounds.Left);
        Assert.All(target.VisibleRegions, region => Assert.True(region.Left >= foregroundWidth));
        Assert.Equal((100 - foregroundWidth) * 100, target.VisibleRegions.Sum(region =>
            (region.Right - region.Left) * (region.Bottom - region.Top)));
    }

    [Theory]
    [InlineData("explorer")]
    [InlineData("SystemSettings")]
    public void LowAttentionApplication_RemainsEligibleWhenActive(string processName)
    {
        var bounds = Bounds(100);
        var policy = ScreenshotCaptureRules.Load();
        var foreground = new ForegroundCaptureTarget((IntPtr)1, bounds, processName, "Application", "Context", "Window");
        var target = Assert.Single(policy.Plan([new CaptureDisplay(1, bounds)], foreground,
            [new CaptureWindow((IntPtr)1, bounds, processName, "Application", "Window")], true));

        Assert.False(policy.ResolveWindowPolicy(processName, "Window").CaptureSecondary);
        Assert.Equal("active-window", target.Role);
        Assert.Equal("low", target.Attention);
        Assert.True(target.ForAnalysis);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(7201)]
    public void Validate_RejectsOutOfRangeDefaultIdleTimeout(int seconds)
    {
        Assert.Throws<InvalidDataException>((CreatePolicy() with { DefaultIdleTimeoutSeconds = seconds }).Validate);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(121)]
    public void Validate_RejectsOutOfRangeApplicationAndContextTimeouts(int minutes)
    {
        var application = CreatePolicy() with
        {
            ApplicationProfiles = [new("editor", "Editor", ["editor"], "high", "normal", IdleTimeoutMinutes: minutes)]
        };
        var context = CreatePolicy([new("editor", true, 1, ["editor"], ["Project"], IdleTimeoutMinutes: minutes)]);

        Assert.Throws<InvalidDataException>(application.Validate);
        Assert.Throws<InvalidDataException>(context.Validate);
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(7200, 120)]
    public void Validate_AcceptsInclusiveIdleTimeoutBounds(int seconds, int minutes)
    {
        var policy = CreatePolicy([new("context", true, 1, ["editor"], ["Project"], IdleTimeoutMinutes: minutes)]) with
        {
            DefaultIdleTimeoutSeconds = seconds,
            ApplicationProfiles = [new("editor", "Editor", ["editor"], "high", "normal", IdleTimeoutMinutes: minutes)]
        };

        policy.Validate();
        Assert.Equal(minutes * 60, policy.GetIdleTimeoutSeconds("editor", "Project"));
        Assert.Equal(seconds, policy.GetIdleTimeoutSeconds("unknown", string.Empty));
    }

    [Theory]
    [InlineData(-0.01)]
    [InlineData(1.01)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void Validate_RejectsInvalidVisibleFraction(double fraction)
    {
        var policy = new ScreenshotCaptureRules([new("secondary", true, "secondary-windows", "window", "secondary", "both",
            RequireSecondaryMatch: true, MinimumVisibleFraction: fraction)]);

        Assert.Throws<InvalidDataException>(policy.Validate);
    }

    [Fact]
    public void Validate_RejectsSecondaryRequirementOnActiveRule()
    {
        var policy = new ScreenshotCaptureRules([new("active", true, "active-window", "window", "primary", "both",
            RequireSecondaryMatch: true)]);
        Assert.Throws<InvalidDataException>(policy.Validate);
    }

    [Fact]
    public void Validate_RejectsNullEmptyAndBlankContextHints()
    {
        IReadOnlyList<string>?[] hints = [null, [], [null!], [""], [" "]];
        foreach (var invalidHints in hints)
        {
            var policy = CreatePolicy([new("invalid", true, 1, ["editor"], invalidHints!)]);
            Assert.Throws<InvalidDataException>(policy.Validate);
        }
    }

    [Fact]
    public void Validate_RejectsContextWithoutProcessScope()
    {
        IReadOnlyList<string>?[] processes = [null, [], [null!], [" "]];
        foreach (var invalidProcesses in processes)
        {
            var policy = CreatePolicy([new("invalid", true, 1, invalidProcesses!, ["Project"])]);
            Assert.Throws<InvalidDataException>(policy.Validate);
        }
    }

    [Fact]
    public void Validate_RejectsUnknownMicroPromptsInProfilesAndContexts()
    {
        var application = CreatePolicy() with
        {
            ApplicationProfiles = [new("editor", "Editor", ["editor"], "high", "normal", MicroPromptIds: ["missing"])]
        };
        var context = CreatePolicy([new("editor", true, 1, ["editor"], ["Project"], MicroPromptIds: ["missing"])]);

        Assert.Throws<InvalidDataException>(application.Validate);
        Assert.Throws<InvalidDataException>(context.Validate);
    }

    private static ScreenshotCaptureRules CreatePolicy(IReadOnlyList<ScreenshotContextRule>? contexts = null) =>
        new([new("active", true, "active-window", "window", "primary", "both")], ContextRules: contexts);

    private static IReadOnlyList<CaptureImageTarget> PlanSecondary(ScreenshotCaptureRules policy, string processName,
        string title, int foregroundWidth)
    {
        var full = Bounds(100);
        var front = Bounds(foregroundWidth);
        var foreground = new ForegroundCaptureTarget((IntPtr)1, front, "foreground-app", "Foreground", "Context", "Foreground");
        return policy.Plan([new CaptureDisplay(1, full)], foreground,
            [new CaptureWindow((IntPtr)1, front, "foreground-app", "Foreground", "Foreground"),
             new CaptureWindow((IntPtr)2, full, processName, processName, title)], false);
    }

    private static NativeMethods.Rect Bounds(int right) => new() { Left = 0, Top = 0, Right = right, Bottom = 100 };
}
