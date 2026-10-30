// SPDX-License-Identifier: MIT
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WorkTrail.Application;
using WorkTrail.Services;

namespace WorkTrail.Controls;

/// <summary>Collects local working preferences without reading or writing persistence.</summary>
public sealed class AvailabilityScheduleEditor : UserControl
{
    private readonly AvailabilitySchedule _original;
    private readonly TimePicker _start = new() { ClockIdentifier = "24HourClock" };
    private readonly TimePicker _end = new() { ClockIdentifier = "24HourClock" };
    private readonly NumberBox _before = new() { Minimum = 0, Maximum = 360, SmallChange = 30, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact };
    private readonly NumberBox _after = new() { Minimum = 0, Maximum = 360, SmallChange = 30, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact };
    private readonly List<(DayOfWeek Day, CheckBox Box)> _days = [];
    private readonly DateOnly _exceptionDate;
    private readonly ComboBox _exception = new() { HorizontalAlignment = HorizontalAlignment.Stretch };

    /// <summary>Creates an editor for a city's rules and the selected local date.</summary>
    public AvailabilityScheduleEditor(AvailabilitySchedule schedule, DateOnly date, LocalizationService strings)
    {
        _original = schedule; _exceptionDate = date;
        var panel = new StackPanel { Spacing = 12, MinWidth = 350 };
        _start.Header = strings.Translate("Planner.Start"); _start.Time = schedule.Start.ToTimeSpan();
        _end.Header = strings.Translate("Planner.End"); _end.Time = schedule.End.ToTimeSpan();
        var hours = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        hours.Children.Add(_start); hours.Children.Add(_end); panel.Children.Add(hours);
        panel.Children.Add(new TextBlock { Text = strings.Translate("Planner.Overnight"), TextWrapping = TextWrapping.Wrap });
        var reserves = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        _before.Header = strings.Translate("Planner.Before"); _before.Value = schedule.ReserveBeforeMinutes;
        _after.Header = strings.Translate("Planner.After"); _after.Value = schedule.ReserveAfterMinutes;
        _before.Width = 170; _after.Width = 170; reserves.Children.Add(_before); reserves.Children.Add(_after); panel.Children.Add(reserves);
        panel.Children.Add(new TextBlock { Text = strings.Translate("Planner.RestDay") });
        var days = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        foreach (var day in new[] { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday })
        {
            var box = new CheckBox { Content = strings.Culture.DateTimeFormat.GetAbbreviatedDayName(day), IsChecked = schedule.RestDays.Contains(day), MinWidth = 0 };
            _days.Add((day, box)); days.Children.Add(box);
        }
        panel.Children.Add(days);
        _exception.Header = strings.Translate("Planner.PersonalException") + " · " + date.ToString("d", strings.Culture);
        _exception.Items.Add(strings.Translate("Planner.UseSchedule"));
        foreach (var state in new[] { AvailabilityState.Available, AvailabilityState.Reserve, AvailabilityState.Unavailable })
            _exception.Items.Add(strings.Translate("Planner.State." + state));
        var existing = schedule.Exceptions?.FirstOrDefault(e => e.Date == date);
        _exception.SelectedIndex = existing is null ? 0 : (int)existing.State + 1;
        panel.Children.Add(_exception);
        panel.Children.Add(new TextBlock { Text = strings.Translate("Planner.RulesHint"), TextWrapping = TextWrapping.Wrap });
        Content = panel;
    }

    /// <summary>Returns the entered rules for application validation and persistence.</summary>
    public AvailabilitySchedule GetSchedule()
    {
        var exceptions = _original.Exceptions?.Where(e => e.Date != _exceptionDate).ToList() ?? [];
        if (_exception.SelectedIndex > 0) exceptions.Add(new(_exceptionDate, (AvailabilityState)(_exception.SelectedIndex - 1)));
        return _original with
        {
            Start = TimeOnly.FromTimeSpan(_start.Time),
            End = TimeOnly.FromTimeSpan(_end.Time),
            ReserveBeforeMinutes = double.IsFinite(_before.Value) ? (int)_before.Value : -1,
            ReserveAfterMinutes = double.IsFinite(_after.Value) ? (int)_after.Value : -1,
            RestDays = _days.Where(d => d.Box.IsChecked == true).Select(d => d.Day).ToArray(),
            Exceptions = exceptions
        };
    }
}
