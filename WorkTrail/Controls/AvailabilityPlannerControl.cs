// SPDX-License-Identifier: MIT
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using WorkTrail.Application;
using WorkTrail.Presentation;
using WorkTrail.Services;

namespace WorkTrail.Controls;

/// <summary>Composes the reusable planner surface; application operations remain owned by its host.</summary>
public sealed class AvailabilityPlannerControl : UserControl
{
    private readonly ResponsiveWrapPanel _toolbar = new();
    private readonly TextBlock _dayLabel = new() { FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap, MaxWidth = 250 };
    private readonly Button _previousDay = new() { Content = new SymbolIcon(Symbol.Back), Width = 36, Height = 36, Padding = new Thickness(6) };
    private readonly Button _nextDay = new() { Content = new SymbolIcon(Symbol.Forward), Width = 36, Height = 36, Padding = new Thickness(6) };
    private readonly ComboBox _cities = new() { Width = 190, DisplayMemberPath = "CityName", SelectedValuePath = "CityId" };
    private readonly CalendarDatePicker _date = new() { Width = 170, MinWidth = 0, MinDate = new DateTimeOffset(1901, 1, 3, 0, 0, 0, TimeSpan.Zero), MaxDate = new DateTimeOffset(2098, 12, 28, 0, 0, 0, TimeSpan.Zero) };
    private readonly TimePicker _time = new() { ClockIdentifier = "24HourClock", MinuteIncrement = 1, Width = 242, MinWidth = 0 };
    private readonly MenuFlyoutSubItem _step = new();
    private readonly MenuFlyoutSubItem _hours = new();
    private readonly MenuFlyoutSubItem _weekStart = new();
    private readonly ToggleMenuFlyoutItem _monday = new();
    private readonly ToggleMenuFlyoutItem _sunday = new();
    private readonly MenuFlyoutItem _openClocks = new();
    private readonly ToggleMenuFlyoutItem _hours16 = new();
    private readonly ToggleMenuFlyoutItem _hours24 = new() { IsChecked = true };
    private readonly ToggleMenuFlyoutItem _step15 = new();
    private readonly ToggleMenuFlyoutItem _step30 = new() { IsChecked = true };
    private readonly ToggleMenuFlyoutItem _step60 = new();
    private readonly Button _more = new() { Content = new SymbolIcon(Symbol.More), Width = 36, Height = 36, Padding = new Thickness(6), VerticalAlignment = VerticalAlignment.Top };
    private readonly Button _now = new();
    private readonly TextBlock _nowLabel = new() { VerticalAlignment = VerticalAlignment.Center };
    private readonly Hyperlink _weatherSetup = new();
    private readonly MenuFlyoutItem _exportPdf = new() { IsEnabled = false };
    private readonly TextBlock _weatherStatus = new() { FontSize = 13, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _selection = new() { FontSize = 15, IsTextSelectionEnabled = true, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _legend = new() { FontSize = 13, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _moonText = new() { FontSize = 14, TextWrapping = TextWrapping.Wrap, MaxWidth = 230 };
    private readonly CelestialPhaseControl _moon = new() { Width = 52, Height = 52, IsDaylight = false };
    private readonly AvailabilityTimelineGrid _timeline = new();
    private LocalizationService _strings = new("system");
    private AvailabilityPlannerSnapshot? _snapshot;
    private bool _rendering;
    private bool _exportBusy;
    private int _visibleHours = 24;
    private int _stepMinutes = 30;
    private DayOfWeek _firstDay = DayOfWeek.Monday;

    /// <summary>Requests persistence of the date calendar's first weekday.</summary>
    public event Action<DayOfWeek>? FirstDayOfWeekRequested;

    /// <summary>Requests persistence of the visible duration and time step.</summary>
    public event Action<int, int>? ViewPreferencesRequested;

    /// <summary>Requests saving a completed equal-column resize or automatic fitting.</summary>
    public event Action<double>? ColumnWidthRequested;

    /// <summary>Applies a committed width preference without starting another save.</summary>
    public void SetColumnWidthPreference(double width) => _timeline.SetColumnWidthPreference(width);

    /// <summary>Requests World Clock's city configuration surface.</summary>
    public event Action? OpenClocksRequested;

    /// <summary>Applies the saved week preference without starting another settings mutation.</summary>
    public void SetFirstDayOfWeek(DayOfWeek day)
    {
        _firstDay = day;
        _date.FirstDayOfWeek = day == DayOfWeek.Sunday ? Windows.Globalization.DayOfWeek.Sunday : Windows.Globalization.DayOfWeek.Monday;
        _monday.IsChecked = day == DayOfWeek.Monday;
        _sunday.IsChecked = day == DayOfWeek.Sunday;
    }

    /// <summary>Applies committed view preferences without requesting another save.</summary>
    public void SetViewPreferences(int visibleHours, int stepMinutes)
    {
        _visibleHours = visibleHours;
        _stepMinutes = stepMinutes;
        UpdateMenuSelection();
        if (_snapshot is not null) _timeline.Apply(_snapshot, _strings, _visibleHours, false);
    }

    /// <summary>Prevents another density change from using preferences whose save is still pending.</summary>
    public void SetViewPreferencesBusy(bool busy)
    {
        _hours.IsEnabled = !busy;
        _step.IsEnabled = !busy;
    }

    /// <summary>Combines view commands with the shared navigation and window options.</summary>
    public void SetWindowMenu(MenuFlyout menu)
    {
        ArgumentNullException.ThrowIfNull(menu);
        if (ReferenceEquals(_more.Flyout, menu)) return;
        if (_more.Flyout is MenuFlyout previous) previous.Items.Clear();
        MenuFlyoutItemBase[] commands = [_hours, _step, _weekStart, new MenuFlyoutSeparator(), _exportPdf, _openClocks, new MenuFlyoutSeparator()];
        for (var index = commands.Length - 1; index >= 0; index--) menu.Items.Insert(0, commands[index]);
        _more.Flyout = menu;
    }

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
        var nowContent = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        nowContent.Children.Add(new SymbolIcon(Symbol.Clock));
        nowContent.Children.Add(_nowLabel);
        _now.Content = nowContent;
        _now.Style = (Style)Microsoft.UI.Xaml.Application.Current.Resources["AccentButtonStyle"];
        foreach (var element in new FrameworkElement[] { _cities, _date, _time, _now })
        { element.VerticalAlignment = VerticalAlignment.Bottom; _toolbar.Children.Add(element); }
        var moonPanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center, MaxWidth = 270 };
        _moonText.MaxWidth = 200;
        moonPanel.Children.Add(_moon); moonPanel.Children.Add(_moonText); _toolbar.Children.Add(moonPanel);
        var commands = new Grid { ColumnSpacing = 12, RowSpacing = 10 };
        commands.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        commands.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        commands.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        commands.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        commands.Children.Add(_toolbar);
        Grid.SetColumn(_more, 1); commands.Children.Add(_more);
        _legend.HorizontalAlignment = HorizontalAlignment.Right;
        _legend.TextAlignment = TextAlignment.Right;
        Grid.SetRow(_legend, 1); Grid.SetColumnSpan(_legend, 2); commands.Children.Add(_legend);
        _hours.Items.Add(_hours16); _hours.Items.Add(_hours24);
        _step.Items.Add(_step15); _step.Items.Add(_step30); _step.Items.Add(_step60);
        _weekStart.Items.Add(_monday); _weekStart.Items.Add(_sunday);
        _hours.Icon = new SymbolIcon { Symbol = Symbol.Clock };
        _step.Icon = new SymbolIcon { Symbol = Symbol.Sort };
        _weekStart.Icon = new SymbolIcon { Symbol = Symbol.Calendar };
        _exportPdf.Icon = new SymbolIcon { Symbol = Symbol.Save };
        _openClocks.Icon = new SymbolIcon { Symbol = Symbol.World };
        var menu = new MenuFlyout();
        menu.Items.Add(_hours); menu.Items.Add(_step); menu.Items.Add(_weekStart);
        menu.Items.Add(new MenuFlyoutSeparator()); menu.Items.Add(_exportPdf);
        menu.Items.Add(_openClocks);
        _more.Flyout = menu;
        root.Children.Add(new Border
        {
            Child = commands,
            Padding = new Thickness(12),
            CornerRadius = new CornerRadius(12),
            Background = (Brush)Microsoft.UI.Xaml.Application.Current.Resources["CardBackgroundFillColorDefaultBrush"]
        });
        var days = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, HorizontalAlignment = HorizontalAlignment.Center };
        days.Children.Add(_previousDay); days.Children.Add(_dayLabel); days.Children.Add(_nextDay);
        Grid.SetRow(days, 1); root.Children.Add(days);
        Grid.SetRow(_weatherStatus, 2); root.Children.Add(_weatherStatus);
        Grid.SetRow(_timeline, 3); root.Children.Add(_timeline);
        Grid.SetRow(_selection, 4); root.Children.Add(_selection);
        _now.Click += (_, _) =>
        {
            _timeline.ScrollToCurrentSlot();
            Submit(true);
        };
        _previousDay.Click += (_, _) => MoveDay(-1);
        _nextDay.Click += (_, _) => MoveDay(1);
        _hours16.Click += (_, _) => SetVisibleHours(16);
        _hours24.Click += (_, _) => SetVisibleHours(24);
        _step15.Click += (_, _) => SetStep(15);
        _step30.Click += (_, _) => SetStep(30);
        _step60.Click += (_, _) => SetStep(60);
        _monday.Click += (_, _) => { SetFirstDayOfWeek(_firstDay); FirstDayOfWeekRequested?.Invoke(DayOfWeek.Monday); };
        _sunday.Click += (_, _) => { SetFirstDayOfWeek(_firstDay); FirstDayOfWeekRequested?.Invoke(DayOfWeek.Sunday); };
        _openClocks.Click += (_, _) => OpenClocksRequested?.Invoke();
        _cities.SelectionChanged += (_, _) => ReferenceChanged();
        _date.DateChanged += (_, _) => ReferenceChanged();
        _time.TimeChanged += (_, _) => ReferenceChanged();
        _timeline.ScheduleRequested += city => ScheduleRequested?.Invoke(city);
        _timeline.ColumnWidthRequested += width => ColumnWidthRequested?.Invoke(width);
        _timeline.SelectionChanged += row => ShowSelection(row);
        _weatherSetup.Click += (_, _) => WeatherSetupRequested?.Invoke();
        _exportPdf.Click += (_, _) =>
        {
            if (_snapshot is not null)
                ExportPdfRequested?.Invoke(new(_snapshot, _timeline.VisibleStartUtc ?? _snapshot.InstantUtc, _visibleHours, _strings.Language));
        };
        Content = root;
    }

    /// <summary>Gets a request preserving the user's selected reference and interval.</summary>
    public AvailabilityPlannerRequest GetRequest() => new(_cities.SelectedValue as string,
        IsLive || _date.Date is null ? null : _date.Date.Value.Date + _time.Time, _stepMinutes);

    /// <summary>Labels the controls even when the initial application query has failed.</summary>
    public void ApplyLocalization(LocalizationService strings)
    {
        _strings = strings;
        _date.Language = strings.Language;
        _date.DateFormat = "{day.integer} {month.abbreviated} {year.full}";
        _time.Language = strings.Language;
        _cities.Header = strings.Translate("WorldClock.ReferenceCity");
        _date.Header = strings.Translate("WorldClock.ReferenceDate");
        _time.Header = strings.Translate("WorldClock.ReferenceTime");
        _hours.Text = strings.Translate("Planner.ViewHours");
        _step.Text = strings.Translate("Planner.StepMinutes");
        _weekStart.Text = strings.Translate("Planner.FirstDayOfWeek");
        _monday.Text = strings.Translate("Planner.Monday");
        _sunday.Text = strings.Translate("Planner.Sunday");
        _openClocks.Text = strings.Translate("Planner.OpenClocks");
        _hours16.Text = strings.Translate("Planner.Hours16");
        _hours24.Text = strings.Translate("Planner.Hours24");
        _step15.Text = strings.Translate("Planner.Minutes15");
        _step30.Text = strings.Translate("Planner.Minutes30");
        _step60.Text = strings.Translate("Planner.Minutes60");
        _exportPdf.Text = strings.Translate("Planner.ExportPdf");
        _nowLabel.Text = strings.Translate("Planner.NowLabel");
        _weatherSetup.Inlines.Clear();
        _weatherSetup.Inlines.Add(new Run { Text = strings.Translate("Planner.ConfigureWeather") });
        Label(_cities, "Planner.City", "WorldClock.ReferenceCity");
        Label(_date, "Planner.Date", "WorldClock.ReferenceDate");
        Label(_time, "Planner.Time", "WorldClock.ReferenceTime");
        Label(_hours, "Planner.Hours", "Planner.ViewHours");
        Label(_step, "Planner.Step", "Planner.StepMinutes");
        Label(_weekStart, "Planner.WeekStart", "Planner.FirstDayOfWeek");
        Label(_monday, "Planner.Monday", "Planner.Monday");
        Label(_sunday, "Planner.Sunday", "Planner.Sunday");
        Label(_openClocks, "Planner.OpenClocks", "Planner.OpenClocks");
        Label(_hours16, "Planner.Hours16", "Planner.Hours16");
        Label(_hours24, "Planner.Hours24", "Planner.Hours24");
        Label(_step15, "Planner.Minutes15", "Planner.Minutes15");
        Label(_step30, "Planner.Minutes30", "Planner.Minutes30");
        Label(_step60, "Planner.Minutes60", "Planner.Minutes60");
        Label(_more, "Planner.Options", "Planner.Options");
        Label(_now, "Planner.Now", "Planner.GoToNow");
        Label(_previousDay, "Planner.PreviousDay", "WorldClock.PreviousDay");
        Label(_nextDay, "Planner.NextDay", "WorldClock.NextDay");
        Label(_weatherSetup, "Planner.WeatherSettings", "Planner.WeatherSetup");
        Label(_exportPdf, "Planner.ExportPdf", "Planner.ExportPdf");
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
        UpdateMenuSelection();
        var phase = LunarPhaseProjection.Create(snapshot.MoonPhaseAngleDegrees);
        _moon.MoonPhaseAngleDegrees = snapshot.MoonPhaseAngleDegrees;
        var nextPhase = new[] { "New", "FirstQuarter", "Full", "LastQuarter" }[snapshot.NextMoonQuarter];
        _moonText.Text = strings.Translate(phase.LocalizationKey) + "\n" + strings.Format("Planner.NextMoon",
            strings.Translate("WorldClock.MoonPhase." + nextPhase), snapshot.NextMoonPhaseUtc.ToString("d MMM HH:mm", strings.Culture) + " UTC");
        AutomationProperties.SetName(_moon, _moonText.Text);
        var status = snapshot.WeatherStatus;
        var statusKey = status.State switch
        { "available" => "Planner.WeatherConnected", "partial" => "WorldClock.WeatherStatus.Partial", "disabled" => "Planner.WeatherDisabled", "configuration-required" => "Planner.WeatherRequired", _ => "WorldClock.WeatherStatus.Unavailable" };
        _weatherStatus.Inlines.Clear();
        _weatherStatus.Inlines.Add(new Run
        {
            Text = strings.Translate("Planner.CurrentWeather") + " · OpenWeather · "
            + (status.IsProviderConfigured && status.State == "unavailable" ? strings.Translate("WeatherSetup.ConfiguredUnavailable") : strings.Translate(statusKey)) + "  "
        });
        _weatherStatus.Inlines.Add(_weatherSetup);
        _legend.Inlines.Clear();
        _legend.Inlines.Add(new Run
        {
            Text = string.Join("   ", new[] { "✓ " + strings.Translate("Planner.State.Available"), "◐ " + strings.Translate("Planner.State.Reserve"),
                "× " + strings.Translate("Planner.State.Unavailable"), "ℹ " + strings.Translate("Planner.State.Unknown") })
        });
        _legend.Inlines.Add(new Run
        {
            Text = "   ● " + strings.Translate("Planner.NowLabel"),
            Foreground = (Brush)Microsoft.UI.Xaml.Application.Current.Resources["SystemFillColorCriticalBrush"],
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
        });
        _dayLabel.Text = snapshot.ReferenceLocalTime.ToString("dddd d MMMM yyyy", strings.Culture);
        _dayLabel.Foreground = (Brush)Microsoft.UI.Xaml.Application.Current.Resources[snapshot.Cities.First(c => c.Clock.CityId == snapshot.ReferenceCityId).IsNonWorkingDay
            ? "SystemFillColorCriticalBrush" : "TextFillColorPrimaryBrush"];
        _previousDay.IsEnabled = _date.Date > _date.MinDate;
        _nextDay.IsEnabled = _date.Date < _date.MaxDate;
        HasPendingEdits = false;
        _rendering = false;
        _timeline.Apply(snapshot, strings, _visibleHours, navigate);
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

    private void ReferenceChanged()
    {
        // Snapshot rendering must never trigger another request. Incomplete date/time edits stay local.
        if (_rendering) return;
        IsLive = false;
        HasPendingEdits = true;
        if (_cities.SelectedValue is string && _date.Date is not null && _time.Time >= TimeSpan.Zero)
            Submit(false);
    }

    private void SetVisibleHours(int hours)
    {
        UpdateMenuSelection();
        ViewPreferencesRequested?.Invoke(hours, _stepMinutes);
    }

    private void SetStep(int minutes)
    {
        UpdateMenuSelection();
        ViewPreferencesRequested?.Invoke(_visibleHours, minutes);
    }

    private void UpdateMenuSelection()
    {
        _hours16.IsChecked = _visibleHours == 16;
        _hours24.IsChecked = _visibleHours == 24;
        _step15.IsChecked = _stepMinutes == 15;
        _step30.IsChecked = _stepMinutes == 30;
        _step60.IsChecked = _stepMinutes == 60;
    }

    private void Label(DependencyObject control, string identifier, string key)
    {
        AutomationProperties.SetAutomationId(control, identifier);
        UiLocalization.SetAccessibleLabel(control, _strings.Translate(key));
    }

    private void MoveDay(int offset)
    {
        if (_date.Date is not { } current) return;
        var next = current.AddDays(offset);
        if (next < _date.MinDate || next > _date.MaxDate) return;
        _date.Date = next;
    }

    private void ShowSelection(AvailabilityRow row)
    {
        if (_snapshot is null) return;
        _selection.Text = _strings.Translate("Planner.SelectedSlot") + "  " + string.Join("   ·   ",
            row.Cells.Select((cell, index) => $"{_snapshot.Cities[index].Clock.CityName} {cell.LocalStart.ToString("ddd d MMM HH:mm", _strings.Culture)}–{cell.LocalEnd:HH:mm}"));
    }
}
