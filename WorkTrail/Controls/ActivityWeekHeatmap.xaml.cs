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


using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using WorkTrail.Presentation;
using WorkTrail.Services;
using Windows.System;

namespace WorkTrail.Controls;

/// <summary>Renders application-supplied hourly aggregates and forwards user selection.</summary>
public sealed partial class ActivityWeekHeatmap : UserControl
{
    private const double MinimumCellHeight = 20;
    private readonly List<TextBlock> _columnHeaders = [];
    private readonly List<(Button Button, Border Fill, ActivityWeekCell Cell)> _cells = [];
    private Brush? _selectionBrush;

    /// <summary>Creates the passive weekly grid.</summary>
    public ActivityWeekHeatmap() => InitializeComponent();

    /// <summary>Occurs when the user selects an hour.</summary>
    public event Action<ActivityWeekCell>? CellSelected;

    /// <summary>Occurs when the user double-clicks an hour to explore its date.</summary>
    public event Action<ActivityWeekCell>? CellInvoked;

    private void Heatmap_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateGridHeight();
    }

    private void UpdateGridHeight()
    {
        var headerHeight = _columnHeaders.Count == 0 ? 0 : _columnHeaders.Max(header =>
            header.ActualHeight + header.Margin.Top + header.Margin.Bottom);
        // Give every hour at least 20 DIPs; taller viewports distribute the remaining height equally.
        CellsGrid.MinHeight = headerHeight + 24 * (MinimumCellHeight + CellsGrid.RowSpacing);
        CellsGrid.Height = Math.Max(CellsGrid.MinHeight, HeatmapViewport.ActualHeight);
    }

    /// <summary>Replaces the visible cells with a validated weekly projection.</summary>
    public void Render(IReadOnlyList<ActivityWeekCell> cells, LocalizationService strings,
        Func<int, Brush> heatBrush, Brush emptyBrush, Brush selectionBrush)
    {
        _selectionBrush = selectionBrush;
        _columnHeaders.Clear();
        _cells.Clear();
        CellsGrid.Children.Clear();
        CellsGrid.RowDefinitions.Clear();
        CellsGrid.ColumnDefinitions.Clear();
        CellsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(44) });
        for (var day = 0; day < 7; day++)
        {
            CellsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        }

        CellsGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        for (var hour = 0; hour < 24; hour++)
        {
            CellsGrid.RowDefinitions.Add(new RowDefinition
            {
                Height = new GridLength(1, GridUnitType.Star),
                MinHeight = MinimumCellHeight
            });
            Add(new TextBlock
            {
                Text = $"{hour:00}:00",
                FontSize = 10,
                LineHeight = 10,
                LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
                VerticalAlignment = VerticalAlignment.Center
            }, hour + 1, 0);
        }

        foreach (var cell in cells)
        {
            var day = ((int)cell.Date.DayOfWeek + 6) % 7;
            if (cell.Hour == 0)
            {
                var header = new TextBlock
                {
                    Text = cell.Date.ToString("ddd d", strings.Culture),
                    TextAlignment = TextAlignment.Center,
                    FontSize = 12,
                    Margin = new Thickness(0, 4, 0, 6)
                };
                header.SizeChanged += Heatmap_SizeChanged;
                _columnHeaders.Add(header);
                Add(header, 0, day + 1);
            }

            var fill = new Border
            {
                Background = cell.Activity.ActivityScore is { } score ? heatBrush(score) : emptyBrush,
                BorderThickness = new Thickness(1),
                BackgroundSizing = BackgroundSizing.OuterBorderEdge,
                CornerRadius = new CornerRadius(0)
            };
            var badges = new ActivityInstallationBadges(cell.Activity.Installations, strings.Culture)
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            fill.Child = badges;
            var button = new Button
            {
                Content = fill,
                Padding = new Thickness(0),
                MinHeight = 0,
                MinWidth = 0,
                BorderThickness = new Thickness(0),
                CornerRadius = new CornerRadius(0),
                Background = null,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                VerticalContentAlignment = VerticalAlignment.Stretch,
                IsDoubleTapEnabled = true,
                IsEnabled = cell.IsAvailable,
                Tag = cell
            };
            var activityLabel = cell.Activity.ActivityScore is { } value
                ? string.Format(strings.Culture, strings.Translate("ActivityCalendar.ScoreAccessible"), value)
                : strings.Translate("ActivityCalendar.NoDataLegend");
            var label = $"{cell.Date.ToString("D", strings.Culture)}, {cell.Hour:00}:00–{cell.Hour + 1:00}:00. {activityLabel}";
            if (cell.Activity.Installations.Count > 0)
            {
                var installationNames = string.Join(", ", cell.Activity.Installations.Select(profile =>
                    string.Equals(profile.FriendlyName, profile.MachineName, StringComparison.OrdinalIgnoreCase)
                        ? profile.FriendlyName
                        : $"{profile.FriendlyName} ({profile.MachineName})"));
                label += $". {installationNames}";
            }
            AutomationProperties.SetName(button, label);
            ToolTipService.SetToolTip(button, label);
            button.SizeChanged += (_, e) => badges.UpdateAvailableSize(
                Math.Max(0, e.NewSize.Width - fill.BorderThickness.Left - fill.BorderThickness.Right),
                e.NewSize.Height);
            button.Click += (_, _) => SelectAndNotify(cell);
            button.DoubleTapped += (_, e) => { e.Handled = true; CellInvoked?.Invoke(cell); };
            button.KeyDown += Cell_KeyDown;
            _cells.Add((button, fill, cell));
            Add(button, cell.Hour + 1, day + 1);
        }

        UpdateGridHeight();
    }

    /// <summary>Shows selection without changing the activity fill or emitting a user action.</summary>
    public void Select(DateOnly date, int hour)
    {
        foreach (var entry in _cells)
        {
            entry.Fill.BorderBrush = entry.Cell.Date == date && entry.Cell.Hour == hour ? _selectionBrush : null;
        }
    }

    private void SelectAndNotify(ActivityWeekCell cell)
    {
        Select(cell.Date, cell.Hour);
        CellSelected?.Invoke(cell);
    }

    private void Cell_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        var offset = e.Key switch { VirtualKey.Left => -1, VirtualKey.Right => 1, VirtualKey.Up => -7, VirtualKey.Down => 7, _ => 0 };
        if (offset == 0) return;
        e.Handled = true;
        var index = _cells.FindIndex(entry => ReferenceEquals(entry.Button, sender));
        var next = index + offset;
        if (next < 0 || next >= _cells.Count || !_cells[next].Cell.IsAvailable
            || (Math.Abs(offset) == 1 && next / 7 != index / 7)) return;
        _cells[next].Button.Focus(FocusState.Keyboard);
        _cells[next].Button.StartBringIntoView();
        SelectAndNotify(_cells[next].Cell);
    }

    private void Add(FrameworkElement element, int row, int column)
    {
        Grid.SetRow(element, row);
        Grid.SetColumn(element, column);
        CellsGrid.Children.Add(element);
    }
}
