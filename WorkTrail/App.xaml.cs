// SPDX-License-Identifier: MIT

using Microsoft.UI.Xaml;
using Microsoft.UI.Dispatching;
using Microsoft.Windows.AppLifecycle;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using WorkTrail.Application;
using WorkTrail.Cli;
using WorkTrail.Controls;
using WorkTrail.Presentation;
using WorkTrail.Runtime;
using WorkTrail.Services;
using TaskbarWidgetSurface = WorkTrail.Taskbar.TaskbarWidgetSurface;

namespace WorkTrail;

/// <summary>Provides the Windows composition root and selects a launch mode before any view is created.</summary>
public partial class App : Microsoft.UI.Xaml.Application
{
    private readonly ServiceProvider _services;
    private readonly ILogger<App> _logger;
    private readonly MicaDialogService _dialogs = new();
    private readonly IWindowsToastNotificationService _windowsNotifications;
    private readonly AtomicResetService _atomicReset = new();
    private readonly DispatcherQueue _dispatcherQueue;
    private MainWindow? _window;
    private WorldClockWindow? _worldClockWindow;
    private SensorsWindow? _sensorsWindow;
    private bool _sensorsWindowOpening;
    private WorldMapWindow? _worldMapWindow;
    private LunarPhaseWindow? _lunarPhaseWindow;
    private readonly Dictionary<string, CelestialWindow> _celestialWindows = new(StringComparer.Ordinal);
    private readonly HashSet<string> _celestialWindowsOpening = new(StringComparer.Ordinal);
    private ScreenshotWindow? _screenshotsWindow;
    private SearchWindow? _searchWindow;
    private QuickSetupWindow? _quickSetupWindow;
    private TaskbarWidgetSurface? _taskbarWidgetSurface;
    private RuntimeHost? _runtimeHost;
    private IWorkTrailApplication? _runtimeApplication;
    private IWorkTrailApplication? _applicationFacade;
    private DashboardRefreshCoordinator? _dashboardRefreshCoordinator;
    private bool _searchWindowOpening;
    private bool _worldClockWindowOpening;
    private bool _worldMapWindowOpening;
    private bool _lunarPhaseWindowOpening;
    private bool _uiStarting;
    private bool _showMainWindowWhenUiReady;
    private bool _quickSetupOwnerWasInteractive;
    private string _uiLanguage = "system";
    private int _shutdownStarted;
    private int _atomicResetStarted;

    /// <summary>Initializes the WinUI application object and its logging composition root.</summary>
    public App()
    {
        _dispatcherQueue = DispatcherQueue.GetForCurrentThread()
            ?? throw new InvalidOperationException("The WinUI dispatcher queue is unavailable.");
        _services = LoggingBootstrapper.CreateServiceProvider();
        _logger = _services.GetRequiredService<ILogger<App>>();
        _windowsNotifications = new WindowsToastNotificationService(
            _services.GetRequiredService<ILoggerFactory>().CreateLogger<WindowsToastNotificationService>());
        InitializeComponent();
        WindowPlacementService.PersistenceFailed += WindowPlacementService_PersistenceFailed;
        WindowPlacementService.SnappingFailed += WindowPlacementService_SnappingFailed;
        UnhandledException += (_, eventArgs) => _logger.LogCritical(eventArgs.Exception, "Unhandled WinUI exception.");
        _logger.LogInformation("WorkTrail process started. Architecture={Architecture}", RuntimeInformation.ProcessArchitecture);
    }

    /// <summary>Routes launch modes to the CLI, background runtime, or WinUI player.</summary>
    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        try
        {
            var activationKind = ReadActivationKind();
            var options = StartupActivationPolicy.Apply(
                LaunchOptions.Parse(Environment.GetCommandLineArgs().Skip(1).ToArray()),
                activationKind);
            _logger.LogInformation(
                "Launch requested. Mode={Mode} ActivationKind={ActivationKind}",
                options.Mode,
                activationKind);
            switch (options.Mode)
            {
                case LaunchMode.Cli:
                    _ = RunCliAndExitAsync(Environment.GetCommandLineArgs().Skip(1).ToArray());
                    return;
                case LaunchMode.Help:
                    _ = RunCliAndExitAsync(["--help"]);
                    return;
                case LaunchMode.Version:
                    _ = RunCliAndExitAsync(["--version"]);
                    return;
                case LaunchMode.Background:
                    StartBackgroundRuntime(options);
                    return;
                default:
                    StartUi(options);
                    return;
            }
        }
        catch (Exception exception)
        {
            _logger.LogCritical(exception, "Launch failed before the main window was created.");
            throw;
        }
    }

    private static ExtendedActivationKind ReadActivationKind()
    {
        try
        {
            return AppInstance.GetCurrent().GetActivatedEventArgs()?.Kind ?? ExtendedActivationKind.Launch;
        }
        catch (Exception exception) when (exception is InvalidOperationException or COMException)
        {
            // Plain command-line launches still have complete bootstrap arguments when rich activation is unavailable.
            return ExtendedActivationKind.Launch;
        }
    }

    /// <summary>Queues the managed launch snapshot to restore the existing UI or promote a headless runtime.</summary>
    internal void HandleRedirectedActivation(RedirectedActivationRequest activation)
    {
        ArgumentNullException.ThrowIfNull(activation);
        if (!_dispatcherQueue.TryEnqueue(() => HandleRedirectedActivationOnUiThread(activation)))
        {
            // Dispatcher rejection means the registered process is already shutting down; no replacement runtime is started in parallel.
            _logger.LogWarning("Redirected activation was ignored because the UI dispatcher is shutting down.");
        }
    }

    private void HandleRedirectedActivationOnUiThread(RedirectedActivationRequest activation)
    {
        var options = activation.Options;
        _logger.LogInformation("Redirected activation received. Mode={Mode} ActivationKind={ActivationKind}", options.Mode, activation.Kind);
        switch (options.Mode)
        {
            case LaunchMode.Background:
                // The registered process already owns startup; a duplicate headless request must not create a window.
                return;
            case LaunchMode.Ui:
                // Promote the existing background process while preserving explicit pause, safe-mode, and UI options.
                StartUi(options);
                return;
            default:
                // Short-lived CLI modes are never redirected by Program and cannot execute inside the runtime owner.
                throw new ArgumentException("Unsupported redirected WorkTrail launch mode.", nameof(activation));
        }
    }

    private async void StartUi(LaunchOptions options)
    {
        if (_window is not null)
        {
            _window.ShowFlyout();
            return;
        }

        if (_uiStarting)
        {
            // A Start-menu activation can arrive while the original UI request is still loading settings.
            // Preserve that explicit reveal so a sign-in launch cannot hide the player after the request.
            _showMainWindowWhenUiReady = true;
            return;
        }

        _uiStarting = true;
        try
        {

            var application = StartOrConnectRuntime();
            // Read the previous session before newly created windows can persist their visibility.
            var initialSettings = await application.GetSettingsAsync(CancellationToken.None);
            if (!initialSettings.Succeeded || initialSettings.Value is null)
            {
                throw new InvalidOperationException($"Workspace settings could not be loaded ({initialSettings.Code}).");
            }

            ApplyTitleBarSettings(initialSettings.Value);
            _dashboardRefreshCoordinator = new DashboardRefreshCoordinator(application);
            var trayIcon = new TrayIconService(_services.GetRequiredService<ILoggerFactory>().CreateLogger<TrayIconService>());
            _window = new MainWindow(application, options, _dialogs, trayIcon, _windowsNotifications, _dashboardRefreshCoordinator);
            _window.SettingsApplied += ApplyTaskbarWidgetSettings;
            _window.SettingsApplied += ApplyWorldClockWindowSettings;
            _window.SettingsApplied += ApplyTitleBarSettings;
            _window.QuickSetupRequested += MainWindow_QuickSetupRequested;
#if DEBUG
            _window.DebugOobeResetRequested += MainWindow_DebugOobeResetRequested;
#endif
            _window.WorldClocksRequested += MainWindow_WorldClocksRequested;
            _window.SensorsRequested += MainWindow_SensorsRequested;
            _window.SearchRequested += MainWindow_SearchRequested;
            _window.ScreenshotGalleryRequested += MainWindow_ScreenshotGalleryRequested;
            _window.ScreenshotGalleryDateRequested += MainWindow_ScreenshotGalleryDateRequested;
            _window.ScreenshotsRequested += MainWindow_ScreenshotsRequested;
            _window.ExitRequested += MainWindow_ExitRequested;
            _window.AtomicResetPrepared += MainWindow_AtomicResetPrepared;
            _window.Closed += MainWindow_Closed;
            var startHidden = options.StartWithWindows && !_showMainWindowWhenUiReady;
            _showMainWindowWhenUiReady = false;
            try
            {
                _window.EnsureNotificationAreaIcon();
                if (startHidden)
                {
                    _window.StartMinimizedToNotificationArea();
                }
                else
                {
                    _window.ShowFlyout();
                }
            }
            catch (Exception exception)
            {
                // If Explorer rejects the tray icon, keep the application reachable through its main window.
                _logger.LogError(exception, "UI startup could not initialize the notification-area icon.");
                var strings = new LocalizationService(options.Language ?? "system");
                _windowsNotifications.TryShow(
                    strings.Translate("Tray.UnavailableTitle"),
                    strings.Translate("Tray.UnavailableMessage"));
                _window.ShowFlyout();
            }

            await CompleteUiStartupAsync(application, options, initialSettings.Value);
        }
        finally
        {
            _uiStarting = false;
        }
    }

    private async Task CompleteUiStartupAsync(IWorkTrailApplication application, LaunchOptions options, AppSettings previousSettings)
    {
        try
        {
            var settingsResult = await application.GetSettingsAsync(CancellationToken.None);
            if (!settingsResult.Succeeded || settingsResult.Value is null)
            {
                throw new InvalidOperationException($"UI startup settings could not be loaded ({settingsResult.Code}).");
            }

            var settings = settingsResult.Value;
            ApplyTaskbarWidgetSettings(settings);
            if (_window is null)
            {
                return;
            }

            await _window.WaitForWorkspaceReadyAsync();
            if (!settings.QuickSetupCompleted && !options.StartWithWindows)
            {
                ShowQuickSetupWindow(application, settings, firstRun: true);
            }
            else
            {
                await RestoreWorkspaceAsync(application, previousSettings);
            }
        }
        catch (OperationCanceledException)
        {
            // The owner already reported an initialization failure or closed before restoration could start.
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "UI startup preparation failed after the main window was activated.");
            DisposeTaskbarWidget();
            if (_window is not null && Volatile.Read(ref _shutdownStarted) == 0)
            {
                var strings = new LocalizationService(previousSettings.UiLanguage);
                await _dialogs.ShowInformativeAsync(_window, DialogRequest.Informative(
                    strings.Translate("Workspace.RestoreFailed.Title"),
                    strings.Translate("Workspace.RestoreFailed.Message"),
                    strings.Translate("Dialog.Ok")));
            }
        }
    }

    private async Task RestoreWorkspaceAsync(IWorkTrailApplication application, AppSettings settings)
    {
        var windowKeys = WorkspaceWindowState.GetWindowsToRestore(settings.WindowOpenStates);
        if (windowKeys.Contains(WindowStateKeys.OcrText) && settings.OcrTextWindowSource is null)
        {
            // An OCR surface requires a retained source; invalid session state must be visible to the user.
            var cleared = await application.SetWindowOpenStateAsync(WindowStateKeys.OcrText, false, CancellationToken.None);
            if (!cleared.Succeeded)
            {
                throw new InvalidOperationException($"The invalid OCR workspace state could not be cleared ({cleared.Code}).");
            }

            throw new InvalidOperationException("The saved OCR workspace is missing its screenshot source.");
        }

        foreach (var key in windowKeys)
        {
            if (_window is null || Volatile.Read(ref _shutdownStarted) != 0)
            {
                return;
            }

            switch (key)
            {
                case WindowStateKeys.Sensors:
                    await ShowSensorsWindowAsync(application);
                    break;
                case WindowStateKeys.WorldClocks:
                    await ShowWorldClockWindowAsync(application);
                    break;
                case WindowStateKeys.WorldMap:
                    await ShowAstronomyWindowAsync(isLunarPhase: false);
                    break;
                case WindowStateKeys.LunarPhase:
                    await ShowAstronomyWindowAsync(isLunarPhase: true);
                    break;
                case WindowStateKeys.LocalSky:
                case WindowStateKeys.AstronomyAgenda:
                case WindowStateKeys.CelestialMap:
                    await ShowCelestialWindowAsync(key);
                    break;
                case WindowStateKeys.Search:
                    await ShowSearchWindowAsync(application);
                    break;
                case WindowStateKeys.Screenshots:
                    if (_screenshotsWindow is not null)
                    {
                        break;
                    }

                    if (windowKeys.Contains(WindowStateKeys.OcrText) && settings.OcrTextWindowSource is { } source)
                    {
                        _screenshotsWindow = new ScreenshotWindow(application, _dialogs, null,
                            source.ScreenshotPath, source.CapturedAt, restoreOcrWindow: true);
                        _screenshotsWindow.Closed += ScreenshotsWindow_Closed;
                        _screenshotsWindow.Activate();
                    }
                    else
                    {
                        await ShowScreenshotWindowAsync(application, null);
                    }

                    break;
            }
        }

        if (_window is not null)
        {
            await _window.RestoreToolWindowsAsync(windowKeys);
        }
    }

    private async void MainWindow_WorldClocksRequested(object? sender, EventArgs eventArgs) =>
        await ShowWorldClockWindowAsync(StartOrConnectRuntime());

    private async void MainWindow_QuickSetupRequested(object? sender, EventArgs eventArgs)
    {
        var application = StartOrConnectRuntime();
        var result = await application.GetSettingsAsync(CancellationToken.None);
        if (!result.Succeeded || result.Value is null)
        {
            if (_window is not null)
            {
                var strings = new LocalizationService("system");
                await _dialogs.ShowInformativeAsync(
                    _window,
                    DialogRequest.Informative(
                        strings.Translate("QuickSetup.Unavailable.Title"),
                        strings.Translate("QuickSetup.Unavailable.Message"),
                        strings.Translate("Dialog.Ok")));
            }

            return;
        }

        ShowQuickSetupWindow(application, result.Value, firstRun: false);
    }

#if DEBUG
    private async void MainWindow_DebugOobeResetRequested(object? sender, EventArgs eventArgs)
    {
        var application = StartOrConnectRuntime();
        var result = await application.GetSettingsAsync(CancellationToken.None);
        if (!result.Succeeded || result.Value is null)
        {
            if (_window is not null)
            {
                var strings = new LocalizationService("system");
                await _dialogs.ShowInformativeAsync(
                    _window,
                    DialogRequest.Informative(
                        strings.Translate("QuickSetup.Unavailable.Title"),
                        strings.Translate("QuickSetup.Unavailable.Message"),
                        strings.Translate("Dialog.Ok")));
            }

            return;
        }

        ShowQuickSetupWindow(application, result.Value, firstRun: true);
    }
#endif

    private void ShowQuickSetupWindow(IWorkTrailApplication application, AppSettings settings, bool firstRun)
    {
        if (_quickSetupWindow is not null)
        {
            _quickSetupWindow.Activate();
            return;
        }

        if (_window is null)
        {
            throw new InvalidOperationException("Quick Setup requires the main WorkTrail window.");
        }

        var ownerHandle = WinRT.Interop.WindowNative.GetWindowHandle(_window);
        var ownerAppWindow = Microsoft.UI.Windowing.AppWindow.GetFromWindowId(
            Microsoft.UI.Win32Interop.GetWindowIdFromWindow(ownerHandle));
        if (_window.Content is UIElement ownerContent)
        {
            _quickSetupOwnerWasInteractive = ownerContent.IsHitTestVisible;
            ownerContent.IsHitTestVisible = false;
        }

        _quickSetupWindow = new QuickSetupWindow(application, settings, firstRun, ownerAppWindow, ownerHandle);
        _quickSetupWindow.ProfileApplied += QuickSetupWindow_ProfileApplied;
        _quickSetupWindow.Closed += QuickSetupWindow_Closed;
        _quickSetupWindow.Activate();
    }

    private async void QuickSetupWindow_ProfileApplied(AppSettings settings)
    {
        if (_window is not null)
        {
            await _window.ApplyExternalSettingsAsync(settings);
        }
        else
        {
            ApplyTaskbarWidgetSettings(settings);
        }
    }

    private void QuickSetupWindow_Closed(object sender, WindowEventArgs args)
    {
        if (_quickSetupWindow is not null)
        {
            _quickSetupWindow.ProfileApplied -= QuickSetupWindow_ProfileApplied;
            _quickSetupWindow.Closed -= QuickSetupWindow_Closed;
            _quickSetupWindow = null;
        }

        if (_window?.Content is UIElement ownerContent)
        {
            ownerContent.IsHitTestVisible = _quickSetupOwnerWasInteractive;
        }
    }

    private async void MainWindow_SearchRequested(object? sender, EventArgs eventArgs) =>
        await ShowSearchWindowAsync(StartOrConnectRuntime());

    private async void MainWindow_ScreenshotGalleryRequested(object? sender, EventArgs eventArgs)
        => await ShowScreenshotWindowAsync(StartOrConnectRuntime(), null);

    private async void MainWindow_ScreenshotGalleryDateRequested(object? sender, ScreenshotGalleryDateRequestedEventArgs eventArgs)
        => await ShowScreenshotWindowAsync(StartOrConnectRuntime(), null, eventArgs.Date);

    private async void MainWindow_ScreenshotsRequested(object? sender, ScreenshotPreviewRequestedEventArgs eventArgs)
        => await ShowScreenshotWindowAsync(StartOrConnectRuntime(), null, eventArgs.ScreenshotPath, eventArgs.CapturedAt);

    private async void MainWindow_ExitRequested(object? sender, EventArgs eventArgs)
    {
        if (_window is not null)
        {
            await _window.RequestCloseAsync();
        }
    }

    private async void MainWindow_SensorsRequested(object? sender, EventArgs eventArgs) =>
        await ShowSensorsWindowAsync(StartOrConnectRuntime());

    private async Task ShowSensorsWindowAsync(IWorkTrailApplication application)
    {
        if (_sensorsWindow is not null) { _sensorsWindow.Activate(); return; }
        if (_sensorsWindowOpening) return;
        _sensorsWindowOpening = true;
        try
        {
            var settings = await application.GetSettingsAsync(CancellationToken.None);
            if (_window is null || Volatile.Read(ref _shutdownStarted) != 0) return;
            if (!settings.Succeeded || settings.Value is null) { _window.ShowSensorsOpenFailure(); return; }
            _sensorsWindow = new SensorsWindow(application, settings.Value);
            _sensorsWindow.SettingsSaved += SensorsWindow_SettingsSaved;
            _sensorsWindow.Closed += SensorsWindow_Closed;
            _sensorsWindow.Activate();
        }
        catch (Exception exception)
        {
            // Opening failures remain visible; never start an alternate telemetry collector.
            _logger.LogError(exception, "Sensor window could not be opened.");
            if (_sensorsWindow is not null)
            {
                _sensorsWindow.SettingsSaved -= SensorsWindow_SettingsSaved;
                _sensorsWindow.Closed -= SensorsWindow_Closed;
                _sensorsWindow.CloseForShutdown();
                _sensorsWindow = null;
            }
            _window?.ShowSensorsOpenFailure();
        }
        finally { _sensorsWindowOpening = false; }
    }

    private async void SensorsWindow_SettingsSaved(AppSettings settings)
    {
        try
        {
            if (_window is not null) await _window.ApplyExternalSettingsAsync(settings);
        }
        catch (Exception exception)
        {
            // Persistence succeeded; report a failed peer refresh instead of undoing confirmed preferences.
            _logger.LogError(exception, "Sensor settings could not refresh the main window.");
            _window?.ShowSensorsOpenFailure();
        }
    }

    private void SensorsWindow_Closed(object sender, WindowEventArgs args)
    {
        if (_sensorsWindow is null) return;
        _sensorsWindow.SettingsSaved -= SensorsWindow_SettingsSaved;
        _sensorsWindow.Closed -= SensorsWindow_Closed;
        _sensorsWindow = null;
    }

    private async Task ShowWorldClockWindowAsync(IWorkTrailApplication application)
    {
        if (_worldClockWindow is not null)
        {
            _worldClockWindow.Activate();
            return;
        }

        if (_worldClockWindowOpening)
        {
            return;
        }

        _worldClockWindowOpening = true;
        try
        {
            var settings = await application.GetSettingsAsync(CancellationToken.None);
            if (_window is null || Volatile.Read(ref _shutdownStarted) != 0)
            {
                return;
            }

            if (!settings.Succeeded || settings.Value is null)
            {
                _logger.LogWarning("World-clock window settings could not be loaded. Code={Code}", settings.Code);
                _window.ShowWorldClockOpenFailure();
                return;
            }

            _worldClockWindow = new WorldClockWindow(application, _dialogs, settings.Value);
            _worldClockWindow.WorldMapRequested += WorldClockWindow_WorldMapRequested;
            _worldClockWindow.LunarPhaseRequested += WorldClockWindow_LunarPhaseRequested;
            _worldClockWindow.CelestialWindowRequested += WorldClockWindow_CelestialWindowRequested;
            _worldClockWindow.ProjectionChanged += WorldClockWindow_ProjectionChanged;
            _worldClockWindow.SettingsSaved += ApplyAstronomyWindowSettings;
            _worldClockWindow.Closed += WorldClockWindow_Closed;
            AttachAstronomyContextMenu(_worldClockWindow);
            _worldClockWindow.Activate();
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "World-clock window could not be opened.");
            _worldClockWindow = null;
            if (_window is not null && Volatile.Read(ref _shutdownStarted) == 0)
            {
                _window.ShowWorldClockOpenFailure();
            }
        }
        finally
        {
            _worldClockWindowOpening = false;
        }
    }

    private void ApplyWorldClockWindowSettings(AppSettings settings)
    {
        _worldClockWindow?.ApplySettings(settings);
        _sensorsWindow?.ApplySettings(settings);
        ApplyAstronomyWindowSettings(settings);
    }

    private void ApplyAstronomyWindowSettings(AppSettings settings)
    {
        _worldMapWindow?.ApplySettings(settings);
        _lunarPhaseWindow?.ApplySettings(settings);
        foreach (var window in _celestialWindows.Values) window.ApplySettings(settings);
    }

    private void ApplyTitleBarSettings(AppSettings settings)
    {
        _uiLanguage = settings.UiLanguage;
        CustomTitleBarController.ApplyAutoHideSetting(settings.AutoHideTitleBar);
        _applicationFacade?.ConfigureWindowSnapping(settings.WindowSnappingEnabled);
    }

    private async Task WindowPlacementService_SnappingFailed(Window owner, Exception exception)
    {
        _logger.LogError(exception, "Window snapping failed; native free movement remains available for this operation.");
        if (Volatile.Read(ref _shutdownStarted) != 0 || owner.Content is not FrameworkElement { IsLoaded: true }) return;
        var strings = new LocalizationService(_uiLanguage);
        await _dialogs.ShowInformativeAsync(owner, DialogRequest.Informative(
            strings.Translate("Operations.Status.Failed.Title"),
            strings.Translate("Options.Window.Snapping.Failed"),
            strings.Translate("Dialog.Ok")));
    }

    private async Task WindowPlacementService_PersistenceFailed(Window owner, string windowKey, Exception exception)
    {
        _logger.LogError(exception, "Window placement persistence failed. WindowKey={WindowKey}", windowKey);
        if (Volatile.Read(ref _shutdownStarted) != 0 || owner.Content is not FrameworkElement { IsLoaded: true })
        {
            return;
        }

        var strings = new LocalizationService(_uiLanguage);
        await _dialogs.ShowInformativeAsync(owner, DialogRequest.Informative(
            strings.Translate("Operations.Status.Failed.Title"),
            strings.Translate("WorldClock.PlacementFailed"),
            strings.Translate("Dialog.Ok")));
    }

    private async void WorldClockWindow_WorldMapRequested(object? sender, EventArgs args) =>
        await ShowAstronomyWindowAsync(isLunarPhase: false);

    private async void WorldClockWindow_LunarPhaseRequested(object? sender, EventArgs args) =>
        await ShowAstronomyWindowAsync(isLunarPhase: true);

    private void WorldClockWindow_ProjectionChanged(WorldClockSnapshot snapshot, bool isLive)
    {
        _worldMapWindow?.ApplySnapshot(snapshot, isLive);
        _lunarPhaseWindow?.ApplySnapshot(snapshot, isLive);
        foreach (var window in _celestialWindows.Values) window.ApplySnapshot(snapshot, isLive);
    }

    private async void WorldClockWindow_CelestialWindowRequested(string key) => await ShowCelestialWindowAsync(key);

    private void AttachAstronomyContextMenu(Window window)
    {
        window.Content.ContextFlyout = AstronomyWindowMenu.Create(
            () => new LocalizationService(_uiLanguage),
            OpenAstronomyWindowFromMenu,
            window.Close);
    }

    private async void OpenAstronomyWindowFromMenu(string key)
    {
        if (key == WindowStateKeys.WorldMap)
        {
            await ShowAstronomyWindowAsync(isLunarPhase: false);
        }
        else
        {
            await ShowCelestialWindowAsync(key);
        }
    }

    private async Task ShowCelestialWindowAsync(string key)
    {
        if (!_celestialWindowsOpening.Add(key)) return;
        CelestialWindow? created = null;
        try
        {
            if (_celestialWindows.TryGetValue(key, out var existing))
            {
                existing.Activate();
                return;
            }

            var application = StartOrConnectRuntime();
            var settings = await application.GetSettingsAsync(CancellationToken.None);
            if (_window is null || Volatile.Read(ref _shutdownStarted) != 0) return;
            if (!settings.Succeeded || settings.Value is null)
                throw new InvalidOperationException($"Celestial window settings are unavailable ({settings.Code}).");

            created = new CelestialWindow(application, _dialogs, settings.Value, key);
            if (key is WindowStateKeys.AstronomyAgenda or WindowStateKeys.CelestialMap)
            {
                AttachAstronomyContextMenu(created);
            }

            _celestialWindows.Add(key, created);
            created.Closed += (sender, _) =>
            {
                if (_celestialWindows.TryGetValue(key, out var current) && ReferenceEquals(current, sender))
                    _celestialWindows.Remove(key);
            };
            if (_worldClockWindow?.CurrentSnapshot is { } snapshot)
                created.ApplySnapshot(snapshot, _worldClockWindow.IsLive);
            created.Activate();
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Celestial window could not be opened. WindowKey={WindowKey}", key);
            // Only the failed new instance is removed; activation failure must retain an existing surface.
            if (created is not null)
            {
                _celestialWindows.Remove(key);
                try { created.CloseAfterFailedOpening(); }
                catch (Exception cleanupException) { _logger.LogError(cleanupException, "Failed celestial opening could not release its window."); }
            }
            if (_window is not null && Volatile.Read(ref _shutdownStarted) == 0) _window.ShowWorldClockOpenFailure();
        }
        finally { _celestialWindowsOpening.Remove(key); }
    }

    private async Task ShowAstronomyWindowAsync(bool isLunarPhase)
    {
        Window? existing = isLunarPhase ? _lunarPhaseWindow : _worldMapWindow;
        if (isLunarPhase ? _lunarPhaseWindowOpening : _worldMapWindowOpening)
        {
            return;
        }

        if (isLunarPhase) _lunarPhaseWindowOpening = true;
        else _worldMapWindowOpening = true;
        Window? createdWindow = null;
        try
        {
            if (existing is not null)
            {
                existing.Activate();
                return;
            }

            var application = StartOrConnectRuntime();
            var settings = await application.GetSettingsAsync(CancellationToken.None);
            if (_window is null || Volatile.Read(ref _shutdownStarted) != 0)
            {
                return;
            }

            if (!settings.Succeeded || settings.Value is null)
            {
                throw new InvalidOperationException($"Astronomy window settings are unavailable ({settings.Code}).");
            }

            if (isLunarPhase)
            {
                _lunarPhaseWindow = new LunarPhaseWindow(application, _dialogs, settings.Value);
                createdWindow = _lunarPhaseWindow;
                AttachAstronomyContextMenu(_lunarPhaseWindow);
                _lunarPhaseWindow.Closed += (sender, _) =>
                {
                    if (ReferenceEquals(_lunarPhaseWindow, sender)) _lunarPhaseWindow = null;
                };
                if (_worldClockWindow?.CurrentSnapshot is { } snapshot)
                {
                    _lunarPhaseWindow.ApplySnapshot(snapshot, _worldClockWindow.IsLive);
                }

                _lunarPhaseWindow.Activate();
            }
            else
            {
                _worldMapWindow = new WorldMapWindow(application, _dialogs, settings.Value);
                createdWindow = _worldMapWindow;
                AttachAstronomyContextMenu(_worldMapWindow);
                _worldMapWindow.Closed += (sender, _) =>
                {
                    if (ReferenceEquals(_worldMapWindow, sender)) _worldMapWindow = null;
                };
                if (_worldClockWindow?.CurrentSnapshot is { } snapshot)
                {
                    _worldMapWindow.ApplySnapshot(snapshot, _worldClockWindow.IsLive);
                }

                _worldMapWindow.Activate();
            }
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Astronomy window could not be opened. IsLunarPhase={IsLunarPhase}", isLunarPhase);
            // Only discard a window created by this attempt; a failed activation must retain an existing surface.
            try
            {
                switch (createdWindow)
                {
                    case LunarPhaseWindow lunarWindow:
                        _lunarPhaseWindow = null;
                        lunarWindow.CloseAfterFailedOpening();
                        break;
                    case WorldMapWindow mapWindow:
                        _worldMapWindow = null;
                        mapWindow.CloseAfterFailedOpening();
                        break;
                }
            }
            catch (Exception cleanupException)
            {
                _logger.LogError(cleanupException, "Failed astronomy opening could not release its window.");
            }

            if (_window is not null && Volatile.Read(ref _shutdownStarted) == 0)
            {
                _window.ShowWorldClockOpenFailure();
            }
        }
        finally
        {
            if (isLunarPhase) _lunarPhaseWindowOpening = false;
            else _worldMapWindowOpening = false;
        }
    }

    private async Task ShowSearchWindowAsync(IWorkTrailApplication application)
    {
        if (_searchWindow is not null)
        {
            _searchWindow.ActivateSearch();
            return;
        }

        if (_searchWindowOpening)
        {
            return;
        }

        _searchWindowOpening = true;
        try
        {
            if (_window is null)
            {
                throw new InvalidOperationException("Search requires the main WorkTrail window.");
            }

            var settingsTask = application.GetSettingsAsync(CancellationToken.None);
            var availabilityTask = application.GetSearchAvailabilityAsync(CancellationToken.None);
            await Task.WhenAll(settingsTask, availabilityTask);
            var settings = await settingsTask;
            var availability = await availabilityTask;
            if (!settings.Succeeded || settings.Value is null || !availability.Succeeded || availability.Value is null)
            {
                throw new InvalidOperationException($"Search availability is unavailable ({settings.Code}, {availability.Code}).");
            }

            if (availability.Value.TotalSnapshotCount == 0)
            {
                var strings = new LocalizationService(settings.Value.UiLanguage);
                await _dialogs.ShowInformativeAsync(
                    _window,
                    DialogRequest.Informative(
                        strings.Translate("Search.Empty.Title"),
                        strings.Translate("Search.Empty.Message"),
                        strings.Translate("Dialog.Ok")));
                return;
            }

            _searchWindow = new SearchWindow(application, settings.Value.UiLanguage, availability.Value);
            _searchWindow.ScreenshotRequested += SearchWindow_ScreenshotRequested;
            _searchWindow.Closed += SearchWindow_Closed;
            _searchWindow.Activate();
        }
        finally
        {
            _searchWindowOpening = false;
        }
    }

    private async void SearchWindow_ScreenshotRequested(object? sender, ScreenshotPreviewRequestedEventArgs eventArgs) =>
        await ShowScreenshotWindowAsync(
            StartOrConnectRuntime(),
            null,
            eventArgs.ScreenshotPath,
            eventArgs.CapturedAt);

    private async Task ShowScreenshotWindowAsync(
        IWorkTrailApplication application,
        string? launchTheme,
        string screenshotPath,
        DateTimeOffset capturedAt)
    {
        if (_screenshotsWindow is not null)
        {
            await _screenshotsWindow.FocusScreenshotAsync(screenshotPath, capturedAt);
            _screenshotsWindow.Activate();
            return;
        }

        _screenshotsWindow = new ScreenshotWindow(application, _dialogs, launchTheme, screenshotPath, capturedAt);
        _screenshotsWindow.Closed += ScreenshotsWindow_Closed;
        _screenshotsWindow.Activate();
    }

    private async Task ShowScreenshotWindowAsync(IWorkTrailApplication application, string? launchTheme)
    {
        if (_screenshotsWindow is not null)
        {
            await _screenshotsWindow.FocusLatestAsync();
            _screenshotsWindow.Activate();
            return;
        }

        _screenshotsWindow = new ScreenshotWindow(application, _dialogs, launchTheme);
        _screenshotsWindow.Closed += ScreenshotsWindow_Closed;
        _screenshotsWindow.Activate();
    }

    private async Task ShowScreenshotWindowAsync(
        IWorkTrailApplication application,
        string? launchTheme,
        DateOnly selectedDate)
    {
        if (_screenshotsWindow is not null)
        {
            await _screenshotsWindow.FocusDateAsync(selectedDate);
            _screenshotsWindow.Activate();
            return;
        }

        _screenshotsWindow = new ScreenshotWindow(application, _dialogs, launchTheme, requestedDate: selectedDate);
        _screenshotsWindow.Closed += ScreenshotsWindow_Closed;
        _screenshotsWindow.Activate();
    }

    private void WorldClockWindow_Closed(object sender, WindowEventArgs args)
    {
        if (_worldClockWindow is not null)
        {
            _worldClockWindow.Closed -= WorldClockWindow_Closed;
            _worldClockWindow.WorldMapRequested -= WorldClockWindow_WorldMapRequested;
            _worldClockWindow.LunarPhaseRequested -= WorldClockWindow_LunarPhaseRequested;
            _worldClockWindow.CelestialWindowRequested -= WorldClockWindow_CelestialWindowRequested;
            _worldClockWindow.ProjectionChanged -= WorldClockWindow_ProjectionChanged;
            _worldClockWindow.SettingsSaved -= ApplyAstronomyWindowSettings;
            _worldClockWindow = null;
        }
    }

    private async void MainWindow_Closed(object sender, WindowEventArgs args)
    {
        foreach (var celestialWindow in _celestialWindows.Values.ToArray()) celestialWindow.CloseForShutdown();
        _celestialWindows.Clear();
        _worldMapWindow?.CloseForShutdown();
        _worldMapWindow = null;
        _lunarPhaseWindow?.CloseForShutdown();
        _lunarPhaseWindow = null;
        if (_window is not null)
        {
            _window.SettingsApplied -= ApplyTaskbarWidgetSettings;
            _window.SettingsApplied -= ApplyWorldClockWindowSettings;
            _window.SettingsApplied -= ApplyTitleBarSettings;
            _window.QuickSetupRequested -= MainWindow_QuickSetupRequested;
#if DEBUG
            _window.DebugOobeResetRequested -= MainWindow_DebugOobeResetRequested;
#endif
            _window.WorldClocksRequested -= MainWindow_WorldClocksRequested;
            _window.SensorsRequested -= MainWindow_SensorsRequested;
            _window.SearchRequested -= MainWindow_SearchRequested;
            _window.ScreenshotGalleryRequested -= MainWindow_ScreenshotGalleryRequested;
            _window.ScreenshotGalleryDateRequested -= MainWindow_ScreenshotGalleryDateRequested;
            _window.ScreenshotsRequested -= MainWindow_ScreenshotsRequested;
            _window.ExitRequested -= MainWindow_ExitRequested;
            _window.AtomicResetPrepared -= MainWindow_AtomicResetPrepared;
            _window.Closed -= MainWindow_Closed;
            _window = null;
        }

        if (_quickSetupWindow is not null)
        {
            _quickSetupWindow.ProfileApplied -= QuickSetupWindow_ProfileApplied;
            _quickSetupWindow.Closed -= QuickSetupWindow_Closed;
            _quickSetupWindow.Close();
            _quickSetupWindow = null;
        }

        if (_sensorsWindow is not null)
        {
            _sensorsWindow.SettingsSaved -= SensorsWindow_SettingsSaved;
            _sensorsWindow.Closed -= SensorsWindow_Closed;
            _sensorsWindow.CloseForShutdown();
            _sensorsWindow = null;
        }

        if (_worldClockWindow is not null)
        {
            _worldClockWindow.Closed -= WorldClockWindow_Closed;
            _worldClockWindow.WorldMapRequested -= WorldClockWindow_WorldMapRequested;
            _worldClockWindow.LunarPhaseRequested -= WorldClockWindow_LunarPhaseRequested;
            _worldClockWindow.CelestialWindowRequested -= WorldClockWindow_CelestialWindowRequested;
            _worldClockWindow.ProjectionChanged -= WorldClockWindow_ProjectionChanged;
            _worldClockWindow.SettingsSaved -= ApplyAstronomyWindowSettings;
            _worldClockWindow.CloseForShutdown();
            _worldClockWindow = null;
        }

        if (_screenshotsWindow is not null)
        {
            _screenshotsWindow.Closed -= ScreenshotsWindow_Closed;
            _screenshotsWindow.CloseForShutdown();
            _screenshotsWindow = null;
        }

        if (_searchWindow is not null)
        {
            _searchWindow.ScreenshotRequested -= SearchWindow_ScreenshotRequested;
            _searchWindow.Closed -= SearchWindow_Closed;
            _searchWindow.Close();
            _searchWindow = null;
        }

        DisposeTaskbarWidget();
        if (Volatile.Read(ref _atomicResetStarted) != 0)
        {
            return;
        }

        await ShutdownRuntimeAsync();
        Exit();
    }

    private void ScreenshotsWindow_Closed(object sender, WindowEventArgs args)
    {
        if (_screenshotsWindow is not null)
        {
            _screenshotsWindow.Closed -= ScreenshotsWindow_Closed;
            _screenshotsWindow = null;
        }
    }

    private void SearchWindow_Closed(object sender, WindowEventArgs args)
    {
        if (_searchWindow is not null)
        {
            _searchWindow.ScreenshotRequested -= SearchWindow_ScreenshotRequested;
            _searchWindow.Closed -= SearchWindow_Closed;
            _searchWindow = null;
        }
    }

    private void ApplyTaskbarWidgetSettings(AppSettings settings)
    {
        if (!settings.TaskbarWidgetVisible)
        {
            DisposeTaskbarWidget();
            return;
        }

        if (_taskbarWidgetSurface is not null)
        {
            _taskbarWidgetSurface.ApplySettings(settings);
            _taskbarWidgetSurface.Configure(settings.TaskbarWidgetPosition);
            return;
        }

        try
        {
            var application = _applicationFacade ?? throw new InvalidOperationException("The taskbar widget requires an initialized application facade.");
            var dashboardRefreshCoordinator = _dashboardRefreshCoordinator
                ?? throw new InvalidOperationException("The taskbar widget requires an initialized dashboard coordinator.");
            var taskbarWidgetSurface = new TaskbarWidgetSurface(application, dashboardRefreshCoordinator, new TaskbarWidgetHost(_services.GetRequiredService<ILogger<TaskbarWidgetHost>>()), _services.GetRequiredService<ILogger<TaskbarWidgetSurface>>());
            _taskbarWidgetSurface = taskbarWidgetSurface;
            taskbarWidgetSurface.FlyoutRequested += (_, _) => _window?.DispatcherQueue.TryEnqueue(() => _window?.ShowFlyout());
            taskbarWidgetSurface.ApplySettings(settings);
            if (!taskbarWidgetSurface.Attach(settings.TaskbarWidgetPosition))
            {
                // If a custom shell rejects parenting, keep the normal player usable rather than leaving an orphaned top-level control.
                DisposeTaskbarWidget();
            }
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Taskbar widget initialization failed; the main window remains available.");
            DisposeTaskbarWidget();
        }
    }

    private void DisposeTaskbarWidget()
    {
        _taskbarWidgetSurface?.Dispose();
        _taskbarWidgetSurface = null;
    }

    private void StartBackgroundRuntime(LaunchOptions options) => _ = StartBackgroundRuntimeAsync(options);

    private async Task StartBackgroundRuntimeAsync(LaunchOptions options)
    {
        try
        {
            var application = StartOrConnectRuntime();
            if (!ReferenceEquals(application, _runtimeApplication))
            {
                return;
            }

            var settings = await application.GetSettingsAsync(CancellationToken.None);
            if (!settings.Succeeded || settings.Value is null)
            {
                // A headless launch cannot present recovery UI, so leave tracking paused and record the explicit failure.
                _logger.LogWarning("Background startup settings could not be loaded. Code={Code}", settings.Code);
                return;
            }

            var startup = await application.SetStartupEnabledAsync(
                settings.Value.StartWithWindows,
                CancellationToken.None);
            if (!startup.Succeeded)
            {
                // Startup reconciliation is recoverable: the runtime may track, but diagnostics retain the OS integration failure.
                _logger.LogWarning("Background Windows startup registration reconciliation failed. Code={Code}", startup.Code);
            }

            if (TrackingStartupPolicy.ShouldStart(options, settings.Value))
            {
                var started = await application.StartTrackingAsync(
                    new StartTrackingRequest(options.SafeMode, "background"),
                    CancellationToken.None);
                if (!started.Succeeded)
                {
                    _logger.LogError("Background tracking startup failed. Code={Code}", started.Code);
                }
            }
        }
        catch (Exception exception)
        {
            // Fire-and-forget launch tasks must retain an explicit paused failure path instead of surfacing an unobserved exception.
            _logger.LogError(exception, "Background startup preparation failed; tracking remains paused.");
        }
    }

    private IWorkTrailApplication StartOrConnectRuntime()
    {
        if (_applicationFacade is not null)
        {
            return _applicationFacade;
        }

        var loggerFactory = _services.GetRequiredService<ILoggerFactory>();
        var observability = _services.GetRequiredService<ObservabilityHealth>();
        var installationId = WorkTrailApplicationFactory.LoadInstallationId();
        var host = new RuntimeHost(
            () => WorkTrailApplicationFactory.Create(loggerFactory, observability),
            installationId,
            loggerFactory.CreateLogger<RuntimeHost>());
        if (host.TryStart())
        {
            var localApplication = host.Application;
            _logger.LogInformation("Runtime ownership acquired for this installation.");
            host.AtomicResetPrepared += RuntimeHost_AtomicResetPrepared;
            _runtimeHost = host;
            _runtimeApplication = localApplication;
            _applicationFacade = localApplication;
            return _applicationFacade;
        }

        // A separate process owns hooks and persistence; this frontend uses the same facade through its pipe.
        _ = host.DisposeAsync();
        _logger.LogInformation("Runtime ownership is held by another process; connecting through the named pipe.");
        _applicationFacade = new RuntimeClient(installationId, TimeSpan.FromSeconds(5), loggerFactory.CreateLogger<RuntimeClient>());
        return _applicationFacade;
    }

    private async Task ShutdownRuntimeAsync()
    {
        if (Interlocked.Exchange(ref _shutdownStarted, 1) != 0)
        {
            return;
        }

        // A local RuntimeHost owns and disposes its application before releasing the runtime mutex.
        // Remote clients do not own a runtime and are disposed directly below.
        _dashboardRefreshCoordinator?.Dispose();
        _dashboardRefreshCoordinator = null;
        if (_runtimeHost is not null)
        {
            _runtimeHost.AtomicResetPrepared -= RuntimeHost_AtomicResetPrepared;
            await _runtimeHost.DisposeAsync();
            _runtimeHost = null;
            _applicationFacade = null;
            _runtimeApplication = null;
        }
        else if (_applicationFacade is not null)
        {
            await _applicationFacade.DisposeAsync();
            _applicationFacade = null;
        }

        _runtimeApplication = null;
    }

    private void MainWindow_AtomicResetPrepared(object? sender, AtomicResetPreparedEventArgs e) =>
        BeginAtomicReset(e.Plan);

    private void RuntimeHost_AtomicResetPrepared(AtomicResetPlan plan)
    {
        _ = _dispatcherQueue.TryEnqueue(() => BeginAtomicReset(plan));
    }

    private void BeginAtomicReset(AtomicResetPlan plan)
    {
        if (Interlocked.Exchange(ref _atomicResetStarted, 1) != 0)
        {
            return;
        }

        WindowPlacementService.DiscardForReset();
        _ = CompleteAtomicResetAsync(plan, ownsRuntime: _runtimeHost is not null);
    }

    private async Task CompleteAtomicResetAsync(AtomicResetPlan plan, bool ownsRuntime)
    {
        try
        {
            _window?.CloseForShutdown();
            if (ownsRuntime)
            {
                // A remote frontend receives the response first and gets a short window to release its log sink.
                await Task.Delay(TimeSpan.FromMilliseconds(750));
            }

            await ShutdownRuntimeAsync();
            await LoggingBootstrapper.ShutdownAsync(_services);
            if (ownsRuntime)
            {
                _atomicReset.ExecuteAndRelaunch(plan);
            }
        }
        catch (Exception exception)
        {
            // Logging may already be closed because deletion must run without open handles.
            System.Diagnostics.Debug.WriteLine($"WorkTrail atomic reset failed: {exception.GetType().Name}");
            Environment.ExitCode = 1;
        }
        finally
        {
            Exit();
        }
    }

    private async Task RunCliAndExitAsync(string[] arguments)
    {
        try
        {
            var executable = Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "WorkTrail.exe");
            Environment.ExitCode = await CliBootstrap.RunAsync(arguments, executable);
        }
        finally
        {
            // Short-lived CLI processes explicitly flush and dispose providers before WinUI exits.
            await LoggingBootstrapper.ShutdownAsync(_services);
            Exit();
        }
    }
}

internal static class StartupActivationPolicy
{
    internal static LaunchOptions Apply(LaunchOptions options, ExtendedActivationKind activationKind)
    {
        ArgumentNullException.ThrowIfNull(options);
        return activationKind == ExtendedActivationKind.StartupTask
            ? options with { Mode = LaunchMode.Ui, StartWithWindows = true }
            : options;
    }
}
