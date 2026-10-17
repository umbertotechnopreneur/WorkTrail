// SPDX-License-Identifier: MIT
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using WorkTrail.Application;
using WorkTrail.Presentation;
using WorkTrail.Services;

namespace WorkTrail;

public sealed partial class MainWindow
{
    private DispatcherQueueTimer? _worldTimeRefreshTimer;
    private CancellationTokenSource? _worldTimePreviewCancellation;

    private void InitializeWorldTimeMenu()
    {
        _worldTimeRefreshTimer = DispatcherQueue.CreateTimer();
        _worldTimeRefreshTimer.IsRepeating = false;
        _worldTimeRefreshTimer.Tick += WorldTimeRefreshTimer_Tick;
    }

    private async void WorldTimeFlyout_Opening(object sender, object args)
    {
        CloseWorldTimeMenu();
        var cancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifecycle.Token);
        _worldTimePreviewCancellation = cancellation;
        WorldTimeMenuContent.RequestedTheme = RootGrid.ActualTheme;
        UiLocalization.Apply(WorldTimeMenuContent, _strings);
        var scale = RootGrid.XamlRoot?.RasterizationScale ?? 1d;
        WorldTimeMenuContent.Width = Math.Clamp(_currentWorkArea.Width / scale - 80d, 220d, 340d);
        WorldTimeMenuScroller.MaxHeight = Math.Clamp(_currentWorkArea.Height / scale - 100d, 160d, 600d);
        WorldTimePreview.ItemsSource = null;
        WorldTimePreviewStatus.Text = T("WorldClock.Loading");
        WorldTimePreviewStatus.Visibility = Visibility.Visible;
        await RefreshWorldTimePreviewAsync(cancellation);
    }

    private void WorldTimeFlyout_Closed(object sender, object args) => CloseWorldTimeMenu();

    private void CloseWorldTimeMenu()
    {
        _worldTimeRefreshTimer?.Stop();
        var cancellation = _worldTimePreviewCancellation;
        _worldTimePreviewCancellation = null;
        cancellation?.Cancel();
        cancellation?.Dispose();
    }

    private async void WorldTimeRefreshTimer_Tick(DispatcherQueueTimer sender, object args)
    {
        if (!WorldTimeFlyout.IsOpen)
        {
            CloseWorldTimeMenu();
            return;
        }

        if (_worldTimePreviewCancellation is { } cancellation)
        {
            await RefreshWorldTimePreviewAsync(cancellation);
        }
    }

    private async Task RefreshWorldTimePreviewAsync(CancellationTokenSource cancellation)
    {
        try
        {
            // The shared facade projects saved cities locally; opening a menu never requests weather or AI.
            var result = await _application.GetCelestialReferenceAsync(cancellation.Token);
            if (!IsCurrentWorldTimePreview(cancellation)) return;
            if (!result.Succeeded || result.Value is null)
            {
                WorldTimePreview.ItemsSource = null;
                WorldTimePreviewStatus.Text = T(result.MessageKey);
                WorldTimePreviewStatus.Visibility = Visibility.Visible;
                return;
            }

            WorldTimePreview.ItemsSource = result.Value.Clocks.Take(3).Select(clock => new WorldTimePreviewRow(
                clock.CityName,
                clock.LocalTime.ToString("ddd d MMM", _strings.Culture),
                clock.LocalTime.ToString("t", _strings.Culture),
                clock.IsDaylight ? "\uE706" : "\uE708")).ToArray();
            WorldTimePreviewStatus.Text = T("WorldTime.Empty");
            WorldTimePreviewStatus.Visibility = result.Value.Clocks.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            if (result.Value.Clocks.Count > 0 && _worldTimeRefreshTimer is { } timer)
            {
                timer.Interval = WorldClockWindowLayoutState.DelayUntilNextMinute(result.Value.InstantUtc, DateTimeOffset.UtcNow);
                timer.Start();
            }
        }
        catch (OperationCanceledException) when (!IsCurrentWorldTimePreview(cancellation))
        {
            // A closed flyout or window must never publish a late preview.
        }
        catch (Exception exception)
        {
            if (!IsCurrentWorldTimePreview(cancellation)) return;
            WorldTimePreview.ItemsSource = null;
            WorldTimePreviewStatus.Text = T("WorldClock.CatalogUnavailable");
            WorldTimePreviewStatus.Visibility = Visibility.Visible;
            ApplicationErrorService.Report(exception, "WorldTimePreview", _strings.RequestedLanguage);
        }
    }

    private bool IsCurrentWorldTimePreview(CancellationTokenSource cancellation) =>
        !_dashboardSurfaceClosed && ReferenceEquals(_worldTimePreviewCancellation, cancellation);

    private void WorldTimeClocksButton_Click(object sender, RoutedEventArgs args)
    {
        WorldTimeFlyout.Hide();
        WorldClocksRequested?.Invoke(this, EventArgs.Empty);
    }

    private void WorldTimeMapButton_Click(object sender, RoutedEventArgs args) => OpenWorldTimeWindow(WindowStateKeys.WorldMap);

    private void WorldTimeCalendarButton_Click(object sender, RoutedEventArgs args) => OpenWorldTimeWindow(WindowStateKeys.AstronomyAgenda);

    private void WorldTimeSkyButton_Click(object sender, RoutedEventArgs args) => OpenWorldTimeWindow(WindowStateKeys.LocalSky);

    private void WorldTimeSunButton_Click(object sender, RoutedEventArgs args) => OpenWorldTimeWindow(WindowStateKeys.CelestialMap);

    private void WorldTimeMoonButton_Click(object sender, RoutedEventArgs args) => OpenWorldTimeWindow(WindowStateKeys.LunarPhase);

    private void OpenWorldTimeWindow(string key)
    {
        WorldTimeFlyout.Hide();
        WorldTimeWindowRequested?.Invoke(key);
    }
}

/// <summary>Contains only localized display text for a compact clock preview.</summary>
public sealed record WorldTimePreviewRow(string City, string Date, string Time, string Glyph);
