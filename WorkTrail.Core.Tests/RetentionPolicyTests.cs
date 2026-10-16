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
using WorkTrail.Application;
using Xunit;

namespace WorkTrail.Core.Tests;

public sealed class RetentionPolicyTests
{
    [Theory]
    [InlineData(ProductTier.Free, 90, 1)]
    [InlineData(ProductTier.Premium, 30, 1)]
    [InlineData(ProductTier.Premium, 60, 2)]
    [InlineData(ProductTier.Premium, 90, 3)]
    [InlineData(ProductTier.Premium, 360, 12)]
    [InlineData(ProductTier.Premium, 3650, 12)]
    [InlineData(ProductTier.Free, 360, 1)]
    public void EffectiveMonths_EnforcesTierAndMaximum(ProductTier tier, int days, int expected) =>
        Assert.Equal(expected, RetentionPolicy.EffectiveMonths(days, tier));

    /// <summary>Expired imports are eligible on the first ready day.</summary>
    [Fact]
    public void FirstCleanup_IsDueOnTheInstallationDay() =>
        Assert.Equal(new DateOnly(2026, 1, 31), RetentionPolicy.NextCleanupDate(new DateOnly(2026, 1, 31), null));

    /// <summary>Daily maintenance crosses month, leap-day and year boundaries.</summary>
    [Theory]
    [InlineData(2026, 2, 28, 2026, 3, 1)]
    [InlineData(2028, 2, 28, 2028, 2, 29)]
    [InlineData(2026, 12, 31, 2027, 1, 1)]
    public void SuccessfulCleanup_SchedulesTheFollowingLocalDay(int y, int m, int d, int nextY, int nextM, int nextD) =>
        Assert.Equal(new DateOnly(nextY, nextM, nextD), RetentionPolicy.NextCleanupDate(new DateOnly(2026, 1, 1), new DateOnly(y, m, d)));

    [Theory]
    [InlineData("retention.data_days")]
    [InlineData("retention.screenshots_days")]
    public void Free_CannotExtendRetentionThroughSettings(string key)
    {
        var policy = new FeatureAccessPolicy();
        Assert.NotNull(policy.DeniedSetting(new SettingsPatch(new Dictionary<string, string?> { [key] = "90" }), new AppSettings()));
        Assert.Null(policy.DeniedSetting(new SettingsPatch(new Dictionary<string, string?> { [key] = "30" }), new AppSettings()));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("31")]
    [InlineData("390")]
    public void Settings_RejectUnsupportedRetentionChoices(string value) =>
        Assert.False(SettingsCatalog.Apply(new AppSettings(), new SettingsPatch(new Dictionary<string, string?> { ["retention.data_days"] = value })).Succeeded);
    /// <summary>All supported extended retention selections can be persisted.</summary>
    [Theory]
    [InlineData("120")]
    [InlineData("180")]
    [InlineData("360")]
    public void Settings_AcceptPremiumCalendarMonthsThroughOneYear(string value) =>
        Assert.True(SettingsCatalog.Apply(new AppSettings(), new SettingsPatch(new Dictionary<string, string?> { ["retention.data_days"] = value })).Succeeded);

    /// <summary>The storage warning starts above one GB and follows its saved threshold.</summary>
    [Fact]
    public void StorageWarning_DefaultsToOneGigabyteAndUsesTheSavedThreshold()
    {
        Assert.Equal(1024, new AppSettings().HistoryWarningMegabytes);
        var status = new RetentionStatus(30, 30, "", StorageBytes: 1024L * 1024 * 1024);
        Assert.False(status.IsStorageWarning);
        Assert.True((status with { StorageBytes = status.StorageBytes + 1 }).IsStorageWarning);
        Assert.False((status with { HistoryWarningMegabytes = 2048 }).IsStorageWarning);
        var result = SettingsCatalog.Apply(new AppSettings(), new SettingsPatch(new Dictionary<string, string?> { ["retention.warning_mb"] = "2048" }));
        Assert.True(result.Succeeded);
        Assert.Equal(2048, result.Value!.HistoryWarningMegabytes);
        Assert.False(SettingsCatalog.Apply(new AppSettings(), new SettingsPatch(new Dictionary<string, string?> { ["retention.warning_mb"] = "0" })).Succeeded);
    }
    /// <summary>Invalid ranges travel to application validation without evaluating an overflowing boundary.</summary>
    [Fact]
    public void DateRange_TransportSerializesOnlyTheSelectedDates()
    {
        var range = new HistoryDateRange(new DateOnly(2026, 8, 10), DateOnly.MaxValue);
        var json = System.Text.Json.JsonSerializer.Serialize(range);
        Assert.DoesNotContain("EndExclusive", json);
        Assert.DoesNotContain("Start", json);
        var restored = System.Text.Json.JsonSerializer.Deserialize<HistoryDateRange>(json);
        Assert.Equal(range, restored);
        Assert.False(restored!.IsValid);
    }
}
