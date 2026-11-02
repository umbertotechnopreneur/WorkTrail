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
    private ScrollViewer? _vertical;
    private AvailabilityPlannerSnapshot? _snapshot;
    private LocalizationService? _strings;
    private int _visibleHours = 24;
    private double _rowHeight = 44;
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
        CityHeaders.SizeChanged += (_, e) => GutterHeaderRow.Height = new GridLength(e.NewSize.Height);
        TimelineRows.SelectionChanged += (_, _) =>
        {
            if (!_rendering && TimelineRows.SelectedItem is AvailabilityRowPresentation item) SelectionChanged?.Invoke(item.Row);
        };
        TimelineRows.Loaded += (_, _) =>
        {
            _vertical = FindScrollViewer(TimelineRows);
            if (_vertical is not null) _vertical.ViewChanged += SyncGutter;
        };
        TimelineRows.Unloaded += (_, _) => { if (_vertical is not null) _vertical.ViewChanged -= SyncGutter; _vertical = null; };
        TimelineRows.SizeChanged += (_, e) =>
        {
            // A horizontal scrollbar reduces the rows' viewport; UTC must keep the same scrollable extent.
            GutterScroller.Height = e.NewSize.Height;
            UpdateDensity();
        };
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
        CityHeaders.Children.Clear();
        foreach (var city in snapshot.Cities)
        {
            var header = new PlannerCityHeader { Width = CityWidth };
            header.Apply(city, snapshot.ReferenceCityId, strings);
            header.ScheduleRequested += item => ScheduleRequested?.Invoke(item);
            CityHeaders.Children.Add(header);
        }
        TimelineSurface.Width = CityWidth * snapshot.Cities.Count;
        AutomationProperties.SetName(TimelineRows, strings.Translate("Planner.Title"));
        _rowHeight = DesiredRowHeight();
        RebuildRows();
        DispatcherQueue.TryEnqueue(() =>
        {
            if (!ReferenceEquals(_snapshot, snapshot)) return;
            TimelineRows.UpdateLayout();
            if (navigate)
            {
                var row = snapshot.Rows.FirstOrDefault(r => r.StartUtc <= snapshot.InstantUtc && r.EndUtc > snapshot.InstantUtc);
                var item = (TimelineRows.ItemsSource as AvailabilityRowPresentation[])?.FirstOrDefault(i => i.Row == row);
                if (item is not null) { TimelineRows.SelectedItem = item; TimelineRows.ScrollIntoView(item, ScrollIntoViewAlignment.Leading); }
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
        var height = DesiredRowHeight();
        if (Math.Abs(height - _rowHeight) < 1) return;
        var rowOffset = (_vertical?.VerticalOffset ?? 0) / _rowHeight;
        _rowHeight = height;
        RebuildRows();
        _vertical?.ChangeView(null, rowOffset * _rowHeight, null, true);
    }

    private double DesiredRowHeight() => _snapshot is null ? 44
        : Math.Clamp(TimelineRows.ActualHeight / (_visibleHours * 60d / _snapshot.StepMinutes), 44, 56);

    private void RebuildRows()
    {
        if (_snapshot is null || _strings is null) return;
        var selected = (TimelineRows.SelectedItem as AvailabilityRowPresentation)?.Row.StartUtc;
        _rendering = true;
        // Fix container height before realization: recycled templates can initially measure as empty.
        // The virtualizer and the UTC gutter must use the same extent, including off-screen rows.
        var style = new Style(typeof(ListViewItem)) { BasedOn = (Style)Resources["TimelineItemStyle"] };
        style.Setters.Add(new Setter(ListViewItem.HeightProperty, _rowHeight));
        TimelineRows.ItemContainerStyle = style;
        var items = _snapshot.Rows.Select(row => new AvailabilityRowPresentation(row, _snapshot.Cities, _strings, _rowHeight)).ToArray();
        TimelineRows.ItemsSource = items;
        TimelineRows.SelectedItem = items.FirstOrDefault(i => i.Row.StartUtc == selected);
        UtcLabels.Children.Clear();
        foreach (var row in _snapshot.Rows)
        {
            var now = row.StartUtc <= _snapshot.NowUtc && row.EndUtc > _snapshot.NowUtc;
            var label = new TextBlock
            {
                Text = (now ? "● " : "") + row.StartUtc.ToString("HH:mm\nddd d MMM", _strings.Culture),
                FontSize = 14,
                TextAlignment = TextAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            ToolTipService.SetToolTip(label, row.StartUtc.ToString("ddd d MMM HH:mm", _strings.Culture) + " UTC");
            UtcLabels.Children.Add(new Border { Height = _rowHeight, Child = label });
        }
        _rendering = false;
    }

    private void SyncGutter(object? sender, ScrollViewerViewChangedEventArgs e) => GutterScroller.ChangeView(null, _vertical?.VerticalOffset, null, true);

    private static ScrollViewer? FindScrollViewer(DependencyObject root)
    {
        if (root is ScrollViewer viewer) return viewer;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            if (FindScrollViewer(VisualTreeHelper.GetChild(root, i)) is { } child) return child;
        return null;
    }
}
