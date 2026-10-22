// SPDX-License-Identifier: MIT
using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Xunit;

namespace WorkTrail.Presentation.Tests;

/// <summary>Protects the launcher's visible destinations and passive weather credential boundary.</summary>
public sealed class LauncherSurfaceContractTests
{
    /// <summary>The launcher exposes every retained desktop tool through a stable selector.</summary>
    [Fact]
    public void Launcher_HasTheSixDesktopDestinations()
    {
        var document = XDocument.Load(RepositoryFile("WorkTrail/MainWindow.xaml"));
        var identifiers = document.Descendants().Attributes("AutomationProperties.AutomationId").Select(item => item.Value).ToArray();
        foreach (var key in new[] { "Clocks", "Calendar", "Sky", "Moon", "Map", "Globe" })
            Assert.Contains("Launcher." + key, identifiers);
        Assert.DoesNotContain(identifiers, id => id.Contains("Screenshot", StringComparison.Ordinal)
            || id.Contains("Tracking", StringComparison.Ordinal) || id.Contains("Timesheet", StringComparison.Ordinal));
    }

    /// <summary>Each destination has a visible caption and the recovered panel icon without opening a popup.</summary>
    [Fact]
    public void Launcher_DestinationsHaveVisibleCaptionsAndIconsInTheWindow()
    {
        var document = XDocument.Load(RepositoryFile("WorkTrail/MainWindow.xaml"));
        Assert.DoesNotContain(document.Descendants(), node => node.Name.LocalName is "Flyout" or "MenuFlyout");
        foreach (var key in new[] { "Clocks", "Calendar", "Sky", "Moon", "Map", "Globe" })
        {
            var button = Assert.Single(document.Descendants(), node => node.Attribute("AutomationProperties.AutomationId")?.Value == "Launcher." + key);
            var icon = Assert.Single(button.Descendants(), node => node.Name.LocalName == "FontIcon");
            Assert.False(string.IsNullOrWhiteSpace(icon.Attribute("Glyph")?.Value));
            var caption = Assert.Single(button.Descendants(), node => node.Name.LocalName == "TextBlock" && node.Attribute("Tag")?.Value == "Launcher." + key);
            Assert.False(string.IsNullOrWhiteSpace(caption.Attribute("Text")?.Value));
        }
    }

    /// <summary>The launcher has distinct icon colors and no introductory copy or visible page scrollbar.</summary>
    [Fact]
    public void Launcher_IsACompactColoredToolPanel()
    {
        var document = XDocument.Load(RepositoryFile("WorkTrail/MainWindow.xaml"));
        Assert.DoesNotContain(document.Descendants().Attributes("Tag"), tag => tag.Value is "WorldTime.Heading" or "WorldTime.Description" or "Launcher.Description");
        var scroller = Assert.Single(document.Descendants(), node => node.Name.LocalName == "ScrollViewer");
        Assert.Equal("Hidden", scroller.Attribute("VerticalScrollBarVisibility")?.Value);
        var icons = document.Descendants().Where(node => node.Name.LocalName == "FontIcon" && node.Parent?.Parent?.Name.LocalName == "Button").ToArray();
        Assert.Equal(6, icons.Select(icon => icon.Attribute("Foreground")?.Value).Distinct().Count());
    }

    /// <summary>Command captions are present in XAML before localization or template realization can run.</summary>
    [Theory]
    [InlineData("WorkTrail/MainWindow.xaml")]
    [InlineData("WorkTrail/Controls/WeatherKeySetupControl.xaml")]
    public void NewSurfaces_DoNotRelyOnAccessibleNamesForVisibleButtonCaptions(string path)
    {
        var document = XDocument.Load(RepositoryFile(path));
        foreach (var button in document.Descendants().Where(node => node.Name.LocalName is "Button" or "HyperlinkButton"))
        {
            var hasCaption = !string.IsNullOrWhiteSpace(button.Attribute("Content")?.Value)
                || button.Descendants().Any(node => node.Name.LocalName == "TextBlock" && !string.IsNullOrWhiteSpace(node.Attribute("Text")?.Value));
            Assert.True(hasCaption, $"{path}: {button.Attribute("AutomationProperties.AutomationId")?.Value} needs a visible caption.");
        }
    }

    /// <summary>The weather editor masks secrets and delegates verification without direct network or environment access.</summary>
    [Fact]
    public void WeatherEditor_CollectsSecretsWithoutOwningProviderCalls()
    {
        var document = XDocument.Load(RepositoryFile("WorkTrail/Controls/WeatherKeySetupControl.xaml"));
        var password = Assert.Single(document.Descendants(), node => node.Name.LocalName == "PasswordBox");
        Assert.Equal("Hidden", password.Attribute("PasswordRevealMode")?.Value);
        Assert.Null(password.Attribute("Password"));
        var source = File.ReadAllText(RepositoryFile("WorkTrail/Controls/WeatherKeySetupControl.xaml.cs"));
        Assert.Contains("_application.SetWorldClockWeatherKeyAsync", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Environment.", source, StringComparison.Ordinal);
        Assert.DoesNotContain("HttpClient", source, StringComparison.Ordinal);
        Assert.DoesNotContain("SetAiKey", source, StringComparison.Ordinal);
    }

    private static string RepositoryFile(string relative)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "WorkTrail.slnx"))) directory = directory.Parent;
        return Path.Combine(directory?.FullName ?? throw new DirectoryNotFoundException("Repository root was not found."), relative);
    }
}
