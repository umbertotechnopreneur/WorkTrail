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


using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using WorkTrail.Presentation;
using WorkTrail.Services;
using Windows.Foundation;

namespace WorkTrail.Controls;

/// <summary>Edits the complete weekly selection through two synchronized, scrollable half-day views.</summary>
public sealed partial class WeeklyHoursEditor : UserControl
{
    private const int SlotsPerDay = WeeklyHoursGridProjection.SlotsPerDay;
    private const int SlotsPerHour = 60 / WeeklyHoursGridProjection.MinutesPerSlot;
    private const double SlotHeight = 24d;
    private const double TimeAxisWidth = 56d;
    private const double DragMovementThreshold = 4d;
    private static IReadOnlyList<string> Days => ActiveHoursSchedule.Days;
    private readonly Dictionary<string, ToggleButton[]> _daySlots = new(StringComparer.Ordinal);
    private readonly Dictionary<string, TextBlock> _dayLabels = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Grid> _dayTimelines = new(StringComparer.Ordinal);
    private readonly Dictionary<ToggleButton, string> _slotDays = new();
    private readonly double[] _halfScrollOffsets = [6 * SlotsPerHour * SlotHeight, 0];
    private LocalizationService _strings = new("system");
    private WeeklyHoursViewport _viewport = new(0);
    private bool _updatingSelection;
    private uint? _dragPointerId;
    private Point _dragStartPosition;
    private int _lastDragDayIndex;
    private int _lastDragSlotIndex;
    private bool? _dragSelectionValue;
    private bool _isDragging;

    /// <summary>Creates the reusable weekly hours editor.</summary>
    public WeeklyHoursEditor()
    {
        InitializeComponent();
        BuildGrid();
        ApplyViewport();
        DaysHost.AddHandler(PointerPressedEvent, new PointerEventHandler(DaysHost_PointerPressed), true);
        DaysHost.AddHandler(PointerMovedEvent, new PointerEventHandler(DaysHost_PointerMoved), true);
        DaysHost.AddHandler(PointerReleasedEvent, new PointerEventHandler(DaysHost_PointerReleased), true);
        DaysHost.PointerCanceled += DaysHost_PointerCanceled;
        DaysHost.PointerCaptureLost += DaysHost_PointerCaptureLost;
    }

    /// <summary>Localizes view selectors, weekday names, instructions and slot accessibility labels.</summary>
    public void ApplyLanguage(string language)
    {
        _strings = new LocalizationService(language);
        UiLocalization.Apply(this, _strings);
        UpdateLocalizedLabels();
    }

    /// <summary>Loads all 96 slots per day, including the half that is currently hidden.</summary>
    public void LoadSchedule(IReadOnlyList<ActiveHoursDay>? schedule)
    {
        SetSelection(dayIndex =>
        {
            var dayName = Days[dayIndex];
            var day = schedule?.LastOrDefault(candidate => string.Equals(candidate.Day, dayName, StringComparison.OrdinalIgnoreCase))
                ?? new ActiveHoursDay(dayName);
            return WeeklyHoursGridProjection.ToSlots(day);
        });
        UpdateLocalizedLabels();
    }

    /// <summary>Returns both halves of every day in the application's normalized schedule format.</summary>
    public IReadOnlyList<ActiveHoursDay> GetSchedule() =>
        Days.Select(day => WeeklyHoursGridProjection.FromSlots(day, GetSelectedSlots(day))).ToArray();

    /// <summary>Replaces both views with Monday-Friday 09:00-18:00 and clears weekends.</summary>
    public void ApplyStandardWorkWeek() =>
        SetSelection(dayIndex => Enumerable.Range(0, SlotsPerDay).Select(slot => dayIndex < 5 && slot is >= 36 and < 72).ToArray());

    /// <summary>Clears every selected slot in both halves of the day.</summary>
    public void ClearAll() => SetSelection(_ => new bool[SlotsPerDay]);

    private void SetSelection(Func<int, bool[]> getSlots)
    {
        _updatingSelection = true;
        try
        {
            for (var dayIndex = 0; dayIndex < Days.Count; dayIndex++)
            {
                var slots = getSlots(dayIndex);
                for (var slot = 0; slot < SlotsPerDay; slot++)
                {
                    _daySlots[Days[dayIndex]][slot].IsChecked = slots[slot];
                }
            }
        }
        finally
        {
            _updatingSelection = false;
        }

        UpdateAllDayBands();
    }

    private void BuildGrid()
    {
        DayHeaders.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(TimeAxisWidth) });
        DaysHost.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(TimeAxisWidth) });
        TimeAxis.Height = WeeklyHoursViewport.SlotCount * SlotHeight;
        for (var dayIndex = 0; dayIndex < Days.Count; dayIndex++)
        {
            DayHeaders.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            DaysHost.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var day = Days[dayIndex];
            var label = new TextBlock
            {
                Style = RequiredStyle(dayIndex >= 5 ? "ScheduleWeekendDayLabelStyle" : "ScheduleDayLabelStyle")
            };
            Grid.SetColumn(label, dayIndex + 1);
            DayHeaders.Children.Add(label);
            _dayLabels.Add(day, label);

            var timeline = new Grid
            {
                Height = WeeklyHoursViewport.SlotCount * SlotHeight,
                VerticalAlignment = VerticalAlignment.Top,
                Style = RequiredStyle(dayIndex >= 5 ? "ScheduleWeekendTimelineStyle" : "ScheduleTimelineStyle")
            };
            for (var row = 0; row < WeeklyHoursViewport.SlotCount; row++)
            {
                timeline.RowDefinitions.Add(new RowDefinition { Height = new GridLength(SlotHeight) });
            }

            _dayTimelines.Add(day, timeline);
            _daySlots.Add(day, new ToggleButton[SlotsPerDay]);
            for (var slot = 0; slot < SlotsPerDay; slot++)
            {
                var button = new ToggleButton
                {
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    VerticalAlignment = VerticalAlignment.Stretch,
                    Style = RequiredStyle(slot % SlotsPerHour == 0 ? "ScheduleHourSlotStyle" : "ScheduleSlotStyle"),
                    Tag = slot
                };
                button.Checked += Slot_CheckedChanged;
                button.Unchecked += Slot_CheckedChanged;
                _daySlots[day][slot] = button;
                _slotDays.Add(button, day);
                Grid.SetRow(button, slot % WeeklyHoursViewport.SlotCount);
                timeline.Children.Add(button);
            }

            Grid.SetColumn(timeline, dayIndex + 1);
            DaysHost.Children.Add(timeline);
        }
    }

    private void MorningButton_Click(object sender, RoutedEventArgs e) => SwitchHalf(0);

    private void EveningButton_Click(object sender, RoutedEventArgs e) => SwitchHalf(1);

    private void SwitchHalf(int half)
    {
        var next = new WeeklyHoursViewport(half);
        if (next.FirstSlot != _viewport.FirstSlot)
        {
            _halfScrollOffsets[_viewport.FirstSlot / WeeklyHoursViewport.SlotCount] = DaysScrollViewer.VerticalOffset;
            ClearDragGesture();
            DaysHost.ReleasePointerCaptures();
            _viewport = next;
            ApplyViewport();
            RestoreViewOffset();
        }

        // A half-day selector always has exactly one selected value.
        MorningButton.IsChecked = half == 0;
        EveningButton.IsChecked = half == 1;
    }

    private void ApplyViewport()
    {
        foreach (var slots in _daySlots.Values)
        {
            for (var slot = 0; slot < SlotsPerDay; slot++)
            {
                slots[slot].Visibility = slot >= _viewport.FirstSlot && slot < _viewport.EndSlot
                    ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        TimeAxis.Children.Clear();
        for (var slot = _viewport.FirstSlot; slot <= _viewport.EndSlot; slot += SlotsPerHour)
        {
            var label = new TextBlock { Text = CreateSlotLabel(slot), Style = RequiredStyle("ScheduleTimeLabelStyle") };
            Canvas.SetTop(label, (slot - _viewport.FirstSlot) * SlotHeight - 8);
            Canvas.SetLeft(label, 2);
            TimeAxis.Children.Add(label);
        }

        UpdateAllDayBands();
        UpdateVisibleRange();
    }

    private void DaysScrollViewer_Loaded(object sender, RoutedEventArgs e) => RestoreViewOffset();

    private void RestoreViewOffset()
    {
        if (DaysScrollViewer.IsLoaded)
        {
            DaysScrollViewer.ChangeView(null, _halfScrollOffsets[_viewport.FirstSlot / WeeklyHoursViewport.SlotCount], null, true);
        }

        UpdateVisibleRange();
    }

    private void DaysScrollViewer_SizeChanged(object sender, SizeChangedEventArgs e) => UpdateVisibleRange();

    private void DaysScrollViewer_ViewChanged(object sender, ScrollViewerViewChangedEventArgs e) => UpdateVisibleRange();

    private void UpdateVisibleRange()
    {
        var firstRow = Math.Clamp((int)Math.Floor(DaysScrollViewer.VerticalOffset / SlotHeight), 0, WeeklyHoursViewport.SlotCount - 1);
        var lastRow = Math.Clamp((int)Math.Ceiling((DaysScrollViewer.VerticalOffset + DaysScrollViewer.ViewportHeight) / SlotHeight), firstRow + 1, WeeklyHoursViewport.SlotCount);
        VisibleRangeText.Text = _strings.Format("Schedule.View.VisibleRange",
            CreateSlotLabel(_viewport.FirstSlot + firstRow), CreateSlotLabel(_viewport.FirstSlot + lastRow));
    }

    private void DaysHost_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (e.Pointer.PointerDeviceType == PointerDeviceType.Touch)
        {
            return;
        }

        var point = e.GetCurrentPoint(DaysHost);
        if ((e.Pointer.PointerDeviceType == PointerDeviceType.Mouse && !point.Properties.IsLeftButtonPressed)
            || !TryGetSlot(point.Position, out var dayIndex, out var slotIndex, out var slot))
        {
            return;
        }

        // ToggleButton captures pointer input before this handled-events-too parent handler runs.
        // Transfer that capture now so the editor receives the full drag and owns pointer release.
        slot.ReleasePointerCapture(e.Pointer);
        if (!DaysHost.CapturePointer(e.Pointer))
        {
            return;
        }

        _dragPointerId = e.Pointer.PointerId;
        _dragStartPosition = point.Position;
        _lastDragDayIndex = dayIndex;
        _lastDragSlotIndex = slotIndex;
        _dragSelectionValue = !(slot.IsChecked == true);
        _isDragging = false;
        e.Handled = true;
    }

    private void DaysHost_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (e.Pointer.PointerDeviceType == PointerDeviceType.Touch
            || _dragPointerId != e.Pointer.PointerId
            || !_dragSelectionValue.HasValue)
        {
            return;
        }

        var point = e.GetCurrentPoint(DaysHost);
        if ((e.Pointer.PointerDeviceType == PointerDeviceType.Mouse && !point.Properties.IsLeftButtonPressed)
            || (e.Pointer.PointerDeviceType != PointerDeviceType.Mouse && !point.IsInContact))
        {
            return;
        }

        var position = point.Position;
        var hasTargetSlot = TryGetSlot(position, out var dayIndex, out var slotIndex, out _);
        if (!_isDragging)
        {
            var horizontalMovement = Math.Abs(position.X - _dragStartPosition.X);
            var verticalMovement = Math.Abs(position.Y - _dragStartPosition.Y);
            var enteredAnotherSlot = hasTargetSlot
                && (dayIndex != _lastDragDayIndex || slotIndex != _lastDragSlotIndex);
            if (!enteredAnotherSlot
                && horizontalMovement <= DragMovementThreshold
                && verticalMovement <= DragMovementThreshold)
            {
                return;
            }

            _isDragging = true;
            _daySlots[Days[_lastDragDayIndex]][_lastDragSlotIndex].IsChecked = _dragSelectionValue.Value;
        }

        if (hasTargetSlot)
        {
            ApplyDragPath(dayIndex, slotIndex, _dragSelectionValue.Value);
        }

        e.Handled = true;
    }

    private void DaysHost_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (e.Pointer.PointerDeviceType == PointerDeviceType.Touch || _dragPointerId != e.Pointer.PointerId)
        {
            return;
        }

        if (!_isDragging && _dragSelectionValue.HasValue)
        {
            _daySlots[Days[_lastDragDayIndex]][_lastDragSlotIndex].IsChecked = _dragSelectionValue.Value;
        }

        e.Handled = true;
        ClearDragGesture();
        DaysHost.ReleasePointerCapture(e.Pointer);
    }

    private void DaysHost_PointerCanceled(object sender, PointerRoutedEventArgs e)
    {
        if (_dragPointerId == e.Pointer.PointerId)
        {
            ClearDragGesture();
        }
    }

    private void DaysHost_PointerCaptureLost(object sender, PointerRoutedEventArgs e)
    {
        if (ReferenceEquals(e.OriginalSource, DaysHost) && _dragPointerId == e.Pointer.PointerId)
        {
            ClearDragGesture();
        }
    }

    private void ClearDragGesture()
    {
        _dragPointerId = null;
        _dragSelectionValue = null;
        _isDragging = false;
    }

    private void ApplyDragPath(int targetDayIndex, int targetSlotIndex, bool selectionValue)
    {
        var dayIndex = _lastDragDayIndex;
        var slotIndex = _lastDragSlotIndex;
        var dayDistance = Math.Abs(targetDayIndex - dayIndex);
        var dayStep = dayIndex < targetDayIndex ? 1 : -1;
        var slotDistance = -Math.Abs(targetSlotIndex - slotIndex);
        var slotStep = slotIndex < targetSlotIndex ? 1 : -1;
        var error = dayDistance + slotDistance;

        while (true)
        {
            _daySlots[Days[dayIndex]][slotIndex].IsChecked = selectionValue;
            if (dayIndex == targetDayIndex && slotIndex == targetSlotIndex)
            {
                break;
            }

            var doubledError = 2 * error;
            if (doubledError >= slotDistance)
            {
                error += slotDistance;
                dayIndex += dayStep;
            }

            if (doubledError <= dayDistance)
            {
                error += dayDistance;
                slotIndex += slotStep;
            }
        }

        _lastDragDayIndex = targetDayIndex;
        _lastDragSlotIndex = targetSlotIndex;
    }

    private bool TryGetSlot(Point position, out int dayIndex, out int slotIndex, out ToggleButton slot)
    {
        dayIndex = -1;
        slotIndex = -1;
        slot = null!;
        if (position.X < 0 || position.Y < 0)
        {
            return false;
        }

        for (var candidateDayIndex = 0; candidateDayIndex < Days.Count; candidateDayIndex++)
        {
            var timeline = _dayTimelines[Days[candidateDayIndex]];
            if (timeline.ActualWidth <= 0 || timeline.ActualHeight <= 0)
            {
                continue;
            }

            var origin = timeline.TransformToVisual(DaysHost).TransformPoint(new Point(0, 0));
            var relativeX = position.X - origin.X;
            var relativeY = position.Y - origin.Y;
            if (relativeX < 0
                || relativeX >= timeline.ActualWidth
                || relativeY < 0
                || relativeY >= timeline.ActualHeight)
            {
                continue;
            }

            dayIndex = candidateDayIndex;
            var row = (int)Math.Floor(relativeY / SlotHeight);
            if (row >= WeeklyHoursViewport.SlotCount)
            {
                continue;
            }

            slotIndex = _viewport.GetSlotIndex(row);
            slot = _daySlots[Days[dayIndex]][slotIndex];
            return true;
        }

        return false;
    }

    private void Slot_CheckedChanged(object sender, RoutedEventArgs e)
    {
        if (!_updatingSelection && sender is ToggleButton button && _slotDays.TryGetValue(button, out var day))
        {
            UpdateDayBands(day);
        }
    }

    /// <summary>Labels schedule cells in the selected language while keeping their test IDs invariant.</summary>
    private void UpdateLocalizedLabels()
    {
        foreach (var day in Days)
        {
            var dayOfWeek = Enum.Parse<DayOfWeek>(day, ignoreCase: true);
            var dayName = _strings.Culture.DateTimeFormat.GetDayName(dayOfWeek);
            _dayLabels[day].Text = _strings.Culture.DateTimeFormat.GetAbbreviatedDayName(dayOfWeek)
                .TrimEnd('.').ToUpper(_strings.Culture);
            AutomationProperties.SetName(_dayTimelines[day], dayName);
            for (var slot = 0; slot < SlotsPerDay; slot++)
            {
                var name = _strings.Format("Schedule.Slot.Accessible", dayName, CreateSlotLabel(slot), CreateSlotLabel(slot + 1));
                AutomationProperties.SetAutomationId(
                    _daySlots[day][slot],
                    $"Schedule.Weekly.Slot.{day}.{slot.ToString("D2", System.Globalization.CultureInfo.InvariantCulture)}");
                AutomationProperties.SetName(_daySlots[day][slot], name);
                ToolTipService.SetToolTip(_daySlots[day][slot], name);
            }
        }

        SetViewAccessibility(MorningButton, "Schedule.View.Morning", "00:00–12:00");
        SetViewAccessibility(EveningButton, "Schedule.View.Evening", "12:00–24:00");
        UpdateVisibleRange();
    }

    private void SetViewAccessibility(ToggleButton button, string key, string range)
    {
        var name = $"{_strings.Translate(key)} · {range}";
        AutomationProperties.SetName(button, name);
        ToolTipService.SetToolTip(button, name);
    }

    private bool[] GetSelectedSlots(string day) => _daySlots[day].Select(static button => button.IsChecked == true).ToArray();

    private void UpdateAllDayBands()
    {
        foreach (var day in Days)
        {
            UpdateDayBands(day);
        }
    }

    private void UpdateDayBands(string day)
    {
        foreach (var button in _daySlots[day])
        {
            button.Content = null;
        }

        foreach (var band in _viewport.GetBands(GetSelectedSlots(day)))
        {
            var prefix = band.StartSlot < band.VisibleStartSlot ? "↑ " : string.Empty;
            var suffix = band.EndSlot > band.VisibleEndSlot ? " ↓" : string.Empty;
            _daySlots[day][band.VisibleStartSlot].Content = new TextBlock
            {
                Text = $"{prefix}{CreateSlotLabel(band.StartSlot)}–{CreateSlotLabel(band.EndSlot)}{suffix}",
                TextTrimming = TextTrimming.CharacterEllipsis,
                FontSize = 12
            };
        }
    }

    private Style RequiredStyle(string key) => Resources[key] as Style
        ?? throw new InvalidOperationException($"The schedule style '{key}' is required.");

    private static string CreateSlotLabel(int slot) => WeeklyHoursGridProjection.FormatBoundary(slot);
}
