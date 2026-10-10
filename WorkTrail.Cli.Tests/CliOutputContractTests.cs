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
using System.Text.Json;
using Spectre.Console;
using Spectre.Console.Testing;
using WorkTrail.Application;
using WorkTrail.Cli;
using WorkTrail.Services;
using Xunit;

namespace WorkTrail.Cli.Tests;

public sealed class CliOutputContractTests
{
    [Fact]
    public void HardwareSnapshot_RendersBatteryAndEscapesSensorNamesWithoutSummingPower()
    {
        var output = new CliOutput(new CliOptions(CliFormat.Rich, "en-US", false, false, 5, false, []));
        var timestamp = new DateTimeOffset(2026, 9, 12, 10, 30, 0, TimeSpan.Zero);
        var snapshot = new SystemSnapshot(timestamp, "partial",
        [
            new("/battery/0", "Battery [test]", "Battery", timestamp,
            [
                new("/battery/0/power/0", "Discharge Rate", "Power", "W", 8.4),
                new("/battery/0/energy/0", "Remaining Capacity", "Energy", "mWh", 43000),
                new("/battery/0/temp/0", "Temperature", "Temperature", "°C", null)
            ])
        ]);
        var console = new TestConsole();
        console.Profile.Width = 180;

        console.Write(output.RenderSystemSnapshot(snapshot));

        Assert.Contains("Battery [test]", console.Output, StringComparison.Ordinal);
        Assert.Contains("8.4 W", console.Output, StringComparison.Ordinal);
        Assert.Contains("43000 mWh", console.Output, StringComparison.Ordinal);
        Assert.Contains("partial", console.Output, StringComparison.Ordinal);
        Assert.Contains("not-installed", console.Output, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("en-US", "Windows language", "System", "Light", "Dark")]
    [InlineData("it-IT", "Lingua di Windows", "Sistema", "Chiaro", "Scuro")]
    [InlineData("fr-FR", "Langue de Windows", "Système", "Clair", "Sombre")]
    [InlineData("de-DE", "Windows-Sprache", "System", "Hell", "Dunkel")]
    [InlineData("es-ES", "Idioma de Windows", "Sistema", "Claro", "Oscuro")]
    [InlineData("zh-Hans", "Windows 语言", "系统", "浅色", "深色")]
    [InlineData("vi-VN", "Ngôn ngữ Windows", "Hệ thống", "Sáng", "Tối")]
    [InlineData("ko-KR", "Windows 언어", "시스템", "밝게", "어둡게")]
    [InlineData("pt-PT", "Idioma do Windows", "Sistema", "Claro", "Escuro")]
    [InlineData("pt-BR", "Idioma do Windows", "Sistema", "Claro", "Escuro")]
    public void WizardChoices_LocalizeLabelsAndPreserveContractValues(
        string locale,
        string languageSystem,
        string themeSystem,
        string themeLight,
        string themeDark)
    {
        var output = new CliOutput(new CliOptions(
            CliFormat.Plain,
            locale,
            false,
            false,
            5,
            false,
            []));

        var languages = CliRouter.LanguageWizardChoices(output);
        var themes = CliRouter.ThemeWizardChoices(output);

        Assert.Equal(CliOptions.SupportedLanguages, languages.Select(choice => choice.Value));
        Assert.Equal(languageSystem, languages[0].Label);
        Assert.Equal(["system", "light", "dark"], themes.Select(choice => choice.Value));
        Assert.Equal([themeSystem, themeLight, themeDark], themes.Select(choice => choice.Label));
    }

    [Theory]
    [InlineData("en-US", "Operation completed.", "The operation could not be completed.")]
    [InlineData("it-IT", "Operazione completata.", "Non è stato possibile completare l’operazione.")]
    [InlineData("zh-Hans", "操作已完成。", "无法完成操作。")]
    [InlineData("pt-BR", "Operação concluída.", "Não foi possível concluir a operação.")]
    public void HumanResultFallback_NeverPrintsAnUntranslatedMessageKey(
        string locale,
        string expectedSuccess,
        string expectedFailure)
    {
        var output = new CliOutput(new CliOptions(
            CliFormat.Plain,
            locale,
            false,
            false,
            5,
            false,
            []));

        Assert.Equal(expectedSuccess, output.ResultText("UnknownSuccessMessageKey", succeeded: true));
        Assert.Equal(expectedFailure, output.ResultText("UnknownFailureMessageKey", succeeded: false));
    }

    [Fact]
    public void JsonResultEnvelope_KeepsStableEnglishFieldNamesAndCodes()
    {
        var result = OperationResult<object>.Failure(
            "command.arguments.invalid",
            "CommandInvalid",
            new ValidationIssue("language", "unsupported", "CommandInvalid"));

        using var document = JsonDocument.Parse(CliOutput.SerializeResult(result));

        Assert.Equal(["succeeded", "code", "messageKey", "value", "issues"],
            document.RootElement.EnumerateObject().Select(property => property.Name));
        Assert.Equal("command.arguments.invalid", document.RootElement.GetProperty("code").GetString());
        Assert.Equal("CommandInvalid", document.RootElement.GetProperty("messageKey").GetString());
        Assert.Equal("language", document.RootElement.GetProperty("issues")[0].GetProperty("field").GetString());
    }
}
