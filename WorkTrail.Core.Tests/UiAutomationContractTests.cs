// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using WorkTrail.Services;
using Xunit;

namespace WorkTrail.Core.Tests;

/// <summary>Protects stable selectors and translated accessible names on converted UI surfaces.</summary>
public sealed class UiAutomationContractTests
{
    private static readonly string[] ConvertedSurfaces =
    [
        "WorkTrail/MainWindow.xaml",
        "WorkTrail/ReportExportWindow.xaml",
        "WorkTrail/Controls/OptionsControl.xaml",
        "WorkTrail/Controls/RetentionOperationsControl.xaml",
        "WorkTrail/SensorsWindow.xaml",
        "WorkTrail/Controls/ScreenshotHeaderControl.xaml",
        "WorkTrail/Controls/ScreenshotDetailsControl.xaml",
        "WorkTrail/Controls/ScreenshotTimelineControl.xaml",
        "WorkTrail/Controls/SensorOptionsControl.xaml",
        "WorkTrail/Controls/ProviderKeySetupControl.xaml",
        "WorkTrail/Controls/WeeklyHoursEditor.xaml",
        "WorkTrail/Controls/OperationsControl.xaml"
    ];

    private static readonly HashSet<string> InteractiveControls = new(StringComparer.Ordinal)
    {
        "Button", "HyperlinkButton", "ToggleButton", "ToggleSwitch", "CheckBox",
        "ComboBox", "TextBox", "PasswordBox", "NumberBox", "Slider", "CalendarDatePicker",
        "DatePicker", "NavigationViewItem", "ListView", "GridView", "MenuFlyoutItem",
        "ToggleMenuFlyoutItem", "MenuFlyoutSubItem", "AutoSuggestBox", "Expander", "Thumb",
        "AppBarButton", "AppBarToggleButton"
    };

    /// <summary>Every converted static interactive control has an explicit selector and a registered name key.</summary>
    [Fact]
    public void StaticInteractiveControls_HaveRegisteredUniqueSelectors()
    {
        var identifiers = new HashSet<string>(StringComparer.Ordinal);
        foreach (var surface in ConvertedSurfaces)
        {
            var document = XDocument.Load(RepositoryFile(surface));
            foreach (var control in document.Descendants().Where(element =>
                InteractiveControls.Contains(element.Name.LocalName)
                && !element.Ancestors().Any(parent => parent.Name.LocalName is "DataTemplate" or "ControlTemplate")))
            {
                var identifier = control.Attribute("AutomationProperties.AutomationId")?.Value;
                Assert.False(string.IsNullOrWhiteSpace(identifier), $"{surface}: {control.Name.LocalName} needs a stable automation ID.");
                Assert.True(identifiers.Add(identifier!), $"Duplicate selector: {identifier}.");
                Assert.True(LocalizationService.AutomationNameKeys.ContainsKey(identifier!), $"{identifier} needs a registered accessible name.");
                Assert.Null(control.Attribute("AutomationProperties.Name"));
            }
        }

        Assert.Equal(LocalizationService.AutomationNameKeys.Keys.OrderBy(key => key), identifiers.OrderBy(key => key));
    }

    /// <summary>Every registered control name resolves through each shipped language catalog.</summary>
    [Fact]
    public void RegisteredNames_ResolveInEverySupportedLanguage()
    {
        foreach (var language in LocalizationService.SupportedLanguages)
        {
            var strings = new LocalizationService(language);
            foreach (var (identifier, key) in LocalizationService.AutomationNameKeys)
            {
                Assert.True(strings.TryTranslateAutomationName(identifier, out var name));
                Assert.False(string.IsNullOrWhiteSpace(name));
                Assert.Equal(strings.Translate(key), name);
                Assert.DoesNotContain("{", name);
            }
        }
    }

    /// <summary>Accessible names follow the chosen language while selectors stay unchanged.</summary>
    [Fact]
    public void NameResolution_UsesTheSelectedLanguage()
    {
        var english = new LocalizationService("en-US");
        var italian = new LocalizationService("it-IT");
        Assert.True(english.TryTranslateAutomationName("Report.Export", out var englishName));
        Assert.True(italian.TryTranslateAutomationName("Report.Export", out var italianName));
        Assert.Equal(english.Translate("Export.Action"), englishName);
        Assert.Equal(italian.Translate("Export.Action"), italianName);
        Assert.NotEqual(englishName, italianName);
    }

    /// <summary>Unregistered external controls keep their own names rather than receiving guessed labels.</summary>
    [Fact]
    public void UnregisteredIdentifiers_DoNotProduceNames()
    {
        var strings = new LocalizationService("en-US");
        Assert.False(strings.TryTranslateAutomationName("External.Control", out var name));
        Assert.Equal(string.Empty, name);
        Assert.Throws<ArgumentException>(() => strings.TryTranslateAutomationName(" ", out _));
    }

    /// <summary>Locates checked-in UI declarations without depending on the test runner working directory.</summary>
    /// <param name="relativePath">The UI declaration relative to the repository root.</param>
    private static string RepositoryFile(string relativePath)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "WorkTrail.slnx")))
            {
                return Path.Combine(directory.FullName, relativePath);
            }
        }

        throw new DirectoryNotFoundException("Could not locate the WorkTrail repository root.");
    }
}
