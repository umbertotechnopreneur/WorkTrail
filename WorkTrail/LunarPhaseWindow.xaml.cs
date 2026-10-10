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
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using WorkTrail.Application;
using WorkTrail.Presentation;
using WorkTrail.Services;

namespace WorkTrail;

/// <summary>Shows the reference instant's photographed Moon and calculated phase in an independent Acrylic window.</summary>
internal sealed partial class LunarPhaseWindow : Window
{
    private readonly AstronomyWindowController _controller;
    private LocalizationService _strings = new("system");

    /// <summary>Creates the independently restorable lunar display over the shared application facade.</summary>
    internal LunarPhaseWindow(IWorkTrailApplication application, MicaDialogService dialogs, AppSettings settings)
    {
        InitializeComponent();
        _controller = new AstronomyWindowController(
            this, RootGrid, TitleBarDragRegion, TitleBarLeftInsetColumn, TitleBarRightInsetColumn,
            LoadingIndicator, LunarPhaseNotificationBanner, application, dialogs,
            WindowStateKeys.LunarPhase, 480, 560, RenderSnapshot);
        ApplySettings(settings);
    }

    /// <summary>Applies the current theme and language without changing the selected reference instant.</summary>
    internal void ApplySettings(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _strings = new LocalizationService(settings.UiLanguage);
        Title = _strings.Translate("WorldClock.MoonPhase.Title");
        TitleBarText.Text = Title.ToUpper(_strings.Culture);
        _controller.ApplySettings(settings);
    }

    /// <summary>Mirrors the clocks' live or explicitly selected astronomical projection.</summary>
    internal void ApplySnapshot(WorldClockSnapshot snapshot, bool isLive) => _controller.ApplySnapshot(snapshot, isLive);

    /// <summary>Closes the lunar display after the composition root has persisted the open application session.</summary>
    internal void CloseForShutdown() => _controller.CloseForShutdown();

    /// <summary>Releases a failed opening without overwriting the last valid window placement.</summary>
    internal void CloseAfterFailedOpening() => _controller.CloseAfterFailedOpening();

    private void RootGrid_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        var compactTitle = e.NewSize.Width < 320d;
        TitleBarLogo.Margin = compactTitle ? new Thickness(8, 0, 8, 0) : new Thickness(16, 0, 10, 0);
        TitleBarText.Visibility = compactTitle ? Visibility.Collapsed : Visibility.Visible;
        UpdateMoonLayout();
    }

    private void MoonContent_SizeChanged(object sender, SizeChangedEventArgs e) => UpdateMoonLayout();

    private void UpdateMoonLayout()
    {
        // Use the actual content slot so caption overlay changes and display scaling need no special-case geometry.
        // Adding the current margins keeps the thresholds stable when the responsive layout changes those margins.
        var availableWidth = MoonContent.ActualWidth + MoonContent.Margin.Left + MoonContent.Margin.Right;
        var availableHeight = MoonContent.ActualHeight + MoonContent.Margin.Top + MoonContent.Margin.Bottom;
        var moonOnly = availableWidth < 300d || availableHeight < 300d;
        var compact = availableWidth < 400d || availableHeight < 400d;
        MoonContent.Margin = moonOnly ? new Thickness(0)
            : compact ? new Thickness(8, 0, 8, 12) : new Thickness(20, 0, 20, 24);
        PhaseSummaryText.Visibility = moonOnly ? Visibility.Collapsed : Visibility.Visible;
        ReferenceInstantText.Visibility = moonOnly ? Visibility.Collapsed : Visibility.Visible;
        AutomationProperties.SetAccessibilityView(MoonPhaseControl, moonOnly ? AccessibilityView.Content : AccessibilityView.Raw);
        PhaseSummaryText.FontSize = compact ? 16d : 22d;
        PhaseSummaryText.TextWrapping = compact ? TextWrapping.NoWrap : TextWrapping.Wrap;
        ReferenceInstantText.FontSize = compact ? 10d : 12d;
        ReferenceInstantText.TextWrapping = compact ? TextWrapping.NoWrap : TextWrapping.Wrap;
    }

    private void RenderSnapshot(WorldClockSnapshot snapshot)
    {
        var phase = LunarPhaseProjection.Create(snapshot.Map.MoonPhaseAngleDegrees);
        MoonPhaseControl.MoonPhaseAngleDegrees = snapshot.Map.MoonPhaseAngleDegrees;
        PhaseSummaryText.Text = _strings.Format(
            "WorldClock.MoonPhase.Summary", _strings.Translate(phase.LocalizationKey), phase.IlluminatedPercentage);
        AutomationProperties.SetName(PhaseSummaryText, PhaseSummaryText.Text);
        ReferenceInstantText.Text = $"{snapshot.InstantUtc.ToString("g", _strings.Culture)} UTC";
        var label = $"{_strings.Translate("WorldClock.ReferenceInstant")}: {ReferenceInstantText.Text}";
        AutomationProperties.SetName(ReferenceInstantText, label);
        ToolTipService.SetToolTip(ReferenceInstantText, label);
        var moonDescription = $"{Title}. {PhaseSummaryText.Text}. {label}";
        AutomationProperties.SetName(MoonPhaseControl, moonDescription);
        ToolTipService.SetToolTip(MoonContent, moonDescription);
        MoonContent.Visibility = Visibility.Visible;
        UpdateMoonLayout();
    }
}
