// SPDX-License-Identifier: MIT
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WorkTrail.Controls;
using WorkTrail.Presentation;

namespace WorkTrail;

internal sealed partial class ActivityCalendarDialogWindow
{
    private CancellationTokenSource? _dayActivityCancellation;

    private void InitializeActivityViews()
    {
        DayTab.Header = T("ActivityTimeline.Day");
        OpenActivityDay.Content = T("ActivityTimeline.OpenDay");
        RefreshActivityDay.Content = T("Operations.Refresh");
        WeekActivitiesToggle.Header = T("ActivityTimeline.Title");
        WeekActivitiesToggle.OnContent = T("ActivityTimeline.Title");
        WeekActivitiesToggle.OffContent = T("ActivityCalendar.RecordedActivity");
        UiLocalization.SetAccessibleLabel(PreviousActivityDay, T("ActivityTimeline.PreviousDay"));
        UiLocalization.SetAccessibleLabel(NextActivityDay, T("ActivityTimeline.NextDay"));
        SelectedDayActivities.RefreshRequested += async () => await LoadDayActivitiesAsync(_selectedDate);
        SelectedDayActivities.SpanInvoked += span =>
        {
            ActivityTabs.SelectedItem = DayTab;
            DayActivities.SelectTime(span.Start);
        };
    }

    private async Task LoadDayActivitiesAsync(DateOnly date)
    {
        _dayActivityCancellation?.Cancel();
        using var request = CancellationTokenSource.CreateLinkedTokenSource(_lifetimeCancellation.Token);
        _dayActivityCancellation = request;
        SelectedDayActivities.ShowMessage(T("ActivityCalendar.Loading"));
        DayActivities.ShowMessage(T("ActivityCalendar.Loading"));
        PreviousActivityDay.IsEnabled = date > _firstDate;
        NextActivityDay.IsEnabled = date < _lastDate;
        try
        {
            var snapshot = await ActivitySpanLoader.LoadAsync(_application, date, date, request.Token);
            request.Token.ThrowIfCancellationRequested();
            SelectedDayActivities.SetItems(snapshot.Spans, date, _strings, dayState: snapshot.Days.FirstOrDefault()?.State);
            DayActivities.SetItems(snapshot.Spans, date, _strings, vertical: true, dayState: snapshot.Days.FirstOrDefault()?.State);
        }
        catch (OperationCanceledException) when (request.IsCancellationRequested) { }
        catch (Exception)
        {
            if (request.IsCancellationRequested) return;
            SelectedDayActivities.ShowMessage(T("ActivityCalendar.Unavailable"));
            DayActivities.ShowMessage(T("ActivityCalendar.Unavailable"));
        }
        finally
        {
            if (ReferenceEquals(_dayActivityCancellation, request)) _dayActivityCancellation = null;
        }
    }

    private async Task LoadWeekActivitiesAsync(DateOnly monday, CancellationToken token)
    {
        WeekActivitiesGrid.Children.Clear();
        WeekActivitiesGrid.ColumnDefinitions.Clear();
        try
        {
            var snapshot = await ActivitySpanLoader.LoadAsync(_application, monday, monday.AddDays(6), token);
            token.ThrowIfCancellationRequested();
            for (var index = 0; index < 7; index++)
            {
                var date = monday.AddDays(index);
                WeekActivitiesGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                var column = new ActivitySpanTimeline();
                column.SetItems(snapshot.Spans.Where(s => s.Date == date).ToArray(), date, _strings, vertical: true,
                    dayState: snapshot.Days.FirstOrDefault(d => d.Date == date)?.State);
                column.SpanInvoked += span =>
                {
                    _selectedDate = span.Date;
                    ActivityTabs.SelectedItem = DayTab;
                };
                Grid.SetColumn(column, index);
                WeekActivitiesGrid.Children.Add(column);
            }
            UpdateActivityWeekMode();
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception)
        {
            if (token.IsCancellationRequested) return;
            WeekActivitiesGrid.Children.Add(new TextBlock { Text = T("ActivityCalendar.Unavailable"), TextWrapping = TextWrapping.Wrap });
            UpdateActivityWeekMode();
        }
    }

    private void WeekActivitiesToggle_Toggled(object sender, RoutedEventArgs e) => UpdateActivityWeekMode();

    private void UpdateActivityWeekMode()
    {
        if (WeekHeatmap is null || WeekActivitiesViewport is null || WeekActivitiesToggle is null) return;
        WeekActivitiesViewport.Visibility = WeekActivitiesToggle.IsOn ? Visibility.Visible : Visibility.Collapsed;
        WeekHeatmap.Visibility = WeekActivitiesToggle.IsOn ? Visibility.Collapsed : Visibility.Visible;
        if (CalendarHintText is not null) CalendarHintText.Text = T(WeekActivitiesToggle.IsOn ? "ActivityTimeline.Hint" : "ActivityCalendar.WeekHint");
    }

    private void OpenActivityDay_Click(object sender, RoutedEventArgs e) => ActivityTabs.SelectedItem = DayTab;

    private void PreviousActivityDay_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedDate > _firstDate) UpdateSelectedDay(_selectedDate.AddDays(-1));
    }

    private void NextActivityDay_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedDate < _lastDate) UpdateSelectedDay(_selectedDate.AddDays(1));
    }

    private async void RefreshActivityDay_Click(object sender, RoutedEventArgs e) => await LoadDayActivitiesAsync(_selectedDate);
}
