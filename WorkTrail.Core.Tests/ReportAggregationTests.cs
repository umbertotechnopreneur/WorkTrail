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
using System.IO;
using System.Linq;
using System.Threading;
using Microsoft.Data.Sqlite;
using WorkTrail.Application;
using WorkTrail.Runtime;
using WorkTrail.Services;
using Xunit;

namespace WorkTrail.Core.Tests;

public sealed class ReportAggregationTests
{
    [Fact]
    public void Build_RejectsInvalidAndOversizedRanges()
    {
        WithStore((_, reports) =>
        {
            var reversed = reports.Build(
                new ReportQuery(new DateOnly(2026, 2, 2), new DateOnly(2026, 2, 1), "UTC"),
                CancellationToken.None);
            var oversized = reports.Build(
                new ReportQuery(new DateOnly(2025, 1, 1), new DateOnly(2026, 1, 2), "UTC"),
                CancellationToken.None);
            var unknownTimeZone = reports.Build(
                new ReportQuery(new DateOnly(2026, 2, 1), new DateOnly(2026, 2, 1), "Not/A-Time-Zone"),
                CancellationToken.None);

            Assert.False(reversed.Succeeded);
            Assert.Equal("report.range.invalid", reversed.Code);
            Assert.False(oversized.Succeeded);
            Assert.Equal("report.range.too_large", oversized.Code);
            Assert.False(unknownTimeZone.Succeeded);
            Assert.Equal("report.time_zone.invalid", unknownTimeZone.Code);
        });
    }

    [Fact]
    public void Build_RepresentsNoDataSeparatelyFromZeroActivity()
    {
        WithStore((_, reports) =>
        {
            var result = reports.Build(
                new ReportQuery(new DateOnly(2026, 2, 1), new DateOnly(2026, 2, 1), "UTC"),
                CancellationToken.None);

            Assert.True(result.Succeeded);
            var snapshot = Assert.IsType<ReportSnapshot>(result.Value);
            Assert.False(snapshot.Quality.HasData);
            Assert.Equal(0, snapshot.Quality.SampleCount);
            Assert.Equal(86_400, snapshot.Quality.RequestedSeconds);
            Assert.Equal(0, snapshot.Quality.CoveredSeconds);
            Assert.Equal(0d, snapshot.Quality.CoverageRatio);
            Assert.Single(snapshot.Calendar);
            Assert.False(snapshot.Calendar[0].HasData);
            Assert.Null(snapshot.Calendar[0].ActivityScore);
            Assert.Empty(snapshot.Calendar[0].Installations!);
            Assert.Equal(168, snapshot.HourOfWeek.Count);
            Assert.All(snapshot.HourOfWeek, cell => Assert.False(cell.HasData));
            Assert.All(snapshot.HourOfWeek, cell => Assert.Null(cell.ActivityScore));
            Assert.All(snapshot.HourOfWeek, cell => Assert.Empty(cell.Installations));
            Assert.Empty(snapshot.Applications);
        });
    }

    [Fact]
    public void Build_ComputesNormalizedDailyActivityScoreAndKeepsObservedZeroDistinct()
    {
        WithStore((store, reports) =>
        {
            store.AppendSample(Sample(store,
                new DateTimeOffset(2026, 2, 1, 12, 0, 0, TimeSpan.Zero),
                durationSeconds: 60,
                application: "Editor",
                keyPresses: 40,
                mouseClicks: 8));
            store.AppendSample(Sample(store,
                new DateTimeOffset(2026, 2, 2, 12, 0, 0, TimeSpan.Zero),
                durationSeconds: 60,
                application: "Desktop",
                state: "idle"));

            var result = reports.Build(
                new ReportQuery(new DateOnly(2026, 2, 1), new DateOnly(2026, 2, 3), "UTC"),
                CancellationToken.None);

            Assert.True(result.Succeeded);
            var snapshot = Assert.IsType<ReportSnapshot>(result.Value);
            Assert.Equal(6, snapshot.ContractVersion);
            Assert.Equal(62, snapshot.Calendar[0].ActivityScore);
            Assert.True(snapshot.Calendar[1].HasData);
            Assert.Equal(0, snapshot.Calendar[1].ActivityScore);
            var observedIdle = Assert.Single(snapshot.HourOfWeek, cell => cell.DayOfWeek == 1 && cell.Hour == 11);
            Assert.True(observedIdle.HasData);
            Assert.Equal(0, observedIdle.ActivityScore);
            Assert.False(snapshot.Calendar[2].HasData);
            Assert.Null(snapshot.Calendar[2].ActivityScore);
        });
    }

    [Fact]
    public void Build_UnionsOverlappingInstallationsAndKeepsEventCountsAdditive()
    {
        WithStore((store, reports) =>
        {
            var intervalEnd = new DateTimeOffset(2026, 2, 1, 12, 1, 0, TimeSpan.Zero);
            var installationA = InsertInstallationProfile(store, "Work laptop");
            var installationB = InsertInstallationProfile(store, "Home desktop");
            store.AppendSample(Sample(store,
                intervalEnd,
                durationSeconds: 60,
                application: "Editor",
                keyPresses: 4,
                mouseClicks: 1,
                installationId: installationA.InstallationId));
            store.AppendSample(Sample(store,
                intervalEnd,
                durationSeconds: 60,
                application: "Desktop",
                keyPresses: 6,
                mouseClicks: 2,
                state: "idle",
                installationId: installationB.InstallationId));

            var result = reports.Build(
                new ReportQuery(new DateOnly(2026, 2, 1), new DateOnly(2026, 2, 1), "UTC"),
                CancellationToken.None);

            Assert.True(result.Succeeded);
            var snapshot = Assert.IsType<ReportSnapshot>(result.Value);
            Assert.Equal(6, snapshot.ContractVersion);
            Assert.Equal(60, snapshot.Totals.TrackedSeconds);
            Assert.Equal(60, snapshot.Totals.ActiveSeconds);
            Assert.Equal(0, snapshot.Totals.IdleSeconds);
            Assert.Equal(10, snapshot.Totals.KeyPresses);
            Assert.Equal(3, snapshot.Totals.MouseClicks);
            Assert.Equal(2, snapshot.Quality.SampleCount);
            Assert.Equal(60, snapshot.Quality.CoveredSeconds);

            var day = Assert.Single(snapshot.Calendar);
            Assert.Equal(2, day.SampleCount);
            Assert.Equal(day.TrackedSeconds, day.ActiveSeconds + day.IdleSeconds);
            Assert.Equal(
                [installationB.InstallationId, installationA.InstallationId],
                day.Installations!.Select(profile => profile.InstallationId));
            Assert.Equal(["Home desktop", "Work laptop"], day.Installations!.Select(profile => profile.FriendlyName));
            var sundayNoon = Assert.Single(snapshot.HourOfWeek, cell => cell.DayOfWeek == 0 && cell.Hour == 12);
            Assert.Equal(60, sundayNoon.TrackedSeconds);
            Assert.Equal(60, sundayNoon.ActiveSeconds);
            Assert.Equal(0, sundayNoon.IdleSeconds);
            Assert.Equal(10, sundayNoon.KeyPresses);
            Assert.Equal(3, sundayNoon.MouseClicks);
            Assert.Equal(2, sundayNoon.SampleCount);
            Assert.Equal(day.ActivityScore, sundayNoon.ActivityScore);
            Assert.Equal(day.Installations, sundayNoon.Installations);
        });
    }

    [Fact]
    public void Build_ReportsOnlyInstallationsContributingToEachObservedHour()
    {
        WithStore((store, reports) =>
        {
            var morning = new DateTimeOffset(2026, 2, 1, 8, 0, 0, TimeSpan.Zero);
            var workstation = InsertInstallationProfile(store, "Workstation");
            var laptop = InsertInstallationProfile(store, "Laptop");
            var tablet = InsertInstallationProfile(store, "Tablet");
            store.AppendSample(Sample(store, morning.AddMinutes(1), 60, "Editor",
                installationId: workstation.InstallationId));
            store.AppendSample(Sample(store, morning.AddMinutes(2), 60, "Editor",
                installationId: workstation.InstallationId));
            store.AppendSample(Sample(store, morning.AddHours(1).AddMinutes(1), 60, "Desktop", state: "idle",
                installationId: laptop.InstallationId));
            store.AppendSample(Sample(store, morning.AddHours(2).AddSeconds(30), 60, "Notes",
                installationId: tablet.InstallationId));
            store.AppendSample(Sample(store, morning.AddDays(7).AddMinutes(1), 60, "Editor",
                installationId: laptop.InstallationId));

            var result = reports.Build(
                new ReportQuery(new DateOnly(2026, 2, 1), new DateOnly(2026, 2, 8), "UTC"),
                CancellationToken.None);

            Assert.True(result.Succeeded);
            var snapshot = Assert.IsType<ReportSnapshot>(result.Value);
            var eight = Assert.Single(snapshot.HourOfWeek, cell => cell.DayOfWeek == 0 && cell.Hour == 8);
            Assert.Equal(2, eight.ObservationDays);
            Assert.Equal(3, eight.SampleCount);
            Assert.Equal([laptop.InstallationId, workstation.InstallationId],
                eight.Installations.Select(profile => profile.InstallationId));
            var nine = Assert.Single(snapshot.HourOfWeek, cell => cell.DayOfWeek == 0 && cell.Hour == 9);
            Assert.Equal([laptop.InstallationId, tablet.InstallationId],
                nine.Installations.Select(profile => profile.InstallationId));
            var ten = Assert.Single(snapshot.HourOfWeek, cell => cell.DayOfWeek == 0 && cell.Hour == 10);
            Assert.Equal(tablet.InstallationId, Assert.Single(ten.Installations).InstallationId);
            Assert.All(snapshot.HourOfWeek.Where(cell => !cell.HasData), cell => Assert.Empty(cell.Installations));
            Assert.Equal(3, snapshot.Calendar[0].Installations!.Count);
            Assert.Equal(laptop.InstallationId, Assert.Single(snapshot.Calendar[7].Installations!).InstallationId);
        });
    }

    [Theory]
    [InlineData(12, "active", 6, 3)]
    [InlineData(12, "idle", 5, 2)]
    [InlineData(0, "active", 6, 3)]
    [InlineData(0, "idle", 5, 2)]
    public void Build_UnionsOverlappingInstallationsAcrossHourAndDayBoundaries(
        int boundaryHour,
        string secondState,
        long expectedActiveSeconds,
        long expectedActiveSecondsAfterBoundary)
    {
        WithStore((store, reports) =>
        {
            var boundary = new DateTimeOffset(2026, 2, 2, boundaryHour, 0, 0, TimeSpan.Zero);
            var installationA = InsertInstallationProfile(store, "Work laptop");
            var installationB = InsertInstallationProfile(store, "Home desktop");
            store.AppendSample(Sample(store,
                boundary.AddSeconds(2),
                durationSeconds: 5,
                application: "Editor",
                keyPresses: 5,
                mouseClicks: 2,
                installationId: installationA.InstallationId));
            store.AppendSample(Sample(store,
                boundary.AddSeconds(3),
                durationSeconds: 5,
                application: "Editor",
                keyPresses: 5,
                mouseClicks: 2,
                state: secondState,
                installationId: installationB.InstallationId));
            var from = DateOnly.FromDateTime(boundary.AddSeconds(-3).DateTime);
            var to = DateOnly.FromDateTime(boundary.DateTime);

            var result = reports.Build(new ReportQuery(from, to, "UTC"), CancellationToken.None);

            Assert.True(result.Succeeded);
            var snapshot = Assert.IsType<ReportSnapshot>(result.Value);
            Assert.Equal(6, snapshot.Totals.TrackedSeconds);
            Assert.Equal(expectedActiveSeconds, snapshot.Totals.ActiveSeconds);
            Assert.Equal(6 - expectedActiveSeconds, snapshot.Totals.IdleSeconds);
            Assert.Equal(10, snapshot.Totals.KeyPresses);
            Assert.Equal(4, snapshot.Totals.MouseClicks);
            Assert.Equal(6, snapshot.Quality.CoveredSeconds);
            Assert.Equal(2, snapshot.Quality.SampleCount);
            Assert.Equal(expectedActiveSeconds, Assert.Single(snapshot.Applications).ActiveSeconds);
            Assert.All(snapshot.Calendar, day =>
            {
                Assert.Equal(2, day.SampleCount);
                Assert.Equal(2, day.Installations!.Count);
                Assert.Equal(boundaryHour == 0 ? 3 : 6, day.TrackedSeconds);
                Assert.Equal(day.TrackedSeconds, day.ActiveSeconds + day.IdleSeconds);
            });

            var before = boundary.AddSeconds(-1);
            var beforeHour = Assert.Single(snapshot.HourOfWeek,
                cell => cell.DayOfWeek == (int)before.DayOfWeek && cell.Hour == before.Hour);
            var afterHour = Assert.Single(snapshot.HourOfWeek,
                cell => cell.DayOfWeek == (int)boundary.DayOfWeek && cell.Hour == boundary.Hour);
            Assert.Equal(3, beforeHour.TrackedSeconds);
            Assert.Equal(3, beforeHour.ActiveSeconds);
            Assert.Equal(3, afterHour.TrackedSeconds);
            Assert.Equal(expectedActiveSecondsAfterBoundary, afterHour.ActiveSeconds);
            Assert.Equal(2, beforeHour.SampleCount);
            Assert.Equal(2, afterHour.SampleCount);
            Assert.Equal([installationB.InstallationId, installationA.InstallationId],
                beforeHour.Installations.Select(profile => profile.InstallationId));
            Assert.Equal(beforeHour.Installations, afterHour.Installations);
            Assert.Equal(10, beforeHour.KeyPresses + afterHour.KeyPresses);
            Assert.Equal(4, beforeHour.MouseClicks + afterHour.MouseClicks);
        });
    }

    [Fact]
    public void Build_PreservesDailyCoverageWhenOverlappingSamplesSpanMultipleDays()
    {
        WithStore((store, reports) =>
        {
            var boundary = new DateTimeOffset(2026, 2, 1, 12, 0, 0, TimeSpan.Zero);
            var installationA = InsertInstallationProfile(store, "Work laptop");
            var installationB = InsertInstallationProfile(store, "Home desktop");
            store.AppendSample(Sample(store,
                boundary.AddDays(1).AddSeconds(2),
                durationSeconds: 86_405,
                application: "Editor",
                keyPresses: 7,
                installationId: installationA.InstallationId));
            store.AppendSample(Sample(store,
                boundary.AddSeconds(3),
                durationSeconds: 5,
                application: "Desktop",
                keyPresses: 5,
                state: "idle",
                installationId: installationB.InstallationId));

            var result = reports.Build(
                new ReportQuery(new DateOnly(2026, 2, 1), new DateOnly(2026, 2, 2), "UTC"),
                CancellationToken.None);

            Assert.True(result.Succeeded);
            var snapshot = Assert.IsType<ReportSnapshot>(result.Value);
            Assert.Equal(86_405, snapshot.Totals.TrackedSeconds);
            Assert.Equal(86_405, snapshot.Totals.ActiveSeconds);
            Assert.Equal(0, snapshot.Totals.IdleSeconds);
            Assert.Equal(12, snapshot.Totals.KeyPresses);
            Assert.Equal(86_405, snapshot.Quality.CoveredSeconds);
            Assert.Equal(43_203, snapshot.Calendar[0].TrackedSeconds);
            Assert.Equal(43_202, snapshot.Calendar[1].TrackedSeconds);
            Assert.Equal(2, snapshot.Calendar[0].Installations!.Count);
            Assert.Single(snapshot.Calendar[1].Installations!);
            Assert.Equal(86_405, snapshot.HourOfWeek.Sum(hour => hour.TrackedSeconds));
        });
    }

    [Fact]
    public void Build_FailsFastWhenActivityReferencesUnknownInstallationProfile()
    {
        WithStore((store, reports) =>
        {
            store.AppendSample(Sample(
                store,
                new DateTimeOffset(2026, 2, 1, 12, 1, 0, TimeSpan.Zero),
                durationSeconds: 60,
                application: "Editor",
                installationId: Guid.NewGuid().ToString("N")));

            var exception = Assert.Throws<InvalidDataException>(() => reports.Build(
                new ReportQuery(new DateOnly(2026, 2, 1), new DateOnly(2026, 2, 1), "UTC"),
                CancellationToken.None));

            Assert.Contains("unknown installation profile", exception.Message, StringComparison.OrdinalIgnoreCase);
        });
    }

    [Fact]
    public void Build_UnionsPartialCoverageButKeepsApplicationSlicesIndependent()
    {
        WithStore((store, reports) =>
        {
            var intervalStart = new DateTimeOffset(2026, 2, 1, 12, 0, 0, TimeSpan.Zero);
            var installationA = InsertInstallationProfile(store, "Editing PC");
            var installationB = InsertInstallationProfile(store, "Browser PC");
            var installationC = InsertInstallationProfile(store, "Idle PC");
            store.AppendSample(Sample(store,
                intervalStart.AddSeconds(5),
                durationSeconds: 5,
                application: "Editor",
                installationId: installationA.InstallationId));
            store.AppendSample(Sample(store,
                intervalStart.AddSeconds(8),
                durationSeconds: 5,
                application: "Browser",
                installationId: installationB.InstallationId));
            store.AppendSample(Sample(store,
                intervalStart.AddSeconds(12),
                durationSeconds: 5,
                application: "Desktop",
                state: "idle",
                installationId: installationC.InstallationId));

            var result = reports.Build(
                new ReportQuery(new DateOnly(2026, 2, 1), new DateOnly(2026, 2, 1), "UTC"),
                CancellationToken.None);

            Assert.True(result.Succeeded);
            var snapshot = Assert.IsType<ReportSnapshot>(result.Value);
            Assert.Equal(12, snapshot.Totals.TrackedSeconds);
            Assert.Equal(8, snapshot.Totals.ActiveSeconds);
            Assert.Equal(4, snapshot.Totals.IdleSeconds);
            Assert.Equal(3, snapshot.Quality.SampleCount);
            Assert.Equal(12, snapshot.Quality.CoveredSeconds);
            Assert.Equal(5, Assert.Single(snapshot.Applications, item => item.Application == "Editor").ActiveSeconds);
            Assert.Equal(5, Assert.Single(snapshot.Applications, item => item.Application == "Browser").ActiveSeconds);
            Assert.Equal(10, snapshot.Applications.Sum(item => item.ActiveSeconds));
        });
    }

    [Fact]
    public void Get24HourActivityTrend_RequiresFullCoverageAndBucketsActiveSeconds()
    {
        WithStore((store, _) =>
        {
            var windowEnd = new DateTimeOffset(2026, 2, 2, 0, 0, 0, TimeSpan.Zero);
            var windowStart = windowEnd.AddHours(-24);
            store.AppendSample(Sample(store, windowStart.AddHours(1), 3600, "first-hour"));

            var incomplete = store.Get24HourActivityTrend(windowEnd);

            Assert.False(incomplete.HasCompleteCoverage);

            for (var hour = 0; hour < 24; hour++)
            {
                store.AppendSample(Sample(store, windowStart.AddHours(hour + 1), 3600, $"hour-{hour}"));
            }

            var trend = store.Get24HourActivityTrend(windowEnd);

            Assert.True(trend.HasCompleteCoverage);
            Assert.Equal(24, trend.HourlyActivityLevels.Count);
            Assert.All(trend.HourlyActivityLevels, level => Assert.Equal(100d, level));
        });
    }

    [Fact]
    public void Build_AggregatesAiUsageByProviderAndOrigin_WithoutEstimatingMissingCosts()
    {
        WithStore((store, reports) =>
        {
            var occurredAt = new DateTimeOffset(2026, 2, 1, 12, 0, 0, TimeSpan.Zero);
            var firstRequest = AiUsage(
                occurredAt,
                "openrouter",
                "snapshot.scheduled",
                success: true,
                usage: new AiUsageMetrics(
                    InputTokens: 100,
                    OutputTokens: 20,
                    TotalTokens: 120,
                    CachedInputTokens: 30,
                    ReasoningTokens: 2,
                    ReportedCostUsd: 0.015m));
            store.AppendAiAnalysisAndUsage(firstRequest, AnalysisFor(firstRequest));

            var secondRequest = AiUsage(
                occurredAt.AddMinutes(1),
                "anthropic",
                "cli.ai",
                success: true,
                usage: new AiUsageMetrics(
                    InputTokens: 50,
                    OutputTokens: 10,
                    TotalTokens: 60,
                    CacheReadInputTokens: 10,
                    ThinkingTokens: 4));
            store.AppendAiAnalysisAndUsage(secondRequest, AnalysisFor(secondRequest));

            store.AppendAiUsage(AiUsage(
                occurredAt.AddMinutes(2),
                "openrouter",
                "snapshot.scheduled",
                success: false,
                usage: new AiUsageMetrics()));
            var outOfRangeRequest = AiUsage(
                occurredAt.AddDays(-1),
                "openrouter",
                "snapshot.scheduled",
                success: true,
                usage: new AiUsageMetrics(InputTokens: 999, ReportedCostUsd: 9.99m));
            store.AppendAiAnalysisAndUsage(outOfRangeRequest, AnalysisFor(outOfRangeRequest));

            var result = reports.Build(
                new ReportQuery(new DateOnly(2026, 2, 1), new DateOnly(2026, 2, 1), "UTC"),
                CancellationToken.None);

            Assert.True(result.Succeeded);
            var snapshot = Assert.IsType<ReportSnapshot>(result.Value);
            Assert.Equal(6, snapshot.ContractVersion);
            var usage = snapshot.AiUsage;
            Assert.Equal(3, usage.RequestCount);
            Assert.Equal(2, usage.SuccessfulRequestCount);
            Assert.Equal(1, usage.FailedRequestCount);
            Assert.Equal(150, usage.InputTokens);
            Assert.Equal(30, usage.OutputTokens);
            Assert.Equal(180, usage.TotalTokens);
            Assert.Equal(40, usage.CachedInputTokens);
            Assert.Equal(2, usage.ReasoningTokens);
            Assert.Equal(4, usage.ThinkingTokens);
            Assert.Equal(0.015m, usage.ActualCostUsd);
            Assert.Equal(1, usage.ActualCostRequestCount);
            Assert.Null(usage.EstimatedCostUsd);
            Assert.Equal(0, usage.EstimatedCostRequestCount);
            Assert.Null(usage.EstimatedCostPricingUpdatedAt);

            Assert.Collection(
                usage.ByProvider,
                openRouter =>
                {
                    Assert.Equal("openrouter", openRouter.Label);
                    Assert.Equal(2, openRouter.RequestCount);
                    Assert.Equal(120, openRouter.TotalTokens);
                    Assert.Equal(0.015m, openRouter.ActualCostUsd);
                    Assert.Null(openRouter.EstimatedCostUsd);
                },
                anthropic =>
                {
                    Assert.Equal("anthropic", anthropic.Label);
                    Assert.Equal(1, anthropic.RequestCount);
                    Assert.Equal(60, anthropic.TotalTokens);
                    Assert.Null(anthropic.ActualCostUsd);
                    Assert.Null(anthropic.EstimatedCostUsd);
                });
            Assert.Collection(
                usage.ByOrigin,
                automatic =>
                {
                    Assert.Equal("snapshot.scheduled", automatic.Label);
                    Assert.Equal(2, automatic.RequestCount);
                    Assert.Equal(0.015m, automatic.ActualCostUsd);
                    Assert.Null(automatic.EstimatedCostUsd);
                },
                cli =>
                {
                    Assert.Equal("cli.ai", cli.Label);
                    Assert.Equal(1, cli.RequestCount);
                    Assert.Null(cli.ActualCostUsd);
                    Assert.Null(cli.EstimatedCostUsd);
                });
        });
    }

    [Fact]
    public void BuildAiUsage_DoesNotScanTheActivityHistory()
    {
        WithStore((store, reports) =>
        {
            var occurredAt = new DateTimeOffset(2026, 2, 1, 12, 0, 0, TimeSpan.Zero);
            store.AppendAiUsage(AiUsage(
                occurredAt,
                "openrouter",
                "snapshot.scheduled",
                success: true,
                usage: new AiUsageMetrics(InputTokens: 10, OutputTokens: 2, TotalTokens: 12, ReportedCostUsd: 0.01m)));
            using (var connection = new SqliteConnection($"Data Source={store.ActivityDatabasePath};Pooling=False"))
            {
                connection.Open();
                using var command = connection.CreateCommand();
                command.CommandText = "DROP TABLE activity_samples;";
                command.ExecuteNonQuery();
            }

            var usage = reports.BuildAiUsage(
                new DateOnly(2026, 2, 1),
                new DateOnly(2026, 2, 1),
                TimeZoneInfo.Utc,
                CancellationToken.None);

            Assert.Equal(1, usage.RequestCount);
            Assert.Equal(12, usage.TotalTokens);
            Assert.Equal(0.01m, usage.ActualCostUsd);
        });
    }

    [Fact]
    public void Build_EstimatesOpenAiCostsFromStoredPricing()
    {
        WithStore((store, reports) =>
        {
            var pricingUpdatedAt = new DateTimeOffset(2026, 2, 1, 8, 0, 0, TimeSpan.Zero);
            store.ReplaceAiModelPricing(AiPricingProviders.OpenAi,
            [
                new AiModelPricing(
                    AiPricingProviders.OpenAi,
                    "gpt-test",
                    AiPricingServiceTiers.Standard,
                    AiPricingContextWindows.Short,
                    "usd",
                    InputUsdPerMillionTokens: 1.25m,
                    CachedInputUsdPerMillionTokens: 0.125m,
                    CacheWriteUsdPerMillionTokens: null,
                    OutputUsdPerMillionTokens: 10m,
                    SourceUrl: "https://developers.openai.com/api/docs/pricing.md",
                    SourceRetrievedAt: pricingUpdatedAt)
            ]);
            var occurredAt = new DateTimeOffset(2026, 2, 1, 12, 0, 0, TimeSpan.Zero);
            var request = AiUsage(
                occurredAt,
                AiPricingProviders.OpenAi,
                "snapshot.scheduled",
                success: true,
                usage: new AiUsageMetrics(
                    InputTokens: 1_000,
                    OutputTokens: 100,
                    TotalTokens: 1_100,
                    CachedInputTokens: 200),
                requestedModel: "gpt-test-2026-01-01");
            store.AppendAiAnalysisAndUsage(request, AnalysisFor(request));

            var result = reports.Build(
                new ReportQuery(new DateOnly(2026, 2, 1), new DateOnly(2026, 2, 1), "UTC"),
                CancellationToken.None);

            Assert.True(result.Succeeded);
            var usage = Assert.IsType<ReportSnapshot>(result.Value).AiUsage;
            Assert.Null(usage.ActualCostUsd);
            Assert.Equal(0.002025m, usage.EstimatedCostUsd);
            Assert.Equal(1, usage.EstimatedCostRequestCount);
            Assert.Equal(pricingUpdatedAt, usage.EstimatedCostPricingUpdatedAt);
            var provider = Assert.Single(usage.ByProvider);
            Assert.Equal("openai", provider.Label);
            Assert.Equal(0.002025m, provider.EstimatedCostUsd);
        });
    }

    [Fact]
    public void Retention_PreservesCurrentAnalysisLinkedToAnOlderRequest()
    {
        WithStore((store, reports) =>
        {
            var now = DateTimeOffset.UtcNow;
            var request = AiUsage(
                now.AddDays(-10),
                "openrouter",
                "snapshot.scheduled",
                success: true,
                usage: new AiUsageMetrics(InputTokens: 10, OutputTokens: 2, TotalTokens: 12));
            var analysis = AnalysisFor(request) with { Timestamp = now };
            store.AppendAiAnalysisAndUsage(request, analysis);
            var cutoff = now.AddDays(-1);

            var preview = store.GetRetentionPreview(cutoff);
            var removed = store.ApplyRetention(cutoff);

            Assert.Equal(0, preview.RecordCount);
            Assert.Equal(0, removed);
            var report = reports.Build(
                new ReportQuery(
                    DateOnly.FromDateTime(request.OccurredAt.UtcDateTime),
                    DateOnly.FromDateTime(request.OccurredAt.UtcDateTime),
                    "UTC"),
                CancellationToken.None);
            Assert.True(report.Succeeded);
            Assert.Equal(1, report.Value?.AiUsage.RequestCount);
            Assert.Equal(request.CorrelationId, store.LoadLatestAnalysis()?.CorrelationId);
        });
    }

    [Fact]
    public void Build_MaximumRangeSnapshotFitsTheIpcEnvelopeLimit()
    {
        WithStore((_, reports) =>
        {
            var result = reports.Build(
                new ReportQuery(new DateOnly(2026, 1, 1), new DateOnly(2027, 1, 1), "UTC"),
                CancellationToken.None);

            Assert.True(result.Succeeded);
            var response = new RuntimeResponseEnvelope(
                RuntimeProtocol.ProtocolVersion,
                Guid.NewGuid(),
                true,
                result.Code,
                result.MessageKey,
                result.Value,
                result.Issues);
            var payload = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(response, RuntimeProtocol.SerializerOptions);

            Assert.True(payload.Length < RuntimeProtocol.MaximumMessageBytes);
        });
    }

    [Fact]
    public void Build_SplitsOneSampleAcrossLocalMidnightAndHour()
    {
        WithStore((store, reports) =>
        {
            store.AppendSample(Sample(store,
                new DateTimeOffset(2026, 2, 2, 0, 0, 5, TimeSpan.Zero),
                durationSeconds: 10,
                application: "Editor",
                keyPresses: 10,
                mouseClicks: 2));

            var result = reports.Build(
                new ReportQuery(new DateOnly(2026, 2, 1), new DateOnly(2026, 2, 2), "UTC"),
                CancellationToken.None);

            Assert.True(result.Succeeded);
            var snapshot = Assert.IsType<ReportSnapshot>(result.Value);
            Assert.Equal(5, snapshot.Calendar[0].TrackedSeconds);
            Assert.Equal(5, snapshot.Calendar[1].TrackedSeconds);
            Assert.Equal(5, snapshot.Calendar[0].KeyPresses);
            Assert.Equal(5, snapshot.Calendar[1].KeyPresses);
            Assert.Equal(1, snapshot.Calendar[0].MouseClicks);
            Assert.Equal(1, snapshot.Calendar[1].MouseClicks);
            Assert.Equal(1, snapshot.Calendar[0].SampleCount);
            Assert.Equal(1, snapshot.Calendar[1].SampleCount);
            Assert.Equal(15, snapshot.Calendar[0].ActivityScore);
            Assert.Equal(15, snapshot.Calendar[1].ActivityScore);

            var beforeMidnight = Assert.Single(snapshot.HourOfWeek, cell => cell.DayOfWeek == 0 && cell.Hour == 23);
            var afterMidnight = Assert.Single(snapshot.HourOfWeek, cell => cell.DayOfWeek == 1 && cell.Hour == 0);
            Assert.Equal(5, beforeMidnight.ActiveSeconds);
            Assert.Equal(5, afterMidnight.ActiveSeconds);
            Assert.Equal(5, beforeMidnight.KeyPresses);
            Assert.Equal(5, afterMidnight.KeyPresses);
            Assert.Equal(1, beforeMidnight.MouseClicks);
            Assert.Equal(1, afterMidnight.MouseClicks);
            Assert.Equal(1, beforeMidnight.SampleCount);
            Assert.Equal(1, afterMidnight.SampleCount);
            Assert.Equal(15, beforeMidnight.ActivityScore);
            Assert.Equal(15, afterMidnight.ActivityScore);
            Assert.Equal(10, snapshot.Totals.ActiveSeconds);
            Assert.Equal(10, snapshot.Quality.CoveredSeconds);
        });
    }

    [Fact]
    public void Build_SplitsAcrossDaylightSavingGapAndUsesActualDayLength()
    {
        WithStore((store, reports) =>
        {
            store.AppendSample(Sample(store,
                new DateTimeOffset(2026, 3, 8, 7, 0, 5, TimeSpan.Zero),
                durationSeconds: 10,
                application: "Editor"));

            var result = reports.Build(
                new ReportQuery(new DateOnly(2026, 3, 8), new DateOnly(2026, 3, 8), "Eastern Standard Time"),
                CancellationToken.None);

            Assert.True(result.Succeeded);
            var snapshot = Assert.IsType<ReportSnapshot>(result.Value);
            Assert.Equal(82_800, snapshot.Quality.RequestedSeconds);
            Assert.Equal(5, Assert.Single(snapshot.HourOfWeek, cell => cell.DayOfWeek == 0 && cell.Hour == 1).ActiveSeconds);
            Assert.Equal(0, Assert.Single(snapshot.HourOfWeek, cell => cell.DayOfWeek == 0 && cell.Hour == 2).ActiveSeconds);
            Assert.Equal(5, Assert.Single(snapshot.HourOfWeek, cell => cell.DayOfWeek == 0 && cell.Hour == 3).ActiveSeconds);
            Assert.Equal(10, snapshot.Totals.ActiveSeconds);
        });
    }

    [Fact]
    public void Build_CombinesRepeatedLocalHoursWithoutDuplicatingObservationDays()
    {
        WithStore((store, reports) =>
        {
            foreach (var utcHour in new[] { 5, 6 })
            {
                store.AppendSample(Sample(store,
                    new DateTimeOffset(2026, 11, 1, utcHour, 30, 0, TimeSpan.Zero),
                    durationSeconds: 60, application: "Editor", keyPresses: 40, mouseClicks: 8));
            }

            var result = reports.Build(new ReportQuery(new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 1), "Eastern Standard Time"), CancellationToken.None);
            var snapshot = Assert.IsType<ReportSnapshot>(result.Value);
            var repeated = Assert.Single(snapshot.HourOfWeek, cell => cell.DayOfWeek == 0 && cell.Hour == 1);
            Assert.Equal(120, repeated.ActiveSeconds);
            Assert.Equal(1, repeated.ObservationDays);
            Assert.Equal(2, repeated.SampleCount);
            Assert.Equal(80, repeated.KeyPresses);
            Assert.Equal(16, repeated.MouseClicks);
            Assert.Equal(62, repeated.ActivityScore);
            Assert.Equal(90_000, snapshot.Quality.RequestedSeconds);
        });
    }

    [Fact]
    public void Build_ReturnsTopTwelveApplicationsAndOther()
    {
        WithStore((store, reports) =>
        {
            for (var index = 1; index <= 14; index++)
            {
                store.AppendSample(Sample(store,
                    new DateTimeOffset(2026, 2, 1, 12, index, 0, TimeSpan.Zero),
                    durationSeconds: index,
                    application: $"App{index:00}"));
            }

            var result = reports.Build(
                new ReportQuery(new DateOnly(2026, 2, 1), new DateOnly(2026, 2, 1), "UTC"),
                CancellationToken.None);

            Assert.True(result.Succeeded);
            var applications = Assert.IsType<ReportSnapshot>(result.Value).Applications;
            Assert.Equal(13, applications.Count);
            Assert.Equal("App14", applications[0].Application);
            Assert.Equal(14, applications[0].ActiveSeconds);
            Assert.Equal("Other", applications[^1].Application);
            Assert.Equal(3, applications[^1].ActiveSeconds);
            Assert.DoesNotContain(applications, item => item.Application is "App01" or "App02");
        });
    }

    [Fact]
    public void Build_HourOfWeekUsesTheMeanAcrossObservedDates()
    {
        WithStore((store, reports) =>
        {
            store.AppendSample(Sample(store,
                new DateTimeOffset(2026, 2, 1, 12, 0, 10, TimeSpan.Zero),
                durationSeconds: 10,
                application: "Editor"));
            store.AppendSample(Sample(store,
                new DateTimeOffset(2026, 2, 8, 12, 0, 21, TimeSpan.Zero),
                durationSeconds: 21,
                application: "Editor"));

            var result = reports.Build(
                new ReportQuery(new DateOnly(2026, 2, 1), new DateOnly(2026, 2, 8), "UTC"),
                CancellationToken.None);

            Assert.True(result.Succeeded);
            var snapshot = Assert.IsType<ReportSnapshot>(result.Value);
            var sundayNoon = Assert.Single(snapshot.HourOfWeek, cell => cell.DayOfWeek == 0 && cell.Hour == 12);
            Assert.Equal(2, sundayNoon.ObservationDays);
            Assert.Equal(16, sundayNoon.ActiveSeconds);
            Assert.Equal(16, sundayNoon.TrackedSeconds);
            Assert.Equal(31, snapshot.Totals.ActiveSeconds);
        });
    }

    [Fact]
    public void ActivityHistory_FailsWhenLegacyJsonlExists()
    {
        var dataDirectory = CreateDataDirectory();
        try
        {
            File.WriteAllText(
                Path.Combine(dataDirectory, "activity.jsonl"),
                "{\"timestamp\":\"2026-02-01T12:00:00Z\",\"durationSeconds\":5,\"state\":\"active\"}");
            var exception = Assert.Throws<InvalidOperationException>(() => new LocalStore(dataDirectory));
            Assert.Contains("Legacy storage", exception.Message, StringComparison.Ordinal);
            Assert.False(File.Exists(Path.Combine(dataDirectory, "activity.sqlite3")));
        }
        finally
        {
            DeleteDataDirectory(dataDirectory);
        }
    }

    [Fact]
    public void ActivityHistory_FailsWhenLegacyAnalysisJsonlExists()
    {
        var dataDirectory = CreateDataDirectory();
        try
        {
            File.WriteAllText(Path.Combine(dataDirectory, "analyses.jsonl"), "{}");

            var exception = Assert.Throws<InvalidOperationException>(() => new LocalStore(dataDirectory));

            Assert.Contains("analyses.jsonl", exception.Message, StringComparison.Ordinal);
            Assert.False(File.Exists(Path.Combine(dataDirectory, "activity.sqlite3")));
        }
        finally
        {
            DeleteDataDirectory(dataDirectory);
        }
    }

    /// <summary>Preserves real activity history while opening unversioned, old or newer beta databases.</summary>
    /// <param name="version">The informational database version that must not block opening.</param>
    [Theory]
    [InlineData(0)]
    [InlineData(10)]
    [InlineData(99)]
    public void ActivityHistory_OpensDifferentVersionsAndPreservesHistory(int version)
    {
        var dataDirectory = CreateDataDirectory();
        try
        {
            var store = new LocalStore(dataDirectory);
            store.AppendSample(Sample(store, new DateTimeOffset(2026, 10, 3, 12, 0, 0, TimeSpan.Zero), 60, "Beta editor"));
            var databasePath = Path.Combine(dataDirectory, "activity.sqlite3");
            using (var connection = new SqliteConnection($"Data Source={databasePath};Pooling=False"))
            {
                connection.Open();
                using var command = connection.CreateCommand();
                command.CommandText = $"PRAGMA user_version = {version};";
                command.ExecuteNonQuery();
            }

            _ = new LocalStore(dataDirectory);
            using var check = new SqliteConnection($"Data Source={databasePath};Pooling=False");
            check.Open();
            using var checkCommand = check.CreateCommand();
            checkCommand.CommandText = "SELECT application FROM activity_samples;";
            Assert.Equal("Beta editor", checkCommand.ExecuteScalar());
            checkCommand.CommandText = "PRAGMA user_version;";
            Assert.Equal((long)version, Convert.ToInt64(checkCommand.ExecuteScalar()));
        }
        finally
        {
            DeleteDataDirectory(dataDirectory);
        }
    }

    /// <summary>Retains unrelated data when adding missing activity objects to an unversioned beta database.</summary>
    [Fact]
    public void ActivityHistory_OpensUnversionedDatabaseWithoutRemovingExistingData()
    {
        var dataDirectory = CreateDataDirectory();
        try
        {
            var databasePath = Path.Combine(dataDirectory, "activity.sqlite3");
            using (var connection = new SqliteConnection($"Data Source={databasePath};Pooling=False"))
            {
                connection.Open();
                using var command = connection.CreateCommand();
                command.CommandText = "CREATE TABLE unrelated_data (id INTEGER PRIMARY KEY); INSERT INTO unrelated_data VALUES (42);";
                command.ExecuteNonQuery();
            }

            _ = new LocalStore(dataDirectory);
            using var check = new SqliteConnection($"Data Source={databasePath};Pooling=False");
            check.Open();
            using var checkCommand = check.CreateCommand();
            checkCommand.CommandText = "SELECT id FROM unrelated_data;";
            Assert.Equal(42L, Convert.ToInt64(checkCommand.ExecuteScalar()));
        }
        finally
        {
            DeleteDataDirectory(dataDirectory);
        }
    }

    /// <summary>Exposes a real SQLite failure without replacing data when a required operation cannot run.</summary>
    [Fact]
    public void ActivityHistory_ReportsActualSqliteFailureWithoutReplacingData()
    {
        var dataDirectory = CreateDataDirectory();
        try
        {
            var databasePath = Path.Combine(dataDirectory, "activity.sqlite3");
            using (var connection = new SqliteConnection($"Data Source={databasePath};Pooling=False"))
            {
                connection.Open();
                using var command = connection.CreateCommand();
                command.CommandText = "CREATE TABLE activity_samples (id INTEGER PRIMARY KEY); INSERT INTO activity_samples VALUES (42); PRAGMA user_version = 7;";
                command.ExecuteNonQuery();
            }

            var exception = Assert.Throws<SqliteException>(() => new LocalStore(dataDirectory));
            Assert.Equal(1, exception.SqliteErrorCode);
            using var check = new SqliteConnection($"Data Source={databasePath};Pooling=False");
            check.Open();
            using var checkCommand = check.CreateCommand();
            checkCommand.CommandText = "SELECT id FROM activity_samples;";
            Assert.Equal(42L, Convert.ToInt64(checkCommand.ExecuteScalar()));
        }
        finally
        {
            DeleteDataDirectory(dataDirectory);
        }
    }

    /// <summary>Recreates missing AI objects while preserving retained activity rows in a beta database.</summary>
    [Fact]
    public void ActivityHistory_CreatesMissingObjectsWithoutRemovingHistory()
    {
        var dataDirectory = CreateDataDirectory();
        try
        {
            var store = new LocalStore(dataDirectory);
            store.AppendSample(Sample(store, new DateTimeOffset(2026, 10, 3, 12, 0, 0, TimeSpan.Zero), 60, "Retained editor"));
            var databasePath = Path.Combine(dataDirectory, "activity.sqlite3");
            using (var connection = new SqliteConnection($"Data Source={databasePath};Pooling=False"))
            {
                connection.Open();
                using var command = connection.CreateCommand();
                command.CommandText = """
                    DROP TABLE ai_analysis_search;
                    DROP TABLE ai_analysis_results;
                    DROP TABLE ai_request_usage;
                    PRAGMA user_version = 1;
                    PRAGMA journal_mode = DELETE;
                    """;
                command.ExecuteNonQuery();
            }

            _ = new LocalStore(dataDirectory);
            using var check = new SqliteConnection($"Data Source={databasePath};Pooling=False");
            check.Open();
            using var checkCommand = check.CreateCommand();
            checkCommand.CommandText = "SELECT application FROM activity_samples;";
            Assert.Equal("Retained editor", checkCommand.ExecuteScalar());
            checkCommand.CommandText = "SELECT COUNT(*) FROM ai_request_usage;";
            Assert.Equal(0L, Convert.ToInt64(checkCommand.ExecuteScalar()));
        }
        finally
        {
            DeleteDataDirectory(dataDirectory);
        }
    }

    /// <summary>Initializes an existing empty file instead of rejecting its missing version metadata.</summary>
    [Fact]
    public void ActivityHistory_InitializesAnExistingEmptyDatabase()
    {
        var dataDirectory = CreateDataDirectory();
        try
        {
            var databasePath = Path.Combine(dataDirectory, "activity.sqlite3");
            File.WriteAllBytes(databasePath, []);

            _ = new LocalStore(dataDirectory);
            using var check = new SqliteConnection($"Data Source={databasePath};Pooling=False");
            check.Open();
            using var checkCommand = check.CreateCommand();
            checkCommand.CommandText = "SELECT COUNT(*) FROM activity_samples;";
            Assert.Equal(0L, Convert.ToInt64(checkCommand.ExecuteScalar()));
        }
        finally
        {
            DeleteDataDirectory(dataDirectory);
        }
    }

    /// <summary>Opens a usable beta database without deleting additional objects or their data.</summary>
    [Fact]
    public void ActivityHistory_PreservesAdditionalDatabaseObjects()
    {
        var dataDirectory = CreateDataDirectory();
        try
        {
            _ = new LocalStore(dataDirectory);
            var databasePath = Path.Combine(dataDirectory, "activity.sqlite3");
            using (var connection = new SqliteConnection($"Data Source={databasePath};Pooling=False"))
            {
                connection.Open();
                using var command = connection.CreateCommand();
                command.CommandText = "CREATE TABLE unsupported_data (id INTEGER PRIMARY KEY); INSERT INTO unsupported_data VALUES (42);";
                command.ExecuteNonQuery();
            }

            _ = new LocalStore(dataDirectory);
            using var check = new SqliteConnection($"Data Source={databasePath};Pooling=False");
            check.Open();
            using var checkCommand = check.CreateCommand();
            checkCommand.CommandText = "SELECT id FROM unsupported_data;";
            Assert.Equal(42L, Convert.ToInt64(checkCommand.ExecuteScalar()));
        }
        finally
        {
            DeleteDataDirectory(dataDirectory);
        }
    }

    /// <summary>Retains valid existing constraint definitions rather than rejecting exact SQL differences.</summary>
    [Fact]
    public void ActivityHistory_AllowsExistingConstraintDifferences()
    {
        var dataDirectory = CreateDataDirectory();
        try
        {
            _ = new LocalStore(dataDirectory);
            var databasePath = Path.Combine(dataDirectory, "activity.sqlite3");
            using (var connection = new SqliteConnection($"Data Source={databasePath};Pooling=False"))
            {
                connection.Open();
                using var command = connection.CreateCommand();
                command.CommandText = """
                    PRAGMA writable_schema = ON;
                    UPDATE sqlite_schema
                    SET sql = replace(sql, 'CHECK (success IN (0, 1))', 'CHECK (success IN (0, 1, 2))')
                    WHERE name = 'ai_request_usage';
                    PRAGMA writable_schema = OFF;
                    PRAGMA schema_version = 999;
                    """;
                command.ExecuteNonQuery();
            }

            _ = new LocalStore(dataDirectory);
            using var check = new SqliteConnection($"Data Source={databasePath};Pooling=False");
            check.Open();
            using var checkCommand = check.CreateCommand();
            checkCommand.CommandText = "SELECT sql FROM sqlite_schema WHERE name = 'ai_request_usage';";
            var sql = Assert.IsType<string>(checkCommand.ExecuteScalar());
            Assert.Contains("CHECK (success IN (0, 1, 2))", sql, StringComparison.Ordinal);
        }
        finally
        {
            DeleteDataDirectory(dataDirectory);
        }
    }

    private static ActivitySample Sample(
        LocalStore store,
        DateTimeOffset timestamp,
        int durationSeconds,
        string application,
        long keyPresses = 0,
        long mouseClicks = 0,
        string state = "active",
        string? installationId = null) => new(
            timestamp,
            durationSeconds,
            state,
            "test-process",
            application,
            "private context",
            "private window title",
            installationId ?? store.LoadSettings().InstallationId,
            keyPresses,
            mouseClicks);

    private static InstallationProfile InsertInstallationProfile(LocalStore store, string friendlyName)
    {
        var observedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var profile = InstallationProfileCatalog.CreateDefault(
            Guid.NewGuid().ToString("N"),
            friendlyName.Replace(' ', '-'),
            observedAt) with
        {
            FriendlyName = friendlyName
        };
        using var connection = new SqliteConnection($"Data Source={store.ActivityDatabasePath};Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO installation_profiles (
                installation_id, machine_name, friendly_name, color, icon,
                first_seen_utc_ticks, updated_utc_ticks, profile_revision)
            VALUES ($id, $machine, $friendly, $color, $icon, $firstSeen, $updated, $revision);
            """;
        command.Parameters.AddWithValue("$id", profile.InstallationId);
        command.Parameters.AddWithValue("$machine", profile.MachineName);
        command.Parameters.AddWithValue("$friendly", profile.FriendlyName);
        command.Parameters.AddWithValue("$color", profile.Color);
        command.Parameters.AddWithValue("$icon", profile.Icon);
        command.Parameters.AddWithValue("$firstSeen", profile.FirstSeenAt.UtcDateTime.Ticks);
        command.Parameters.AddWithValue("$updated", profile.UpdatedAt.UtcDateTime.Ticks);
        command.Parameters.AddWithValue("$revision", profile.Revision);
        command.ExecuteNonQuery();
        return profile;
    }

    private static AiRequestUsageRecord AiUsage(
        DateTimeOffset occurredAt,
        string provider,
        string origin,
        bool success,
        AiUsageMetrics usage,
        string requestedModel = "test-model",
        string? returnedModel = null) => new(
            Guid.NewGuid().ToString("N"),
            Guid.NewGuid().ToString("N"),
            occurredAt,
            occurredAt.AddSeconds(1),
            origin,
            "screen_analysis",
            provider,
            "api.example.test",
            requestedModel,
            returnedModel,
            null,
            null,
            success ? 200 : 503,
            100,
            null,
            1,
            100,
            256,
            usage,
            null,
            success,
            success ? null : "http_503");

    private static AiAnalysis AnalysisFor(AiRequestUsageRecord usage) => new(
        usage.OccurredAt,
        "test application",
        "test context",
        "test summary",
        "test-installation",
        null,
        null,
        usage.CorrelationId,
        usage.Origin);

    private static void WithStore(Action<LocalStore, ReportAggregationService> action)
    {
        var dataDirectory = CreateDataDirectory();
        try
        {
            var store = new LocalStore(dataDirectory);
            action(store, new ReportAggregationService(store));
        }
        finally
        {
            DeleteDataDirectory(dataDirectory);
        }
    }

    private static string CreateDataDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "WorkTrail.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void DeleteDataDirectory(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }
}
