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
using WorkTrail.Cli;
using Xunit;

namespace WorkTrail.Cli.Tests;

public sealed class CliOptionsTests
{
    [Fact]
    public void DefaultLanguage_FollowsTheSystemAndCanBeSelectedExplicitly()
    {
        Assert.Equal("system", CliOptions.Parse(["status"], redirected: false).Language);
        Assert.Equal("system", CliOptions.Parse(["--language", "system", "status"], redirected: false).Language);
    }

    [Fact]
    public void JsonMode_RetainsCommand()
    {
        var options = CliOptions.Parse(["--json", "--language", "pt-br", "status"], redirected: false);

        Assert.Equal(CliFormat.Json, options.Format);
        Assert.Equal("pt-BR", options.Language);
        Assert.Equal(["status"], options.CommandArguments);
    }

    [Theory]
    [InlineData("en")]
    [InlineData("it")]
    [InlineData("pt")]
    [InlineData("zh")]
    [InlineData("zh-CN")]
    [InlineData("pt_BR")]
    public void AmbiguousLegacyOrNonCanonicalLanguage_IsRejectedBeforeCommandDispatch(string language)
    {
        Assert.Throws<ArgumentException>(() => CliOptions.Parse(["--language", language, "status"], redirected: false));
    }

    [Fact]
    public void SupportedLanguages_AreTheCanonicalProductLocaleSet()
    {
        Assert.Equal(
            ["system", "en-US", "it-IT", "fr-FR", "de-DE", "es-ES", "zh-Hans", "vi-VN", "ko-KR", "pt-PT", "pt-BR"],
            CliOptions.SupportedLanguages);
    }

    [Theory]
    [InlineData("en-US")]
    [InlineData("it-IT")]
    [InlineData("fr-FR")]
    [InlineData("de-DE")]
    [InlineData("es-ES")]
    [InlineData("zh-Hans")]
    [InlineData("vi-VN")]
    [InlineData("ko-KR")]
    [InlineData("pt-PT")]
    [InlineData("pt-BR")]
    public void EveryCanonicalLocale_IsAcceptedWithoutChangingItsTag(string locale)
    {
        Assert.Equal(locale, CliOptions.Parse(["--language", locale, "status"], redirected: false).Language);
    }

    [Theory]
    [InlineData("--format")]
    [InlineData("--language")]
    [InlineData("--timeout")]
    public void GlobalOptionWithoutValue_IsRejected(string option)
    {
        Assert.Throws<ArgumentException>(() => CliOptions.Parse([option], redirected: false));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("301")]
    [InlineData("later")]
    public void InvalidTimeout_IsRejected(string value)
    {
        Assert.Throws<ArgumentException>(() => CliOptions.Parse(["--timeout", value, "status"], redirected: false));
    }

    [Theory]
    [InlineData("privacy.blocked", 5)]
    [InlineData("cli.premium.required", 11)]
    [InlineData("ai.disabled", 6)]
    [InlineData("operation.cancelled", 130)]
    public void ExitCodeMapper_UsesStableCodes(string code, int expected) => Assert.Equal(expected, ExitCodeMapper.Map(code));
}
