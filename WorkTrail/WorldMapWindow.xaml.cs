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


using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using WorkTrail.Application;
using WorkTrail.Services;

namespace WorkTrail;

/// <summary>Renders the shared astronomical projection in an independent Acrylic window.</summary>
internal sealed partial class WorldMapWindow : Window
{
    private readonly AstronomyWindowController _controller;
    private LocalizationService _strings = new("system");

    /// <summary>Creates the independently restorable map over the shared application facade.</summary>
    internal WorldMapWindow(IWorkTrailApplication application, MicaDialogService dialogs, AppSettings settings)
    {
        InitializeComponent();
        _controller = new AstronomyWindowController(
            this, RootGrid, TitleBarDragRegion, TitleBarLeftInsetColumn, TitleBarRightInsetColumn,
            LoadingIndicator, WorldMapNotificationBanner, application, dialogs,
            WindowStateKeys.WorldMap, 1120, 608, RenderSnapshot);
        ApplySettings(settings);
    }

    /// <summary>Applies the current theme and language without changing the selected reference instant.</summary>
    internal void ApplySettings(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _strings = new LocalizationService(settings.UiLanguage);
        Title = _strings.Translate("WorldClock.Map.Title");
        TitleBarText.Text = Title.ToUpper(_strings.Culture);
        WorldMapControl.ApplyLanguage(_strings);
        _controller.ApplySettings(settings);
    }

    /// <summary>Mirrors the clock reference instant, including its live or explicitly selected time mode.</summary>
    internal void ApplySnapshot(WorldClockSnapshot snapshot, bool isLive) => _controller.ApplySnapshot(snapshot, isLive);

    /// <summary>Closes the map after the composition root has persisted the open application session.</summary>
    internal void CloseForShutdown() => _controller.CloseForShutdown();

    /// <summary>Releases a failed opening without overwriting the last valid window placement.</summary>
    internal void CloseAfterFailedOpening() => _controller.CloseAfterFailedOpening();

    private void RootGrid_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        // Keep native caption space available even when only the compact logo fits beside it.
        var compact = e.NewSize.Width < 320d;
        TitleBarLogo.Margin = compact ? new Thickness(8, 0, 8, 0) : new Thickness(16, 0, 10, 0);
        TitleBarText.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        ReferenceInstantText.Visibility = e.NewSize.Width < 640d ? Visibility.Collapsed : Visibility.Visible;
    }

    private void RenderSnapshot(WorldClockSnapshot snapshot)
    {
        WorldMapControl.Apply(snapshot.Map, _strings);
        WorldMapControl.Visibility = Visibility.Visible;
        ReferenceInstantText.Text = $"{snapshot.InstantUtc.ToString("g", _strings.Culture)} UTC";
        var label = $"{_strings.Translate("WorldClock.ReferenceInstant")}: {ReferenceInstantText.Text}";
        AutomationProperties.SetName(ReferenceInstantText, label);
        ToolTipService.SetToolTip(ReferenceInstantText, label);
    }
}
