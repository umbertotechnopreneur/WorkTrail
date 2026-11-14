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
    private readonly MenuFlyoutSubItem _palettes = new();
    private readonly ToggleMenuFlyoutItem[] _paletteItems = Enumerable.Range(0, 5).Select(index => new ToggleMenuFlyoutItem { Tag = index }).ToArray();
    private readonly ToggleMenuFlyoutItem _monday = new();
    private readonly ToggleMenuFlyoutItem _sunday = new();
    private readonly MenuFlyoutItem _openClocks = new();
    private readonly ToggleMenuFlyoutItem _hours16 = new();
    private readonly ToggleMenuFlyoutItem _hours24 = new() { IsChecked = true };
    private readonly ToggleMenuFlyoutItem _step15 = new();
    private readonly ToggleMenuFlyoutItem _step30 = new() { IsChecked = true };
    private readonly ToggleMenuFlyoutItem _step60 = new();
    private readonly Button _more = new() { Content = new SymbolIcon(Symbol.More), Width = 40, Height = 40, Padding = new Thickness(8), VerticalAlignment = VerticalAlignment.Center };
    private readonly Button _toggleHeaders = new() { Width = 40, Height = 40, Padding = new Thickness(8), CornerRadius = new CornerRadius(8), VerticalAlignment = VerticalAlignment.Center };
    private readonly FontIcon _headersIcon = new() { FontFamily = new FontFamily("Segoe Fluent Icons") };
    private readonly ToggleMenuFlyoutItem _emptyCellsOption = new() { Tag = "Planner.EmptyCells" };
    private readonly Button _now = new();
    private readonly TextBlock _nowLabel = new() { VerticalAlignment = VerticalAlignment.Center };
    private readonly Hyperlink _weatherSetup = new();
    private readonly MenuFlyoutItem _exportPdf = new() { IsEnabled = false };
    private readonly TextBlock _weatherStatus = new() { FontSize = 13, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _selection = new() { FontSize = 13, IsTextSelectionEnabled = true, TextWrapping = TextWrapping.Wrap };
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
    private bool _emptyCells = true;
    private bool _compactHeaders;
    private int _palette;

    /// <summary>Requests persistence of the availability color palette.</summary>
    public event Action<int>? PaletteRequested;

    /// <summary>Applies a saved palette to the timeline.</summary>
    public void SetPalette(int palette)
    {
        _palette = Math.Clamp(palette, 0, 4);
        for (var i = 0; i < _paletteItems.Length; i++) _paletteItems[i].IsChecked = i == _palette;
        _timeline.SetPalette(_palette);
    }

    /// <summary>Requests persistence of the date calendar's first weekday.</summary>
    public event Action<DayOfWeek>? FirstDayOfWeekRequested;

    /// <summary>Requests persistence of the visible duration and time step.</summary>
    public event Action<int, int>? ViewPreferencesRequested;

    /// <summary>Requests saving the timeline's cell and header display options.</summary>
    public event Action<bool, bool>? TableDisplayRequested;

    /// <summary>Renders the saved cell and header options.</summary>
    public void SetTableDisplay(bool emptyCells, bool compactHeaders)
    {
        _emptyCells = emptyCells;
        _compactHeaders = compactHeaders;
        _emptyCellsOption.IsChecked = emptyCells;
        UpdateHeaderToggle();
        _timeline.SetDisplayOptions(emptyCells, compactHeaders);
    }

    /// <summary>Prevents overlapping changes while a display preference is being saved.</summary>
    public void SetTableDisplayBusy(bool busy)
    {
        _emptyCellsOption.IsEnabled = !busy;
        _toggleHeaders.IsEnabled = !busy;
    }

    /// <summary>Requests saving a completed equal-column resize or automatic fitting.</summary>
    public event Action<double, double>? ColumnWidthRequested;

    /// <summary>Applies a committed width preference without starting another save.</summary>
    public void SetColumnWidthPreferences(double homeWidth, double width) => _timeline.SetColumnWidthPreferences(homeWidth, width);

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
        if (_more.Flyout is MenuFlyout previous)
        {
            previous.Items.OfType<MenuFlyoutSubItem>().FirstOrDefault(item => Equals(item.Tag, "Window.Options"))
                ?.Items.Remove(_emptyCellsOption);
            previous.Items.Clear();
        }
        menu.Items.OfType<MenuFlyoutSubItem>().FirstOrDefault(item => Equals(item.Tag, "Window.Options"))
            ?.Items.Add(_emptyCellsOption);
        MenuFlyoutItemBase[] commands = [_hours, _step, _weekStart, _palettes, new MenuFlyoutSeparator(), _exportPdf, _openClocks, new MenuFlyoutSeparator()];
        for (var index = commands.Length - 1; index >= 0; index--) menu.Items.Insert(0, commands[index]);
        _more.Flyout = menu;
    }

    /// <summary>Publishes input when the user edits the reference fields.</summary>
    public event Action<string?, DateTime?, TimeSpan>? ReferenceEdited;
    /// <summary>Requests a return to the current instant.</summary>
    public event Action? CurrentTimeRequested;
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
        var root = new Grid { RowSpacing = 6 };
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
        _toggleHeaders.Content = _headersIcon;
        var headerActions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        headerActions.Children.Add(_toggleHeaders);
        headerActions.Children.Add(_more);
        Grid.SetColumn(headerActions, 1);
        commands.Children.Add(headerActions);
        var days = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        days.Children.Add(_previousDay); days.Children.Add(_dayLabel); days.Children.Add(_nextDay);
        var lowerBar = new Grid { ColumnSpacing = 8 };
        lowerBar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        lowerBar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        lowerBar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetColumn(days, 1); lowerBar.Children.Add(days);
        Grid.SetColumn(_legend, 2); lowerBar.Children.Add(_legend);
        Grid.SetRow(lowerBar, 1); Grid.SetColumnSpan(lowerBar, 2); commands.Children.Add(lowerBar);
        _legend.VerticalAlignment = VerticalAlignment.Center;
        _legend.HorizontalAlignment = HorizontalAlignment.Right;
        _legend.TextAlignment = TextAlignment.Right;

        _hours.Items.Add(_hours16); _hours.Items.Add(_hours24);
        _step.Items.Add(_step15); _step.Items.Add(_step30); _step.Items.Add(_step60);
        _weekStart.Items.Add(_monday); _weekStart.Items.Add(_sunday);
        foreach (var item in _paletteItems)
        {
            _palettes.Items.Add(item);
            item.Click += (_, _) => { SetPalette(_palette); PaletteRequested?.Invoke((int)item.Tag); };
        }
        _hours.Icon = new SymbolIcon { Symbol = Symbol.Clock };
        _step.Icon = new SymbolIcon { Symbol = Symbol.Sort };
        _weekStart.Icon = new SymbolIcon { Symbol = Symbol.Calendar };
        _palettes.Icon = new FontIcon { FontFamily = new FontFamily("Segoe Fluent Icons"), Glyph = "" };
        _exportPdf.Icon = new SymbolIcon { Symbol = Symbol.Save };
        _openClocks.Icon = new SymbolIcon { Symbol = Symbol.World };
        var menu = new MenuFlyout();
        menu.Items.Add(_hours); menu.Items.Add(_step); menu.Items.Add(_weekStart); menu.Items.Add(_palettes);
        menu.Items.Add(new MenuFlyoutSeparator()); menu.Items.Add(_exportPdf);
        menu.Items.Add(_openClocks);
        _more.Flyout = menu;
        var toolbarSurface = new Border
        {
            Child = commands,
            Padding = new Thickness(8),
            CornerRadius = new CornerRadius(12),
            Background = (Brush)Microsoft.UI.Xaml.Application.Current.Resources["CardBackgroundFillColorDefaultBrush"]
        };
        var shadow = new ThemeShadow();
        shadow.Receivers.Add(root);
        toolbarSurface.Shadow = shadow;
        Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.SetIsTranslationEnabled(toolbarSurface, true);
        toolbarSurface.Translation = new System.Numerics.Vector3(0, 0, 12);
        root.Children.Add(toolbarSurface);
        Grid.SetRow(_weatherStatus, 2); root.Children.Add(_weatherStatus);
        Grid.SetRow(_timeline, 3); root.Children.Add(_timeline);
        Grid.SetRow(_selection, 4); root.Children.Add(_selection);
        _now.Click += (_, _) =>
        {
            _timeline.ScrollToCurrentSlot();
            CurrentTimeRequested?.Invoke();
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
        _toggleHeaders.Click += (_, _) => TableDisplayRequested?.Invoke(_emptyCells, !_compactHeaders);
        _emptyCellsOption.Click += (_, _) =>
        {
            var requested = _emptyCellsOption.IsChecked;
            _emptyCellsOption.IsChecked = _emptyCells;
            TableDisplayRequested?.Invoke(requested, _compactHeaders);
        };
        _cities.SelectionChanged += (_, _) => ReferenceChanged();
        _date.DateChanged += (_, _) => ReferenceChanged();
        _time.TimeChanged += (_, _) => ReferenceChanged();
        _timeline.ScheduleRequested += city => ScheduleRequested?.Invoke(city);
        _timeline.ColumnWidthRequested += (homeWidth, width) => ColumnWidthRequested?.Invoke(homeWidth, width);
        _timeline.SelectionChanged += row => ShowSelection(row);
        _weatherSetup.Click += (_, _) => WeatherSetupRequested?.Invoke();
        _exportPdf.Click += (_, _) =>
        {
            if (_snapshot is not null)
                ExportPdfRequested?.Invoke(new(_snapshot, _timeline.VisibleStartUtc ?? _snapshot.InstantUtc,
                    _visibleHours, _strings.Language, _emptyCells, _compactHeaders, _timeline.SelectedStartUtc, _palette));
        };
        Content = root;
    }

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
        _palettes.Text = strings.Translate("Planner.ColorPalette");
        for (var i = 0; i < _paletteItems.Length; i++)
        {
            _paletteItems[i].Text = strings.Translate("Planner.Palette." + new[] { "Garden", "Dawn", "Forest", "Sky", "Linen" }[i]);
            var state = i == 1 ? AvailabilityState.Reserve : i == 4 ? AvailabilityState.Unavailable : AvailabilityState.Available;
            var (red, green, blue) = AvailabilityPalette.GetFill(state, i);
            _paletteItems[i].Icon = new FontIcon
            {
                Glyph = "",
                FontFamily = new FontFamily("Segoe Fluent Icons"),
                Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, (byte)(red * 0.60), (byte)(green * 0.60), (byte)(blue * 0.60)))
            };
        }
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
        _emptyCellsOption.Text = _strings.Translate("Planner.EmptyCells");
        Label(_emptyCellsOption, "Planner.EmptyCells", "Planner.EmptyCells");
        UpdateHeaderToggle();
        Label(_now, "Planner.Now", "Planner.GoToNow");
        Label(_previousDay, "Planner.PreviousDay", "WorldClock.PreviousDay");
        Label(_nextDay, "Planner.NextDay", "WorldClock.NextDay");
        Label(_weatherSetup, "Planner.WeatherSettings", "Planner.WeatherSetup");
        Label(_exportPdf, "Planner.ExportPdf", "Planner.ExportPdf");
    }

    /// <summary>Applies a complete snapshot without executing application operations.</summary>
    public void Apply(AvailabilityPlannerSnapshot snapshot, LocalizationService strings, bool navigate, bool updateReference = true)
    {
        _snapshot = snapshot; _strings = strings; _rendering = true;
        _exportPdf.IsEnabled = !_exportBusy;
        ApplyLocalization(strings);
        if (updateReference)
        {
            _cities.ItemsSource = snapshot.Cities.Select(c => c.Clock).ToArray();
            _cities.SelectedValue = snapshot.ReferenceCityId;
            _date.Date = new DateTimeOffset(snapshot.ReferenceLocalTime.Date, TimeSpan.Zero);
            _time.Time = snapshot.ReferenceLocalTime.TimeOfDay;
        }
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
        _rendering = false;
        _timeline.Apply(snapshot, strings, _visibleHours, navigate);
        var selected = navigate ? snapshot.InstantUtc : _timeline.SelectedStartUtc ?? snapshot.InstantUtc;
        var row = snapshot.Rows.FirstOrDefault(r => r.StartUtc <= selected && r.EndUtc > selected);
        if (row is not null) ShowSelection(row);
        else _selection.Text = "";
    }

    private void ReferenceChanged()
    {
        // Only user edits publish input. Requests and refreshes use the session model.
        if (_rendering) return;
        ReferenceEdited?.Invoke(_cities.SelectedValue as string, _date.Date?.Date, _time.Time);
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

    private void UpdateHeaderToggle()
    {
        _headersIcon.Glyph = _compactHeaders ? "\uE70D" : "\uE70E";
        Label(_toggleHeaders, "Planner.ToggleHeaders",
            _compactHeaders ? "Planner.ExpandHeaders" : "Planner.CollapseHeaders");
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
        var snapshot = _snapshot;
        if (snapshot is null || row.Cells.Count == 0 || snapshot.Cities.Count == 0) return;
        var count = Math.Min(row.Cells.Count, snapshot.Cities.Count);
        string FormatCell(int index)
        {
            var cell = row.Cells[index];
            return $"{snapshot.Cities[index].Clock.CityName}  {cell.LocalStart.ToString("ddd d MMM HH:mm", _strings.Culture)}–{cell.LocalEnd:HH:mm}";
        }
        var groups = snapshot.DisplayGroups ?? Enumerable.Range(0, count).Select(index => new AvailabilityPlannerGroup(index, [index])).ToArray();
        var details = groups.Select(group =>
        {
            var index = group.PrimaryIndex;
            var names = string.Join(", ", group.CityIndices.Select(cityIndex => snapshot.Cities[cityIndex].Clock.CityName));
            var cell = row.Cells[index];
            return $"{names}  {cell.LocalStart.ToString("ddd d MMM HH:mm", _strings.Culture)}–{cell.LocalEnd:HH:mm}";
        });
        _selection.Text = _strings.Translate("Planner.SelectedSlot") + "  ·  " + string.Join("   |   ", details);
        ToolTipService.SetToolTip(_selection, string.Join("\n", Enumerable.Range(0, count).Select(FormatCell)));
    }
}
