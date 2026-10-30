// SPDX-License-Identifier: MIT
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Automation;
using WorkTrail.Application;
using WorkTrail.Presentation;
using WorkTrail.Services;

namespace WorkTrail.Controls;

/// <summary>Composes the reusable planner surface; application operations remain owned by its host.</summary>
public sealed class AvailabilityPlannerControl : UserControl
{
    private readonly StackPanel _toolbar = new() { Orientation = Orientation.Horizontal, Spacing = 12 };
    private readonly StackPanel _days = new() { Orientation = Orientation.Horizontal, Spacing = 6 };
    private readonly ComboBox _cities = new() { Width = 165, DisplayMemberPath = "CityName", SelectedValuePath = "CityId" };
    private readonly CalendarDatePicker _date = new() { Width = 170, MinDate = new DateTimeOffset(1901, 1, 3, 0, 0, 0, TimeSpan.Zero), MaxDate = new DateTimeOffset(2098, 12, 28, 0, 0, 0, TimeSpan.Zero) };
    private readonly TimePicker _time = new() { ClockIdentifier = "24HourClock", MinuteIncrement = 1 };
    private readonly ComboBox _step = new() { ItemsSource = new[] { 15, 30, 60 }, SelectedItem = 30, Width = 85 };
    private readonly ComboBox _hours = new() { ItemsSource = new[] { 16, 24 }, SelectedItem = 24, Width = 75 };
    private readonly Button _now = new();
    private readonly Button _apply = new();
    private readonly Button _weatherSetup = new();
    private readonly Button _exportPdf = new() { IsEnabled = false };
    private readonly TextBlock _weatherStatus = new() { FontSize = 12, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _selection = new() { FontSize = 14, IsTextSelectionEnabled = true, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _legend = new() { FontSize = 12, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _moonText = new() { FontSize = 13, TextWrapping = TextWrapping.Wrap, MaxWidth = 230 };
    private readonly CelestialPhaseControl _moon = new() { Width = 52, Height = 52, IsDaylight = false };
    private readonly AvailabilityTimelineGrid _timeline = new();
    private LocalizationService _strings = new("system");
    private AvailabilityPlannerSnapshot? _snapshot;
    private bool _rendering;
    private bool _exportBusy;

    /// <summary>Gets whether the reference follows the current time.</summary>
    public bool IsLive { get; private set; } = true;
    /// <summary>Gets whether pending reference edits must be protected from periodic refreshes.</summary>
    public bool HasPendingEdits { get; private set; }
    /// <summary>Requests a fresh application projection.</summary>
    public event Action<AvailabilityPlannerRequest>? Requested;
    /// <summary>Requests a city's schedule editor.</summary>
    public event Action<AvailabilityPlannerCity>? ScheduleRequested;
    /// <summary>Requests the existing weather-key configuration surface.</summary>
    public event Action? WeatherSetupRequested;
    /// <summary>Requests a PDF of the currently displayed logical interval.</summary>
    public event Action<AvailabilityPdfRequest>? ExportPdfRequested;

    /// <summary>Prevents concurrent export dialogs while leaving the timetable visible.</summary>
    public void SetExportBusy(bool busy)
    {
        _exportBusy = busy;
        _exportPdf.IsEnabled = !busy && _snapshot is not null;
    }
    /// <summary>Gets the active reference day for a personal exception editor.</summary>
    public DateOnly? SelectedDate => _snapshot is null ? null : DateOnly.FromDateTime(_snapshot.ReferenceLocalTime.DateTime);

    /// <summary>Creates a passive toolbar, lunar summary and availability timeline.</summary>
    public AvailabilityPlannerControl()
    {
        var root = new Grid { Padding = new Thickness(16, 12, 16, 12), RowSpacing = 10 };
        foreach (var height in new[] { GridLength.Auto, GridLength.Auto, GridLength.Auto, new GridLength(1, GridUnitType.Star), GridLength.Auto })
            root.RowDefinitions.Add(new RowDefinition { Height = height });
        foreach (var element in new FrameworkElement[] { _cities, _date, _time, _apply, _now, _hours, _step, _exportPdf })
        { element.VerticalAlignment = VerticalAlignment.Bottom; _toolbar.Children.Add(element); }
        var moonPanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
        moonPanel.Children.Add(_moon); moonPanel.Children.Add(_moonText); _toolbar.Children.Add(moonPanel);
        root.Children.Add(new ScrollViewer
        {
            Content = _toolbar,
            HorizontalScrollMode = ScrollMode.Enabled,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollMode = ScrollMode.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled
        });
        Grid.SetRow(_days, 1); root.Children.Add(_days);
        var info = new Grid { ColumnSpacing = 12 };
        info.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        info.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        info.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        info.Children.Add(_weatherStatus); Grid.SetColumn(_weatherSetup, 1); info.Children.Add(_weatherSetup);
        Grid.SetColumn(_legend, 2); info.Children.Add(_legend); Grid.SetRow(info, 2); root.Children.Add(info);
        Grid.SetRow(_timeline, 3); root.Children.Add(_timeline);
        Grid.SetRow(_selection, 4); root.Children.Add(_selection);
        _apply.Click += (_, _) => Submit(false);
        _now.Click += (_, _) => Submit(true);
        _step.SelectionChanged += (_, _) => { if (!_rendering) Submit(IsLive); };
        _hours.SelectionChanged += (_, _) => { if (!_rendering && _snapshot is not null) _timeline.Apply(_snapshot, _strings, (int)_hours.SelectedItem, false); };
        _cities.SelectionChanged += (_, _) => { if (!_rendering) { IsLive = false; HasPendingEdits = true; } };
        _date.DateChanged += (_, _) => { if (!_rendering) { IsLive = false; HasPendingEdits = true; } };
        _time.TimeChanged += (_, _) => { if (!_rendering) { IsLive = false; HasPendingEdits = true; } };
        _timeline.ScheduleRequested += city => ScheduleRequested?.Invoke(city);
        _timeline.SelectionChanged += row => ShowSelection(row);
        _weatherSetup.Click += (_, _) => WeatherSetupRequested?.Invoke();
        _exportPdf.Click += (_, _) =>
        {
            if (_snapshot is not null)
                ExportPdfRequested?.Invoke(new(_snapshot, _timeline.VisibleStartUtc ?? _snapshot.InstantUtc, (int)_hours.SelectedItem, _strings.Language));
        };
        Content = root;
    }

    /// <summary>Gets a request preserving the user's selected reference and interval.</summary>
    public AvailabilityPlannerRequest GetRequest() => new(_cities.SelectedValue as string,
        IsLive || _date.Date is null ? null : _date.Date.Value.Date + _time.Time, (int)_step.SelectedItem);

    /// <summary>Labels the controls even when the initial application query has failed.</summary>
    public void ApplyLocalization(LocalizationService strings)
    {
        _strings = strings;
        _cities.Header = strings.Translate("WorldClock.ReferenceCity");
        _date.Header = strings.Translate("WorldClock.ReferenceDate");
        _time.Header = strings.Translate("WorldClock.ReferenceTime");
        _hours.Header = strings.Translate("Planner.ViewHours");
        _step.Header = strings.Translate("Planner.StepMinutes");
        _apply.Content = strings.Translate("WorldClock.Apply");
        _now.Content = strings.Translate("WorldClock.RestoreNow");
        _weatherSetup.Content = strings.Translate("Planner.WeatherSetup");
        _exportPdf.Content = strings.Translate("Planner.ExportPdf");
    }

    /// <summary>Applies a complete snapshot without executing application operations.</summary>
    public void Apply(AvailabilityPlannerSnapshot snapshot, LocalizationService strings, bool navigate)
    {
        _snapshot = snapshot; _strings = strings; _rendering = true;
        _exportPdf.IsEnabled = !_exportBusy;
        ApplyLocalization(strings);
        _cities.ItemsSource = snapshot.Cities.Select(c => c.Clock).ToArray();
        _cities.SelectedValue = snapshot.ReferenceCityId;
        _date.Date = new DateTimeOffset(snapshot.ReferenceLocalTime.Date, TimeSpan.Zero);
        _time.Time = snapshot.ReferenceLocalTime.TimeOfDay;
        _step.SelectedItem = snapshot.StepMinutes;
        var phase = LunarPhaseProjection.Create(snapshot.MoonPhaseAngleDegrees);
        _moon.MoonPhaseAngleDegrees = snapshot.MoonPhaseAngleDegrees;
        var nextPhase = new[] { "New", "FirstQuarter", "Full", "LastQuarter" }[snapshot.NextMoonQuarter];
        _moonText.Text = strings.Translate(phase.LocalizationKey) + "\n" + strings.Format("Planner.NextMoon",
            strings.Translate("WorldClock.MoonPhase." + nextPhase), snapshot.NextMoonPhaseUtc.ToString("d MMM HH:mm", strings.Culture) + " UTC");
        AutomationProperties.SetName(_moon, _moonText.Text);
        var status = snapshot.WeatherStatus;
        var statusKey = status.State switch
        { "available" => "Planner.WeatherConnected", "partial" => "WorldClock.WeatherStatus.Partial", "disabled" => "Planner.WeatherDisabled", "configuration-required" => "Planner.WeatherRequired", _ => "WorldClock.WeatherStatus.Unavailable" };
        _weatherStatus.Text = strings.Translate("Planner.CurrentWeather") + " · OpenWeather\n" + strings.Translate(statusKey);
        _legend.Text = string.Join("   ", new[] { "✓ " + strings.Translate("Planner.State.Available"), "◐ " + strings.Translate("Planner.State.Reserve"),
            "– " + strings.Translate("Planner.State.Unavailable"), "? " + strings.Translate("Planner.State.Unknown") });
        _days.Children.Clear();
        foreach (var offset in Enumerable.Range(-2, 5))
        {
            var date = snapshot.ReferenceLocalTime.Date.AddDays(offset);
            var button = new Button { Content = date.ToString("ddd d MMM", strings.Culture), Tag = offset };
            if (offset == 0) button.Style = (Style)Microsoft.UI.Xaml.Application.Current.Resources["AccentButtonStyle"];
            button.Click += (_, _) => { _date.Date = new DateTimeOffset(date, TimeSpan.Zero); Submit(false); };
            _days.Children.Add(button);
        }
        _rendering = false;
        _timeline.Apply(snapshot, strings, (int)_hours.SelectedItem, navigate);
        if (navigate || string.IsNullOrEmpty(_selection.Text))
        {
            var row = snapshot.Rows.FirstOrDefault(r => r.StartUtc <= snapshot.InstantUtc && r.EndUtc > snapshot.InstantUtc);
            if (row is not null) ShowSelection(row);
        }
    }

    private void Submit(bool live)
    {
        IsLive = live;
        HasPendingEdits = false;
        Requested?.Invoke(GetRequest());
    }

    private void ShowSelection(AvailabilityRow row)
    {
        if (_snapshot is null) return;
        _selection.Text = _strings.Translate("Planner.SelectedSlot") + "  " + string.Join("   ·   ",
            row.Cells.Select((cell, index) => $"{_snapshot.Cities[index].Clock.CityName} {cell.LocalStart.ToString("ddd d MMM HH:mm", _strings.Culture)}–{cell.LocalEnd:HH:mm}"));
    }
}
