// SPDX-License-Identifier: MIT
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using WorkTrail.Application;
using WorkTrail.Presentation;
using WorkTrail.Services;

namespace WorkTrail.Controls;

/// <summary>Renders one reusable, accessible availability cell without calculating business rules.</summary>
public sealed class AvailabilityBlock : UserControl
{
    private FrameworkElement? _normalLabels;
    private FrameworkElement? _selectedLabels;

    /// <summary>Displays a projected local interval and its source-backed reason.</summary>
    public void Apply(AvailabilityCell cell, string cityName, LocalizationService strings, bool empty,
        IReadOnlyList<(string CityName, AvailabilityCell Cell)>? members = null, int palette = 0)
    {
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        VerticalContentAlignment = VerticalAlignment.Stretch;
        var labels = new Grid();
        _normalLabels = CreateLabels(cell, strings);
        _normalLabels.Visibility = empty ? Visibility.Collapsed : Visibility.Visible;
        _selectedLabels = CreateLabels(cell, strings, selected: true);
        _selectedLabels.Visibility = Visibility.Collapsed;
        labels.Children.Add(_normalLabels);
        labels.Children.Add(_selectedLabels);
        var phases = (members is { Count: > 0 } ? members.Select(member => member.Cell.SolarPhase) : [cell.SolarPhase]).Distinct().ToArray();
        var symbols = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 2,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0),
            IsHitTestVisible = false
        };
        if (phases.Contains(AvailabilitySolarPhase.Sunset)) symbols.Children.Add(SolarIcon("🌇"));
        if (phases.Contains(AvailabilitySolarPhase.Night)) symbols.Children.Add(SolarIcon("🌙"));
        labels.Children.Add(symbols);
        var border = new Border
        {
            Child = labels,
            BorderThickness = new Thickness(0, 0, 1, 1),
            BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(24, 128, 128, 128))
        };
        var (red, green, blue) = AvailabilityPalette.GetFill(cell.State, palette);
        border.Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, red, green, blue));
        Content = border;
        string Describe(string name, AvailabilityCell value)
        {
            var text = $"{name}: {value.LocalStart.ToString("ddd d MMM HH:mm zzz", strings.Culture)} – {value.LocalEnd.ToString("HH:mm zzz", strings.Culture)}. "
                + strings.Translate($"Planner.State.{value.State}") + ". " + strings.Translate(value.ReasonKey)
                + (value.Detail is null ? "" : $": {value.Detail}");
            if (value.IsNonWorkingDay) text += ". " + strings.Translate("Planner.NonWorkingDay");
            var phaseKey = value.SolarPhase switch
            {
                AvailabilitySolarPhase.Sunset => "WorldClock.SunsetLabel",
                AvailabilitySolarPhase.Night => "WorldClock.Night",
                _ => null
            };
            return phaseKey is null ? text : text + ". " + strings.Translate(phaseKey);
        }
        var description = members is { Count: > 1 }
            ? string.Join("\n", members.Select(member => Describe(member.CityName, member.Cell)))
            : Describe(cityName, cell);
        ToolTipService.SetToolTip(this, description);
        AutomationProperties.SetName(this, description);
    }

    /// <summary>Shows the local date and time for a selected intersection.</summary>
    public void SetSelected(bool selected)
    {
        if (_selectedLabels is not null) _selectedLabels.Visibility = selected ? Visibility.Visible : Visibility.Collapsed;
        if (_normalLabels is not null) _normalLabels.Opacity = selected ? 0 : 1;
    }

    private static TextBlock SolarIcon(string glyph) => new()
    {
        Text = glyph,
        FontFamily = new FontFamily("Segoe UI Emoji"),
        FontSize = 16,
        VerticalAlignment = VerticalAlignment.Center
    };

    private static Grid CreateLabels(AvailabilityCell cell, LocalizationService strings, bool selected = false)
    {
        var symbol = cell.State switch { AvailabilityState.Available => "✓", AvailabilityState.Reserve => "◐", AvailabilityState.Unknown => "ℹ", _ => "×" };
        var ink = new SolidColorBrush(selected ? Windows.UI.Color.FromArgb(255, 80, 80, 80) : cell.IsNonWorkingDay
            ? Windows.UI.Color.FromArgb(255, 113, 53, 68)
            : Windows.UI.Color.FromArgb(255, 36, 51, 66));
        var content = new Grid { Padding = new Thickness(8, 0, 8, 0) };
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(20) });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(20) });
        content.Children.Add(new TextBlock
        {
            Text = selected ? "" : symbol,
            Foreground = ink,
            FontFamily = new FontFamily("Segoe UI Symbol"),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            FontSize = 15
        });
        var time = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        time.Children.Add(new TextBlock
        {
            Text = cell.LocalStart.ToString("HH:mm", strings.Culture),
            TextAlignment = TextAlignment.Center,
            FontSize = 15,
            Foreground = ink
        });
        time.Children.Add(new TextBlock
        {
            Text = cell.LocalStart.ToString("ddd d MMM", strings.Culture),
            TextAlignment = TextAlignment.Center,
            FontSize = 12,
            Foreground = ink,
            Opacity = 0.8
        });
        Grid.SetColumn(time, 1);
        content.Children.Add(time);
        return content;
    }
}

/// <summary>Supplies a recycled row with presentation context.</summary>
public sealed record AvailabilityRowPresentation(AvailabilityRow Row, IReadOnlyList<AvailabilityPlannerCity> Cities,
    LocalizationService Strings, double RowHeight, TimelineColumnLayout? Columns = null, string? ReferenceCityName = null,
    bool IsCurrentSlot = false, bool EmptyCells = true, IReadOnlyList<AvailabilityPlannerGroup>? Groups = null, string HomeCityId = "", int Palette = 0);

/// <summary>Reuses availability blocks inside a virtualized timeline row.</summary>
public sealed class AvailabilityTimelineRowControl : UserControl
{
    private TimelineColumnLayout? _columns;
    private Grid? _cells;
    private bool _listening;
    private ListViewItem? _container;
    private long _selectionToken;
    private Border? _selectionBorder;

    /// <summary>Connects recycled item data to the passive row renderer.</summary>
    public AvailabilityTimelineRowControl()
    {
        DataContextChanged += (_, _) => Render();
        Loaded += (_, _) => { ListenForWidthChanges(); TrackSelection(); };
        Unloaded += (_, _) => { StopListeningForWidthChanges(); StopTrackingSelection(); };
    }

    private void Render()
    {
        StopListeningForWidthChanges();
        _columns = null;
        _cells = null;
        if (DataContext is not AvailabilityRowPresentation item) { Content = null; return; }
        _columns = item.Columns;
        var grid = new Grid { Height = item.RowHeight };
        var groups = item.Groups ?? Enumerable.Range(0, item.Row.Cells.Count).Select(index => new AvailabilityPlannerGroup(index, [index])).ToArray();
        for (var i = 0; i < groups.Count; i++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(_columns?.GetColumnWidth(i) ?? TimelineColumnLayout.MinimumColumnWidth) });
            var cell = new AvailabilityBlock();
            var group = groups[i];
            var sourceCells = group.CityIndices.Select(index => (item.Cities[index].Clock.CityName, item.Row.Cells[index])).ToArray();
            var displayCell = item.Row.GroupedCells is { } grouped ? grouped[i] : item.Row.Cells[group.PrimaryIndex];
            cell.Apply(displayCell, item.Cities[group.PrimaryIndex].Clock.CityName, item.Strings, item.EmptyCells && !group.CityIndices.Any(index => item.Cities[index].Clock.CityId == item.HomeCityId), sourceCells, item.Palette);
            Grid.SetColumn(cell, i);
            grid.Children.Add(cell);
        }
        _cells = grid;
        var root = new Grid { Height = item.RowHeight };
        root.Children.Add(grid);
        if (item.IsCurrentSlot) AddCurrentSlotHighlight(root);
        if (item.Row.ReferenceDayBoundary is { } boundary)
        {
            var fraction = (boundary.InstantUtc - item.Row.StartUtc).Ticks / (double)(item.Row.EndUtc - item.Row.StartUtc).Ticks;
            var line = new Border
            {
                Height = 2,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, Math.Clamp(fraction * item.RowHeight, 0, item.RowHeight - 2), 0, 0),
                Background = (Brush)Microsoft.UI.Xaml.Application.Current.Resources["AccentFillColorDefaultBrush"],
                IsHitTestVisible = false
            };
            var description = item.Strings.Format("Planner.ReferenceDayStarts", item.ReferenceCityName ?? "",
                boundary.LocalDate.ToString("ddd d MMM", item.Strings.Culture));
            AutomationProperties.SetName(line, description);
            ToolTipService.SetToolTip(root, description);
            root.Children.Add(line);
        }
        _selectionBorder = new Border
        {
            BorderThickness = new Thickness(0, 2, 0, 2),
            BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 55, 55, 55)),
            IsHitTestVisible = false
        };
        root.Children.Add(_selectionBorder);
        Content = root;
        UpdateSelection();
        if (IsLoaded) ListenForWidthChanges();
    }

    /// <summary>Marks the current slot without changing the row's measured height or pointer behavior.</summary>
    internal static void AddCurrentSlotHighlight(Grid row)
    {
        row.Children.Add(new Border
        {
            Margin = new Thickness(0, 2, 0, 2),
            BorderThickness = new Thickness(0, 2, 0, 2),
            BorderBrush = (Brush)Microsoft.UI.Xaml.Application.Current.Resources["SystemFillColorCriticalBrush"],
            IsHitTestVisible = false
        });
    }

    private void TrackSelection()
    {
        StopTrackingSelection();
        DependencyObject? parent = this;
        while (parent is not null && parent is not ListViewItem) parent = VisualTreeHelper.GetParent(parent);
        if (parent is not ListViewItem container) return;
        _container = container;
        _selectionToken = container.RegisterPropertyChangedCallback(ListViewItem.IsSelectedProperty, (_, _) => UpdateSelection());
        UpdateSelection();
    }

    private void StopTrackingSelection()
    {
        if (_container is not null) _container.UnregisterPropertyChangedCallback(ListViewItem.IsSelectedProperty, _selectionToken);
        _container = null;
    }

    private void UpdateSelection()
    {
        var selected = _container?.IsSelected == true;
        if (_selectionBorder is not null) _selectionBorder.Visibility = selected ? Visibility.Visible : Visibility.Collapsed;
        if (_cells is not null)
            foreach (var cell in _cells.Children.OfType<AvailabilityBlock>()) cell.SetSelected(selected);
    }

    private void ListenForWidthChanges()
    {
        if (_columns is null || _listening) return;
        _columns.Changed += ColumnWidthChanged;
        _listening = true;
        ColumnWidthChanged(this, EventArgs.Empty);
    }

    private void StopListeningForWidthChanges()
    {
        if (_columns is not null && _listening) _columns.Changed -= ColumnWidthChanged;
        _listening = false;
    }

    private void ColumnWidthChanged(object? sender, EventArgs args)
    {
        if (_columns is null || _cells is null) return;
        for (var i = 0; i < _cells.ColumnDefinitions.Count; i++)
            _cells.ColumnDefinitions[i].Width = new GridLength(_columns.GetColumnWidth(i));
    }
}
