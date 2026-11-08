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
    /// <summary>Displays a projected local interval and its source-backed reason.</summary>
    public void Apply(AvailabilityCell cell, string cityName, LocalizationService strings, bool empty)
    {
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        VerticalContentAlignment = VerticalAlignment.Stretch;
        var border = new Border
        {
            Child = empty ? null : CreateLabels(cell, strings),
            BorderThickness = new Thickness(0, 0, 1, 1),
            BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(24, 128, 128, 128))
        };
        var color = cell.State switch
        {
            AvailabilityState.Available => Windows.UI.Color.FromArgb(42, 76, 175, 100),
            AvailabilityState.Reserve => Windows.UI.Color.FromArgb(65, 230, 165, 40),
            AvailabilityState.Unavailable => Windows.UI.Color.FromArgb(46, 190, 85, 85),
            _ => Windows.UI.Color.FromArgb(35, 128, 128, 128)
        };
        border.Background = new SolidColorBrush(color);
        Content = border;
        var description = $"{cityName}: {cell.LocalStart.ToString("ddd d MMM HH:mm zzz", strings.Culture)} – {cell.LocalEnd.ToString("HH:mm zzz", strings.Culture)}. "
            + strings.Translate($"Planner.State.{cell.State}") + ". " + strings.Translate(cell.ReasonKey)
            + (cell.Detail is null ? "" : $": {cell.Detail}");
        if (cell.IsNonWorkingDay) description += ". " + strings.Translate("Planner.NonWorkingDay");
        ToolTipService.SetToolTip(this, description);
        AutomationProperties.SetName(this, description);
    }

    private static Grid CreateLabels(AvailabilityCell cell, LocalizationService strings)
    {
        var symbol = cell.State switch { AvailabilityState.Available => "✓", AvailabilityState.Reserve => "◐", AvailabilityState.Unknown => "ℹ", _ => "×" };
        var content = new Grid { Padding = new Thickness(8, 0, 8, 0) };
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(20) });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(20) });
        content.Children.Add(new TextBlock
        {
            Text = symbol,
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
            Foreground = (Brush)Microsoft.UI.Xaml.Application.Current.Resources[cell.IsNonWorkingDay ? "SystemFillColorCriticalBrush" : "TextFillColorPrimaryBrush"]
        });
        time.Children.Add(new TextBlock
        {
            Text = cell.LocalStart.ToString("ddd d MMM", strings.Culture),
            TextAlignment = TextAlignment.Center,
            FontSize = 12,
            Foreground = (Brush)Microsoft.UI.Xaml.Application.Current.Resources[cell.IsNonWorkingDay ? "SystemFillColorCriticalBrush" : "TextFillColorPrimaryBrush"],
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
    bool IsCurrentSlot = false, bool EmptyCells = true);

/// <summary>Reuses availability blocks inside a virtualized timeline row.</summary>
public sealed class AvailabilityTimelineRowControl : UserControl
{
    private TimelineColumnLayout? _columns;
    private Grid? _cells;
    private bool _listening;

    /// <summary>Connects recycled item data to the passive row renderer.</summary>
    public AvailabilityTimelineRowControl()
    {
        DataContextChanged += (_, _) => Render();
        Loaded += (_, _) => ListenForWidthChanges();
        Unloaded += (_, _) => StopListeningForWidthChanges();
    }

    private void Render()
    {
        StopListeningForWidthChanges();
        _columns = null;
        _cells = null;
        if (DataContext is not AvailabilityRowPresentation item) { Content = null; return; }
        _columns = item.Columns;
        var grid = new Grid { Height = item.RowHeight };
        for (var i = 0; i < item.Row.Cells.Count; i++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(_columns?.ColumnWidth ?? TimelineColumnLayout.MinimumColumnWidth) });
            var cell = new AvailabilityBlock();
            cell.Apply(item.Row.Cells[i], item.Cities[i].Clock.CityName, item.Strings, item.EmptyCells);
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
        Content = root;
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
        foreach (var column in _cells.ColumnDefinitions) column.Width = new GridLength(_columns.ColumnWidth);
    }
}
