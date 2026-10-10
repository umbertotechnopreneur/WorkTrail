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
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using WorkTrail.Application;
using WorkTrail.Services;
using Xunit;

namespace WorkTrail.Core.Tests;

[Collection(ProcessEnvironmentCollection.Name)]
public sealed class AiEnablementGuardTests
{
    private const string TestKeyVariable = "OPENAI_API_KEY";
    private const string PlausibleTestKey = "sk-test-only-key-1234567890";

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("sk-short")]
    [InlineData("not-an-openai-key-1234567890")]
    [InlineData("sk-test key-1234567890")]
    [InlineData(" sk-test-only-key-1234567890")]
    [InlineData("sk-test-only-key-1234567890 ")]
    [InlineData("sk-or-v1-test-only-1234567890")]
    [InlineData("sk-ant-test-only-1234567890")]
    public void OpenAiKeyPolicy_RejectsValuesThatDoNotLookLikeOpenAiKeys(string? value)
    {
        Assert.False(AiApiKeyPolicy.LooksLikeOpenAiApiKey(value));
    }

    [Theory]
    [InlineData("sk-proj-test-only-1234567890")]
    [InlineData("sk-admin-test-only-1234567890")]
    [InlineData(PlausibleTestKey)]
    public void OpenAiKeyPolicy_AcceptsRecognizableTestShapes(string value)
    {
        Assert.True(AiApiKeyPolicy.LooksLikeOpenAiApiKey(value));
    }

    [Fact]
    public void ProviderPolicy_RequiresTheMatchingEnvironmentVariable()
    {
        Assert.True(AiApiKeyPolicy.LooksPlausible("openai", "OPENAI_API_KEY", PlausibleTestKey));
        Assert.True(AiApiKeyPolicy.LooksPlausible("openai", "WORKTRAIL_OPENAI_APIKEY", PlausibleTestKey));
        Assert.False(AiApiKeyPolicy.LooksPlausible("openai", "OPENROUTER_API_KEY", PlausibleTestKey));
        Assert.False(AiApiKeyPolicy.LooksPlausible("openrouter", "OPENAI_API_KEY", PlausibleTestKey));
    }

    [Fact]
    public async Task EnablePaths_RequirePlausibleConfiguredKey()
    {
        var previousKey = Environment.GetEnvironmentVariable(TestKeyVariable, EnvironmentVariableTarget.Process);
        var dataDirectory = CreateTemporaryDirectory();
        try
        {
            Environment.SetEnvironmentVariable(
                TestKeyVariable,
                "not-an-openai-key-1234567890",
                EnvironmentVariableTarget.Process);
            var store = new LocalStore(dataDirectory);
            store.SaveSettings(store.LoadSettings() with
            {
                AiProvider = "openai",
                AiApiKeyName = TestKeyVariable,
                OpenAiEnabled = false
            });

            var utilities = new UtilityService();
            await using var application = new WorkTrailApplication(
                store,
                utilities,
                new TrackingDomainService(store),
                new ScreenCaptureService(utilities.GetAppVersion()),
                new FakeHardwareTelemetryService(),
                new OpenAiAnalysisService(store, new ScreenCaptureService(utilities.GetAppVersion())),
                new StartupService(),
                new BuildInformationService());

            var invalidStatus = await application.GetAiStatusAsync(CancellationToken.None);
            var directWithoutKey = await application.SetAiEnabledAsync(true, CancellationToken.None);
            var patchWithoutKey = await application.PatchSettingsAsync(
                new SettingsPatch(new Dictionary<string, string?> { ["ai.enabled"] = "true" }),
                CancellationToken.None);

            Assert.True(invalidStatus.Succeeded);
            Assert.True(invalidStatus.Value?.HasKey);
            Assert.False(invalidStatus.Value?.CanEnable);
            Assert.False(directWithoutKey.Succeeded);
            Assert.False(patchWithoutKey.Succeeded);
            Assert.False(store.LoadSettings().OpenAiEnabled);

            Environment.SetEnvironmentVariable(TestKeyVariable, PlausibleTestKey, EnvironmentVariableTarget.Process);
            var readyStatus = await application.GetAiStatusAsync(CancellationToken.None);
            var enabled = await application.SetAiEnabledAsync(true, CancellationToken.None);

            Assert.True(readyStatus.Value?.HasKey);
            Assert.True(readyStatus.Value?.CanEnable);
            var unverifiedProfile = await application.ApplyQuickSetupProfileAsync(
                new QuickSetupProfileRequest(QuickSetupProfileIds.Assisted, false), CancellationToken.None);
            Assert.False(unverifiedProfile.Succeeded);
            Assert.Equal("quick_setup.ai.verification_required", unverifiedProfile.Code);
            Assert.False(store.LoadSettings().QuickSetupCompleted);
            Assert.True(enabled.Succeeded);
            Assert.True(enabled.Value?.Enabled);
            Assert.True(store.LoadSettings().OpenAiEnabled);

            Environment.SetEnvironmentVariable(
                TestKeyVariable,
                "not-an-openai-key-1234567890",
                EnvironmentVariableTarget.Process);
            var enabledWithoutUsableKey = await application.GetAiStatusAsync(CancellationToken.None);
            var disabled = await application.SetAiEnabledAsync(false, CancellationToken.None);

            Assert.True(enabledWithoutUsableKey.Value?.Enabled);
            Assert.False(enabledWithoutUsableKey.Value?.CanEnable);
            Assert.True(disabled.Succeeded);
            Assert.False(disabled.Value?.Enabled);
            Assert.False(store.LoadSettings().OpenAiEnabled);
        }
        finally
        {
            Environment.SetEnvironmentVariable(TestKeyVariable, previousKey, EnvironmentVariableTarget.Process);
            await DeleteTemporaryDirectoryAsync(dataDirectory);
        }
    }

    private static string CreateTemporaryDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "WorkTrail.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static async Task DeleteTemporaryDirectoryAsync(string path)
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                Directory.Delete(path, recursive: true);
                return;
            }
            catch (IOException) when (attempt < 4)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(50));
            }
        }
    }
}
