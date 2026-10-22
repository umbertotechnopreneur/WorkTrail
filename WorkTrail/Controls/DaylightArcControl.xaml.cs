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
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;

namespace WorkTrail.Controls;

/// <summary>Passively renders a normalized 24-hour daylight arc.</summary>
public sealed partial class DaylightArcControl : UserControl
{
    #region Fields

    public static readonly DependencyProperty CurrentHourProperty = DependencyProperty.Register(
        nameof(CurrentHour), typeof(double), typeof(DaylightArcControl), new PropertyMetadata(0d, OnVisualPropertyChanged));

    public static readonly DependencyProperty SunriseHourProperty = DependencyProperty.Register(
        nameof(SunriseHour), typeof(double), typeof(DaylightArcControl), new PropertyMetadata(-1d, OnVisualPropertyChanged));

    public static readonly DependencyProperty SunsetHourProperty = DependencyProperty.Register(
        nameof(SunsetHour), typeof(double), typeof(DaylightArcControl), new PropertyMetadata(-1d, OnVisualPropertyChanged));

    public static readonly DependencyProperty IsDaylightProperty = DependencyProperty.Register(
        nameof(IsDaylight), typeof(bool), typeof(DaylightArcControl), new PropertyMetadata(false, OnVisualPropertyChanged));

    #endregion

    #region Initialization

    /// <summary>
    /// Initializes the passive daylight-arc control and its XAML visual tree.
    /// </summary>
    public DaylightArcControl()
    {
        InitializeComponent();
    }

    #endregion

    #region Implementation

    public double CurrentHour
    {
        get => (double)GetValue(CurrentHourProperty);
        set => SetValue(CurrentHourProperty, value);
    }

    public double SunriseHour
    {
        get => (double)GetValue(SunriseHourProperty);
        set => SetValue(SunriseHourProperty, value);
    }

    public double SunsetHour
    {
        get => (double)GetValue(SunsetHourProperty);
        set => SetValue(SunsetHourProperty, value);
    }

    public bool IsDaylight
    {
        get => (bool)GetValue(IsDaylightProperty);
        set => SetValue(IsDaylightProperty, value);
    }

    /// <summary>Redraws the control after a visual dependency property changes.</summary>
    private static void OnVisualPropertyChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args) =>
        ((DaylightArcControl)sender).UpdateVisual();

    /// <summary>Recalculates the daylight arc for the current viewport.</summary>
    private void ArcRoot_SizeChanged(object sender, SizeChangedEventArgs e) => UpdateVisual();

    /// <summary>Recalculates the control's geometry and visibility from its current properties.</summary>
    private void UpdateVisual()
    {
        if (FullArc is null || DayArc is null || CurrentMarker is null || CurrentMarkerGlow is null || ActualWidth <= 0d)
        {
            return;
        }

        FullArc.Data = BuildArc(0d, 24d);
        DayArc.Data = SunriseHour >= 0d && SunsetHour >= SunriseHour
            ? BuildArc(SunriseHour, SunsetHour)
            : IsDaylight ? BuildArc(0d, 24d) : new PathGeometry();

        var marker = PointAt(CurrentHour);
        Canvas.SetLeft(CurrentMarkerGlow, marker.X - 9d);
        Canvas.SetTop(CurrentMarkerGlow, marker.Y - 9d);
        Canvas.SetLeft(CurrentMarker, marker.X - 5d);
        Canvas.SetTop(CurrentMarker, marker.Y - 5d);
    }

    /// <summary>Constructs the daylight path between two clamped hours of the day.</summary>
    private Geometry BuildArc(double startHour, double endHour)
    {
        const int segmentCount = 72;
        var start = Math.Clamp(startHour, 0d, 24d);
        var end = Math.Clamp(endHour, start, 24d);
        var figure = new PathFigure { StartPoint = PointAt(start), IsClosed = false, IsFilled = false };
        var segment = new PolyLineSegment();
        for (var index = 1; index <= segmentCount; index++)
        {
            var hour = start + ((end - start) * index / segmentCount);
            segment.Points.Add(PointAt(hour));
        }

        figure.Segments.Add(segment);
        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);
        return geometry;
    }

    /// <summary>Projects an hour of the day onto the elliptical daylight arc.</summary>
    private Point PointAt(double hour)
    {
        var normalized = Math.Clamp(hour / 24d, 0d, 1d);
        var horizontalRadius = Math.Max(1d, (ActualWidth - 20d) / 2d);
        const double baseY = 71d;
        const double verticalRadius = 47d;
        var angle = Math.PI * normalized;
        return new Point(
            (ActualWidth / 2d) - (horizontalRadius * Math.Cos(angle)),
            baseY - (verticalRadius * Math.Sin(angle)));
    }

    #endregion
}
