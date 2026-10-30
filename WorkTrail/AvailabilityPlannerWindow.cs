// SPDX-License-Identifier: MIT
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
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
    private readonly ContentControl _setupHost = new() { Visibility = Visibility.Collapsed, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, MaxWidth = 600 };
    private readonly WindowPlacementService _placement;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly DispatcherQueueTimer _refresh;
    private LocalizationService _strings = new("system");
    private AvailabilityPlannerSnapshot? _snapshot;
    private CancellationTokenSource? _request;
    private bool _loaded;
    private bool _closed;
    private bool _editing;

    /// <summary>Creates the work surface using the existing placement infrastructure.</summary>
    internal AvailabilityPlannerWindow(IWorkTrailApplication application, AppSettings settings)
    {
        _application = application;
        SystemBackdrop = new MicaBackdrop();
        _root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        _root.Children.Add(_progress);
        Grid.SetRow(_status, 1); _root.Children.Add(_status);
        Grid.SetRow(_planner, 2); _root.Children.Add(_planner);
        Grid.SetRow(_setupHost, 2); _root.Children.Add(_setupHost);
        Content = _root;
        _placement = new WindowPlacementService(application, this, AppWindow, WindowStateKeys.AvailabilityPlanner, 1420, 960, 16);
        _placement.ApplyDefaultBounds(_root);
        _refresh = DispatcherQueue.CreateTimer();
        _refresh.Interval = TimeSpan.FromMinutes(1);
        _refresh.Tick += Refresh_Tick;
        _planner.Requested += Request;
        _planner.ScheduleRequested += EditSchedule;
        _planner.WeatherSetupRequested += ShowWeatherSetup;
        _planner.ExportPdfRequested += ExportPdf;
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
        if (_snapshot is not null) _planner.Apply(_snapshot, _strings, false);
    }

    /// <summary>Closes after the composition root has retained the open workspace.</summary>
    internal void CloseForShutdown() { _lifetime.Cancel(); Close(); }

    private async void Loaded(object sender, RoutedEventArgs args)
    {
        if (_loaded) return;
        _loaded = true;
        try
        {
            await _placement.RestoreAsync(_root, _lifetime.Token);
            await LoadAsync(new(), true);
            if (!_closed) _refresh.Start();
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception error) { Report(error); }
    }

    private async void Request(AvailabilityPlannerRequest request) => await LoadAsync(request, true);

    private async void Refresh_Tick(DispatcherQueueTimer sender, object args)
    {
        if (!_editing && _request is null && !_planner.HasPendingEdits) await LoadAsync(_planner.GetRequest(), false);
    }

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
            _snapshot = result.Value;
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
        if (_editing || _snapshot is null) return;
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
                else await LoadAsync(_planner.GetRequest(), false);
            }
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception error) { if (!_closed) Report(error); }
        finally { _editing = false; }
    }

    private async void ShowWeatherSetup()
    {
        if (_editing) return;
        _editing = true;
        var setup = new WeatherKeySetupControl();
        _setupHost.Content = setup; _setupHost.Visibility = Visibility.Visible; _planner.Visibility = Visibility.Collapsed;
        setup.Dismissed += async verified =>
        {
            _setupHost.Content = null; _setupHost.Visibility = Visibility.Collapsed; _planner.Visibility = Visibility.Visible; _editing = false;
            if (verified && !_closed) await LoadAsync(_planner.GetRequest(), false);
        };
        try { await setup.InitializeAsync(_application, _strings, _lifetime.Token); }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception error) { Report(error); }
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

    private void Report(Exception error)
    {
        // Shared reporting writes the durable log and displays the native error dialog.
        ApplicationErrorService.Report(error, "AvailabilityPlanner", _strings.RequestedLanguage);
    }

    private void OnClosed(object sender, WindowEventArgs args)
    {
        _closed = true; _refresh.Stop(); _refresh.Tick -= Refresh_Tick;
        _lifetime.Cancel(); _request?.Cancel(); _placement.Dispose();
        _planner.Requested -= Request; _planner.ScheduleRequested -= EditSchedule; _planner.WeatherSetupRequested -= ShowWeatherSetup;
        _planner.ExportPdfRequested -= ExportPdf;
        _root.Loaded -= Loaded; Closed -= OnClosed;
        _lifetime.Dispose();
    }
}
