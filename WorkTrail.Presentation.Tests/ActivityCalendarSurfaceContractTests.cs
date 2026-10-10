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

public sealed class ActivityCalendarSurfaceContractTests
{
    [Fact]
    public void ActivityCalendar_UsesNativeCalendarAndAggregateApplicationFacade()
    {
        var dialog = XDocument.Load(RepositoryFile("WorkTrail", "ActivityCalendarDialogWindow.xaml"));
        var source = File.ReadAllText(RepositoryFile("WorkTrail", "ActivityCalendarDialogWindow.xaml.cs"));
        var calendar = dialog.Descendants().Single(element => HasName(element, "ActivityCalendarView"));
        var root = dialog.Descendants().Single(element => HasName(element, "RootGrid"));

        Assert.Equal("CalendarView", calendar.Name.LocalName);
        Assert.Equal("Single", calendar.Attribute("SelectionMode")?.Value);
        Assert.Equal("ActivityCalendarView_SelectedDatesChanged", calendar.Attribute("SelectedDatesChanged")?.Value);
        Assert.Equal("ActivityCalendarView_CalendarViewDayItemChanging", calendar.Attribute("CalendarViewDayItemChanging")?.Value);
        Assert.Equal("ActivityCalendarView_DoubleTapped", calendar.Attribute("DoubleTapped")?.Value);
        Assert.Equal("True", calendar.Attribute("IsDoubleTapEnabled")?.Value);
        Assert.Equal("Transparent", root.Attribute("Background")?.Value);
        Assert.Contains(dialog.Descendants(), element => element.Name.LocalName == "DesktopAcrylicBackdrop");
        Assert.Contains(dialog.Descendants(), element => HasName(element, "ScoreValueText"));
        Assert.Contains(dialog.Descendants(), element => HasName(element, "ActiveTimeValueText"));
        Assert.Contains(dialog.Descendants(), element => HasName(element, "IdleTimeValueText"));
        Assert.Contains(dialog.Descendants(), element => HasName(element, "TrackedTimeValueText"));
        Assert.Contains(dialog.Descendants(), element => HasName(element, "KeyPressesValueText"));
        Assert.Contains(dialog.Descendants(), element => HasName(element, "MouseClicksValueText"));
        Assert.Contains(dialog.Descendants(), element => HasName(element, "InstallationLegendSection"));
        Assert.Contains(dialog.Descendants(), element => HasName(element, "InstallationLegendItems"));
        var reprocess = dialog.Descendants().Single(element => HasName(element, "ReprocessAiButton"));
        Assert.Equal("ReprocessAiButton_Click", reprocess.Attribute("Click")?.Value);
        Assert.DoesNotContain(dialog.Descendants(), element => HasName(element, "OpenGalleryButton"));
        Assert.DoesNotContain(dialog.Descendants(), element => element.Name.LocalName is "WebView2" or "Frame");

        Assert.Contains("_application.GetReportAsync", source, StringComparison.Ordinal);
        Assert.Contains("new ReportQuery(from, today, string.Empty, ReportView.Calendar)", source, StringComparison.Ordinal);
        Assert.Contains("today.AddDays(-365)", source, StringComparison.Ordinal);
        Assert.True(
            source.IndexOf("TryApplySnapshot(result.Value)", StringComparison.Ordinal) <
            source.IndexOf("ActivityCalendarView.MinDate = ToCalendarDate(from)", StringComparison.Ordinal));
        Assert.Contains("ExpectedReportContractVersion = 6", source, StringComparison.Ordinal);
        Assert.Contains("cell.ActivityScore", source, StringComparison.Ordinal);
        Assert.Contains("cell.Installations", source, StringComparison.Ordinal);
        Assert.Contains("InstallationAppearance.CreateAccentBrush", source, StringComparison.Ordinal);
        Assert.Contains("InstallationAppearance.GetIconGlyph", source, StringComparison.Ordinal);
        Assert.Contains("BuildInstallationAccessibleLabel", source, StringComparison.Ordinal);
        Assert.Contains("args.Item.Background = GetActivityHeatBrush(score)", source, StringComparison.Ordinal);
        Assert.Contains("args.Item.ClearValue(Control.BackgroundProperty)", source, StringComparison.Ordinal);
        Assert.DoesNotContain("SetDensityColors", source, StringComparison.Ordinal);
        Assert.Contains("WindowStateKeys.ActivityCalendar", source, StringComparison.Ordinal);
        Assert.Contains("ActivityCalendarDialogResult", source, StringComparison.Ordinal);
        Assert.Contains("new ActivityCalendarDialogResult(_selectedDate, ActivityCalendarAction.ReprocessDescriptions)", source, StringComparison.Ordinal);
        Assert.Contains("new ActivityCalendarDialogResult(date, ActivityCalendarAction.OpenScreenshots)", source, StringComparison.Ordinal);
        Assert.Contains("FindCalendarDayItem(e.OriginalSource as DependencyObject)", source, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.SetName(args.Item", source, StringComparison.Ordinal);
        Assert.Contains("ToolTipService.SetToolTip(args.Item", source, StringComparison.Ordinal);
        Assert.DoesNotContain("LocalStore", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Sqlite", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("File.", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Directory.", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Process.", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Environment.", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ActivityMenu_OpensCalendarThroughSharedDialogCoordinator()
    {
        var main = XDocument.Load(RepositoryFile("WorkTrail", "MainWindow.xaml"));
        var mainSource = File.ReadAllText(RepositoryFile("WorkTrail", "MainWindow.xaml.cs"));
        var dialogs = File.ReadAllText(RepositoryFile("WorkTrail", "MicaDialogService.cs"));
        var menuItem = main.Descendants().Single(element => HasName(element, "QuickActivityCalendarMenuItem"));

        Assert.Equal("ActivityCalendar.MenuTitle", menuItem.Attribute("Tag")?.Value);
        Assert.Equal("ActivityCalendarMenuItem_Click", menuItem.Attribute("Click")?.Value);
        Assert.Contains(menuItem.Ancestors(), element => HasName(element, "MainMenuFlyout"));
        Assert.DoesNotContain(main.Descendants(), element => HasName(element, "ActivityMenu"));
        Assert.Contains("QuickActivityCalendarMenuItem.Text = T(\"ActivityCalendar.MenuTitle\")", mainSource, StringComparison.Ordinal);
        Assert.Contains("await _dialogs.ShowActivityCalendarAsync(_application, this, RootGrid.RequestedTheme, _strings)", mainSource, StringComparison.Ordinal);
        Assert.Contains("ScreenshotGalleryDateRequested?.Invoke", mainSource, StringComparison.Ordinal);
        Assert.Contains("internal async Task<DateOnly?> ShowActivityCalendarAsync", dialogs, StringComparison.Ordinal);
        Assert.Contains("new ActivityCalendarDialogWindow(application, theme, strings, ownerAppWindow, ownerHandle)", dialogs, StringComparison.Ordinal);
        Assert.Contains("result.Action == ActivityCalendarAction.OpenScreenshots", dialogs, StringComparison.Ordinal);
        Assert.Contains("using var session = await _queue.EnterAsync(ownerLifetime.Token)", dialogs, StringComparison.Ordinal);
        Assert.Contains("WindowInteropService.DisableCurrentThreadPeerWindows(dialogHandle)", dialogs, StringComparison.Ordinal);
        Assert.Contains("var result = await ShowDialogWindowAsync(dialog", dialogs, StringComparison.Ordinal);
        Assert.Contains("new AiScreenshotReprocessingDialogWindow(", dialogs, StringComparison.Ordinal);
        Assert.Contains("retain the same queue lease", dialogs, StringComparison.Ordinal);
    }

    [Fact]
    public void ActivityCalendar_OpensTheSharedScreenshotInspectorOnTheSelectedDate()
    {
        var app = File.ReadAllText(RepositoryFile("WorkTrail", "App.xaml.cs"));
        var screenshotWindow = File.ReadAllText(RepositoryFile("WorkTrail", "ScreenshotWindow.xaml.cs"));

        Assert.Contains("_window.ScreenshotGalleryDateRequested += MainWindow_ScreenshotGalleryDateRequested", app, StringComparison.Ordinal);
        Assert.Contains("await ShowScreenshotWindowAsync(StartOrConnectRuntime(), null, eventArgs.Date)", app, StringComparison.Ordinal);
        Assert.Contains("await _screenshotsWindow.FocusDateAsync(selectedDate)", app, StringComparison.Ordinal);
        Assert.Contains("new ScreenshotWindow(application, _dialogs, launchTheme, requestedDate: selectedDate)", app, StringComparison.Ordinal);
        Assert.Contains("public async Task FocusDateAsync(DateOnly date)", screenshotWindow, StringComparison.Ordinal);
        Assert.Contains("_requestedScreenshotPath is null && _requestedDate is null ? null : _selectedDate", screenshotWindow, StringComparison.Ordinal);
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
