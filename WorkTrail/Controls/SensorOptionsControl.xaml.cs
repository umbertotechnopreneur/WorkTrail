// SPDX-License-Identifier: MIT

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using WorkTrail.Application;
using WorkTrail.Presentation;
using WorkTrail.Services;

namespace WorkTrail.Controls;

/// <summary>Edits the shared sensor preferences inside their owning window.</summary>
public sealed partial class SensorOptionsControl : UserControl
{
    private IWorkTrailApplication? _application;
    private AppSettings? _settings;
    private LocalizationService _strings = new("system");
    private CancellationToken _lifetime;
    private bool _updating;
    private bool _busy;

    /// <summary>Publishes confirmed settings for every open presentation surface.</summary>
    public event Action<AppSettings>? SettingsSaved;

    /// <summary>Creates the passive sensor settings editor.</summary>
    public SensorOptionsControl() => InitializeComponent();

    /// <summary>Connects the editor to the existing application facade.</summary>
    public void Initialize(IWorkTrailApplication application, AppSettings settings, CancellationToken lifetime)
    {
        _application = application;
        _lifetime = lifetime;
        ApplyState(settings);
    }

    /// <summary>Updates controls without resubmitting externally changed preferences.</summary>
    public void ApplyState(AppSettings settings)
    {
        _settings = settings;
        _strings = new LocalizationService(settings.UiLanguage);
        _updating = true;
        try
        {
            UiLocalization.Apply(this, _strings);
            HardwareSamplingSlider.Header = _strings.Translate("Options.Sensors.Sampling.Header");
            HardwareSensorsEnabledSwitch.IsOn = settings.HardwareSensorsEnabled;
            HardwareAdvancedSwitch.IsOn = settings.HardwareUseAdvancedSensors;
            HardwareSamplingSlider.Value = HardwareSettingsProjection.ProfileIndex(settings.HardwareSamplingProfile);
            foreach (var (control, key) in new (FrameworkElement, string)[]
            {
                (HardwareSensorsEnabledSwitch, "Options.Sensors.Enabled.Header"),
                (HardwareAdvancedSwitch, "Options.Sensors.Advanced.Header"),
                (HardwareSamplingSlider, "Options.Sensors.Sampling.Header"),
                (ActivateAdvancedSensorsButton, "Hardware.Advanced.Action")
            }) AutomationProperties.SetName(control, _strings.Translate(key));
            ToolTipService.SetToolTip(ActivateAdvancedSensorsButton, _strings.Translate("Hardware.Advanced.Action"));
            UpdatePresentation();
        }
        finally { _updating = false; }
    }

    private void UpdatePresentation()
    {
        HardwareSensorsEnabledSwitch.IsEnabled = !_busy;
        var enabled = HardwareSensorsEnabledSwitch.IsOn && !_busy;
        HardwareAdvancedSwitch.IsEnabled = enabled;
        HardwareSamplingSlider.IsEnabled = enabled;
        ActivateAdvancedSensorsButton.IsEnabled = enabled && HardwareAdvancedSwitch.IsOn;
        var profile = HardwareSettingsProjection.Create(HardwareSettingsProjection.ProfileKeyAt(HardwareSamplingSlider.Value), _strings.Culture, _strings.Translate);
        HardwareSamplingSummaryText.Text = $"{profile.Label} · {profile.Summary}";
        ToolTipService.SetToolTip(HardwareSamplingSummaryText, profile.Detail);
        AutomationProperties.SetHelpText(HardwareSamplingSlider, $"{profile.Summary}. {profile.Detail}");
    }

    private async void Enabled_Toggled(object sender, RoutedEventArgs e) => await SaveAsync("sensors.enabled", HardwareSensorsEnabledSwitch.IsOn ? "true" : "false");
    private async void Advanced_Toggled(object sender, RoutedEventArgs e) => await SaveAsync("sensors.advanced", HardwareAdvancedSwitch.IsOn ? "true" : "false");
    private async void Sampling_Changed(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e) =>
        await SaveAsync("sensors.sampling_profile", HardwareSettingsProjection.ProfileKeyAt(e.NewValue));

    private async Task SaveAsync(string key, string value)
    {
        if (_updating || _busy || _application is null || _settings is null || _lifetime.IsCancellationRequested) return;
        _busy = true;
        UpdatePresentation();
        HardwareActivationStatusText.Text = string.Empty;
        try
        {
            var result = await _application.PatchSettingsAsync(new SettingsPatch(new Dictionary<string, string?> { [key] = value }), _lifetime);
            if (_lifetime.IsCancellationRequested) return;
            if (result is { Succeeded: true, Value: { } settings })
            {
                ApplyState(settings);
                SettingsSaved?.Invoke(settings);
            }
            else
            {
                // A failed save restores confirmed preferences and remains visible in the owning surface.
                ApplyState(_settings);
                HardwareActivationStatusText.Text = _strings.Translate("Options.SaveError");
            }
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { /* Closing cancels this editor's pending request. */ }
        catch (Exception)
        {
            ApplyState(_settings);
            HardwareActivationStatusText.Text = _strings.Translate("Options.SaveError");
        }
        finally
        {
            _busy = false;
            if (!_lifetime.IsCancellationRequested) UpdatePresentation();
        }
    }

    private async void ActivateAdvancedSensorsButton_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || _application is null || _settings is not { HardwareSensorsEnabled: true, HardwareUseAdvancedSensors: true }) return;
        _busy = true;
        UpdatePresentation();
        try
        {
            // Only this explicit action requests Windows consent; persisting the preference never elevates.
            var result = await _application.EnableAdvancedHardwareTelemetryAsync(_lifetime);
            if (_lifetime.IsCancellationRequested) return;
            HardwareActivationStatusText.Text = result is { Succeeded: true, Value: { } snapshot }
                ? HardwareSnapshotProjection.Create(snapshot, _strings.Culture, _strings.Translate).DriverStatus
                : _strings.Translate(result.MessageKey);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { /* The window closed during explicit activation. */ }
        catch (Exception)
        {
            // Optional driver failures are visible and never trigger an automatic elevated retry.
            HardwareActivationStatusText.Text = _strings.Translate("HardwareAdvancedFailed");
        }
        finally
        {
            _busy = false;
            if (!_lifetime.IsCancellationRequested) UpdatePresentation();
        }
    }
}
