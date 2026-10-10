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
using System.Linq;
using WorkTrail.Application;
using WorkTrail.Services;
using Xunit;

namespace WorkTrail.Core.Tests;

/// <summary>Checks tier enforcement independently of a frontend and persisted settings.</summary>
public sealed class FeatureAccessPolicyTests
{
    private static readonly ActivityLabelDefinition Work = new("123456781234123412341234567890ab", "Work", "work", "#FF6268");
    private static SettingsPatch Patch(string key, string value) => new(new Dictionary<string, string?> { [key] = value });

    /// <summary>Label editing and selection are available in Free; creation uses the separate quota guard.</summary>
    [Theory]
    [InlineData("activity.label.save")]
    [InlineData("activity.label.delete")]
    [InlineData("activity.label.select")]
    [InlineData(" ACTIVITY.LABEL.SELECT ")]
    public void Free_AllowsLabelCommandsRegardlessOfFrontend(string key)
    {
        var policy = new FeatureAccessPolicy();
        var denied = policy.DeniedSetting(Patch(key, Work.Id), new AppSettings(ActivityLabels: [Work]));
        Assert.Null(denied);
    }

    /// <summary>Free allows both saved-label and one-off taskbar selection.</summary>
    [Fact]
    public void Free_CanSelectSavedLabelThroughTheTaskbarTextRoute()
    {
        var policy = new FeatureAccessPolicy();
        var settings = new AppSettings(ActivityLabels: [Work]);
        Assert.Null(policy.DeniedSetting(Patch("activity.span_label", " work "), settings));
        Assert.Null(policy.DeniedSetting(Patch("activity.span_label", "One-off"), settings));
        Assert.Null(policy.DeniedSetting(Patch("activity.label.select", ""), settings));
        Assert.Null(policy.DeniedSetting(Patch("theme", "dark"), settings));
    }

    /// <summary>Labels are Free while file export and the CLI retain their Premium classification.</summary>
    [Fact]
    public void Catalog_LabelsAreFreeAndOtherPremiumFeaturesStayProtected()
    {
        var policy = new FeatureAccessPolicy(new License(ProductTier.Premium));
        Assert.Null(policy.DeniedSetting(Patch("activity.label.save", "{}"), new AppSettings()));
        Assert.Equal(ProductTier.Free, FeatureCatalog.Get(ProductFeature.ActivityLabels).RequiredTier);
        Assert.Equal(ProductTier.Premium, FeatureCatalog.Get(ProductFeature.ReportExport).RequiredTier);
        Assert.Equal(ProductTier.Premium, FeatureCatalog.Get(ProductFeature.DataTransfer).RequiredTier);
        Assert.Equal(ProductTier.Premium, FeatureCatalog.Get(ProductFeature.ScreenshotSchedule).RequiredTier);
        Assert.Equal(ProductTier.Premium, FeatureCatalog.Get(ProductFeature.Cli).RequiredTier);
        Assert.False(policy.Snapshot.IsDebugSimulation);
    }

    /// <summary>Every schedule field is protected while ordinary screenshot settings remain Free.</summary>
    [Fact]
    public void Free_DeniesEveryScheduleFieldButAllowsScreenshots()
    {
        var free = new FeatureAccessPolicy();
        var premium = new FeatureAccessPolicy(new License(ProductTier.Premium));
        foreach (var key in FeatureCatalog.Get(ProductFeature.ScreenshotSchedule).SettingKeys)
        {
            Assert.Equal("premium_required", free.DeniedSetting(Patch(" " + key.ToUpperInvariant() + " ", ""), new AppSettings())!.Code);
            Assert.Null(premium.DeniedSetting(Patch(key, ""), new AppSettings()));
        }
        Assert.True(FeatureCatalog.IsAllowed(ProductFeature.Screenshots, free.Snapshot));
    }

    /// <summary>Free permits three labels and preserves over-limit catalogs while preventing further growth.</summary>
    [Theory]
    [InlineData(0, 1, false)]
    [InlineData(2, 3, false)]
    [InlineData(3, 4, true)]
    [InlineData(4, 4, false)]
    [InlineData(4, 5, true)]
    [InlineData(4, 3, false)]
    [InlineData(4, 0, false)]
    public void Free_EnforcesCreationQuotaWithoutBlockingEditsOrDeletion(int before, int after, bool denied)
    {
        var previous = new AppSettings(ActivityLabels: Enumerable.Range(0, before).Select(i => Work with { Id = Guid.NewGuid().ToString("N"), Name = "Label " + i }).ToArray());
        var updated = previous with { ActivityLabels = Enumerable.Range(0, after).Select(i => Work with { Id = Guid.NewGuid().ToString("N"), Name = "Label " + i }).ToArray() };
        var issue = new FeatureAccessPolicy().DeniedSettingsChange(previous, updated);
        Assert.Equal(denied, issue is not null);
        if (denied) Assert.Equal("Labels.FreeLimit", issue!.MessageKey);
        Assert.Null(new FeatureAccessPolicy(new License(ProductTier.Premium)).DeniedSettingsChange(previous, updated));
    }

    /// <summary>An invalid verified-source result fails closed.</summary>
    [Fact]
    public void UnknownLicenseTier_IsRejectedRatherThanGrantedAccess()
    {
        var policy = new FeatureAccessPolicy(new License((ProductTier)42));
        Assert.Throws<InvalidOperationException>(() => policy.Snapshot);
    }

#if DEBUG
    /// <summary>Simulation never survives creation of a fresh runtime policy.</summary>
    [Fact]
    public void DebugOverride_IsRuntimeLocalAndCanSwitchBackToFree()
    {
        var policy = new FeatureAccessPolicy();
        policy.Simulate(ProductTier.Premium);
        Assert.True(policy.Snapshot.IsDebugSimulation);
        Assert.Equal(ProductTier.Premium, policy.Snapshot.Tier);
        Assert.Equal(ProductTier.Free, new FeatureAccessPolicy().Snapshot.Tier);
        policy.Simulate(ProductTier.Free);
        Assert.Equal(ProductTier.Free, policy.Snapshot.Tier);
        Assert.Null(policy.DeniedSetting(Patch("activity.label.delete", Work.Id), new AppSettings(ActivityLabels: [Work])));
        Assert.Throws<ArgumentOutOfRangeException>(() => policy.Simulate((ProductTier)42));
    }
#else
    /// <summary>Non-Debug assemblies contain no callable simulation method.</summary>
    [Fact]
    public void NonDebug_HasNoSimulationEntryPoint()
    {
        Assert.False(new FeatureAccessPolicy().Snapshot.CanSimulate);
        Assert.Null(typeof(IWorkTrailApplication).GetMethod("SimulateFeatureAccessAsync"));
        Assert.Null(typeof(FeatureAccessPolicy).GetMethod("Simulate"));
    }
#endif

    private sealed class License(ProductTier tier) : IFeatureLicenseSource
    {
        /// <inheritdoc />
        public ProductTier Tier => tier;
    }
}
