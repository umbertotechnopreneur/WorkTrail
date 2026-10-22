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

    /// <summary>Requests the saved world clocks.</summary>
    public event EventHandler? WorldClocksRequested;
    /// <summary>Requests one of the independent astronomy widgets.</summary>
    public event Action<string>? WorldTimeWindowRequested;
    /// <summary>Publishes committed widget preferences to the open windows.</summary>
    public event Action<AppSettings>? SettingsApplied;
    /// <summary>Requests orderly shutdown of every app window.</summary>
    public event EventHandler? ExitRequested;

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

    private string T(string key) => _strings.Translate(key);

    private void LauncherTile_PointerEntered(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs args) =>
        SetArtworkHighlighted(sender, true);

    private void LauncherTile_PointerExited(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs args) =>
        SetArtworkHighlighted(sender, false);

    private static void SetArtworkHighlighted(object sender, bool highlighted)
    {
        if (sender is FrameworkElement { Parent: Grid tile })
        {
            foreach (var artwork in tile.Children.OfType<Microsoft.UI.Xaml.Shapes.Rectangle>())
            {
                if (artwork.Fill is Controls.LauncherArtworkBrush brush) brush.SetHighlighted(highlighted);
            }
        }
    }

    private async void Root_Loaded(object sender, RoutedEventArgs args)
    {
        if (_loaded) return;
        _loaded = true;
        try
        {
            if (!await _placement.RestoreAsync(RootGrid, _lifetime.Token)) _placement.ApplyDefaultBounds(RootGrid);
            _placement.CenterOnCursorDisplay(RootGrid);
            var startup = await _application.GetStartupStatusAsync(_lifetime.Token);
            if (!startup.Succeeded) ShowFailure(startup.MessageKey);
            else
            {
                _applying = true;
                StartupToggle.IsOn = startup.Value;
                _applying = false;
            }
            await RefreshHomeCityAsync();
            if (_lifetime.IsCancellationRequested) return;
            _homeCityTimer.Start();
            _ready.TrySetResult();
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { _ready.TrySetCanceled(); }
        catch (Exception exception) { _ready.TrySetException(exception); ApplicationErrorService.Report(exception, "LauncherStartup", _settings.UiLanguage); }
    }

    internal Task WaitForWorkspaceReadyAsync() => _ready.Task;

    private void LauncherContent_SizeChanged(object sender, SizeChangedEventArgs args)
    {
        var columns = args.NewSize.Width >= 580d ? 2 : 1;
        if (_launcherColumns != columns)
        {
            _launcherColumns = columns;
            ArrangeCommands(LauncherTiles, [ClocksTile, MapTile, CalendarTile, SkyTile, GlobeTile, MoonTile], columns);
        }
        var footerColumns = args.NewSize.Width >= 420d ? 3 : 1;
        if (_footerColumns != footerColumns)
        {
            _footerColumns = footerColumns;
            ArrangeCommands(LauncherFooter, [AboutButton, HideButton, ExitButton], footerColumns);
        }
        UpdateLauncherTileSizing();
    }

    private void LauncherViewport_SizeChanged(object sender, SizeChangedEventArgs args) => UpdateLauncherTileSizing();

    private void UpdateLauncherTileSizing()
    {
        if (LauncherScroller is null || LauncherBody is null || LauncherTiles is null
            || _launcherColumns == 0 || LauncherBody.ActualHeight <= 0) return;
        var rows = 6 / _launcherColumns;
        // Subtract measured settings/footer space so extra viewport height belongs to the tiles.
        // At the minimum height the content scrolls, including when settings are expanded.
        var surroundingHeight = Math.Max(0, LauncherBody.ActualHeight - LauncherTiles.ActualHeight);
        var tileHeight = Math.Clamp(
            (LauncherScroller.ActualHeight - surroundingHeight - LauncherTiles.RowSpacing * (rows - 1)) / rows,
            112d, 192d);
        tileHeight = Math.Floor(tileHeight);
        var artworkSize = Math.Min(192d, 144d + (tileHeight - 96d) / 2d);
        foreach (var tile in LauncherTiles.Children.OfType<Grid>())
        {
            if (Math.Abs(tile.Height - tileHeight) >= 1d) tile.Height = tileHeight;
            foreach (var artwork in tile.Children.OfType<Microsoft.UI.Xaml.Shapes.Rectangle>())
            {
                artwork.Width = artworkSize;
                artwork.Height = artworkSize;
            }
            foreach (var button in tile.Children.OfType<Button>())
                button.Padding = new Thickness(artworkSize - 12d, 16d, 18d, 16d);
        }
    }

    private static void ArrangeCommands(Grid grid, FrameworkElement[] commands, int columns)
    {
        grid.ColumnDefinitions.Clear();
        grid.RowDefinitions.Clear();
        for (var column = 0; column < columns; column++)
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (var row = 0; row < (commands.Length + columns - 1) / columns; row++)
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        for (var index = 0; index < commands.Length; index++)
        {
            Grid.SetRow(commands[index], index / columns);
            Grid.SetColumn(commands[index], index % columns);
        }
    }

    internal async Task ApplyExternalSettingsAsync(AppSettings settings)
    {
        ApplySettings(settings);
        SettingsApplied?.Invoke(settings);
        if (_loaded) await RefreshHomeCityAsync();
    }

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
            LanguageCombo.ItemsSource = ProductLanguageCatalog.UiChoices;
            LanguageCombo.SelectedItem = settings.UiLanguage;
            _placement.ApplyTaskbarVisibility(settings.MainWindowShowInTaskbar);
            _trayIcon.MenuStateProvider = CreateTrayMenuState;
        }
        finally { _applying = false; }
    }

    private async void HomeCityTimer_Tick(object? sender, object args) => await RefreshHomeCityAsync();

    private async Task RefreshHomeCityAsync()
    {
        var version = ++_homeCityRefreshVersion;
        try
        {
            var result = await _application.GetCelestialReferenceAsync(_lifetime.Token);
            if (_lifetime.IsCancellationRequested || version != _homeCityRefreshVersion) return;
            if (!result.Succeeded || result.Value is null)
            {
                ApplyHomeCity(null);
                ShowFailure(result.MessageKey);
                return;
            }
            ApplyHomeCity(result.Value.HomeCity);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception exception)
        {
            if (_lifetime.IsCancellationRequested || version != _homeCityRefreshVersion) return;
            ApplyHomeCity(null);
            ApplicationErrorService.Report(exception, "LauncherHomeCity", _settings.UiLanguage);
        }
    }

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

        HomeCityNameText.Text = city.CityName;
        HomeCityPositionText.Text = string.Format(_strings.Culture, "{4} · {0:0.####}° {1} · {2:0.####}° {3}",
            Math.Abs(city.Latitude), city.Latitude < 0 ? "S" : "N",
            Math.Abs(city.Longitude), city.Longitude < 0 ? "W" : "E", city.CountryCode);
        if (string.Equals(_homeCityAssetPath, city.SkylineAssetPath, StringComparison.Ordinal)) return;
        HomeCityImage.Source = new BitmapImage(new Uri($"ms-appx:///{city.SkylineAssetPath}")) { DecodePixelWidth = 1280 };
        _homeCityAssetPath = city.SkylineAssetPath;
    }

    private void HomeCityImage_Failed(object sender, ExceptionRoutedEventArgs args)
    {
        if (_lifetime.IsCancellationRequested) return;
        HomeCityImage.Source = null;
        _homeCityAssetPath = null;
        ApplicationErrorService.Report(new InvalidOperationException(args.ErrorMessage), "LauncherHomeCityImage", _settings.UiLanguage);
    }

    private void Clocks_Click(object sender, RoutedEventArgs args) => WorldClocksRequested?.Invoke(this, EventArgs.Empty);
    private void Calendar_Click(object sender, RoutedEventArgs args) => WorldTimeWindowRequested?.Invoke(WindowStateKeys.AstronomyAgenda);
    private void Sky_Click(object sender, RoutedEventArgs args) => WorldTimeWindowRequested?.Invoke(WindowStateKeys.LocalSky);
    private void Moon_Click(object sender, RoutedEventArgs args) => WorldTimeWindowRequested?.Invoke(WindowStateKeys.LunarPhase);
    private void Map_Click(object sender, RoutedEventArgs args) => WorldTimeWindowRequested?.Invoke(WindowStateKeys.WorldMap);
    private void Globe_Click(object sender, RoutedEventArgs args) => WorldTimeWindowRequested?.Invoke(WindowStateKeys.CelestialMap);
    private void Hide_Click(object sender, RoutedEventArgs args) => HideLauncher();
    private void Exit_Click(object sender, RoutedEventArgs args) => ExitRequested?.Invoke(this, EventArgs.Empty);
    private async void ShowAll_Click(object sender, RoutedEventArgs args) => await RevealOpenWindowsAsync();
    private void About_Click(object sender, RoutedEventArgs args) => ShowAbout();

    private async void Preference_Toggled(object sender, RoutedEventArgs args)
    {
        if (_applying || _saving) return;
        var key = ReferenceEquals(sender, SnappingToggle) ? "window.snapping.enabled" : "window.titlebar.auto_hide";
        var value = ((ToggleSwitch)sender).IsOn ? "true" : "false";
        await SavePreferenceAsync(key, value);
    }

    private async void Theme_Changed(object sender, SelectionChangedEventArgs args)
    {
        if (_applying || _saving || ThemeCombo.SelectedIndex < 0) return;
        await SavePreferenceAsync("theme", new[] { "system", "light", "dark" }[ThemeCombo.SelectedIndex]);
    }

    private async void Language_Changed(object sender, SelectionChangedEventArgs args)
    {
        if (_applying || _saving || LanguageCombo.SelectedItem is not string language) return;
        await SavePreferenceAsync("language", language);
    }

    private async Task SavePreferenceAsync(string key, string value)
    {
        _saving = true;
        SettingsExpander.IsEnabled = false;
        try
        {
            var result = await _application.PatchSettingsAsync(new SettingsPatch(new Dictionary<string, string?> { [key] = value }), _lifetime.Token);
            if (!result.Succeeded || result.Value is null) { ShowFailure(result.MessageKey); ApplySettings(_settings); return; }
            await ApplyExternalSettingsAsync(result.Value);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception exception) { ApplySettings(_settings); ApplicationErrorService.Report(exception, "LauncherSettings", _settings.UiLanguage); }
        finally { _saving = false; SettingsExpander.IsEnabled = true; }
    }

    private async void Startup_Toggled(object sender, RoutedEventArgs args)
    {
        if (_applying || _saving) return;
        _saving = true;
        SettingsExpander.IsEnabled = false;
        try
        {
            var result = await _application.SetStartupEnabledAsync(StartupToggle.IsOn, _lifetime.Token);
            if (!result.Succeeded) { ShowFailure(result.MessageKey); ApplySettings(_settings); return; }
            var settings = await _application.GetSettingsAsync(_lifetime.Token);
            if (!settings.Succeeded || settings.Value is null) throw new InvalidOperationException("Saved startup preference is unavailable.");
            await ApplyExternalSettingsAsync(settings.Value);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception exception) { ApplySettings(_settings); ApplicationErrorService.Report(exception, "LauncherStartupSetting", _settings.UiLanguage); }
        finally { _saving = false; SettingsExpander.IsEnabled = true; }
    }

    private TrayIconMenuState CreateTrayMenuState() => new(T("Tray.ShowAllWindows"), T("Tray.WorldClocks"),
        T("Tray.AstronomicalCalendar"), T("Tray.DayNightMap"), T("Tray.DayNightGlobe"), T("MenuTitleOptions"), T("MenuTitleAbout"), !_allowClose);

    private void Tray_ExitRequested(object? sender, EventArgs args) => DispatcherQueue.TryEnqueue(() => ExitRequested?.Invoke(this, EventArgs.Empty));
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
        catch (Exception exception) { ApplicationErrorService.Report(exception, "LauncherTray", _settings.UiLanguage); }
    });

    private async Task RevealOpenWindowsAsync()
    {
        ShowLauncher();
        var handle = WinRT.Interop.WindowNative.GetWindowHandle(this).ToInt64();
        var result = await _application.RevealOpenWindowsAsync(new WindowRevealRequest(handle, WindowPlacementService.GetOpenPeerWindowHandles(handle)), _lifetime.Token);
        if (!result.Succeeded) ShowFailure(result.MessageKey);
    }

    private void ShowAbout()
    {
        if (_about is null)
        {
            _about = new AboutWindow(_application, _settings.Theme, _settings.UiLanguage, _appWindow, WinRT.Interop.WindowNative.GetWindowHandle(this));
            _about.Closed += About_Closed;
        }
        _about.Activate();
    }

    private void About_Closed(object sender, WindowEventArgs args) { if (_about is not null) _about.Closed -= About_Closed; _about = null; }
    private void ShowFailure(string key) { StatusBar.Message = T(key); StatusBar.IsOpen = true; }
    internal void ShowWorldClockOpenFailure() { ShowLauncher(); ShowFailure("WorldClock.CatalogUnavailable"); }

    internal void EnsureNotificationAreaIcon() => _trayIcon.ShowInNotificationArea(WinRT.Interop.WindowNative.GetWindowHandle(this), _iconPath, "WorkTrail", TrayLabels());
    private TrayIconMenuLabels TrayLabels() => new(T("Tray.ShowMainWindow"), T("Tray.HideMainWindow"), T("Tray.CloseApplication"));
    internal void ShowLauncher()
    {
        _placement.CenterOnCursorDisplay(RootGrid);
        _trayIcon.ShowMainWindow();
        Activate();
    }
    internal void StartMinimizedToNotificationArea() { Activate(); HideLauncher(); }
    private void HideLauncher()
    {
        try { _trayIcon.HideToNotificationArea(WinRT.Interop.WindowNative.GetWindowHandle(this), _iconPath, "WorkTrail", TrayLabels()); }
        catch (Exception exception) { Activate(); ApplicationErrorService.Report(exception, "LauncherTray", _settings.UiLanguage); }
    }
    private async void AppWindow_Closing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (_allowClose) return;
        args.Cancel = true;
        if (_closePromptOpen) return;
        _closePromptOpen = true;
        try
        {
            var choice = await _dialogs.ChooseLauncherCloseAsync(this, T);
            if (_allowClose || _lifetime.IsCancellationRequested) return;
            if (choice == ContentDialogResult.Primary) HideLauncher();
            else if (choice == ContentDialogResult.Secondary) ExitRequested?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception exception) { ApplicationErrorService.Report(exception, "LauncherClose", _settings.UiLanguage); }
        finally { _closePromptOpen = false; }
    }
    internal void CloseForShutdown() { _allowClose = true; _about?.Close(); Close(); }
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
}
