// SPDX-License-Identifier: MIT
using System;
using System.Text.Json;
using WorkTrail.Services;
using Xunit;

namespace WorkTrail.Core.Tests;

public sealed class ScreenshotActivityContinuityTests
{
    private static readonly DateTimeOffset CaptureTime = new(2026, 10, 19, 9, 0, 0, TimeSpan.Zero);
    private static readonly ActivityContinuityAssessment Continuing = new(0.9, 0.05);

    [Fact]
    public void Response_PreservesMarkdownAndIndependentUncertainty()
    {
        var response = ScreenshotAnalysisResponse.Parse("""
            {"summary":"<!-- summary -->\nReading **project notes**.","activity_continuity":{"continues_confidence":0.75,"ended_confidence":0.05}}
            """);

        Assert.Equal("<!-- summary -->\nReading **project notes**.", response.Summary);
        Assert.Equal(new ActivityContinuityAssessment(0.75, 0.05), response.ActivityContinuity);
    }

    [Theory]
    [InlineData("plain Markdown")]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("{\"summary\":\"text\"}")]
    [InlineData("{\"summary\":\"\",\"activity_continuity\":{\"continues_confidence\":0,\"ended_confidence\":0}}")]
    [InlineData("{\"summary\":\"text\",\"activity_continuity\":{\"continues_confidence\":\"0.9\",\"ended_confidence\":0}}")]
    [InlineData("{\"summary\":\"text\",\"activity_continuity\":{\"continues_confidence\":0.9,\"ended_confidence\":0.8}}")]
    [InlineData("{\"summary\":\"text\",\"activity_continuity\":{\"continues_confidence\":-0.1,\"ended_confidence\":0}}")]
    [InlineData("{\"summary\":\"text\",\"activity_continuity\":{\"continues_confidence\":0,\"ended_confidence\":1.1}}")]
    [InlineData("{\"summary\":\"text\",\"activity_continuity\":{\"continues_confidence\":1e400,\"ended_confidence\":0}}")]
    [InlineData("{\"summary\":\"text\",\"activity_continuity\":{\"continues_confidence\":0,\"ended_confidence\":0,\"extra\":0}}")]
    [InlineData("{\"summary\":\"text\",\"activity_continuity\":{\"continues_confidence\":0,\"ended_confidence\":0},\"extra\":0}")]
    public void Response_RejectsMalformedOrContradictoryEstimates(string response)
    {
        Assert.ThrowsAny<JsonException>(() => ScreenshotAnalysisResponse.Parse(response));
    }

    [Theory]
    [InlineData(0, 0, true)]
    [InlineData(299_999, 599_999, true)]
    [InlineData(300_000, 599_999, false)]
    [InlineData(60_000, 600_000, false)]
    [InlineData(-1, 60_000, false)]
    public void PassiveEligibility_ExpiresAtFiveMinutesAndTenMinutesWithoutInput(int elapsedMilliseconds, uint idleMilliseconds, bool expected)
    {
        var policy = new ScreenshotActivityContinuityPolicy();
        policy.Observe(Capture(), Continuing);

        Assert.Equal(expected, policy.Allows((IntPtr)42, "reader", "Project notes", idleMilliseconds,
            CaptureTime.AddMilliseconds(elapsedMilliseconds)));
    }

    [Theory]
    [InlineData(0.79, 0.05, false)]
    [InlineData(0.8, 0.2, true)]
    [InlineData(0.1, 0.9, false)]
    [InlineData(0.1, 0.1, false)]
    [InlineData(0.9, 0.9, false)]
    public void PassiveEligibility_RequiresConfidentContinuation(double continues, double ended, bool expected)
    {
        var policy = new ScreenshotActivityContinuityPolicy();
        policy.Observe(Capture(), new ActivityContinuityAssessment(continues, ended));

        Assert.Equal(expected, Allows(policy, CaptureTime.AddSeconds(1)));
    }

    [Fact]
    public void NewEndedOrUncertainAssessment_RevokesGraceAndOlderResponsesCannotRestoreIt()
    {
        foreach (var assessment in new[] { new ActivityContinuityAssessment(0.1, 0.9), new(0.1, 0.1) })
        {
            var policy = new ScreenshotActivityContinuityPolicy();
            policy.Observe(Capture(), Continuing);
            policy.Observe(Capture() with { CapturedAt = CaptureTime.AddMinutes(1) }, assessment);
            policy.Observe(Capture(), Continuing);

            Assert.False(Allows(policy, CaptureTime.AddMinutes(2)));
        }
    }

    [Fact]
    public void RepeatedContinuation_DoesNotRestartTheWindowsInputBudget()
    {
        var policy = new ScreenshotActivityContinuityPolicy();
        policy.Observe(Capture(), Continuing);
        policy.Observe(Capture() with { CapturedAt = CaptureTime.AddMinutes(4) }, Continuing);

        Assert.True(Allows(policy, CaptureTime.AddMinutes(6), 599_999));
        Assert.False(Allows(policy, CaptureTime.AddMinutes(6), 600_000));
    }

    [Fact]
    public void PassiveEligibility_IsBoundToTheSameWindowProcessAndTitle()
    {
        var policy = new ScreenshotActivityContinuityPolicy();
        policy.Observe(Capture(), Continuing);
        var now = CaptureTime.AddSeconds(30);

        Assert.True(policy.Allows((IntPtr)42, "READER", "Project notes", 90_000, now));
        Assert.False(policy.Allows((IntPtr)43, "reader", "Project notes", 90_000, now));
        Assert.False(policy.Allows((IntPtr)42, "other", "Project notes", 90_000, now));
        Assert.False(policy.Allows((IntPtr)42, "reader", "Different document", 90_000, now));
    }

    [Fact]
    public void MissingOrExcludedActiveImageAndHistoricalProvenance_NeverGrantGrace()
    {
        var capture = Capture();
        foreach (var ineligible in new[]
        {
            capture with { ContinuityForeground = null },
            capture with { CapturedAt = null },
            capture with { Images = null },
            capture with { AnalysisScreenshotPaths = [] },
            capture with { Images = [capture.Images![0] with { Role = "secondary-window" }] },
            capture with { Images = [capture.Images![0] with { ForAnalysis = false }] },
            capture with { ContinuityForeground = capture.ContinuityForeground! with { WindowTitle = "" } }
        })
        {
            var policy = new ScreenshotActivityContinuityPolicy();
            policy.Observe(ineligible, Continuing);
            Assert.False(Allows(policy, CaptureTime.AddSeconds(30)));
        }
    }

    private static bool Allows(ScreenshotActivityContinuityPolicy policy, DateTimeOffset now, uint idleMilliseconds = 90_000)
        => policy.Allows((IntPtr)42, "reader", "Project notes", idleMilliseconds, now);

    private static ScreenshotCaptureResult Capture() => new("capture", ["active.webp"], ["active.webp"], "scheduled",
        CapturedAt: CaptureTime,
        Images: [new ScreenshotImageMetadata("active.webp", "active-window", 1, "Monitor 1", 0, 0, 1920, 1080,
            100, 100, 800, 600, true, "Reader", "Project notes", [1])])
    {
        ContinuityForeground = new ForegroundCaptureTarget((IntPtr)42, default, "reader", "Reader", "Document", "Project notes")
    };
}
