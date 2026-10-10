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
using System.Linq;
using System.Text.Json;
using WorkTrail.Application;
using WorkTrail.Services;
using Xunit;

namespace WorkTrail.Core.Tests;

public sealed class HardwareSettingsTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>Uses the same safe sensor defaults for constructed settings and omitted JSON properties.</summary>
    [Fact]
    public void Defaults_EnableStandardSensorsAndSavingWithTheNormalProfile()
    {
        var deserialized = Assert.IsType<AppSettings>(JsonSerializer.Deserialize<AppSettings>("{}", Json));
        foreach (var settings in new[] { new AppSettings(), deserialized })
        {
            Assert.True(settings.HardwareSensorsEnabled);
            Assert.False(settings.HardwareUseAdvancedSensors);
            Assert.True(settings.HardwareSaveSnapshots);
            Assert.Equal("normal", settings.HardwareSamplingProfile);
        }

        var descriptors = SettingsCatalog.Definitions.Where(item => item.Key.StartsWith("sensors.", StringComparison.Ordinal)).ToArray();
        Assert.Equal(4, descriptors.Length);
        Assert.All(descriptors, descriptor => Assert.False(descriptor.RequiresRestart));
        Assert.All(descriptors.Where(descriptor => descriptor.Key != "sensors.sampling_profile"), descriptor =>
        {
            Assert.Equal("boolean", descriptor.ValueType);
            Assert.Equal(new[] { "true", "false" }, descriptor.AllowedValues);
        });
        var profile = Assert.Single(descriptors, descriptor => descriptor.Key == "sensors.sampling_profile");
        Assert.Equal("choice", profile.ValueType);
        Assert.Equal(HardwareSamplingProfiles.All.Select(item => item.Key), profile.AllowedValues);
        Assert.Equal(new[] { "slow", "normal", "fast", "fastest" }, profile.AllowedValues);
    }

    /// <summary>Round-trips every independent boolean combination without clearing disabled subordinate preferences.</summary>
    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, false, true)]
    [InlineData(false, true, false)]
    [InlineData(false, true, true)]
    [InlineData(true, false, false)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    [InlineData(true, true, true)]
    public void Preferences_RoundTripIndependentlyThroughCatalogJsonAndLocalStore(bool enabled, bool advanced, bool save)
    {
        var dataDirectory = Path.Combine(Path.GetTempPath(), "WorkTrail.Tests", Guid.NewGuid().ToString("N"));
        try
        {
            var store = new LocalStore(dataDirectory);
            var result = SettingsCatalog.Apply(store.LoadSettings(), new SettingsPatch(new Dictionary<string, string?>
            {
                ["sensors.enabled"] = enabled ? "true" : "false",
                ["sensors.advanced"] = advanced ? "true" : "false",
                ["sensors.save_snapshots"] = save ? "true" : "false",
                ["sensors.sampling_profile"] = "fastest"
            }));
            Assert.True(result.Succeeded);
            var settings = Assert.IsType<AppSettings>(result.Value);
            store.SaveSettings(settings);
            var restored = new LocalStore(dataDirectory).LoadSettings();
            var jsonSettings = Assert.IsType<AppSettings>(JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(settings, Json), Json));

            foreach (var candidate in new[] { settings, restored, jsonSettings })
            {
                Assert.Equal(enabled, candidate.HardwareSensorsEnabled);
                Assert.Equal(advanced, candidate.HardwareUseAdvancedSensors);
                Assert.Equal(save, candidate.HardwareSaveSnapshots);
                Assert.Equal("fastest", candidate.HardwareSamplingProfile);
                Assert.True(SettingsCatalog.TryGetValue(candidate, "sensors.enabled", out var enabledValue));
                Assert.Equal(enabled, Assert.IsType<bool>(enabledValue));
                Assert.True(SettingsCatalog.TryGetValue(candidate, "sensors.advanced", out var advancedValue));
                Assert.Equal(advanced, Assert.IsType<bool>(advancedValue));
                Assert.True(SettingsCatalog.TryGetValue(candidate, "sensors.save_snapshots", out var saveValue));
                Assert.Equal(save, Assert.IsType<bool>(saveValue));
                Assert.True(SettingsCatalog.TryGetValue(candidate, "sensors.sampling_profile", out var profileValue));
                Assert.Equal("fastest", Assert.IsType<string>(profileValue));
            }

            var toggled = SettingsCatalog.Apply(restored, new SettingsPatch(new Dictionary<string, string?>
            {
                ["sensors.enabled"] = enabled ? "false" : "true",
                ["theme"] = "dark"
            }));
            Assert.True(toggled.Succeeded);
            Assert.Equal(!enabled, toggled.Value!.HardwareSensorsEnabled);
            Assert.Equal(advanced, toggled.Value.HardwareUseAdvancedSensors);
            Assert.Equal(save, toggled.Value.HardwareSaveSnapshots);
            Assert.Equal("fastest", toggled.Value.HardwareSamplingProfile);
        }
        finally
        {
            if (Directory.Exists(dataDirectory))
            {
                Directory.Delete(dataDirectory, recursive: true);
            }
        }
    }

    /// <summary>Canonicalizes only supported profile identifiers before the strict runtime catalog is called.</summary>
    [Theory]
    [InlineData("slow")]
    [InlineData("normal")]
    [InlineData("fast")]
    [InlineData("fastest")]
    public void SamplingProfiles_AcceptOnlyCatalogChoicesAndRoundTrip(string profile)
    {
        var result = SettingsCatalog.Apply(new AppSettings(), new SettingsPatch(new Dictionary<string, string?>
        {
            ["sensors.sampling_profile"] = $" {profile.ToUpperInvariant()} "
        }));
        Assert.True(result.Succeeded);
        Assert.Equal(profile, result.Value!.HardwareSamplingProfile);
        var normalized = SettingsCatalog.NormalizePersisted(
            new AppSettings(HardwareSamplingProfile: $" {profile.ToUpperInvariant()} "), Path.GetTempPath());
        Assert.Equal(profile, normalized.HardwareSamplingProfile);
        Assert.Equal(profile, HardwareSamplingProfiles.Get(normalized.HardwareSamplingProfile).Key);
        var restored = Assert.IsType<AppSettings>(JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(result.Value, Json), Json));
        Assert.Equal(profile, restored.HardwareSamplingProfile);
    }

    /// <summary>Rejects one invalid sensor field without exposing any partial settings update.</summary>
    [Theory]
    [InlineData("sensors.enabled", "yes")]
    [InlineData("sensors.advanced", "1")]
    [InlineData("sensors.save_snapshots", null)]
    [InlineData("sensors.sampling_profile", null)]
    [InlineData("sensors.sampling_profile", "")]
    [InlineData("sensors.sampling_profile", "turbo")]
    public void Apply_RejectsInvalidSensorSettingsAtomically(string key, string? value)
    {
        var original = new AppSettings();
        var patch = new Dictionary<string, string?>
        {
            ["sensors.enabled"] = "false",
            ["sensors.advanced"] = "true",
            ["sensors.save_snapshots"] = "false",
            ["sensors.sampling_profile"] = "fast",
            ["theme"] = "dark"
        };
        patch[key] = value;
        var result = SettingsCatalog.Apply(original, new SettingsPatch(patch));

        Assert.False(result.Succeeded);
        Assert.Null(result.Value);
        Assert.Contains(result.Issues, issue => issue.Field == key);
        Assert.Equal(new AppSettings(), original);
    }

    /// <summary>Invalid stored profiles fail fast even when sensor collection is currently disabled.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("turbo")]
    [InlineData("0.5s")]
    public void NormalizePersisted_RejectsUnknownSamplingProfiles(string? profile)
    {
        var exception = Assert.Throws<InvalidDataException>(() => SettingsCatalog.NormalizePersisted(
            new AppSettings(HardwareSensorsEnabled: false, HardwareSamplingProfile: profile!), Path.GetTempPath()));

        Assert.Contains("sensors.sampling_profile", exception.Message, StringComparison.Ordinal);
    }
}
