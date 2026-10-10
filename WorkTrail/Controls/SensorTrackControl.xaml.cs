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
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using WorkTrail.Presentation;
using WorkTrail.Services;
using Windows.Foundation;

namespace WorkTrail.Controls;

/// <summary>Renders a responsive device track from supplied readings and bounded graph points.</summary>
public sealed partial class SensorTrackControl : UserControl
{
    private IReadOnlyList<SensorTracePoint> _points = [];
    private DateTimeOffset _now;
    private SensorMonitorRow? _row;
    private SolidColorBrush _accent = new(Microsoft.UI.Colors.CornflowerBlue);

    /// <summary>Creates a passive sensor track.</summary>
    public SensorTrackControl() => InitializeComponent();

    /// <summary>Renders supplied data without accessing hardware or changing the sampling cadence.</summary>
    public void Apply(SensorMonitorRow row, IReadOnlyList<SensorTracePoint> points, DateTimeOffset now, LocalizationService strings)
    {
        _row = row;
        _points = points;
        _now = now;
        _accent = (SolidColorBrush)Resources["Sensor" + row.Category + "Brush"];
        DeviceIcon.Foreground = _accent;
        LevelBarFill.Background = _accent;
        DeviceIcon.Glyph = row.Category switch { "Cpu" => "\uE950", "Memory" => "\uE964", "Gpu" => "\uE7F4", "Storage" => "\uEDA2", _ => "\uE850" };
        CategoryText.Text = strings.Translate("Hardware.Category." + row.Category).ToUpper(strings.Culture)
            + (row.Category == "Storage" ? $" ({row.Name})" : string.Empty);
        NameText.Text = row.Category == "Memory" && row.Name == "Total Memory" ? strings.Translate("Sensors.SystemMemory") : row.Name;
        NameText.Visibility = row.Category == "Storage" ? Visibility.Collapsed : Visibility.Visible;
        ValueText.Text = row.Value;
        CapacityText.Text = row.CapacityText;
        SecondaryText.Text = row.SecondaryValue;
        TemperatureLabel.Text = strings.Translate("Sensors.Temperature");
        TemperatureText.Text = row.HasTemperature ? row.TemperatureValue : "—";
        TemperatureIcon.Foreground = _accent;
        TemperatureIcon.Visibility = row.HasTemperature ? Visibility.Visible : Visibility.Collapsed;
        TemperatureStatus.Text = strings.Translate("Sensors.TemperatureUnavailable");
        TemperatureStatus.Visibility = !row.HasTemperature && row.Category != "Memory" ? Visibility.Visible : Visibility.Collapsed;
        var traceStatus = row.Category != "Battery" && row.Percent.HasValue && points.Count(point => point.Value.HasValue) < 2
            ? strings.Translate("Sensors.Collecting") : string.Empty;
        var updated = string.Format(strings.Culture, strings.Translate("Hardware.DeviceUpdated"), row.SampledAt.ToLocalTime().ToString("T", strings.Culture));
        var description = string.Join(" · ", new[] { CategoryText.Text, row.Name, row.Value, row.Temperature, row.Details, updated, traceStatus }.Where(text => text.Length > 0));
        AutomationProperties.SetName(this, description);
        ToolTipService.SetToolTip(this, description + "\n" + row.Source);
        CapacityText.Visibility = row.CapacityText.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        SecondaryText.Visibility = row.SecondaryValue.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        TraceHost.Visibility = row.Category == "Battery" ? Visibility.Collapsed : Visibility.Visible;
        LevelBarTrack.Visibility = row.Category == "Battery" && row.Percent.HasValue ? Visibility.Visible : Visibility.Collapsed;
        UpdateLevelBar();
        DrawTrace();
    }

    /// <summary>Measures text at the window's available width, including accessibility text scaling.</summary>
    public double MeasureForViewport(double width)
    {
        var layout = SensorMonitorLayout.ResolveTrack(width);
        // A child's desired size must not feed back into its breakpoint or expand the background past the window.
        Width = width;
        Track.MinHeight = layout.RowHeight;
        Track.Padding = new Thickness(0, layout.Padding, 0, layout.Padding);
        ContentGrid.ColumnSpacing = layout.Stacked ? 0 : 16;
        MetricsColumn.Width = new GridLength(layout.Stacked ? 0 : layout.ValueWidth + layout.TemperatureWidth + 16);
        ValueColumn.Width = new GridLength(layout.ValueWidth);
        TemperatureColumn.Width = new GridLength(layout.TemperatureWidth);
        Grid.SetRow(MetricsGrid, layout.Stacked ? 1 : 0);
        Grid.SetColumn(MetricsGrid, layout.Stacked ? 0 : 1);
        MetricsGrid.Margin = layout.Stacked ? new Thickness(44, 12, 0, 0) : new Thickness(0);
        TemperatureLabel.Visibility = layout.Stacked ? Visibility.Visible : Visibility.Collapsed;
        ValueText.FontSize = layout.Stacked ? 22 : 28;
        Measure(new Size(width, double.PositiveInfinity));
        return DesiredSize.Height;
    }

    private void LevelBarTrack_SizeChanged(object sender, SizeChangedEventArgs e) => UpdateLevelBar();

    private void UpdateLevelBar() => LevelBarFill.Width = LevelBarTrack.ActualWidth * (_row?.Percent ?? 0) / 100;

    private void TraceCanvas_SizeChanged(object sender, SizeChangedEventArgs e) => DrawTrace();

    private void DrawTrace()
    {
        TraceCanvas.Children.Clear();
        var width = TraceCanvas.ActualWidth;
        var height = TraceCanvas.ActualHeight;
        if (width <= 0 || height <= 0) return;
        TraceCanvas.Clip = new RectangleGeometry { Rect = new Rect(0, 0, width, height) };
        var segment = new List<Point>();
        foreach (var sample in _points)
        {
            // Missing readings split both the line and its fill; a gap must never look measured.
            if (sample.Value is not { } value) { DrawSegment(segment, height); segment.Clear(); continue; }
            segment.Add(new Point(Math.Clamp(1 - (_now - sample.Timestamp).TotalSeconds / 120, 0, 1) * width,
                (1 - value / 100) * Math.Max(0, height - 4) + 2));
        }
        DrawSegment(segment, height);
    }

    private void DrawSegment(IReadOnlyList<Point> points, double height)
    {
        if (points.Count < 2) return;
        var color = _accent.Color;
        var gradient = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
        gradient.GradientStops.Add(new GradientStop { Offset = 0, Color = Windows.UI.Color.FromArgb(96, color.R, color.G, color.B) });
        gradient.GradientStops.Add(new GradientStop { Offset = 1, Color = Windows.UI.Color.FromArgb(0, color.R, color.G, color.B) });
        var fill = new Polygon { Fill = gradient };
        fill.Points.Add(new Point(points[0].X, height));
        var line = new Polyline { Stroke = _accent, StrokeThickness = 1.7, StrokeLineJoin = PenLineJoin.Round };
        foreach (var point in points) { fill.Points.Add(point); line.Points.Add(point); }
        fill.Points.Add(new Point(points[^1].X, height));
        TraceCanvas.Children.Add(fill);
        TraceCanvas.Children.Add(line);
    }
}
