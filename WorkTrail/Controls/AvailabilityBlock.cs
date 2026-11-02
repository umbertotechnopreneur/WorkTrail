// SPDX-License-Identifier: MIT
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using WorkTrail.Application;
using WorkTrail.Services;

namespace WorkTrail.Controls;

/// <summary>Renders one reusable, accessible availability cell without calculating business rules.</summary>
public sealed class AvailabilityBlock : UserControl
{
    /// <summary>Displays a projected local interval and its source-backed reason.</summary>
    public void Apply(AvailabilityCell cell, string cityName, LocalizationService strings)
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
        var border = new Border
        {
            Child = content,
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
}

/// <summary>Supplies a recycled row with presentation context.</summary>
public sealed record AvailabilityRowPresentation(AvailabilityRow Row, IReadOnlyList<AvailabilityPlannerCity> Cities,
    LocalizationService Strings, double RowHeight);

/// <summary>Reuses availability blocks inside a virtualized timeline row.</summary>
public sealed class AvailabilityTimelineRowControl : UserControl
{
    /// <summary>Connects recycled item data to the passive row renderer.</summary>
    public AvailabilityTimelineRowControl()
    {
        DataContextChanged += (_, _) => Render();
    }

    private void Render()
    {
        if (DataContext is not AvailabilityRowPresentation item) return;
        var grid = new Grid { Height = item.RowHeight };
        for (var i = 0; i < item.Row.Cells.Count; i++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(AvailabilityTimelineGrid.CityWidth) });
            var cell = new AvailabilityBlock();
            cell.Apply(item.Row.Cells[i], item.Cities[i].Clock.CityName, item.Strings);
            Grid.SetColumn(cell, i);
            grid.Children.Add(cell);
        }
        Content = grid;
    }
}
