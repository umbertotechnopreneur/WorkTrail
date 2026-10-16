// SPDX-License-Identifier: MIT
using System;
using WorkTrail.Services;
using Xunit;

namespace WorkTrail.Core.Tests;

public sealed class ScreenshotIdlePolicyTests
{
    [Theory]
    [InlineData(0, 60, true)]
    [InlineData(59_999, 60, true)]
    [InlineData(60_000, 60, false)]
    [InlineData(599_999, 600, true)]
    [InlineData(600_000, 600, false)]
    [InlineData(899_999, 900, true)]
    [InlineData(900_000, 900, false)]
    public void IdleTimeout_UsesTotalTimeSinceInputAndStopsAtBoundary(int elapsedMilliseconds, int timeoutSeconds, bool expected)
    {
        Assert.Equal(expected, ScreenshotIdlePolicy.IsWithinIdleTimeout((uint)elapsedMilliseconds, 0, timeoutSeconds));
    }

    [Fact]
    public void IdleTimeout_HandlesWin32TickCountRollover()
    {
        Assert.True(ScreenshotIdlePolicy.IsWithinIdleTimeout(999, uint.MaxValue - 58_999, 60));
        Assert.False(ScreenshotIdlePolicy.IsWithinIdleTimeout(1_000, uint.MaxValue - 58_999, 60));
    }

    [Fact]
    public void IdleTimeout_DoesNotRenewBudgetOnCaptureOrAnalysis()
    {
        const uint inputAt = 10_000;
        Assert.True(ScreenshotIdlePolicy.IsWithinIdleTimeout(310_000, inputAt, 600));
        Assert.True(ScreenshotIdlePolicy.IsWithinIdleTimeout(609_999, inputAt, 600));
        Assert.False(ScreenshotIdlePolicy.IsWithinIdleTimeout(610_000, inputAt, 600));
        Assert.False(ScreenshotIdlePolicy.IsWithinIdleTimeout(910_000, inputAt, 600));
    }

    [Fact]
    public void IdleTimeout_FailsClosedForAnInputTickAheadOfTheCurrentClock()
    {
        Assert.False(ScreenshotIdlePolicy.IsWithinIdleTimeout(1_000, 1_001, 60));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void IdleTimeout_RejectsInvalidBudget(int timeoutSeconds)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ScreenshotIdlePolicy.IsWithinIdleTimeout(0, 0, timeoutSeconds));
    }
}
