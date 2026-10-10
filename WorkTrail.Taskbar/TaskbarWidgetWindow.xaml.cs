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


using System.Collections.Generic;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using WorkTrail.Application;
using WorkTrail.Services;

namespace WorkTrail.Taskbar;

/// <summary>Renders the alpha-capable compact taskbar controls and forwards actions to the shared application facade.</summary>
public sealed partial class TaskbarWidgetWindow : Window
{
    private readonly IWorkTrailApplication _application;
    private readonly DashboardRefreshCoordinator _dashboardRefreshCoordinator;
    private readonly DispatcherTimer _spanLabelSaveTimer;
    private IDisposable? _dashboardSubscription;
    private bool _closed;
    private bool _updatingSpanLabel;
    private bool _spanLabelSaveInProgress;
    private bool _isTracking;
    private LocalizationService _strings = new("system");

    /// <summary>Initializes the transparent taskbar control.</summary>
    public TaskbarWidgetWindow(IWorkTrailApplication application, DashboardRefreshCoordinator dashboardRefreshCoordinator)
    {
        _application = application;
        _dashboardRefreshCoordinator = dashboardRefreshCoordinator ?? throw new ArgumentNullException(nameof(dashboardRefreshCoordinator));
        InitializeComponent();
        ApplyLocalization();

        _spanLabelSaveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
        _spanLabelSaveTimer.Tick += SpanLabelSaveTimer_Tick;
        Loaded += TaskbarWidgetWindow_Loaded;
        Closed += (_, _) =>
        {
            _closed = true;
            _dashboardSubscription?.Dispose();
            _dashboardSubscription = null;
            _spanLabelSaveTimer.Stop();
            RecordingGlow.BeginAnimation(OpacityProperty, null);
        };
    }

    /// <summary>Occurs when the user wants to open the full WorkTrail flyout.</summary>
    public event EventHandler? FlyoutRequested;

    /// <summary>Gets the native transparent HWND used by the shared Core taskbar host.</summary>
    internal IntPtr Handle => new WindowInteropHelper(this).EnsureHandle();

    /// <summary>Creates the hidden WPF HWND at the logical taskbar size before Core reparents it into Explorer.</summary>
    internal void PrepareForTaskbar(TaskbarWidgetHost.TaskbarWidgetBounds bounds)
    {
        // Size and position the hidden window in WPF DIPs so its HWND adopts the taskbar monitor's DPI.
        Width = bounds.Width / bounds.Scale;
        Height = bounds.Height / bounds.Scale;
        Left = bounds.ScreenX / bounds.Scale;
        Top = bounds.ScreenY / bounds.Scale;
        Opacity = 1;
        _ = Handle;
    }

    /// <summary>Applies presentation colors from the persisted app theme without accessing external state.</summary>
    internal void ApplySettings(AppSettings settings)
    {
        _strings = new LocalizationService(settings.UiLanguage);
        ApplyLocalization();
        var isLight = settings.Theme == "light";
        var foreground = new SolidColorBrush(isLight
            ? Color.FromRgb(23, 59, 63)
            : Color.FromRgb(244, 245, 241));
        PlayPauseIcon.Foreground = foreground;
        if (!SpanLabelTextBox.IsKeyboardFocusWithin)
        {
            SetSpanLabelText(settings.SpanLabel);
        }
    }

    private void TaskbarWidgetWindow_Loaded(object sender, RoutedEventArgs e) =>
        _dashboardSubscription = _dashboardRefreshCoordinator.Subscribe(OnDashboardStateChanged);

    private void OpenFlyoutButton_Click(object sender, RoutedEventArgs e) => FlyoutRequested?.Invoke(this, EventArgs.Empty);

    private async void TrackingButton_Click(object sender, RoutedEventArgs e)
    {
        var result = await _application.ToggleTrackingAsync(CancellationToken.None);
        if (result.Succeeded && result.Value is not null)
        {
            UpdateWidget(result.Value);
        }
    }

    private void OnDashboardStateChanged(DashboardState state)
    {
        if (_closed)
        {
            return;
        }

        _ = Dispatcher.BeginInvoke(() =>
        {
            if (!_closed)
            {
                UpdateWidget(state);
            }
        });
    }

    private void UpdateWidget(DashboardState state)
    {
        _isTracking = state.IsTracking;
        PlayPauseIcon.Text = state.IsTracking ? "\uE769" : "\uE768";
        var actionName = _strings.Translate(state.IsTracking ? "TrackingActionPause" : "TrackingActionStart");
        TrackingButton.ToolTip = actionName;
        AutomationProperties.SetName(TrackingButton, actionName);
        SetRecordingVisual(state.IsTracking);
        if (!SpanLabelTextBox.IsKeyboardFocusWithin)
        {
            SetSpanLabelText(state.SpanLabel);
        }
    }

    private void ApplyLocalization()
    {
        Title = _strings.Translate("Taskbar.WindowTitle");
        RootGrid.Language = XmlLanguage.GetLanguage(_strings.Language);
        var openLabel = _strings.Translate("Taskbar.Open");
        OpenFlyoutButton.ToolTip = openLabel;
        AutomationProperties.SetName(OpenFlyoutButton, openLabel);
        SpanLabelWatermark.Text = _strings.Translate("Taskbar.SpanLabel");
        AutomationProperties.SetName(SpanLabelTextBox, _strings.Translate("Taskbar.SpanLabel"));
        SpanLabelTextBox.ToolTip = _strings.Translate("Taskbar.SpanLabel.Tooltip");
        SetRecordingVisual(_isTracking);
    }

    private void SpanLabelTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        SpanLabelWatermark.Visibility = string.IsNullOrEmpty(SpanLabelTextBox.Text)
            ? Visibility.Visible
            : Visibility.Collapsed;
        if (_updatingSpanLabel)
        {
            return;
        }

        _spanLabelSaveTimer.Stop();
        _spanLabelSaveTimer.Start();
    }

    private async void SpanLabelSaveTimer_Tick(object? sender, EventArgs e)
    {
        _spanLabelSaveTimer.Stop();
        if (_spanLabelSaveInProgress)
        {
            return;
        }

        _spanLabelSaveInProgress = true;
        var label = SpanLabelTextBox.Text;
        try
        {
            var result = await _application.PatchSettingsAsync(
                new SettingsPatch(new Dictionary<string, string?> { ["activity.span_label"] = label }),
                CancellationToken.None);
            if (result.Succeeded && result.Value is not null)
            {
                SetSpanLabelText(result.Value.SpanLabel);
            }
        }
        finally
        {
            _spanLabelSaveInProgress = false;
            if (!string.Equals(label, SpanLabelTextBox.Text, StringComparison.Ordinal))
            {
                _spanLabelSaveTimer.Start();
            }
        }
    }

    private void SetSpanLabelText(string? label)
    {
        _updatingSpanLabel = true;
        SpanLabelTextBox.Text = label ?? string.Empty;
        _updatingSpanLabel = false;
        SpanLabelWatermark.Visibility = string.IsNullOrEmpty(SpanLabelTextBox.Text)
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void SetRecordingVisual(bool isTracking)
    {
        RecordingGlow.BeginAnimation(OpacityProperty, null);
        if (!isTracking)
        {
            RecordingLed.Fill = new SolidColorBrush(Color.FromRgb(119, 128, 141));
            RecordingGlow.Opacity = 0;
            RecordingIndicator.ToolTip = _strings.Translate("Taskbar.State.Paused");
            AutomationProperties.SetName(RecordingIndicator, _strings.Translate("Taskbar.State.Paused"));
            return;
        }

        RecordingLed.Fill = new SolidColorBrush(Color.FromRgb(244, 61, 75));
        RecordingIndicator.ToolTip = _strings.Translate("Taskbar.State.Running");
        AutomationProperties.SetName(RecordingIndicator, _strings.Translate("Taskbar.State.Running"));
        RecordingGlow.BeginAnimation(
            OpacityProperty,
            new DoubleAnimation
            {
                From = 0.16,
                To = 0.48,
                Duration = TimeSpan.FromMilliseconds(1100),
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever
            });
    }
}
