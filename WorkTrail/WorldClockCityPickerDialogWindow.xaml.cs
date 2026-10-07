// SPDX-License-Identifier: MIT

using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using WorkTrail.Application;
using WorkTrail.Services;
using Windows.System;

namespace WorkTrail;

/// <summary>Collects and applies world-clock city choices through the shared application facade.</summary>
internal sealed partial class WorldClockCityPickerDialogWindow : Window
{
    private const int LogicalWidth = 500;
    private const int LogicalHeight = 560;
    private const int LogicalScreenMargin = 24;
    private readonly TaskCompletionSource<bool> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly IWorkTrailApplication _application;
    private readonly List<WorldClockCityPickerOption> _options;
    private readonly ToastNotificationService _notifications;
    private readonly MicaDialogService _messages = new();
    private readonly LocalizationService _strings;
    private readonly AppWindow _appWindow;
    private readonly CustomTitleBarController _titleBar;
    private readonly WindowPlacementService _placement;
    private readonly IntPtr _windowHandle;
    private readonly CancellationTokenSource _lifetimeCancellation = new();
    private bool _addedCity;
    private bool _isAdding;
    private bool _isCompleting;
    private bool _allowClose;
    private bool _closed;

    private bool IsClosing => _isCompleting || _closed;

    internal WorldClockCityPickerDialogWindow(
        IWorkTrailApplication application,
        IReadOnlyList<WorldClockCitySummary> cities,
        ElementTheme theme,
        LocalizationService strings,
        AppWindow ownerAppWindow,
        IntPtr ownerHandle,
        ToastNotificationService notifications)
    {
        _application = application ?? throw new ArgumentNullException(nameof(application));
        ArgumentNullException.ThrowIfNull(cities);
        _strings = strings ?? throw new ArgumentNullException(nameof(strings));
        ArgumentNullException.ThrowIfNull(ownerAppWindow);
        _notifications = notifications ?? throw new ArgumentNullException(nameof(notifications));
        _options = cities
            .Select(city => new WorldClockCityPickerOption(city.Id, $"{city.Name} · {city.CountryCode}"))
            .OrderBy(option => option.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        InitializeComponent();
        Title = _strings.Translate("WorldClock.PickerTitle");
        RootGrid.RequestedTheme = theme;
        RootGrid.Language = _strings.Language;
        _windowHandle = WinRT.Interop.WindowNative.GetWindowHandle(this);
        _appWindow = AppWindow.GetFromWindowId(Win32Interop.GetWindowIdFromWindow(_windowHandle));
        _titleBar = new CustomTitleBarController(
            this,
            _appWindow,
            RootGrid,
            TitleDragRegion,
            TitleBarLeftInsetColumn,
            TitleBarRightInsetColumn,
            static () => Array.Empty<FrameworkElement>(),
            useTallTitleBar: false);
        _placement = new WindowPlacementService(
            _application,
            this,
            _appWindow,
            WindowStateKeys.WorldClockCityPicker,
            LogicalWidth,
            LogicalHeight,
            LogicalScreenMargin,
            ownerAppWindow.Id,
            deferNativeClose: false);
        WindowInteropService.SetOwner(_windowHandle, ownerHandle);
        if (_appWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsResizable = false;
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
            presenter.SetBorderAndTitleBar(hasBorder: true, hasTitleBar: false);
        }

        DialogTitleText.Text = Title;
        CityComboBox.PlaceholderText = _strings.Translate("WorldClock.SearchCity");
        CancelButton.Content = _strings.Translate("Dialog.Cancel");
        AddAnotherButton.Content = _strings.Translate("WorldClock.AddAnother");
        AddButton.Content = _strings.Translate("WorldClock.Add");
        AutomationProperties.SetName(RootGrid, Title);
        AutomationProperties.SetName(DialogTitleText, Title);
        AutomationProperties.SetName(CityComboBox, _strings.Translate("WorldClock.SearchCity"));
        AutomationProperties.SetName(CancelButton, _strings.Translate("Dialog.Cancel"));
        AutomationProperties.SetName(AddAnotherButton, _strings.Translate("WorldClock.AddAnother"));
        AutomationProperties.SetName(AddButton, _strings.Translate("WorldClock.Add"));
        CityComboBox.ItemsSource = _options;
        _appWindow.Closing += AppWindow_Closing;
        Closed += WorldClockCityPickerDialogWindow_Closed;
    }

    internal IntPtr WindowHandle => _windowHandle;

    internal Task<bool> ShowAsync()
    {
        WindowInteropService.MakeTopmostWithoutActivation(_windowHandle);
        Activate();
        return _completion.Task;
    }

    internal void DisposePlacement() => _placement.Dispose();

    /// <summary>Cancels pending work and closes immediately when the owner or application shuts down.</summary>
    internal void CloseForShutdown()
    {
        if (_closed)
        {
            return;
        }

        _isCompleting = true;
        _allowClose = true;
        _lifetimeCancellation.Cancel();
        Close();
    }

    private async void RootGrid_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            _placement.ApplyDefaultSize(RootGrid);
            await _placement.RestoreAsync(RootGrid, _lifetimeCancellation.Token);
            if (!IsClosing)
            {
                CityComboBox.Focus(FocusState.Programmatic);
            }
        }
        catch (OperationCanceledException) when (_lifetimeCancellation.IsCancellationRequested)
        {
            // Closing the owner cancels placement restoration; do not touch the detached controls.
        }
    }

    /// <summary>Enables confirmation only for a city supplied by the packaged catalog.</summary>
    private void CityComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateCommandState();

    private async void AddButton_Click(object sender, RoutedEventArgs e)
    {
        await AddSelectedCityAsync(closeWhenAdded: true);
    }

    private async void AddAnotherButton_Click(object sender, RoutedEventArgs e) =>
        await AddSelectedCityAsync(closeWhenAdded: false);

    private async void CancelButton_Click(object sender, RoutedEventArgs e) => await CompleteAsync();

    private async void RootGrid_KeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Escape)
        {
            e.Handled = true;
            await CompleteAsync();
        }
    }

    /// <summary>Routes the native close command through the same placement-save path as dialog buttons.</summary>
    private async void AppWindow_Closing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (_allowClose)
        {
            return;
        }

        args.Cancel = true;
        await CompleteAsync();
    }

    private void WorldClockCityPickerDialogWindow_Closed(object sender, WindowEventArgs args)
    {
        _closed = true;
        _messages.CloseActive();
        _lifetimeCancellation.Cancel();
        _appWindow.Closing -= AppWindow_Closing;
        Closed -= WorldClockCityPickerDialogWindow_Closed;
        _titleBar.Dispose();
        _completion.TrySetResult(_addedCity);
        _lifetimeCancellation.Dispose();
    }

    // closeWhenAdded determines whether a successful addition closes the picker.
    private async Task AddSelectedCityAsync(bool closeWhenAdded)
    {
        if (IsClosing || _isAdding || CityComboBox.SelectedItem is not WorldClockCityPickerOption option)
        {
            return;
        }

        _isAdding = true;
        UpdateCommandState();
        var shouldClose = false;
        try
        {
            // User dismissal is disabled while a mutation is pending; shutdown cancels its lifetime.
            var result = await _application.AddWorldClockAsync(option.Id, _lifetimeCancellation.Token);
            if (IsClosing)
            {
                return;
            }

            if (!result.Succeeded || result.Value is null)
            {
                if (result.Code == "feature.clock_limit")
                {
                    await _messages.ShowPremiumUpgradeAsync(_application, this, _strings.Translate, result.MessageKey);
                    return;
                }
                // The picker remains usable after a rejected mutation so the user can select another city.
                _notifications.ShowWarning(
                    PickerNotificationBanner,
                    _strings.Translate("WorldClock.ErrorTitle"),
                    ResultMessage(result.MessageKey));
                return;
            }

            _addedCity = true;
            _options.Remove(option);
            CityComboBox.SelectedItem = null;
            CityComboBox.ItemsSource = null;
            CityComboBox.ItemsSource = _options;
            _notifications.ShowSuccess(
                PickerNotificationBanner,
                _strings.Translate("WorldClock.AddedTitle"),
                _strings.Format("WorldClock.AddedMessage", option.DisplayName));

            shouldClose = closeWhenAdded;
        }
        catch (OperationCanceledException) when (_lifetimeCancellation.IsCancellationRequested)
        {
            // Shutdown may race the provider response. Never publish feedback into a closed window.
        }
        catch (Exception)
        {
            // Late faults are observed, but only a live picker can render an operation failure.
            if (!IsClosing)
            {
                _notifications.ShowError(
                    PickerNotificationBanner,
                    _strings.Translate("WorldClock.ErrorTitle"),
                    _strings.Translate("WorldClock.CatalogUnavailable"));
            }
        }
        finally
        {
            _isAdding = false;
            if (!IsClosing)
            {
                UpdateCommandState();
            }
        }

        if (!IsClosing)
        {
            if (shouldClose)
            {
                await CompleteAsync();
            }
            else
            {
                CityComboBox.Focus(FocusState.Programmatic);
            }
        }
    }

    private string ResultMessage(string messageKey) =>
        _strings.TryTranslate(messageKey, out var message)
            ? message
            : _strings.Translate("WorldClock.CatalogUnavailable");

    private void UpdateCommandState()
    {
        if (_closed)
        {
            return;
        }

        var canInteract = !IsClosing && !_isAdding;
        CancelButton.IsEnabled = canInteract;
        CityComboBox.IsEnabled = canInteract;
        var canAdd = canInteract && CityComboBox.SelectedItem is WorldClockCityPickerOption;
        AddButton.IsEnabled = canAdd;
        AddAnotherButton.IsEnabled = canAdd;
    }

    private async Task CompleteAsync()
    {
        if (IsClosing || _isAdding)
        {
            return;
        }

        _isCompleting = true;
        UpdateCommandState();
        try
        {
            await _placement.SaveForExplicitCloseAsync(_lifetimeCancellation.Token);
            if (!_closed)
            {
                _allowClose = true;
                Close();
            }
        }
        finally
        {
            // A failed save leaves the guarded dialog available for another close attempt.
            _isCompleting = false;
            if (!_closed)
            {
                UpdateCommandState();
            }
        }
    }

    private sealed record WorldClockCityPickerOption(string Id, string DisplayName);
}
