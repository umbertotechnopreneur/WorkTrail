// SPDX-License-Identifier: MIT
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using WorkTrail.Application;
using WorkTrail.Services;

namespace WorkTrail.Controls;

/// <summary>Shows one existing city skyline, local clock and current weather observation.</summary>
public sealed class PlannerCityHeader : UserControl
{
    /// <summary>Requests editing the displayed city's working rules.</summary>
    public event Action<AvailabilityPlannerCity>? ScheduleRequested;

    /// <summary>Renders a city header from application DTOs.</summary>
    public void Apply(AvailabilityPlannerCity city, string referenceId, LocalizationService strings)
    {
        var clock = city.Clock;
        var panel = new StackPanel { Spacing = 3, Padding = new Thickness(8, 4, 8, 8) };
        panel.Children.Add(new Image
        {
            Source = new BitmapImage(new Uri("ms-appx:///" + clock.SkylineAssetPath.Replace('\\', '/'))),
            Height = 94,
            Stretch = Stretch.Uniform,
            IsHitTestVisible = false
        });
        panel.Children.Add(Label((clock.CityId == referenceId ? "⌂ " : "") + clock.CityName, 17, true));
        panel.Children.Add(Label($"{clock.CountryCode}  UTC{clock.LocalTime:zzz}", 11));
        panel.Children.Add(Label(clock.LocalTime.ToString("HH:mm", strings.Culture), 23, true));
        panel.Children.Add(Label(clock.LocalTime.ToString("ddd d MMM", strings.Culture), 11));
        var weather = clock.Weather;
        var weatherText = weather is { IsFresh: true }
            ? $"{WeatherGlyph(weather.ConditionKey)}  {weather.TemperatureCelsius.ToString("0", strings.Culture)}°C  {strings.Translate("WorldClock.WeatherCondition." + weather.ConditionKey)}"
            : strings.Translate("WorldClock.WeatherNoData");
        var weatherLabel = Label(weatherText, 12);
        weatherLabel.TextWrapping = TextWrapping.Wrap;
        ToolTipService.SetToolTip(weatherLabel, weather is null ? weatherText
            : strings.Format("Planner.WeatherObserved", weather.ObservedAtUtc.ToString("g", strings.Culture) + " UTC"));
        panel.Children.Add(weatherLabel);
        var hours = $"{city.Schedule.Start:HH:mm}–{city.Schedule.End:HH:mm}";
        var edit = new Button
        {
            Content = hours + "  ⚙",
            HorizontalAlignment = HorizontalAlignment.Center,
            Padding = new Thickness(6, 2, 6, 2),
            FontSize = 12
        };
        UiLocalization.SetAccessibleLabel(edit, strings.Format("Planner.EditCity", clock.CityName));
        edit.Click += (_, _) => ScheduleRequested?.Invoke(city);
        panel.Children.Add(edit);
        Content = new Border
        {
            Child = panel,
            BorderThickness = new Thickness(0, 0, 1, 1),
            BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(35, 128, 128, 128))
        };
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
