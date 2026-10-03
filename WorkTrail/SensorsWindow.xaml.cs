// SPDX-License-Identifier: MIT

using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WorkTrail.Application;
using WorkTrail.Controls;
using WorkTrail.Presentation;
using WorkTrail.Services;

namespace WorkTrail;

/// <summary>Hosts live sensor tracks and their settings in one independent Acrylic window.</summary>
public sealed partial class SensorsWindow : Window
{
    private readonly IWorkTrailApplication _application;
    private readonly AppWindow _appWindow;
    private readonly CustomTitleBarController _titleBar;
    private readonly WindowPlacementService _placement;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly DispatcherQueueTimer _timer;
    private readonly SensorTraceHistory _history = new();
    private readonly Dictionary<string, SensorTrackControl> _tracks = new(StringComparer.Ordinal);
    private IReadOnlyList<SensorMonitorRow> _rows = [];
    private DateTimeOffset _snapshotTimestamp;
    private double _measuredWidth = -1;
    private double _observedRowHeight;
    private int _pageIndex;
    private string _snapshotStatus = string.Empty;
    private AppSettings _settings;
    private LocalizationService _strings = new("system");
    private bool _loaded;
    private bool _closed;
    private bool _refreshing;
    private bool _optionsVisible;
    private bool _placementFailed;
    private bool _arrangingTracks;

    /// <summary>Publishes confirmed settings to the main window and its sibling surfaces.</summary>
    internal event Action<AppSettings>? SettingsSaved;

    /// <summary>Creates a presentation-only monitor over the installation's shared facade.</summary>
    internal SensorsWindow(IWorkTrailApplication application, AppSettings settings)
    {
        _application = application;
        _settings = settings;
        InitializeComponent();
        SystemBackdrop = new GlassBackdrop();
        _appWindow = AppWindow.GetFromWindowId(Win32Interop.GetWindowIdFromWindow(WinRT.Interop.WindowNative.GetWindowHandle(this)));
        _titleBar = new CustomTitleBarController(this, _appWindow, RootGrid, HeaderDragRegion,
            TitleBarLeftInsetColumn, TitleBarRightInsetColumn, () => [BackButton, OptionsButton]);
        _placement = new WindowPlacementService(application, this, _appWindow, WindowStateKeys.Sensors, 1040, 800, 24);
        _timer = DispatcherQueue.CreateTimer();
        _timer.IsRepeating = false;
        _timer.Tick += Timer_Tick;
        SensorOptions.Initialize(application, settings, _lifetime.Token);
        SensorOptions.SettingsSaved += SensorOptions_SettingsSaved;
        _appWindow.Changed += AppWindow_Changed;
        Closed += SensorsWindow_Closed;
        ApplySettings(settings);
        _placement.ApplyDefaultBounds(RootGrid);
    }

    /// <summary>Applies shared theme, language and confirmed sampling preferences.</summary>
    internal void ApplySettings(AppSettings settings)
    {
        if (_closed) return;
        _measuredWidth = -1;
        _settings = settings;
        _strings = new LocalizationService(settings.UiLanguage);
        RootGrid.RequestedTheme = settings.Theme switch { "light" => ElementTheme.Light, "dark" => ElementTheme.Dark, _ => ElementTheme.Default };
        Title = T("Sensors.Title");
        UiLocalization.Apply(RootGrid, _strings);
        UiLocalization.SetAccessibleLabel(OptionsButton, T("Sensors.Options.Open"));
        UiLocalization.SetAccessibleLabel(BackButton, T("Sensors.Back"));
        UiLocalization.SetAccessibleLabel(PreviousPageButton, T("Sensors.PreviousPage"));
        UiLocalization.SetAccessibleLabel(NextPageButton, T("Sensors.NextPage"));
        UpdateHistoryTooltip();
        HeaderText.Text = T(_optionsVisible ? "Sensors.Options.Title" : "Sensors.Title");
        SensorOptions.ApplyState(settings);
        _titleBar.ApplyTheme(RootGrid.RequestedTheme == ElementTheme.Default ? RootGrid.ActualTheme : RootGrid.RequestedTheme);
        _titleBar.QueueLayoutUpdate();
        if (_loaded && !_optionsVisible) _ = RefreshAsync();
    }

    /// <summary>Closes the monitor while the composition root preserves the current workspace.</summary>
    internal void CloseForShutdown() { _lifetime.Cancel(); Close(); }

    private async void RootGrid_Loaded(object sender, RoutedEventArgs e)
    {
        if (_loaded) return;
        _loaded = true;
        try { await _placement.RestoreAsync(RootGrid, _lifetime.Token); }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { return; }
        catch (Exception)
        {
            // Placement errors are reported explicitly; default bounds keep the monitor reachable.
            _placementFailed = true;
            ShowStatus(string.Empty);
        }
        await RefreshAsync();
    }

    private async void Timer_Tick(DispatcherQueueTimer sender, object args) => await RefreshAsync();

    private bool CanRefresh => !_closed && !_lifetime.IsCancellationRequested && !_optionsVisible
        && _appWindow.Presenter is not OverlappedPresenter { State: OverlappedPresenterState.Minimized };

    private async Task RefreshAsync()
    {
        if (!CanRefresh || _refreshing) return;
        _timer.Stop();
        _refreshing = true;
        try
        {
            var result = await _application.CaptureHardwareSnapshotAsync(_lifetime.Token);
            if (!CanRefresh) return;
            if (result is not { Succeeded: true, Value: { } snapshot })
            {
                ClearTracks();
                _snapshotStatus = T("SystemSnapshotFailed");
                UpdateHistoryTooltip();
                ShowStatus(T("SystemSnapshotFailed"));
                return;
            }
            var status = HardwareSnapshotProjection.Create(snapshot, _strings.Culture, _strings.Translate);
            _snapshotStatus = $"{status.Status} · {status.DriverStatus} · {status.CollectedAt}";
            UpdateHistoryTooltip();
            var rows = SensorMonitorProjection.Create(snapshot, _strings.Culture, _strings.Translate);
            _history.Retain(rows);
            var ids = rows.Select(row => row.Id).ToHashSet(StringComparer.Ordinal);
            foreach (var removed in _tracks.Keys.Where(id => !ids.Contains(id)).ToArray())
            {
                TracksHost.Children.Remove(_tracks[removed]);
                _tracks.Remove(removed);
            }
            // Every device keeps its trace, while controls and trace copies belong to the visible page.
            foreach (var row in rows) _history.Record(row, snapshot.Timestamp);
            _snapshotTimestamp = snapshot.Timestamp;
            _rows = rows;
            RenderPage();
            // Ordinary partial readings are explained in the footer tooltip; failures remain visible.
            ShowStatus(snapshot.Status is not ("ready" or "partial") ? status.Status
                : rows.Count == 0 ? T("Sensors.NoDevices") : string.Empty);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { /* Closing stops this view's pending request. */ }
        catch (Exception)
        {
            // Failed refreshes remove old live values instead of presenting them as current measurements.
            if (!_closed)
            {
                ClearTracks();
                _snapshotStatus = T("SystemSnapshotFailed");
                UpdateHistoryTooltip();
                ShowStatus(T("SystemSnapshotFailed"));
            }
        }
        finally
        {
            _refreshing = false;
            if (CanRefresh)
            {
                _timer.Interval = HardwareSamplingProfiles.Get(_settings.HardwareSamplingProfile).CpuInterval;
                _timer.Start();
            }
        }
    }

    private void ClearTracks()
    {
        _tracks.Clear();
        _rows = [];
        _pageIndex = 0;
        _observedRowHeight = 0;
        TracksHost.Children.Clear();
        TracksHost.RowDefinitions.Clear();
        PageNavigation.Visibility = Visibility.Collapsed;
        _history.Retain([]);
    }

    private void ShowStatus(string message)
    {
        StatusText.Text = _placementFailed
            ? string.Join(" · ", new[] { T("WorldClock.PlacementFailed"), message }.Where(value => value.Length > 0)) : message;
        StatusText.Visibility = StatusText.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        ToolTipService.SetToolTip(StatusText, StatusText.Text);
    }

    private void UpdateHistoryTooltip() => ToolTipService.SetToolTip(HistoryText,
        string.Join("\n", new[] { T("Sensors.History"), _snapshotStatus }.Where(value => value.Length > 0)));

    private void TracksViewport_SizeChanged(object sender, SizeChangedEventArgs e) => RenderPage();

    private void RenderPage()
    {
        if (_closed || _arrangingTracks || TracksViewport.ActualHeight <= 0) return;
        // Derive the constraint from the window, never from a child's potentially overflowing desired size.
        var width = Math.Max(0, RootGrid.ActualWidth - MonitorSurface.Margin.Left - MonitorSurface.Margin.Right);
        if (width <= 0) return;
        _arrangingTracks = true;
        try
        {
            var layout = SensorMonitorLayout.ResolveTrack(width);
            ColumnHeaders.Width = width;
            ColumnHeaders.Visibility = layout.Stacked ? Visibility.Collapsed : Visibility.Visible;
            DeviceHeaderColumn.Width = new GridLength(layout.NameWidth);
            LoadHeaderColumn.Width = new GridLength(layout.ValueWidth);
            TemperatureHeaderColumn.Width = new GridLength(layout.TemperatureWidth);
            TracksHost.Width = width;
            if (_rows.Count == 0) return;
            if (Math.Abs(_measuredWidth - width) > 0.5)
            {
                _observedRowHeight = 0;
                _measuredWidth = width;
            }
            var rowHeight = Math.Max(layout.RowHeight, _observedRowHeight);
            var pageSize = SensorMonitorLayout.PageSize(TracksViewport.ActualHeight, rowHeight, _rows.Count);
            SensorTrackControl[] visible;
            int pageCount;
            while (true)
            {
                pageCount = (_rows.Count + pageSize - 1) / pageSize;
                _pageIndex = Math.Clamp(_pageIndex, 0, pageCount - 1);
                visible = _rows.Skip(_pageIndex * pageSize).Take(pageSize).Select(row =>
                {
                    if (!_tracks.TryGetValue(row.Id, out var control))
                        _tracks.Add(row.Id, control = new SensorTrackControl());
                    control.Apply(row, _history.GetPoints(row.Id), _snapshotTimestamp, _strings);
                    return control;
                }).ToArray();
                // Visible measurements can shrink the page until it fits; hidden controls are never measured.
                rowHeight = Math.Max(rowHeight, visible.Max(track => track.MeasureForViewport(width)));
                _observedRowHeight = rowHeight;
                var fittedSize = SensorMonitorLayout.PageSize(TracksViewport.ActualHeight, rowHeight, _rows.Count);
                if (fittedSize == pageSize) break;
                pageSize = fittedSize;
            }
            if (!TracksHost.Children.SequenceEqual(visible))
            {
                // Reparent only when the page changes; every device keeps receiving samples while off-page.
                TracksHost.Children.Clear();
                TracksHost.RowDefinitions.Clear();
                for (var index = 0; index < visible.Length; index++)
                {
                    TracksHost.RowDefinitions.Add(new RowDefinition { Height = new GridLength(rowHeight) });
                    Grid.SetRow(visible[index], index);
                    TracksHost.Children.Add(visible[index]);
                }
                TracksViewport.ChangeView(null, 0, null, disableAnimation: true);
            }
            foreach (var definition in TracksHost.RowDefinitions) definition.Height = new GridLength(rowHeight);
            PageNavigation.Visibility = pageCount > 1 ? Visibility.Visible : Visibility.Collapsed;
            PreviousPageButton.IsEnabled = _pageIndex > 0;
            NextPageButton.IsEnabled = _pageIndex < pageCount - 1;
            PageText.Text = string.Format(_strings.Culture, "{0} / {1}", _pageIndex + 1, pageCount);
        }
        finally { _arrangingTracks = false; }
    }

    private void PreviousPageButton_Click(object sender, RoutedEventArgs e) { _pageIndex--; RenderPage(); }

    private void NextPageButton_Click(object sender, RoutedEventArgs e) { _pageIndex++; RenderPage(); }

    private void OptionsButton_Click(object sender, RoutedEventArgs e)
    {
        _optionsVisible = true;
        _timer.Stop();
        _history.Retain([]);
        MonitorSurface.Visibility = Visibility.Collapsed;
        OptionsSurface.Visibility = Visibility.Visible;
        OptionsButton.Visibility = Visibility.Collapsed;
        BackButton.Visibility = Visibility.Visible;
        HeaderText.Text = T("Sensors.Options.Title");
        _titleBar.QueueLayoutUpdate();
        BackButton.Focus(FocusState.Programmatic);
    }

    private async void BackButton_Click(object sender, RoutedEventArgs e)
    {
        _optionsVisible = false;
        OptionsSurface.Visibility = Visibility.Collapsed;
        MonitorSurface.Visibility = Visibility.Visible;
        OptionsButton.Visibility = Visibility.Visible;
        BackButton.Visibility = Visibility.Collapsed;
        HeaderText.Text = T("Sensors.Title");
        _titleBar.QueueLayoutUpdate();
        OptionsButton.Focus(FocusState.Programmatic);
        await RefreshAsync();
    }

    private void SensorOptions_SettingsSaved(AppSettings settings) { ApplySettings(settings); SettingsSaved?.Invoke(settings); }

    private void AppWindow_Changed(AppWindow sender, AppWindowChangedEventArgs args)
    {
        if (!args.DidPresenterChange) return;
        if (!CanRefresh) { _timer.Stop(); _history.Retain([]); }
        else if (_loaded) _ = RefreshAsync();
    }

    private void SensorsWindow_Closed(object sender, WindowEventArgs args)
    {
        _closed = true;
        _lifetime.Cancel();
        _timer.Stop();
        _timer.Tick -= Timer_Tick;
        _appWindow.Changed -= AppWindow_Changed;
        SensorOptions.SettingsSaved -= SensorOptions_SettingsSaved;
        _titleBar.Dispose();
        _placement.Dispose();
        ClearTracks();
        _lifetime.Dispose();
    }

    private string T(string key) => _strings.Translate(key);
}
