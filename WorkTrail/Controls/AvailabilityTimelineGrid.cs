// SPDX-License-Identifier: MIT
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using WorkTrail.Application;
using WorkTrail.Services;

namespace WorkTrail.Controls;

/// <summary>A reusable virtualized city timeline with fixed headers and a fixed UTC time gutter.</summary>
public sealed partial class AvailabilityTimelineGrid : UserControl
{
    /// <summary>Gets the shared logical column width used by headers and timeline cells.</summary>
    public const double CityWidth = 178;
    private readonly StackPanel _headers = new() { Orientation = Orientation.Horizontal };
    private readonly StackPanel _utcLabels = new();
    private readonly ScrollViewer _gutter = new() { VerticalScrollBarVisibility = ScrollBarVisibility.Hidden, HorizontalScrollMode = ScrollMode.Disabled, IsHitTestVisible = false };
    private readonly ListView _rows = new() { SelectionMode = ListViewSelectionMode.Single, IsItemClickEnabled = true };
    private readonly ScrollViewer _horizontal = new()
    {
        HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
        HorizontalScrollMode = ScrollMode.Enabled,
        VerticalScrollMode = ScrollMode.Disabled,
        VerticalScrollBarVisibility = ScrollBarVisibility.Disabled
    };
    private ScrollViewer? _vertical;
    private AvailabilityPlannerSnapshot? _snapshot;
    private LocalizationService? _strings;
    private int _visibleHours = 24;
    private double _rowHeight = 30;
    private bool _rendering;

    /// <summary>Occurs when the user selects a shared UTC interval.</summary>
    public event Action<AvailabilityRow>? SelectionChanged;
    /// <summary>Occurs when the user requests a city's schedule editor.</summary>
    public event Action<AvailabilityPlannerCity>? ScheduleRequested;

    /// <summary>Gets the first visible time slot without reading any application or OS state.</summary>
    public DateTimeOffset? VisibleStartUtc => _snapshot is { Rows.Count: > 0 }
        ? _snapshot.Rows[Math.Clamp((int)((_vertical?.VerticalOffset ?? 0) / _rowHeight), 0, _snapshot.Rows.Count - 1)].StartUtc
        : null;

    /// <summary>Creates the constrained scrolling surface and virtualized row template.</summary>
    public AvailabilityTimelineGrid()
    {
        InitializeComponent();
        var root = new Grid();
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(82) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var timeline = new Grid();
        timeline.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        timeline.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        timeline.Children.Add(_headers);
        Grid.SetRow(_rows, 1);
        timeline.Children.Add(_rows);
        _horizontal.Content = timeline;
        Grid.SetColumn(_horizontal, 1);
        root.Children.Add(_horizontal);
        var gutterLayout = new Grid();
        gutterLayout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(260) });
        gutterLayout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        gutterLayout.Children.Add(new TextBlock
        {
            Text = "UTC",
            VerticalAlignment = VerticalAlignment.Bottom,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 12)
        });
        _gutter.Content = _utcLabels;
        Grid.SetRow(_gutter, 1);
        gutterLayout.Children.Add(_gutter);
        root.Children.Add(gutterLayout);
        _headers.SizeChanged += (_, e) => gutterLayout.RowDefinitions[0].Height = new GridLength(e.NewSize.Height);
        _rows.ItemTemplate = (DataTemplate)Resources["AvailabilityRowTemplate"];
        var style = new Style(typeof(ListViewItem));
        style.Setters.Add(new Setter(ListViewItem.PaddingProperty, new Thickness(0)));
        style.Setters.Add(new Setter(ListViewItem.MarginProperty, new Thickness(0)));
        style.Setters.Add(new Setter(ListViewItem.MinHeightProperty, 0d));
        style.Setters.Add(new Setter(ListViewItem.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch));
        _rows.ItemContainerStyle = style;
        _rows.SelectionChanged += (_, _) =>
        {
            if (!_rendering && _rows.SelectedItem is AvailabilityRowPresentation item) SelectionChanged?.Invoke(item.Row);
        };
        _rows.Loaded += (_, _) =>
        {
            _vertical = FindScrollViewer(_rows);
            if (_vertical is not null) _vertical.ViewChanged += SyncGutter;
        };
        _rows.Unloaded += (_, _) => { if (_vertical is not null) _vertical.ViewChanged -= SyncGutter; _vertical = null; };
        SizeChanged += (_, _) => UpdateDensity();
        Content = root;
    }

    /// <summary>Updates city headers and rows while optionally retaining the explored scroll position.</summary>
    public void Apply(AvailabilityPlannerSnapshot snapshot, LocalizationService strings, int visibleHours, bool navigate)
    {
        var oldOffset = _vertical?.VerticalOffset ?? 0;
        var anchorIndex = (int)(oldOffset / _rowHeight);
        var anchor = _snapshot is not null && anchorIndex < _snapshot.Rows.Count ? _snapshot.Rows[anchorIndex].StartUtc : (DateTimeOffset?)null;
        var remainder = oldOffset % _rowHeight;
        _snapshot = snapshot;
        _strings = strings;
        _visibleHours = visibleHours;
        _headers.Children.Clear();
        foreach (var city in snapshot.Cities)
        {
            var header = new PlannerCityHeader { Width = CityWidth };
            header.Apply(city, snapshot.ReferenceCityId, strings);
            header.ScheduleRequested += item => ScheduleRequested?.Invoke(item);
            _headers.Children.Add(header);
        }
        _rows.Width = CityWidth * snapshot.Cities.Count;
        AutomationProperties.SetName(_rows, strings.Translate("Planner.Title"));
        RebuildRows();
        DispatcherQueue.TryEnqueue(() =>
        {
            if (!ReferenceEquals(_snapshot, snapshot)) return;
            if (navigate)
            {
                var row = snapshot.Rows.FirstOrDefault(r => r.StartUtc <= snapshot.InstantUtc && r.EndUtc > snapshot.InstantUtc);
                var item = (_rows.ItemsSource as AvailabilityRowPresentation[])?.FirstOrDefault(i => i.Row == row);
                if (item is not null) { _rows.SelectedItem = item; _rows.ScrollIntoView(item, ScrollIntoViewAlignment.Leading); }
            }
            else
            {
                var index = anchor.HasValue ? snapshot.Rows.ToList().FindIndex(r => r.StartUtc == anchor) : -1;
                _vertical?.ChangeView(null, index < 0 ? oldOffset : index * _rowHeight + remainder, null, true);
            }
        });
    }

    private void UpdateDensity()
    {
        if (_snapshot is null) return;
        var height = Math.Max(28, Math.Min(48, (ActualHeight - _headers.ActualHeight - 18) / (_visibleHours * 60d / _snapshot.StepMinutes)));
        if (Math.Abs(height - _rowHeight) < 1) return;
        var rowOffset = (_vertical?.VerticalOffset ?? 0) / _rowHeight;
        _rowHeight = height;
        RebuildRows();
        _vertical?.ChangeView(null, rowOffset * _rowHeight, null, true);
    }

    private void RebuildRows()
    {
        if (_snapshot is null || _strings is null) return;
        var selected = (_rows.SelectedItem as AvailabilityRowPresentation)?.Row.StartUtc;
        _rendering = true;
        var items = _snapshot.Rows.Select(row => new AvailabilityRowPresentation(row, _snapshot.Cities, _strings, _rowHeight)).ToArray();
        _rows.ItemsSource = items;
        _rows.SelectedItem = items.FirstOrDefault(i => i.Row.StartUtc == selected);
        _utcLabels.Children.Clear();
        foreach (var row in _snapshot.Rows)
        {
            var now = row.StartUtc <= _snapshot.NowUtc && row.EndUtc > _snapshot.NowUtc;
            var label = new TextBlock
            {
                Text = (now ? "● " : "") + row.StartUtc.ToString(row.StartUtc.TimeOfDay == TimeSpan.Zero ? "d MMM\nHH:mm" : "HH:mm", _strings.Culture),
                FontSize = 12,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            ToolTipService.SetToolTip(label, row.StartUtc.ToString("ddd d MMM HH:mm", _strings.Culture) + " UTC");
            _utcLabels.Children.Add(new Border { Height = _rowHeight, Child = label });
        }
        _rendering = false;
    }

    private void SyncGutter(object? sender, ScrollViewerViewChangedEventArgs e) => _gutter.ChangeView(null, _vertical?.VerticalOffset, null, true);

    private static ScrollViewer? FindScrollViewer(DependencyObject root)
    {
        if (root is ScrollViewer viewer) return viewer;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            if (FindScrollViewer(VisualTreeHelper.GetChild(root, i)) is { } child) return child;
        return null;
    }
}
