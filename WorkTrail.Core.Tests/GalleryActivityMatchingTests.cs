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
using System.Threading;
using WorkTrail.Application;
using WorkTrail.Services;
using Xunit;

namespace WorkTrail.Core.Tests;

public sealed class GalleryActivityMatchingTests
{
    [Fact]
    public void LinearMatcher_EqualsReferenceAcrossRandomizedIntervalsAndInstallations()
    {
        var random = new Random(0x51A7);
        var origin = new DateTimeOffset(2026, 3, 29, 0, 0, 0, TimeSpan.Zero);
        for (var iteration = 0; iteration < 40; iteration++)
        {
            var samples = Enumerable.Range(0, 500)
                .Select(index => CreateSample(
                    origin.AddSeconds(random.Next(0, 86_400)),
                    random.Next(0, 181),
                    index % 3 == 0 ? "installation-b" : "installation-a",
                    index))
                .OrderBy(sample => sample.Timestamp)
                .ToArray();
            var intervals = Enumerable.Range(0, 180)
                .Select(index =>
                {
                    var from = origin.AddSeconds(random.Next(0, 85_800));
                    return new ScreenshotActivityInterval(
                        index % 4 == 0 ? "installation-b" : "installation-a",
                        from,
                        from.AddSeconds(random.Next(1, 901)));
                })
                .ToArray();

            var actual = LocalStore.MatchActivitySamples(intervals, samples, CancellationToken.None);

            Assert.Equal(intervals.Length, actual.Count);
            for (var index = 0; index < intervals.Length; index++)
            {
                var interval = intervals[index];
                var expected = samples.Where(sample =>
                    string.Equals(sample.InstallationId, interval.InstallationId, StringComparison.Ordinal)
                    && sample.Timestamp.ToUniversalTime() > interval.FromUtc
                    && sample.Timestamp.ToUniversalTime().AddSeconds(-sample.DurationSeconds) < interval.ToUtc);
                Assert.Equal(expected, actual[index]);
            }
        }
    }

    [Fact]
    public void LinearMatcher_PreservesOpenBoundariesDuplicatesAndUtcNormalization()
    {
        var from = new DateTimeOffset(2026, 10, 25, 1, 0, 0, TimeSpan.Zero);
        var to = from.AddMinutes(15);
        var intervals = new[]
        {
            new ScreenshotActivityInterval("installation-a", from, to),
            new ScreenshotActivityInterval("installation-a", from, to),
            new ScreenshotActivityInterval("installation-b", from, to)
        };
        var samples = new[]
        {
            CreateSample(from, 60, "installation-a", 1),
            CreateSample(from.AddMinutes(1), 60, "installation-a", 2),
            CreateSample(to, 60, "installation-a", 3),
            CreateSample(to.AddMinutes(1), 60, "installation-a", 4),
            CreateSample(to.ToOffset(TimeSpan.FromHours(2)), 60, "installation-b", 5)
        };

        var actual = LocalStore.MatchActivitySamples(intervals, samples, CancellationToken.None);

        Assert.Equal(new[] { samples[1], samples[2] }, actual[0]);
        Assert.Equal(actual[0], actual[1]);
        Assert.Equal(new[] { samples[4] }, actual[2]);
    }

    private static ActivitySample CreateSample(
        DateTimeOffset timestamp,
        int durationSeconds,
        string installationId,
        int ordinal) => new(
        timestamp,
        durationSeconds,
        "active",
        "test",
        "Test",
        $"Sample {ordinal}",
        "Gallery",
        installationId,
        0,
        ordinal);
}
