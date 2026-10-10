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
using WorkTrail.Cli;
using Xunit;

namespace WorkTrail.Cli.Tests;

public sealed class CliCommandCatalogTests
{
    [Fact]
    public void LocalizationCatalog_CoversEveryCanonicalLocaleAndCommandHelpEntry()
    {
        Assert.Equal(
            ["en-US", "it-IT", "fr-FR", "de-DE", "es-ES", "zh-Hans", "vi-VN", "ko-KR", "pt-PT", "pt-BR"],
            CliStrings.SupportedLocales);

        foreach (var locale in CliStrings.SupportedLocales)
        {
            foreach (var key in CliStrings.Keys)
            {
                Assert.False(string.IsNullOrWhiteSpace(CliStrings.Get(locale, key)), $"{locale}:{key}");
            }

            foreach (var command in CliCommandCatalog.Commands)
            {
                Assert.Contains(command.SummaryKey, CliStrings.Keys);
                Assert.All(command.DetailKeys, key => Assert.Contains(key, CliStrings.Keys));
            }
        }

        Assert.Throws<KeyNotFoundException>(() => CliStrings.Get("en-US", "missing.localization.key"));
    }

    [Fact]
    public void PortugueseLocalesRemainDistinctAndChineseUsesSimplifiedCulture()
    {
        Assert.NotEqual(CliStrings.Get("pt-PT", "settings"), CliStrings.Get("pt-BR", "settings"));
        Assert.Equal("zh-CN", CliStrings.GetCulture("zh-Hans").Name);
    }

    [Theory]
    [InlineData("/status", "status")]
    [InlineData("/tracking", "tracking")]
    [InlineData("/?", "help")]
    public void Normalize_RemovesOnlyTheCommandSlash(string input, string expected)
    {
        var normalized = CliCommandCatalog.Normalize([input, "value/with/slash"]);

        Assert.Equal(expected, normalized[0]);
        Assert.Equal("value/with/slash", normalized[1]);
    }

    [Theory]
    [InlineData(new[] { "/help" }, null)]
    [InlineData(new[] { "/help", "/config" }, "config")]
    [InlineData(new[] { "/tracking", "--help" }, "tracking")]
    [InlineData(new[] { "/settings", "help" }, "settings")]
    public void TryGetHelpTopic_AcceptsGeneralAndCommandForms(string[] arguments, string? expectedTopic)
    {
        Assert.True(CliCommandCatalog.TryGetHelpTopic(arguments, out var topic));
        Assert.Equal(expectedTopic, topic);
    }

    [Fact]
    public void SettingsAlias_ResolvesToConfigHelp()
    {
        Assert.True(CliCommandCatalog.TryGet("settings", out var command));
        Assert.Equal("config", command!.Name);
    }

    [Fact]
    public void HelpWordAsOptionValue_IsNotTreatedAsCommandHelp()
    {
        Assert.False(CliCommandCatalog.TryGetHelpTopic(["focus", "start", "--objective", "help"], out _));
    }

    [Theory]
    [InlineData("/help", "/config", "unexpected")]
    [InlineData("/tracking", "--help", "unexpected")]
    public void TryGetHelpTopic_RejectsAmbiguousExtraArguments(params string[] arguments)
    {
        Assert.False(CliCommandCatalog.TryGetHelpTopic(arguments, out _));
    }

    [Theory]
    [InlineData("--status", "status")]
    [InlineData("--start", "tracking", "start")]
    [InlineData("--ai-on", "ai", "enable")]
    [InlineData("--ai-off", "ai", "disable")]
    public void TryExpandShortcut_MapsQuickSwitchToCanonicalCommand(string shortcut, params string[] expected)
    {
        Assert.True(CliCommandCatalog.TryExpandShortcut([shortcut], out var expanded));
        Assert.Equal(expected, expanded);
    }

    [Fact]
    public void TryExpandShortcut_AllowsOnlyHelpAfterQuickSwitch()
    {
        Assert.True(CliCommandCatalog.TryExpandShortcut(["--ai-on", "--help"], out var expanded));
        Assert.Equal(["ai", "enable", "--help"], expanded);
        Assert.True(CliCommandCatalog.TryGetHelpTopic(expanded, out var topic));
        Assert.Equal("ai", topic);
    }

    [Fact]
    public void TryExpandShortcut_RejectsAmbiguousArguments()
    {
        Assert.False(CliCommandCatalog.TryExpandShortcut(["--ai-on", "status"], out _));
    }
}
