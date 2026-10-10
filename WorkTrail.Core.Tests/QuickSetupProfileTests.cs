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


using System.Text.Json;
using WorkTrail.Application;
using Xunit;

namespace WorkTrail.Core.Tests;

public sealed class QuickSetupProfileTests
{
    [Theory]
    [InlineData(QuickSetupProfileIds.Complete, true, true)]
    [InlineData(QuickSetupProfileIds.Assisted, true, false)]
    [InlineData(QuickSetupProfileIds.LocalRecord, false, true)]
    [InlineData(QuickSetupProfileIds.EssentialOffline, false, false)]
    public void CreatePatch_AppliesTheCompleteProfileAsOneValidatedTransaction(
        string profileId,
        bool aiEnabled,
        bool screenshotsEnabled)
    {
        var profile = QuickSetupProfileCatalog.CreatePatch(new QuickSetupProfileRequest(profileId, true));

        Assert.True(profile.Succeeded);
        var patch = Assert.IsType<SettingsPatch>(profile.Value);
        var result = SettingsCatalog.Apply(
            new AppSettings(
                OpenAiEnabled: !aiEnabled,
                ScreenshotsEnabled: !screenshotsEnabled,
                KeepScreenshots: !screenshotsEnabled),
            patch);

        Assert.True(result.Succeeded);
        var settings = Assert.IsType<AppSettings>(result.Value);
        Assert.Equal(aiEnabled, settings.OpenAiEnabled);
        Assert.Equal(screenshotsEnabled, settings.ScreenshotsEnabled);
        Assert.Equal(screenshotsEnabled, settings.KeepScreenshots);
        Assert.True(settings.StartWithWindows);
        Assert.True(settings.QuickSetupCompleted);
    }

    [Fact]
    public void CreatePatch_RejectsUnsupportedProfilesWithoutProducingSettings()
    {
        var result = QuickSetupProfileCatalog.CreatePatch(new QuickSetupProfileRequest("custom", false));

        Assert.False(result.Succeeded);
        Assert.Null(result.Value);
        Assert.Contains(result.Issues, issue => issue.Field == "profileId" && issue.Code == "unsupported");
    }

    [Fact]
    public void SettingsWithoutQuickSetupMarker_RequireFirstRunOnboarding()
    {
        var settings = JsonSerializer.Deserialize<AppSettings>("{}", new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.NotNull(settings);
        Assert.False(settings.QuickSetupCompleted);
        Assert.True(SettingsCatalog.TryGetValue(settings, "quick_setup.completed", out var completed));
        Assert.Equal(false, completed);
    }
}
