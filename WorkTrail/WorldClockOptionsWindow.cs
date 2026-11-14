// SPDX-License-Identifier: MIT

using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Graphics;
using WorkTrail.Application;
using WorkTrail.Controls;
using WorkTrail.Services;

namespace WorkTrail;

/// <summary>Hosts the shared clock settings in a separate window.</summary>
internal sealed class WorldClockOptionsWindow : Window
{
    private const int DefaultWidth = 1040;
    private const int DefaultHeight = 720;
    private const int ScreenMargin = 16;
    private readonly Grid _root;
    private readonly IWorkTrailApplication _application;
    private readonly WorldClockSession _session;
    private readonly MicaDialogService _dialogs;
    private readonly CancellationToken _sessionLifetime;
    private readonly WorldClockOptionsControl _options = new();
    private readonly TimedInfoBar _notificationBanner = new()
    {
        Margin = new Thickness(24, 0, 24, 20),
        VerticalAlignment = VerticalAlignment.Bottom
    };
    private AppSettings _settings;
    private LocalizationService _strings;
    private bool _closed;

    /// <summary>Requests the existing picker to add a city.</summary>
    internal event Action<Window>? AddCityRequested;

    /// <summary>Publishes settings after the shared runtime saves them.</summary>
    internal event Action<AppSettings>? SettingsSaved;

    /// <summary>Requests the global weather settings.</summary>
    internal event Action? WeatherSettingsRequested;

    /// <summary>Creates the shared settings surface.</summary>
    internal WorldClockOptionsWindow(IWorkTrailApplication application, WorldClockSession session,
        MicaDialogService dialogs, AppSettings settings, CancellationToken sessionLifetime)
    {
        _application = application;
        _session = session;
        _dialogs = dialogs;
        _settings = settings;
        _strings = new LocalizationService(settings.UiLanguage);
        _sessionLifetime = sessionLifetime;
        _root = new Grid();
        _root.Children.Add(_options);
        _root.Children.Add(_notificationBanner);
        Content = _root;
        SystemBackdrop = new GlassBackdrop();
        WindowPlacementService.SetWorkTrailIcon(AppWindow);

        _options.Initialize(application, settings, session.Snapshot, session.ReferenceClock?.CityId, _strings, sessionLifetime);
        _options.AddRequested += (_, _) => AddCityRequested?.Invoke(this);
        _options.ReferenceRequested += (_, args) => _session.SelectReference(args.CityId);
        _options.RemoveRequested += async (_, args) =>
        {
            var result = await RunClockCommandAsync(() => _session.RemoveCityAsync(args.CityId, _sessionLifetime));
            if (!_closed && result is { Succeeded: true, Value: not null })
            {
                _dialogs.Notifications.ShowInfo(_notificationBanner,
                    _strings.Translate("WorldClock.RemovedTitle"), _strings.Format("WorldClock.RemovedMessage", args.CityName));
            }
        };
        _options.MoveUpRequested += async (_, args) => await RunClockCommandAsync(
            () => _session.MoveCityAsync(args.CityId, WorldClockMoveDirection.Up, _sessionLifetime));
        _options.MoveDownRequested += async (_, args) => await RunClockCommandAsync(
            () => _session.MoveCityAsync(args.CityId, WorldClockMoveDirection.Down, _sessionLifetime));
        _options.WarningRequested += ShowFailure;
        _options.SettingsSaved += saved =>
        {
            if (!_closed)
            {
                ApplySettings(saved);
            }
            SettingsSaved?.Invoke(saved);
        };
        _options.KeySetupRequested += (_, _) => WeatherSettingsRequested?.Invoke();
        _options.ProviderLinkRequested += OpenProviderLink;
        ApplySettings(settings);
        _session.ProjectionChanged += ApplyProjection;
        Closed += OptionsWindow_Closed;
    }

    /// <summary>Centers the initial bounds on the invoking window's display before activation.</summary>
    internal void ShowCentered(Window openingWindow)
    {
        var display = DisplayArea.GetFromWindowId(openingWindow.AppWindow.Id, DisplayAreaFallback.Primary);
        var area = display.WorkArea;
        var scale = WindowInteropService.GetRasterizationScale(WinRT.Interop.WindowNative.GetWindowHandle(openingWindow));
        var margin = (int)Math.Ceiling(ScreenMargin * scale);
        var width = Math.Clamp((int)Math.Ceiling(DefaultWidth * scale), 1, Math.Max(1, area.Width - 2 * margin));
        var height = Math.Clamp((int)Math.Ceiling(DefaultHeight * scale), 1, Math.Max(1, area.Height - 2 * margin));
        AppWindow.MoveAndResize(new RectInt32(
            area.X + (area.Width - width) / 2,
            area.Y + (area.Height - height) / 2,
            width,
            height), display);
        Activate();
    }

    /// <summary>Loads the city list when the settings are opened independently of the clock window.</summary>
    internal async Task RefreshAsync() =>
        await RunClockCommandAsync(() => _session.RefreshAsync(_sessionLifetime));

    /// <summary>Applies the current theme, language and saved appearance preferences.</summary>
    internal void ApplySettings(AppSettings settings)
    {
        _settings = settings;
        _strings = new LocalizationService(settings.UiLanguage);
        _root.RequestedTheme = settings.Theme switch
        {
            "light" => ElementTheme.Light,
            "dark" => ElementTheme.Dark,
            _ => ElementTheme.Default
        };
        Title = _strings.Translate("WorldClock.Options.Title");
        _options.ApplyLanguage(_strings);
        _options.ApplyState(settings, _session.Snapshot, _session.ReferenceClock?.CityId);
    }

    /// <summary>Shows feedback in the settings window that initiated the command.</summary>
    internal void ShowFailure(string messageKey)
    {
        if (!_closed)
        {
            _dialogs.Notifications.ShowWarning(_notificationBanner,
                _strings.Translate("WorldClock.ErrorTitle"), _strings.Translate(messageKey));
        }
    }

    private async Task<OperationResult<WorldClockSnapshot>?> RunClockCommandAsync(
        Func<Task<OperationResult<WorldClockSnapshot>?>> command)
    {
        try
        {
            var result = await command();
            if (result is { Succeeded: false } or { Value: null })
            {
                ShowFailure(result.MessageKey);
            }
            return result;
        }
        catch (OperationCanceledException) when (_sessionLifetime.IsCancellationRequested)
        {
            return null;
        }
        catch (Exception exception)
        {
            ApplicationErrorService.Record(exception, "WorldClockOptions");
            ShowFailure("WorldClock.CatalogUnavailable");
            return null;
        }
    }

    private async void OpenProviderLink(object? sender, EventArgs args)
    {
        try
        {
            var result = await _application.OpenProductLinkAsync("openweather", _sessionLifetime);
            if (!result.Succeeded)
            {
                ShowFailure("About.LinkFailed");
            }
        }
        catch (OperationCanceledException) when (_sessionLifetime.IsCancellationRequested) { }
        catch (Exception exception)
        {
            ApplicationErrorService.Record(exception, "WorldClockOptionsProviderLink");
            ShowFailure("About.LinkFailed");
        }
    }

    private void ApplyProjection(WorldClockSnapshot snapshot, bool updateSettings)
    {
        if (updateSettings && !_closed)
        {
            _options.ApplyState(_settings, snapshot, _session.ReferenceClock?.CityId);
        }
    }

    private void OptionsWindow_Closed(object sender, WindowEventArgs args)
    {
        _closed = true;
        _session.ProjectionChanged -= ApplyProjection;
    }
}
