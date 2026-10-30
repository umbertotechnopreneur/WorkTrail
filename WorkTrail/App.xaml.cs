// SPDX-License-Identifier: MIT
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;
using System.Runtime.InteropServices;
using WorkTrail.Application;
using WorkTrail.Controls;
using WorkTrail.Presentation;
using WorkTrail.Runtime;
using WorkTrail.Services;

namespace WorkTrail;

/// <summary>Composes the launcher, independent widgets and their single shared settings runtime.</summary>
public partial class App : Microsoft.UI.Xaml.Application
{
    #region Fields

    private readonly ServiceProvider _services;
    private readonly ILogger<App> _logger;
    private readonly MicaDialogService _dialogs = new();
    private readonly DispatcherQueue _dispatcherQueue;
    private MainWindow? _window;
    private WorldClockWindow? _worldClockWindow;
    private AvailabilityPlannerWindow? _availabilityPlannerWindow;
    private bool _availabilityPlannerOpening;
    private WorldMapWindow? _worldMapWindow;
    private LunarPhaseWindow? _lunarPhaseWindow;
    private readonly Dictionary<string, CelestialWindow> _celestialWindows = new(StringComparer.Ordinal);
    private readonly HashSet<string> _celestialWindowsOpening = new(StringComparer.Ordinal);
    private RuntimeHost? _runtimeHost;
    private IWorkTrailApplication? _applicationFacade;
    private bool _worldClockWindowOpening;
    private bool _worldMapWindowOpening;
    private bool _lunarPhaseWindowOpening;
    private bool _uiStarting;
    private bool _showMainWindowWhenUiReady;
    private string _uiLanguage = "system";
    private int _shutdownStarted;

    #endregion

    #region Initialization

    /// <summary>Initializes application logging before any preferences or windows are opened.</summary>
    public App()
    {
        _dispatcherQueue = DispatcherQueue.GetForCurrentThread() ?? throw new InvalidOperationException("The WinUI dispatcher is unavailable.");
        _services = LoggingBootstrapper.CreateServiceProvider();
        _logger = _services.GetRequiredService<ILogger<App>>();
        ApplicationErrorService.Configure(_logger);
        UnhandledException += Application_UnhandledException;
        InitializeComponent();
        WindowPlacementService.PersistenceFailed += WindowPlacementService_PersistenceFailed;
        WindowPlacementService.SnappingFailed += WindowPlacementService_SnappingFailed;
    }

    #endregion

    #region Implementation

    /// <summary>Reports an unhandled UI exception through the durable application error service.</summary>
    private void Application_UnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs args)
    {
        args.Handled = true;
        ApplicationErrorService.Report(args.Exception, "WinUI", _uiLanguage);
    }

    /// <summary>Starts the desktop workspace after parsing launch or Windows sign-in activation.</summary>
    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        try
        {
            var kind = ReadActivationKind();
            if (kind == ExtendedActivationKind.Protocol)
            {
                throw new ArgumentException("Protocol activation is not supported.");
            }
            var options = StartupActivationPolicy.Apply(LaunchOptions.Parse(Environment.GetCommandLineArgs().Skip(1).ToArray()), kind);
            StartUi(options);
        }
        catch (Exception exception)
        {
            ApplicationErrorService.Report(exception, "Launch", _uiLanguage);
        }
    }

    /// <summary>Restores the existing launcher after Windows redirects another activation.</summary>
    internal void HandleRedirectedActivation(RedirectedActivationRequest activation)
    {
        if (!_dispatcherQueue.TryEnqueue(() => StartUi(activation.Options)))
        {
            _logger.LogWarning("Activation arrived while the widget dispatcher was shutting down.");
        }
    }

    /// <summary>Creates the main UI surfaces after application startup has resolved their dependencies.</summary>
    private async void StartUi(LaunchOptions options)
    {
        if (_window is not null)
        {
            if (!options.StartWithWindows && options.Mode != LaunchMode.Background)
            {
                _window.ShowLauncher();
            }
            return;
        }
        if (_uiStarting)
        {
            _showMainWindowWhenUiReady |= !options.StartWithWindows && options.Mode != LaunchMode.Background;
            return;
        }
        _uiStarting = true;
        try
        {
            var application = StartOrConnectRuntime();
            var result = await application.GetSettingsAsync(CancellationToken.None);
            if (!result.Succeeded || result.Value is null)
            {
                throw new InvalidOperationException($"Widget preferences could not be loaded ({result.Code}).");
            }
            var settings = result.Value;
            ApplyTitleBarSettings(settings);
            var tray = new TrayIconService(_services.GetRequiredService<ILoggerFactory>().CreateLogger<TrayIconService>());
            _window = new MainWindow(application, options, settings, _dialogs, tray, Path.Combine(AppContext.BaseDirectory, "Assets", "WorkTrailIcon.ico"));
            _window.SettingsApplied += ApplyTitleBarSettings;
            _window.SettingsApplied += ApplyWorldClockWindowSettings;
            _window.WorldClocksRequested += MainWindow_WorldClocksRequested;
            _window.WorldTimeWindowRequested += OpenAstronomyWindowFromMenu;
            _window.ExitRequested += MainWindow_ExitRequested;
            try
            {
                _window.EnsureNotificationAreaIcon();
                if ((options.StartWithWindows || options.Mode == LaunchMode.Background) && !_showMainWindowWhenUiReady)
                {
                    _window.StartMinimizedToNotificationArea();
                }
                else
                {
                    _window.ShowLauncher();
                }
            }
            catch (Exception exception)
            {
                _window.Activate();
                ApplicationErrorService.Report(exception, "LauncherTray", _uiLanguage);
            }
            _showMainWindowWhenUiReady = false;
            await _window.WaitForWorkspaceReadyAsync();
            if (!settings.WelcomeCompleted)
            {
                _window.ShowLauncher();
                var catalog = await application.GetWorldClockCityCatalogAsync(CancellationToken.None);
                if (!catalog.Succeeded || catalog.Value is null)
                {
                    throw new InvalidOperationException("The offline city catalog is unavailable for welcome.");
                }
                if (!await _dialogs.ShowWelcomeAsync(application, _window, catalog.Value, new LocalizationService(_uiLanguage)))
                {
                    return;
                }
                settings = (await application.GetSettingsAsync(CancellationToken.None)).Value
                    ?? throw new InvalidOperationException("Home-city preferences could not be loaded.");
                await _window.ApplyExternalSettingsAsync(settings);
            }
            foreach (var key in WorkspaceWindowState.GetWindowsToRestore(settings.WindowOpenStates))
            {
                if (Volatile.Read(ref _shutdownStarted) != 0)
                {
                    break;
                }
                switch (key)
                {
                    case WindowStateKeys.WorldClocks: await ShowWorldClockWindowAsync(application); break;
                    case WindowStateKeys.AvailabilityPlanner: await ShowAvailabilityPlannerAsync(); break;
                    case WindowStateKeys.WorldMap: await ShowAstronomyWindowAsync(false); break;
                    case WindowStateKeys.LunarPhase: await ShowAstronomyWindowAsync(true); break;
                    case WindowStateKeys.LocalSky:
                    case WindowStateKeys.AstronomyAgenda:
                    case WindowStateKeys.CelestialMap: await ShowCelestialWindowAsync(key); break;
                }
            }
        }
        catch (OperationCanceledException) when (Volatile.Read(ref _shutdownStarted) != 0)
        {
        }
        catch (Exception exception)
        {
            ApplicationErrorService.Report(exception, "WidgetStartup", _uiLanguage);
        }
        finally
        {
            _uiStarting = false;
        }
    }

    /// <summary>Propagates the saved caption visibility preference to window controllers.</summary>
    private void ApplyTitleBarSettings(AppSettings settings)
    {
        _uiLanguage = settings.UiLanguage;
        CustomTitleBarController.ApplyAutoHideSetting(settings.AutoHideTitleBar);
        _applicationFacade?.ConfigureWindowSnapping(settings.WindowSnappingEnabled);
    }

    /// <summary>Starts or connects to the shared application runtime for this launch.</summary>
    private IWorkTrailApplication StartOrConnectRuntime()
    {
        if (_applicationFacade is not null)
        {
            return _applicationFacade;
        }
        var factory = _services.GetRequiredService<ILoggerFactory>();
        var installation = WorkTrailApplicationFactory.LoadInstallationId();
        var host = new RuntimeHost(() => WorkTrailApplicationFactory.Create(factory, _services.GetRequiredService<ObservabilityHealth>()),
            installation, factory.CreateLogger<RuntimeHost>());
        if (host.TryStart())
        {
            _runtimeHost = host;
            return _applicationFacade = host.Application;
        }
        _ = host.DisposeAsync();
        return _applicationFacade = new RuntimeClient(installation, TimeSpan.FromSeconds(5), factory.CreateLogger<RuntimeClient>());
    }

    /// <summary>Starts orderly shutdown after the launcher requests application exit.</summary>
    private async void MainWindow_ExitRequested(object? sender, EventArgs args)
    {
        if (Interlocked.Exchange(ref _shutdownStarted, 1) != 0)
        {
            return;
        }
        try
        {
            // Save open widget visibility before closing native windows for process shutdown.
            await WindowPlacementService.PrepareForShutdownAsync();
            _dialogs.CloseActive();
            foreach (var window in _celestialWindows.Values.ToArray())
            {
                window.CloseForShutdown();
            }
            _worldMapWindow?.CloseForShutdown();
            _lunarPhaseWindow?.CloseForShutdown();
            _worldClockWindow?.CloseForShutdown();
            _availabilityPlannerWindow?.CloseForShutdown();
            _window?.CloseForShutdown();
            if (_runtimeHost is not null)
            {
                await _runtimeHost.DisposeAsync();
            }
            else if (_applicationFacade is not null)
            {
                await _applicationFacade.DisposeAsync();
            }
            WindowPlacementService.PersistenceFailed -= WindowPlacementService_PersistenceFailed;
            WindowPlacementService.SnappingFailed -= WindowPlacementService_SnappingFailed;
            await LoggingBootstrapper.ShutdownAsync(_services);
            Exit();
        }
        catch (Exception exception)
        {
            Interlocked.Exchange(ref _shutdownStarted, 0);
            ApplicationErrorService.Report(exception, "WidgetShutdown", _uiLanguage);
        }
    }


    /// <summary>Classifies the activation payload used to select the requested application surface.</summary>
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


    /// <summary>Opens the saved clock workspace in response to the launcher.</summary>
    private async void MainWindow_WorldClocksRequested(object? sender, EventArgs eventArgs) =>
        await ShowWorldClockWindowAsync(StartOrConnectRuntime());



    /// <summary>Creates or activates the clock window while coordinating its opening state.</summary>
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
            AttachAstronomyContextMenu(_worldClockWindow, WindowStateKeys.WorldClocks);
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


    /// <summary>Applies committed preferences to the existing clock window.</summary>
    private void ApplyWorldClockWindowSettings(AppSettings settings)
    {
        _worldClockWindow?.ApplySettings(settings);
        ApplyAstronomyWindowSettings(settings);
    }


    /// <summary>Propagates committed preferences to open astronomy windows.</summary>
    private void ApplyAstronomyWindowSettings(AppSettings settings)
    {
        _availabilityPlannerWindow?.ApplySettings(settings);
        _worldMapWindow?.ApplySettings(settings);
        _lunarPhaseWindow?.ApplySettings(settings);
        foreach (var window in _celestialWindows.Values)
        {
            window.ApplySettings(settings);
        }
    }


    /// <summary>Reports a native window-snapping failure through the application error surface.</summary>
    private async Task WindowPlacementService_SnappingFailed(Window owner, Exception exception)
    {
        _logger.LogError(exception, "Window snapping failed; native free movement remains available for this operation.");
        if (Volatile.Read(ref _shutdownStarted) != 0 || owner.Content is not FrameworkElement { IsLoaded: true })
        {
            return;
        }
        var strings = new LocalizationService(_uiLanguage);
        await _dialogs.ShowInformativeAsync(owner, DialogRequest.Informative(
            strings.Translate("Operations.Status.Failed.Title"),
            strings.Translate("Options.Window.Snapping.Failed"),
            strings.Translate("Dialog.Ok")));
    }


    /// <summary>Reports a failed window-state save while retaining the running workspace.</summary>
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


    /// <summary>Opens the map requested by the clock window.</summary>
    private async void WorldClockWindow_WorldMapRequested(object? sender, EventArgs args) =>
        await ShowAstronomyWindowAsync(isLunarPhase: false);


    /// <summary>Opens the lunar phase surface requested by the clock window.</summary>
    private async void WorldClockWindow_LunarPhaseRequested(object? sender, EventArgs args) =>
        await ShowAstronomyWindowAsync(isLunarPhase: true);


    /// <summary>Shares the clock reference instant with open astronomy surfaces.</summary>
    private void WorldClockWindow_ProjectionChanged(WorldClockSnapshot snapshot, bool isLive)
    {
        _worldMapWindow?.ApplySnapshot(snapshot, isLive);
        _lunarPhaseWindow?.ApplySnapshot(snapshot, isLive);
        foreach (var window in _celestialWindows.Values)
        {
            window.ApplySnapshot(snapshot, isLive);
        }
    }


    /// <summary>Opens the requested astronomy surface from the clock window.</summary>
    private async void WorldClockWindow_CelestialWindowRequested(string key) => await ShowCelestialWindowAsync(key);


    /// <summary>Attaches the shared astronomy menu to a window and binds it to that window's settings key.</summary>
    /// <param name="window">The window that owns the context menu.</param>
    /// <param name="windowKey">The persisted state key for the owning window.</param>
    private void AttachAstronomyContextMenu(Window window, string windowKey)
    {
        window.Content.ContextFlyout = AstronomyWindowMenu.Create(
            () => new LocalizationService(_uiLanguage),
            OpenAstronomyWindowFromMenu,
            () => _window?.ShowLauncher(),
            WindowPlacementService.GetTaskbarVisibilityReader(window),
            showInTaskbar => SetAstronomyWindowShownInTaskbarAsync(windowKey, showInTaskbar),
            window.Close);
    }


    /// <summary>Persists taskbar visibility for one astronomy settings group and refreshes every open projection.</summary>
    /// <param name="windowKey">The state key of the window whose menu was used.</param>
    /// <param name="showInTaskbar">Whether the settings group should appear in the taskbar and system switchers.</param>
    /// <returns><see langword="true"/> when the setting was persisted and applied; otherwise, <see langword="false"/>.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The supplied window key has no astronomy taskbar setting.</exception>
    private async Task<bool> SetAstronomyWindowShownInTaskbarAsync(string windowKey, bool showInTaskbar)
    {
        var settingKey = windowKey switch
        {
            WindowStateKeys.WorldMap => "window.world_map.show_in_taskbar",
            WindowStateKeys.LunarPhase => "window.lunar_phase.show_in_taskbar",
            WindowStateKeys.WorldClocks or WindowStateKeys.LocalSky or WindowStateKeys.AstronomyAgenda or WindowStateKeys.CelestialMap
                => "window.world_clocks.show_in_taskbar",
            _ => throw new ArgumentOutOfRangeException(nameof(windowKey), windowKey, "Unsupported astronomy window key.")
        };

        try
        {
            var application = StartOrConnectRuntime();
            var result = await application.PatchSettingsAsync(
                new SettingsPatch(new Dictionary<string, string?>
                {
                    [settingKey] = showInTaskbar ? "true" : "false"
                }),
                CancellationToken.None);
            if (!result.Succeeded || result.Value is null)
            {
                _logger.LogWarning(
                    "Astronomy taskbar visibility could not be saved. WindowKey={WindowKey} Code={Code}",
                    windowKey,
                    result.Code);
                return false;
            }

            if (_window is not null)
            {
                await _window.ApplyExternalSettingsAsync(result.Value);
            }
            else
            {
                ApplyWorldClockWindowSettings(result.Value);
            }

            return true;
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Astronomy taskbar visibility could not be changed. WindowKey={WindowKey}", windowKey);
            return false;
        }
    }


    /// <summary>Routes a menu selection to the corresponding astronomy window.</summary>
    private async void OpenAstronomyWindowFromMenu(string key)
    {
        if (key == WindowStateKeys.WorldMap)
        {
            await ShowAstronomyWindowAsync(isLunarPhase: false);
        }
        else if (key == WindowStateKeys.LunarPhase)
        {
            await ShowAstronomyWindowAsync(isLunarPhase: true);
        }
        else
        {
            await ShowCelestialWindowAsync(key);
        }
    }


    /// <summary>Opens one reusable planner surface through the shared runtime.</summary>
    private async Task ShowAvailabilityPlannerAsync()
    {
        if (_availabilityPlannerWindow is not null) { _availabilityPlannerWindow.Activate(); return; }
        if (_availabilityPlannerOpening) return;
        _availabilityPlannerOpening = true;
        try
        {
            var application = StartOrConnectRuntime();
            var settings = await application.GetSettingsAsync(CancellationToken.None);
            if (Volatile.Read(ref _shutdownStarted) != 0) return;
            if (!settings.Succeeded || settings.Value is null) throw new InvalidOperationException("Planner settings are unavailable.");
            var created = new AvailabilityPlannerWindow(application, settings.Value);
            _availabilityPlannerWindow = created;
            created.Closed += (sender, _) => { if (ReferenceEquals(sender, _availabilityPlannerWindow)) _availabilityPlannerWindow = null; };
            created.Activate();
        }
        catch (Exception error)
        {
            ApplicationErrorService.Report(error, "AvailabilityPlannerOpening", _uiLanguage);
        }
        finally { _availabilityPlannerOpening = false; }
    }

    /// <summary>Creates or activates the requested celestial surface.</summary>
    private async Task ShowCelestialWindowAsync(string key)
    {
        if (key == WindowStateKeys.AvailabilityPlanner)
        {
            await ShowAvailabilityPlannerAsync();
            return;
        }
        if (!_celestialWindowsOpening.Add(key))
        {
            return;
        }
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
            if (_window is null || Volatile.Read(ref _shutdownStarted) != 0)
            {
                return;
            }
            if (!settings.Succeeded || settings.Value is null)
            {
                throw new InvalidOperationException($"Celestial window settings are unavailable ({settings.Code}).");
            }
            created = new CelestialWindow(application, _dialogs, settings.Value, key);
            if (key is WindowStateKeys.AstronomyAgenda or WindowStateKeys.CelestialMap)
            {
                AttachAstronomyContextMenu(created, key);
            }

            _celestialWindows.Add(key, created);
            created.Closed += (sender, _) =>
            {
                if (_celestialWindows.TryGetValue(key, out var current) && ReferenceEquals(current, sender))
                {
                    _celestialWindows.Remove(key);
                }
            };
            if (_worldClockWindow?.CurrentSnapshot is { } snapshot)
            {
                created.ApplySnapshot(snapshot, _worldClockWindow.IsLive);
            }
            created.Activate();
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Celestial window could not be opened. WindowKey={WindowKey}", key);
            // Only the failed new instance is removed; activation failure must retain an existing surface.
            if (created is not null)
            {
                _celestialWindows.Remove(key);
                try
                {
                    created.CloseAfterFailedOpening();
                }
                catch (Exception cleanupException)
                {
                    _logger.LogError(cleanupException, "Failed celestial opening could not release its window.");
                }
            }
            if (_window is not null && Volatile.Read(ref _shutdownStarted) == 0)
            {
                _window.ShowWorldClockOpenFailure();
            }
        }
        finally
        {
            _celestialWindowsOpening.Remove(key);
        }
    }


    /// <summary>Coordinates an astronomy window's opening and handles initialization failure.</summary>
    private async Task ShowAstronomyWindowAsync(bool isLunarPhase)
    {
        Window? existing = isLunarPhase ? _lunarPhaseWindow : _worldMapWindow;
        if (isLunarPhase ? _lunarPhaseWindowOpening : _worldMapWindowOpening)
        {
            return;
        }

        if (isLunarPhase)
        {
            _lunarPhaseWindowOpening = true;
        }
        else
        {
            _worldMapWindowOpening = true;
        }
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
                AttachAstronomyContextMenu(_lunarPhaseWindow, WindowStateKeys.LunarPhase);
                _lunarPhaseWindow.Closed += (sender, _) =>
                {
                    if (ReferenceEquals(_lunarPhaseWindow, sender))
                    {
                        _lunarPhaseWindow = null;
                    }
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
                AttachAstronomyContextMenu(_worldMapWindow, WindowStateKeys.WorldMap);
                _worldMapWindow.Closed += (sender, _) =>
                {
                    if (ReferenceEquals(_worldMapWindow, sender))
                    {
                        _worldMapWindow = null;
                    }
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
            if (isLunarPhase)
            {
                _lunarPhaseWindowOpening = false;
            }
            else
            {
                _worldMapWindowOpening = false;
            }
        }
    }


    /// <summary>Releases the closed clock instance and its event subscriptions.</summary>
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

    #endregion
}

/// <summary>Maps Windows sign-in activation to the hidden launcher.</summary>
internal static class StartupActivationPolicy
{

    /// <summary>Marks sign-in activation for a hidden launcher while preserving other launch options.</summary>
    internal static LaunchOptions Apply(LaunchOptions options, ExtendedActivationKind kind) =>
        kind == ExtendedActivationKind.StartupTask ? options with { StartWithWindows = true } : options;
}
