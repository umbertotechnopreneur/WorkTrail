// SPDX-License-Identifier: MIT

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
    [InlineData(ProductTier.Premium, 3650, 3)]
    public void EffectiveMonths_EnforcesTierAndMaximum(ProductTier tier, int days, int expected) =>
        Assert.Equal(expected, RetentionPolicy.EffectiveMonths(days, tier));

    [Theory]
    [InlineData(2026, 1, 31, 2026, 2, 28)]
    [InlineData(2028, 1, 31, 2028, 2, 29)]
    [InlineData(2026, 12, 15, 2027, 1, 15)]
    public void FirstCleanup_UsesFollowingCalendarMonth(int y, int m, int d, int nextY, int nextM, int nextD) =>
        Assert.Equal(new DateOnly(nextY, nextM, nextD), RetentionPolicy.NextCleanupDate(new DateOnly(y, m, d), null));

    [Fact]
    public void ShortMonth_DoesNotShiftSubsequentAnniversaries() =>
        Assert.Equal(new DateOnly(2026, 3, 31), RetentionPolicy.NextCleanupDate(new DateOnly(2026, 1, 31), new DateOnly(2026, 2, 28)));

    [Fact]
    public void MissedCleanup_AdvancesFromInstallationDayAfterCatchUp() =>
        Assert.Equal(new DateOnly(2026, 10, 12), RetentionPolicy.NextCleanupDate(new DateOnly(2026, 5, 12), new DateOnly(2026, 10, 2)));

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
    [InlineData("120")]
    public void Settings_RejectUnsupportedRetentionChoices(string value) =>
        Assert.False(SettingsCatalog.Apply(new AppSettings(), new SettingsPatch(new Dictionary<string, string?> { ["retention.data_days"] = value })).Succeeded);
}
