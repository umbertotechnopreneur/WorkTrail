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
    #region Fields

    private const int LogicalWidth = 500;
    private const int LogicalHeight = 340;
    private const int LogicalScreenMargin = 24;
    private readonly TaskCompletionSource<bool> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly IWorkTrailApplication _application;
    private readonly bool _welcome;
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

    #endregion

    #region Implementation

    private bool IsClosing => _isCompleting || _closed;

    #endregion

    #region Initialization

    /// <summary>Initializes the city picker and its guarded modal window lifetime.</summary>
    internal WorldClockCityPickerDialogWindow(
        IWorkTrailApplication application,
        IReadOnlyList<WorldClockCitySummary> cities,
        ElementTheme theme,
        LocalizationService strings,
        AppWindow ownerAppWindow,
        IntPtr ownerHandle,
        ToastNotificationService notifications,
        bool welcome = false,
        string? suggestedCityId = null,
        string? windowsTimeZoneName = null)
    {
        _application = application ?? throw new ArgumentNullException(nameof(application));
        ArgumentNullException.ThrowIfNull(cities);
        _strings = strings ?? throw new ArgumentNullException(nameof(strings));
        ArgumentNullException.ThrowIfNull(ownerAppWindow);
        _notifications = notifications ?? throw new ArgumentNullException(nameof(notifications));
        _welcome = welcome;
        InitializeComponent();
        Title = _strings.Translate(welcome ? "Welcome.Title" : "WorldClock.PickerTitle");
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
        CancelButton.Content = _strings.Translate("Dialog.Cancel");
        AddAnotherButton.Content = _strings.Translate("WorldClock.AddAnother");
        AddButton.Content = _strings.Translate(welcome ? "Welcome.Continue" : "WorldClock.Add");
        if (welcome)
        {
            WelcomeMessageText.Text = _strings.Translate("Welcome.Message");
            WelcomeMessageText.Visibility = Visibility.Visible;
            SuggestionText.Text = suggestedCityId is not null && windowsTimeZoneName is not null
                ? _strings.Format("Welcome.Suggestion", windowsTimeZoneName) : _strings.Translate("Welcome.Manual");
            SuggestionText.Visibility = Visibility.Visible;
            CancelButton.Visibility = Visibility.Collapsed;
            AddAnotherButton.Visibility = Visibility.Collapsed;
        }
        AutomationProperties.SetName(RootGrid, Title);
        AutomationProperties.SetName(DialogTitleText, Title);
        AutomationProperties.SetName(CancelButton, _strings.Translate("Dialog.Cancel"));
        AutomationProperties.SetName(AddAnotherButton, _strings.Translate("WorldClock.AddAnother"));
        AutomationProperties.SetName(AddButton, (string)AddButton.Content);
        CityPicker.ApplyCities(cities, strings, suggestedCityId);
        _appWindow.Closing += AppWindow_Closing;
        Closed += WorldClockCityPickerDialogWindow_Closed;
    }

    #endregion

    #region Implementation

    internal IntPtr WindowHandle => _windowHandle;

    /// <summary>Shows the city picker and awaits its modal completion.</summary>
    internal Task<bool> ShowAsync()
    {
        WindowInteropService.MakeTopmostWithoutActivation(_windowHandle);
        Activate();
        return _completion.Task;
    }

    /// <summary>Releases the dialog's placement controller once its lifetime ends.</summary>
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

    /// <summary>Restores window placement and initializes presentation after the visual tree is ready.</summary>
    private async void RootGrid_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            _placement.ApplyDefaultSize(RootGrid);
            await _placement.RestoreAsync(RootGrid, _lifetimeCancellation.Token);
            if (!IsClosing)
            {
                CityPicker.FocusPicker();
            }
        }
        catch (OperationCanceledException) when (_lifetimeCancellation.IsCancellationRequested)
        {
            // Closing the owner cancels placement restoration; do not touch the detached controls.
        }
    }

    /// <summary>Enables confirmation only for a city supplied by the packaged catalog.</summary>
    private void CityPicker_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateCommandState();

    /// <summary>Adds the selected city and completes the picker when successful.</summary>
    private async void AddButton_Click(object sender, RoutedEventArgs e)
    {
        await AddSelectedCityAsync(closeWhenAdded: true);
    }

    /// <summary>Adds the selected city while keeping the picker available for another choice.</summary>
    private async void AddAnotherButton_Click(object sender, RoutedEventArgs e) =>
        await AddSelectedCityAsync(closeWhenAdded: false);

    /// <summary>Completes the city picker without adding another city.</summary>
    private async void CancelButton_Click(object sender, RoutedEventArgs e) => await CompleteAsync();

    /// <summary>Handles keyboard confirmation and cancellation within the city picker.</summary>
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

    /// <summary>Completes and releases the city picker after native closure.</summary>
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

    /// <summary>Persists the selected city and updates picker feedback before optional completion.</summary>
    private async Task AddSelectedCityAsync(bool closeWhenAdded)
    {
        if (IsClosing || _isAdding || CityPicker.SelectedCityId is not { } cityId)
        {
            return;
        }

        _isAdding = true;
        UpdateCommandState();
        var shouldClose = false;
        try
        {
            // User dismissal is disabled while a mutation is pending; shutdown cancels its lifetime.
            if (_welcome)
            {
                var saved = await _application.PatchSettingsAsync(new SettingsPatch(new Dictionary<string, string?>
                {
                    ["home.city_id"] = cityId
                }), _lifetimeCancellation.Token);
                if (IsClosing)
                {
                    return;
                }
                if (!saved.Succeeded)
                {
                    _notifications.ShowError(PickerNotificationBanner, Title, ResultMessage(saved.MessageKey));
                    return;
                }
                _addedCity = true;
                shouldClose = true;
            }
            else
            {
                var result = await _application.AddWorldClockAsync(cityId, _lifetimeCancellation.Token);
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
                var displayName = CityPicker.SelectedDisplayName;
                CityPicker.RemoveCity(cityId);
                _notifications.ShowSuccess(
                    PickerNotificationBanner,
                    _strings.Translate("WorldClock.AddedTitle"),
                    _strings.Format("WorldClock.AddedMessage", displayName));

                shouldClose = closeWhenAdded;
            }
        }
        catch (OperationCanceledException) when (_lifetimeCancellation.IsCancellationRequested)
        {
            // Shutdown may race the provider response. Never publish feedback into a closed window.
        }
        catch (Exception exception)
        {
            ApplicationErrorService.Record(exception, "CitySelection");
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
                CityPicker.FocusPicker();
            }
        }
    }

    /// <summary>Formats localized feedback for the city-addition result.</summary>
    private string ResultMessage(string messageKey) =>
        _strings.TryTranslate(messageKey, out var message)
            ? message
            : _strings.Translate("WorldClock.CatalogUnavailable");

    /// <summary>Enables picker actions according to selection and pending-operation state.</summary>
    private void UpdateCommandState()
    {
        if (_closed)
        {
            return;
        }

        var canInteract = !IsClosing && !_isAdding;
        CancelButton.IsEnabled = canInteract;
        CityPicker.IsEnabled = canInteract;
        var canAdd = canInteract && CityPicker.SelectedCityId is not null;
        AddButton.IsEnabled = canAdd;
        AddAnotherButton.IsEnabled = canAdd;
    }

    /// <summary>Persists dialog placement before publishing the picker result and closing.</summary>
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

    #endregion
}
