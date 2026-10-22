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
    KeyBox.Password = "";
};
    }

    #endregion

    #region Implementation

    /// <summary>Returns to world-clock options after save or cancellation.</summary>
    public event Action<bool>? Dismissed;

    /// <summary>Initializes the weather-only editor without requesting provider data.</summary>
    public Task InitializeAsync(IWorkTrailApplication application, LocalizationService strings, CancellationToken ownerToken)
    {
        _application = application;
        _strings = strings;
        _lifetime = CancellationTokenSource.CreateLinkedTokenSource(ownerToken);
        UiLocalization.Apply(RootPanel, strings);
        UiLocalization.SetAccessibleLabel(KeyBox, strings.Translate("WeatherSetup.Key"));
        return Task.CompletedTask;
    }

    /// <summary>Saves and verifies the entered weather-provider key through the application facade.</summary>
    private async void Save_Click(object sender, RoutedEventArgs args)
    {
        if (_busy || _lifetime is null)
        {
            return;
        }
        _busy = true;
        SaveButton.IsEnabled = BackButton.IsEnabled = false;
        try
        {
            var result = await _application.SetWorldClockWeatherKeyAsync(KeyBox.Password, _lifetime.Token);
            if (_lifetime.IsCancellationRequested)
            {
                return;
            }
            if (result.Succeeded)
            {
                KeyBox.Password = "";
                Dismissed?.Invoke(true);
            }
            else
            {
                StatusBar.Message = _strings.Translate(result.MessageKey);
                StatusBar.IsOpen = true;
            }
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            ApplicationErrorService.Report(exception, "WeatherKey", _strings.RequestedLanguage);
        }
        finally
        {
            _busy = false;
            SaveButton.IsEnabled = BackButton.IsEnabled = true;
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

    /// <summary>Dismisses key setup without reporting a newly verified key.</summary>
    private void Back_Click(object sender, RoutedEventArgs args) => Dismissed?.Invoke(false);

    #endregion
}
