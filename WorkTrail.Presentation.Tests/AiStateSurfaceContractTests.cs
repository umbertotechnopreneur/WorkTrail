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
using System.Xml.Linq;
using Xunit;

namespace WorkTrail.Presentation.Tests;

public sealed class AiStateSurfaceContractTests
{
    [Fact]
    public void MenuAndOptions_BindToOneSharedAiState()
    {
        var main = XDocument.Load(RepositoryFile("WorkTrail", "MainWindow.xaml"));
        var options = XDocument.Load(RepositoryFile("WorkTrail", "Controls", "OptionsControl.xaml"));
        var mainSource = File.ReadAllText(RepositoryFile("WorkTrail", "MainWindow.xaml.cs"));
        var optionsSource = File.ReadAllText(RepositoryFile("WorkTrail", "Controls", "OptionsControl.xaml.cs"));
        var menuToggle = main.Descendants().Single(element => HasName(element, "OpenAiMenuToggle"));
        var optionsToggle = options.Descendants().Single(element => HasName(element, "OpenAiEnabledSwitch"));
        var statusText = options.Descendants().Single(element => HasName(element, "ApiKeyStatusText"));
        var statusIcon = options.Descendants().Single(element => HasName(element, "ApiKeyStatusIcon"));

        Assert.Equal("{x:Bind AiState.Enabled, Mode=OneWay}", menuToggle.Attribute("IsChecked")?.Value);
        Assert.Equal("{x:Bind AiState.CanToggle, Mode=OneWay}", menuToggle.Attribute("IsEnabled")?.Value);
        Assert.Equal("{Binding Enabled, Mode=OneWay}", optionsToggle.Attribute("IsOn")?.Value);
        Assert.Equal("{Binding CanToggle, Mode=OneWay}", optionsToggle.Attribute("IsEnabled")?.Value);
        Assert.Equal("Polite", statusText.Attributes().Single(attribute => attribute.Name.LocalName == "AutomationProperties.LiveSetting").Value);
        Assert.Equal("Raw", statusIcon.Attributes().Single(attribute => attribute.Name.LocalName == "AutomationProperties.AccessibilityView").Value);
        Assert.Contains("AiState = new AiApplicationState(application);", mainSource, StringComparison.Ordinal);
        Assert.Contains("options.InitializeAsync(_application, AiState, _lifecycle.Token)", mainSource, StringComparison.Ordinal);
        Assert.Contains("DataContext = aiState;", optionsSource, StringComparison.Ordinal);
        Assert.Contains("UpdateApiKeyPresentation();", optionsSource, StringComparison.Ordinal);
        Assert.DoesNotContain("[\"ai.enabled\"]", optionsSource, StringComparison.Ordinal);
        Assert.DoesNotContain("Environment.", optionsSource, StringComparison.Ordinal);
    }

    [Fact]
    public void AiOptions_ShowAccessibleDailyDescriptionQuotaFromFacadeDtos()
    {
        var options = XDocument.Load(RepositoryFile("WorkTrail", "Controls", "OptionsControl.xaml"));
        var optionsSource = File.ReadAllText(RepositoryFile("WorkTrail", "Controls", "OptionsControl.xaml.cs"));
        var stateSource = File.ReadAllText(RepositoryFile("WorkTrail.Presentation", "AiApplicationState.cs"));
        var quotaPanel = options.Descendants().Single(element => HasName(element, "AiQuotaPanel"));
        var quotaTitle = options.Descendants().Single(element => HasName(element, "AiQuotaTitleText"));
        var quotaUsage = options.Descendants().Single(element => HasName(element, "AiQuotaUsageText"));
        var quotaProgress = options.Descendants().Single(element => HasName(element, "AiQuotaProgressBar"));
        var quotaDescription = options.Descendants().Single(element => HasName(element, "AiQuotaDescriptionText"));
        var quotaExpander = options.Descendants().Single(element => HasName(element, "AiDailyLimitExpander"));
        var quotaLimit = options.Descendants().Single(element => HasName(element, "AiDailyLimitBox"));

        Assert.Equal("Border", quotaPanel.Name.LocalName);
        Assert.Equal("Polite", quotaPanel.Attributes().Single(attribute => attribute.Name.LocalName == "AutomationProperties.LiveSetting").Value);
        Assert.Equal("Options.AiQuota.Title", quotaTitle.Attribute("Tag")?.Value);
        Assert.Equal("Polite", quotaUsage.Attributes().Single(attribute => attribute.Name.LocalName == "AutomationProperties.LiveSetting").Value);
        Assert.Equal("ProgressBar", quotaProgress.Name.LocalName);
        Assert.Equal("Options.AiQuota.Description", quotaDescription.Attribute("Tag")?.Value);
        Assert.Equal("Expander", quotaExpander.Name.LocalName);
        Assert.Equal("Options.AiQuota.Configure", quotaExpander.Descendants().Single(element => HasName(element, "AiDailyLimitActionText")).Attribute("Tag")?.Value);
        Assert.Equal("NumberBox", quotaLimit.Name.LocalName);
        Assert.Equal("0", quotaLimit.Attribute("Minimum")?.Value);
        Assert.Equal("400", quotaLimit.Attribute("Maximum")?.Value);
        Assert.Equal("20", quotaLimit.Attribute("Value")?.Value);
        Assert.DoesNotContain(options.Descendants(), element => HasName(element, "SaveAiDailyLimitButton"));
        Assert.Contains("_openAiDailyLimit = settings.OpenAiDailyLimit;", optionsSource, StringComparison.Ordinal);
        Assert.Contains("QueueAutoSave(", optionsSource, StringComparison.Ordinal);
        Assert.Contains("\"ai.daily_limit\"", optionsSource, StringComparison.Ordinal);
        Assert.Contains("AiDailyLimitBox.ValueChanged +=", optionsSource, StringComparison.Ordinal);
        Assert.Contains("QueueAiDailyLimitSave();", optionsSource, StringComparison.Ordinal);
        Assert.Contains("SettingsCatalog.MaximumAiDailyLimit", optionsSource, StringComparison.Ordinal);
        Assert.Contains("costGate.DailyAnalysisCount", optionsSource, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.SetName(AiQuotaProgressBar", optionsSource, StringComparison.Ordinal);
        Assert.Contains("nameof(AiApplicationState.CostGate)", optionsSource, StringComparison.Ordinal);
        Assert.Contains("_application.GetAiStatusAsync(cancellationToken)", stateSource, StringComparison.Ordinal);
        Assert.DoesNotContain("SqliteActivityStore", optionsSource, StringComparison.Ordinal);
        Assert.DoesNotContain("CountAiAnalysisResults", optionsSource, StringComparison.Ordinal);
    }

    private static string RepositoryFile(params string[] pathSegments)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "WorkTrail.slnx")))
            {
                return Path.Combine(new[] { directory.FullName }.Concat(pathSegments).ToArray());
            }
        }

        throw new DirectoryNotFoundException("Could not locate the WorkTrail repository root.");
    }

    private static bool HasName(XElement element, string name) =>
        element.Attributes().Any(attribute => attribute.Name.LocalName == "Name" && attribute.Value == name);
}
