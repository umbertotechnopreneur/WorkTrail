// SPDX-License-Identifier: MIT
using System;
using System.IO;
using WorkTrail.Services;
using Xunit;

namespace WorkTrail.Core.Tests;

public sealed class ScreenshotStabilityPolicyTests
{
    private static readonly ScreenshotForegroundIdentity WindowA = new(new IntPtr(1), "Code", "Project - Code", 0, 0, 1920, 1080);
    private static readonly ScreenshotForegroundIdentity WindowB = new(new IntPtr(2), "Spotify", "Track - Spotify", 1920, 0, 3840, 1080);

    [Fact]
    public void Observe_WaitsForTheWholeSettleInterval()
    {
        var policy = new ScreenshotStabilityPolicy(new ScreenshotStabilityOptions());

        Assert.False(policy.IsExpired(50_000));
        Assert.False(policy.Observe(WindowA, 50_000));
        Assert.False(policy.Observe(WindowA, 50_999));
        Assert.True(policy.Observe(WindowA, 51_000));
    }

    [Fact]
    public void Observe_ReturningToAnEarlierWindowRestartsTheDelay()
    {
        var policy = new ScreenshotStabilityPolicy(new ScreenshotStabilityOptions());

        Assert.False(policy.Observe(WindowA, 0));
        Assert.False(policy.Observe(WindowB, 900));
        Assert.False(policy.Observe(WindowA, 950));
        Assert.False(policy.Observe(WindowA, 1949));
        Assert.True(policy.Observe(WindowA, 1950));
    }

    [Theory]
    [InlineData("handle")]
    [InlineData("process")]
    [InlineData("title")]
    [InlineData("left")]
    [InlineData("top")]
    [InlineData("right")]
    [InlineData("bottom")]
    public void Observe_EachForegroundChangeRestartsTheDelay(string field)
    {
        var policy = new ScreenshotStabilityPolicy(new ScreenshotStabilityOptions());
        var changed = field switch
        {
            "handle" => WindowA with { WindowHandle = new IntPtr(3) },
            "process" => WindowA with { ProcessName = "devenv" },
            "title" => WindowA with { WindowTitle = "Another project - Code" },
            "left" => WindowA with { Left = 1 },
            "top" => WindowA with { Top = 1 },
            "right" => WindowA with { Right = 1919 },
            "bottom" => WindowA with { Bottom = 1079 },
            _ => throw new ArgumentOutOfRangeException(nameof(field))
        };

        Assert.False(policy.Observe(WindowA, 0));
        Assert.False(policy.Observe(changed, 900));
        Assert.False(policy.Observe(changed, 1899));
        Assert.True(policy.Observe(changed, 1900));
    }

    [Fact]
    public void Observe_RepeatedSwitchingCannotExtendTheAttemptBudget()
    {
        var policy = new ScreenshotStabilityPolicy(new ScreenshotStabilityOptions());

        Assert.False(policy.Observe(WindowA, 10_000));
        Assert.False(policy.Observe(WindowB, 10_900));
        Assert.False(policy.Observe(WindowA, 11_800));
        Assert.False(policy.Observe(WindowB, 12_700));
        Assert.False(policy.IsExpired(12_999));
        Assert.False(policy.Observe(WindowB, 13_000));
        Assert.True(policy.IsExpired(13_000));
        Assert.False(policy.Observe(WindowB, 13_700));
    }

    [Fact]
    public void Observe_AllowsStabilityReachedExactlyAtTheDeadline()
    {
        var policy = new ScreenshotStabilityPolicy(new ScreenshotStabilityOptions());

        Assert.False(policy.Observe(WindowA, 0));
        Assert.False(policy.Observe(WindowB, 2000));
        Assert.True(policy.Observe(WindowB, 3000));
    }

    [Fact]
    public void Observe_NewAttemptDoesNotInheritEarlierWindowStability()
    {
        var firstAttempt = new ScreenshotStabilityPolicy(new ScreenshotStabilityOptions());
        Assert.False(firstAttempt.Observe(WindowA, 0));
        Assert.True(firstAttempt.Observe(WindowA, 1000));

        var nextAttempt = new ScreenshotStabilityPolicy(new ScreenshotStabilityOptions());
        Assert.False(nextAttempt.Observe(WindowA, 2000));
        Assert.True(nextAttempt.Observe(WindowA, 3000));
    }

    [Fact]
    public void DisabledPolicy_DoesNotDelayOrExpire()
    {
        var policy = new ScreenshotStabilityPolicy(new ScreenshotStabilityOptions(Enabled: false));

        Assert.True(policy.Observe(WindowA, 0));
        Assert.True(policy.Observe(WindowB, 1));
        Assert.False(policy.IsExpired(100_000));
    }

    [Fact]
    public void Observe_EmptyTitlesAreValidAndStillDetectWindowChanges()
    {
        var policy = new ScreenshotStabilityPolicy(new ScreenshotStabilityOptions());
        var titleRedactedA = WindowA with { WindowTitle = string.Empty };
        var titleRedactedB = WindowB with { WindowTitle = string.Empty };

        Assert.False(policy.Observe(titleRedactedA, 0));
        Assert.False(policy.Observe(titleRedactedB, 900));
        Assert.True(policy.Observe(titleRedactedB, 1900));
    }

    [Fact]
    public void Observe_RejectsBackwardOrNegativeTime()
    {
        var policy = new ScreenshotStabilityPolicy(new ScreenshotStabilityOptions());

        Assert.Throws<ArgumentOutOfRangeException>(() => policy.Observe(WindowA, -1));
        Assert.False(policy.Observe(WindowA, 1000));
        Assert.Throws<ArgumentOutOfRangeException>(() => policy.Observe(WindowA, 999));
        Assert.Throws<ArgumentOutOfRangeException>(() => policy.IsExpired(999));
    }

    [Theory]
    [InlineData(99, 3000, 100, 3)]
    [InlineData(5001, 6000, 100, 3)]
    [InlineData(1000, 999, 100, 3)]
    [InlineData(1000, 10001, 100, 3)]
    [InlineData(1000, 3000, 24, 3)]
    [InlineData(1000, 3000, 1001, 3)]
    [InlineData(100, 3000, 200, 3)]
    [InlineData(1000, 3000, 100, 0)]
    [InlineData(1000, 3000, 100, 61)]
    public void Options_RejectUnboundedOrInconsistentTiming(int settle, int maximumWait, int poll, int retry)
    {
        var options = new ScreenshotStabilityOptions(
            SettleMilliseconds: settle,
            MaximumWaitMilliseconds: maximumWait,
            PollMilliseconds: poll,
            RetrySeconds: retry);

        Assert.Throws<InvalidDataException>(() => options.Validate());
    }

    [Fact]
    public void Options_BoundRetriesAndValidateEmbeddedPolicy()
    {
        Assert.Throws<InvalidDataException>(() => new ScreenshotStabilityOptions(MaxRetries: -1).Validate());
        Assert.Throws<InvalidDataException>(() => new ScreenshotStabilityOptions(MaxRetries: 6).Validate());
        new ScreenshotStabilityOptions(MaxRetries: 0).Validate();
        var rules = ScreenshotCaptureRules.Load();
        Assert.Equal(2, rules.Stability?.MaxRetries);
        Assert.Equal(1000, rules.Stability?.SettleMilliseconds);
        Assert.Throws<InvalidDataException>(() => (rules with
        {
            Stability = new ScreenshotStabilityOptions(MaximumWaitMilliseconds: 50)
        }).Validate());
    }
}
