// SPDX-License-Identifier: MIT

using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using WorkTrail.Application;
using WorkTrail.Controls;
using WorkTrail.Presentation;
using WorkTrail.Services;

namespace WorkTrail;

/// <summary>Shares placement and live DTO refreshes between passive astronomical windows.</summary>
internal sealed class AstronomyWindowController
{
    private readonly Window _window;
    private readonly FrameworkElement _root;
    private readonly ProgressRing _loadingIndicator;
    private readonly TimedInfoBar _notificationBanner;
    private readonly Action<WorldClockSnapshot> _renderSnapshot;
    private readonly IWorkTrailApplication _application;
    private readonly MicaDialogService _dialogs;
    private readonly AppWindow _appWindow;
    private readonly string _windowKey;
    private readonly bool _celestialReferenceOnly;
    private readonly CustomTitleBarController _titleBar;
    private readonly WindowPlacementService _placement;
    private readonly CancellationTokenSource _lifetimeCancellation = new();
    private readonly DispatcherQueueTimer _refreshTimer;
    private LocalizationService _strings = new("system");
    private WorldClockSnapshot? _snapshot;
    private bool _isLive = true;
    private bool _loaded;
    private bool _closed;
    private bool _refreshInProgress;
    private bool _wasMinimized;
    private int _projectionVersion;

    /// <summary>Connects a passive astronomical surface to the shared application facade and window infrastructure.</summary>
    internal AstronomyWindowController(
        Window window,
        FrameworkElement root,
        FrameworkElement titleBarRegion,
        ColumnDefinition leftInset,
        ColumnDefinition rightInset,
        ProgressRing loadingIndicator,
        TimedInfoBar notificationBanner,
        IWorkTrailApplication application,
        MicaDialogService dialogs,
        string windowKey,
        int defaultWidth,
        int defaultHeight,
        Action<WorldClockSnapshot> renderSnapshot,
        bool celestialReferenceOnly = false,
        Func<IEnumerable<FrameworkElement>>? interactiveElements = null)
    {
        _window = window ?? throw new ArgumentNullException(nameof(window));
        _root = root ?? throw new ArgumentNullException(nameof(root));
        _loadingIndicator = loadingIndicator ?? throw new ArgumentNullException(nameof(loadingIndicator));
        _notificationBanner = notificationBanner ?? throw new ArgumentNullException(nameof(notificationBanner));
        _renderSnapshot = renderSnapshot ?? throw new ArgumentNullException(nameof(renderSnapshot));
        _application = application ?? throw new ArgumentNullException(nameof(application));
        _dialogs = dialogs ?? throw new ArgumentNullException(nameof(dialogs));
        _windowKey = windowKey;
        _celestialReferenceOnly = celestialReferenceOnly;
        _appWindow = AppWindow.GetFromWindowId(
            Win32Interop.GetWindowIdFromWindow(WinRT.Interop.WindowNative.GetWindowHandle(window)));
        _titleBar = new CustomTitleBarController(
            window,
            _appWindow,
            root,
            titleBarRegion,
            leftInset,
            rightInset,
            interactiveElements ?? (() => []),
            overlayContent: true);
        _placement = new WindowPlacementService(
            application, window, _appWindow, windowKey, defaultWidth, defaultHeight, 0);
        _refreshTimer = window.DispatcherQueue.CreateTimer();
        _refreshTimer.IsRepeating = false;
        _refreshTimer.Tick += RefreshTimer_Tick;
        _appWindow.Changed += AppWindow_Changed;
        _window.Closed += Window_Closed;
        _root.Loaded += Root_Loaded;
        _placement.ApplyDefaultBounds(_root);
    }

    /// <summary>Applies the current theme and language without changing the selected reference instant.</summary>
    internal void ApplySettings(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _appWindow.IsShownInSwitchers = _windowKey switch
        {
            WindowStateKeys.WorldMap => settings.WorldMapWindowShowInTaskbar,
            WindowStateKeys.LunarPhase => settings.LunarPhaseWindowShowInTaskbar,
            WindowStateKeys.LocalSky or WindowStateKeys.AstronomyAgenda or WindowStateKeys.CelestialMap => settings.WorldClockWindowShowInTaskbar,
            _ => throw new InvalidOperationException("Unsupported astronomical window.")
        };
        _strings = new LocalizationService(settings.UiLanguage);
        _root.RequestedTheme = settings.Theme switch
        {
            "light" => ElementTheme.Light,
            "dark" => ElementTheme.Dark,
            _ => ElementTheme.Default
        };
        AutomationProperties.SetName(_loadingIndicator, T("WorldClock.Loading"));
        if (_snapshot is { } snapshot)
        {
            _renderSnapshot(snapshot);
        }

        _titleBar.ApplyTheme(_root.RequestedTheme == ElementTheme.Default ? _root.ActualTheme : _root.RequestedTheme);
        _titleBar.QueueLayoutUpdate();
    }

    /// <summary>Gets whether the current reference follows live time rather than an explicitly selected instant.</summary>
    internal bool IsLive => _isLive;

    /// <summary>Mirrors the clock reference instant, including its live or explicitly selected time mode.</summary>
    internal void ApplySnapshot(WorldClockSnapshot snapshot, bool isLive)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (_closed)
        {
            return;
        }

        _projectionVersion++;
        _snapshot = snapshot;
        _isLive = isLive;
        if (!_celestialReferenceOnly)
        {
            _loadingIndicator.IsActive = false;
            _loadingIndicator.Visibility = Visibility.Collapsed;
        }
        _renderSnapshot(snapshot);
        ScheduleRefresh();
    }

    /// <summary>Closes the map when the composition root has already saved the application session.</summary>
    internal void CloseForShutdown()
    {
        _lifetimeCancellation.Cancel();
        _window.Close();
    }

    /// <summary>Discards a failed opening without saving incomplete geometry or replacing the previous workspace state.</summary>
    internal void CloseAfterFailedOpening()
    {
        _lifetimeCancellation.Cancel();
        _placement.Dispose();
        _window.Close();
    }

    private async void Root_Loaded(object sender, RoutedEventArgs e)
    {
        if (_loaded)
        {
            return;
        }

        _loaded = true;
        try
        {
            await _placement.RestoreAsync(_root, _lifetimeCancellation.Token);
        }
        catch (OperationCanceledException) when (_lifetimeCancellation.IsCancellationRequested)
        {
            // Closing the window cancels its pending placement restoration.
            return;
        }
        catch (Exception exception)
        {
            ShowFailure("WorldClock.PlacementFailed", exception);
        }

        if (_snapshot is null)
        {
            await RefreshCurrentAsync();
        }
        else
        {
            ScheduleRefresh();
        }
    }

    private async void RefreshTimer_Tick(DispatcherQueueTimer sender, object args)
    {
        sender.Stop();
        await RefreshCurrentAsync();
    }

    private async Task RefreshCurrentAsync()
    {
        if (!_isLive || _closed || _refreshInProgress || _lifetimeCancellation.IsCancellationRequested)
        {
            return;
        }

        _refreshInProgress = true;
        var version = _projectionVersion;
        try
        {
            var result = _celestialReferenceOnly
                ? await _application.GetCelestialReferenceAsync(_lifetimeCancellation.Token)
                : await _application.GetWorldClocksAsync(_lifetimeCancellation.Token);
            if (_closed || _lifetimeCancellation.IsCancellationRequested || version != _projectionVersion)
            {
                // A newer clock reference or a closed window invalidates this pending live projection.
                return;
            }

            if (!result.Succeeded || result.Value is null)
            {
                ShowFailure(result.MessageKey);
                return;
            }

            ApplySnapshot(result.Value, isLive: true);
        }
        catch (OperationCanceledException) when (_lifetimeCancellation.IsCancellationRequested)
        {
            // Shutdown cancels presentation refreshes without displaying an error or retrying.
        }
        catch (Exception exception)
        {
            ShowFailure("WorldClock.CatalogUnavailable", exception);
        }
        finally
        {
            _refreshInProgress = false;
            if (!_closed && !_lifetimeCancellation.IsCancellationRequested)
            {
                // Once a celestial reference exists, its view owns the spinner for independent city/size projections.
                if (!_celestialReferenceOnly || _snapshot is null)
                {
                    _loadingIndicator.IsActive = false;
                    _loadingIndicator.Visibility = Visibility.Collapsed;
                }
                // A failed request visibly reports the error, retains the last complete map, and retries in one minute.
                ScheduleRefresh(useRetryDelay: version == _projectionVersion);
            }
        }
    }

    private void ScheduleRefresh(bool useRetryDelay = false)
    {
        _refreshTimer.Stop();
        var minimized = _appWindow.Presenter is OverlappedPresenter presenter
            && presenter.State == OverlappedPresenterState.Minimized;
        if (!_loaded || !_isLive || _closed || minimized || _lifetimeCancellation.IsCancellationRequested)
        {
            return;
        }

        _refreshTimer.Interval = !useRetryDelay && _snapshot is { } snapshot
            ? WorldClockWindowLayoutState.DelayUntilNextMinute(snapshot.InstantUtc, DateTimeOffset.UtcNow)
            : TimeSpan.FromMinutes(1);
        _refreshTimer.Start();
    }

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
        ScheduleRefresh();
        if (restored)
        {
            await RefreshCurrentAsync();
        }
    }

    private void ShowFailure(string key, Exception? exception = null)
    {
        if (_closed || _lifetimeCancellation.IsCancellationRequested)
        {
            return;
        }

        var message = exception is null ? T(key) : $"{T(key)} ({exception.GetType().Name})";
        _dialogs.Notifications.ShowError(_notificationBanner, T("WorldClock.ErrorTitle"), message);
    }

    private void Window_Closed(object sender, WindowEventArgs args)
    {
        _closed = true;
        _refreshTimer.Stop();
        _refreshTimer.Tick -= RefreshTimer_Tick;
        _appWindow.Changed -= AppWindow_Changed;
        _window.Closed -= Window_Closed;
        _root.Loaded -= Root_Loaded;
        _lifetimeCancellation.Cancel();
        _titleBar.Dispose();
        _placement.Dispose();
        _lifetimeCancellation.Dispose();
    }

    private string T(string key) => _strings.Translate(key);
}
