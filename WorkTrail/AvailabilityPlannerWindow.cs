// SPDX-License-Identifier: MIT
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WorkTrail.Application;
using WorkTrail.Controls;
using WorkTrail.Services;

namespace WorkTrail;

/// <summary>Hosts the reusable planner over the shared facade and independent window lifetime.</summary>
internal sealed class AvailabilityPlannerWindow : Window
{
    private readonly IWorkTrailApplication _application;
    private readonly AvailabilityPlannerControl _planner = new();
    private readonly Grid _root = new();
    private readonly InfoBar _status = new() { IsClosable = true };
    private readonly ProgressBar _progress = new() { IsIndeterminate = true, Height = 3, Visibility = Visibility.Collapsed };
    private readonly WindowPlacementService _placement;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly DispatcherQueueTimer _refresh;
    private LocalizationService _strings = new("system");
    private readonly AvailabilityPlannerSession _session = new();
    private CancellationTokenSource? _request;
    private bool _loaded;
    private bool _closed;
    private bool _editing;
    private bool _savingViewPreferences;
    private bool _savingTableDisplay;
    private DayOfWeek _firstDay = DayOfWeek.Monday;
    private double _columnWidth;
    private double? _pendingColumnWidth;
    private bool _savingColumnWidth;

    /// <summary>Requests World Clock's city configuration.</summary>
    internal event Action<Window>? OpenClocksRequested;

    /// <summary>Publishes the committed week preference to the other app windows.</summary>
    internal event Action<AppSettings>? SettingsSaved;

    /// <summary>Requests the application's global weather settings.</summary>
    internal event Action? WeatherSettingsRequested;

    /// <summary>Refreshes weather while retaining the current reference and viewport.</summary>
    internal Task RefreshWeatherAsync() => RefreshAsync();

    /// <summary>Refreshes the timetable after the shared city selection changes.</summary>
    internal Task RefreshCitiesAsync(WorldClockSnapshot snapshot)
    {
        if (_session.Snapshot is { } displayed &&
            displayed.Cities.Select(city => city.Clock.CityId).SequenceEqual(snapshot.Clocks.Select(clock => clock.CityId)))
        {
            return Task.CompletedTask;
        }

        _session.ReconcileCities(snapshot.Clocks);
        return RefreshAsync();
    }

    /// <summary>Creates the work surface using the existing placement infrastructure.</summary>
    internal AvailabilityPlannerWindow(IWorkTrailApplication application, AppSettings settings)
    {
        _application = application;
        SystemBackdrop = new GlassBackdrop();
        _root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        _root.Children.Add(_progress);
        Grid.SetRow(_status, 1); _root.Children.Add(_status);
        Grid.SetRow(_planner, 2); _root.Children.Add(_planner);
        Content = _root;
        _placement = new WindowPlacementService(application, this, AppWindow, WindowStateKeys.AvailabilityPlanner, 1420, 960, 16);
        _placement.ApplyDefaultBounds(_root);
        _refresh = DispatcherQueue.CreateTimer();
        _refresh.Interval = TimeSpan.FromMinutes(1);
        _refresh.Tick += Refresh_Tick;
        _planner.ReferenceEdited += ReferenceEdited;
        _planner.CurrentTimeRequested += CurrentTimeRequested;
        _planner.ScheduleRequested += EditSchedule;
        _planner.WeatherSetupRequested += ShowWeatherSetup;
        _planner.ExportPdfRequested += ExportPdf;
        _planner.FirstDayOfWeekRequested += SaveFirstDayOfWeek;
        _planner.ViewPreferencesRequested += SaveViewPreferences;
        _planner.TableDisplayRequested += SaveTableDisplay;
        _planner.ColumnWidthRequested += SaveColumnWidth;
        _planner.OpenClocksRequested += OpenClocks;
        _root.Loaded += Loaded;
        Closed += OnClosed;
        ApplySettings(settings);
    }

    /// <summary>Updates theme and localization while retaining the selected reference.</summary>
    internal void ApplySettings(AppSettings settings)
    {
        _strings = new LocalizationService(settings.UiLanguage);
        _root.Language = _strings.Language;
        _root.RequestedTheme = settings.Theme switch { "dark" => ElementTheme.Dark, "light" => ElementTheme.Light, _ => ElementTheme.Default };
        Title = "WorkTrail · " + _strings.Translate("Planner.Title");
        _planner.ApplyLocalization(_strings);
        _firstDay = settings.PlannerFirstDayOfWeek;
        _planner.SetFirstDayOfWeek(_firstDay);
        if (_session.Snapshot is { } snapshot) _planner.Apply(snapshot, _strings, false, updateReference: false);
        _session.StepMinutes = settings.PlannerStepMinutes;
        _planner.SetViewPreferences(settings.PlannerVisibleHours, _session.StepMinutes);
        _planner.SetTableDisplay(settings.PlannerEmptyCells, settings.PlannerCompactHeaders);
        _columnWidth = settings.PlannerColumnWidth;
        _planner.SetColumnWidthPreference(_columnWidth);
        var appearance = WindowAppearanceSettings.Get(settings, WindowStateKeys.AvailabilityPlanner);
        _placement.ApplyTaskbarVisibility(appearance.ShowInTaskbar);
        _root.Opacity = appearance.OpacityPercent / 100d;
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsAlwaysOnTop = appearance.AlwaysOnTop;
        }
    }

    /// <summary>Uses the same window menu for toolbar and contextual access.</summary>
    internal void SetWindowMenu(MenuFlyout menu) => _planner.SetWindowMenu(menu);

    /// <summary>Closes after the composition root has retained the open workspace.</summary>
    internal void CloseForShutdown() { _lifetime.Cancel(); Close(); }

    private async void Loaded(object sender, RoutedEventArgs args)
    {
        if (_loaded) return;
        _loaded = true;
        try
        {
            await _placement.RestoreAsync(_root, _lifetime.Token);
            await RefreshAsync(navigate: true);
            if (!_closed) _refresh.Start();
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception error) { Report(error); }
    }

    private async void ReferenceEdited(string? cityId, DateTime? date, TimeSpan time)
    {
        _session.EditReference(cityId, date, time);
        // An incomplete edit supersedes any pending result, which would otherwise overwrite the input.
        _request?.Cancel();
        await RefreshAsync(navigate: true);
    }

    private async void CurrentTimeRequested()
    {
        _session.UseLive();
        await RefreshAsync(navigate: true);
    }

    private async void Refresh_Tick(DispatcherQueueTimer sender, object args)
    {
        if (!_editing && _request is null) await RefreshAsync();
    }

    private Task RefreshAsync(bool navigate = false) =>
        !_closed && _session.Request is { } request ? LoadAsync(request, navigate) : Task.CompletedTask;

    private async Task LoadAsync(AvailabilityPlannerRequest request, bool navigate)
    {
        _request?.Cancel();
        using var pending = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _request = pending;
        _progress.Visibility = Visibility.Visible;
        try
        {
            var result = await _application.GetAvailabilityPlannerAsync(request, pending.Token);
            if (pending.IsCancellationRequested || _closed || !ReferenceEquals(_request, pending)) return;
            if (!result.Succeeded || result.Value is null)
            {
                _status.Severity = InfoBarSeverity.Warning; _status.Message = _strings.Translate(result.MessageKey); _status.IsOpen = true;
                return;
            }
            _status.IsOpen = false;
            _session.Apply(result.Value);
            _planner.Apply(result.Value, _strings, navigate);
        }
        catch (OperationCanceledException) when (pending.IsCancellationRequested) { }
        catch (Exception error) { if (!_closed) Report(error); }
        finally
        {
            if (ReferenceEquals(_request, pending))
            {
                _request = null;
                if (!_closed) _progress.Visibility = Visibility.Collapsed;
            }
        }
    }

    private async void EditSchedule(AvailabilityPlannerCity city)
    {
        if (_editing || _session.Snapshot is null) return;
        _editing = true;
        try
        {
            var editor = new AvailabilityScheduleEditor(city.Schedule, DateOnly.FromDateTime(city.Clock.LocalTime.DateTime), _strings);
            var dialog = new ContentDialog
            {
                XamlRoot = _root.XamlRoot,
                RequestedTheme = _root.ActualTheme,
                Title = _strings.Format("Planner.EditCity", city.Clock.CityName),
                Content = editor,
                PrimaryButtonText = _strings.Translate("Planner.Save"),
                CloseButtonText = _strings.Translate("Planner.Cancel")
            };
            if (await dialog.ShowAsync() == ContentDialogResult.Primary && !_closed)
            {
                var result = await _application.SaveAvailabilityScheduleAsync(editor.GetSchedule(), _lifetime.Token);
                if (_closed) return;
                if (!result.Succeeded)
                { _status.Message = _strings.Translate(result.MessageKey); _status.Severity = InfoBarSeverity.Warning; _status.IsOpen = true; }
                else await RefreshAsync();
            }
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception error) { if (!_closed) Report(error); }
        finally { _editing = false; }
    }

    private void ShowWeatherSetup() => WeatherSettingsRequested?.Invoke();

    private void OpenClocks() => OpenClocksRequested?.Invoke(this);

    private async void SaveFirstDayOfWeek(DayOfWeek day)
    {
        try
        {
            var result = await _application.PatchSettingsAsync(new SettingsPatch(new Dictionary<string, string?>
            { ["planner.first_day_of_week"] = day == DayOfWeek.Sunday ? "sunday" : "monday" }), _lifetime.Token);
            if (_closed) return;
            if (result.Succeeded && result.Value is not null)
            {
                _firstDay = result.Value.PlannerFirstDayOfWeek;
                _planner.SetFirstDayOfWeek(_firstDay);
                SettingsSaved?.Invoke(result.Value);
            }
            else
            {
                _planner.SetFirstDayOfWeek(_firstDay);
                _status.Message = _strings.Translate(result.MessageKey);
                _status.Severity = InfoBarSeverity.Error; _status.IsOpen = true;
            }
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception error) { if (!_closed) { _planner.SetFirstDayOfWeek(_firstDay); Report(error); } }
    }

    /// <summary>Persists the timetable density and refreshes its projection when the step changes.</summary>
    private async void SaveViewPreferences(int visibleHours, int stepMinutes)
    {
        if (_savingViewPreferences || _closed) return;
        _savingViewPreferences = true;
        _planner.SetViewPreferencesBusy(true);
        try
        {
            var stepChanged = stepMinutes != _session.StepMinutes;
            var result = await _application.PatchSettingsAsync(new SettingsPatch(new Dictionary<string, string?>
            {
                ["planner.visible_hours"] = visibleHours.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["planner.step_minutes"] = stepMinutes.ToString(System.Globalization.CultureInfo.InvariantCulture)
            }), _lifetime.Token);
            if (_closed) return;
            if (!result.Succeeded || result.Value is null)
            {
                _status.Message = _strings.Translate(result.MessageKey);
                _status.Severity = InfoBarSeverity.Error;
                _status.IsOpen = true;
                return;
            }
            ApplySettings(result.Value);
            SettingsSaved?.Invoke(result.Value);
            if (stepChanged) await RefreshAsync();
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception error) { if (!_closed) Report(error); }
        finally
        {
            _savingViewPreferences = false;
            if (!_closed) _planner.SetViewPreferencesBusy(false);
        }
    }

    /// <summary>Persists visual preferences without recomputing the availability schedule.</summary>
    private async void SaveTableDisplay(bool emptyCells, bool compactHeaders)
    {
        if (_savingTableDisplay || _closed) return;
        _savingTableDisplay = true;
        _planner.SetTableDisplayBusy(true);
        try
        {
            var result = await _application.PatchSettingsAsync(new SettingsPatch(new Dictionary<string, string?>
            {
                ["planner.empty_cells"] = emptyCells ? "true" : "false",
                ["planner.compact_headers"] = compactHeaders ? "true" : "false"
            }), _lifetime.Token);
            if (_closed) return;
            if (result.Succeeded && result.Value is { } saved)
            {
                _planner.SetTableDisplay(saved.PlannerEmptyCells, saved.PlannerCompactHeaders);
                SettingsSaved?.Invoke(saved);
            }
            else
            {
                _status.Message = _strings.Translate(result.MessageKey);
                _status.Severity = InfoBarSeverity.Error;
                _status.IsOpen = true;
            }
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception error) { if (!_closed) Report(error); }
        finally
        {
            _savingTableDisplay = false;
            if (!_closed) _planner.SetTableDisplayBusy(false);
        }
    }

    private async void ExportPdf(AvailabilityPdfRequest request)
    {
        if (_editing || _closed) return;
        _editing = true;
        _planner.SetExportBusy(true);
        try
        {
            var result = await _application.ExportAvailabilityPdfAsync(_placement.OwnerWindowHandle, request, _lifetime.Token);
            if (!_closed && result.Value is not null)
            {
                _status.Message = _strings.Translate(result.MessageKey);
                _status.Severity = result.Succeeded ? InfoBarSeverity.Success : InfoBarSeverity.Error;
                _status.IsOpen = true;
            }
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception error) { if (!_closed) Report(error); }
        finally { _editing = false; if (!_closed) _planner.SetExportBusy(false); }
    }

    /// <summary>Coalesces completed drags and publishes only the latest committed width.</summary>
    private async void SaveColumnWidth(double width)
    {
        if (_closed) return;
        if (!_savingColumnWidth && width == _columnWidth) return;
        _pendingColumnWidth = width;
        if (_savingColumnWidth) return;
        _savingColumnWidth = true;
        try
        {
            while (_pendingColumnWidth is { } requested && !_closed)
            {
                _pendingColumnWidth = null;
                var result = await _application.PatchSettingsAsync(new SettingsPatch(new Dictionary<string, string?>
                {
                    ["planner.column_width"] = requested.ToString("R", System.Globalization.CultureInfo.InvariantCulture)
                }), _lifetime.Token);
                if (_closed) return;
                if (!result.Succeeded || result.Value is null)
                {
                    _planner.SetColumnWidthPreference(_columnWidth);
                    _status.Message = _strings.Translate(result.MessageKey);
                    _status.Severity = InfoBarSeverity.Error;
                    _status.IsOpen = true;
                    continue;
                }
                _columnWidth = result.Value.PlannerColumnWidth;
                if (_pendingColumnWidth is null)
                {
                    ApplySettings(result.Value);
                    SettingsSaved?.Invoke(result.Value);
                }
            }
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception error)
        {
            if (!_closed) { _planner.SetColumnWidthPreference(_columnWidth); Report(error); }
        }
        finally { _savingColumnWidth = false; }
    }

    private void Report(Exception error)
    {
        // Shared reporting writes the durable log and displays the native error dialog.
        ApplicationErrorService.Report(error, "AvailabilityPlanner", _strings.RequestedLanguage);
    }

    private void OnClosed(object sender, WindowEventArgs args)
    {
        _closed = true; _refresh.Stop(); _refresh.Tick -= Refresh_Tick;
        _lifetime.Cancel(); _request?.Cancel(); _placement.Dispose();
        _planner.ReferenceEdited -= ReferenceEdited; _planner.CurrentTimeRequested -= CurrentTimeRequested;
        _planner.ScheduleRequested -= EditSchedule; _planner.WeatherSetupRequested -= ShowWeatherSetup;
        _planner.ExportPdfRequested -= ExportPdf;
        _planner.FirstDayOfWeekRequested -= SaveFirstDayOfWeek; _planner.OpenClocksRequested -= OpenClocks;
        _planner.ViewPreferencesRequested -= SaveViewPreferences;
        _planner.TableDisplayRequested -= SaveTableDisplay;
        _planner.ColumnWidthRequested -= SaveColumnWidth;
        _root.Loaded -= Loaded; Closed -= OnClosed;
        _lifetime.Dispose();
    }
}
