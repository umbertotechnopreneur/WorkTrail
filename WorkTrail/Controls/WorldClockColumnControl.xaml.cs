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
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using WorkTrail.Application;
using WorkTrail.Presentation;
using WorkTrail.Services;
using Windows.Foundation;

namespace WorkTrail.Controls;

/// <summary>Renders one city in the detached world-clock comparison surface.</summary>
public sealed partial class WorldClockColumnControl : UserControl
{
    private const double SolarArcStartX = 32d;
    private const double SolarArcEndX = 272d;
    private const double SolarArcBaselineY = 138d;
    private const double SolarArcControlY = 4d;
    private const int SolarArcSegments = 48;

    private string? _skylineAssetPath;
    private string[] _backdropAssetPaths = [];
    private string[] _foregroundAssetPaths = [];
    private double _viewportWidth;
    private double _viewportHeight;

    /// <summary>Gets the measured height needed by the explicitly requested presentation, before window chrome.</summary>
    public double PreferredContentHeight { get; private set; }

    /// <summary>Creates one passive world-clock column.</summary>
    public WorldClockColumnControl()
    {
        InitializeComponent();
    }

    private void ColumnRoot_SizeChanged(object sender, SizeChangedEventArgs e) => UpdateSceneLayout();

    private void UpdateSceneLayout()
    {
        if (XamlRoot is null)
        {
            // Defer decoration until the column is attached and its display scale is available.
            return;
        }

        var width = ColumnRoot.ActualWidth;
        var height = WorldClockWindowLayoutState.CalculateSceneHeight(
            width, ColumnRoot.ActualHeight, XamlRoot.RasterizationScale);
        // Move and clip the whole composition together; images never measure the clock content.
        SceneGrid.Width = width;
        SceneGrid.Height = height;
        Canvas.SetTop(SceneGrid, ColumnRoot.ActualHeight - height);
        SceneGrid.Clip = new RectangleGeometry { Rect = new Rect(0d, 0d, width, height) };
    }

    /// <summary>Measures the non-wrapping clock at its largest type size, including accessibility text scaling.</summary>
    public double MeasureMinimumWidth()
    {
        LocalTimeText.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        return Math.Max(320d, Math.Ceiling(LocalTimeText.DesiredSize.Width * 58d / LocalTimeText.FontSize) + 32d);
    }

    /// <summary>Renders the detail that fits the viewport, reserving text clearance for the floating attribution.</summary>
    public void SetViewportSize(double width, double height, double bottomOverlayHeight)
    {
        if (!double.IsFinite(width) || !double.IsFinite(height) || width < 0d || height < 0d)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "The viewport must be finite and non-negative.");
        }

        if (!double.IsFinite(bottomOverlayHeight) || bottomOverlayHeight < 0d)
        {
            throw new ArgumentOutOfRangeException(nameof(bottomOverlayHeight));
        }

        _viewportWidth = width;
        _viewportHeight = height;
        MinHeight = height;
        ClockDetailsLayout.Padding = new Thickness(16d, 12d, 16d, 16d + bottomOverlayHeight);
        ApplyPresentationMode();
        // The window also refreshes the viewport when moving between monitors with different DPI.
        UpdateSceneLayout();
    }

    /// <summary>Applies the latest locally calculated city projection.</summary>
    public void Apply(
        WorldClockItem clock,
        WorldClockItem referenceClock,
        bool isReference,
        SpaceWeatherAlert? spaceWeatherAlert,
        LocalizationService strings)
    {
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(referenceClock);
        ArgumentNullException.ThrowIfNull(strings);
        if (!double.IsFinite(clock.MoonPhaseAngleDegrees))
        {
            throw new InvalidDataException("The world-clock lunar phase must be finite.");
        }

        var accent = clock.IsDaylight ? DayAccentResource.Background : NightAccentResource.Background;
        CityNameText.Text = clock.CityName.ToUpper(strings.Culture);
        LocalTimeText.Text = clock.LocalTime.ToString("HH:mm", strings.Culture);
        LocalTimeText.Foreground = accent;
        DayStateText.Text = strings.Translate(clock.IsDaylight ? "WorldClock.Day" : "WorldClock.Night");
        OffsetText.Text = isReference
            ? strings.Translate("WorldClock.LocalTime")
            : strings.Format("WorldClock.OffsetFromReference", FormatOffset(clock.LocalTime.Offset - referenceClock.LocalTime.Offset));
        DateRelationText.Text = DateRelation(clock.LocalTime.Date, referenceClock.LocalTime.Date, strings);
        DateRelationText.Visibility = string.IsNullOrEmpty(DateRelationText.Text) ? Visibility.Collapsed : Visibility.Visible;
        var daylightSavingSummary = ApplyDaylightSaving(clock, strings);

        ApplySkyline(clock.SkylineAssetPath);
        _backdropAssetPaths = ApplyLayers(
            BackdropLayerHost,
            clock.Atmosphere.BackdropAssetPaths,
            _backdropAssetPaths,
            Stretch.UniformToFill);
        _foregroundAssetPaths = ApplyLayers(
            ForegroundLayerHost,
            clock.Atmosphere.ForegroundAssetPaths,
            _foregroundAssetPaths,
            Stretch.UniformToFill);
        var moonPhaseSummary = ApplyMoonPhase(clock, strings, accent);
        var sunriseTime = FormatTime(clock.Sunrise, strings);
        var sunsetTime = FormatTime(clock.Sunset, strings);
        SunriseLabelText.Text = strings.Translate("WorldClock.SunriseLabel");
        SunsetLabelText.Text = strings.Translate("WorldClock.SunsetLabel");
        SunriseTimeText.Text = sunriseTime;
        SunsetTimeText.Text = sunsetTime;
        var sunriseSummary = strings.Format("WorldClock.Sunrise", sunriseTime);
        var sunsetSummary = strings.Format("WorldClock.Sunset", sunsetTime);
        var daylightSummary = ApplySolarTimeline(clock, strings);
        var weatherSummary = ApplyWeather(clock.Weather, strings);
        var spaceWeatherSummary = ApplySpaceWeatherAlert(spaceWeatherAlert, strings);
        CompactDaylightDurationText.Text = daylightSummary;
        CompactTimeZoneText.Text = string.Concat("UTC", FormatOffset(clock.LocalTime.Offset));
        ApplyPresentationMode();

        var accessibleDetails = new[]
        {
            clock.CityName,
            LocalTimeText.Text,
            OffsetText.Text,
            DateRelationText.Text,
            DayStateText.Text,
            daylightSavingSummary,
            moonPhaseSummary,
            weatherSummary,
            spaceWeatherSummary,
            sunriseSummary,
            sunsetSummary,
            daylightSummary
        }.Where(static detail => !string.IsNullOrEmpty(detail));
        var accessibleSummary = string.Join(", ", accessibleDetails);
        AutomationProperties.SetName(ColumnRoot, accessibleSummary);
        AutomationProperties.SetLocalizedLandmarkType(ColumnRoot, clock.CityName);
    }

    private void ApplyPresentationMode()
    {
        if (_viewportWidth <= 0d)
        {
            return;
        }

        // Measure actual localized text (including Windows text scaling), not a fixed window-height preset.
        ApplyDetailLayout(WorldClockDetailLevel.Summary, inlineWeather: false);
        TimeInfo.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        WeatherPanel.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var inlineWeather = TimeInfo.DesiredSize.Width + WeatherPanel.DesiredSize.Width + 48d <= _viewportWidth;

        ApplyDetailLayout(WorldClockDetailLevel.Expanded, inlineWeather: false);
        ClockDetailsLayout.Measure(new Size(_viewportWidth, double.PositiveInfinity));
        var expandedHeight = ClockDetailsLayout.DesiredSize.Height + ColumnRoot.RowDefinitions[0].Height.Value;

        ApplyDetailLayout(WorldClockDetailLevel.Summary, inlineWeather);
        ClockDetailsLayout.Measure(new Size(_viewportWidth, double.PositiveInfinity));
        var summaryHeight = ClockDetailsLayout.DesiredSize.Height;
        PreferredContentHeight = expandedHeight;

        var detail = WorldClockWindowLayoutState.CalculateDetailLevel(
            _viewportHeight, expandedHeight, summaryHeight);
        ApplyDetailLayout(detail, inlineWeather);
    }

    private void ApplyDetailLayout(WorldClockDetailLevel detail, bool inlineWeather)
    {
        var expanded = detail == WorldClockDetailLevel.Expanded;
        var sideBySide = !expanded && inlineWeather;
        ReferenceMarker.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
        ColumnRoot.RowDefinitions[0].Height = new GridLength(expanded ? 38 : 0);
        ClockDetailsLayout.ColumnSpacing = sideBySide ? 16d : 0d;
        WeatherColumn.Width = sideBySide ? GridLength.Auto : new GridLength(0);
        SkylineSpacerRow.MinHeight = 0d;

        CityNameText.HorizontalAlignment = expanded ? HorizontalAlignment.Center : HorizontalAlignment.Stretch;
        CityNameText.FontSize = expanded ? 20d : 18d;
        CityNameText.TextAlignment = expanded ? TextAlignment.Center : TextAlignment.Left;
        Grid.SetColumnSpan(TimeInfo, sideBySide ? 1 : 2);
        TimeInfo.HorizontalAlignment = expanded ? HorizontalAlignment.Center : HorizontalAlignment.Stretch;
        LocalTimeText.FontSize = expanded ? 58d : 56d;
        LocalTimeText.HorizontalAlignment = expanded ? HorizontalAlignment.Center : HorizontalAlignment.Left;
        OffsetText.HorizontalAlignment = expanded ? HorizontalAlignment.Center : HorizontalAlignment.Left;
        OffsetText.TextAlignment = expanded ? TextAlignment.Center : TextAlignment.Left;
        DateRelationText.HorizontalAlignment = expanded ? HorizontalAlignment.Center : HorizontalAlignment.Left;
        DateRelationText.TextAlignment = expanded ? TextAlignment.Center : TextAlignment.Left;
        DaylightSavingText.HorizontalAlignment = expanded ? HorizontalAlignment.Center : HorizontalAlignment.Left;
        DaylightSavingText.TextAlignment = expanded ? TextAlignment.Center : TextAlignment.Left;
        DaylightSavingText.Visibility = detail != WorldClockDetailLevel.Essential ? Visibility.Visible : Visibility.Collapsed;
        // Day changes remain visible even in the smallest comparison.
        DateRelationText.Visibility = string.IsNullOrEmpty(DateRelationText.Text) ? Visibility.Collapsed : Visibility.Visible;
        DayStateText.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
        SolarArcPanel.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
        MoonPhaseSummaryText.Visibility = expanded && !string.IsNullOrEmpty(MoonPhaseSummaryText.Text)
            ? Visibility.Visible : Visibility.Collapsed;

        Grid.SetRow(WeatherPanel, sideBySide ? 1 : 5);
        Grid.SetColumn(WeatherPanel, sideBySide ? 1 : 0);
        Grid.SetColumnSpan(WeatherPanel, sideBySide ? 1 : 2);
        WeatherPanel.Margin = sideBySide ? new Thickness(0) : new Thickness(0, 8, 0, 0);
        WeatherPanel.HorizontalAlignment = expanded ? HorizontalAlignment.Center : HorizontalAlignment.Stretch;
        WeatherPanel.VerticalAlignment = VerticalAlignment.Top;
        WeatherTemperatureText.FontSize = expanded ? 38d : 36d;
        WeatherConditionText.FontSize = 12d;

        CompactFooter.Visibility = expanded ? Visibility.Collapsed : Visibility.Visible;
        CompactDaylightDurationText.Visibility = detail == WorldClockDetailLevel.Summary
            && !string.IsNullOrEmpty(CompactDaylightDurationText.Text) ? Visibility.Visible : Visibility.Collapsed;
        CompactTimeZoneText.Visibility = !expanded && !string.IsNullOrEmpty(CompactTimeZoneText.Text)
            ? Visibility.Visible : Visibility.Collapsed;
    }

    private string ApplyDaylightSaving(WorldClockItem clock, LocalizationService strings)
    {
        var summary = clock.IsDaylightSavingTime
            ? clock.DaylightSavingEndsAt is { } end
                ? strings.Format("WorldClock.DstActiveUntil", end.ToString("d MMM yyyy", strings.Culture))
                : strings.Translate("WorldClock.DstActiveEndUnknown")
            : strings.Translate("WorldClock.DstInactive");
        DaylightSavingText.Text = summary;
        if (clock.IsDaylightSavingTime && clock.DaylightSavingEndsAt is { } transition)
        {
            summary = strings.Format("WorldClock.DstActiveUntil",
                $"{transition.ToString("f", strings.Culture)} (UTC{FormatOffset(transition.Offset)})");
        }

        ToolTipService.SetToolTip(DaylightSavingText, summary);
        ToolTipService.SetToolTip(LocalTimeText, summary);
        AutomationProperties.SetName(DaylightSavingText, summary);
        return summary;
    }

    private string ApplyMoonPhase(
        WorldClockItem clock,
        LocalizationService strings,
        Brush accent)
    {
        if (clock.IsDaylight)
        {
            MoonPhaseSummaryText.Text = string.Empty;
            MoonPhaseSummaryText.Visibility = Visibility.Collapsed;
            ToolTipService.SetToolTip(MoonPhaseSummaryText, null);
            AutomationProperties.SetName(MoonPhaseSummaryText, string.Empty);
            return string.Empty;
        }

        var presentation = LunarPhaseProjection.Create(clock.MoonPhaseAngleDegrees);
        var phaseName = strings.Translate(presentation.LocalizationKey);
        var summary = strings.Format(
            "WorldClock.MoonPhase.Summary",
            phaseName,
            presentation.IlluminatedPercentage);
        MoonPhaseSummaryText.Text = summary;
        MoonPhaseSummaryText.Foreground = accent;
        MoonPhaseSummaryText.Visibility = Visibility.Visible;
        ToolTipService.SetToolTip(MoonPhaseSummaryText, summary);
        AutomationProperties.SetName(MoonPhaseSummaryText, summary);
        return summary;
    }

    private string ApplySolarTimeline(WorldClockItem clock, LocalizationService strings)
    {
        if (clock.Sunrise is not { } sunrise || clock.Sunset is not { } sunset)
        {
            throw new InvalidDataException("The world-clock solar timeline requires both sunrise and sunset.");
        }

        var daylight = sunset - sunrise;
        if (daylight <= TimeSpan.Zero || daylight > TimeSpan.FromHours(24))
        {
            throw new InvalidDataException("The world-clock daylight duration must be between zero and 24 hours.");
        }

        SolarDaylightFill.Data = BuildSolarFillGeometry();
        SolarDaylightPath.Data = BuildSolarArcGeometry(0d, 1d);

        var rawProgress = (clock.LocalTime - sunrise).TotalSeconds / daylight.TotalSeconds;
        var isInDaylight = rawProgress is >= 0d and <= 1d;
        var progress = Math.Clamp(rawProgress, 0d, 1d);
        SolarElapsedPath.Data = BuildSolarArcGeometry(0d, progress);

        var currentPoint = SolarArcPoint(progress);
        Canvas.SetLeft(SolarCurrentMarker, currentPoint.X - (SolarCurrentMarker.Width / 2d));
        Canvas.SetTop(SolarCurrentMarker, currentPoint.Y - (SolarCurrentMarker.Height / 2d));
        SolarCurrentGuide.X1 = currentPoint.X;
        SolarCurrentGuide.X2 = currentPoint.X;
        SolarCurrentGuide.Y1 = currentPoint.Y + (SolarCurrentMarker.Height / 2d);
        SolarCurrentGuide.Y2 = SolarArcBaselineY;
        SolarCurrentMarker.Visibility = isInDaylight ? Visibility.Visible : Visibility.Collapsed;
        SolarCurrentGuide.Visibility = isInDaylight ? Visibility.Visible : Visibility.Collapsed;
        SolarCurrentTimeText.Text = isInDaylight ? FormatTime(clock.LocalTime, strings) : string.Empty;
        SolarCurrentTimeText.Visibility = isInDaylight ? Visibility.Visible : Visibility.Collapsed;

        var durationText = $"{(int)daylight.TotalHours} h {daylight.Minutes:00} min";
        var summary = strings.Format("WorldClock.DaylightDuration", durationText);
        SolarDaylightDurationText.Text = summary;
        AutomationProperties.SetName(SolarArcPanel, summary);
        return summary;
    }

    private static PathGeometry BuildSolarFillGeometry()
    {
        var figure = new PathFigure
        {
            StartPoint = new Point(SolarArcStartX, SolarArcBaselineY),
            IsClosed = true,
            IsFilled = true
        };
        var arc = new PolyLineSegment();
        for (var segment = 1; segment <= SolarArcSegments; segment++)
        {
            arc.Points.Add(SolarArcPoint((double)segment / SolarArcSegments));
        }

        figure.Segments.Add(arc);
        figure.Segments.Add(new LineSegment { Point = new Point(SolarArcEndX, SolarArcBaselineY) });
        return new PathGeometry { Figures = { figure } };
    }

    private static PathGeometry BuildSolarArcGeometry(double from, double to)
    {
        var figure = new PathFigure
        {
            StartPoint = SolarArcPoint(from),
            IsFilled = false
        };
        var segmentCount = Math.Max(1, (int)Math.Ceiling((to - from) * SolarArcSegments));
        var arc = new PolyLineSegment();
        for (var segment = 1; segment <= segmentCount; segment++)
        {
            arc.Points.Add(SolarArcPoint(from + ((to - from) * segment / segmentCount)));
        }

        figure.Segments.Add(arc);
        return new PathGeometry { Figures = { figure } };
    }

    private static Point SolarArcPoint(double progress)
    {
        var remaining = 1d - progress;
        return new Point(
            (remaining * remaining * SolarArcStartX)
                + (2d * remaining * progress * ((SolarArcStartX + SolarArcEndX) / 2d))
                + (progress * progress * SolarArcEndX),
            (remaining * remaining * SolarArcBaselineY)
                + (2d * remaining * progress * SolarArcControlY)
                + (progress * progress * SolarArcBaselineY));
    }

    private string ApplyWeather(WorldClockWeather? weather, LocalizationService strings)
    {
        if (weather is null || !weather.IsFresh)
        {
            WeatherTemperatureText.Text = "—";
            WeatherConditionText.Text = strings.Translate("WorldClock.WeatherNoData");
            WeatherPanel.Opacity = 0.58d;
            ApplyWeatherIcon(null);
            var unavailableSummary = WeatherConditionText.Text;
            AutomationProperties.SetName(WeatherPanel, unavailableSummary);
            return unavailableSummary;
        }

        var conditionKey = weather.ConditionKey switch
        {
            "clear" => "WorldClock.WeatherCondition.clear",
            "cloudy" => "WorldClock.WeatherCondition.cloudy",
            "rain" => "WorldClock.WeatherCondition.rain",
            "snow" => "WorldClock.WeatherCondition.snow",
            "mixed-precipitation" => "WorldClock.WeatherCondition.mixed-precipitation",
            "fog" => "WorldClock.WeatherCondition.fog",
            "lightning" => "WorldClock.WeatherCondition.lightning",
            "unknown" => "WorldClock.WeatherCondition.unknown",
            _ => throw new InvalidDataException($"Unsupported world-clock weather condition '{weather.ConditionKey}'.")
        };
        WeatherTemperatureText.Text = string.Concat(
            weather.TemperatureCelsius.ToString("0", strings.Culture),
            "°");
        var accessibleTemperature = strings.Format(
            "WorldClock.WeatherTemperature",
            weather.TemperatureCelsius);
        WeatherConditionText.Text = strings.Translate(conditionKey);
        var summary = $"{accessibleTemperature}, {WeatherConditionText.Text}";
        WeatherPanel.Opacity = 1d;
        ApplyWeatherIcon(weather.ConditionKey);
        AutomationProperties.SetName(WeatherPanel, summary);
        return summary;
    }

    private void ApplyWeatherIcon(string? condition)
    {
        WeatherAdornmentHost.Visibility = condition is null ? Visibility.Collapsed : Visibility.Visible;
        WeatherSunIcon.Visibility = condition is "clear" or "cloudy" ? Visibility.Visible : Visibility.Collapsed;
        WeatherCloudIcon.Visibility = condition is "cloudy" or "rain" or "snow" or "mixed-precipitation" or "lightning"
            ? Visibility.Visible
            : Visibility.Collapsed;
        WeatherRainIcon.Visibility = condition is "rain" or "mixed-precipitation" ? Visibility.Visible : Visibility.Collapsed;
        WeatherSnowIcon.Visibility = condition is "snow" or "mixed-precipitation" ? Visibility.Visible : Visibility.Collapsed;
        WeatherFogIcon.Visibility = condition is "fog" ? Visibility.Visible : Visibility.Collapsed;
        WeatherLightningIcon.Visibility = condition is "lightning" ? Visibility.Visible : Visibility.Collapsed;
        WeatherUnknownIcon.Visibility = condition is "unknown" ? Visibility.Visible : Visibility.Collapsed;
    }

    private string ApplySpaceWeatherAlert(SpaceWeatherAlert? alert, LocalizationService strings)
    {
        if (alert is null)
        {
            SpaceWeatherAlertText.Text = string.Empty;
            SpaceWeatherAlertPanel.Visibility = Visibility.Collapsed;
            ToolTipService.SetToolTip(SpaceWeatherAlertPanel, null);
            AutomationProperties.SetName(SpaceWeatherAlertPanel, string.Empty);
            return string.Empty;
        }

        if (!Enum.IsDefined(alert.Kind))
        {
            throw new InvalidDataException($"Unsupported space-weather alert kind '{alert.Kind}'.");
        }

        if (alert.KpIndex is { } kpIndex && !double.IsFinite(kpIndex))
        {
            throw new InvalidDataException("The space-weather Kp index must be finite.");
        }

        var kind = strings.Translate($"WorldClock.SpaceWeather.{alert.Kind}");
        var indicators = new List<string>();
        if (alert.NoaaScale is { } scale)
        {
            indicators.Add(strings.Format("WorldClock.SpaceWeather.Scale", scale));
        }

        if (alert.KpIndex is { } kp)
        {
            indicators.Add(strings.Format("WorldClock.SpaceWeather.Kp", kp));
        }

        var detail = indicators.Count == 0 ? kind : $"{kind} · {string.Join(" · ", indicators)}";
        var validUntil = alert.ValidToUtc is { } end
            ? strings.Format("WorldClock.SpaceWeather.ValidUntil", end.ToLocalTime().ToString("g", strings.Culture))
            : string.Empty;
        var summary = string.IsNullOrWhiteSpace(alert.Summary)
            ? detail
            : $"{detail}. {alert.Summary}";
        if (!string.IsNullOrEmpty(validUntil))
        {
            summary = $"{summary}. {validUntil}";
        }

        SpaceWeatherAlertText.Text = detail;
        SpaceWeatherAlertPanel.Visibility = Visibility.Visible;
        ToolTipService.SetToolTip(SpaceWeatherAlertPanel, summary);
        AutomationProperties.SetName(SpaceWeatherAlertPanel, summary);
        return summary;
    }

    private void ApplySkyline(string assetPath)
    {
        if (string.Equals(_skylineAssetPath, assetPath, StringComparison.Ordinal))
        {
            return;
        }

        SkylineImage.Source = PackagedBitmap(assetPath);
        _skylineAssetPath = assetPath;
    }

    private static string[] ApplyLayers(
        Grid host,
        IReadOnlyList<string> assetPaths,
        IReadOnlyList<string> currentPaths,
        Stretch stretch)
    {
        ArgumentNullException.ThrowIfNull(assetPaths);
        if (assetPaths.SequenceEqual(currentPaths, StringComparer.Ordinal))
        {
            return currentPaths as string[] ?? currentPaths.ToArray();
        }

        host.Children.Clear();
        foreach (var assetPath in assetPaths)
        {
            var image = new Image
            {
                Source = PackagedBitmap(assetPath),
                Stretch = stretch,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Bottom,
                IsHitTestVisible = false
            };
            AutomationProperties.SetAccessibilityView(image, AccessibilityView.Raw);
            host.Children.Add(image);
        }

        return assetPaths.ToArray();
    }

    private static BitmapImage PackagedBitmap(string assetPath)
    {
        if (string.IsNullOrWhiteSpace(assetPath))
        {
            throw new InvalidDataException("World-clock visual layer path is empty.");
        }

        var normalized = assetPath.Replace('\\', '/');
        if (!normalized.StartsWith("Assets/WorldClocks/", StringComparison.Ordinal)
            || normalized.Contains("..", StringComparison.Ordinal)
            || normalized.StartsWith("/", StringComparison.Ordinal)
            || !normalized.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"World-clock visual layer path is invalid: {assetPath}");
        }

        return new BitmapImage(new Uri($"ms-appx:///{normalized}"))
        {
            DecodePixelWidth = WorldClockWindowLayoutState.ScenePixelWidth
        };
    }

    private static string FormatOffset(TimeSpan offset)
    {
        var sign = offset < TimeSpan.Zero ? "−" : "+";
        var absolute = offset.Duration();
        return absolute.Minutes == 0
            ? $"{sign}{absolute.Hours + (absolute.Days * 24)}"
            : $"{sign}{absolute.Hours + (absolute.Days * 24)}:{absolute.Minutes:00}";
    }

    private static string DateRelation(DateTime cityDate, DateTime referenceDate, LocalizationService strings) =>
        (cityDate.Date - referenceDate.Date).Days switch
        {
            -1 => strings.Translate("WorldClock.PreviousDay"),
            0 => string.Empty,
            1 => strings.Translate("WorldClock.NextDay"),
            _ => cityDate.ToString("ddd d MMM", strings.Culture)
        };

    private static string FormatTime(DateTimeOffset? value, LocalizationService strings) =>
        value?.ToString("HH:mm", strings.Culture) ?? strings.Translate("WorldClock.NoEvent");
}

/// <summary>Identifies a city selected from the detached world-clock surface.</summary>
public sealed class WorldClockCityEventArgs(string cityId, string cityName) : EventArgs
{
    /// <summary>Gets the stable packaged city identifier.</summary>
    public string CityId { get; } = cityId;

    /// <summary>Gets the localized catalog city name.</summary>
    public string CityName { get; } = cityName;
}
