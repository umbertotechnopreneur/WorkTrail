// SPDX-License-Identifier: MIT
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using WorkTrail.Application;
using WorkTrail.Runtime;
using WorkTrail.Services;

namespace WorkTrail;

/// <summary>Launches independent desktop widgets and edits their essential preferences.</summary>
public sealed partial class MainWindow : Window
{
    #region Fields

    private readonly IWorkTrailApplication _application;
    private readonly TrayIconService _trayIcon;
    private readonly MicaDialogService _dialogs;
    private readonly string _iconPath;
    private readonly AppWindow _appWindow;
    private readonly CustomTitleBarController _titleBar;
    private readonly WindowPlacementService _placement;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly DispatcherTimer _homeCityTimer = new() { Interval = TimeSpan.FromMinutes(15) };
    private LocalizationService _strings;
    private AppSettings _settings;
    private bool _applying;
    private bool _allowClose;
    private bool _loaded;
    private bool _saving;
    private bool _closePromptOpen;
    private AboutWindow? _about;
    private WorldClockHomeCity? _homeCity;
    private string? _homeCityAssetPath;
    private int _homeCityRefreshVersion;
    private int _launcherColumns;
    private int _footerColumns;

    #endregion

    #region Events

    /// <summary>Requests the saved world clocks.</summary>
    public event EventHandler? WorldClocksRequested;

    /// <summary>Requests one of the independent astronomy widgets.</summary>
    public event Action<string>? WorldTimeWindowRequested;

    /// <summary>Publishes committed widget preferences to the open windows.</summary>
    public event Action<AppSettings>? SettingsApplied;

    /// <summary>Requests orderly shutdown of every app window.</summary>
    public event EventHandler? ExitRequested;

    #endregion

    #region Initialization

    /// <summary>Connects launcher controls, window placement, and tray commands to the application facade.</summary>
    internal MainWindow(IWorkTrailApplication application, LaunchOptions options, AppSettings settings,
        MicaDialogService dialogs, TrayIconService trayIcon, string iconPath)
    {
        _application = application;
        _dialogs = dialogs;
        _trayIcon = trayIcon;
        _iconPath = iconPath;
        _settings = settings;
        _strings = new LocalizationService(options.Language ?? settings.UiLanguage);
        InitializeComponent();
        Title = "WorkTrail";
        SystemBackdrop = new GlassBackdrop();
        var handle = WinRT.Interop.WindowNative.GetWindowHandle(this);
        _appWindow = AppWindow.GetFromWindowId(Win32Interop.GetWindowIdFromWindow(handle));
        _appWindow.SetIcon(iconPath);
        _titleBar = new CustomTitleBarController(this, _appWindow, RootGrid, TitleBarDragRegion,
            TitleBarLeftInsetColumn, TitleBarRightInsetColumn, () => []);
        _placement = new WindowPlacementService(application, this, _appWindow, WindowStateKeys.Main, 520, 620, 24, deferNativeClose: false);
        _appWindow.Closing += AppWindow_Closing;
        RootGrid.Loaded += Root_Loaded;
        _homeCityTimer.Tick += HomeCityTimer_Tick;
        Closed += Window_Closed;
        _trayIcon.ExitRequested += Tray_ExitRequested;
        _trayIcon.CommandRequested += Tray_CommandRequested;
        _trayIcon.MenuStateProvider = CreateTrayMenuState;
        ApplySettings(settings with { UiLanguage = options.Language ?? settings.UiLanguage, Theme = options.Theme ?? settings.Theme });
    }

    #endregion

    #region Implementation

    /// <summary>Translates a resource key using the current UI language.</summary>
    private string T(string key) => _strings.Translate(key);

    /// <summary>Restores full artwork saturation while the pointer is over a launcher tile.</summary>
    private void LauncherTile_PointerEntered(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs args) =>
        SetArtworkHighlighted(sender, true);

    /// <summary>Returns the tile artwork to its resting saturation.</summary>
    private void LauncherTile_PointerExited(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs args) =>
        SetArtworkHighlighted(sender, false);

    /// <summary>Updates the composition brush belonging to the hovered tile.</summary>
    private static void SetArtworkHighlighted(object sender, bool highlighted)
    {
        if (sender is FrameworkElement { Parent: Grid tile })
        {
            foreach (var artwork in tile.Children.OfType<Microsoft.UI.Xaml.Shapes.Rectangle>())
            {
                if (artwork.Fill is Controls.LauncherArtworkBrush brush)
                {
                    brush.SetHighlighted(highlighted);
                }
            }
        }
    }

    /// <summary>Initializes placement and presentation after the root enters the visual tree.</summary>
    private async void Root_Loaded(object sender, RoutedEventArgs args)
    {
        if (_loaded)
        {
            return;
        }
        _loaded = true;
        try
        {
            if (!await _placement.RestoreAsync(RootGrid, _lifetime.Token))
            {
                _placement.ApplyDefaultBounds(RootGrid);
            }
            _placement.CenterOnCursorDisplay(RootGrid);
            var startup = await _application.GetStartupStatusAsync(_lifetime.Token);
            if (!startup.Succeeded)
            {
                ShowFailure(startup.MessageKey);
            }
            else
            {
                _applying = true;
                StartupToggle.IsOn = startup.Value;
                _applying = false;
            }
            await RefreshHomeCityAsync();
            if (_lifetime.IsCancellationRequested)
            {
                return;
            }
            _homeCityTimer.Start();
            _ready.TrySetResult();
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
            _ready.TrySetCanceled();
        }
        catch (Exception exception)
        {
            _ready.TrySetException(exception);
            ApplicationErrorService.Report(exception, "LauncherStartup", _settings.UiLanguage);
        }
    }

    /// <summary>Returns the startup task used before restoring dependent workspace windows.</summary>
    internal Task WaitForWorkspaceReadyAsync() => _ready.Task;

    /// <summary>Selects tile and footer column counts for the available launcher width.</summary>
    private void LauncherContent_SizeChanged(object sender, SizeChangedEventArgs args)
    {
        var columns = args.NewSize.Width >= 580d ? 2 : 1;
        if (_launcherColumns != columns)
        {
            _launcherColumns = columns;
            ArrangeCommands(LauncherTiles, [ClocksTile, MapTile, PlannerTile, CalendarTile, SkyTile, GlobeTile, MoonTile], columns);
            ArrangeCommands(SettingsGrid, [StartupCard, SnappingCard, TitleBarCard, ThemeCombo, LanguageCombo], columns);
        }
        var footerColumns = args.NewSize.Width >= 420d ? 3 : 1;
        if (_footerColumns != footerColumns)
        {
            _footerColumns = footerColumns;
            ArrangeCommands(LauncherFooter, [AboutButton, HideButton, ExitButton], footerColumns);
        }
        UpdateLauncherTileSizing();
    }

    /// <summary>Recalculates tile height after the scroll viewport changes.</summary>
    private void LauncherViewport_SizeChanged(object sender, SizeChangedEventArgs args) => UpdateLauncherTileSizing();

    /// <summary>Shares spare viewport height across tile rows while preserving a scrollable minimum.</summary>
    private void UpdateLauncherTileSizing()
    {
        if (LauncherScroller is null || LauncherBody is null || LauncherTiles is null
            || _launcherColumns == 0 || LauncherBody.ActualHeight <= 0)
        {
            return;
        }
        var rowCount = (7 + _launcherColumns - 1) / _launcherColumns;

        // Subtract measured settings/footer space so extra viewport height belongs to the tiles.
        // At the minimum height the content scrolls, including when settings are expanded.
        var surroundingHeight = Math.Max(0, LauncherBody.ActualHeight - LauncherTiles.ActualHeight);
        var spacingBetweenRows = LauncherTiles.RowSpacing * (rowCount - 1);
        var heightAvailableForTiles = LauncherScroller.ActualHeight - surroundingHeight - spacingBetweenRows;
        var preferredTileHeight = heightAvailableForTiles / rowCount;
        var tileHeight = Math.Clamp(preferredTileHeight, 112d, 192d);
        tileHeight = Math.Floor(tileHeight);

        var artworkGrowth = (tileHeight - 96d) / 2d;
        var artworkSize = Math.Min(192d, 144d + artworkGrowth);

        foreach (var tile in LauncherTiles.Children.OfType<Grid>())
        {
            if (Math.Abs(tile.Height - tileHeight) >= 1d)
            {
                tile.Height = tileHeight;
            }
            foreach (var artwork in tile.Children.OfType<Microsoft.UI.Xaml.Shapes.Rectangle>())
            {
                artwork.Width = artworkSize;
                artwork.Height = artworkSize;
            }
            foreach (var button in tile.Children.OfType<Button>())
            {
                button.Padding = new Thickness(artworkSize - 12d, 16d, 18d, 16d);
            }
        }
    }

    /// <summary>Rebuilds equal-width columns and places commands in row-major order.</summary>
    private static void ArrangeCommands(Grid grid, FrameworkElement[] commands, int columns)
    {
        grid.ColumnDefinitions.Clear();
        grid.RowDefinitions.Clear();
        for (var column = 0; column < columns; column++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        }
        var rowCount = (commands.Length + columns - 1) / columns;
        for (var row = 0; row < rowCount; row++)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        }
        for (var index = 0; index < commands.Length; index++)
        {
            var command = commands[index];
            var row = index / columns;
            var column = index % columns;

            Grid.SetRow(command, row);
            Grid.SetColumn(command, column);
        }
    }

    /// <summary>Refreshes launcher controls and publishes committed preferences to open widgets.</summary>
    internal async Task ApplyExternalSettingsAsync(AppSettings settings)
    {
        ApplySettings(settings);
        SettingsApplied?.Invoke(settings);
        if (_loaded)
        {
            await RefreshHomeCityAsync();
        }
    }

    /// <summary>Updates localized controls and window presentation from the supplied preferences.</summary>
    private void ApplySettings(AppSettings settings)
    {
        _applying = true;
        try
        {
            _settings = settings;
            _strings = new LocalizationService(settings.UiLanguage);
            RootGrid.RequestedTheme = settings.Theme switch { "light" => ElementTheme.Light, "dark" => ElementTheme.Dark, _ => ElementTheme.Default };
            RootGrid.Opacity = settings.MainWindowOpacityPercent / 100d;
            UiLocalization.Apply(RootGrid, _strings);
            ApplyHomeCity(_homeCity);
            StartupToggle.Header = T("Launcher.Startup");
            SnappingToggle.Header = T("Launcher.Snapping");
            TitleBarToggle.Header = T("Launcher.AutoHide");
            ThemeCombo.Header = T("Launcher.Theme");
            LanguageCombo.Header = T("Launcher.Language");
            StartupToggle.IsOn = settings.StartWithWindows;
            SnappingToggle.IsOn = settings.WindowSnappingEnabled;
            TitleBarToggle.IsOn = settings.AutoHideTitleBar;
            ThemeCombo.ItemsSource = new[] { T("Launcher.System"), T("Launcher.Light"), T("Launcher.Dark") };
            ThemeCombo.SelectedIndex = settings.Theme switch { "light" => 1, "dark" => 2, _ => 0 };
            LanguageCombo.Apply(settings.UiLanguage, T("Launcher.System"));
            _placement.ApplyTaskbarVisibility(settings.MainWindowShowInTaskbar);
            _trayIcon.MenuStateProvider = CreateTrayMenuState;
        }
        finally
        {
            _applying = false;
        }
    }

    /// <summary>Refreshes the reference-city presentation on the launcher timer.</summary>
    private async void HomeCityTimer_Tick(object? sender, object args) => await RefreshHomeCityAsync();

    /// <summary>Loads the reference city and ignores results superseded by a newer refresh or shutdown.</summary>
    private async Task RefreshHomeCityAsync()
    {
        var version = ++_homeCityRefreshVersion;
        try
        {
            var result = await _application.GetCelestialReferenceAsync(_lifetime.Token);
            if (_lifetime.IsCancellationRequested || version != _homeCityRefreshVersion)
            {
                return;
            }
            if (!result.Succeeded || result.Value is null)
            {
                ApplyHomeCity(null);
                ShowFailure(result.MessageKey);
                return;
            }
            ApplyHomeCity(result.Value.HomeCity);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            if (_lifetime.IsCancellationRequested || version != _homeCityRefreshVersion)
            {
                return;
            }
            ApplyHomeCity(null);
            ApplicationErrorService.Report(exception, "LauncherHomeCity", _settings.UiLanguage);
        }
    }

    /// <summary>Updates the city heading and changes the packaged skyline only when its asset changes.</summary>
    private void ApplyHomeCity(WorldClockHomeCity? city)
    {
        _homeCity = city;
        HomeCityHeader.Visibility = city is null ? Visibility.Collapsed : Visibility.Visible;
        if (city is null)
        {
            HomeCityImage.Source = null;
            _homeCityAssetPath = null;
            HomeCityNameText.Text = string.Empty;
            HomeCityPositionText.Text = string.Empty;
            return;
        }

        var latitudeHemisphere = city.Latitude < 0 ? "S" : "N";
        var longitudeHemisphere = city.Longitude < 0 ? "W" : "E";

        HomeCityNameText.Text = city.CityName;
        HomeCityPositionText.Text = string.Format(_strings.Culture, "{4} · {0:0.####}° {1} · {2:0.####}° {3}",
            Math.Abs(city.Latitude), latitudeHemisphere,
            Math.Abs(city.Longitude), longitudeHemisphere, city.CountryCode);
        if (string.Equals(_homeCityAssetPath, city.SkylineAssetPath, StringComparison.Ordinal))
        {
            return;
        }
        HomeCityImage.Source = new BitmapImage(new Uri($"ms-appx:///{city.SkylineAssetPath}")) { DecodePixelWidth = 1280 };
        _homeCityAssetPath = city.SkylineAssetPath;
    }

    /// <summary>Clears failed skyline artwork and reports the packaged-image failure.</summary>
    private void HomeCityImage_Failed(object sender, ExceptionRoutedEventArgs args)
    {
        if (_lifetime.IsCancellationRequested)
        {
            return;
        }
        HomeCityImage.Source = null;
        _homeCityAssetPath = null;
        ApplicationErrorService.Report(new InvalidOperationException(args.ErrorMessage), "LauncherHomeCityImage", _settings.UiLanguage);
    }

    /// <summary>Requests the saved world-clock window.</summary>
    private void Clocks_Click(object sender, RoutedEventArgs args) => WorldClocksRequested?.Invoke(this, EventArgs.Empty);

    /// <summary>Opens the availability planner directly from the launcher.</summary>
    private void Planner_Click(object sender, RoutedEventArgs args) => WorldTimeWindowRequested?.Invoke(WindowStateKeys.AvailabilityPlanner);

    /// <summary>Requests the astronomical agenda window.</summary>
    private void Calendar_Click(object sender, RoutedEventArgs args) => WorldTimeWindowRequested?.Invoke(WindowStateKeys.AstronomyAgenda);

    /// <summary>Requests the local sky window.</summary>
    private void Sky_Click(object sender, RoutedEventArgs args) => WorldTimeWindowRequested?.Invoke(WindowStateKeys.LocalSky);

    /// <summary>Requests the lunar phase window.</summary>
    private void Moon_Click(object sender, RoutedEventArgs args) => WorldTimeWindowRequested?.Invoke(WindowStateKeys.LunarPhase);

    /// <summary>Requests the day-and-night map window.</summary>
    private void Map_Click(object sender, RoutedEventArgs args) => WorldTimeWindowRequested?.Invoke(WindowStateKeys.WorldMap);

    /// <summary>Requests the globe window.</summary>
    private void Globe_Click(object sender, RoutedEventArgs args) => WorldTimeWindowRequested?.Invoke(WindowStateKeys.CelestialMap);

    /// <summary>Moves the launcher into the notification area.</summary>
    private void Hide_Click(object sender, RoutedEventArgs args) => HideLauncher();

    /// <summary>Requests orderly application shutdown.</summary>
    private async void Exit_Click(object sender, RoutedEventArgs args) => await ConfirmExitAsync();

    /// <summary>Confirms explicit exit requests from both the launcher and the notification area.</summary>
    private async Task ConfirmExitAsync()
    {
        if (_closePromptOpen || _allowClose) return;
        _closePromptOpen = true;
        try
        {
            ShowLauncher();
            if (await _dialogs.ConfirmAsync(this, DialogRequest.Confirmation(T("Launcher.CloseTitle"),
                T("Launcher.ExitMessage"), T("Launcher.Exit"), T("Dialog.Cancel")))
                && !_lifetime.IsCancellationRequested)
                ExitRequested?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception error) { ApplicationErrorService.Report(error, "LauncherExit", _settings.UiLanguage); }
        finally { _closePromptOpen = false; }
    }

    /// <summary>Brings the open workspace windows to the foreground.</summary>
    private async void ShowAll_Click(object sender, RoutedEventArgs args) => await RevealOpenWindowsAsync();

    /// <summary>Opens or activates the application information window.</summary>
    private void About_Click(object sender, RoutedEventArgs args) => ShowAbout();

    /// <summary>Persists a user toggle while ignoring programmatic changes and an active save.</summary>
    private async void Preference_Toggled(object sender, RoutedEventArgs args)
    {
        if (_applying || _saving)
        {
            return;
        }
        var key = ReferenceEquals(sender, SnappingToggle) ? "window.snapping.enabled" : "window.titlebar.auto_hide";
        var value = ((ToggleSwitch)sender).IsOn ? "true" : "false";
        await SavePreferenceAsync(key, value);
    }

    /// <summary>Persists the selected theme after control initialization has completed.</summary>
    private async void Theme_Changed(object sender, SelectionChangedEventArgs args)
    {
        if (_applying || _saving || ThemeCombo.SelectedIndex < 0)
        {
            return;
        }
        await SavePreferenceAsync("theme", new[] { "system", "light", "dark" }[ThemeCombo.SelectedIndex]);
    }

    /// <summary>Persists the selected interface language after control initialization has completed.</summary>
    private async void Language_Changed(object sender, SelectionChangedEventArgs args)
    {
        if (_applying || _saving || LanguageCombo.SelectedValue is not string language)
        {
            return;
        }
        await SavePreferenceAsync("language", language);
    }

    /// <summary>Saves one preference and restores the last committed UI state on failure.</summary>
    private async Task SavePreferenceAsync(string key, string value)
    {
        _saving = true;
        SettingsExpander.IsEnabled = false;
        try
        {
            var changedPreferences = new Dictionary<string, string?> { [key] = value };
            var patch = new SettingsPatch(changedPreferences);
            var result = await _application.PatchSettingsAsync(patch, _lifetime.Token);
            if (!result.Succeeded || result.Value is null)
            {
                ShowFailure(result.MessageKey);
                ApplySettings(_settings);
                return;
            }
            await ApplyExternalSettingsAsync(result.Value);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            ApplySettings(_settings);
            ApplicationErrorService.Report(exception, "LauncherSettings", _settings.UiLanguage);
        }
        finally
        {
            _saving = false;
            SettingsExpander.IsEnabled = true;
        }
    }

    /// <summary>Persists Windows startup integration and reloads the committed settings.</summary>
    private async void Startup_Toggled(object sender, RoutedEventArgs args)
    {
        if (_applying || _saving)
        {
            return;
        }
        _saving = true;
        SettingsExpander.IsEnabled = false;
        try
        {
            var result = await _application.SetStartupEnabledAsync(StartupToggle.IsOn, _lifetime.Token);
            if (!result.Succeeded)
            {
                ShowFailure(result.MessageKey);
                ApplySettings(_settings);
                return;
            }
            var settings = await _application.GetSettingsAsync(_lifetime.Token);
            if (!settings.Succeeded || settings.Value is null)
            {
                throw new InvalidOperationException("Saved startup preference is unavailable.");
            }
            await ApplyExternalSettingsAsync(settings.Value);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            ApplySettings(_settings);
            ApplicationErrorService.Report(exception, "LauncherStartupSetting", _settings.UiLanguage);
        }
        finally
        {
            _saving = false;
            SettingsExpander.IsEnabled = true;
        }
    }

    /// <summary>Builds localized notification-area commands from the current launcher state.</summary>
    private TrayIconMenuState CreateTrayMenuState() => new(T("Tray.ShowAllWindows"), T("Tray.WorldClocks"),
        T("Tray.AstronomicalCalendar"), T("Tray.DayNightMap"), T("Tray.DayNightGlobe"), T("MenuTitleOptions"), T("MenuTitleAbout"), !_allowClose);

    /// <summary>Marshals a notification-area exit request onto the UI dispatcher.</summary>
    private void Tray_ExitRequested(object? sender, EventArgs args) => DispatcherQueue.TryEnqueue(async () => await ConfirmExitAsync());

    /// <summary>Dispatches a notification-area command and reports failures on the UI thread.</summary>
    private void Tray_CommandRequested(TrayIconMenuCommand command) => DispatcherQueue.TryEnqueue(async () =>
    {
        try
        {
            switch (command)
            {
                case TrayIconMenuCommand.ShowAllWindows: await RevealOpenWindowsAsync(); break;
                case TrayIconMenuCommand.WorldClocks: WorldClocksRequested?.Invoke(this, EventArgs.Empty); break;
                case TrayIconMenuCommand.AstronomicalCalendar: WorldTimeWindowRequested?.Invoke(WindowStateKeys.AstronomyAgenda); break;
                case TrayIconMenuCommand.DayNightMap: WorldTimeWindowRequested?.Invoke(WindowStateKeys.WorldMap); break;
                case TrayIconMenuCommand.DayNightGlobe: WorldTimeWindowRequested?.Invoke(WindowStateKeys.CelestialMap); break;
                case TrayIconMenuCommand.Settings: ShowLauncher(); SettingsExpander.IsExpanded = true; break;
                case TrayIconMenuCommand.About: ShowLauncher(); ShowAbout(); break;
            }
        }
        catch (Exception exception)
        {
            ApplicationErrorService.Report(exception, "LauncherTray", _settings.UiLanguage);
        }
    });

    /// <summary>Asks the application facade to reveal the launcher and its peer windows.</summary>
    private async Task RevealOpenWindowsAsync()
    {
        ShowLauncher();
        var handle = WinRT.Interop.WindowNative.GetWindowHandle(this).ToInt64();
        var peerWindowHandles = WindowPlacementService.GetOpenPeerWindowHandles(handle);
        var request = new WindowRevealRequest(handle, peerWindowHandles);
        var result = await _application.RevealOpenWindowsAsync(request, _lifetime.Token);
        if (!result.Succeeded)
        {
            ShowFailure(result.MessageKey);
        }
    }

    /// <summary>Creates the information window once and activates the existing instance thereafter.</summary>
    private void ShowAbout()
    {
        if (_about is null)
        {
            _about = new AboutWindow(_application, _settings.Theme, _settings.UiLanguage, _appWindow, WinRT.Interop.WindowNative.GetWindowHandle(this));
            _about.Closed += About_Closed;
        }
        _about.Activate();
    }

    /// <summary>Detaches the closed information window so the next request creates a new instance.</summary>
    private void About_Closed(object sender, WindowEventArgs args)
    {
        if (_about is not null)
        {
            _about.Closed -= About_Closed;
        }
        _about = null;
    }

    /// <summary>Presents a localized failure in the window's existing notification surface.</summary>
    private void ShowFailure(string key)
    {
        StatusBar.Message = T(key);
        StatusBar.IsOpen = true;
    }

    /// <summary>Reveals the launcher and explains why the clock window could not open.</summary>
    internal void ShowWorldClockOpenFailure()
    {
        ShowLauncher();
        ShowFailure("WorldClock.CatalogUnavailable");
    }

    /// <summary>Registers the launcher handle and localized commands with the tray service.</summary>
    internal void EnsureNotificationAreaIcon() => _trayIcon.ShowInNotificationArea(WinRT.Interop.WindowNative.GetWindowHandle(this), _iconPath, "WorkTrail", TrayLabels());

    /// <summary>Creates localized labels for the notification-area visibility commands.</summary>
    private TrayIconMenuLabels TrayLabels() => new(T("Tray.ShowMainWindow"), T("Tray.HideMainWindow"), T("Tray.CloseApplication"));

    /// <summary>Centers the launcher on the pointer's display and activates it.</summary>
    internal void ShowLauncher()
    {
        _placement.CenterOnCursorDisplay(RootGrid);
        _trayIcon.ShowMainWindow();
        Activate();
    }

    /// <summary>Initializes the native window before hiding it in the notification area.</summary>
    internal void StartMinimizedToNotificationArea()
    {
        Activate();
        HideLauncher();
    }

    /// <summary>Hides the launcher through the tray service and restores it if that operation fails.</summary>
    private void HideLauncher()
    {
        try
        {
            _trayIcon.HideToNotificationArea(WinRT.Interop.WindowNative.GetWindowHandle(this), _iconPath, "WorkTrail", TrayLabels());
        }
        catch (Exception exception)
        {
            Activate();
            ApplicationErrorService.Report(exception, "LauncherTray", _settings.UiLanguage);
        }
    }

    /// <summary>Intercepts native closure while the window's close policy completes.</summary>
    private async void AppWindow_Closing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (_allowClose)
        {
            return;
        }
        args.Cancel = true;
        if (_closePromptOpen)
        {
            return;
        }
        _closePromptOpen = true;
        try
        {
            var choice = await _dialogs.ChooseLauncherCloseAsync(this, T);
            if (_allowClose || _lifetime.IsCancellationRequested)
            {
                return;
            }
            if (choice == ContentDialogResult.Primary)
            {
                HideLauncher();
            }
            else if (choice == ContentDialogResult.Secondary)
            {
                ExitRequested?.Invoke(this, EventArgs.Empty);
            }
        }
        catch (Exception exception)
        {
            ApplicationErrorService.Report(exception, "LauncherClose", _settings.UiLanguage);
        }
        finally
        {
            _closePromptOpen = false;
        }
    }

    /// <summary>Closes the window through the application's orderly shutdown path.</summary>
    internal void CloseForShutdown()
    {
        _allowClose = true;
        _about?.Close();
        Close();
    }

    /// <summary>Stops pending presentation work and releases window event subscriptions and resources.</summary>
    private void Window_Closed(object sender, WindowEventArgs args)
    {
        _homeCityTimer.Stop();
        _homeCityTimer.Tick -= HomeCityTimer_Tick;
        _lifetime.Cancel();
        _ready.TrySetCanceled();
        _trayIcon.ExitRequested -= Tray_ExitRequested;
        _trayIcon.CommandRequested -= Tray_CommandRequested;
        _trayIcon.MenuStateProvider = null;
        _trayIcon.Dispose();
        _titleBar.Dispose();
        _placement.Dispose();
        _lifetime.Dispose();
    }

    #endregion
}
