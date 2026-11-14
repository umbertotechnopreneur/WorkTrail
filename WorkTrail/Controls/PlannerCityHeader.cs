// SPDX-License-Identifier: MIT
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using WorkTrail.Application;
using WorkTrail.Services;
using Windows.System;

namespace WorkTrail.Controls;

/// <summary>Shows one existing city skyline, local clock and current weather observation.</summary>
public sealed class PlannerCityHeader : UserControl
{
    private readonly StackPanel _panel = new() { Spacing = 3, Padding = new Thickness(8, 4, 16, 8) };
    private readonly HorizontalResizeGrip _resize = new()
    {
        Width = 16,
        HorizontalAlignment = HorizontalAlignment.Right,
        VerticalAlignment = VerticalAlignment.Stretch,
        IsTabStop = true,
        UseSystemFocusVisuals = true,
        Background = new SolidColorBrush(Windows.UI.Color.FromArgb(0, 0, 0, 0))
    };

    /// <summary>Gets the identity retained while the header's data refreshes.</summary>
    public string CityId { get; private set; } = "";

    /// <summary>Requests movement of this column's right edge in logical pixels.</summary>
    public event Action<double>? ResizeDelta;

    /// <summary>Requests saving the shared width after a pointer or keyboard adjustment.</summary>
    public event Action? ResizeCompleted;

    /// <summary>Requests automatic fitting of all columns to the viewport.</summary>
    public event Action? FitWidthRequested;

    /// <summary>Requests editing the displayed city's working rules.</summary>
    public event Action<AvailabilityPlannerCity>? ScheduleRequested;

    /// <summary>Creates a persistent resize handle that survives clock and weather refreshes.</summary>
    public PlannerCityHeader()
    {
        var root = new Grid();
        root.Children.Add(new Border
        {
            Child = _panel,
            BorderThickness = new Thickness(0, 0, 1, 1),
            BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(35, 128, 128, 128))
        });
        root.Children.Add(_resize);
        var grip = new Border
        {
            Width = 3,
            Height = 30,
            Margin = new Thickness(0, 0, 6, 0),
            CornerRadius = new CornerRadius(1.5),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            Background = (Brush)Microsoft.UI.Xaml.Application.Current.Resources["TextFillColorSecondaryBrush"],
            Opacity = 0.65,
            IsHitTestVisible = false
        };
        root.Children.Add(grip);
        _resize.DragDelta += (_, args) => ResizeDelta?.Invoke(args.HorizontalChange);
        _resize.DragCompleted += (_, _) => ResizeCompleted?.Invoke();
        _resize.DoubleTapped += (_, args) => { FitWidthRequested?.Invoke(); args.Handled = true; };
        _resize.KeyDown += (_, args) =>
        {
            if (args.Key is VirtualKey.Left or VirtualKey.Right)
            {
                ResizeDelta?.Invoke(args.Key == VirtualKey.Left ? -12 : 12);
                ResizeCompleted?.Invoke();
                args.Handled = true;
            }
            else if (args.Key == VirtualKey.End)
            {
                FitWidthRequested?.Invoke();
                args.Handled = true;
            }
        };
        Content = root;
    }

    /// <summary>Renders a city header from application DTOs.</summary>
    public void Apply(AvailabilityPlannerCity city, IReadOnlyList<AvailabilityPlannerCity> members, string referenceId, LocalizationService strings, bool compact)
    {
        var clock = city.Clock;
        CityId = clock.CityId;
        AutomationProperties.SetAutomationId(_resize, "Planner.ResizeColumns." + clock.CityId);
        UiLocalization.SetAccessibleLabel(_resize, clock.CityName + ". " + strings.Translate("Planner.ResizeColumns"));
        var panel = _panel;
        panel.SizeChanged -= OnCityPanelSizeChanged;
        panel.Children.Clear();
        var cityPanel = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
        panel.Children.Add(cityPanel);
        panel.SizeChanged += OnCityPanelSizeChanged;
        foreach (var member in members)
        {
            var cityItem = new StackPanel { Width = members.Count > 1 ? 150 : double.NaN, Spacing = 3 };
            cityPanel.Children.Add(cityItem);
            if (!compact)
            {
                cityItem.Children.Add(new Image
                {
                    Source = new BitmapImage(new Uri("ms-appx:///" + member.Clock.SkylineAssetPath.Replace('\\', '/'))),
                    Height = members.Count > 1 ? 48 : 94,
                    Stretch = Stretch.Uniform,
                    IsHitTestVisible = false
                });
            }
            var cityName = Label((member.Clock.CityId == referenceId ? "⌂ " : "") + member.Clock.CityName, compact ? 15 : 18, true);
            cityName.TextWrapping = TextWrapping.Wrap;
            cityItem.Children.Add(cityName);
        }
        if (!compact) panel.Children.Add(Label($"{string.Join(" · ", members.Select(member => member.Clock.CountryCode).Distinct())}  UTC{clock.LocalTime:zzz}", 12));
        panel.Children.Add(Label(clock.LocalTime.ToString("HH:mm", strings.Culture), compact ? 18 : 25, true));
        var localDate = Label(clock.LocalTime.ToString("ddd d MMM", strings.Culture), 13);
        if (city.IsNonWorkingDay) localDate.Foreground = (Brush)Microsoft.UI.Xaml.Application.Current.Resources["SystemFillColorCriticalBrush"];
        panel.Children.Add(localDate);
        if (compact) return;
        foreach (var member in members)
        {
            var weather = member.Clock.Weather;
            var weatherText = weather is { IsFresh: true }
                ? $"{WeatherGlyph(weather.ConditionKey)}  {weather.TemperatureCelsius.ToString("0", strings.Culture)}°C  {strings.Translate("WorldClock.WeatherCondition." + weather.ConditionKey)}"
                : strings.Translate("WorldClock.WeatherNoData");
            if (members.Count > 1) weatherText = member.Clock.CityName + " · " + weatherText;
            var weatherLabel = Label(weatherText, 13);
            weatherLabel.TextWrapping = TextWrapping.Wrap;
            ToolTipService.SetToolTip(weatherLabel, weather is null ? weatherText
                : strings.Format("Planner.WeatherObserved", weather.ObservedAtUtc.ToString("g", strings.Culture) + " UTC"));
            panel.Children.Add(weatherLabel);
            var hours = $"{member.Schedule.Start:HH:mm}–{member.Schedule.End:HH:mm}";
            var edit = new Button
            {
                Content = (members.Count > 1 ? member.Clock.CityName + " " : "") + hours + "  ⚙",
                HorizontalAlignment = HorizontalAlignment.Center,
                Padding = new Thickness(6, 2, 6, 2),
                FontSize = 13
            };
            UiLocalization.SetAccessibleLabel(edit, strings.Format("Planner.EditCity", member.Clock.CityName));
            edit.Click += (_, _) => ScheduleRequested?.Invoke(member);
            panel.Children.Add(edit);
        }
    }

    private void OnCityPanelSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (_panel.Children.FirstOrDefault() is StackPanel cities)
            cities.Orientation = cities.Children.Count > 1 && e.NewSize.Width >= cities.Children.Count * 150 + _panel.Padding.Left + _panel.Padding.Right
                ? Orientation.Horizontal : Orientation.Vertical;
    }

    private static TextBlock Label(string text, double size, bool strong = false) => new()
    {
        Text = text,
        FontSize = size,
        TextAlignment = TextAlignment.Center,
        FontWeight = strong ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal,
        TextTrimming = TextTrimming.CharacterEllipsis
    };

    private static string WeatherGlyph(string condition) => condition switch
    { "clear" => "☀", "cloudy" => "☁", "rain" => "🌧", "snow" => "❄", "lightning" => "⛈", "fog" => "≋", "mixed-precipitation" => "🌨", _ => "?" };
}
