// SPDX-License-Identifier: MIT
/* VBWR B
 *
 * Project: WorkTrail
 * Repository: https://github.com/umbertotechnopreneur/WorkTrail
 * Creator: Umberto Giacobbi | https://umbertogiacobbi.biz
 *
 * VibeWare: Human intent, AI execution, and plenty of tokens
 * Manifesto: https://umbertogiacobbi.biz/vibeware/manifesto
 *
 * Modified with AI: OpenAI Codex; added this header on 2026-10-10.
 * Human guidance: Umberto Giacobbi; requested VibeWare branding.
 *
 * Copyright (c) 2026 Umberto Giacobbi
 * License: MIT - see LICENSE
 *
 * VBWR E */


using System.Globalization;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using WorkTrail.Application;
using WorkTrail.Controls;
using WorkTrail.Presentation;
using WorkTrail.Services;
using Windows.System;

namespace WorkTrail;

/// <summary>Identifies the follow-up experience selected from the activity calendar.</summary>
internal enum ActivityCalendarAction
{
    OpenScreenshots,
    ReprocessDescriptions
}

/// <summary>Returns the selected day and requested follow-up experience.</summary>
internal sealed record ActivityCalendarDialogResult(DateOnly Date, ActivityCalendarAction Action);

/// <summary>Contains passive presentation values for one installation in the selected-day legend.</summary>
internal sealed record ActivityCalendarInstallationLegendItem(
    string FriendlyName,
    string MachineName,
    string IconGlyph,
    SolidColorBrush AccentBrush,
    string AccessibleName);

/// <summary>Shows a native rolling activity calendar backed only by aggregate application-layer report data.</summary>
internal sealed partial class ActivityCalendarDialogWindow : Window
{
    private const int ExpectedReportContractVersion = 6;
    private const int LogicalWidth = 1080;
    private const int LogicalHeight = 820;
    private const int LogicalScreenMargin = 24;
    private readonly TaskCompletionSource<ActivityCalendarDialogResult?> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly CancellationTokenSource _lifetimeCancellation = new();
    private readonly IWorkTrailApplication _application;
    private readonly LocalizationService _strings;
    private readonly CultureInfo _culture;
    private readonly AppWindow _appWindow;
    private readonly CustomTitleBarController _titleBar;
    private readonly WindowPlacementService _placement;
    private readonly IntPtr _windowHandle;
    private readonly TextBlock _dayNumberMeasure = new() { Text = "88" };
    private IReadOnlyDictionary<DateOnly, ReportCalendarCell> _recordedDays = new Dictionary<DateOnly, ReportCalendarCell>();
    private IReadOnlySet<DateOnly> _vipDates = new HashSet<DateOnly>();
    private DateOnly _selectedDate = DateOnly.FromDateTime(DateTime.Today);
    private ActivityCalendarDialogResult? _result;
    private bool _isCompleting;
    private bool _isLoaded;
    private bool _calendarReady;
    private DateOnly _firstDate;
    private DateOnly _lastDate;
    private DateOnly _weekStart;
    private int? _selectedHour;
    private CancellationTokenSource? _weekCancellation;
    private IReadOnlyList<ActivityWeekCell>? _weekCells;

    /// <summary>Creates a passive calendar dialog that obtains daily aggregates through the application facade.</summary>
    internal ActivityCalendarDialogWindow(
        IWorkTrailApplication application,
        ElementTheme theme,
        LocalizationService strings,
        AppWindow ownerAppWindow,
        IntPtr ownerHandle)
    {
        _application = application ?? throw new ArgumentNullException(nameof(application));
        _strings = strings ?? throw new ArgumentNullException(nameof(strings));
        ArgumentNullException.ThrowIfNull(ownerAppWindow);
        _culture = _strings.Culture;
        InitializeComponent();
        Title = T("ActivityCalendar.Title");
        RootGrid.RequestedTheme = theme;
        RootGrid.Language = _strings.Language;
        ActivityCalendarView.Language = _strings.Language;
        _windowHandle = WinRT.Interop.WindowNative.GetWindowHandle(this);
        _appWindow = AppWindow.GetFromWindowId(Win32Interop.GetWindowIdFromWindow(_windowHandle));
        _titleBar = new CustomTitleBarController(
            this,
            _appWindow,
            RootGrid,
            TitleDragRegion,
            TitleBarLeftInsetColumn,
            TitleBarRightInsetColumn,
            () => new FrameworkElement[] { TitleBarCloseButton },
            useTallTitleBar: false);
        _placement = new WindowPlacementService(
            application,
            this,
            _appWindow,
            WindowStateKeys.ActivityCalendar,
            LogicalWidth,
            LogicalHeight,
            LogicalScreenMargin,
            ownerAppWindow.Id);
        WindowInteropService.SetOwner(_windowHandle, ownerHandle);
        if (_appWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsResizable = true;
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
            presenter.SetBorderAndTitleBar(hasBorder: true, hasTitleBar: false);
        }

        ApplyLocalizedContent();
        WeekHeatmap.CellSelected += UpdateSelectedHour;
        WeekHeatmap.CellInvoked += async cell => await OpenScreenshotsAsync(cell.Date);
        Closed += ActivityCalendarDialogWindow_Closed;
    }

    /// <summary>Activates the queued acrylic surface and completes after closure.</summary>
    internal Task<ActivityCalendarDialogResult?> ShowAsync()
    {
        WindowInteropService.MakeTopmostWithoutActivation(_windowHandle);
        Activate();
        return _completion.Task;
    }

    internal IntPtr WindowHandle => _windowHandle;

    internal void DisposePlacement()
    {
        _placement.Dispose();
        _lifetimeCancellation.Dispose();
    }

    private async void RootGrid_Loaded(object sender, RoutedEventArgs e)
    {
        if (_isLoaded)
        {
            return;
        }

        _isLoaded = true;
        _placement.ApplyDefaultBounds(RootGrid);
        await _placement.RestoreOrCenterAsync(RootGrid, CancellationToken.None);
        CloseButton.Focus(FocusState.Programmatic);
        await LoadCalendarAsync();
    }

    private async Task LoadCalendarAsync()
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        var from = today.AddDays(-365);
        StatusText.Text = T("ActivityCalendar.Loading");

        try
        {
            var result = await _application.GetReportAsync(
                new ReportQuery(from, today, string.Empty, ReportView.Calendar),
                _lifetimeCancellation.Token);
            if (!result.Succeeded || result.Value is null)
            {
                ShowError(string.Format(_culture, T("ActivityCalendar.Error"), result.Code));
                return;
            }

            if (!TryApplySnapshot(result.Value))
            {
                ShowError(T("ActivityCalendar.InvalidData"));
                return;
            }

            var vip = await _application.GetVipScreenshotDatesAsync(new(from, today), _lifetimeCancellation.Token);
            if (!vip.Succeeded || vip.Value is null)
            {
                ShowError(T("ActivityCalendar.Unavailable"));
                return;
            }
            _vipDates = vip.Value.ToHashSet();
            ShowCalendar(from, today);
        }
        catch (OperationCanceledException) when (_lifetimeCancellation.IsCancellationRequested)
        {
            // Closing the dialog cancels the aggregate report request; no stale UI state is applied.
        }
        catch (Exception)
        {
            ShowError(T("ActivityCalendar.Unavailable"));
        }
    }

    private bool TryApplySnapshot(ReportSnapshot snapshot)
    {
        if (snapshot.ContractVersion != ExpectedReportContractVersion)
        {
            return false;
        }

        var recordedDays = new Dictionary<DateOnly, ReportCalendarCell>();
        var reportDates = new HashSet<DateOnly>();
        foreach (var cell in snapshot.Calendar)
        {
            if (!reportDates.Add(cell.Date) || !IsValidCell(cell))
            {
                return false;
            }

            if (cell.HasData && !recordedDays.TryAdd(cell.Date, cell))
            {
                return false;
            }
        }

        _recordedDays = recordedDays;
        return true;
    }

    private static bool IsValidCell(ReportCalendarCell cell) =>
        cell.ActiveSeconds >= 0 &&
        cell.IdleSeconds >= 0 &&
        cell.TrackedSeconds >= 0 &&
        cell.KeyPresses >= 0 &&
        cell.MouseClicks >= 0 &&
        cell.SampleCount >= 0 &&
        cell.Installations is { } installations &&
        installations.All(IsValidInstallationProfile) &&
        installations.Select(profile => profile.InstallationId).Distinct(StringComparer.Ordinal).Count() == installations.Count &&
        (cell.HasData
            ? cell.SampleCount > 0 && cell.ActivityScore is >= 0 and <= 100 && installations.Count > 0
            : cell.SampleCount == 0 && cell.ActivityScore is null && installations.Count == 0);

    private static bool IsValidInstallationProfile(InstallationProfile profile) =>
        profile is not null &&
        Guid.TryParseExact(profile.InstallationId, "N", out _) &&
        profile.MachineName.Length is >= 1 and <= 128 &&
        profile.MachineName == profile.MachineName.Trim() &&
        profile.FriendlyName.Length is >= 1 and <= 64 &&
        profile.FriendlyName == profile.FriendlyName.Trim() &&
        InstallationProfileCatalog.Colors.Contains(profile.Color, StringComparer.Ordinal) &&
        InstallationProfileCatalog.Icons.Contains(profile.Icon, StringComparer.Ordinal) &&
        profile.FirstSeenAt.Offset == TimeSpan.Zero &&
        profile.UpdatedAt.Offset == TimeSpan.Zero &&
        profile.UpdatedAt >= profile.FirstSeenAt &&
        profile.Revision >= 1;

    private void ShowCalendar(DateOnly from, DateOnly today)
    {
        _firstDate = from;
        _lastDate = today;
        // Apply the range only after the report map exists so newly realized day items receive their heat colors.
        ActivityCalendarView.MinDate = ToCalendarDate(from);
        // Let the native month viewport display the current month in full; future days remain blacked out.
        ActivityCalendarView.MaxDate = ToCalendarDate(new DateOnly(today.Year, today.Month, 1).AddMonths(2).AddDays(-1));
        LoadingRing.IsActive = false;
        StatusPanel.Visibility = Visibility.Collapsed;
        CalendarPanel.Visibility = Visibility.Visible;
        ActivityTabs.Visibility = Visibility.Visible;
        DayDetailsBorder.Visibility = Visibility.Visible;
        CalendarLegendText.Text = _recordedDays.Count == 0
            ? T("ActivityCalendar.Empty")
            : string.Format(_culture, T("ActivityCalendar.Legend"), _recordedDays.Count);

        var selectedDate = _recordedDays.ContainsKey(today) || _vipDates.Contains(today)
            ? today
            : _recordedDays.Keys.Concat(_vipDates).DefaultIfEmpty(today).Max();
        var calendarDate = ToCalendarDate(selectedDate);
        ActivityCalendarView.SelectedDates.Clear();
        ActivityCalendarView.SelectedDates.Add(calendarDate);
        ActivityCalendarView.SetDisplayDate(calendarDate);
        UpdateSelectedDay(selectedDate);
        _weekStart = ActivityWeekProjection.MondayOf(selectedDate);
        _calendarReady = true;
    }

    private async void ActivityTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_calendarReady) return;
        if (ReferenceEquals(ActivityTabs.SelectedItem, WeekTab))
        {
            _weekStart = ActivityWeekProjection.MondayOf(_selectedDate);
            await LoadWeekAsync();
        }
        else
        {
            _weekCancellation?.Cancel();
            ActivityCalendarView.SelectedDates.Clear();
            ActivityCalendarView.SelectedDates.Add(ToCalendarDate(_selectedDate));
            ActivityCalendarView.SetDisplayDate(ToCalendarDate(_selectedDate));
            UpdateSelectedDay(_selectedDate);
            CalendarLegendText.Text = string.Format(_culture, T("ActivityCalendar.Legend"), _recordedDays.Count);
            CalendarHintText.Text = T("ActivityCalendar.CalendarHint");
        }
    }

    private void CalendarPanel_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        ActivityCalendarView.Height = Math.Max(0, e.NewSize.Height);
    }

    private async Task LoadWeekAsync()
    {
        _weekCancellation?.Cancel();
        using var request = CancellationTokenSource.CreateLinkedTokenSource(_lifetimeCancellation.Token);
        _weekCancellation = request;
        var monday = _weekStart;
        var from = monday < _firstDate ? _firstDate : monday;
        var sunday = monday.AddDays(6);
        var to = sunday > _lastDate ? _lastDate : sunday;
        _weekCells = null;
        WeekRangeText.Text = $"{monday.ToString("d MMM yyyy", _culture)} – {sunday.ToString("d MMM yyyy", _culture)}";
        PreviousWeekButton.IsEnabled = monday > ActivityWeekProjection.MondayOf(_firstDate);
        NextWeekButton.IsEnabled = sunday < _lastDate;
        WeekHeatmap.Visibility = Visibility.Collapsed;
        DayDetailsBorder.Visibility = Visibility.Collapsed;
        WeekStatusPanel.Visibility = Visibility.Visible;
        WeekLoadingRing.IsActive = true;
        WeekRetryButton.Visibility = Visibility.Collapsed;
        WeekStatusText.Text = T("ActivityCalendar.Loading");
        CalendarLegendText.Text = T("ActivityCalendar.WeekLegend");
        CalendarHintText.Text = T("ActivityCalendar.WeekHint");
        try
        {
            var result = await _application.GetReportAsync(new ReportQuery(from, to, string.Empty, ReportView.HourOfWeek), request.Token);
            // Navigation cancels obsolete reports; they must never overwrite a newer week or the calendar tab.
            request.Token.ThrowIfCancellationRequested();
            if (!result.Succeeded || result.Value is null)
            {
                throw new InvalidDataException(result.Code);
            }

            _weekCells = ActivityWeekProjection.Create(result.Value, monday, from, to);
            WeekHeatmap.Render(_weekCells, _strings, GetActivityHeatBrush, HeatNoData.Background, CloseButton.Foreground);
            WeekHeatmap.Visibility = Visibility.Visible;
            WeekStatusPanel.Visibility = Visibility.Collapsed;
            var selected = _weekCells.FirstOrDefault(cell => cell.Date == _selectedDate && cell.Hour == _selectedHour && cell.IsAvailable)
                ?? _weekCells.LastOrDefault(cell => cell.Date == _selectedDate && cell.Activity.HasData)
                ?? _weekCells.FirstOrDefault(cell => cell.Activity.HasData)
                ?? _weekCells.First(cell => cell.IsAvailable);
            UpdateSelectedHour(selected);
        }
        catch (OperationCanceledException) when (request.IsCancellationRequested)
        {
            // Closing or navigating away discards the old projection without a fallback snapshot.
        }
        catch (Exception)
        {
            // Failed queries stay visibly unavailable and can be retried; old data is never relabeled as this week.
            if (request.IsCancellationRequested) return;
            WeekStatusText.Text = T("ActivityCalendar.Unavailable");
            WeekRetryButton.Visibility = Visibility.Visible;
        }
        finally
        {
            if (ReferenceEquals(_weekCancellation, request))
            {
                WeekLoadingRing.IsActive = false;
                _weekCancellation = null;
            }
        }
    }

    private async void PreviousWeekButton_Click(object sender, RoutedEventArgs e)
    {
        _weekStart = _weekStart.AddDays(-7);
        await LoadWeekAsync();
    }

    private async void NextWeekButton_Click(object sender, RoutedEventArgs e)
    {
        _weekStart = _weekStart.AddDays(7);
        await LoadWeekAsync();
    }

    private async void CurrentWeekButton_Click(object sender, RoutedEventArgs e)
    {
        _weekStart = ActivityWeekProjection.MondayOf(_lastDate);
        await LoadWeekAsync();
    }

    private async void WeekRetryButton_Click(object sender, RoutedEventArgs e) => await LoadWeekAsync();

    private void TodayButton_Click(object sender, RoutedEventArgs e)
    {
        ActivityCalendarView.SelectedDates.Clear();
        ActivityCalendarView.SelectedDates.Add(ToCalendarDate(_lastDate));
        ActivityCalendarView.SetDisplayDate(ToCalendarDate(_lastDate));
        UpdateSelectedDay(_lastDate);
    }

    private void UpdateSelectedHour(ActivityWeekCell selected)
    {
        _selectedDate = selected.Date;
        _selectedHour = selected.Hour;
        WeekHeatmap.Select(selected.Date, selected.Hour);
        var cell = selected.Activity;
        SelectedDateText.Text = $"{selected.Date.ToString("D", _culture)}\n{selected.Hour:00}:00–{selected.Hour + 1:00}:00";
        DayDetailsBorder.Visibility = Visibility.Visible;
        InstallationLegendSection.Visibility = cell.HasData ? Visibility.Visible : Visibility.Collapsed;
        UpdateInstallationLegend(cell.Installations);
        ReprocessAiButton.Visibility = Visibility.Collapsed;
        DayStatusText.Text = T(cell.HasData ? "ActivityCalendar.RecordedActivity" : "ActivityCalendar.NoDataLegend");
        DayMetricsPanel.Visibility = cell.HasData ? Visibility.Visible : Visibility.Collapsed;
        if (cell.ActivityScore is { } score)
        {
            ScoreValueText.Text = score.ToString("N0", _culture);
            ScoreProgressBar.Value = score;
            ScoreProgressBar.Foreground = GetActivityHeatBrush(score);
            ActiveTimeValueText.Text = FormatDuration(cell.ActiveSeconds);
            IdleTimeValueText.Text = FormatDuration(cell.IdleSeconds);
            TrackedTimeValueText.Text = FormatDuration(cell.TrackedSeconds);
            KeyPressesValueText.Text = cell.KeyPresses.ToString("N0", _culture);
            MouseClicksValueText.Text = cell.MouseClicks.ToString("N0", _culture);
            SamplesValueText.Text = cell.SampleCount.ToString("N0", _culture);
            AutomationProperties.SetName(ScoreValueText, string.Format(_culture, T("ActivityCalendar.ScoreAccessible"), score));
        }

        AutomationProperties.SetName(DayDetailsBorder,
            $"{SelectedDateText.Text}. {DayStatusText.Text}. {BuildInstallationAccessibleLabel(cell.Installations)}");
    }

    private void ActivityCalendarView_CalendarViewDayItemChanging(
        CalendarView sender,
        CalendarViewDayItemChangingEventArgs args)
    {
        // CalendarView reuses containers across months; clear the previous day's presentation first.
        args.Item.SizeChanged -= CalendarDay_SizeChanged;
        args.Item.Tag = null;
        args.Item.ClearValue(Control.BackgroundProperty);
        ToolTipService.SetToolTip(args.Item, null);
        args.Item.ClearValue(AutomationProperties.NameProperty);
        if (args.InRecycleQueue)
        {
            return;
        }

        var date = FromCalendarDate(args.Item.Date);
        args.Item.IsBlackout = date > _lastDate || date < _firstDate;
        if (!_recordedDays.TryGetValue(date, out var cell))
        {
            var noDataLabel = string.Format(
                _culture,
                T("ActivityCalendar.Day.NoDataAccessible"),
                date.ToString("D", _culture));
            if (_vipDates.Contains(date))
            {
                args.Item.Tag = CreateCalendarBadges(null, true);
                noDataLabel += ". " + T("Snapshot.Vip.Calendar");
            }
            AutomationProperties.SetName(args.Item, noDataLabel);
            ToolTipService.SetToolTip(args.Item, noDataLabel);
            return;
        }

        var score = cell.ActivityScore!.Value;
        var installations = cell.Installations!;
        args.Item.Background = GetActivityHeatBrush(score);
        args.Item.Tag = CreateCalendarBadges(new ActivityInstallationBadges(installations, _culture), _vipDates.Contains(date));
        args.Item.SizeChanged += CalendarDay_SizeChanged;
        UpdateCalendarDayBadges(args.Item);
        var label = string.Format(
            _culture,
            T("ActivityCalendar.Day.ScoreAccessible"),
            date.ToString("D", _culture),
            score);
        var provenanceLabel = BuildInstallationAccessibleLabel(installations);
        var accessibleLabel = $"{label}. {T("Operations.InstallationTransfer.Installations.List")}: {provenanceLabel}." +
            (_vipDates.Contains(date) ? " " + T("Snapshot.Vip.Calendar") : string.Empty);
        AutomationProperties.SetName(args.Item, accessibleLabel);
        ToolTipService.SetToolTip(args.Item, accessibleLabel);
    }

    private void CalendarDay_SizeChanged(object sender, SizeChangedEventArgs e) =>
        UpdateCalendarDayBadges((CalendarViewDayItem)sender);

    private void UpdateCalendarDayBadges(CalendarViewDayItem item)
    {
        if (item.Tag is not Grid badgeLayout
            || badgeLayout.Children.OfType<ActivityInstallationBadges>().FirstOrDefault() is not { } badges) return;

        // Native CalendarView draws the centered day number outside its template. Reserve its space,
        // using a second line in tall cells and the right-hand side in compact, wide cells.
        _dayNumberMeasure.FontFamily = ActivityCalendarView.DayItemFontFamily;
        _dayNumberMeasure.FontSize = ActivityCalendarView.DayItemFontSize;
        _dayNumberMeasure.Measure(new Windows.Foundation.Size(double.PositiveInfinity, double.PositiveInfinity));
        var dateHeight = _dayNumberMeasure.DesiredSize.Height + Math.Abs(ActivityCalendarView.DayItemMargin.Top - ActivityCalendarView.DayItemMargin.Bottom);
        var belowDate = item.ActualHeight >= dateHeight + 48;
        var width = Math.Max(0, belowDate ? item.ActualWidth - 8 : item.ActualWidth / 2 - _dayNumberMeasure.DesiredSize.Width / 2 - 8);
        badges.HorizontalAlignment = belowDate ? HorizontalAlignment.Center : HorizontalAlignment.Right;
        badges.VerticalAlignment = belowDate ? VerticalAlignment.Bottom : VerticalAlignment.Center;
        badges.Margin = belowDate ? new Thickness(4, 0, 4, 4) : new Thickness(0, 0, 4, 0);
        badges.MaxWidth = width;
        badges.UpdateAvailableSize(width, item.ActualHeight);
    }

    // installations contains activity provenance badges, when activity was recorded.
    // hasVip indicates a retained important screenshot, including days without activity samples.
    private Grid CreateCalendarBadges(ActivityInstallationBadges? installations, bool hasVip)
    {
        var layout = new Grid { IsHitTestVisible = false };
        if (installations is not null) layout.Children.Add(installations);
        if (hasVip)
        {
            layout.Children.Add(new VipBadge
            {
                Text = T("Snapshot.Vip.Calendar"),
                ShowText = false,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 3, 3, 0)
            });
        }
        return layout;
    }

    private Brush GetActivityHeatBrush(int score) => score switch
    {
        >= 0 and < 20 => HeatLevelOne.Background,
        >= 20 and < 40 => HeatLevelTwo.Background,
        >= 40 and < 60 => HeatLevelThree.Background,
        >= 60 and < 80 => HeatLevelFour.Background,
        >= 80 and <= 100 => HeatLevelFive.Background,
        // Invalid report scores must never be silently mapped to an activity level.
        _ => throw new ArgumentOutOfRangeException(nameof(score))
    };

    private void ActivityCalendarView_SelectedDatesChanged(
        CalendarView sender,
        CalendarViewSelectedDatesChangedEventArgs args)
    {
        if (args.AddedDates.Count > 0)
        {
            UpdateSelectedDay(FromCalendarDate(args.AddedDates[0]));
        }
    }

    private async void ActivityCalendarView_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (FindCalendarDayItem(e.OriginalSource as DependencyObject) is not { } dayItem)
        {
            return;
        }

        e.Handled = true;
        var date = FromCalendarDate(dayItem.Date);
        if (dayItem.IsBlackout || date < _firstDate || date > _lastDate) return;
        await OpenScreenshotsAsync(date);
    }

    private static CalendarViewDayItem? FindCalendarDayItem(DependencyObject? source)
    {
        for (var current = source; current is not null; current = VisualTreeHelper.GetParent(current))
        {
            if (current is CalendarViewDayItem dayItem)
            {
                return dayItem;
            }
        }

        return null;
    }

    private void UpdateSelectedDay(DateOnly date)
    {
        _selectedDate = date;
        _selectedHour = null;
        DayDetailsBorder.Visibility = Visibility.Visible;
        InstallationLegendSection.Visibility = Visibility.Visible;
        ReprocessAiButton.Visibility = Visibility.Visible;
        SelectedDateText.Text = date.ToString("D", _culture);
        if (!_recordedDays.TryGetValue(date, out var cell))
        {
            DayStatusText.Text = T("ActivityCalendar.NoActivity");
            if (_vipDates.Contains(date)) DayStatusText.Text += " · " + T("Snapshot.Vip.Calendar");
            DayMetricsPanel.Visibility = Visibility.Collapsed;
            InstallationLegendItems.ItemsSource = null;
            AutomationProperties.SetName(
                DayDetailsBorder,
                string.Format(_culture, T("ActivityCalendar.Day.NoDataAccessible"), SelectedDateText.Text));
            return;
        }

        var score = cell.ActivityScore!.Value;
        DayStatusText.Text = T("ActivityCalendar.RecordedActivity");
        ScoreValueText.Text = score.ToString("N0", _culture);
        ScoreProgressBar.Value = score;
        ScoreProgressBar.Foreground = GetActivityHeatBrush(score);
        ActiveTimeValueText.Text = FormatDuration(cell.ActiveSeconds);
        IdleTimeValueText.Text = FormatDuration(cell.IdleSeconds);
        TrackedTimeValueText.Text = FormatDuration(cell.TrackedSeconds);
        KeyPressesValueText.Text = cell.KeyPresses.ToString("N0", _culture);
        MouseClicksValueText.Text = cell.MouseClicks.ToString("N0", _culture);
        SamplesValueText.Text = cell.SampleCount.ToString("N0", _culture);
        UpdateInstallationLegend(cell.Installations!);
        DayMetricsPanel.Visibility = Visibility.Visible;
        AutomationProperties.SetName(ScoreValueText, string.Format(_culture, T("ActivityCalendar.ScoreAccessible"), score));
        var scoreLabel = string.Format(_culture, T("ActivityCalendar.ScoreAccessible"), score);
        var provenanceLabel = BuildInstallationAccessibleLabel(cell.Installations!);
        AutomationProperties.SetName(
            DayDetailsBorder,
            $"{SelectedDateText.Text}. {scoreLabel}. {T("Operations.InstallationTransfer.Installations.List")}: {provenanceLabel}.");
    }

    private void UpdateInstallationLegend(IReadOnlyList<InstallationProfile> installations)
    {
        InstallationLegendItems.ItemsSource = installations
            .Select(profile => new ActivityCalendarInstallationLegendItem(
                profile.FriendlyName,
                profile.MachineName,
                InstallationAppearance.GetIconGlyph(profile.Icon),
                InstallationAppearance.CreateAccentBrush(profile.Color),
                $"{profile.FriendlyName}, {profile.MachineName}"))
            .ToArray();
    }

    private void ApplyLocalizedContent()
    {
        DialogTitleText.Text = T("ActivityCalendar.CompactTitle");
        DialogSubtitleText.Text = T("ActivityCalendar.RecordedActivity");
        CalendarTab.Header = T("ActivityCalendar.CalendarTab");
        WeekTab.Header = T("ActivityCalendar.WeekTab");
        TodayButton.Content = T("ActivityCalendar.Today");
        CurrentWeekButton.Content = T("ActivityCalendar.CurrentWeek");
        WeekRetryButton.Content = T("ActivityCalendar.Retry");
        NoDataLegendText.Text = T("ActivityCalendar.NoDataLegend");
        UiLocalization.SetAccessibleLabel(PreviousWeekButton, T("ActivityCalendar.PreviousWeek"));
        UiLocalization.SetAccessibleLabel(NextWeekButton, T("ActivityCalendar.NextWeek"));
        CalendarHintText.Text = T("ActivityCalendar.CalendarHint");
        StatusText.Text = T("ActivityCalendar.Loading");
        ScoreLabelText.Text = T("ActivityCalendar.Score");
        ActiveTimeLabelText.Text = T("ActivityCalendar.ActiveTime");
        IdleTimeLabelText.Text = T("ActivityCalendar.IdleTime");
        TrackedTimeLabelText.Text = T("ActivityCalendar.TrackedTime");
        KeyPressesLabelText.Text = T("ActivityCalendar.KeyPresses");
        MouseClicksLabelText.Text = T("ActivityCalendar.MouseClicks");
        SamplesLabelText.Text = T("ActivityCalendar.Samples");
        InstallationLegendTitleText.Text = T("Operations.InstallationTransfer.Installations.List");
        ReprocessAiButtonText.Text = T("ActivityCalendar.Reprocess");
        CloseButton.Content = T("About.Close");
        UiLocalization.SetAccessibleLabel(TitleBarCloseButton, T("About.Close"));
        AutomationProperties.SetName(RootGrid, T("ActivityCalendar.Title"));
        AutomationProperties.SetName(DialogTitleText, DialogTitleText.Text);
        AutomationProperties.SetName(DialogSubtitleText, DialogSubtitleText.Text);
        AutomationProperties.SetName(ActivityCalendarView, T("ActivityCalendar.Title"));
        AutomationProperties.SetName(ReprocessAiButton, ReprocessAiButtonText.Text);
        AutomationProperties.SetName(CloseButton, T("About.Close"));
    }

    private void ShowError(string message)
    {
        LoadingRing.IsActive = false;
        LoadingRing.Visibility = Visibility.Collapsed;
        StatusIcon.Visibility = Visibility.Visible;
        StatusText.Text = message;
    }

    private string FormatDuration(long seconds)
    {
        var hours = seconds / 3600;
        var minutes = (seconds % 3600) / 60;
        var remainingSeconds = seconds % 60;
        return string.Format(
            _culture,
            T("ActivityCalendar.Duration"),
            hours,
            minutes,
            remainingSeconds);
    }

    private string T(string key) => _strings.Translate(key);

    private static DateTimeOffset ToCalendarDate(DateOnly date) =>
        new(new DateTime(date.Year, date.Month, date.Day, 0, 0, 0, DateTimeKind.Local));

    private static DateOnly FromCalendarDate(DateTimeOffset date) => DateOnly.FromDateTime(date.Date);

    private static string BuildInstallationAccessibleLabel(IEnumerable<InstallationProfile> installations) =>
        string.Join(", ", installations.Select(profile =>
            string.Equals(profile.FriendlyName, profile.MachineName, StringComparison.OrdinalIgnoreCase)
                ? profile.FriendlyName
                : $"{profile.FriendlyName} ({profile.MachineName})"));

    private async void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        await CompleteAsync();
    }

    private async void ReprocessAiButton_Click(object sender, RoutedEventArgs e)
    {
        _result = new ActivityCalendarDialogResult(_selectedDate, ActivityCalendarAction.ReprocessDescriptions);
        await CompleteAsync();
    }

    private async Task OpenScreenshotsAsync(DateOnly date)
    {
        if (_isCompleting)
        {
            return;
        }

        _selectedDate = date;
        _result = new ActivityCalendarDialogResult(date, ActivityCalendarAction.OpenScreenshots);
        await CompleteAsync();
    }

    private async void RootGrid_KeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Escape)
        {
            return;
        }

        e.Handled = true;
        await CompleteAsync();
    }

    private async Task CompleteAsync()
    {
        if (_isCompleting)
        {
            return;
        }

        _isCompleting = true;
        CloseButton.IsEnabled = false;
        TitleBarCloseButton.IsEnabled = false;
        ReprocessAiButton.IsEnabled = false;
        _lifetimeCancellation.Cancel();
        _ = await _placement.TrySaveForCloseAsync(CancellationToken.None);
        Close();
    }

    private void ActivityCalendarDialogWindow_Closed(object sender, WindowEventArgs args)
    {
        _lifetimeCancellation.Cancel();
        _titleBar.Dispose();
        _completion.TrySetResult(_result);
    }

}
