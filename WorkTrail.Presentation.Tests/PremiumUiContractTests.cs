// SPDX-License-Identifier: MIT

using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Xunit;

namespace WorkTrail.Presentation.Tests;

/// <summary>Checks the shared Premium affordances and requested navigation simplifications.</summary>
public sealed class PremiumUiContractTests
{
    /// <summary>Save-protected windows show a single badge in the title bar, not in their editable content.</summary>
    [Theory]
    [InlineData("ScheduleWindow.xaml")]
    [InlineData("ReportExportWindow.xaml")]
    [InlineData("MainWindow.xaml")]
    public void PremiumBadge_IsUniqueAndInTitleBar(string file)
    {
        var document = XDocument.Load(PathFor("WorkTrail", file));
        var badge = Assert.Single(document.Descendants(), element => element.Name.LocalName == "PremiumBadge");
        Assert.Equal("TitlePremiumBadge", Name(badge));
        Assert.Contains(badge.Ancestors(), element => Name(element)?.EndsWith("DragRegion", StringComparison.Ordinal) == true);
    }

    /// <summary>Labels stay left-aligned beside management, with no redundant history submenu.</summary>
    [Fact]
    public void MainWindow_KeepsLabelActionsTogetherAndHistoryActionsOnlyAtTopLevel()
    {
        var document = XDocument.Load(PathFor("WorkTrail", "MainWindow.xaml"));
        var actions = document.Descendants().Single(element => Name(element) == "PlayerLabelActionsPanel");
        Assert.Equal("Right", actions.Attribute("HorizontalAlignment")?.Value);
        Assert.Equal("Center", actions.Attribute("VerticalAlignment")?.Value);
        Assert.Contains(actions.Descendants(), element => Name(element) == "ManageLabelsButton"
            && element.Attribute("Grid.Row")?.Value == "1" && element.Attribute("HorizontalAlignment")?.Value == "Right");
        Assert.Contains(actions.Descendants(), element => Name(element) == "PlayerLabelFeatureGate" && element.Attribute("HorizontalAlignment")?.Value == "Right");
        Assert.DoesNotContain(actions.Descendants(), element => element.Name.LocalName == "PremiumBadge");
        Assert.DoesNotContain(document.Descendants(), element => Name(element) == "ActivityMenu");
        foreach (var name in new[] { "QuickSearchMenuItem", "QuickActivityCalendarMenuItem", "QuickScreenshotGalleryMenuItem" })
            Assert.Equal("MenuFlyout", document.Descendants().Single(element => Name(element) == name).Parent!.Name.LocalName);
    }

    /// <summary>The shared celestial menu keeps its navigation styling and the add-clock badge.</summary>
    [Fact]
    public void WorldClocks_UseSharedColoredNavigationMenuAndAnAddButtonBadge()
    {
        var windowSource = File.ReadAllText(PathFor("WorkTrail", "WorldClockWindow.xaml.cs"));
        var menuSource = File.ReadAllText(PathFor("WorkTrail", "Controls", "AstronomyWindowMenu.cs"));
        Assert.Contains("WorldMapButton.Flyout = AstronomyWindowMenu.Create", windowSource, StringComparison.Ordinal);
        Assert.Contains("new Setter(FrameworkElement.MinWidthProperty, 320d)", menuSource, StringComparison.Ordinal);
        Assert.Contains("new Setter(Control.CornerRadiusProperty, new CornerRadius(12))", menuSource, StringComparison.Ordinal);
        Assert.Contains("icon.Foreground = new SolidColorBrush(foreground)", menuSource, StringComparison.Ordinal);
        Assert.Equal(4, menuSource.Split("Color.FromArgb(", StringSplitOptions.None).Length - 1);
        Assert.Contains("AddItem(\"Window.Close\"", menuSource, StringComparison.Ordinal);
        var options = XDocument.Load(PathFor("WorkTrail", "Controls", "WorldClockOptionsControl.xaml"));
        var add = options.Descendants().Single(element => Name(element) == "AddClockButton");
        Assert.Contains(add.Parent!.Elements(), element => Name(element) == "AddClockPremiumBadge");
    }

    /// <summary>The OCR/AI action uses the same navigation control as the surrounding settings links.</summary>
    [Fact]
    public void OcrSettings_HaveNoDecorativeHeadingOrDuplicateBorder()
    {
        var document = XDocument.Load(PathFor("WorkTrail", "Controls", "OptionsControl.xaml"));
        Assert.DoesNotContain(document.Descendants(), element => element.Attribute("Tag")?.Value == "Options.Section.Ai");
        var action = document.Descendants().Single(element => Name(element) == "OcrAiSettingsButton");
        Assert.Equal("HyperlinkButton", action.Name.LocalName);
        Assert.DoesNotContain("OptionsPanoramaVioletBrush", document.ToString(), StringComparison.Ordinal);
    }

    /// <summary>The archive badge follows page navigation; Free execution invokes the shared upgrade dialog.</summary>
    [Fact]
    public void ProtectedActions_UseRuntimeAccessAndStandardDialogs()
    {
        var main = File.ReadAllText(PathFor("WorkTrail", "MainWindow.xaml.cs"));
        Assert.Contains("_operationsControl?.IsArchivePageVisible == true", main, StringComparison.Ordinal);
        Assert.Contains("saveResult.Code == \"feature.premium_required\"", main, StringComparison.Ordinal);
        var archives = File.ReadAllText(PathFor("WorkTrail", "Controls", "InstallationTransferOperationsControl.xaml.cs"));
        Assert.Equal(2, archives.Split("if (!await EnsureArchiveAccessAsync())", StringSplitOptions.None).Length - 1);
        Assert.Contains("ProductFeature.DataTransfer", archives, StringComparison.Ordinal);
        Assert.Contains("Context.Dialogs.ShowPremiumUpgradeAsync", archives, StringComparison.Ordinal);
        var labels = File.ReadAllText(PathFor("WorkTrail", "Controls", "ActivityLabelsEditor.cs"));
        Assert.Contains("result.Code == \"feature.label_limit\"", labels, StringComparison.Ordinal);
        Assert.Contains("button.BorderThickness = new Thickness(0);", labels, StringComparison.Ordinal);
        Assert.Contains("ToggleButtonBackground", labels, StringComparison.Ordinal);
    }

    /// <summary>Extended retention uses the same Premium control and exposes all three monthly choices.</summary>
    [Fact]
    public void Retention_ExtendedChoicesUseSharedPremiumBadge()
    {
        var document = XDocument.Load(PathFor("WorkTrail", "Controls", "RetentionOperationsControl.xaml"));
        foreach (var buttonName in new[] { "TwoMonthsButton", "ThreeMonthsButton" })
        {
            var button = document.Descendants().Single(element => Name(element) == buttonName);
            Assert.Single(button.Descendants(), element => element.Name.LocalName == "PremiumBadge");
        }
        var free = document.Descendants().Single(element => Name(element) == "OneMonthButton");
        Assert.DoesNotContain(free.Descendants(), element => element.Name.LocalName == "PremiumBadge");
        foreach (var name in new[] { "FirstActivationText", "LastCleanupText", "NextCleanupText" })
            Assert.Single(document.Descendants(), element => Name(element) == name);
    }

    /// <summary>The folder editor and keep-captures toggle have a single home on the retention page.</summary>
    [Fact]
    public void Retention_OwnsScreenshotStorageSettings()
    {
        var options = XDocument.Load(PathFor("WorkTrail", "Controls", "OptionsControl.xaml"));
        var retention = XDocument.Load(PathFor("WorkTrail", "Controls", "RetentionOperationsControl.xaml"));
        foreach (var controlName in new[] { "ScreenshotFolderBox", "KeepScreenshotsSwitch", "ScreenshotsEnabledSwitch", "ScreenshotModeBox", "OcrEnabledSwitch", "OcrLanguageBox" })
        {
            Assert.DoesNotContain(options.Descendants(), element => Name(element) == controlName);
            Assert.Single(retention.Descendants(), element => Name(element) == controlName);
        }
        var sensors = XDocument.Load(PathFor("WorkTrail", "Controls", "SensorOptionsControl.xaml"));
        Assert.DoesNotContain(sensors.Descendants(), element => Name(element) == "HardwareSaveSnapshotsSwitch");
        Assert.Single(retention.Descendants(), element => Name(element) == "HardwareSaveSnapshotsSwitch");
    }

    /// <summary>The v1 surface never advertises or instantiates the withdrawn taskbar widget.</summary>
    [Fact]
    public void TaskbarWidget_HasNoV1SettingsOrActivationPath()
    {
        var options = XDocument.Load(PathFor("WorkTrail", "Controls", "OptionsControl.xaml"));
        Assert.DoesNotContain(options.Descendants(), element => element.Attribute("Tag")?.Value?.StartsWith("Options.TaskbarWidget.", StringComparison.Ordinal) == true);
        var app = File.ReadAllText(PathFor("WorkTrail", "App.xaml.cs"));
        Assert.DoesNotContain("TaskbarWidgetSurface", app, StringComparison.Ordinal);
        Assert.DoesNotContain("ApplyTaskbarWidgetSettings", app, StringComparison.Ordinal);
    }

    /// <summary>Monthly cleanup is queued only after the workspace and restored windows are ready.</summary>
    [Fact]
    public void RetentionStartup_WaitsForWorkspaceAndRestoration()
    {
        var app = File.ReadAllText(PathFor("WorkTrail", "App.xaml.cs"));
        var ready = app.IndexOf("await _window.WaitForWorkspaceReadyAsync();", StringComparison.Ordinal);
        var restored = app.IndexOf("await RestoreWorkspaceAsync(application, previousSettings);", StringComparison.Ordinal);
        var maintenance = app.IndexOf("StartRetentionMaintenance();", StringComparison.Ordinal);
        Assert.True(ready >= 0 && ready < restored && restored < maintenance);
        Assert.Contains("DispatcherQueuePriority.Low, () => _ = CheckScheduledRetentionAsync()", app, StringComparison.Ordinal);
        Assert.Contains("Scheduled: true, OperationId: operationId", app, StringComparison.Ordinal);
    }

    private static string? Name(XElement element) => element.Attributes().FirstOrDefault(attribute => attribute.Name.LocalName == "Name")?.Value;

    private static string PathFor(params string[] segments)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "WorkTrail.slnx")))
                return Path.Combine(new[] { directory.FullName }.Concat(segments).ToArray());
        throw new DirectoryNotFoundException("Could not locate the WorkTrail repository root.");
    }
}
