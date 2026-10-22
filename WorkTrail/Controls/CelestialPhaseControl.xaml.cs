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
using WorkTrail.Services;

namespace WorkTrail.Controls;

/// <summary>Renders the current sun or a continuously calculated lunar phase.</summary>
public sealed partial class CelestialPhaseControl : UserControl
{
    #region Fields

    /// <summary>Identifies the daylight dependency property.</summary>
    public static readonly DependencyProperty IsDaylightProperty = DependencyProperty.Register(
        nameof(IsDaylight),
        typeof(bool),
        typeof(CelestialPhaseControl),
        new PropertyMetadata(true, OnVisualPropertyChanged));

    /// <summary>Identifies the 0-360 degree lunar phase dependency property.</summary>
    public static readonly DependencyProperty MoonPhaseAngleDegreesProperty = DependencyProperty.Register(
        nameof(MoonPhaseAngleDegrees),
        typeof(double),
        typeof(CelestialPhaseControl),
        new PropertyMetadata(0d, OnVisualPropertyChanged));

    #endregion

    #region Initialization

    /// <summary>Creates the celestial phase control.</summary>
    public CelestialPhaseControl()
    {
        InitializeComponent();
        UpdateVisual();
    }

    #endregion

    #region Implementation

    /// <summary>Gets or sets whether the sun is currently above the standard apparent horizon.</summary>
    public bool IsDaylight
    {
        get => (bool)GetValue(IsDaylightProperty);
        set => SetValue(IsDaylightProperty, value);
    }

    /// <summary>Gets or sets the Moon's eastward elongation from the Sun.</summary>
    public double MoonPhaseAngleDegrees
    {
        get => (double)GetValue(MoonPhaseAngleDegreesProperty);
        set => SetValue(MoonPhaseAngleDegreesProperty, value);
    }

    /// <summary>Redraws the control after a visual dependency property changes.</summary>
    private static void OnVisualPropertyChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args) =>
        ((CelestialPhaseControl)sender).UpdateVisual();

    /// <summary>Recalculates the control's geometry and visibility from its current properties.</summary>
    private void UpdateVisual()
    {
        if (SunVisual is null || MoonVisual is null || MoonShadow is null)
        {
            return;
        }

        SunVisual.Visibility = IsDaylight ? Visibility.Visible : Visibility.Collapsed;
        MoonVisual.Visibility = IsDaylight ? Visibility.Collapsed : Visibility.Visible;
        if (!IsDaylight)
        {
            MoonShadow.Data = BuildMoonShadow(MoonPhaseAngleDegrees, 102d);
        }
    }

    /// <summary>Reports a failed lunar surface image instead of silently leaving an empty disk.</summary>
    private void MoonImage_Failed(object sender, ExceptionRoutedEventArgs args) =>
        ApplicationErrorService.Report(new InvalidOperationException(args.ErrorMessage), "MoonSurfaceImage", Language);

    /// <summary>Builds the dark lunar region from the outer limb and phase-dependent terminator.</summary>
    private static Geometry BuildMoonShadow(double phaseAngleDegrees, double size)
    {
        const int segmentCount = 96;
        var angle = (phaseAngleDegrees % 360d + 360d) % 360d;
        var waxing = angle <= 180d;
        var radius = size / 2d;
        var phaseAngleRadians = angle * Math.PI / 180d;
        var terminatorCosine = Math.Cos(phaseAngleRadians);
        var points = new List<Windows.Foundation.Point>();

        // The outer limb and calculated terminator form the only dark region.
        for (var index = 0; index <= segmentCount; index++)
        {
            var theta = -Math.PI / 2d + Math.PI * index / segmentCount;
            var x = waxing
                ? radius - radius * Math.Cos(theta)
                : radius + radius * Math.Cos(theta);
            points.Add(new Windows.Foundation.Point(x, radius + radius * Math.Sin(theta)));
        }

        for (var index = segmentCount; index >= 0; index--)
        {
            var yNormalized = -1d + 2d * index / segmentCount;
            var horizontal = radius * Math.Sqrt(Math.Max(0d, 1d - yNormalized * yNormalized));
            var x = waxing
                ? radius + terminatorCosine * horizontal
                : radius - terminatorCosine * horizontal;
            points.Add(new Windows.Foundation.Point(x, radius + yNormalized * radius));
        }

        var figure = new PathFigure { StartPoint = points[0], IsClosed = true, IsFilled = true };
        var segment = new PolyLineSegment();
        foreach (var point in points.Skip(1))
        {
            segment.Points.Add(point);
        }

        figure.Segments.Add(segment);
        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);
        return geometry;
    }

    #endregion
}
