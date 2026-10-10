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
using Spectre.Console;
using Spectre.Console.Testing;
using WorkTrail.Application;
using WorkTrail.Cli;
using Xunit;

namespace WorkTrail.Cli.Tests;

public sealed class CliSettingsCatalogTests
{
    [Fact]
    public void PublicSettings_ExposeUiKeysWithoutInternalFields()
    {
        var keys = CliSettingsCatalog.Settings.Select(setting => setting.Key).ToArray();

        Assert.Contains("screenshots.enabled", keys);
        Assert.Contains("ai.provider", keys);
        Assert.Contains("ai.key_variable", keys);
        Assert.Contains("ai.output_detail", keys);
        Assert.Contains("ai.reasoning_effort", keys);
        Assert.DoesNotContain(keys, key => key.StartsWith("taskbar.widget.", StringComparison.Ordinal));
        Assert.Contains("window.titlebar.auto_hide", keys);
        Assert.DoesNotContain(keys, key => key.Contains("installation", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(keys, key => key.Contains("privacy", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ReadAll_UsesStablePublicKeysInsteadOfPropertyNames()
    {
        var values = CliSettingsCatalog.ReadAll(new AppSettings(Theme: "dark", ScreenshotsEnabled: true, AutoHideTitleBar: false));

        Assert.Equal("dark", Assert.Single(values, value => value.Key == "theme").Value);
        Assert.Equal(true, Assert.Single(values, value => value.Key == "screenshots.enabled").Value);
        Assert.Equal(false, Assert.Single(values, value => value.Key == "window.titlebar.auto_hide").Value);
        Assert.DoesNotContain(values, value => value.Key == nameof(AppSettings.InstallationId));
    }

    [Fact]
    public void RichSettingsRenderer_ShowsKeyAndCurrentValue()
    {
        var values = CliSettingsCatalog.ReadAll(new AppSettings(Theme: "dark"))
            .Where(value => value.Key == "theme")
            .ToArray();
        var options = new CliOptions(CliFormat.Rich, "en-US", false, false, 5, false, []);
        var console = new TestConsole();

        console.Write(new CliOutput(options).RenderSettings(values));

        Assert.Contains("theme", console.Output);
        Assert.Contains("dark", console.Output);
        Assert.Contains("system | light | dark", console.Output);
    }

    [Fact]
    public void GenericRichResult_RendersFacadeValueAndEscapesMarkup()
    {
        var options = new CliOptions(CliFormat.Rich, "en-US", false, false, 5, false, []);
        var result = OperationResult<object>.Success("test.loaded", "TestLoaded", new { state = "[ready]" });
        var console = new TestConsole();

        console.Write(new CliOutput(options).RenderResult(result));

        Assert.Contains("test.loaded", console.Output);
        Assert.Contains("[ready]", console.Output);
    }
}
