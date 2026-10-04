// SPDX-License-Identifier: MIT

using Microsoft.UI;
using Microsoft.UI.Input;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using System.Globalization;
using WorkTrail.Application;
using WorkTrail.Presentation;
using WorkTrail.Services;
using Windows.Storage.Pickers;
using Windows.System;

namespace WorkTrail;

/// <summary>Renders the retained screenshot gallery and forwards date/navigation intent to the shared facade.</summary>
public sealed partial class ScreenshotWindow : Window
{
    private const int LogicalWindowWidth = 1180;
    private const int LogicalWindowHeight = 820;
    private const int LogicalScreenMargin = 24;
    private const double DefaultDetailsPaneWidth = 360d;
    private const double MinimumDetailsPaneWidth = 300d;
    private const double MaximumDetailsPaneWidthRatio = 0.5d;
    private const double DetailsPaneKeyboardResizeStep = 16d;
    private const string DefaultAiDescriptionEmptyMessageKey = "Screenshots.AiDescription.Empty";
    private const string DailyAiLimitEmptyMessageKey = "Notification.AiDailyLimitReached.Message";

    private readonly IWorkTrailApplication _application;
    private readonly AppWindow _appWindow;
    private readonly CustomTitleBarController _titleBar;
    private readonly MicaDialogService _dialogs;
    private readonly WindowPlacementService _placement;
    private readonly CancellationTokenSource _lifetimeCancellation = new();
    private readonly string? _launchTheme;
    private CancellationTokenSource? _galleryCancellation;
    private IReadOnlyList<ScreenshotGalleryItem> _items = Array.Empty<ScreenshotGalleryItem>();
    private DateOnly _selectedDate = DateOnly.FromDateTime(DateTime.Today);
    private LocalizationService _strings = new("system");
    private string _theme = "system";
    private string _aiDescriptionEmptyMessageKey = DefaultAiDescriptionEmptyMessageKey;
    private int? _privacyRuleCount;
    private ScreenshotDetailsViewState? _selectedDetailsState;
    private OcrTextWindow? _ocrTextWindow;
    private string? _ocrTextScreenshotPath;
    private readonly OcrTextWindowSource? _restoreOcrSource;
    private bool _openingOcrTextWindow;
    private bool _initialized;
    private bool _settingSelectedDate;
    private bool _detailsPaneOpenPreference;
    private bool _isSavingDetailsPanePreference;
    private bool _deleteOperationInProgress;
    private uint? _detailsResizePointerId;
    private double _detailsResizeStartPointerX;
    private double _detailsResizeStartWidth;
    private string? _requestedScreenshotPath;
    private DateOnly? _requestedDate;

    private CalendarDatePicker SelectedDatePicker => HeaderSection.DatePicker;

    private TextBlock GalleryCountText => HeaderSection.CountText;

    private TextBlock ExtendedDateText => HeaderSection.DisplayDateText;

    private Controls.ScreenshotImageViewerControl ScreenshotViewer => GallerySection.Viewer;

    private Grid EmptyGalleryPanel => GallerySection.EmptyPanel;

    private TextBlock EmptyGalleryText => GallerySection.EmptyText;

    private ProgressRing GalleryProgressRing => GallerySection.LoadingRing;

    private StackPanel FilmstripStrip => TimelineSection.TimelineRoot;

    private ListView FilmstripList => TimelineSection.ItemsView;

    private Button FilmstripToggleButton => TimelineSection.ToggleButton;

    private FontIcon FilmstripChevronIcon => TimelineSection.ToggleChevronIcon;

    /// <summary>Creates the translucent screenshot inspector backed by the shared application facade.</summary>
    internal ScreenshotWindow(
        IWorkTrailApplication application,
        MicaDialogService dialogs,
        string? launchTheme = null,
        string? requestedScreenshotPath = null,
        DateTimeOffset? requestedCapturedAt = null,
        DateOnly? requestedDate = null,
        bool restoreOcrWindow = false)
    {
        if ((requestedScreenshotPath is null) != (requestedCapturedAt is null))
        {
            throw new ArgumentException("A targeted screenshot requires both its path and capture timestamp.");
        }

        if (requestedScreenshotPath is not null && string.IsNullOrWhiteSpace(requestedScreenshotPath))
        {
            throw new ArgumentException("The targeted screenshot path cannot be empty.", nameof(requestedScreenshotPath));
        }

        if (requestedDate is not null && requestedScreenshotPath is not null)
        {
            throw new ArgumentException("A gallery request cannot target both a day and a screenshot.");
        }

        if (restoreOcrWindow && requestedScreenshotPath is null)
        {
            throw new ArgumentException("Restoring the OCR text window requires its screenshot source.", nameof(restoreOcrWindow));
        }

        _application = application;
        _dialogs = dialogs ?? throw new ArgumentNullException(nameof(dialogs));
        _launchTheme = launchTheme;
        _requestedScreenshotPath = requestedScreenshotPath;
        _requestedDate = requestedDate;
        _restoreOcrSource = restoreOcrWindow
            ? new OcrTextWindowSource(requestedScreenshotPath!, requestedCapturedAt!.Value)
            : null;
        if (requestedCapturedAt is { } capturedAt)
        {
            _selectedDate = DateOnly.FromDateTime(capturedAt.ToLocalTime().DateTime);
        }
        else if (requestedDate is { } date)
        {
            _selectedDate = date;
        }

        InitializeComponent();
        ScreenshotViewer.Configure(_application, _lifetimeCancellation.Token);
        TimelineSection.Configure(_application, _lifetimeCancellation.Token);
        WireViewEvents();
        _appWindow = AppWindow.GetFromWindowId(Win32Interop.GetWindowIdFromWindow(WinRT.Interop.WindowNative.GetWindowHandle(this)));
        _titleBar = new CustomTitleBarController(
            this,
            _appWindow,
            RootGrid,
            TitleBarDragRegion,
            TitleBarLeftInsetColumn,
            TitleBarRightInsetColumn,
            () => []);
        _placement = new WindowPlacementService(_application, this, _appWindow, WindowStateKeys.Screenshots, LogicalWindowWidth, LogicalWindowHeight, LogicalScreenMargin);
        SetSelectedDate(_selectedDate);
        ApplyTheme(_theme);
        ApplyLocalization();
        _placement.ApplyDefaultBounds(RootGrid);
        Closed += ScreenshotWindow_Closed;
    }

    private void WireViewEvents()
    {
        SelectedDatePicker.DateChanged += SelectedDatePicker_DateChanged;
        HeaderSection.ZoomOutRequested += HeaderSection_ZoomOutRequested;
        HeaderSection.ZoomResetRequested += HeaderSection_ZoomResetRequested;
        HeaderSection.ZoomInRequested += HeaderSection_ZoomInRequested;
        HeaderSection.SaveRequested += HeaderSection_SaveRequested;
        HeaderSection.ShareRequested += HeaderSection_ShareRequested;
        HeaderSection.OpenFolderRequested += HeaderSection_OpenFolderRequested;
        HeaderSection.DeleteScreenshotRequested += HeaderSection_DeleteScreenshotRequested;
        HeaderSection.DeleteAnalysisRequested += HeaderSection_DeleteAnalysisRequested;
        HeaderSection.DetailsVisibilityRequested += HeaderSection_DetailsVisibilityRequested;
        ScreenshotViewer.ZoomStateChanged += ScreenshotViewer_ZoomStateChanged;
        ScreenshotViewer.ImageLoadFailed += ScreenshotViewer_ImageLoadFailed;
        DetailsSection.OcrTextRequested += DetailsSection_OcrTextRequested;
        TimelineSection.SelectedIndexChanged += TimelineSection_SelectedIndexChanged;
        DayOverviewSection.SelectedIndexChanged += DayOverviewSection_SelectedIndexChanged;
        FilmstripToggleButton.Click += FilmstripToggleButton_Click;
    }

    /// <summary>Selects a retained capture when an already-open inspector is reused.</summary>
    public async Task FocusScreenshotAsync(string screenshotPath, DateTimeOffset capturedAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(screenshotPath);
        _requestedScreenshotPath = screenshotPath;
        _requestedDate = null;
        var selectedDate = DateOnly.FromDateTime(capturedAt.ToLocalTime().DateTime);
        SetSelectedDate(selectedDate);
        if (_initialized)
        {
            await RefreshPrivacyStatusAsync(_lifetimeCancellation.Token);
            await LoadGalleryAsync(selectedDate);
        }
    }

    /// <summary>Reselects the gallery to the most recent retained capture when the menu reopens an existing window.</summary>
    public async Task FocusLatestAsync()
    {
        _requestedScreenshotPath = null;
        _requestedDate = null;
        if (_initialized)
        {
            await RefreshPrivacyStatusAsync(_lifetimeCancellation.Token);
            await LoadGalleryAsync(null);
        }
    }

    /// <summary>Selects a day when an already-open screenshot inspector is reused.</summary>
    public async Task FocusDateAsync(DateOnly date)
    {
        _requestedScreenshotPath = null;
        _requestedDate = date;
        SetSelectedDate(date);
        if (_initialized)
        {
            await RefreshPrivacyStatusAsync(_lifetimeCancellation.Token);
            await LoadGalleryAsync(date);
        }
    }

    private async void RootGrid_Loaded(object sender, RoutedEventArgs e)
    {
        _placement.ApplyDefaultBounds(RootGrid);
        await _placement.RestoreOrCenterAsync(RootGrid, _lifetimeCancellation.Token);

        if (_initialized)
        {
            return;
        }

        _initialized = true;
        await InitializeAsync();
    }

    private async Task InitializeAsync()
    {
        try
        {
            var result = _launchTheme is null
                ? await _application.GetSettingsAsync(_lifetimeCancellation.Token)
                : await _application.PatchSettingsAsync(
                    new SettingsPatch(new Dictionary<string, string?> { ["theme"] = _launchTheme }),
                    _lifetimeCancellation.Token);
            if (result.Succeeded && result.Value is not null)
            {
                _theme = result.Value.Theme;
                _detailsPaneOpenPreference = result.Value.ScreenshotDetailsPaneOpen;
                _strings = new LocalizationService(result.Value.UiLanguage);
                ApplyTheme(_theme);
                ApplyLocalization();
            }
        }
        catch (OperationCanceledException) when (_lifetimeCancellation.IsCancellationRequested)
        {
            return;
        }
        catch (Exception)
        {
            // The gallery remains usable with the system theme and default strings if optional settings cannot be read.
            _strings = new LocalizationService("system");
            ApplyLocalization();
        }

        await RefreshPrivacyStatusAsync(_lifetimeCancellation.Token);
        var loaded = await LoadGalleryAsync(_requestedScreenshotPath is null && _requestedDate is null ? null : _selectedDate);
        if (loaded && _restoreOcrSource is { } source && !_lifetimeCancellation.IsCancellationRequested)
        {
            await RestoreOcrTextWindowAsync(source);
        }
    }

    private async Task RefreshPrivacyStatusAsync(CancellationToken cancellationToken)
    {
        _privacyRuleCount = null;
        try
        {
            var result = await _application.GetPrivacyRulesAsync(cancellationToken);
            if (result.Succeeded && result.Value is not null)
            {
                _privacyRuleCount = result.Value.Count;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            // Privacy status is supplementary inspector context; an unavailable query stays visibly unknown.
            _privacyRuleCount = null;
        }

        ApplyPrivacyStatus();
    }

    private async void SelectedDatePicker_DateChanged(CalendarDatePicker sender, CalendarDatePickerDateChangedEventArgs args)
    {
        if (_settingSelectedDate || !_initialized || args.NewDate is not { } newDate)
        {
            return;
        }

        _selectedDate = DateOnly.FromDateTime(newDate.DateTime);
        await LoadGalleryAsync(_selectedDate);
    }

    private async Task<bool> LoadGalleryAsync(DateOnly? date)
    {
        _galleryCancellation?.Cancel();
        _galleryCancellation?.Dispose();
        _galleryCancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetimeCancellation.Token);
        var cancellationToken = _galleryCancellation.Token;
        SetLoading(true);

        try
        {
            var result = date is { } selectedDate
                ? await _application.GetScreenshotGalleryAsync(selectedDate, cancellationToken)
                : await _application.GetLatestScreenshotGalleryAsync(cancellationToken);
            if (cancellationToken.IsCancellationRequested)
            {
                return false;
            }

            if (!result.Succeeded || result.Value is null)
            {
                _items = Array.Empty<ScreenshotGalleryItem>();
                RenderGallery(_strings.Format("Screenshots.Error.UnavailableWithCode", result.Code));
                return false;
            }

            await RefreshAiDescriptionEmptyMessageAsync(cancellationToken);
            if (cancellationToken.IsCancellationRequested)
            {
                return false;
            }

            SetSelectedDate(result.Value.Date);
            _items = result.Value.Items;
            SelectRequestedScreenshot();
            RenderGallery(null);
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // A newer date selection owns the next gallery request; the superseded result is ignored.
            return false;
        }
        catch (Exception)
        {
            _items = Array.Empty<ScreenshotGalleryItem>();
            RenderGallery(T("Screenshots.Error.Unavailable"));
            return false;
        }
        finally
        {
            if (!cancellationToken.IsCancellationRequested)
            {
                SetLoading(false);
            }
        }
    }

    private async Task RefreshAiDescriptionEmptyMessageAsync(CancellationToken cancellationToken)
    {
        _aiDescriptionEmptyMessageKey = DefaultAiDescriptionEmptyMessageKey;
        try
        {
            var result = await _application.GetAiStatusAsync(cancellationToken);
            if (result.Succeeded &&
                result.Value is { Enabled: true, CostGate: { Allowed: false, Reason: "daily_limit" } })
            {
                // Historical rows do not retain an empty-description reason; report only the truthful current gate state.
                _aiDescriptionEmptyMessageKey = DailyAiLimitEmptyMessageKey;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            // Contextual gate status is optional; the existing neutral empty-state copy remains the safe fallback.
            _aiDescriptionEmptyMessageKey = DefaultAiDescriptionEmptyMessageKey;
        }
    }

    private void SetSelectedDate(DateOnly date)
    {
        _selectedDate = date;
        UpdateDisplayedDate();
        _settingSelectedDate = true;
        try
        {
            SelectedDatePicker.Date = new DateTimeOffset(date.ToDateTime(TimeOnly.MinValue));
        }
        finally
        {
            _settingSelectedDate = false;
        }
    }

    private void ApplyHeaderLocalization()
    {
        HeaderSection.ApplyToolbarLocalization(_strings);
        SelectedDatePicker.PlaceholderText = T("Screenshots.Date.Placeholder");
        var selectDateLabel = T("Screenshots.Date.Select");
        AutomationProperties.SetName(SelectedDatePicker, selectDateLabel);
        ToolTipService.SetToolTip(SelectedDatePicker, selectDateLabel);
        UpdateDisplayedDate();
        UpdateDetailsToggleAccessibility();
        UpdateFilmstripToggleAccessibility();
    }

    private void ApplyLocalization()
    {
        UiLocalization.Apply(RootGrid, _strings);
        Title = T("Screenshots.Title");
        var resizeLabel = T("Screenshots.Details.Resize");
        AutomationProperties.SetName(DetailsResizeGrip, resizeLabel);
        ToolTipService.SetToolTip(DetailsResizeGrip, resizeLabel);
        ApplyPrivacyStatus();
        ApplyHeaderLocalization();
    }

    private void UpdateDisplayedDate()
    {
        ExtendedDateText.Text = _selectedDate.ToDateTime(TimeOnly.MinValue).ToString("D", _strings.Culture);
    }

    private void SelectRequestedScreenshot()
    {
        if (_requestedScreenshotPath is not { } requestedPath)
        {
            return;
        }

        var requestedFullPath = Path.GetFullPath(requestedPath);
        for (var index = 0; index < _items.Count; index++)
        {
            if (string.Equals(Path.GetFullPath(_items[index].Path), requestedFullPath, StringComparison.OrdinalIgnoreCase))
            {
                _selectedIndex = index;
                _requestedScreenshotPath = null;
                return;
            }
        }
    }

    private void RenderGallery(string? error)
    {
        var hasItems = _items.Count > 0;
        GalleryCountText.Text = _items.Count switch
        {
            0 => T("Screenshots.Count.Zero"),
            1 => T("Screenshots.Count.One"),
            _ => _strings.Format("Screenshots.Count.Many", _items.Count)
        };
        EmptyGalleryPanel.Visibility = hasItems ? Visibility.Collapsed : Visibility.Visible;
        ScreenshotViewer.Visibility = hasItems ? Visibility.Visible : Visibility.Collapsed;
        FilmstripStrip.Visibility = hasItems ? Visibility.Visible : Visibility.Collapsed;
        EmptyGalleryText.Text = error ?? (_items.Count == 0 ? T("Screenshots.Empty") : string.Empty);

        _selectedIndex = hasItems ? Math.Clamp(_selectedIndex, 0, _items.Count - 1) : 0;
        TimelineSection.SetItems(_items, hasItems ? _selectedIndex : -1, _strings.Language);
        DayOverviewSection.SetItems(_items, hasItems ? _selectedIndex : -1, _strings.Language);
        RenderSelectedScreenshot();
        SetDetailsPaneVisibility(hasItems && _detailsPaneOpenPreference);
        UpdateDetailsToggleAccessibility();
    }

    private int _selectedIndex;

    private void TimelineSection_SelectedIndexChanged(int selectedIndex)
    {
        if (selectedIndex < 0 || selectedIndex >= _items.Count)
        {
            return;
        }

        _selectedIndex = selectedIndex;
        DayOverviewSection.SetItems(_items, _selectedIndex, _strings.Language);
        RenderSelectedScreenshot();
    }

    private void DayOverviewSection_SelectedIndexChanged(int selectedIndex)
    {
        if (selectedIndex < 0 || selectedIndex >= _items.Count)
        {
            return;
        }

        _selectedIndex = selectedIndex;
        TimelineSection.SetItems(_items, _selectedIndex, _strings.Language);
        DayOverviewSection.SetItems(_items, _selectedIndex, _strings.Language);
        RenderSelectedScreenshot();
    }

    private void RenderSelectedScreenshot()
    {
        if (_items.Count == 0)
        {
            ScreenshotViewer.SetItem(null, -1, 0, _strings.Language);
            RenderMetadata(null);
            return;
        }

        var selected = _items[_selectedIndex];
        ScreenshotViewer.SetItem(selected, _selectedIndex, _items.Count, _strings.Language);
        RenderMetadata(selected);
    }

    private void RenderMetadata(ScreenshotGalleryItem? item)
    {
        if (item is null)
        {
            HeaderSection.ClearMetadata();
            HeaderSection.SetAnalysisDeletionAvailable(false);
            _selectedDetailsState = null;
            RenderDetails();
            return;
        }

        var culture = _strings.Culture;
        var localTime = item.CapturedAt.ToLocalTime();
        var installation = item.Installation
            ?? throw new InvalidDataException("Screenshot installation provenance is required by the header.");
        HeaderSection.SetMetadata(
            FormatMetadataDate(localTime, culture),
            localTime.ToString("t", culture),
            string.IsNullOrWhiteSpace(item.ForegroundApplication)
                ? T("Screenshots.Application.Desktop")
                : item.ForegroundApplication,
            installation);
        HeaderSection.SetAnalysisDeletionAvailable(item.HasRemovableAnalysisData);
        var captureOrigin = FormatCaptureOrigin(item.CaptureOrigin);
        _selectedDetailsState = ScreenshotDetailsProjection.Create(
            item,
            culture,
            FormatCaptureKind(item.CaptureKind),
            captureOrigin,
            "--",
            _strings.Translate);
        RenderDetails();
    }

    private void RenderDetails()
    {
        // The pane starts collapsed, so explicitly localize its declared subtree whenever it is bound or revealed.
        UiLocalization.Apply(DetailsSection, _strings);
        DetailsSection.Render(
            _selectedDetailsState,
            _strings.Translate(_aiDescriptionEmptyMessageKey),
            FormatPrivacyStatus());
    }

    private void ApplyPrivacyStatus() =>
        HeaderSection.SetPrivacyStatus(FormatPrivacyStatus(), _privacyRuleCount is > 0);

    private string FormatPrivacyStatus() => _privacyRuleCount switch
    {
        null => T("Screenshots.Privacy.Unavailable"),
        0 => T("Screenshots.Privacy.None"),
        1 => T("Screenshots.Privacy.One"),
        var count => _strings.Format("Screenshots.Privacy.Many", count)
    };

    private async void DetailsSection_OcrTextRequested(string ocrText)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ocrText);
        if (_openingOcrTextWindow || _lifetimeCancellation.IsCancellationRequested) return;
        _openingOcrTextWindow = true;
        var cancellationToken = _lifetimeCancellation.Token;
        try
        {
            var selected = GetSelectedItem();
            var saved = await _application.SetOcrTextWindowSourceAsync(selected.Path, selected.CapturedAt, cancellationToken);
            if (!saved.Succeeded)
            {
                ShowActionResult(saved, "Screenshots.OcrText.WindowTitle");
                return;
            }

            cancellationToken.ThrowIfCancellationRequested();
            ShowOcrTextWindow(ocrText, selected.Path);
        }
        catch (OperationCanceledException) when (_lifetimeCancellation.IsCancellationRequested)
        {
            // Closing owns cancellation; never reopen a child window after its gallery has closed.
        }
        catch (Exception)
        {
            // Surface an opening or persistence failure without retrying with different screenshot content.
            _dialogs.Notifications.ShowError(ScreenshotActionBanner, T("Screenshots.OcrText.WindowTitle"), T("Screenshots.Action.Failed"));
        }
        finally
        {
            _openingOcrTextWindow = false;
        }
    }

    private async Task RestoreOcrTextWindowAsync(OcrTextWindowSource source)
    {
        var selected = _items.Count > 0 ? GetSelectedItem() : null;
        var ocrText = _selectedDetailsState?.OcrText;
        if (selected is not null
            && StringComparer.OrdinalIgnoreCase.Equals(selected.Path, source.ScreenshotPath)
            && selected.CapturedAt == source.CapturedAt
            && !string.IsNullOrWhiteSpace(ocrText))
        {
            ShowOcrTextWindow(ocrText, selected.Path);
            return;
        }

        // Retention or deletion can remove this exact screenshot or its OCR. Close the saved surface explicitly
        // and show the existing unavailable notice instead of opening text from the gallery's replacement selection.
        var cleared = await _application.SetWindowOpenStateAsync(WindowStateKeys.OcrText, false, _lifetimeCancellation.Token);
        if (!cleared.Succeeded)
        {
            throw new InvalidOperationException($"The unavailable OCR window state could not be saved ({cleared.Code}).");
        }

        _dialogs.Notifications.ShowError(ScreenshotActionBanner, T("Screenshots.OcrText.WindowTitle"), T("Screenshots.Error.Unavailable"));
    }

    private void ShowOcrTextWindow(string ocrText, string screenshotPath)
    {
        var requestedTheme = RootGrid.RequestedTheme;
        if (_ocrTextWindow is null)
        {
            _ocrTextWindow = new OcrTextWindow(
                _application,
                _appWindow,
                ocrText,
                requestedTheme,
                _strings.Language);
            _ocrTextWindow.Closed += OcrTextWindow_Closed;
        }
        else
        {
            _ocrTextWindow.UpdateContent(ocrText, requestedTheme, _strings.Language);
        }

        _ocrTextScreenshotPath = screenshotPath;
        _ocrTextWindow.Activate();
    }

    private void OcrTextWindow_Closed(object sender, WindowEventArgs args)
    {
        if (!ReferenceEquals(sender, _ocrTextWindow))
        {
            return;
        }

        _ocrTextWindow.Closed -= OcrTextWindow_Closed;
        _ocrTextWindow = null;
        _ocrTextScreenshotPath = null;
    }

    private void HeaderSection_ZoomOutRequested(object? sender, EventArgs e) => ScreenshotViewer.ZoomOut();

    private void HeaderSection_ZoomResetRequested(object? sender, EventArgs e) => ScreenshotViewer.ResetZoom();

    private void HeaderSection_ZoomInRequested(object? sender, EventArgs e) => ScreenshotViewer.ZoomIn();

    private void ScreenshotViewer_ZoomStateChanged(object? sender, EventArgs e) => UpdateScreenshotToolbarState();

    private void ScreenshotViewer_ImageLoadFailed(object? sender, EventArgs e) =>
        _dialogs.Notifications.ShowError(
            ScreenshotActionBanner,
            T("Screenshots.Caption"),
            T("Screenshots.Error.Unavailable"));

    private void UpdateScreenshotToolbarState() =>
        HeaderSection.SetViewerState(
            ScreenshotViewer.ZoomText,
            ScreenshotViewer.HasImage,
            ScreenshotViewer.CanZoomOut,
            ScreenshotViewer.CanZoomIn);

    private async void HeaderSection_DetailsVisibilityRequested(bool isVisible)
    {
        if (_isSavingDetailsPanePreference)
        {
            return;
        }

        var previousPreference = _detailsPaneOpenPreference;
        _isSavingDetailsPanePreference = true;
        _detailsPaneOpenPreference = isVisible;
        SetDetailsPaneVisibility(_items.Count > 0 && isVisible);
        try
        {
            var result = await _application.PatchSettingsAsync(
                new SettingsPatch(new Dictionary<string, string?>
                {
                    ["screenshots.details_pane_open"] = isVisible ? "true" : "false"
                }),
                _lifetimeCancellation.Token);
            if (!result.Succeeded || result.Value is null)
            {
                RestoreDetailsPanePreference(previousPreference);
                ShowDetailsPanePreferenceFailure();
                return;
            }

            _detailsPaneOpenPreference = result.Value.ScreenshotDetailsPaneOpen;
            SetDetailsPaneVisibility(_items.Count > 0 && _detailsPaneOpenPreference);
        }
        catch (OperationCanceledException) when (_lifetimeCancellation.IsCancellationRequested)
        {
            // The window is closing; the shared runtime owns the final persisted state.
        }
        catch (Exception)
        {
            RestoreDetailsPanePreference(previousPreference);
            ShowDetailsPanePreferenceFailure();
        }
        finally
        {
            _isSavingDetailsPanePreference = false;
            if (!_lifetimeCancellation.IsCancellationRequested)
            {
                UpdateDetailsToggleAccessibility();
            }
        }
    }

    /// <summary>Closes the inspector without starting persistence while the owning application is shutting down.</summary>
    internal void CloseForShutdown()
    {
        _lifetimeCancellation.Cancel();
        Close();
    }

    private void RestoreDetailsPanePreference(bool preference)
    {
        _detailsPaneOpenPreference = preference;
        SetDetailsPaneVisibility(_items.Count > 0 && preference);
    }

    private void ShowDetailsPanePreferenceFailure() =>
        _dialogs.Notifications.ShowError(
            ScreenshotActionBanner,
            T("Screenshots.Caption"),
            T("Screenshots.Action.Failed"));

    private void SetDetailsPaneVisibility(bool isVisible)
    {
        DetailsPane.Visibility = isVisible ? Visibility.Visible : Visibility.Collapsed;
        if (isVisible)
        {
            SetDetailsPaneWidth(double.IsNaN(DetailsPane.Width) ? DefaultDetailsPaneWidth : DetailsPane.Width);
            RenderDetails();
        }

        UpdateDetailsToggleAccessibility();
    }

    private void UpdateDetailsToggleAccessibility()
    {
        var key = DetailsPane.Visibility == Visibility.Visible
            ? "Screenshots.Details.Hide"
            : "Screenshots.Details.Show";
        var label = _strings.Translate(key);
        HeaderSection.SetDetailsState(
            _items.Count > 0 && !_isSavingDetailsPanePreference,
            DetailsPane.Visibility == Visibility.Visible,
            label);
    }

    private void RootGrid_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (DetailsPane.Visibility == Visibility.Visible)
        {
            SetDetailsPaneWidth(DetailsPane.Width);
        }
    }

    private void DetailsResizeGrip_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        var point = e.GetCurrentPoint(RootGrid);
        if (sender is not Controls.HorizontalResizeGrip grip
            || (e.Pointer.PointerDeviceType == PointerDeviceType.Mouse && !point.Properties.IsLeftButtonPressed)
            || !grip.CapturePointer(e.Pointer))
        {
            return;
        }

        _detailsResizePointerId = e.Pointer.PointerId;
        _detailsResizeStartPointerX = point.Position.X;
        _detailsResizeStartWidth = DetailsPane.Width;
        e.Handled = true;
    }

    private void DetailsResizeGrip_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (_detailsResizePointerId != e.Pointer.PointerId)
        {
            return;
        }

        var currentPointerX = e.GetCurrentPoint(RootGrid).Position.X;
        SetDetailsPaneWidth(_detailsResizeStartWidth + (_detailsResizeStartPointerX - currentPointerX));
        e.Handled = true;
    }

    private void DetailsResizeGrip_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (_detailsResizePointerId != e.Pointer.PointerId || sender is not Controls.HorizontalResizeGrip grip)
        {
            return;
        }

        _detailsResizePointerId = null;
        grip.ReleasePointerCapture(e.Pointer);
        e.Handled = true;
    }

    private void DetailsResizeGrip_PointerCanceled(object sender, PointerRoutedEventArgs e) =>
        _detailsResizePointerId = null;

    private void DetailsResizeGrip_PointerCaptureLost(object sender, PointerRoutedEventArgs e) =>
        _detailsResizePointerId = null;

    private void DetailsResizeGrip_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        var targetWidth = e.Key switch
        {
            VirtualKey.Left => DetailsPane.Width + DetailsPaneKeyboardResizeStep,
            VirtualKey.Right => DetailsPane.Width - DetailsPaneKeyboardResizeStep,
            VirtualKey.Home => MinimumDetailsPaneWidth,
            VirtualKey.End => RootGrid.ActualWidth * MaximumDetailsPaneWidthRatio,
            _ => double.NaN
        };
        if (double.IsNaN(targetWidth))
        {
            return;
        }

        SetDetailsPaneWidth(targetWidth);
        e.Handled = true;
    }

    private void SetDetailsPaneWidth(double requestedWidth)
    {
        var maximumWidth = Math.Max(1d, RootGrid.ActualWidth * MaximumDetailsPaneWidthRatio);
        var minimumWidth = Math.Min(MinimumDetailsPaneWidth, maximumWidth);
        DetailsPane.Width = Math.Clamp(requestedWidth, minimumWidth, maximumWidth);
    }

    private static string FormatMetadataDate(DateTimeOffset capturedAt, CultureInfo culture)
    {
        var pattern = culture.DateTimeFormat.LongDatePattern
            .Replace("dddd,", string.Empty, StringComparison.Ordinal)
            .Replace("dddd", string.Empty, StringComparison.Ordinal)
            .TrimStart(' ', ',');
        return capturedAt.ToString(pattern, culture);
    }

    private string FormatCaptureOrigin(string captureOrigin)
    {
        var normalizedOrigin = captureOrigin.Trim().ToLowerInvariant();
        return normalizedOrigin switch
        {
            ScreenshotCaptureOrigins.Manual => _strings.Translate("Screenshots.Origin.Manual"),
            ScreenshotCaptureOrigins.Scheduled => _strings.Translate("Screenshots.Origin.Scheduled"),
            _ => captureOrigin
        };
    }

    private string FormatCaptureKind(string captureKind)
    {
        var normalizedKind = captureKind.Trim().ToLowerInvariant();
        return normalizedKind switch
        {
            "active-window" => _strings.Translate("Screenshots.CaptureKind.ActiveWindow"),
            "monitor" => _strings.Translate("Screenshots.CaptureKind.Monitor"),
            _ => throw new InvalidDataException($"Unsupported screenshot capture kind: {captureKind}")
        };
    }

    private async void HeaderSection_DeleteScreenshotRequested(object? sender, EventArgs e) =>
        await RunConfirmedDeletionAsync(
            "Screenshots.DeleteScreenshot.Confirm.Title",
            "Screenshots.DeleteScreenshot.Confirm.Message",
            _application.DeleteScreenshotAsync,
            "Screenshots.Action.ScreenshotDeleted");

    private async void HeaderSection_DeleteAnalysisRequested(object? sender, EventArgs e) =>
        await RunConfirmedDeletionAsync(
            "Screenshots.DeleteAnalysis.Confirm.Title",
            "Screenshots.DeleteAnalysis.Confirm.Message",
            _application.DeleteScreenshotAnalysisAsync,
            "Screenshots.Action.AnalysisDeleted");

    private async Task RunConfirmedDeletionAsync(
        string titleKey,
        string messageKey,
        Func<string, CancellationToken, Task<OperationResult<string>>> deleteAsync,
        string successMessageKey)
    {
        if (_deleteOperationInProgress || _lifetimeCancellation.IsCancellationRequested)
        {
            return;
        }

        _deleteOperationInProgress = true;
        HeaderSection.SetDeletionActionsEnabled(false);
        try
        {
            if (!await _dialogs.ConfirmAsync(
                    this,
                    DialogRequest.Confirmation(T(titleKey), T(messageKey), T("Dialog.Ok"), T("Dialog.Cancel"))))
            {
                return;
            }

            var selected = GetSelectedItem();
            var result = await deleteAsync(selected.Path, _lifetimeCancellation.Token);
            if (result.Succeeded)
            {
                CloseOcrTextWindowForScreenshot(selected.Path);
                await LoadGalleryAsync(_selectedDate);
            }

            ShowActionResult(result, successMessageKey);
        }
        catch (OperationCanceledException) when (_lifetimeCancellation.IsCancellationRequested)
        {
            // Closing the window owns cancellation; destructive commands do not surface a stale result.
        }
        finally
        {
            _deleteOperationInProgress = false;
            HeaderSection.SetDeletionActionsEnabled(true);
        }
    }

    private void CloseOcrTextWindowForScreenshot(string screenshotPath)
    {
        if (_ocrTextWindow is null
            || !StringComparer.OrdinalIgnoreCase.Equals(_ocrTextScreenshotPath, screenshotPath))
        {
            return;
        }

        _ocrTextWindow.Close();
    }

    private async void HeaderSection_SaveRequested(object? sender, EventArgs e) =>
        await SaveSelectedScreenshotAsync();

    private async Task SaveSelectedScreenshotAsync()
    {
        var selected = GetSelectedItem();
        var extension = Path.GetExtension(selected.Path) ?? string.Empty;
        if (string.IsNullOrWhiteSpace(extension))
        {
            throw new InvalidDataException($"Screenshot has no file extension: {selected.Path}");
        }

        var picker = new FileSavePicker
        {
            SuggestedStartLocation = PickerLocationId.PicturesLibrary,
            SuggestedFileName = Path.GetFileName(selected.Path)
        };
        picker.FileTypeChoices.Add(T("Screenshots.FileType"), new[] { extension });
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this));
        var destination = await picker.PickSaveFileAsync();
        if (destination is null || _lifetimeCancellation.IsCancellationRequested)
        {
            return;
        }

        try
        {
            var result = await _dialogs.RunWithProgressAsync(
                _application,
                this,
                RootGrid.RequestedTheme,
                T("Screenshots.Toolbar.Save"),
                T("Operations.Status.InProgress.Message"),
                (application, token) => application.SaveScreenshotAsync(selected.Path, destination.Path, token));
            if (!_lifetimeCancellation.IsCancellationRequested)
            {
                ShowActionResult(result, "Screenshots.Action.Saved");
            }
        }
        catch (OperationCanceledException) when (_lifetimeCancellation.IsCancellationRequested)
        {
            // Closing the gallery owns cancellation; do not report a stale save result.
        }
        catch (Exception)
        {
            if (!_lifetimeCancellation.IsCancellationRequested)
            {
                // Storage or dialog failures retain the localized screenshot-action error and no private paths.
                _dialogs.Notifications.ShowError(ScreenshotActionBanner, T("Screenshots.Caption"), T("Screenshots.Action.Failed"));
            }
        }
    }

    private async void HeaderSection_OpenFolderRequested(object? sender, EventArgs e)
    {
        var selected = GetSelectedItem();
        var directory = System.IO.Path.GetDirectoryName(selected.Path) ?? string.Empty;
        var result = await _application.OpenScreenshotFolderAsync(directory, _lifetimeCancellation.Token);
        ShowActionResult(result, "Screenshots.Action.FolderOpened");
    }

    private async void HeaderSection_ShareRequested(object? sender, EventArgs e)
    {
        var selected = GetSelectedItem();
        var windowHandle = WinRT.Interop.WindowNative.GetWindowHandle(this).ToInt64();
        var result = await _application.ShareScreenshotAsync(selected.Path, windowHandle, _lifetimeCancellation.Token);
        ShowActionResult(result, "Screenshots.Action.ShareOpened");
    }

    private void ShowActionResult<T>(OperationResult<T> result, string successKey)
    {
        var title = _strings.Translate("Screenshots.Caption");
        var message = _strings.Translate(result.Succeeded ? successKey : "Screenshots.Action.Failed");
        if (result.Succeeded)
        {
            _dialogs.Notifications.ShowSuccess(ScreenshotActionBanner, title, message);
        }
        else
        {
            _dialogs.Notifications.ShowError(ScreenshotActionBanner, title, message);
        }
    }

    private ScreenshotGalleryItem GetSelectedItem()
        => _items.Count == 0
            ? throw new InvalidOperationException("Screenshot action requested without a selected capture.")
            : _items[_selectedIndex];

    private void FilmstripToggleButton_Click(object sender, RoutedEventArgs e)
    {
        var isExpanded = FilmstripList.Visibility != Visibility.Visible;
        FilmstripList.Visibility = isExpanded ? Visibility.Visible : Visibility.Collapsed;
        FilmstripChevronIcon.Glyph = isExpanded ? "\uE70E" : "\uE70D";
        UpdateFilmstripToggleAccessibility();
    }

    private void UpdateFilmstripToggleAccessibility()
    {
        var key = FilmstripList.Visibility == Visibility.Visible
            ? "Screenshots.Timeline.Hide"
            : "Screenshots.Timeline.Show";
        var label = _strings.Translate(key);
        FilmstripToggleButton.Tag = key;
        AutomationProperties.SetName(FilmstripToggleButton, label);
        ToolTipService.SetToolTip(FilmstripToggleButton, label);
    }

    private void SetLoading(bool isLoading)
    {
        GalleryProgressRing.IsActive = isLoading;
        GalleryProgressRing.Visibility = isLoading ? Visibility.Visible : Visibility.Collapsed;
        if (isLoading)
        {
            ScreenshotViewer.Visibility = Visibility.Collapsed;
            EmptyGalleryPanel.Visibility = Visibility.Collapsed;
        }

        SelectedDatePicker.IsEnabled = !isLoading;
    }

    private string T(string key) => _strings.Translate(key);

    private void ApplyTheme(string theme)
    {
        _theme = theme;
        RootGrid.RequestedTheme = theme switch
        {
            "light" => ElementTheme.Light,
            "dark" => ElementTheme.Dark,
            _ => ElementTheme.Default
        };

        var effectiveTheme = RootGrid.RequestedTheme == ElementTheme.Default
            ? RootGrid.ActualTheme
            : RootGrid.RequestedTheme;
        _titleBar.ApplyTheme(effectiveTheme);
    }

    private void ScreenshotWindow_Closed(object sender, WindowEventArgs args)
    {
        DetailsSection.OcrTextRequested -= DetailsSection_OcrTextRequested;
        ScreenshotViewer.ZoomStateChanged -= ScreenshotViewer_ZoomStateChanged;
        ScreenshotViewer.ImageLoadFailed -= ScreenshotViewer_ImageLoadFailed;
        DayOverviewSection.SelectedIndexChanged -= DayOverviewSection_SelectedIndexChanged;
        if (_ocrTextWindow is { } ocrTextWindow)
        {
            ocrTextWindow.Closed -= OcrTextWindow_Closed;
            _ocrTextWindow = null;
            _ocrTextScreenshotPath = null;
            ocrTextWindow.Close();
        }

        _placement.Dispose();

        _lifetimeCancellation.Cancel();
        _galleryCancellation?.Cancel();
        _galleryCancellation?.Dispose();
        _titleBar.Dispose();
        _lifetimeCancellation.Dispose();
    }
}
