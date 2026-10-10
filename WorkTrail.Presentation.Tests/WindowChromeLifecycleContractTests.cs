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
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Xunit;

namespace WorkTrail.Presentation.Tests;

/// <summary>Guards the shared title-bar and main-window lifetime ownership contracts.</summary>
public sealed class WindowChromeLifecycleContractTests
{
    /// <summary>Main activation delegates live peer handles to Core without opening new windows or activating each peer.</summary>
    [Fact]
    public void MainActivation_RevealsOnlyLivePeersThroughTheFacade()
    {
        var main = File.ReadAllText(RepositoryFile("WorkTrail", "MainWindow.xaml.cs"));
        var placement = File.ReadAllText(RepositoryFile("WorkTrail", "WindowPlacementService.cs"));
        Assert.Contains("Activated += MainWindow_Activated;", main, StringComparison.Ordinal);
        Assert.Contains("Activated -= MainWindow_Activated;", main, StringComparison.Ordinal);
        var start = main.IndexOf("private async void MainWindow_Activated", StringComparison.Ordinal);
        var end = main.IndexOf("private void ShowWindowRevealFailure", start, StringComparison.Ordinal);
        var handler = main[start..end];
        Assert.Contains("WindowActivationState.Deactivated", handler, StringComparison.Ordinal);
        Assert.Contains("_dashboardSurfaceClosed || _revealingOpenWindows", handler, StringComparison.Ordinal);
        Assert.Contains("_revealingOpenWindows = true;", handler, StringComparison.Ordinal);
        Assert.Contains("_revealingOpenWindows = false;", handler, StringComparison.Ordinal);
        Assert.Contains("WindowPlacementService.GetOpenPeerWindowHandles(mainHandle)", handler, StringComparison.Ordinal);
        Assert.Contains("_application.RevealOpenWindowsAsync(new WindowRevealRequest(mainHandle, peers), _lifecycle.Token)", handler, StringComparison.Ordinal);
        Assert.DoesNotContain(".Activate(", handler, StringComparison.Ordinal);
        Assert.DoesNotContain("new ScreenshotWindow", handler, StringComparison.Ordinal);
        Assert.DoesNotContain("RestoreWorkspace", handler, StringComparison.Ordinal);
        Assert.Contains("!placement._disposed && placement._placementReady", placement, StringComparison.Ordinal);
        Assert.Contains("!placement._closeSaveStarted && !placement._shutdownPrepared", placement, StringComparison.Ordinal);
        Assert.Contains("GetOpenPeerWindowHandles(long mainWindowHandle) => s_preservingWorkspace", placement, StringComparison.Ordinal);
    }

    /// <summary>The delayed reveal shield must stay within the caption, including when its grid row has zero height.</summary>
    [Fact]
    public void RevealShield_IsConfinedToTheTitleBarRow()
    {
        var controller = File.ReadAllText(RepositoryFile("WorkTrail", "CustomTitleBarController.cs"));
        Assert.Contains("Grid.SetRow(_revealSurface, 0);", controller, StringComparison.Ordinal);
        Assert.Contains("_revealSurface.Height = overlay ? _overlayLayout.HeaderHeight : double.NaN;", controller, StringComparison.Ordinal);
        Assert.Contains("_revealSurface.VerticalAlignment = overlay ? VerticalAlignment.Top : VerticalAlignment.Stretch;", controller, StringComparison.Ordinal);
        Assert.Contains("_root.PreviewKeyDown += Root_PreviewKeyDown;", controller, StringComparison.Ordinal);
        Assert.Contains("_transitionTimer.Tick -= TransitionTimer_Tick;", controller, StringComparison.Ordinal);
    }

    /// <summary>Only astronomy widgets opt into overlays; ordinary work windows keep their top controls unobstructed.</summary>
    [Fact]
    public void Overlay_IsLimitedToAstronomyWidgets()
    {
        var clocks = File.ReadAllText(RepositoryFile("WorkTrail", "WorldClockWindow.xaml.cs"));
        var astronomy = File.ReadAllText(RepositoryFile("WorkTrail", "AstronomyWindowController.cs"));
        Assert.Contains("overlayContent: true", clocks, StringComparison.Ordinal);
        Assert.Contains("overlayContent: true", astronomy, StringComparison.Ordinal);
        foreach (var window in MigratedTopLevelWindows.Concat(MigratedDialogWindows).Append("MainWindow"))
        {
            Assert.DoesNotContain("overlayContent: true", File.ReadAllText(RepositoryFile("WorkTrail", window + ".xaml.cs")), StringComparison.Ordinal);
        }

    }

    private static readonly string[] MigratedTopLevelWindows =
    [
        "AboutWindow",
        "OcrTextWindow",
        "QuickSetupWindow",
        "ScheduleWindow",
        "ScreenshotWindow",
        "SearchIndexingWindow",
        "SearchWindow",
        "ThirdPartyLicensesWindow"
    ];

    private static readonly string[] MigratedDialogWindows =
    [
        "ActivityCalendarDialogWindow",
        "AiConnectionTestDialogWindow",
        "AiPricingDialogWindow",
        "AiScreenshotReprocessingDialogWindow",
        "WorldClockCityPickerDialogWindow"
    ];

    /// <summary>Verifies that the main and world-clock windows share the DPI-aware title-bar controller.</summary>
    [Fact]
    public void MainAndWorldClock_UseOneDpiAwareTitleBarController()
    {
        var controller = File.ReadAllText(RepositoryFile("WorkTrail", "CustomTitleBarController.cs"));
        var main = File.ReadAllText(RepositoryFile("WorkTrail", "MainWindow.xaml.cs"));
        var worldClock = File.ReadAllText(RepositoryFile("WorkTrail", "WorldClockWindow.xaml.cs"));

        Assert.Contains("PreferredHeightOption = TitleBarHeightOption.Tall", controller, StringComparison.Ordinal);
        Assert.Contains("xamlRoot.RasterizationScale", controller, StringComparison.Ordinal);
        Assert.Contains(".Where(static rect => rect.Width > 0 && rect.Height > 0)", controller, StringComparison.Ordinal);
        Assert.Contains("_appWindow.TitleBar.LeftInset / scale", controller, StringComparison.Ordinal);
        Assert.Contains("_appWindow.TitleBar.RightInset / scale", controller, StringComparison.Ordinal);
        Assert.Contains("NonClientRegionKind.Passthrough", controller, StringComparison.Ordinal);
        Assert.Contains("new AccessibilitySettings().HighContrast", controller, StringComparison.Ordinal);
        Assert.Contains("new CustomTitleBarController(", main, StringComparison.Ordinal);
        Assert.Contains("new CustomTitleBarController(", worldClock, StringComparison.Ordinal);
        Assert.DoesNotContain("InputNonClientPointerSource", main, StringComparison.Ordinal);
        Assert.DoesNotContain("InputNonClientPointerSource", worldClock, StringComparison.Ordinal);
    }

    /// <summary>Verifies that windows without interactive title-bar controls clear instead of registering an empty region.</summary>
    [Fact]
    public void SharedTitleBar_ClearsPassthroughRegionsWhenNoInteractiveElementsRemain()
    {
        var controller = File.ReadAllText(RepositoryFile("WorkTrail", "CustomTitleBarController.cs"));
        var guardIndex = controller.IndexOf("if (passthroughRects.Length == 0)", StringComparison.Ordinal);
        var setIndex = controller.IndexOf("pointerSource.SetRegionRects(", StringComparison.Ordinal);

        Assert.True(guardIndex >= 0);
        Assert.True(setIndex > guardIndex);
        var emptyRegionBranch = controller[guardIndex..setIndex];
        Assert.Contains("pointerSource.ClearRegionRects(NonClientRegionKind.Passthrough);", emptyRegionBranch, StringComparison.Ordinal);
        Assert.Contains("return;", emptyRegionBranch, StringComparison.Ordinal);
    }

    /// <summary>Verifies shared title-bar command sizing, label alignment, and PNG brand artwork.</summary>
    [Fact]
    public void TitleBars_ShareAlignedCommandsLabelsAndPngBrandMark()
    {
        var styles = XDocument.Load(RepositoryFile("WorkTrail", "Controls", "TitleBarOverflowButtonStyles.xaml"));
        var main = XDocument.Load(RepositoryFile("WorkTrail", "MainWindow.xaml"));
        var worldClock = XDocument.Load(RepositoryFile("WorkTrail", "WorldClockWindow.xaml"));
        var commandStyle = styles.Descendants().Single(element => KeyValue(element) == "WorkTrailTitleBarCommandButtonStyle");
        var logoStyle = styles.Descendants().Single(element => KeyValue(element) == "WorkTrailTitleBarLogoStyle");

        AssertSetter(commandStyle, "Width", "40");
        AssertSetter(commandStyle, "Height", "40");
        AssertSetter(commandStyle, "VerticalAlignment", "Center");
        AssertSetter(logoStyle, "Width", "22");
        AssertSetter(logoStyle, "Height", "22");
        AssertSetter(logoStyle, "Source", "ms-appx:///Assets/WorkTrailSquare44Logo.png");
        Assert.DoesNotContain(".webp", styles.ToString(), StringComparison.OrdinalIgnoreCase);

        Assert.All(
            main.Descendants().Where(element => HasName(element, "TitleBarLogo"))
                .Concat(worldClock.Descendants().Where(element => HasName(element, "TitleBarLogo"))),
            logo => Assert.Equal("{StaticResource WorkTrailTitleBarLogoStyle}", logo.Attribute("Style")?.Value));
        Assert.Equal("48", FirstRowHeight(main));
        Assert.Equal("48", FirstRowHeight(worldClock));
    }

    /// <summary>Verifies that main-window initialization is tracked and cancelled with the window lifetime.</summary>
    [Fact]
    public void MainInitialization_IsTrackedAndCancelledWithWindowLifetime()
    {
        var lifecycle = File.ReadAllText(RepositoryFile("WorkTrail", "WindowSurfaceLifecycle.cs"));
        var main = File.ReadAllText(RepositoryFile("WorkTrail", "MainWindow.xaml.cs"));

        Assert.Contains("private Task? _initializationTask;", lifecycle, StringComparison.Ordinal);
        Assert.Contains("_initializationTask = ObserveInitializationAsync", lifecycle, StringComparison.Ordinal);
        Assert.Contains("WaitUntilLoadedAsync", lifecycle, StringComparison.Ordinal);
        Assert.Contains("catch (OperationCanceledException) when (_cancellation.IsCancellationRequested)", lifecycle, StringComparison.Ordinal);
        Assert.Contains("_lifecycle.StartInitialization(cancellationToken => InitializeAsync(options, cancellationToken));", main, StringComparison.Ordinal);
        Assert.Contains("await _lifecycle.WaitUntilLoadedAsync(cancellationToken);", main, StringComparison.Ordinal);
        Assert.Contains("_viewModel.InitializeAsync(options, cancellationToken)", main, StringComparison.Ordinal);
        Assert.Contains("_application.GetSettingsAsync(cancellationToken)", main, StringComparison.Ordinal);
        Assert.Contains("_lifecycle.Cancel();", main, StringComparison.Ordinal);
        Assert.Contains("_lifecycle.Dispose();", main, StringComparison.Ordinal);
    }

    /// <summary>Verifies that migrated top-level windows delegate native chrome, insets, and palette handling.</summary>
    [Fact]
    public void TopLevelWindows_DelegateNativeChromeInsetsAndPaletteToSharedController()
    {
        foreach (var windowName in MigratedTopLevelWindows)
        {
            var source = File.ReadAllText(RepositoryFile("WorkTrail", $"{windowName}.xaml.cs"));

            Assert.Contains("new CustomTitleBarController(", source, StringComparison.Ordinal);
            Assert.Contains("_titleBar.Dispose();", source, StringComparison.Ordinal);
            Assert.DoesNotContain("SetTitleBar(", source, StringComparison.Ordinal);
            Assert.DoesNotContain("UpdateTitleBarInsets", source, StringComparison.Ordinal);
            Assert.DoesNotContain("AppWindowTitleBar", source, StringComparison.Ordinal);
        }
    }

    /// <summary>Verifies that standard top-level title bars share the PNG brand mark and 48-DIP alignment.</summary>
    [Fact]
    public void StandardTopLevelTitleBars_UseSharedPngBrandMarkAndFortyEightDipAlignment()
    {
        foreach (var windowName in MigratedTopLevelWindows.Where(static name => name != "SearchWindow"))
        {
            var document = XDocument.Load(RepositoryFile("WorkTrail", $"{windowName}.xaml"));
            var titleBar = document.Descendants().Single(element => HasName(element, "TitleBarDragRegion"));

            Assert.Equal("48", titleBar.Attribute("Height")?.Value);
            Assert.Contains(
                titleBar.Descendants(),
                element => element.Name.LocalName == "Image"
                    && element.Attribute("Style")?.Value == "{StaticResource WorkTrailTitleBarLogoStyle}");
        }
    }

    /// <summary>Verifies that floating search opts out of tall chrome while retaining its fixed light theme.</summary>
    [Fact]
    public void FloatingSearch_PreservesCompactFixedLightChromeThroughControllerOptOut()
    {
        var document = XDocument.Load(RepositoryFile("WorkTrail", "SearchWindow.xaml"));
        var source = File.ReadAllText(RepositoryFile("WorkTrail", "SearchWindow.xaml.cs"));
        var titleBar = document.Descendants().Single(element => HasName(element, "TitleBarDragRegion"));

        Assert.Equal("40", FirstRowHeight(document));
        Assert.Equal("40", titleBar.Attribute("Height")?.Value);
        Assert.Contains("useTallTitleBar: false", source, StringComparison.Ordinal);
        Assert.Contains("_titleBar.ApplyTheme(ElementTheme.Light);", source, StringComparison.Ordinal);
    }

    /// <summary>Verifies that owned dialogs delegate chrome without changing their established header heights.</summary>
    [Fact]
    public void OwnedDialogs_DelegateChromeWithoutChangingTheirExistingHeaderHeight()
    {
        var compactDialogs = new HashSet<string>(StringComparer.Ordinal)
        {
            "ActivityCalendarDialogWindow",
            "AiPricingDialogWindow",
            "WorldClockCityPickerDialogWindow"
        };

        foreach (var windowName in MigratedDialogWindows)
        {
            var source = File.ReadAllText(RepositoryFile("WorkTrail", $"{windowName}.xaml.cs"));
            var document = XDocument.Load(RepositoryFile("WorkTrail", $"{windowName}.xaml"));
            var titleBar = document.Descendants().Single(element => HasName(element, "TitleDragRegion"));

            Assert.Contains("new CustomTitleBarController(", source, StringComparison.Ordinal);
            Assert.Contains("_titleBar.Dispose();", source, StringComparison.Ordinal);
            Assert.DoesNotContain("SetTitleBar(", source, StringComparison.Ordinal);
            Assert.DoesNotContain("AppWindowTitleBar", source, StringComparison.Ordinal);
            Assert.Contains(titleBar.Descendants(), element => HasName(element, "TitleBarLeftInsetColumn"));
            Assert.Contains(titleBar.Descendants(), element => HasName(element, "TitleBarRightInsetColumn"));
            Assert.Equal(compactDialogs.Contains(windowName) ? "44" : "48", FirstRowHeight(document));
            Assert.Equal(compactDialogs.Contains(windowName), source.Contains("useTallTitleBar: false", StringComparison.Ordinal));
        }
    }

    /// <summary>Verifies that window code-behind no longer owns custom title-bar setup directly.</summary>
    [Fact]
    public void WindowCodeBehind_HasNoRemainingDirectCustomTitleBarOwnership()
    {
        var windowDirectory = Path.GetDirectoryName(RepositoryFile("WorkTrail", "MainWindow.xaml.cs"))!;
        var offenders = Directory
            .EnumerateFiles(windowDirectory, "*.xaml.cs", SearchOption.TopDirectoryOnly)
            .Where(path =>
            {
                var source = File.ReadAllText(path);
                return source.Contains("SetTitleBar(", StringComparison.Ordinal)
                    || source.Contains("ExtendsContentIntoTitleBar", StringComparison.Ordinal)
                    || source.Contains("AppWindowTitleBar", StringComparison.Ordinal);
            })
            .Select(Path.GetFileName)
            .ToArray();

        Assert.Empty(offenders);
    }

    private static string FirstRowHeight(XDocument document) => document
        .Descendants()
        .First(element => element.Name.LocalName == "Grid.RowDefinitions")
        .Elements()
        .First()
        .Attribute("Height")?.Value ?? string.Empty;

    private static void AssertSetter(XElement style, string property, string value) =>
        Assert.Contains(
            style.Descendants(),
            element => element.Name.LocalName == "Setter"
                && element.Attribute("Property")?.Value == property
                && element.Attribute("Value")?.Value == value);

    private static bool HasName(XElement element, string name) =>
        element.Attributes().Any(attribute => attribute.Name.LocalName == "Name" && attribute.Value == name);

    private static string? KeyValue(XElement element) =>
        element.Attributes().FirstOrDefault(attribute => attribute.Name.LocalName == "Key")?.Value;

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
