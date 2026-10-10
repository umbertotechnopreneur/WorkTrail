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
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Xml.Linq;
using Xunit;

namespace WorkTrail.Presentation.Tests;

/// <summary>Prevents blank export commands and missing captions in the supported application languages.</summary>
public sealed class ReportExportLocalizationContractTests
{
    /// <summary>Text commands expose either string content or a tagged text child, including hidden actions.</summary>
    [Fact]
    public void EveryExportButton_DeclaresVisibleTextForLocalization()
    {
        var window = XDocument.Load(RepositoryFile("WorkTrail", "ReportExportWindow.xaml"));
        var buttons = window.Descendants().Where(element => element.Name.LocalName == "Button").ToArray();
        Assert.Contains(buttons, button => button.Attribute("Click")?.Value == "ExcelPreview_Click");
        Assert.Single(buttons, button => button.Attribute("Tag")?.Value == "Export.MoreInformation"
            && button.Attribute("Click")?.Value == "SummaryInfo_Click");
        Assert.All(buttons, button =>
        {
            Assert.False(string.IsNullOrWhiteSpace(button.Attribute("Tag")?.Value));
            var key = button.Attribute("Tag")!.Value;
            var hasTextContent = !string.IsNullOrWhiteSpace(button.Attribute("Content")?.Value);
            var hasTaggedCaption = button.Descendants().Any(element => element.Name.LocalName == "TextBlock"
                && element.Attribute("Tag")?.Value == key && !string.IsNullOrWhiteSpace(element.Attribute("Text")?.Value));
            Assert.True(hasTextContent || hasTaggedCaption,
                $"{key} needs string content or a tagged text child so UiLocalization can set its visible caption.");
        });
    }

    /// <summary>Primary actions and date shortcuts use decorative theme-aware icons.</summary>
    [Fact]
    public void PrimaryActions_UseDecorativeThemeAwareIconsAlongsideText()
    {
        var window = XDocument.Load(RepositoryFile("WorkTrail", "ReportExportWindow.xaml"));
        var buttons = window.Descendants().Where(element => element.Name.LocalName == "Button").ToArray();
        var primaryTags = new[] { "Export.Generate", "Export.SavePreset", "Export.Action", "Export.Today", "Export.Week", "Export.Month",
            "Timesheet.Submit", "Timesheet.PreviousPage", "Timesheet.NextPage", "Timesheet.Refresh", "Timesheet.Cancel", "Timesheet.Export" };
        foreach (var button in buttons)
        {
            var icons = button.Descendants().Where(element => element.Name.LocalName == "FontIcon").ToArray();
            if (!primaryTags.Contains(button.Attribute("Tag")!.Value))
            {
                Assert.Empty(icons);
                continue;
            }
            var icon = Assert.Single(icons);
            Assert.Equal("Raw", icon.Attribute("AutomationProperties.AccessibilityView")?.Value);
            Assert.StartsWith("{ThemeResource ", icon.Attribute("Foreground")!.Value);
            Assert.Equal("Segoe Fluent Icons", icon.Attribute("FontFamily")?.Value);
        }
        Assert.Equal(primaryTags.Length, buttons.Sum(button => button.Descendants().Count(element => element.Name.LocalName == "FontIcon")));
        Assert.All(window.Descendants().Where(element => element.Name.LocalName == "FontIcon"), icon =>
        {
            Assert.Equal("Raw", icon.Attribute("AutomationProperties.AccessibilityView")?.Value);
            Assert.StartsWith("{ThemeResource ", icon.Attribute("Foreground")!.Value);
            Assert.Equal("Segoe Fluent Icons", icon.Attribute("FontFamily")?.Value);
        });
    }

    /// <summary>Every tagged export caption or field header has nonempty text in each supported locale.</summary>
    [Theory]
    [InlineData("en-US")]
    [InlineData("it-IT")]
    [InlineData("de-DE")]
    [InlineData("es-ES")]
    [InlineData("fr-FR")]
    [InlineData("ko-KR")]
    [InlineData("pt-BR")]
    [InlineData("pt-PT")]
    [InlineData("vi-VN")]
    [InlineData("zh-Hans")]
    public void ExportCaptions_ArePresentInEveryLanguage(string locale)
    {
        var window = XDocument.Load(RepositoryFile("WorkTrail", "ReportExportWindow.xaml"));
        using var strings = JsonDocument.Parse(File.ReadAllText(RepositoryFile("WorkTrail.Core", "Localization", locale + ".json")));
        foreach (var element in window.Descendants().Where(element => element.Attribute("Tag") is not null))
        {
            var key = element.Attribute("Tag")!.Value;
            if (element.Name.LocalName is "ComboBox" or "TextBox") key += ".Header";
            Assert.True(strings.RootElement.TryGetProperty(key, out var text), $"{locale} is missing {key}.");
            Assert.False(string.IsNullOrWhiteSpace(text.GetString()), $"{locale} has an empty {key} caption.");
        }
    }

    private static string RepositoryFile(params string[] segments)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "WorkTrail.slnx")))
                return Path.Combine(new[] { directory.FullName }.Concat(segments).ToArray());
        throw new DirectoryNotFoundException("Could not locate the WorkTrail repository root.");
    }
}
