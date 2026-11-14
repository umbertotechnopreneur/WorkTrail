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


using System.Globalization;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using WorkTrail.Application;
using WorkTrail.Controls;
using WorkTrail.Presentation;
using WorkTrail.Services;
using Windows.Graphics;

namespace WorkTrail;

/// <summary>Hosts the independent world-clock comparison and time-zone conversion surface.</summary>
public sealed partial class WorldClockWindow : Window
{
    #region Fields

    private const int LogicalWindowWidth = 1120;
    private const int LogicalWindowHeight = 720;
    private const int LogicalScreenMargin = 0;
    private readonly IWorkTrailApplication _application;
    private readonly MicaDialogService _dialogs;
    private readonly AppWindow _appWindow;
    private readonly CustomTitleBarController _titleBar;
    private readonly WindowPlacementService _placement;
    private readonly WorldClockWindowLayoutState _layoutState = new();
    private readonly CancellationTokenSource _lifetimeCancellation = new();
    private readonly DispatcherQueueTimer _refreshTimer;
    private string? _lastWeatherMessageKey;
    private readonly WorldClockSession _session;
    private readonly Dictionary<string, WorldClockColumnControl> _columns = new(StringComparer.Ordinal);
    private LocalizationService _strings = new("system");
    private readonly CancellationToken _sessionLifetime;
    private SvgImageSource? _weatherAttributionLogoSource;
    private XamlRoot? _xamlRoot;
    private bool _loaded;
    private bool _updatingReferenceControls;
    private bool _closed;
    private bool _weatherProviderLinkOpening;
    private bool _autoGroupCities;
    private bool _wasMinimized;

    #endregion

    #region Implementation

    private bool IsClosing => _closed || _lifetimeCancellation.IsCancellationRequested;

    /// <summary>Requests opening or focusing the independent world-map window.</summary>
    internal event EventHandler? WorldMapRequested;

    /// <summary>Requests opening or focusing the independent lunar-phase window.</summary>
    internal event EventHandler? LunarPhaseRequested;

    /// <summary>Requests opening a celestial work surface using its stable window identity.</summary>
    internal event Action<string>? CelestialWindowRequested;

    /// <summary>Requests opening the shared city settings.</summary>
    internal event Action<Window>? ConfigurationRequested;

    /// <summary>Requests adding a city to the shared selection.</summary>
    internal event Action<Window>? AddCityRequested;

    /// <summary>Displays feedback from the shared city picker.</summary>
    internal void ShowCityPickerFailure(string messageKey) => ShowFailure(messageKey);

    #endregion

    #region Initialization

    /// <summary>Creates the independent world-clock window over the shared application facade.</summary>
    internal WorldClockWindow(
        IWorkTrailApplication application,
        MicaDialogService dialogs,
        AppSettings settings,
        WorldClockSession session,
        CancellationToken sessionLifetime)
    {
        _application = application ?? throw new ArgumentNullException(nameof(application));
        _dialogs = dialogs ?? throw new ArgumentNullException(nameof(dialogs));
        ArgumentNullException.ThrowIfNull(settings);
        _session = session;
        _sessionLifetime = sessionLifetime;
        InitializeComponent();
        WorldMapButton.Flyout = AstronomyWindowMenu.Create(() => _strings, OpenAstronomyWindow, WindowStateKeys.WorldClocks);

        SystemBackdrop = new GlassBackdrop();
        _appWindow = AppWindow.GetFromWindowId(Win32Interop.GetWindowIdFromWindow(WinRT.Interop.WindowNative.GetWindowHandle(this)));
        _titleBar = new CustomTitleBarController(
            this,
            _appWindow,
            RootGrid,
            HeaderDragRegion,
            TitleBarLeftInsetColumn,
            TitleBarRightInsetColumn,
            () => [ReferenceInstantButton, WorldMapButton, LunarPhaseButton, OptionsButton],
            useTallTitleBar: true);
        _titleBar.ThemeChanged += TitleBar_ThemeChanged;

        _placement = new WindowPlacementService(
            _application,
            this,
            _appWindow,
            WindowStateKeys.WorldClocks,
            LogicalWindowWidth,
            LogicalWindowHeight,
            LogicalScreenMargin);
        if (_appWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsResizable = true;
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
        }

        _refreshTimer = DispatcherQueue.CreateTimer();
        _refreshTimer.IsRepeating = false;
        _refreshTimer.Interval = TimeSpan.FromMinutes(1);
        _refreshTimer.Tick += RefreshTimer_Tick;
        _appWindow.Changed += AppWindow_Changed;
        ApplySettings(settings);
        _placement.ApplyDefaultBounds(RootGrid);
        _session.ProjectionChanged += Session_ProjectionChanged;
        Closed += WorldClockWindow_Closed;
    }

    #endregion

    #region Implementation

    /// <summary>Applies the current application theme and language to the detached surface.</summary>
    internal void ApplySettings(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _autoGroupCities = settings.WorldClockAutoGroupCities;
        _strings = new LocalizationService(settings.UiLanguage);
        RootGrid.RequestedTheme = settings.Theme switch
        {
            "light" => ElementTheme.Light,
            "dark" => ElementTheme.Dark,
            _ => ElementTheme.Default
        };
        var appearance = WindowAppearanceSettings.Get(settings, WindowStateKeys.WorldClocks);
        WindowContent.Opacity = appearance.OpacityPercent / 100d;
        _placement.ApplyTaskbarVisibility(appearance.ShowInTaskbar);
        if (_appWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsAlwaysOnTop = appearance.AlwaysOnTop;
        }
        UiLocalization.Apply(RootGrid, _strings);
        UiLocalization.Apply(ReferenceInstantFlyoutContent, _strings);
        Title = T("WorldClock.Landmark");
        UpdateHeaderLayout();
        NowButton.Content = T("WorldClock.RestoreNow");
        ApplyReferenceButton.Content = T("WorldClock.Apply");
        UiLocalization.SetAccessibleLabel(OptionsButton, T("Window.Menu"));
        UiLocalization.SetAccessibleLabel(WorldMapButton, T("Window.OpenView"));
        ToolTipService.SetToolTip(WorldMapButton, T("Window.OpenView"));
        UiLocalization.SetAccessibleLabel(LunarPhaseButton, T("WorldClock.MoonPhase.Open"));
        ReferenceCityComboBox.Header = T("WorldClock.ReferenceCity");
        ReferenceDatePicker.Header = T("WorldClock.ReferenceDate");
        ReferenceTimePicker.Header = T("WorldClock.ReferenceTime");
        AutomationProperties.SetName(ReferenceCityComboBox, T("WorldClock.ReferenceCity"));
        AutomationProperties.SetName(ReferenceDatePicker, T("WorldClock.ReferenceDate"));
        AutomationProperties.SetName(ReferenceTimePicker, T("WorldClock.ReferenceTime"));
        AutomationProperties.SetName(ReferenceInstantButton, T("WorldClock.ReferenceInstant"));
        ToolTipService.SetToolTip(ReferenceInstantButton, T("WorldClock.ReferenceInstant"));
        AutomationProperties.SetLabeledBy(ReferenceInstantButton, ReferenceInstantLabel);
        AutomationProperties.SetName(NowButton, T("WorldClock.RestoreNow"));
        AutomationProperties.SetName(ApplyReferenceButton, T("WorldClock.Apply"));
        AutomationProperties.SetName(ClockColumnsHost, T("WorldClock.Landmark"));
        AutomationProperties.SetLocalizedLandmarkType(ClockColumnsHost, T("WorldClock.Landmark"));
        AutomationProperties.SetName(LoadingState, T("WorldClock.Loading"));
        AutomationProperties.SetName(LoadingStatusText, T("WorldClock.Loading"));
        var weatherAttribution = T("WorldClock.WeatherAttribution");
        WeatherAttributionText.Text = weatherAttribution;
        AutomationProperties.SetName(WeatherAttributionButton, weatherAttribution);
        ToolTipService.SetToolTip(WeatherAttributionButton, weatherAttribution);
        _titleBar.ApplyTheme(RootGrid.RequestedTheme == ElementTheme.Default ? RootGrid.ActualTheme : RootGrid.RequestedTheme);
        if (_session.Snapshot is { } snapshot)
        {
            ApplySnapshot(snapshot);
        }

        _titleBar.QueueLayoutUpdate();
    }

    /// <summary>Uses a common window menu with a local entry for managing clock cities.</summary>
    internal void SetWindowMenu(MenuFlyout menu)
    {
        var manageCities = new MenuFlyoutItem { Icon = new SymbolIcon(Symbol.World) };
        void ApplyLabel()
        {
            manageCities.Text = T("Window.ManageCities");
            AutomationProperties.SetName(manageCities, manageCities.Text);
        }
        ApplyLabel();
        menu.Opening += (_, _) => ApplyLabel();
        manageCities.Click += (_, _) => ConfigurationRequested?.Invoke(this);
        menu.Items.Insert(0, manageCities);
        menu.Items.Insert(1, new MenuFlyoutSeparator());
        OptionsButton.Flyout = menu;
    }

    /// <summary>Routes a clock-menu selection to the appropriate astronomy request.</summary>
    private void OpenAstronomyWindow(string windowKey)
    {
        if (windowKey == WindowStateKeys.WorldMap)
        {
            WorldMapRequested?.Invoke(this, EventArgs.Empty);
        }
        else if (windowKey == WindowStateKeys.LunarPhase)
        {
            LunarPhaseRequested?.Invoke(this, EventArgs.Empty);
        }
        else
        {
            CelestialWindowRequested?.Invoke(windowKey);
        }
    }

    /// <summary>Requests the independent lunar phase window.</summary>
    private void LunarPhaseButton_Click(object sender, RoutedEventArgs e) => LunarPhaseRequested?.Invoke(this, EventArgs.Empty);

    /// <summary>Opens the city picker from the empty clock workspace.</summary>
    private void EmptyStateAddButton_Click(object sender, RoutedEventArgs e) => AddCityRequested?.Invoke(this);

    /// <summary>Adapts the reference label to the available header width.</summary>
    private void UpdateHeaderLayout()
    {
        var narrowHeader = RootGrid.ActualWidth < 880d;
        ReferenceInstantLabel.Text = T("WorldClock.ReferenceInstant");
        ReferenceInstantLabel.Visibility = narrowHeader ? Visibility.Collapsed : Visibility.Visible;
        ReferenceInstantText.FontSize = narrowHeader ? 12d : 13d;
    }

    /// <summary>Adapts content layout and caption hit testing to the new viewport size.</summary>
    private void RootGrid_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        // XAML may raise size changes before construction has finished attaching the chrome controller.
        if (_titleBar is null)
        {
            return;
        }

        UpdateHeaderLayout();
        if (ReferenceInstantFlyout.IsOpen)
        {
            UpdateReferenceFlyoutConstraints();
        }

        _titleBar.QueueLayoutUpdate();
    }

    /// <summary>Constrains the reference editor before its flyout opens.</summary>
    private void ReferenceInstantFlyout_Opening(object sender, object e) => UpdateReferenceFlyoutConstraints();

    /// <summary>Keeps reference fields scrollable while preserving reachable flyout actions.</summary>
    private void UpdateReferenceFlyoutConstraints()
    {
        var bounds = WorldClockWindowLayoutState.CalculateReferenceFlyoutBounds(
            RootGrid.ActualWidth, RootGrid.ActualHeight);
        // Constrain the child as well as its presenter; only the fields scroll, leaving both actions reachable.
        ReferenceInstantFlyoutContent.Width = bounds.ContentWidth;
        ReferenceInstantFlyoutContent.MaxHeight = bounds.ContentMaxHeight;
    }

    /// <summary>Closes immediately while the composition root is already shutting down.</summary>
    internal void CloseForShutdown()
    {
        _lifetimeCancellation.Cancel();
        Close();
    }

    /// <summary>Restores window placement and initializes presentation after the visual tree is ready.</summary>
    private async void RootGrid_Loaded(object sender, RoutedEventArgs e)
    {
        if (_loaded)
        {
            return;
        }

        _loaded = true;
        if (RootGrid.XamlRoot is { } xamlRoot)
        {
            _xamlRoot = xamlRoot;
            _xamlRoot.Changed += XamlRoot_Changed;
        }

        _placement.ApplyDefaultBounds(RootGrid);
        try
        {
            var restored = await _placement.RestoreAsync(RootGrid, _lifetimeCancellation.Token);
            _layoutState.SetPlacementRestored(restored);
        }
        catch (OperationCanceledException) when (_lifetimeCancellation.IsCancellationRequested)
        {
            // The detached surface was closed while its optional state was loading.
            return;
        }
        catch (Exception exception)
        {
            ApplicationErrorService.Record(exception, "WorldClockWindow");
            if (IsClosing)
            {
                return;
            }

            ShowFailure("WorldClock.PlacementFailed");
        }

        if (_session.Snapshot is { } snapshot)
        {
            ApplySnapshot(snapshot);
        }

        if (_session.Snapshot is null || _session.CanRefreshLive)
        {
            await RefreshCurrentAsync();
        }
    }

    /// <summary>Requests the next live projection without redrawing the settings editor.</summary>
    private async void RefreshTimer_Tick(DispatcherQueueTimer sender, object args)
    {
        sender.Stop();
        await RefreshCurrentAsync(periodic: true);
    }

    /// <summary>Refreshes the live view and reports any application failure.</summary>
    private async Task RefreshCurrentAsync(bool periodic = false)
    {
        if (IsClosing || !_session.CanRefreshLive)
        {
            return;
        }

        ShowLoading(_session.Snapshot is null || _session.Snapshot.Clocks.Count == 0);
        var result = await _session.RefreshAsync(_lifetimeCancellation.Token, periodic);
        if (IsClosing)
        {
            return;
        }

        ShowLoading(false);
        if (result is { Succeeded: false } or { Value: null })
        {
            ShowFailure(result.MessageKey);
        }

        if (result?.Value is null && _session.CanRefreshLive)
        {
            ScheduleLiveRetry();
        }
    }

    /// <summary>Renders the shared session's latest complete projection.</summary>
    private void Session_ProjectionChanged(WorldClockSnapshot snapshot, bool updateSettings)
    {
        if (!IsClosing)
        {
            ApplySnapshot(snapshot);
        }
    }

    /// <summary>Applies a fixed reference from the session input.</summary>
    private async Task ConvertReferenceAsync()
    {
        var conversion = _session.ConvertAsync(_sessionLifetime);
        UpdateRefreshTimerState();
        var result = await conversion;
        if (!IsClosing && (result is { Succeeded: false } or { Value: null }))
        {
            UpdateRefreshTimerState();
            ShowFailure(result.MessageKey);
        }
    }

    /// <summary>Updates clock columns, reference controls, and dependent surfaces from one complete snapshot.</summary>
    private void ApplySnapshot(WorldClockSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (_session.ReferenceClock is not { } referenceClock)
        {
            UpdateWeatherAttribution(snapshot, []);
            _updatingReferenceControls = true;
            try
            {
                ReferenceCityComboBox.ItemsSource = null;
                ReferenceDatePicker.Date = null;
                ReferenceTimeZoneText.Text = "—";
                ReferenceInstantText.Text = "-- · --:--";
                ReferenceWeekText.Text = string.Empty;
                Title = T("WorldClock.Landmark");
            }
            finally
            {
                _updatingReferenceControls = false;
            }

            ReferenceInstantButton.IsEnabled = false;
            var referenceInstantLabel = T("WorldClock.ReferenceInstant");
            AutomationProperties.SetName(ReferenceInstantButton, referenceInstantLabel);
            ToolTipService.SetToolTip(ReferenceInstantButton, referenceInstantLabel);
            EnsureColumns(snapshot.Clocks);
            _layoutState.ResetWindowSizing();
            NowButton.Visibility = Visibility.Collapsed;
            UpdateWeatherStatus(snapshot.WeatherStatus);
            ShowLoading(false);
            UpdateRefreshTimerState();
            return;
        }

        ReferenceInstantButton.IsEnabled = true;
        UpdateReferenceControls(snapshot.Clocks, referenceClock);
        RenderClockColumns(snapshot, referenceClock);
        ApplySmartWindowSizing(_columns.Count);
        UpdateClockColumnsLayout(_columns.Count, ClockColumnsScroller.ActualWidth);

        NowButton.Visibility = Visibility.Visible;
        UpdateWeatherStatus(snapshot.WeatherStatus);
        UpdateRefreshTimerState();
    }

    /// <summary>Renders one column per projected clock group.</summary>
    private void RenderClockColumns(WorldClockSnapshot snapshot, WorldClockItem referenceClock)
    {
        var groups = WorldClockGrouping.Create(snapshot.Clocks, _autoGroupCities, referenceClock.CityId);
        var primaryClocks = groups.Select(group => group.Primary).ToArray();
        UpdateWeatherAttribution(snapshot, primaryClocks);
        EnsureColumns(primaryClocks);
        foreach (var group in groups)
        {
            var clock = group.Primary;
            _columns[clock.CityId].Apply(clock, referenceClock, clock.CityId == referenceClock.CityId,
                clock.SpaceWeatherAlert, _strings, group.Cities);
        }
    }

    /// <summary>Shows a changed weather warning without restarting unchanged notifications.</summary>
    private void UpdateWeatherStatus(WorldClockWeatherStatus status)
    {
        ArgumentNullException.ThrowIfNull(status);
        var messageKey = status.State switch
        {
            "available" => null,
            "not-requested" when status.ReasonCode == "explicit-instant" => null,
            "disabled" when status.ReasonCode == "user-disabled" => null,
            "not-requested" when status.ReasonCode == "no-clocks" => null,
            "configuration-required" when status.ReasonCode == "missing-api-key" => null,
            "configuration-required" when status.ReasonCode == "invalid-api-key" => null,
            "partial" => "WorldClock.WeatherStatus.Partial",
            "unavailable" => "WorldClock.WeatherStatus.Unavailable",
            _ => throw new InvalidDataException($"Unsupported world-clock weather status '{status.State}'.")
        };
        if (messageKey == _lastWeatherMessageKey)
        {
            return;
        }

        // A periodic snapshot must neither clear an action toast nor restart an unchanged weather warning.
        _lastWeatherMessageKey = messageKey;
        if (messageKey is null)
        {
            return;
        }

        _dialogs.Notifications.ShowWarning(
            WorldClockNotificationBanner,
            T("WorldClock.ErrorTitle"),
            T(messageKey));
    }

    /// <summary>Shows provider attribution only when its fresh observations are displayed.</summary>
    private void UpdateWeatherAttribution(WorldClockSnapshot snapshot, IReadOnlyList<WorldClockItem> visibleClocks)
    {
        var displaysOpenWeatherObservation =
            string.Equals(snapshot.WeatherStatus.Provider, "openweather", StringComparison.Ordinal)
            && visibleClocks.Any(static clock => clock.Weather is { IsFresh: true });
        if (displaysOpenWeatherObservation)
        {
            EnsureWeatherAttributionLogo();
        }

        WeatherAttributionButton.Visibility = displaysOpenWeatherObservation
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    /// <summary>Loads the weather-provider attribution image when required.</summary>
    private void EnsureWeatherAttributionLogo()
    {
        if (_weatherAttributionLogoSource is not null)
        {
            return;
        }

        var source = new SvgImageSource
        {
            RasterizePixelWidth = 168,
            RasterizePixelHeight = 64
        };
        source.Opened += WeatherAttributionLogo_Opened;
        source.OpenFailed += WeatherAttributionLogo_OpenFailed;
        _weatherAttributionLogoSource = source;
        WeatherAttributionLogo.Source = source;
        source.UriSource = new Uri("ms-appx:///Assets/WorldClocks/ThirdParty/OpenWeather/ow_logo.svg");
    }

    /// <summary>Updates attribution presentation after its logo has loaded.</summary>
    private void WeatherAttributionLogo_Opened(SvgImageSource sender, SvgImageSourceOpenedEventArgs args)
    {
        WeatherAttributionLogoContainer.Visibility = Visibility.Visible;
        WeatherAttributionText.Visibility = Visibility.Collapsed;
    }

    /// <summary>Reports attribution artwork failure and updates the remaining attribution content.</summary>
    private void WeatherAttributionLogo_OpenFailed(SvgImageSource sender, SvgImageSourceFailedEventArgs args)
    {
        // Keep the existing text attribution available when the optional SVG cannot be decoded.
        WeatherAttributionLogoContainer.Visibility = Visibility.Collapsed;
        WeatherAttributionText.Visibility = Visibility.Visible;
    }

    /// <summary>Opens the weather provider's attribution link.</summary>
    private async void WeatherAttributionButton_Click(object sender, RoutedEventArgs e)
    {
        WeatherAttributionButton.IsEnabled = false;
        try
        {
            await OpenWeatherProviderLinkAsync();
        }
        finally
        {
            if (!IsClosing)
            {
                WeatherAttributionButton.IsEnabled = true;
            }
        }
    }

    /// <summary>Opens provider information through the application facade and reports failures.</summary>
    private async Task OpenWeatherProviderLinkAsync()
    {
        if (_weatherProviderLinkOpening || IsClosing)
        {
            return;
        }

        _weatherProviderLinkOpening = true;
        try
        {
            var result = await _application.OpenProductLinkAsync(
                "openweather",
                _lifetimeCancellation.Token);
            if (!result.Succeeded && !IsClosing)
            {
                ShowFailure("About.LinkFailed");
            }
        }
        catch (OperationCanceledException) when (IsClosing)
        {
            // Closing the detached surface cancels an in-progress link launch.
        }
        catch (Exception exception)
        {
            ApplicationErrorService.Record(exception, "WorldClockWindow");
            if (!IsClosing)
            {
                ShowFailure("About.LinkFailed");
            }
        }
        finally
        {
            _weatherProviderLinkOpening = false;
        }
    }

    /// <summary>Updates reference fields without triggering another time conversion.</summary>
    private void UpdateReferenceControls(IReadOnlyList<WorldClockItem> clocks, WorldClockItem referenceClock)
    {
        _updatingReferenceControls = true;
        try
        {
            var options = clocks.Select(clock => new ReferenceCityOption(clock.CityId, clock.CityName)).ToArray();
            ReferenceCityComboBox.ItemsSource = options;
            ReferenceCityComboBox.SelectedItem = options.Single(option => option.CityId == referenceClock.CityId);
            var referenceInput = _session.ReferenceLocalTime ?? referenceClock.LocalTime.DateTime;
            ReferenceDatePicker.Date = new DateTimeOffset(referenceInput.Date, referenceClock.LocalTime.Offset);
            ReferenceTimePicker.Time = referenceInput.TimeOfDay;
            ReferenceTimeZoneText.Text = $"{T("WorldClock.LocalTime")} · UTC{referenceClock.LocalTime:zzz}";
            var referenceInstantText = referenceClock.LocalTime
                .ToString("dd MMM yyyy · HH:mm", _strings.Culture)
                .ToUpper(_strings.Culture);
            var referenceDate = referenceClock.LocalTime.Date;
            var referenceWeekText = string.Create(
                _strings.Culture,
                $"{T("WorldClock.Week")} {ISOWeek.GetWeekOfYear(referenceDate)} · {ISOWeek.GetYear(referenceDate)}");
            ReferenceInstantText.Text = referenceInstantText;
            ReferenceWeekText.Text = referenceWeekText;
            Title = $"{T("WorldClock.Landmark")} · {referenceWeekText}";
            var referenceInstantDescription = $"{referenceInstantText} · {referenceWeekText}";
            AutomationProperties.SetName(
                ReferenceInstantButton,
                $"{T("WorldClock.ReferenceInstant")}: {referenceInstantDescription}");
            ToolTipService.SetToolTip(ReferenceInstantButton, referenceInstantDescription);
        }
        finally
        {
            _updatingReferenceControls = false;
        }
    }

    /// <summary>Reuses existing city columns and reconciles them with the snapshot order.</summary>
    private void EnsureColumns(IReadOnlyList<WorldClockItem> clocks)
    {
        if (_columns.Count == clocks.Count
            && clocks.Select(clock => clock.CityId).SequenceEqual(_columns.Keys, StringComparer.Ordinal))
        {
            return;
        }

        _columns.Clear();
        ClockColumnsHost.Children.Clear();
        ClockColumnsHost.ColumnDefinitions.Clear();
        if (clocks.Count == 0)
        {
            ClockColumnsHost.MinWidth = 0d;
            ClockColumnsHost.Width = double.NaN;
            ClockColumnsHost.MaxWidth = double.PositiveInfinity;
            ClockColumnsHost.HorizontalAlignment = HorizontalAlignment.Center;
            return;
        }

        UpdateClockColumnsLayout(clocks.Count, ClockColumnsScroller.ActualWidth);

        for (var index = 0; index < clocks.Count; index++)
        {
            ClockColumnsHost.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var column = new WorldClockColumnControl();
            _columns.Add(clocks[index].CityId, column);

            var host = new Border
            {
                Background = new SolidColorBrush(Colors.Transparent),
                BorderBrush = ColumnDividerResource.BorderBrush,
                BorderThickness = index < clocks.Count - 1 ? new Thickness(0, 0, 1, 0) : new Thickness(0),
                Child = column
            };
            Grid.SetColumn(host, index);
            ClockColumnsHost.Children.Add(host);
        }

        UpdateClockColumnsLayout(clocks.Count, ClockColumnsScroller.ActualWidth);
    }

    /// <summary>Recalculates clock-column sizes after the viewport changes.</summary>
    private void ClockColumnsScroller_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (_columns.Count > 0)
        {
            UpdateClockColumnsLayout(_columns.Count, e.NewSize.Width);
        }
    }

    /// <summary>Distributes the available viewport among the current clock columns.</summary>
    private void UpdateClockColumnsLayout(int clockCount, double viewportWidth)
    {
        var layout = WorldClockWindowLayoutState.CalculateColumnsLayout(
            clockCount,
            Math.Max(0d, viewportWidth),
            _columns.Count == 0 ? 320d : _columns.Values.Max(column => column.MeasureMinimumWidth()));
        ClockColumnsHost.MinWidth = layout.MinimumWidth;
        ClockColumnsHost.Width = layout.Width;
        ClockColumnsHost.MaxWidth = layout.Width;
        ClockColumnsHost.HorizontalAlignment = HorizontalAlignment.Left;
        var columnWidth = layout.Width / clockCount;
        var viewportHeight = Math.Max(0d, ClockColumnsScroller.ActualHeight);
        var bottomOverlayHeight = WeatherAttributionButton.Visibility == Visibility.Visible
            ? WeatherAttributionButton.Height + WeatherAttributionButton.Margin.Bottom
            : 0d;
        foreach (var column in _columns.Values)
        {
            column.SetViewportSize(Math.Max(0d, columnWidth - 1d), viewportHeight, bottomOverlayHeight);
        }
    }

    /// <summary>Adapts initial clock-window dimensions while respecting restored user placement.</summary>
    private void ApplySmartWindowSizing(int clockCount)
    {
        if (!_loaded)
        {
            return;
        }

        var request = _layoutState.GetWindowResizeRequest(clockCount);
        if (request is null)
        {
            return;
        }

        if (request.ResizeToPreferred)
        {
            // Measure against the target column width before sizing so the detailed command actually reveals its content.
            UpdateClockColumnsLayout(clockCount, request.Sizing.PreferredLogicalWidth);
            var measuredHeight = _columns.Values.Max(column => column.PreferredContentHeight)
                + _titleBar.ReservedHeight
                + 16d;
            _placement.ResizeForContent(
                RootGrid,
                request.Sizing.PreferredLogicalWidth,
                Math.Max(request.Sizing.PreferredLogicalHeight, (int)Math.Ceiling(measuredHeight)));
            UpdateClockColumnsLayout(clockCount, ClockColumnsScroller.ActualWidth);
        }

        _layoutState.AcceptWindowResizeRequest(request);
        _titleBar.QueueLayoutUpdate();
    }

    /// <summary>Changes the conversion reference after an explicit city selection.</summary>
    private void ReferenceCityComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updatingReferenceControls || ReferenceCityComboBox.SelectedItem is not ReferenceCityOption option)
        {
            return;
        }

        _session.SelectReference(option.CityId);
    }

    /// <summary>Recalculates the selected reference instant after a date edit.</summary>
    private async void ReferenceDatePicker_DateChanged(CalendarDatePicker sender, CalendarDatePickerDateChangedEventArgs args)
    {
        if (_updatingReferenceControls)
        {
            return;
        }

        if (args.NewDate is not { } date)
        {
            if (_session.Snapshot is { } snapshot && _session.ReferenceClock is { } referenceClock)
            {
                UpdateReferenceControls(snapshot.Clocks, referenceClock);
            }

            ShowFailure("WorldClock.ReferenceDateRequired");
            return;
        }

        _session.SetReferenceDate(date.Date);
        await ConvertReferenceAsync();
    }

    /// <summary>Recalculates the selected reference instant after a time edit.</summary>
    private async void ReferenceTimePicker_TimeChanged(object sender, TimePickerValueChangedEventArgs e)
    {
        if (!_updatingReferenceControls)
        {
            _session.SetReferenceTime(e.NewTime);
            await ConvertReferenceAsync();
        }
    }

    /// <summary>Applies the requested fixed instant from the reference editor.</summary>
    private async void ApplyReferenceButton_Click(object sender, RoutedEventArgs e) => await ConvertReferenceAsync();

    /// <summary>Returns the clock workspace to the current live instant.</summary>
    private async void NowButton_Click(object sender, RoutedEventArgs e)
    {
        _session.UseLive();
        UpdateRefreshTimerState();
        await RefreshCurrentAsync();
    }

    /// <summary>Presents a localized failure in the window's notification surface.</summary>
    private void ShowFailure(string messageKey)
    {
        if (!IsClosing)
        {
            _dialogs.Notifications.ShowWarning(WorldClockNotificationBanner, T("WorldClock.ErrorTitle"), T(messageKey));
        }
    }

    /// <summary>Updates the initial-load indicator without discarding an existing projection.</summary>
    private void ShowLoading(bool show)
    {
        LoadingIndicator.IsActive = show;
        LoadingIndicator.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        LoadingState.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        var hasClocks = _session.Snapshot is { Clocks.Count: > 0 };
        var hasEmptySnapshot = _session.Snapshot is { Clocks.Count: 0 };
        ClockColumnsScroller.Visibility = !show && hasClocks ? Visibility.Visible : Visibility.Collapsed;
        EmptyClocksState.Visibility = !show && hasEmptySnapshot ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>Schedules live updates only while the clock surface can consume them.</summary>
    private void UpdateRefreshTimerState()
    {
        _refreshTimer.Stop();
        var minimized = _appWindow.Presenter is OverlappedPresenter presenter
            && presenter.State == OverlappedPresenterState.Minimized;
        if (_session.CanRefreshLive
            && !minimized
            && !_lifetimeCancellation.IsCancellationRequested
            && _session.Snapshot is { Clocks.Count: > 0 } snapshot)
        {
            _refreshTimer.Interval = WorldClockWindowLayoutState.DelayUntilNextMinute(snapshot.InstantUtc, DateTimeOffset.UtcNow);
            _refreshTimer.Start();
        }
    }

    /// <summary>Schedules a bounded retry while retaining the last complete live projection.</summary>
    private void ScheduleLiveRetry()
    {
        _refreshTimer.Stop();
        var minimized = _appWindow.Presenter is OverlappedPresenter presenter
            && presenter.State == OverlappedPresenterState.Minimized;
        if (_session.CanRefreshLive
            && !minimized
            && !_lifetimeCancellation.IsCancellationRequested)
        {
            _refreshTimer.Interval = TimeSpan.FromMinutes(1);
            _refreshTimer.Start();
        }
    }

    /// <summary>Reconciles presentation with native window state and geometry changes.</summary>
    private async void AppWindow_Changed(AppWindow sender, AppWindowChangedEventArgs args)
    {
        var minimized = sender.Presenter is OverlappedPresenter presenter
            && presenter.State == OverlappedPresenterState.Minimized;
        if (minimized == _wasMinimized)
        {
            // Moving or resizing must not restart the minute countdown or its failure retry delay.
            return;
        }

        var restored = _wasMinimized && !minimized;
        _wasMinimized = minimized;
        UpdateRefreshTimerState();
        if (restored
            && _session.CanRefreshLive
            && !IsClosing)
        {
            await RefreshCurrentAsync();
        }
    }

    /// <summary>Reapplies theme-dependent clock visuals after caption theme changes.</summary>
    private void TitleBar_ThemeChanged(ElementTheme effectiveTheme)
    {
        if (_session.Snapshot is not null)
        {
            DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, ReapplyThemeAwareClockPresentation);
        }
    }

    /// <summary>Renders the existing projection using the effective window theme.</summary>
    private void ReapplyThemeAwareClockPresentation()
    {
        if (IsClosing || _session.Snapshot is not { } snapshot || _session.ReferenceClock is not { } referenceClock)
        {
            return;
        }

        RenderClockColumns(snapshot, referenceClock);
    }

    /// <summary>Queues geometry updates after the XAML root's scale or layout changes.</summary>
    private void XamlRoot_Changed(XamlRoot sender, XamlRootChangedEventArgs args)
    {
        if (ReferenceInstantFlyout.IsOpen)
        {
            UpdateReferenceFlyoutConstraints();
        }

        if (_columns.Count > 0)
        {
            UpdateClockColumnsLayout(_columns.Count, ClockColumnsScroller.ActualWidth);
        }

        _titleBar.QueueLayoutUpdate();
    }

    /// <summary>Releases the closed clock instance and its event subscriptions.</summary>
    private void WorldClockWindow_Closed(object sender, WindowEventArgs args)
    {
        _closed = true;
        _appWindow.Changed -= AppWindow_Changed;
        _refreshTimer.Stop();
        _refreshTimer.Tick -= RefreshTimer_Tick;
        if (_weatherAttributionLogoSource is not null)
        {
            _weatherAttributionLogoSource.Opened -= WeatherAttributionLogo_Opened;
            _weatherAttributionLogoSource.OpenFailed -= WeatherAttributionLogo_OpenFailed;
            _weatherAttributionLogoSource = null;
        }

        _session.ProjectionChanged -= Session_ProjectionChanged;

        _columns.Clear();
        if (_xamlRoot is not null)
        {
            _xamlRoot.Changed -= XamlRoot_Changed;
        }

        _titleBar.ThemeChanged -= TitleBar_ThemeChanged;
        _titleBar.Dispose();
        _lifetimeCancellation.Cancel();
        _placement.Dispose();
        _lifetimeCancellation.Dispose();
    }

    /// <summary>Translates a resource key using the current UI language.</summary>
    private string T(string key) => _strings.Translate(key);

    /// <summary>Carries the identity and display label of a time-conversion reference city.</summary>
    private sealed record ReferenceCityOption(string CityId, string CityName);

    #endregion
}
