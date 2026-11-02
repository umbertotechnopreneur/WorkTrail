// SPDX-License-Identifier: MIT
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WorkTrail.Application;
using WorkTrail.Services;

namespace WorkTrail.Controls;

/// <summary>Collects and verifies the optional weather credential through the shared facade.</summary>
public sealed partial class WeatherKeySetupControl : UserControl
{
    #region Fields

    private IWorkTrailApplication _application = null!;
    private LocalizationService _strings = null!;
    private CancellationTokenSource? _lifetime;
    private bool _busy;
    private bool _saved;

    #endregion

    #region Initialization

    /// <summary>Creates the weather credential editor.</summary>
    public WeatherKeySetupControl()
    {
        InitializeComponent();
        Unloaded += (_, _) =>
{
    _lifetime?.Cancel();
    _lifetime?.Dispose();
    _lifetime = null;
    KeyBox.Password = "";
};
    }

    #endregion

    #region Implementation

    /// <summary>Returns to world-clock options after save or cancellation.</summary>
    public event Action<bool>? Dismissed;

    /// <summary>Publishes a verified and persisted key so open widgets can refresh immediately.</summary>
    public event Action? Saved;

    /// <summary>Initializes the weather-only editor without requesting provider data.</summary>
    public Task InitializeAsync(IWorkTrailApplication application, LocalizationService strings, CancellationToken ownerToken)
    {
        _application = application;
        _lifetime = CancellationTokenSource.CreateLinkedTokenSource(ownerToken);
        ApplyLanguage(strings);
        return Task.CompletedTask;
    }

    /// <summary>Refreshes the editor labels when the global language changes.</summary>
    public void ApplyLanguage(LocalizationService strings)
    {
        _strings = strings;
        UiLocalization.Apply(RootPanel, strings);
        KeyBox.Header = strings.Translate("WeatherSetup.Key");
        UiLocalization.SetAccessibleLabel(KeyBox, strings.Translate("WeatherSetup.Key"));
    }

    /// <summary>Saves and verifies the entered weather-provider key through the application facade.</summary>
    private async void Save_Click(object sender, RoutedEventArgs args)
    {
        if (_busy || _lifetime is null)
        {
            return;
        }
        _busy = true;
        var lifetime = _lifetime;
        SaveButton.IsEnabled = BackButton.IsEnabled = KeyBox.IsEnabled = false;
        StatusBar.IsOpen = false;
        ProgressPanel.Visibility = Visibility.Visible;
        VerificationProgress.IsActive = true;
        try
        {
            var result = await _application.SetWorldClockWeatherKeyAsync(KeyBox.Password, lifetime.Token);
            if (lifetime.IsCancellationRequested)
            {
                return;
            }
            if (result.Succeeded)
            {
                KeyBox.Password = "";
                _saved = true;
                StatusBar.Severity = InfoBarSeverity.Success;
                StatusBar.Message = _strings.Translate(result.MessageKey);
                StatusBar.IsOpen = true;
                Saved?.Invoke();
            }
            else
            {
                StatusBar.Severity = InfoBarSeverity.Error;
                StatusBar.Message = _strings.Translate(result.MessageKey);
                StatusBar.IsOpen = true;
            }
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            StatusBar.Severity = InfoBarSeverity.Error;
            StatusBar.Message = _strings.Translate("WeatherSetup.SaveFailed");
            StatusBar.IsOpen = true;
            ApplicationErrorService.Report(exception, "WeatherKey", _strings.RequestedLanguage);
        }
        finally
        {
            _busy = false;
            VerificationProgress.IsActive = false;
            ProgressPanel.Visibility = Visibility.Collapsed;
            SaveButton.IsEnabled = BackButton.IsEnabled = KeyBox.IsEnabled = true;
        }
    }

    /// <summary>Opens weather-provider setup guidance through the application facade.</summary>
    private async void Help_Click(object sender, RoutedEventArgs args)
    {
        try
        {
            var result = await _application.OpenProductLinkAsync("openweather-keys", _lifetime?.Token ?? CancellationToken.None);
            if (!result.Succeeded)
            {
                StatusBar.Severity = InfoBarSeverity.Error;
                StatusBar.Message = _strings.Translate(result.MessageKey);
                StatusBar.IsOpen = true;
            }
        }
        catch (OperationCanceledException) when (_lifetime?.IsCancellationRequested == true)
        {
        }
        catch (Exception exception)
        {
            ApplicationErrorService.Report(exception, "WeatherHelp", _strings.RequestedLanguage);
        }
    }

    /// <summary>Closes the editor after the user has had time to read the result.</summary>
    private void Back_Click(object sender, RoutedEventArgs args) => Dismissed?.Invoke(_saved);

    #endregion
}
