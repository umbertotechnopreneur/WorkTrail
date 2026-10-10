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


using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using WorkTrail.Application;
using WorkTrail.Services;

namespace WorkTrail;

/// <summary>Collects screenshot scheduling input in a detached themed window.</summary>
public sealed partial class ScheduleWindow : Window
{
    private const int LogicalWindowWidth = 1180;
    private const int LogicalWindowHeight = 760;
    private const int LogicalScreenMargin = 24;
    private readonly AppWindow _appWindow;
    private readonly CustomTitleBarController _titleBar;
    private readonly WindowPlacementService _placement;
    private readonly CancellationTokenSource _lifetimeCancellation = new();
    private readonly IWorkTrailApplication _application;
    private readonly MicaDialogService _dialogs;
    private LocalizationService _strings;
    private string _theme;

    /// <summary>Occurs after the user confirms a valid screenshot schedule.</summary>
    public event EventHandler<ScheduleConfigurationEventArgs>? ScheduleConfirmed;

    /// <summary>Creates a detached schedule editor with the supplied working hours, interval and theme.</summary>
    internal ScheduleWindow(
        IReadOnlyList<ActiveHoursDay>? activeHours,
        int intervalMinutes,
        string theme,
        string uiLanguage,
        IWorkTrailApplication application,
        MicaDialogService dialogs)
    {
        ArgumentNullException.ThrowIfNull(application);
        _application = application;
        _dialogs = dialogs ?? throw new ArgumentNullException(nameof(dialogs));
        _strings = new LocalizationService(uiLanguage);
        _theme = theme;
        InitializeComponent();
        _appWindow = AppWindow.GetFromWindowId(Win32Interop.GetWindowIdFromWindow(WinRT.Interop.WindowNative.GetWindowHandle(this)));
        _titleBar = new CustomTitleBarController(
            this,
            _appWindow,
            RootGrid,
            TitleBarDragRegion,
            TitleBarLeftInsetColumn,
            TitleBarRightInsetColumn,
            () => []);
        _placement = new WindowPlacementService(application, this, _appWindow, WindowStateKeys.Schedule, LogicalWindowWidth, LogicalWindowHeight, LogicalScreenMargin);
        ApplyTheme(theme);
        ApplyLanguage(uiLanguage);
        IntervalNumberBox.Value = intervalMinutes is >= 1 and <= 1440 ? intervalMinutes : 5;
        WorkingHoursEditor.LoadSchedule(activeHours);
        _placement.ApplyDefaultBounds(RootGrid);
        Closed += ScheduleWindow_Closed;
    }

    /// <summary>Applies the active application theme to the detached schedule editor.</summary>
    public void ApplyTheme(string theme)
    {
        _theme = theme;
        RootGrid.RequestedTheme = theme switch
        {
            "light" => ElementTheme.Light,
            "dark" => ElementTheme.Dark,
            _ => ElementTheme.Default
        };
        _titleBar.ApplyTheme(RootGrid.RequestedTheme == ElementTheme.Default ? RootGrid.ActualTheme : RootGrid.RequestedTheme);
    }

    /// <summary>Applies localized labels to schedule-specific commands and confirmations.</summary>
    public void ApplyLanguage(string uiLanguage)
    {
        _strings = new LocalizationService(uiLanguage);
        Title = _strings.Translate("Schedule.WindowTitle");
        UiLocalization.Apply(RootGrid, _strings);
        TitlePremiumBadge.Text = _strings.Translate("Premium.Badge");
        IntervalNumberBox.Header = null;
        AutomationProperties.SetName(IntervalNumberBox, _strings.Translate("Schedule.Interval.Value.Header"));
        WorkingHoursEditor.ApplyLanguage(uiLanguage);
        AutomationProperties.SetName(StandardWorkWeekButton, _strings.Translate("Schedule.Preset.WorkWeek.Accessible"));
        AutomationProperties.SetName(ClearAllHoursButton, _strings.Translate("Schedule.ClearAll.Accessible"));
    }

    private async void RootGrid_Loaded(object sender, RoutedEventArgs e)
    {
        _placement.ApplyDefaultBounds(RootGrid);
        await _placement.RestoreOrCenterAsync(RootGrid, _lifetimeCancellation.Token);
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e) => Close();

    private async void StandardWorkWeekButton_Click(object sender, RoutedEventArgs e)
    {
        if (await ConfirmReplacementAsync("Schedule.Preset.WorkWeek.Title", "Schedule.Preset.WorkWeek.Message"))
        {
            WorkingHoursEditor.ApplyStandardWorkWeek();
        }
    }

    private async void ClearAllHoursButton_Click(object sender, RoutedEventArgs e)
    {
        if (await ConfirmReplacementAsync("Schedule.ClearAll.Title", "Schedule.ClearAll.Message"))
        {
            WorkingHoursEditor.ClearAll();
        }
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        var intervalMinutes = (int)IntervalNumberBox.Value;
        ScheduleConfirmed?.Invoke(this, new ScheduleConfigurationEventArgs(intervalMinutes, WorkingHoursEditor.GetSchedule()));
    }

    private async Task<bool> ConfirmReplacementAsync(string titleKey, string messageKey)
    {
        return await _dialogs.ConfirmAsync(
            this,
            DialogRequest.Confirmation(
                _strings.Translate(titleKey),
                _strings.Translate(messageKey),
                _strings.Translate("Dialog.Ok"),
                _strings.Translate("Dialog.Cancel")));
    }

    private async void ScheduleWindow_Closed(object sender, WindowEventArgs args)
    {
        _ = await _placement.TrySaveForCloseAsync(CancellationToken.None);
        _placement.Dispose();
        _titleBar.Dispose();
        _lifetimeCancellation.Cancel();
        _lifetimeCancellation.Dispose();
    }
}

/// <summary>Provides the screenshot scheduling values confirmed by the detached editor.</summary>
public sealed class ScheduleConfigurationEventArgs(int intervalMinutes, IReadOnlyList<ActiveHoursDay> activeHours) : EventArgs
{
    /// <summary>Gets the interval in minutes between eligible snapshots.</summary>
    public int IntervalMinutes { get; } = intervalMinutes;

    /// <summary>Gets the selected active periods and breaks for each day.</summary>
    public IReadOnlyList<ActiveHoursDay> ActiveHours { get; } = activeHours;
}
