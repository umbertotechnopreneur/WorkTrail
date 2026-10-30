// SPDX-License-Identifier: MIT
using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Xunit;

namespace WorkTrail.Presentation.Tests;

/// <summary>Protects the launcher's visible destinations and passive weather credential boundary.</summary>
public sealed class LauncherSurfaceContractTests
{
    #region Implementation

    /// <summary>The launcher exposes every retained desktop tool through a stable selector.</summary>
    [Fact]
    public void Launcher_HasTheSevenDesktopDestinations()
    {
        var document = XDocument.Load(RepositoryFile("WorkTrail/MainWindow.xaml"));
        var identifiers = document.Descendants().Attributes("AutomationProperties.AutomationId").Select(item => item.Value).ToArray();
        foreach (var key in new[] { "Clocks", "Planner", "Calendar", "Sky", "Moon", "Map", "Globe" })
        {
            Assert.Contains("Launcher." + key, identifiers);
        }

        Assert.DoesNotContain(identifiers, id => id.Contains("Screenshot", StringComparison.Ordinal)
            || id.Contains("Tracking", StringComparison.Ordinal) || id.Contains("Timesheet", StringComparison.Ordinal));
    }

    /// <summary>Each destination has a readable title, a localized description, and its packaged watercolor instead of a duplicate icon.</summary>
    [Theory]
    [InlineData("Clocks", "world-clocks-v1.png")]
    [InlineData("Map", "day-night-v1.png")]
    [InlineData("Calendar", "calendar-v1.png")]
    [InlineData("Sky", "local-sky-v1.png")]
    [InlineData("Globe", "globe-v1.png")]
    [InlineData("Moon", "moon-v1.png")]
    public void Launcher_DestinationsHaveVisibleTitlesDescriptionsAndWatercolors(string key, string artworkFile)
    {
        var document = XDocument.Load(RepositoryFile("WorkTrail/MainWindow.xaml"));

        Assert.DoesNotContain(document.Descendants(), node => node.Name.LocalName is "Flyout" or "MenuFlyout");

        var button = Assert.Single(document.Descendants(), node => node.Attribute("AutomationProperties.AutomationId")?.Value == "Launcher." + key);
        var caption = Assert.Single(button.Descendants(), node => node.Name.LocalName == "TextBlock" && node.Attribute("Tag")?.Value == "Launcher." + key);
        var description = Assert.Single(button.Descendants(), node => node.Name.LocalName == "TextBlock" && node.Attribute("Tag")?.Value == "Launcher." + key + "Description");

        Assert.DoesNotContain(button.Descendants(), node => node.Name.LocalName == "FontIcon");
        Assert.False(string.IsNullOrWhiteSpace(caption.Attribute("Text")?.Value));
        Assert.False(string.IsNullOrWhiteSpace(description.Attribute("Text")?.Value));
        Assert.Equal("SemiBold", caption.Attribute("FontWeight")?.Value);
        Assert.True(double.Parse(caption.Attribute("FontSize")!.Value, CultureInfo.InvariantCulture)
            > double.Parse(description.Attribute("FontSize")!.Value, CultureInfo.InvariantCulture));
        Assert.Equal("NoWrap", description.Attribute("TextWrapping")?.Value);
        Assert.Equal("CharacterEllipsis", description.Attribute("TextTrimming")?.Value);

        var artwork = Assert.Single(button.Parent!.Elements(), node => node.Name.LocalName == "Rectangle");
        var brush = Assert.Single(artwork.Descendants(), node => node.Name.LocalName == "LauncherArtworkBrush");

        Assert.Equal("{StaticResource LauncherCornerArtStyle}", artwork.Attribute("Style")?.Value);
        Assert.Equal("ms-appx:///Assets/Launcher/" + artworkFile, brush.Attribute("Source")?.Value);
        Assert.True(File.Exists(RepositoryFile("WorkTrail/Assets/Launcher/" + artworkFile)));
        Assert.Equal("LauncherTile_PointerEntered", button.Attribute("PointerEntered")?.Value);
        Assert.Equal("LauncherTile_PointerExited", button.Attribute("PointerExited")?.Value);
        Assert.Equal("LauncherTile_PointerExited", button.Attribute("PointerCanceled")?.Value);
    }

    /// <summary>Artwork overhang remains inside the scrollable content extent while cards can stretch in both dimensions.</summary>
    [Fact]
    public void Launcher_ReservesArtworkOverhangInsideItsResponsiveScrollingContent()
    {
        var document = XDocument.Load(RepositoryFile("WorkTrail/MainWindow.xaml"));

        Assert.DoesNotContain(document.Descendants().Attributes("Tag"), tag => tag.Value is "WorldTime.Heading" or "WorldTime.Description" or "Launcher.Description");

        var scroller = Assert.Single(document.Descendants(), node => node.Name.LocalName == "ScrollViewer");

        Assert.Equal("Auto", scroller.Attribute("VerticalScrollBarVisibility")?.Value);
        Assert.Equal("Disabled", scroller.Attribute("HorizontalScrollBarVisibility")?.Value);
        Assert.Equal("Disabled", scroller.Attribute("HorizontalScrollMode")?.Value);
        Assert.Null(scroller.Attribute("Padding"));

        var body = Assert.Single(scroller.Elements(), node => node.Name.LocalName == "StackPanel");
        var artworkStyle = NamedResource(document, "LauncherCornerArtStyle");
        var bodyPadding = body.Attribute("Padding")!.Value.Split(',')
            .Select(value => double.Parse(value, CultureInfo.InvariantCulture)).ToArray();
        var artworkMargin = SetterValue(artworkStyle, "Margin").Split(',')
            .Select(value => double.Parse(value, CultureInfo.InvariantCulture)).ToArray();

        Assert.Equal(4, bodyPadding.Length);
        Assert.Equal(4, artworkMargin.Length);
        for (var edge = 0; edge < 4; edge++)
        {
            Assert.True(bodyPadding[edge] >= Math.Max(0, -artworkMargin[edge]),
                $"Content padding must contain artwork overhang at edge {edge}.");
        }

        Assert.Equal("False", SetterValue(artworkStyle, "IsHitTestVisible"));
        Assert.Equal("Raw", SetterValue(artworkStyle, "AutomationProperties.AccessibilityView"));
        Assert.Equal("LauncherViewport_SizeChanged", scroller.Attribute("SizeChanged")?.Value);
        Assert.Equal("LauncherViewport_SizeChanged", body.Attribute("SizeChanged")?.Value);

        var tileStyle = NamedResource(document, "LauncherTileStyle");

        Assert.Equal("Stretch", SetterValue(tileStyle, "HorizontalAlignment"));
        Assert.Equal("Stretch", SetterValue(tileStyle, "VerticalAlignment"));
        Assert.Equal("Stretch", SetterValue(tileStyle, "HorizontalContentAlignment"));
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

    /// <summary>Finds one shared XAML resource by its declared key.</summary>
    private static XElement NamedResource(XDocument document, string key) =>
        Assert.Single(document.Descendants(), node => node.Attributes().Any(attribute => attribute.Name.LocalName == "Key" && attribute.Value == key));

    /// <summary>Reads one explicit style setter so the layout contract fails when the property disappears.</summary>
    private static string SetterValue(XElement style, string property) =>
        Assert.Single(style.Elements(), node => node.Name.LocalName == "Setter" && node.Attribute("Property")?.Value == property)
            .Attribute("Value")!.Value;

    /// <summary>Finds a checked-in fixture by walking upward from the test output to the repository root.</summary>
    private static string RepositoryFile(string relative)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "WorkTrail.slnx")))
        {
            directory = directory.Parent;
        }

        return Path.Combine(directory?.FullName ?? throw new DirectoryNotFoundException("Repository root was not found."), relative);
    }

    #endregion
}
