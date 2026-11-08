// SPDX-License-Identifier: MIT
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using WorkTrail.Application;
using WorkTrail.Presentation;
using WorkTrail.Services;

namespace WorkTrail.Controls;

/// <summary>A reusable virtualized city timeline with fixed headers and a fixed UTC time gutter.</summary>
public sealed partial class AvailabilityTimelineGrid : UserControl
{
    private readonly TimelineColumnLayout _columns = new();
    private ScrollViewer? _vertical;
    private AvailabilityPlannerSnapshot? _snapshot;
    private LocalizationService? _strings;
    private int _visibleHours = 24;
    private double _rowHeight = 44;
    private bool _rendering;
    private bool _emptyCells = true;
    private bool _compactHeaders;
    private bool _resizingColumns;
    private int _widthRevision;

    /// <summary>Occurs when the user selects a shared UTC interval.</summary>
    public event Action<AvailabilityRow>? SelectionChanged;
    /// <summary>Occurs when the user requests a city's schedule editor.</summary>
    public event Action<AvailabilityPlannerCity>? ScheduleRequested;
    /// <summary>Requests persistence after a completed resize or an explicit return to automatic fitting.</summary>
    public event Action<double>? ColumnWidthRequested;

    /// <summary>Gets the first visible time slot without reading any application or OS state.</summary>
    public DateTimeOffset? VisibleStartUtc => _snapshot is { Rows.Count: > 0 }
        ? _snapshot.Rows[Math.Clamp((int)((_vertical?.VerticalOffset ?? 0) / _rowHeight), 0, _snapshot.Rows.Count - 1)].StartUtc
        : null;

    /// <summary>Immediately reveals the current slot when it is already in the loaded timeline.</summary>
    public void ScrollToCurrentSlot()
    {
        if (_snapshot is { } snapshot) ScrollToSlot(snapshot.NowUtc);
    }

    /// <summary>Creates the constrained scrolling surface and virtualized row template.</summary>
    public AvailabilityTimelineGrid()
    {
        InitializeComponent();
        _columns.Changed += (_, _) => ApplyColumnWidths();
        TimelineScroller.SizeChanged += (_, _) => UpdateColumnLayout();
        CityHeaders.SizeChanged += (_, e) => GutterHeaderRow.Height = new GridLength(e.NewSize.Height);
        TimelineRows.SelectionChanged += (_, _) =>
        {
            if (!_rendering && TimelineRows.SelectedItem is AvailabilityRowPresentation item) SelectionChanged?.Invoke(item.Row);
        };
        TimelineRows.Loaded += (_, _) =>
        {
            if (_vertical is not null) _vertical.ViewChanged -= SyncGutter;
            _vertical = FindScrollViewer(TimelineRows);
            if (_vertical is not null) _vertical.ViewChanged += SyncGutter;
            UpdateColumnLayout();
        };
        TimelineRows.Unloaded += (_, _) => { if (_vertical is not null) _vertical.ViewChanged -= SyncGutter; _vertical = null; };
        TimelineRows.SizeChanged += (_, e) =>
        {
            // A horizontal scrollbar reduces the rows' viewport; UTC must keep the same scrollable extent.
            GutterScroller.Height = e.NewSize.Height;
            UpdateDensity();
        };
    }

    /// <summary>Applies a committed column preference without requesting another save.</summary>
    public void SetColumnWidthPreference(double width)
    {
        // An earlier save must not replace the preview while another grip is being dragged.
        if (!_resizingColumns) _columns.SetPreferredWidth(width);
    }

    /// <summary>Updates the table's visual density while keeping the same selected time and viewport.</summary>
    public void SetDisplayOptions(bool emptyCells, bool compactHeaders)
    {
        if (_emptyCells == emptyCells && _compactHeaders == compactHeaders) return;
        _emptyCells = emptyCells;
        _compactHeaders = compactHeaders;
        if (_snapshot is { } snapshot && _strings is { } strings)
        {
            Apply(snapshot, strings, _visibleHours, navigate: false);
        }
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
        var reuseHeaders = CityHeaders.Children.OfType<PlannerCityHeader>().Select(header => header.CityId)
            .SequenceEqual(snapshot.Cities.Select(city => city.Clock.CityId));
        if (!reuseHeaders) CityHeaders.Children.Clear();
        for (var index = 0; index < snapshot.Cities.Count; index++)
        {
            var city = snapshot.Cities[index];
            var header = reuseHeaders ? (PlannerCityHeader)CityHeaders.Children[index] : CreateHeader(index);
            header.Apply(city, snapshot.ReferenceCityId, strings, _compactHeaders);
            if (!reuseHeaders) CityHeaders.Children.Add(header);
        }
        UpdateColumnLayout();
        ApplyColumnWidths();
        AutomationProperties.SetName(TimelineRows, strings.Translate("Planner.Title"));
        if (!_resizingColumns) _rowHeight = DesiredRowHeight();
        RebuildRows();
        DispatcherQueue.TryEnqueue(() =>
        {
            if (!ReferenceEquals(_snapshot, snapshot)) return;
            TimelineRows.UpdateLayout();
            if (navigate)
            {
                ScrollToSlot(snapshot.InstantUtc);
            }
            else
            {
                var index = anchor.HasValue ? snapshot.Rows.ToList().FindIndex(r => r.StartUtc == anchor) : -1;
                _vertical?.ChangeView(null, index < 0 ? oldOffset : index * _rowHeight + remainder, null, true);
            }
        });
    }

    private void ScrollToSlot(DateTimeOffset instant)
    {
        var item = (TimelineRows.ItemsSource as AvailabilityRowPresentation[])?.FirstOrDefault(entry =>
            entry.Row.StartUtc <= instant && instant < entry.Row.EndUtc);
        if (item is null) return;
        TimelineRows.SelectedItem = item;
        TimelineRows.ScrollIntoView(item, ScrollIntoViewAlignment.Leading);
    }

    private PlannerCityHeader CreateHeader(int columnIndex)
    {
        var header = new PlannerCityHeader { Width = _columns.ColumnWidth };
        header.ScheduleRequested += city => ScheduleRequested?.Invoke(city);
        header.ResizeDelta += delta =>
        {
            _resizingColumns = true;
            _columns.ResizeColumn(delta, columnIndex);
        };
        header.ResizeCompleted += () =>
        {
            _resizingColumns = false;
            UpdateDensity();
            ColumnWidthRequested?.Invoke(_columns.PreferredWidth);
        };
        header.FitWidthRequested += () =>
        {
            _resizingColumns = false;
            _columns.FitColumns();
            UpdateDensity();
            ColumnWidthRequested?.Invoke(0);
        };
        return header;
    }

    private void UpdateColumnLayout() => _columns.UpdateViewport(Math.Max(0, TimelineScroller.ActualWidth), _snapshot?.Cities.Count ?? 0);

    private void ApplyColumnWidths()
    {
        var horizontalOffset = TimelineScroller.HorizontalOffset;
        foreach (var header in CityHeaders.Children.OfType<PlannerCityHeader>()) header.Width = _columns.ColumnWidth;
        TimelineSurface.Width = _columns.TotalWidth;
        var revision = ++_widthRevision;
        // Width changes retain the explored horizontal position; ScrollViewer clamps it to the new extent.
        DispatcherQueue.TryEnqueue(() =>
        {
            if (revision == _widthRevision && TimelineScroller.IsLoaded) TimelineScroller.ChangeView(horizontalOffset, null, null, true);
        });
    }

    private void UpdateDensity()
    {
        if (_snapshot is null || _resizingColumns) return;
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
        var referenceCityName = _snapshot.Cities.Single(city => city.Clock.CityId == _snapshot.ReferenceCityId).Clock.CityName;
        var items = _snapshot.Rows.Select(row => new AvailabilityRowPresentation(row, _snapshot.Cities, _strings, _rowHeight,
            _columns, referenceCityName, row.StartUtc <= _snapshot.NowUtc && _snapshot.NowUtc < row.EndUtc, _emptyCells)).ToArray();
        TimelineRows.ItemsSource = items;
        TimelineRows.SelectedItem = items.FirstOrDefault(i => i.Row.StartUtc == selected);
        UtcLabels.Children.Clear();
        foreach (var item in items)
        {
            var row = item.Row;
            var now = item.IsCurrentSlot;
            var label = new TextBlock
            {
                Text = (now ? "● " : "") + row.StartUtc.ToString("HH:mm\nddd d MMM", _strings.Culture),
                FontSize = 14,
                TextAlignment = TextAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            ToolTipService.SetToolTip(label, row.StartUtc.ToString("ddd d MMM HH:mm", _strings.Culture) + " UTC");
            var content = new Grid();
            content.Children.Add(label);
            if (now)
            {
                label.Foreground = (Brush)Microsoft.UI.Xaml.Application.Current.Resources["SystemFillColorCriticalBrush"];
                label.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
                AutomationProperties.SetName(label, _strings.Translate("Planner.NowLabel") + " · " + label.Text + " UTC");
                AvailabilityTimelineRowControl.AddCurrentSlotHighlight(content);
            }
            if (row.ReferenceDayBoundary is { } boundary)
            {
                var fraction = (boundary.InstantUtc - row.StartUtc).Ticks / (double)(row.EndUtc - row.StartUtc).Ticks;
                var marker = new Border
                {
                    Height = 2,
                    VerticalAlignment = VerticalAlignment.Top,
                    Margin = new Thickness(0, Math.Clamp(fraction * _rowHeight, 0, _rowHeight - 2), 0, 0),
                    Background = (Brush)Microsoft.UI.Xaml.Application.Current.Resources["AccentFillColorDefaultBrush"]
                };
                var markerLabel = _strings.Format("Planner.ReferenceDayStarts", referenceCityName, boundary.LocalDate.ToString("D", _strings.Culture));
                ToolTipService.SetToolTip(marker, markerLabel);
                AutomationProperties.SetName(marker, markerLabel);
                content.Children.Add(marker);
            }
            UtcLabels.Children.Add(new Border { Height = _rowHeight, Child = content });
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
