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
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using WorkTrail.Application;
using WorkTrail.Services;

namespace WorkTrail.Controls;

/// <summary>Collects world-clock presentation options and forwards mutations to the shared application facade.</summary>
public sealed partial class WorldClockOptionsControl : UserControl
{
    private IWorkTrailApplication? _application;
    private LocalizationService _strings = new("system");
    private CancellationToken _lifetimeToken;
    private bool _updatingControls;
    private bool _busy;
    private bool _canAddClock;
    private bool _weatherKeyConfigured;
    private int _worldClockOpacityPercent = 100;
    private int _pendingWorldClockOpacityPercent = 100;
    private bool _worldClockShowInTaskbar = true;
    private bool _worldMapShowInTaskbar = true;
    private bool _lunarPhaseShowInTaskbar = true;

    /// <summary>Creates the passive world-clock options surface.</summary>
    public WorldClockOptionsControl() => InitializeComponent();

    /// <summary>Occurs when the selected clocks must be refreshed.</summary>
    public event EventHandler? RefreshRequested;

    /// <summary>Occurs when the city picker should be shown.</summary>
    public event EventHandler? AddRequested;

    /// <summary>Occurs when a city should become the reference city.</summary>
    public event EventHandler<WorldClockCityEventArgs>? ReferenceRequested;

    /// <summary>Occurs when a city should be removed.</summary>
    public event EventHandler<WorldClockCityEventArgs>? RemoveRequested;

    /// <summary>Occurs when a city should move one position toward the start of the list.</summary>
    public event EventHandler<WorldClockCityEventArgs>? MoveUpRequested;

    /// <summary>Occurs when a city should move one position toward the end of the list.</summary>
    public event EventHandler<WorldClockCityEventArgs>? MoveDownRequested;

    /// <summary>Occurs when the native always-on-top presenter state should change.</summary>
    public event Action<bool>? AlwaysOnTopChanged;

    /// <summary>Occurs after the application returns a fully persisted settings snapshot.</summary>
    public event Action<AppSettings>? SettingsSaved;

    /// <summary>Occurs when a non-field-specific failure should use the window's transient banner.</summary>
    public event Action<string>? WarningRequested;

    /// <summary>Occurs when the weather-provider setup link should be opened by the host.</summary>
    public event EventHandler? ProviderLinkRequested;

    /// <summary>Occurs when the guided weather credential sheet should be shown.</summary>
    public event EventHandler? KeySetupRequested;

    /// <summary>Attaches the shared application facade and applies the current localized presentation state.</summary>
    public void Initialize(
        IWorkTrailApplication application,
        AppSettings settings,
        WorldClockSnapshot? snapshot,
        string? referenceCityId,
        bool alwaysOnTop,
        LocalizationService strings,
        CancellationToken lifetimeToken)
    {
        _application = application ?? throw new ArgumentNullException(nameof(application));
        ArgumentNullException.ThrowIfNull(settings);
        _strings = strings ?? throw new ArgumentNullException(nameof(strings));
        AddClockPremiumBadge.Text = _strings.Translate("Premium.Badge");
        _lifetimeToken = lifetimeToken;
        UiLocalization.Apply(this, _strings);
        ApplyLocalizedPresentation();
        ApplyState(settings, snapshot, referenceCityId, alwaysOnTop);
    }

    /// <summary>Refreshes controls from the latest settings, projection, and native presenter state.</summary>
    public void ApplyState(
        AppSettings settings,
        WorldClockSnapshot? snapshot,
        string? referenceCityId,
        bool alwaysOnTop)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _updatingControls = true;
        try
        {
            WeatherEnabledSwitch.IsOn = settings.WorldClockWeatherEnabled;
            _worldClockOpacityPercent = settings.WorldClockWindowOpacityPercent;
            _pendingWorldClockOpacityPercent = _worldClockOpacityPercent;
            _worldClockShowInTaskbar = settings.WorldClockWindowShowInTaskbar;
            _worldMapShowInTaskbar = settings.WorldMapWindowShowInTaskbar;
            _lunarPhaseShowInTaskbar = settings.LunarPhaseWindowShowInTaskbar;
            WorldClockOpacitySlider.Value = _worldClockOpacityPercent;
            WorldClockShowInTaskbarSwitch.IsOn = _worldClockShowInTaskbar;
            WorldMapShowInTaskbarSwitch.IsOn = _worldMapShowInTaskbar;
            LunarPhaseShowInTaskbarSwitch.IsOn = _lunarPhaseShowInTaskbar;
            AlwaysOnTopSwitch.IsOn = alwaysOnTop;
        }
        finally
        {
            _updatingControls = false;
        }

        ApplyWeatherStatus(snapshot?.WeatherStatus);
        RenderCities(snapshot, referenceCityId);
    }

    /// <summary>Reapplies localized text without changing option values.</summary>
    public void ApplyLanguage(LocalizationService strings)
    {
        _strings = strings ?? throw new ArgumentNullException(nameof(strings));
        UiLocalization.Apply(this, _strings);
        ApplyLocalizedPresentation();
    }

    private async void WeatherEnabledSwitch_Toggled(object sender, RoutedEventArgs e)
    {
        if (_updatingControls || _application is null || _busy)
        {
            return;
        }

        var requested = WeatherEnabledSwitch.IsOn;
        SetBusy(true);
        try
        {
            var result = await _application.PatchSettingsAsync(
                new SettingsPatch(new Dictionary<string, string?>
                {
                    ["world_clocks.weather.enabled"] = requested ? "true" : "false"
                }),
                _lifetimeToken);
            if (result.Succeeded && result.Value is not null)
            {
                SettingsSaved?.Invoke(result.Value);
                RefreshRequested?.Invoke(this, EventArgs.Empty);
                return;
            }

            RestoreWeatherToggle(!requested);
            WarningRequested?.Invoke("Options.SaveError");
        }
        catch (OperationCanceledException) when (_lifetimeToken.IsCancellationRequested)
        {
            // Closing the detached surface cancels the optional settings mutation.
        }
        catch (Exception)
        {
            RestoreWeatherToggle(!requested);
            WarningRequested?.Invoke("Options.SaveError");
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void SaveWeatherKeyButton_Click(object sender, RoutedEventArgs e) =>
        KeySetupRequested?.Invoke(this, EventArgs.Empty);

    private void AlwaysOnTopSwitch_Toggled(object sender, RoutedEventArgs e)
    {
        if (!_updatingControls)
        {
            AlwaysOnTopChanged?.Invoke(AlwaysOnTopSwitch.IsOn);
        }
    }

    private async void WorldClockOpacitySlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (_updatingControls)
        {
            return;
        }

        _pendingWorldClockOpacityPercent = (int)Math.Round(e.NewValue);
    }

    private async void WorldClockOpacitySlider_PointerCaptureLost(object sender, PointerRoutedEventArgs e) =>
        await SavePendingWindowOpacityAsync();

    private async void WorldClockOpacitySlider_KeyUp(object sender, KeyRoutedEventArgs e) =>
        await SavePendingWindowOpacityAsync();

    private async Task SavePendingWindowOpacityAsync()
    {
        if (_application is not null && !_busy && _pendingWorldClockOpacityPercent != _worldClockOpacityPercent)
        {
            await SaveWindowPresentationAsync(_pendingWorldClockOpacityPercent, _worldClockShowInTaskbar);
        }
    }

    private async void WorldClockShowInTaskbarSwitch_Toggled(object sender, RoutedEventArgs e)
    {
        if (!_updatingControls && _application is not null && !_busy)
        {
            await SaveWindowPresentationAsync(_worldClockOpacityPercent, WorldClockShowInTaskbarSwitch.IsOn);
        }
    }

    private async void AstronomyShowInTaskbarSwitch_Toggled(object sender, RoutedEventArgs e)
    {
        if (_updatingControls || _application is null || _busy) return;
        var toggle = (ToggleSwitch)sender;
        var key = ReferenceEquals(toggle, WorldMapShowInTaskbarSwitch)
            ? "window.world_map.show_in_taskbar"
            : "window.lunar_phase.show_in_taskbar";
        await SaveWindowPresentationAsync(new SettingsPatch(new Dictionary<string, string?>
        {
            [key] = toggle.IsOn ? "true" : "false"
        }));
    }

    private Task SaveWindowPresentationAsync(int opacityPercent, bool showInTaskbar) =>
        SaveWindowPresentationAsync(new SettingsPatch(new Dictionary<string, string?>
        {
            ["window.world_clocks.opacity_percent"] = opacityPercent.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["window.world_clocks.show_in_taskbar"] = showInTaskbar ? "true" : "false"
        }));

    private async Task SaveWindowPresentationAsync(SettingsPatch patch)
    {
        SetBusy(true);
        try
        {
            var result = await _application!.PatchSettingsAsync(patch, _lifetimeToken);
            if (result.Succeeded && result.Value is not null)
            {
                SettingsSaved?.Invoke(result.Value);
                return;
            }

            RestoreWindowPresentation();
            WarningRequested?.Invoke("Options.SaveError");
        }
        catch (OperationCanceledException) when (_lifetimeToken.IsCancellationRequested)
        {
            // Closing the detached surface cancels the optional settings mutation.
        }
        catch (Exception)
        {
            RestoreWindowPresentation();
            WarningRequested?.Invoke("Options.SaveError");
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void RestoreWindowPresentation()
    {
        _updatingControls = true;
        _pendingWorldClockOpacityPercent = _worldClockOpacityPercent;
        WorldClockOpacitySlider.Value = _worldClockOpacityPercent;
        WorldClockShowInTaskbarSwitch.IsOn = _worldClockShowInTaskbar;
        WorldMapShowInTaskbarSwitch.IsOn = _worldMapShowInTaskbar;
        LunarPhaseShowInTaskbarSwitch.IsOn = _lunarPhaseShowInTaskbar;
        _updatingControls = false;
    }

    private void AddClockButton_Click(object sender, RoutedEventArgs e) => AddRequested?.Invoke(this, EventArgs.Empty);

    private void WeatherProviderLinkButton_Click(object sender, RoutedEventArgs e) =>
        ProviderLinkRequested?.Invoke(this, EventArgs.Empty);

    private void ReferenceRadioButton_Checked(object sender, RoutedEventArgs e)
    {
        if (_updatingControls || sender is not RadioButton { Tag: WorldClockItem clock })
        {
            return;
        }

        ReferenceRequested?.Invoke(this, new WorldClockCityEventArgs(clock.CityId, clock.CityName));
    }

    private void RemoveClockButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: WorldClockItem clock })
        {
            RemoveRequested?.Invoke(this, new WorldClockCityEventArgs(clock.CityId, clock.CityName));
        }
    }

    private void MoveUpClockButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: WorldClockItem clock })
        {
            MoveUpRequested?.Invoke(this, new WorldClockCityEventArgs(clock.CityId, clock.CityName));
        }
    }

    private void MoveDownClockButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: WorldClockItem clock })
        {
            MoveDownRequested?.Invoke(this, new WorldClockCityEventArgs(clock.CityId, clock.CityName));
        }
    }

    // snapshot supplies the current clocks and their tier-dependent limit.
    // referenceCityId identifies the reference clock, if selected.
    private void RenderCities(WorldClockSnapshot? snapshot, string? referenceCityId)
    {
        CitiesHost.Children.Clear();
        if (snapshot is null)
        {
            _canAddClock = false;
            AddClockButton.IsEnabled = false;
            return;
        }

        _updatingControls = true;
        try
        {
            for (var index = 0; index < snapshot.Clocks.Count; index++)
            {
                var clock = snapshot.Clocks[index];
                if (CitiesHost.Children.Count > 0)
                {
                    CitiesHost.Children.Add(new Border
                    {
                        Style = (Style)Resources["WorldClockCitySeparatorStyle"]
                    });
                }

                var row = new Grid
                {
                    MinHeight = 52,
                    ColumnSpacing = 8,
                    Padding = new Thickness(0, 4, 0, 4)
                };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var cityContent = new StackPanel { Spacing = 1 };
                cityContent.Children.Add(new TextBlock
                {
                    Text = clock.CityName,
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    TextTrimming = TextTrimming.CharacterEllipsis
                });
                cityContent.Children.Add(new TextBlock
                {
                    Text = clock.LocalTime.ToString("HH:mm", _strings.Culture),
                    Style = (Style)Resources["WorldClockCityTimeTextStyle"]
                });

                var referenceButton = new RadioButton
                {
                    Content = cityContent,
                    GroupName = "WorldClockReferenceCity",
                    MinHeight = 44,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    HorizontalContentAlignment = HorizontalAlignment.Left,
                    IsChecked = string.Equals(clock.CityId, referenceCityId, StringComparison.Ordinal),
                    Tag = clock
                };
                var referenceName = _strings.Format("WorldClock.SetReference", clock.CityName);
                AutomationProperties.SetName(referenceButton, referenceName);
                ToolTipService.SetToolTip(referenceButton, referenceName);
                referenceButton.Checked += ReferenceRadioButton_Checked;
                row.Children.Add(referenceButton);

                var actions = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 0,
                    VerticalAlignment = VerticalAlignment.Center
                };
                var moveUpButton = CreateCityActionButton(
                    clock,
                    "\uE70E",
                    "WorldClock.MoveUp",
                    index > 0,
                    MoveUpClockButton_Click);
                var moveDownButton = CreateCityActionButton(
                    clock,
                    "\uE70D",
                    "WorldClock.MoveDown",
                    index < snapshot.Clocks.Count - 1,
                    MoveDownClockButton_Click);
                var removeButton = CreateCityActionButton(
                    clock,
                    "\uE74D",
                    "WorldClock.Remove",
                    isEnabled: true,
                    RemoveClockButton_Click);
                actions.Children.Add(moveUpButton);
                actions.Children.Add(moveDownButton);
                actions.Children.Add(removeButton);
                Grid.SetColumn(actions, 1);
                row.Children.Add(actions);
                CitiesHost.Children.Add(row);
            }
        }
        finally
        {
            _updatingControls = false;
        }

        // Free users can reach the shared upgrade prompt at their limit; Premium keeps its capacity limit.
        _canAddClock = snapshot.Clocks.Count < snapshot.MaximumClocks || snapshot.MaximumClocks == 3;
        AddClockButton.IsEnabled = !_busy && _canAddClock;
    }

    private Button CreateCityActionButton(
        WorldClockItem clock,
        string glyph,
        string labelKey,
        bool isEnabled,
        RoutedEventHandler clickHandler)
    {
        var button = new Button
        {
            Width = 40,
            Height = 40,
            Padding = new Thickness(0),
            Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent),
            BorderBrush = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent),
            Content = new FontIcon
            {
                FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Segoe Fluent Icons"),
                FontSize = 15,
                Glyph = glyph
            },
            Tag = clock,
            IsEnabled = isEnabled
        };
        var label = _strings.Format(labelKey, clock.CityName);
        AutomationProperties.SetName(button, label);
        ToolTipService.SetToolTip(button, label);
        button.Click += clickHandler;
        return button;
    }

    private void ApplyWeatherStatus(WorldClockWeatherStatus? status)
    {
        (string Key, string VisualState) presentation = status is null
            ? ("WorldClock.Options.Weather.ApiKeyStatus.Unavailable", "WeatherStatusInformational")
            : (status.State, status.ReasonCode) switch
            {
                ("available", _) => ("WorldClock.Options.Weather.ApiKeyStatus.Ready", "WeatherStatusReady"),
                ("configuration-required", "missing-api-key") =>
                    ("WorldClock.Options.Weather.ApiKeyStatus.Missing", "WeatherStatusNeedsAttention"),
                ("configuration-required", "invalid-api-key") =>
                    ("WorldClock.Options.Weather.ApiKeyStatus.Invalid", "WeatherStatusInvalid"),
                ("not-requested", "no-clocks") when status.IsProviderConfigured =>
                    ("WorldClock.Options.Weather.ApiKeyStatus.Ready", "WeatherStatusReady"),
                ("not-requested", "no-clocks") =>
                    ("WorldClock.Options.Weather.ApiKeyStatus.Missing", "WeatherStatusNeedsAttention"),
                ("disabled", "user-disabled") => ("WorldClock.WeatherStatus.Disabled", "WeatherStatusInformational"),
                ("partial", _) => ("WorldClock.WeatherStatus.Partial", "WeatherStatusNeedsAttention"),
                ("unavailable", _) => ("WorldClock.WeatherStatus.Unavailable", "WeatherStatusInformational"),
                ("not-requested", "explicit-instant") =>
                    ("WorldClock.WeatherStatus.ReferenceInstant", "WeatherStatusInformational"),
                _ => throw new InvalidDataException(
                    $"Unsupported world-clock weather status '{status.State}/{status.ReasonCode}'.")
            };
        WeatherStatusText.Text = T(presentation.Key);
        AutomationProperties.SetName(WeatherStatusText, WeatherStatusText.Text);
        VisualStateManager.GoToState(this, presentation.VisualState, false);
        _weatherKeyConfigured = status?.IsProviderConfigured == true;
        SetSaveWeatherKeyAction(status?.IsProviderConfigured == true
            ? "WorldClock.Options.Weather.KeyAction.Change"
            : "WorldClock.Options.Weather.KeyAction.Set");
    }

    private void RestoreWeatherToggle(bool value)
    {
        _updatingControls = true;
        WeatherEnabledSwitch.IsOn = value;
        _updatingControls = false;
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        BusyIndicator.IsActive = busy;
        BusyIndicator.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        WeatherEnabledSwitch.IsEnabled = !busy;
        WeatherProviderLinkButton.IsEnabled = !busy;
        SaveWeatherKeyButton.IsEnabled = !busy;
        AddClockButton.IsEnabled = !busy && _canAddClock;
        AlwaysOnTopSwitch.IsEnabled = !busy;
        WorldClockOpacitySlider.IsEnabled = !busy;
        WorldClockShowInTaskbarSwitch.IsEnabled = !busy;
        WorldMapShowInTaskbarSwitch.IsEnabled = !busy;
        LunarPhaseShowInTaskbarSwitch.IsEnabled = !busy;
    }

    private void ApplyLocalizedPresentation()
    {
        SetSaveWeatherKeyAction(_weatherKeyConfigured
            ? "WorldClock.Options.Weather.KeyAction.Change"
            : "WorldClock.Options.Weather.KeyAction.Set");
        AutomationProperties.SetName(WeatherProviderLinkButton, WeatherProviderLinkText.Text);
    }

    private void SetSaveWeatherKeyAction(string key)
    {
        var label = T(key);
        SaveWeatherKeyButton.Content = label;
        AutomationProperties.SetName(SaveWeatherKeyButton, label);
    }

    private string T(string key) => _strings.Translate(key);
}
