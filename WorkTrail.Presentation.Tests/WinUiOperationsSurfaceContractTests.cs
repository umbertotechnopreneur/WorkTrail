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

/// <summary>Guards complete, passive access to operational use cases from the WinUI frontend.</summary>
public sealed class WinUiOperationsSurfaceContractTests
{
    /// <summary>Guards the passive About diagnostics surface and its shared-runtime route.</summary>
    [Fact]
    public void AboutDiagnostics_UseFacadeRuntimeAndRedactedShareInfrastructure()
    {
        var about = File.ReadAllText(RepositoryFile("WorkTrail", "AboutWindow.xaml.cs"));
        var runtimeOperations = File.ReadAllText(RepositoryFile("WorkTrail.Core", "Runtime", "RuntimeOperationCatalog.cs"));
        var logs = File.ReadAllText(RepositoryFile("WorkTrail.Core", "Infrastructure", "Services", "ApplicationLogService.cs"));

        Assert.Contains("_application.OpenApplicationLogFolderAsync", about, StringComparison.Ordinal);
        Assert.Contains("_application.ShareApplicationLogAsync", about, StringComparison.Ordinal);
        Assert.Contains("_application.OpenProductLinkAsync", about, StringComparison.Ordinal);
        Assert.DoesNotContain("System.IO", about, StringComparison.Ordinal);
        Assert.DoesNotContain("Process.", about, StringComparison.Ordinal);
        Assert.Contains("\"diagnostics.log.open\"", runtimeOperations, StringComparison.Ordinal);
        Assert.Contains("\"diagnostics.log.open_folder\"", runtimeOperations, StringComparison.Ordinal);
        Assert.Contains("\"diagnostics.log.share\"", runtimeOperations, StringComparison.Ordinal);
        Assert.Contains("\"product.link.open\"", runtimeOperations, StringComparison.Ordinal);
        Assert.Contains("MaximumSharedSourceBytes", logs, StringComparison.Ordinal);
        Assert.Contains("CreateRedactedExport", logs, StringComparison.Ordinal);
        Assert.Contains("RedactForSharing", logs, StringComparison.Ordinal);
        Assert.Contains("OpenLogDirectory", logs, StringComparison.Ordinal);
    }

    /// <summary>Ensures operational tools are separate, reachable from settings, and usable at narrow widths.</summary>
    [Fact]
    public void OperationsSurface_UsesFocusedPagesAndSettingsNavigation()
    {
        var mainWindow = XDocument.Load(RepositoryFile("WorkTrail", "MainWindow.xaml"));
        var options = XDocument.Load(RepositoryFile("WorkTrail", "Controls", "OptionsControl.xaml"));
        var operations = XDocument.Load(RepositoryFile("WorkTrail", "Controls", "OperationsControl.xaml"));
        var snapshots = XDocument.Load(RepositoryFile("WorkTrail", "Controls", "SnapshotAiOperationsControl.xaml"));
        var snapshotSource = File.ReadAllText(RepositoryFile("WorkTrail", "Controls", "SnapshotAiOperationsControl.xaml.cs"));
        var privacy = XDocument.Load(RepositoryFile("WorkTrail", "Controls", "PrivacyOperationsControl.xaml"));
        var retention = XDocument.Load(RepositoryFile("WorkTrail", "Controls", "RetentionOperationsControl.xaml"));
        var plugins = XDocument.Load(RepositoryFile("WorkTrail", "Controls", "PluginOperationsControl.xaml"));
        var installationTransfer = XDocument.Load(RepositoryFile("WorkTrail", "Controls", "InstallationTransferOperationsControl.xaml"));

        Assert.Contains(mainWindow.Descendants(), element => element.Name.LocalName == "ContentPresenter" && HasName(element, "OperationsHost"));
        Assert.DoesNotContain(mainWindow.Descendants(), element => element.Name.LocalName == "OperationsControl");
        Assert.Contains(operations.Descendants(), element => element.Name.LocalName == "ScrollViewer");
        Assert.DoesNotContain(operations.Descendants(), element => element.Name.LocalName == "TimedInfoBar");
        Assert.DoesNotContain(operations.Descendants(), element => element.Name.LocalName == "ToggleButton" && element.Attribute("Tag")?.Value.StartsWith("Operations.Section.", StringComparison.Ordinal) == true);
        Assert.DoesNotContain(operations.Descendants(), element => HasName(element, "RuntimeCapabilitiesList"));
        Assert.DoesNotContain(operations.Descendants(), element => HasName(element, "SystemHardwareSensorsList"));

        var settingsLinks = options.Descendants()
            .Where(element => element.Name.LocalName == "HyperlinkButton" && element.Attributes().Any(attribute =>
                attribute.Name.LocalName == "Name" && attribute.Value.EndsWith("OperationsLink", StringComparison.Ordinal)))
            .ToArray();
        Assert.True(settingsLinks.Length >= 3);
        Assert.All(new[] { "SnapshotAiOperationsLink", "PrivacyOperationsLink", "RetentionOperationsLink", "PluginsOperationsLink" },
            name => Assert.Contains(settingsLinks, element => HasName(element, name)));

        var privacyLink = settingsLinks.Single(element => HasName(element, "PrivacyOperationsLink"));
        Assert.Contains(options.Descendants(), element => element.Attribute("Tag")?.Value == "Options.Navigation.Privacy.Title");
        Assert.Contains(options.Descendants(), element => element.Attribute("Tag")?.Value == "Options.Navigation.Privacy.Description");
        Assert.Contains(privacyLink.Descendants(), element => element.Attribute("Tag")?.Value == "Options.Navigation.Privacy.Action");
        Assert.Contains(privacyLink.Descendants(), element => element.Name.LocalName == "FontIcon" && element.Attribute("Glyph")?.Value == "\uE76C");

        var settingsLinkIcons = new[]
        {
            (Name: "SnapshotAiOperationsLink", Color: "#FFE88F6B", Glyph: "\uE7ED"),
            (Name: "PrivacyOperationsLink", Color: "#FFA97BEA", Glyph: "\uE72E"),
            (Name: "RetentionOperationsLink", Color: "#FF85A8DB", Glyph: "\uE823"),
            (Name: "PluginsOperationsLink", Color: "#FF71CBB7", Glyph: "\uE90F")
        };
        Assert.All(settingsLinkIcons, expected =>
        {
            var link = settingsLinks.Single(element => HasName(element, expected.Name));
            var icons = link.Descendants().Where(element => element.Name.LocalName == "FontIcon").ToArray();
            Assert.Equal(2, icons.Length);
            Assert.Equal(expected.Color, icons[0].Attribute("Foreground")?.Value);
            Assert.Equal(expected.Glyph, icons[0].Attribute("Glyph")?.Value);
            Assert.Equal("Raw", icons[0].Attributes().Single(attribute => attribute.Name.LocalName == "AutomationProperties.AccessibilityView").Value);
            Assert.Equal("\uE76C", icons[1].Attribute("Glyph")?.Value);
        });

        Assert.All(new[] { snapshots, privacy, retention, plugins, installationTransfer }, document =>
        {
            Assert.DoesNotContain(document.Descendants(), element => element.Name.LocalName == "InfoBar");
            Assert.Contains(document.Descendants(), element => element.Attribute("Tag")?.Value?.EndsWith(".Description", StringComparison.Ordinal) == true);
        });
        Assert.DoesNotContain(snapshots.Descendants(), element => element.Attribute("Tag")?.Value == "Operations.TakeSnapshotNow");

        var snapshotRoot = snapshots.Root!.Elements().Single(element => element.Name.LocalName == "Grid");
        var snapshotProgress = snapshots.Descendants().Single(element => HasName(element, "Progress"));
        var latestCapture = snapshots.Descendants().Single(element => HasName(element, "ScreenshotResultText"));
        var latestButton = snapshots.Descendants().Single(element => HasName(element, "OpenLatestCaptureButton"));
        var folderButton = snapshots.Descendants().Single(element => HasName(element, "OpenCapturesFolderButton"));
        var analyzeButton = snapshots.Descendants().Single(element => HasName(element, "GenerateDescriptionButton"));
        Assert.Null(snapshotRoot.Attribute("Background"));
        Assert.Equal("Raw", snapshotProgress.Attribute("AutomationProperties.AccessibilityView")?.Value);
        Assert.Equal("Find latest screenshot", latestButton.Attribute("Content")?.Value);
        Assert.Equal("Open screenshots folder", folderButton.Attribute("Content")?.Value);
        Assert.Equal("Describe activity with AI", analyzeButton.Attribute("Content")?.Value);
        Assert.Equal("{StaticResource AccentButtonStyle}", analyzeButton.Attribute("Style")?.Value);
        Assert.Null(latestCapture.Attribute("TextTrimming"));
        Assert.Equal("Wrap", latestCapture.Attribute("TextWrapping")?.Value);
        Assert.Null(latestCapture.Attribute("MaxLines"));
        Assert.Contains(snapshots.Descendants(), element => element.Attribute("Tag")?.Value == "Operations.Snapshot.LatestLabel");
        Assert.All(snapshots.Descendants().Where(element => element.Name.LocalName == "Border"), divider =>
        {
            Assert.Equal("1", divider.Attribute("Height")?.Value);
            Assert.Equal("{ThemeResource DividerStrokeColorDefaultBrush}", divider.Attribute("Background")?.Value);
        });
        Assert.Contains("ScreenshotResultText.Text = FileNameFromPath(screenshotPath);", snapshotSource, StringComparison.Ordinal);
        Assert.Contains("ToolTipService.SetToolTip(ScreenshotResultText, screenshotPath);", snapshotSource, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.SetHelpText(ScreenshotResultText, screenshotPath);", snapshotSource, StringComparison.Ordinal);
        Assert.DoesNotContain("Operations.Snapshot.FolderOpened", snapshotSource, StringComparison.Ordinal);
        Assert.All(new[] { "SnapshotAiHost", "PrivacyHost", "RetentionHost", "PluginsHost", "InstallationTransferHost" },
            name => Assert.Contains(operations.Descendants(), element => HasName(element, name)));
        Assert.All(
            operations.Descendants().Where(element => element.Name.LocalName == "ContentPresenter" && element.Attributes().Any(attribute => attribute.Name.LocalName == "Name" && attribute.Value.EndsWith("Host", StringComparison.Ordinal))),
            host => Assert.Null(host.Attribute("Visibility")));

        var operationLinkIcons = new[]
        {
            (Name: "OpenSnapshotAiLink", Color: "#FFE88F6B", Glyph: "\uE7ED"),
            (Name: "OpenPrivacyLink", Color: "#FFA97BEA", Glyph: "\uE72E"),
            (Name: "OpenRetentionLink", Color: "#FF85A8DB", Glyph: "\uE823"),
            (Name: "OpenPluginsLink", Color: "#FF71CBB7", Glyph: "\uE90F"),
            (Name: "OpenInstallationTransferLink", Color: "#FF5CC2C7", Glyph: "\uE8B5")
        };
        Assert.All(operationLinkIcons, expected =>
        {
            var link = operations.Descendants().Single(element => HasName(element, expected.Name));
            var icons = link.Descendants().Where(element => element.Name.LocalName == "FontIcon").ToArray();
            Assert.Equal(2, icons.Length);
            Assert.Equal(expected.Color, icons[0].Attribute("Foreground")?.Value);
            Assert.Equal(expected.Glyph, icons[0].Attribute("Glyph")?.Value);
            Assert.Equal("Raw", icons[0].Attributes().Single(attribute => attribute.Name.LocalName == "AutomationProperties.AccessibilityView").Value);
            Assert.Equal("\uE76C", icons[1].Attribute("Glyph")?.Value);
        });
    }

    /// <summary>Guards passive installation identity editing and preview-before-merge archive transfer.</summary>
    [Fact]
    public void InstallationTransfer_UsesArchivePickersAndFacadeBackedPreviewMerge()
    {
        var surface = XDocument.Load(RepositoryFile("WorkTrail", "Controls", "InstallationTransferOperationsControl.xaml"));
        var source = File.ReadAllText(RepositoryFile("WorkTrail", "Controls", "InstallationTransferOperationsControl.xaml.cs"));
        var appearance = File.ReadAllText(RepositoryFile("WorkTrail", "Controls", "InstallationAppearance.cs"));
        var operationsSource = File.ReadAllText(RepositoryFile("WorkTrail", "Controls", "OperationsControl.xaml.cs"));
        var mergeButton = surface.Descendants().Single(element => HasName(element, "MergeImportButton"));

        Assert.DoesNotContain(surface.Descendants(), element => element.Name.LocalName == "InfoBar");
        Assert.Contains(surface.Descendants(), element => HasName(element, "InstallationsList"));
        Assert.Contains(surface.Descendants(), element => HasName(element, "FriendlyNameBox"));
        Assert.Contains(surface.Descendants(), element => HasName(element, "ColorBox"));
        Assert.Contains(surface.Descendants(), element => HasName(element, "IconBox"));
        Assert.Contains(surface.Descendants(), element => HasName(element, "ImportPreviewPanel"));
        Assert.Equal("False", mergeButton.Attribute("IsEnabled")?.Value);
        Assert.All(surface.Descendants().Where(element => element.Name.LocalName == "Button"), button =>
            Assert.False(string.IsNullOrWhiteSpace(button.Attribute("Content")?.Value)));
        Assert.True(surface.Descendants().Count(element => element.Name.LocalName == "Rectangle") >= 2);

        Assert.Contains("private const string ArchiveExtension = \".tmuarchive\";", source, StringComparison.Ordinal);
        Assert.Contains("FileTypeChoices.Add", source, StringComparison.Ordinal);
        Assert.Contains("FileTypeFilter.Add(ArchiveExtension)", source, StringComparison.Ordinal);
        Assert.Contains("WinRT.Interop.InitializeWithWindow.Initialize", source, StringComparison.Ordinal);
        Assert.Contains("new DataArchiveExportRequest(destinationPath, IncludeScreenshots: true, OperationId: operationId)", source, StringComparison.Ordinal);
        Assert.Contains("new DataArchiveImportPreviewRequest(archivePath, operationId)", source, StringComparison.Ordinal);
        Assert.Contains("new DataArchiveImportRequest(plan.PlanId, operationId)", source, StringComparison.Ordinal);
        Assert.Equal(3, source.Split("archiveOperationId: operationId", StringSplitOptions.None).Length - 1);
        Assert.Contains("plan.AlreadyImported", source, StringComparison.Ordinal);
        Assert.Contains("imported.AddedInstallationCount", source, StringComparison.Ordinal);
        Assert.Contains("imported.SkippedScreenshotFileCount", source, StringComparison.Ordinal);
        Assert.DoesNotContain("imported.SkippedAiAnalysisCount + imported.SkippedScreenshotFileCount", source, StringComparison.Ordinal);
        Assert.Contains("Context.Dialogs.ConfirmAsync", source, StringComparison.Ordinal);
        Assert.Contains("InstallationAppearance.CreateAccentBrush", source, StringComparison.Ordinal);
        Assert.Contains("InstallationAppearance.GetIconGlyph", source, StringComparison.Ordinal);
        Assert.DoesNotContain("private static SolidColorBrush CreateBrush", source, StringComparison.Ordinal);
        Assert.All(
            new[] { "desktop", "laptop", "workstation", "home", "tablet", "phone", "server", "cloud", "office", "briefcase", "terminal", "gaming", "travel", "school", "studio", "camera" },
            icon => Assert.Contains($"\"{icon}\" =>", appearance, StringComparison.Ordinal));
        Assert.Contains("section.ShowArchiveActions();", operationsSource, StringComparison.Ordinal);
        Assert.DoesNotContain("System.IO", source, StringComparison.Ordinal);
        Assert.DoesNotContain("File.", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Directory.", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Process.", source, StringComparison.Ordinal);
    }

    /// <summary>Guards automatic plugin loading and direct, per-plugin switch updates.</summary>
    [Fact]
    public void PluginOperations_AutoLoadAndUseOneWayPerPluginSwitches()
    {
        var plugins = XDocument.Load(RepositoryFile("WorkTrail", "Controls", "PluginOperationsControl.xaml"));
        var pluginSource = File.ReadAllText(RepositoryFile("WorkTrail", "Controls", "PluginOperationsControl.xaml.cs"));
        var operationsSource = File.ReadAllText(RepositoryFile("WorkTrail", "Controls", "OperationsControl.xaml.cs"));
        var contextSource = File.ReadAllText(RepositoryFile("WorkTrail", "Controls", "OperationsSectionContext.cs"));
        var list = plugins.Descendants().Single(element => HasName(element, "PluginsList"));
        var toggle = plugins.Descendants().Single(element => element.Name.LocalName == "ToggleSwitch");

        Assert.Equal("None", list.Attribute("SelectionMode")?.Value);
        Assert.Equal("{Binding Enabled, Mode=OneWay}", toggle.Attribute("IsOn")?.Value);
        Assert.Equal("{Binding Id}", toggle.Attribute("Tag")?.Value);
        Assert.Equal("PluginToggle_Toggled", toggle.Attribute("Toggled")?.Value);
        Assert.Equal(string.Empty, toggle.Attribute("OnContent")?.Value);
        Assert.Equal(string.Empty, toggle.Attribute("OffContent")?.Value);
        Assert.DoesNotContain(plugins.Descendants(), element => element.Name.LocalName == "Button");

        Assert.Contains("internal async Task LoadAsync()", pluginSource, StringComparison.Ordinal);
        Assert.Equal(1, CountOccurrences(pluginSource, "GetPluginsAsync"));
        Assert.Contains("showSuccess: false", pluginSource, StringComparison.Ordinal);
        Assert.Contains("SetPluginEnabledAsync(plugin.Id, requestedState, token)", pluginSource, StringComparison.Ordinal);
        Assert.Contains("_isApplyingPluginState", pluginSource, StringComparison.Ordinal);
        Assert.Contains("plugin.Enabled == toggle.IsOn", pluginSource, StringComparison.Ordinal);
        Assert.Contains("RestoreToggle(toggle, plugin.Enabled);", pluginSource, StringComparison.Ordinal);
        Assert.DoesNotContain("RefreshButton_Click", pluginSource, StringComparison.Ordinal);
        Assert.DoesNotContain("EnableButton_Click", pluginSource, StringComparison.Ordinal);
        Assert.DoesNotContain("DisableButton_Click", pluginSource, StringComparison.Ordinal);

        Assert.Contains("if (section == OperationsSection.Plugins)", operationsSource, StringComparison.Ordinal);
        Assert.Contains("_ = _pluginsSection!.LoadAsync();", operationsSource, StringComparison.Ordinal);
        Assert.Contains("bool showSuccess = true", contextSource, StringComparison.Ordinal);
        Assert.Contains("if (showSuccess)", contextSource, StringComparison.Ordinal);
        Assert.Contains("ResultMessage(result.MessageKey", contextSource, StringComparison.Ordinal);
        Assert.Contains("_tryTranslate(messageKey)", contextSource, StringComparison.Ordinal);
        Assert.Contains("key => _strings.TryTranslate(key, out var value) ? value : null", pluginSource, StringComparison.Ordinal);
        Assert.Contains("result.Code == \"feature.premium_required\"", contextSource, StringComparison.Ordinal);
        Assert.DoesNotContain("Translate(result.Code)", contextSource, StringComparison.Ordinal);
        Assert.Contains("ResultMessage(result.MessageKey", operationsSource, StringComparison.Ordinal);
        Assert.DoesNotContain("result.Code", operationsSource, StringComparison.Ordinal);
        Assert.Contains("Context.ResultMessage(result.MessageKey", pluginSource, StringComparison.Ordinal);
        Assert.DoesNotContain("result.Code", pluginSource, StringComparison.Ordinal);
    }

    /// <summary>Guards the shared compact toast, severity colors, native icons, and configurable dismissal timeout.</summary>
    [Fact]
    public void CentralBanners_UseOneTimedOpaqueOverlay()
    {
        var app = XDocument.Load(RepositoryFile("WorkTrail", "App.xaml"));
        var mainWindow = XDocument.Load(RepositoryFile("WorkTrail", "MainWindow.xaml"));
        var operations = XDocument.Load(RepositoryFile("WorkTrail", "Controls", "OperationsControl.xaml"));
        var banner = XDocument.Load(RepositoryFile("WorkTrail", "Controls", "TimedInfoBar.xaml"));
        var screenshotWindow = XDocument.Load(RepositoryFile("WorkTrail", "ScreenshotWindow.xaml"));
        var toastService = File.ReadAllText(RepositoryFile("WorkTrail", "ToastNotificationService.cs"));
        var bannerSource = File.ReadAllText(RepositoryFile("WorkTrail", "Controls", "TimedInfoBar.xaml.cs"));
        var operationsSource = File.ReadAllText(RepositoryFile("WorkTrail", "Controls", "OperationsControl.xaml.cs"));
        var mainWindowSource = File.ReadAllText(RepositoryFile("WorkTrail", "MainWindow.xaml.cs"));
        var screenshotSource = File.ReadAllText(RepositoryFile("WorkTrail", "ScreenshotWindow.xaml.cs"));

        var overlay = mainWindow.Descendants().Single(element => HasName(element, "MainNotificationBanner"));
        Assert.Equal("Top", overlay.Attribute("VerticalAlignment")?.Value);
        Assert.Equal("100", overlay.Attributes().Single(attribute => attribute.Name.LocalName == "Canvas.ZIndex").Value);
        Assert.True(overlay.Parent is { } parent && HasName(parent, "RootGrid"));
        Assert.DoesNotContain(operations.Descendants(), element => element.Name.LocalName == "TimedInfoBar");
        Assert.Contains("operations.Initialize(_application, _dialogs, this, MainNotificationBanner);", mainWindowSource, StringComparison.Ordinal);
        Assert.Contains("_notificationHost = notificationHost ??", operationsSource, StringComparison.Ordinal);
        Assert.All(operations.Descendants().Where(element => element.Name.LocalName == "ScrollViewer"),
            scrollViewer => Assert.DoesNotContain(scrollViewer.Attributes(), attribute => attribute.Name.LocalName == "Grid.Row"));

        Assert.Single(banner.Descendants(), element => element.Name.LocalName == "InfoBar");
        var infoBar = banner.Descendants().Single(element => element.Name.LocalName == "InfoBar");
        var bannerSurface = banner.Descendants().Single(element => HasName(element, "BannerSurface"));
        var bannerFrame = banner.Descendants().Single(element => HasName(element, "BannerFrame"));
        Assert.DoesNotContain(banner.Descendants(), element => element.Name.LocalName == "ProgressBar" || HasName(element, "CountdownTrack"));
        Assert.Equal("620", banner.Root?.Attribute("MaxWidth")?.Value);
        Assert.Equal("Center", banner.Root?.Attribute("HorizontalAlignment")?.Value);
        Assert.Equal("Top", banner.Root?.Attribute("VerticalContentAlignment")?.Value);
        Assert.Equal("620", bannerSurface.Attribute("MaxWidth")?.Value);
        Assert.Equal("Center", bannerSurface.Attribute("HorizontalAlignment")?.Value);
        Assert.Equal("{ThemeResource ToastSuccessBorderBrush}", bannerFrame.Attribute("BorderBrush")?.Value);
        Assert.Equal("{ThemeResource ToastSuccessBackgroundBrush}", bannerFrame.Attribute("Background")?.Value);
        foreach (var severity in new[] { "Success", "Warning", "Error" })
        {
            var backgrounds = banner.Descendants().Where(element => HasKey(element, $"Toast{severity}BackgroundBrush")).ToArray();
            Assert.Equal(3, backgrounds.Length);
            Assert.Single(backgrounds, element => element.Attribute("ResourceKey")?.Value == "SystemColorWindowColorBrush");
            var state = banner.Descendants().Single(element => element.Name.LocalName == "VisualState" && HasName(element, severity));
            Assert.Contains(state.Descendants(), element => element.Attribute("Target")?.Value == "BannerFrame.BorderBrush"
                && element.Attribute("Value")?.Value == $"{{ThemeResource Toast{severity}BorderBrush}}");
        }
        Assert.Equal("1", bannerFrame.Attribute("BorderThickness")?.Value);
        Assert.Equal("16", bannerFrame.Attribute("CornerRadius")?.Value);
        Assert.DoesNotContain(banner.Descendants(), element => element.Name.LocalName == "InfoBar.Resources");
        Assert.Equal("15", infoBar.Attribute("CornerRadius")?.Value);
        Assert.Equal("48", infoBar.Attribute("MinHeight")?.Value);
        Assert.Equal("6,4", infoBar.Attribute("Padding")?.Value);
        Assert.Equal("True", infoBar.Attribute("IsIconVisible")?.Value);
        Assert.DoesNotContain(banner.Descendants(), element => HasName(element, "FrostedVeil"));
        Assert.DoesNotContain(banner.Descendants(), element => element.Name.LocalName.Contains("GradientBrush", StringComparison.Ordinal));
        Assert.Null(infoBar.Attribute("Background"));
        Assert.Null(infoBar.Attribute("BorderBrush"));
        Assert.Equal("0", infoBar.Attribute("BorderThickness")?.Value);
        Assert.Null(infoBar.Attribute("Foreground"));
        Assert.Equal("Polite", infoBar.Attributes().Single(attribute => attribute.Name.LocalName == "AutomationProperties.LiveSetting").Value);
        Assert.Equal("True", infoBar.Attribute("IsClosable")?.Value);
        Assert.Equal("BannerInfoBar_Closing", infoBar.Attribute("Closing")?.Value);
        Assert.Equal("0", bannerSurface.Attribute("Opacity")?.Value);
        Assert.Equal("Top", bannerSurface.Attribute("VerticalAlignment")?.Value);
        Assert.DoesNotContain(app.Descendants(), element =>
            HasKey(element, "TimedInfoBarBackdropBrush") ||
            HasKey(element, "TimedInfoBarGlassBorderBrush") ||
            HasKey(element, "TimedInfoBarGlassVeilBrush"));

        Assert.Contains("TimeSpan.FromSeconds(10)", toastService, StringComparison.Ordinal);
        Assert.Contains("TimeSpan? timeout = null", toastService, StringComparison.Ordinal);
        Assert.Contains("ValidateTimeout", toastService, StringComparison.Ordinal);
        Assert.Contains("DispatcherQueueTimer", toastService, StringComparison.Ordinal);
        Assert.Contains("Stopwatch.GetElapsedTime", toastService, StringComparison.Ordinal);
        Assert.Contains("host.DispatcherQueue.HasThreadAccess", toastService, StringComparison.Ordinal);
        Assert.Contains("host.Dismissed", toastService, StringComparison.Ordinal);
        Assert.Contains("countdown.Timer.Tick -= countdown.Tick;", toastService, StringComparison.Ordinal);
        Assert.Contains("ValidateHostThread(host);", toastService, StringComparison.Ordinal);
        Assert.Contains("countdown.Generation != generation", toastService, StringComparison.Ordinal);
        Assert.Contains("TimeSpan.FromMilliseconds(250)", toastService, StringComparison.Ordinal);
        Assert.DoesNotContain("CountdownIndicator", toastService, StringComparison.Ordinal);
        Assert.Contains("private const float BannerElevation = 18f;", bannerSource, StringComparison.Ordinal);
        Assert.Contains("BannerInfoBar.Severity = severity;", bannerSource, StringComparison.Ordinal);
        Assert.Contains("InfoBarSeverity.Informational", toastService, StringComparison.Ordinal);
        Assert.Contains("InfoBarSeverity.Success", toastService, StringComparison.Ordinal);
        Assert.Contains("InfoBarSeverity.Warning", toastService, StringComparison.Ordinal);
        Assert.Contains("InfoBarSeverity.Error", toastService, StringComparison.Ordinal);
        Assert.Contains("args.Cancel = true;", bannerSource, StringComparison.Ordinal);
        Assert.Contains("new UISettings().AnimationsEnabled", bannerSource, StringComparison.Ordinal);
        Assert.Contains("_transitionGeneration", bannerSource, StringComparison.Ordinal);
        Assert.Contains("if (!_isPresented || _isDismissing)", bannerSource, StringComparison.Ordinal);
        Assert.Contains("generation != _transitionGeneration", bannerSource, StringComparison.Ordinal);
        Assert.Contains("StartOpacityTransition(1d, completion: null, initialOpacity: 0d);", bannerSource, StringComparison.Ordinal);
        Assert.Contains("StartOpacityTransition(0d, () => CompleteDismissal(generation));", bannerSource, StringComparison.Ordinal);
        Assert.Contains(screenshotWindow.Descendants(), element =>
            element.Name.LocalName == "TimedInfoBar" && HasName(element, "ScreenshotActionBanner"));
        Assert.Contains(mainWindow.Descendants(), element =>
            element.Name.LocalName == "TimedInfoBar" && HasName(element, "MainNotificationBanner"));
        Assert.Contains("IsFrameAnalysisNotification(notification)", mainWindowSource, StringComparison.Ordinal);
        Assert.Contains("\"Notification.AiAnalysisFailed.Title\" or", mainWindowSource, StringComparison.Ordinal);
        Assert.Contains("\"Notification.AiDailyLimitReached.Title\";", mainWindowSource, StringComparison.Ordinal);
        Assert.Contains("_dialogs.Notifications.ShowError(MainNotificationBanner, title, message);", mainWindowSource, StringComparison.Ordinal);
        Assert.Contains("_dialogs.Notifications.ShowSuccess(ScreenshotActionBanner, title, message);", screenshotSource, StringComparison.Ordinal);
        Assert.Contains("_dialogs.Notifications.ShowError(ScreenshotActionBanner, title, message);", screenshotSource, StringComparison.Ordinal);
        Assert.Equal(5, CountOccurrences(operationsSource, "OwnerWindow, OperationBanner"));
    }

    /// <summary>Ensures every requested operation remains delegated through the shared facade.</summary>
    [Fact]
    public void OperationsCodeBehind_InvokesCompleteFacadeSurfaceWithoutDirectIo()
    {
        var sources = new[]
        {
            "OperationsControl.xaml.cs",
            "SnapshotAiOperationsControl.xaml.cs",
            "PrivacyOperationsControl.xaml.cs",
            "RetentionOperationsControl.xaml.cs",
            "PluginOperationsControl.xaml.cs",
            "InstallationTransferOperationsControl.xaml.cs"
        }.Select(file => File.ReadAllText(RepositoryFile("WorkTrail", "Controls", file))).ToArray();
        var source = string.Join(Environment.NewLine, sources);
        string[] requiredFacadeCalls =
        [
            "GetLatestScreenshotAsync",
            "OpenScreenshotFolderAsync",
            "AnalyzeCurrentActivityAsync",
            "GetPrivacyRulesAsync",
            "AddPrivacyRuleAsync",
            "RemovePrivacyRuleAsync",
            "TestCurrentPrivacyAsync",
            "GetRetentionStatusAsync",
            "PreviewRetentionAsync",
            "RunRetentionAsync",
            "PrepareAtomicResetAsync",
            "GetPluginsAsync",
            "SetPluginEnabledAsync",
            "GetInstallationProfilesAsync",
            "UpdateInstallationProfileAsync",
            "ExportDataArchiveAsync",
            "PreviewDataArchiveImportAsync",
            "ImportDataArchiveAsync"
        ];

        Assert.All(requiredFacadeCalls, call => Assert.Contains(call, source, StringComparison.Ordinal));
        Assert.DoesNotContain("GetRuntimeHealthAsync", source, StringComparison.Ordinal);
        Assert.DoesNotContain("CaptureSystemSnapshotAsync", source, StringComparison.Ordinal);
        var optionsSource = File.ReadAllText(RepositoryFile("WorkTrail", "Controls", "SensorOptionsControl.xaml.cs"));
        Assert.Contains("_application.EnableAdvancedHardwareTelemetryAsync", optionsSource, StringComparison.Ordinal);
        Assert.DoesNotContain("EnableAdvancedHardwareTelemetryAsync", source, StringComparison.Ordinal);
        Assert.DoesNotContain("System.IO", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Process.", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Environment.", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Registry", source, StringComparison.Ordinal);
        Assert.DoesNotContain("HttpClient", source, StringComparison.Ordinal);
        Assert.DoesNotContain("LocalStore", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ScreenCaptureService", source, StringComparison.Ordinal);
    }

    /// <summary>Guards automatic criteria loading, deliberate cleanup actions, and compact full-path disclosure.</summary>
    [Fact]
    public void RetentionSurface_SeparatesCriteriaPreviewAndConfirmedDeletion()
    {
        var surface = XDocument.Load(RepositoryFile("WorkTrail", "Controls", "RetentionOperationsControl.xaml"));
        var source = File.ReadAllText(RepositoryFile("WorkTrail", "Controls", "RetentionOperationsControl.xaml.cs"));
        var operationsSource = File.ReadAllText(RepositoryFile("WorkTrail", "Controls", "OperationsControl.xaml.cs"));
        var sectionTags = new[]
        {
            "Operations.Retention.PolicyMonths",
            "Operations.Retention.Preview",
            "Operations.Retention.Monthly"
        };

        Assert.All(sectionTags, tag => Assert.Contains(surface.Descendants(), element =>
            element.Attribute("Tag")?.Value == tag
            && element.Attributes().Any(attribute =>
                attribute.Name.LocalName == "AutomationProperties.HeadingLevel"
                && attribute.Value == "Level3")));
        Assert.True(surface.Descendants().Count(element => element.Name.LocalName == "Rectangle") >= 2);
        Assert.Single(surface.Descendants(), element => element.Name.LocalName == "Border");
        Assert.Contains(surface.Descendants(), element => element.Attribute("Tag")?.Value == "Operations.Retention.Preview.Description");
        Assert.Contains(surface.Descendants(), element => element.Attribute("Tag")?.Value == "Operations.Retention.Summary");

        Assert.All(new[]
        {
            "Operations.Retention.PreviewAction",
            "Operations.Retention.CleanupAction"
        }, tag =>
        {
            var button = surface.Descendants().Single(element => element.Name.LocalName == "Button" && element.Attribute("Tag")?.Value == tag);
            Assert.Equal("Left", button.Attribute("HorizontalAlignment")?.Value);
            Assert.Equal("False", button.Attribute("IsEnabled")?.Value);
            Assert.Equal("{StaticResource WrappingActionLabelTemplate}", button.Attribute("ContentTemplate")?.Value);
        });

        Assert.Equal(2, surface.Descendants().Count(element => element.Name.LocalName == "Button"));
        Assert.Contains("internal async Task LoadAsync()", source, StringComparison.Ordinal);
        Assert.Contains("_ = _retentionSection!.LoadAsync();", operationsSource, StringComparison.Ordinal);

        var directory = surface.Descendants().Single(element => HasName(element, "ScreenshotFolderBox"));
        var candidatePath = surface.Descendants().Single(element =>
            element.Name.LocalName == "TextBlock" && element.Attribute("ToolTipService.ToolTip")?.Value == "{Binding}");
        Assert.Equal("TextBox", directory.Name.LocalName);
        Assert.Equal("ScreenshotFolderBox_LostFocus", directory.Attribute("LostFocus")?.Value);
        Assert.Equal("CharacterEllipsis", candidatePath.Attribute("TextTrimming")?.Value);
        Assert.Equal("1", candidatePath.Attribute("MaxLines")?.Value);
        Assert.Contains("ScreenshotFolderBox.Text = status.ScreenshotDirectory", source, StringComparison.Ordinal);
        Assert.Contains("QueueStorageSave(\"screenshots.directory\"", source, StringComparison.Ordinal);
        Assert.Contains("Operations.Retention.Preview.Paths", source, StringComparison.Ordinal);
        Assert.DoesNotContain("System.IO", source, StringComparison.Ordinal);
    }

    /// <summary>Ensures destructive retention execution cannot be triggered without a safe-default dialog.</summary>
    [Fact]
    public void RetentionExecution_RequiresExplicitUiAndApplicationConfirmation()
    {
        var source = File.ReadAllText(RepositoryFile("WorkTrail", "Controls", "RetentionOperationsControl.xaml.cs"));

        Assert.Contains("Dialogs.ConfirmAsync(", source, StringComparison.Ordinal);
        Assert.Contains("DialogRequest.Confirmation(", source, StringComparison.Ordinal);
        Assert.Contains("if (!confirmed)", source, StringComparison.Ordinal);
        Assert.Contains("new RetentionRequest(Execute: true, Confirmed: true, OperationId: operationId)", source, StringComparison.Ordinal);
        Assert.Contains("retentionOperationId: operationId", source, StringComparison.Ordinal);
    }

    /// <summary>Guards the two-step warning flow and keeps destructive reset work behind the shared facade.</summary>
    [Fact]
    public void AtomicReset_RequiresTwoSharedDialogsAndUsesTheApplicationFacade()
    {
        var surface = XDocument.Load(RepositoryFile("WorkTrail", "Controls", "OperationsControl.xaml"));
        var source = File.ReadAllText(RepositoryFile("WorkTrail", "Controls", "OperationsControl.xaml.cs"));
        var button = surface.Descendants().Single(element => HasName(element, "AtomicNukeButton"));

        Assert.Equal("AtomicNukeButton_Click", button.Attribute("Click")?.Value);
        Assert.Contains(surface.Descendants(), element => element.Attribute("Tag")?.Value == "Operations.AtomicNuke.Description");
        Assert.Equal(2, CountOccurrences(source, "Dialogs.ConfirmAtomicResetAsync("));
        var dialog = XDocument.Load(RepositoryFile("WorkTrail", "AtomicResetDialog.xaml"));
        Assert.Equal("Close", dialog.Root!.Attribute("DefaultButton")?.Value);
        var dialogService = File.ReadAllText(RepositoryFile("WorkTrail", "MicaDialogService.cs"));
        Assert.Contains("RunContentDialogSessionAsync(owner, request, ContentDialogButton.Close, atomicReset: true)", dialogService, StringComparison.Ordinal);
        Assert.Equal(2, CountOccurrences(source, "DialogRequest.Confirmation("));
        Assert.Contains("new AtomicResetRequest(firstConfirmation, finalConfirmation)", source, StringComparison.Ordinal);
        Assert.Contains("PrepareAtomicResetAsync", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Directory.Delete", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Process.Start", source, StringComparison.Ordinal);
    }

    /// <summary>Prevents view code from bypassing the single queued dialog engine.</summary>
    [Fact]
    public void WindowViews_UseOnlyTheDedicatedStandardDialogService()
    {
        var workTrailDirectory = Path.GetDirectoryName(RepositoryFile("WorkTrail", "MainWindow.xaml.cs"))!;
        var sources = Directory.EnumerateFiles(workTrailDirectory, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) &&
                           !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase));

        foreach (var path in sources)
        {
            var source = File.ReadAllText(path);
            if (!path.EndsWith("MicaDialogService.cs", StringComparison.OrdinalIgnoreCase))
            {
                Assert.DoesNotContain("new ContentDialog", source, StringComparison.Ordinal);
            }
            Assert.DoesNotContain("MessageBox.Show", source, StringComparison.Ordinal);
            Assert.DoesNotContain("new MessageDialog", source, StringComparison.Ordinal);
            Assert.DoesNotContain("MessageBoxW", source, StringComparison.Ordinal);
        }
    }

    /// <summary>Guards the queued standard WinUI dialog contract while retaining dedicated rich windows.</summary>
    [Fact]
    public void DialogEngine_QueuesContentDialogsAndKeepsRichWindowsDedicated()
    {
        var service = File.ReadAllText(RepositoryFile("WorkTrail", "MicaDialogService.cs"));
        var interop = File.ReadAllText(RepositoryFile("WorkTrail.Core", "Infrastructure", "Services", "WindowInteropService.cs"));
        var connectionDialog = File.ReadAllText(RepositoryFile("WorkTrail", "AiConnectionTestDialogWindow.xaml.cs"));
        var connectionDialogXaml = XDocument.Load(RepositoryFile("WorkTrail", "AiConnectionTestDialogWindow.xaml"));
        var main = File.ReadAllText(RepositoryFile("WorkTrail", "MainWindow.xaml.cs"));
        var app = File.ReadAllText(RepositoryFile("WorkTrail", "App.xaml.cs"));
        var schedule = File.ReadAllText(RepositoryFile("WorkTrail", "ScheduleWindow.xaml.cs"));
        var projectDirectory = Path.GetDirectoryName(RepositoryFile("WorkTrail", "MicaDialogService.cs"))!;

        Assert.Contains("DialogSessionQueue", service, StringComparison.Ordinal);
        Assert.Contains("_queue.EnterAsync(ownerLifetime.Token)", service, StringComparison.Ordinal);
        Assert.Contains("_queue.Shutdown();", service, StringComparison.Ordinal);
        Assert.Contains("_activeContentDialog?.Hide();", service, StringComparison.Ordinal);
        Assert.Contains("Notifications.HideAll();", service, StringComparison.Ordinal);
        Assert.Contains("owner.Closed += OwnerClosed;", service, StringComparison.Ordinal);
        Assert.Contains("owner.Closed -= OwnerClosed;", service, StringComparison.Ordinal);
        Assert.Contains("RequestedTheme = ownerContent.ActualTheme", service, StringComparison.Ordinal);
        Assert.Contains("Language = ownerContent.Language", service, StringComparison.Ordinal);
        Assert.Contains("RunContentDialogSessionAsync", service, StringComparison.Ordinal);
        Assert.Contains("owner.DispatcherQueue.HasThreadAccess", service, StringComparison.Ordinal);
        Assert.Contains("Content dialogs require a loaded owner XamlRoot", service, StringComparison.Ordinal);
        Assert.Contains("XamlRoot = xamlRoot", service, StringComparison.Ordinal);
        Assert.Contains("PrimaryButtonText = request.PrimaryButtonText", service, StringComparison.Ordinal);
        Assert.Contains("CloseButtonText = request.CloseButtonText", service, StringComparison.Ordinal);
        Assert.Contains("ContentDialogResult.Primary", service, StringComparison.Ordinal);
        Assert.Contains("ValidateDialogRequest", service, StringComparison.Ordinal);
        Assert.Contains("WindowInteropService.DisableCurrentThreadPeerWindows(dialogHandle)", service, StringComparison.Ordinal);
        Assert.Contains("WindowInteropService.RestoreWindows(disabledPeerWindows)", service, StringComparison.Ordinal);
        Assert.DoesNotContain("ShowSystemWarningAsync", service, StringComparison.Ordinal);
        Assert.DoesNotContain("MicaDialogRequest", service, StringComparison.Ordinal);
        Assert.DoesNotContain("MicaDialogWindow", service, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(projectDirectory, "MicaDialogWindow.xaml")));
        Assert.False(File.Exists(Path.Combine(projectDirectory, "MicaDialogWindow.xaml.cs")));
        Assert.Contains("EnumThreadWindows", interop, StringComparison.Ordinal);
        Assert.Contains("EnableWindow(windowHandle, false)", interop, StringComparison.Ordinal);
        Assert.Contains("EnableWindow(windowHandle, true)", interop, StringComparison.Ordinal);
        Assert.DoesNotContain("MessageBoxW", interop, StringComparison.Ordinal);
        Assert.DoesNotContain("SystemMessageBox", interop, StringComparison.Ordinal);
        Assert.Contains("DialogRequest.Informative(", main, StringComparison.Ordinal);
        Assert.Contains("DialogRequest.Informative(", app, StringComparison.Ordinal);
        Assert.Contains("DialogRequest.Confirmation(", schedule, StringComparison.Ordinal);
        Assert.DoesNotContain("MicaDialogRequest", main, StringComparison.Ordinal);
        Assert.DoesNotContain("MicaDialogRequest", app, StringComparison.Ordinal);
        Assert.DoesNotContain("MicaDialogRequest", schedule, StringComparison.Ordinal);
        Assert.DoesNotContain("SavePlacementAsync", service, StringComparison.Ordinal);
        Assert.Contains("SetWindowLongPtr64", interop, StringComparison.Ordinal);
        Assert.Contains("SetWindowPos(windowHandle, HwndTopMost", interop, StringComparison.Ordinal);
        Assert.Contains("DrainApplicationNotificationsAsync", main, StringComparison.Ordinal);
        Assert.Contains("Enabled: true, HasKey: false", main, StringComparison.Ordinal);
        Assert.Contains("private readonly MicaDialogService _dialogs = new();", app, StringComparison.Ordinal);
        var screenshot = File.ReadAllText(RepositoryFile("WorkTrail", "ScreenshotWindow.xaml.cs"));
        Assert.DoesNotContain("MicaDialogService _dialogs = new()", screenshot, StringComparison.Ordinal);
        Assert.Contains("_dialogs = dialogs ??", screenshot, StringComparison.Ordinal);
        Assert.Equal(
            CountOccurrences(app, "new ScreenshotWindow("),
            CountOccurrences(app, "new ScreenshotWindow(application, _dialogs,"));
        Assert.Contains("_dialogs.ConfirmAsync(", schedule, StringComparison.Ordinal);
        Assert.Contains("ShowAiConnectionTestAsync", service, StringComparison.Ordinal);
        Assert.Contains("new AiConnectionTestDialogWindow", service, StringComparison.Ordinal);
        Assert.Contains(connectionDialogXaml.Descendants(), element => element.Name.LocalName == "DesktopAcrylicBackdrop");
        Assert.Contains(connectionDialogXaml.Descendants(), element => HasName(element, "TerminalScrollViewer"));
        Assert.Contains(connectionDialogXaml.Descendants(), element => HasName(element, "TerminalText") && element.Attribute("FontFamily")?.Value == "Cascadia Mono");
        Assert.Contains("AiConnectionTestProtocol.Prompt", connectionDialog, StringComparison.Ordinal);
        Assert.Contains("AppendTerminalAsync", connectionDialog, StringComparison.Ordinal);
        Assert.Contains("MaxTerminalOutputCharacters", connectionDialog, StringComparison.Ordinal);
        Assert.Contains("_lifetimeCancellation.Token", connectionDialog, StringComparison.Ordinal);
        Assert.DoesNotContain("TerminalScrollViewer.UpdateLayout()", connectionDialog, StringComparison.Ordinal);
        Assert.Contains("RestoreOrCenterAsync", connectionDialog, StringComparison.Ordinal);
        Assert.Contains("UiLocalization.Apply(RootGrid, _strings);", connectionDialog, StringComparison.Ordinal);
        Assert.Contains("AiConnectionTest.Terminal.Response", connectionDialog, StringComparison.Ordinal);
        Assert.Contains("new LocalizationService(", service, StringComparison.Ordinal);
        Assert.Contains(connectionDialogXaml.Descendants(), element => element.Attribute("Tag")?.Value == "AiConnectionTest.Eyebrow");
        Assert.Contains(connectionDialogXaml.Descendants(), element => element.Attribute("Tag")?.Value == "AiConnectionTest.Console");
        Assert.Contains(connectionDialogXaml.Descendants(), element => element.Attribute("Tag")?.Value == "AiConnectionTest.Cancel");
    }

    /// <summary>Opacity never applies to the shared notification overlay or its ancestors.</summary>
    [Theory]
    [InlineData("MainWindow", "MainNotificationBanner", "MainWindowOpacityPercent")]
    [InlineData("WorldClockWindow", "WorldClockNotificationBanner", "WorldClockWindowOpacityPercent")]
    public void WindowOpacity_ExcludesTheSharedNotificationLayer(string windowName, string bannerName, string opacitySetting)
    {
        var window = XDocument.Load(RepositoryFile("WorkTrail", windowName + ".xaml"));
        var source = File.ReadAllText(RepositoryFile("WorkTrail", windowName + ".xaml.cs"));
        var root = window.Descendants().Single(element => HasName(element, "RootGrid"));
        var content = root.Elements().Single(element => HasName(element, "WindowContent"));
        var banner = root.Elements().Single(element => HasName(element, bannerName));

        Assert.Same(root, banner.Parent);
        Assert.DoesNotContain(content.Descendants(), element => element.Name.LocalName == "TimedInfoBar");
        Assert.Null(root.Attribute("Opacity"));
        Assert.DoesNotContain("RootGrid.Opacity =", source, StringComparison.Ordinal);
        Assert.Contains($"WindowContent.Opacity = settings.{opacitySetting} / 100d;", source, StringComparison.Ordinal);
        Assert.Equal("2", content.Attribute("Grid.RowSpan")?.Value);
        var outerRows = root.Elements().Single(element => element.Name.LocalName == "Grid.RowDefinitions");
        var contentRows = content.Elements().Single(element => element.Name.LocalName == "Grid.RowDefinitions");
        Assert.Equal(outerRows.Elements().Select(row => row.Attribute("Height")?.Value),
            contentRows.Elements().Select(row => row.Attribute("Height")?.Value));
    }

    private static bool HasName(XElement element, string value)
        => element.Attributes().Any(attribute => attribute.Name.LocalName == "Name" && attribute.Value == value);

    private static bool HasKey(XElement element, string key)
        => element.Attributes().Any(attribute => attribute.Name.LocalName == "Key" && attribute.Value == key);

    private static int CountOccurrences(string source, string value)
    {
        var count = 0;
        for (var offset = 0; (offset = source.IndexOf(value, offset, StringComparison.Ordinal)) >= 0; offset += value.Length)
        {
            count++;
        }

        return count;
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
}
