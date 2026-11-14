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
    private readonly Grid _root = new() { Padding = new Thickness(12) };
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
    private bool _savingPalette;
    private DayOfWeek _firstDay = DayOfWeek.Monday;
    private double _columnWidth;
    private double _homeColumnWidth;
    private (double Home, double Other)? _pendingColumnWidth;
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
        _planner.PaletteRequested += SavePalette;
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
        _planner.SetPalette(settings.PlannerPalette);
        _columnWidth = settings.PlannerColumnWidth;
        _homeColumnWidth = settings.PlannerHomeColumnWidth;
        _planner.SetColumnWidthPreferences(_homeColumnWidth, _columnWidth);
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

    private async void SavePalette(int palette)
    {
        if (_savingPalette || _closed) return;
        _savingPalette = true;
        try
        {
            var result = await _application.PatchSettingsAsync(new SettingsPatch(new Dictionary<string, string?>
            {
                ["planner.palette"] = palette.ToString(System.Globalization.CultureInfo.InvariantCulture)
            }), _lifetime.Token);
            if (_closed) return;
            if (result.Succeeded && result.Value is { } saved)
            {
                _planner.SetPalette(saved.PlannerPalette);
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
        finally { _savingPalette = false; }
    }
    private async Task<AvailabilityPdfRequest?> ChoosePdfOptionsAsync(AvailabilityPdfRequest request)
    {
        var fileName = new TextBox
        {
            Header = _strings.Translate("Planner.PdfFileName"),
            Text = $"Capetime-{request.Snapshot.NowUtc:yyyy-MM-dd_HH-mm}-UTC",
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        var pageSize = new ComboBox { Header = _strings.Translate("Planner.PdfPageSize"), HorizontalAlignment = HorizontalAlignment.Stretch };
        foreach (var name in new[] { "A4", "A3", "Letter", "Legal" }) pageSize.Items.Add(name);
        pageSize.SelectedIndex = 0;
        var orientation = new ComboBox { Header = _strings.Translate("Planner.PdfOrientation"), HorizontalAlignment = HorizontalAlignment.Stretch };
        orientation.Items.Add(_strings.Translate("Planner.PdfLandscape"));
        orientation.Items.Add(_strings.Translate("Planner.PdfPortrait"));
        orientation.SelectedIndex = 0;
        var margins = new ComboBox { Header = _strings.Translate("Planner.PdfMargins"), HorizontalAlignment = HorizontalAlignment.Stretch };
        margins.Items.Add(_strings.Translate("Planner.PdfNarrow"));
        margins.Items.Add(_strings.Translate("Planner.PdfNoMargins"));
        margins.SelectedIndex = 0;
        var error = new TextBlock
        {
            Text = _strings.Translate("Planner.PdfInvalidName"),
            Foreground = (Microsoft.UI.Xaml.Media.Brush)Microsoft.UI.Xaml.Application.Current.Resources["SystemFillColorCriticalBrush"],
            TextWrapping = TextWrapping.Wrap,
            Visibility = Visibility.Collapsed
        };
        var content = new StackPanel { Spacing = 12, MinWidth = 320 };
        content.Children.Add(fileName);
        content.Children.Add(error);
        content.Children.Add(pageSize);
        content.Children.Add(orientation);
        content.Children.Add(margins);
        var dialog = new ContentDialog
        {
            XamlRoot = _root.XamlRoot,
            RequestedTheme = _root.ActualTheme,
            Style = (Style)Microsoft.UI.Xaml.Application.Current.Resources["WorkTrailDialogStyle"],
            Title = _strings.Translate("Planner.PdfOptions"),
            Content = content,
            PrimaryButtonText = _strings.Translate("Planner.Save"),
            CloseButtonText = _strings.Translate("Planner.Cancel"),
            DefaultButton = ContentDialogButton.Primary
        };
        dialog.PrimaryButtonClick += (_, args) =>
        {
            var name = fileName.Text.Trim();
            var stem = name.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase) ? name[..^4] : name;
            if (string.IsNullOrWhiteSpace(stem) || stem.EndsWith('.') || stem.EndsWith(' ') ||
                stem.IndexOfAny(['<', '>', ':', '"', '/', (char)92, '|', '?', '*']) >= 0 || stem.Any(char.IsControl))
            {
                args.Cancel = true;
                error.Visibility = Visibility.Visible;
            }
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary || _closed) return null;
        return request with
        {
            FileName = fileName.Text.Trim(),
            PageSize = (AvailabilityPdfPageSize)pageSize.SelectedIndex,
            Orientation = (AvailabilityPdfOrientation)orientation.SelectedIndex,
            Margins = (AvailabilityPdfMargins)margins.SelectedIndex
        };
    }
    private async void ExportPdf(AvailabilityPdfRequest request)
    {
        if (_editing || _closed) return;
        _editing = true;
        _planner.SetExportBusy(true);
        try
        {
            var options = await ChoosePdfOptionsAsync(request);
            if (options is null || _closed) return;
            var result = await _application.ExportAvailabilityPdfAsync(_placement.OwnerWindowHandle, options, _lifetime.Token);
            if (_closed) return;
            if (result.Succeeded && result.Value is not null)
            {
                var confirmation = new StackPanel { Spacing = 12, MinWidth = 300 };
                var badge = new Border
                {
                    Background = (Microsoft.UI.Xaml.Media.Brush)Microsoft.UI.Xaml.Application.Current.Resources["SystemFillColorSuccessBrush"],
                    CornerRadius = new CornerRadius(20),
                    Width = 40,
                    Height = 40,
                    HorizontalAlignment = HorizontalAlignment.Left,
                    Child = new SymbolIcon(Symbol.Accept)
                };
                confirmation.Children.Add(badge);
                confirmation.Children.Add(new TextBlock { Text = _strings.Translate("Planner.PdfSavedDetail"), TextWrapping = TextWrapping.Wrap });
                confirmation.Children.Add(new TextBlock { Text = result.Value, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true, Opacity = 0.75 });
                var dialog = new ContentDialog
                {
                    XamlRoot = _root.XamlRoot,
                    RequestedTheme = _root.ActualTheme,
                    Style = (Style)Microsoft.UI.Xaml.Application.Current.Resources["WorkTrailDialogStyle"],
                    Title = _strings.Translate("Planner.PdfSaved"),
                    Content = confirmation,
                    CloseButtonText = _strings.Translate("Dialog.Ok"),
                    DefaultButton = ContentDialogButton.Close
                };
                await dialog.ShowAsync();
            }
            else if (!result.Succeeded)
            {
                _status.Message = _strings.Translate(result.MessageKey);
                _status.Severity = InfoBarSeverity.Error;
                _status.IsOpen = true;
            }
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception error) { if (!_closed) Report(error); }
        finally { _editing = false; if (!_closed) _planner.SetExportBusy(false); }
    }

    /// <summary>Coalesces completed drags and publishes only the latest committed width.</summary>
    private async void SaveColumnWidth(double homeWidth, double width)
    {
        if (_closed) return;
        if (!_savingColumnWidth && width == _columnWidth && homeWidth == _homeColumnWidth) return;
        _pendingColumnWidth = (homeWidth, width);
        if (_savingColumnWidth) return;
        _savingColumnWidth = true;
        try
        {
            while (_pendingColumnWidth is { } requested && !_closed)
            {
                _pendingColumnWidth = null;
                var result = await _application.PatchSettingsAsync(new SettingsPatch(new Dictionary<string, string?>
                {
                    ["planner.column_width"] = requested.Other.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
                    ["planner.home_column_width"] = requested.Home.ToString("R", System.Globalization.CultureInfo.InvariantCulture)
                }), _lifetime.Token);
                if (_closed) return;
                if (!result.Succeeded || result.Value is null)
                {
                    _planner.SetColumnWidthPreferences(_homeColumnWidth, _columnWidth);
                    _status.Message = _strings.Translate(result.MessageKey);
                    _status.Severity = InfoBarSeverity.Error;
                    _status.IsOpen = true;
                    continue;
                }
                _columnWidth = result.Value.PlannerColumnWidth;
                _homeColumnWidth = result.Value.PlannerHomeColumnWidth;
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
            if (!_closed) { _planner.SetColumnWidthPreferences(_homeColumnWidth, _columnWidth); Report(error); }
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
        _planner.PaletteRequested -= SavePalette;
        _planner.ColumnWidthRequested -= SaveColumnWidth;
        _root.Loaded -= Loaded; Closed -= OnClosed;
        _lifetime.Dispose();
    }
}
