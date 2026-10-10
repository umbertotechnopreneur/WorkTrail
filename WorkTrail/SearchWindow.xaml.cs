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


using System.Globalization;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using WorkTrail.Application;
using WorkTrail.Controls;
using WorkTrail.Presentation;
using WorkTrail.Services;

namespace WorkTrail;

/// <summary>Displays a light Acrylic surface for local screenshot search.</summary>
public sealed partial class SearchWindow : Window
{
    private const int LogicalWindowWidth = 640;
    private const int LogicalWindowHeight = 156;
    private const int PreviewDecodePixelWidth = 1600;
    private const double StackedPreviewWidth = 760d;
    private const int LogicalScreenMargin = 22;
    private static readonly TimeSpan SearchDebounce = TimeSpan.FromMilliseconds(250);
    private readonly SearchViewModel _viewModel;
    private readonly LocalizationService _strings;
    private readonly CultureInfo _culture;
    private readonly AppWindow _appWindow;
    private readonly CustomTitleBarController _titleBar;
    private readonly WindowPlacementService _placement;
    private readonly ScreenshotBitmapSourceLoader _screenshotBitmapLoader;
    private readonly SearchPreviewSelectionState _previewSelection = new();
    private readonly CancellationTokenSource _lifetimeCancellation = new();
    private CancellationTokenSource? _queryCancellation;
    private CancellationTokenSource? _debounceCancellation;
    private CancellationTokenSource? _previewImageCancellation;
    private bool _hasExecutedQuery;
    private bool _closing;
    private int _activeSearchOperationCount;
    private bool _placementReady;
    private bool _expanded;
    private int _expandedWidth = 1040;
    private int _expandedHeight = 720;

    /// <summary>Creates the fixed-light floating local-search window in the requested language.</summary>
    public SearchWindow(IWorkTrailApplication application, string language, SearchAvailability availability)
    {
        ArgumentNullException.ThrowIfNull(application);
        ArgumentNullException.ThrowIfNull(availability);
        if (availability.TotalSnapshotCount <= 0 ||
            availability.TodaySnapshotCount < 0 ||
            availability.TodaySnapshotCount > availability.TotalSnapshotCount)
        {
            throw new ArgumentOutOfRangeException(nameof(availability), "Search availability must describe at least one retained snapshot.");
        }

        _strings = new LocalizationService(language);
        _screenshotBitmapLoader = new ScreenshotBitmapSourceLoader(application);
        _culture = _strings.Culture;
        _viewModel = new SearchViewModel(
            application,
            _strings.Translate("Search.Result.Match"),
            FormatClickCount);
        InitializeComponent();
        RootGrid.DataContext = _viewModel;
        RootGrid.RequestedTheme = ElementTheme.Light;
        UiLocalization.Apply(RootGrid, _strings);
        Title = _strings.Translate("Search.Title");
        QueryBox.PlaceholderText = _strings.Translate("Search.Placeholder");
        AutomationProperties.SetName(QueryBox, _strings.Translate("Search.Placeholder"));
        AutomationProperties.SetName(SearchActivityProgressRing, _strings.Translate("Search.Working"));
        SearchAvailabilityText.Text = string.Format(
            _culture,
            _strings.Translate("Search.SnapshotCounts"),
            availability.TotalSnapshotCount,
            availability.TodaySnapshotCount);
        TextReadingStatusText.Text = _strings.Translate(availability.TextReadingEnabled
            ? "Search.TextReading.Status.Enabled"
            : "Search.TextReading.Status.Disabled");
        TextReadingStatusDot.Fill = (Brush)Microsoft.UI.Xaml.Application.Current.Resources[availability.TextReadingEnabled
            ? "SystemFillColorSuccessBrush"
            : "TextFillColorSecondaryBrush"];
        AutomationProperties.SetName(SearchAvailabilityText, SearchAvailabilityText.Text);
        AutomationProperties.SetName(TextReadingStatusText, TextReadingStatusText.Text);
        AutomationProperties.SetName(SearchResultsList, _strings.Translate("Search.Results.Title"));
        AutomationProperties.SetName(PreviewScroller, _strings.Translate("Search.Preview.Title"));
        AutomationProperties.SetName(OpenSnapshotButton, _strings.Translate("Search.Preview.Open"));
        AutomationProperties.SetName(PreviewImageProgressRing, _strings.Translate("Search.Preview.Loading"));
        _appWindow = AppWindow.GetFromWindowId(
            Win32Interop.GetWindowIdFromWindow(WinRT.Interop.WindowNative.GetWindowHandle(this)));
        _titleBar = new CustomTitleBarController(
            this,
            _appWindow,
            RootGrid,
            TitleBarDragRegion,
            TitleBarLeftInsetColumn,
            TitleBarRightInsetColumn,
            static () => Array.Empty<FrameworkElement>(),
            useTallTitleBar: false);
        _titleBar.ApplyTheme(ElementTheme.Light);
        _placement = new WindowPlacementService(application, this, _appWindow, WindowStateKeys.Search, LogicalWindowWidth, LogicalWindowHeight, LogicalScreenMargin);
        ConfigureWindowBehavior();
        _placement.ApplyDefaultBounds(RootGrid);
        Activated += SearchWindow_Activated;
        Closed += SearchWindow_Closed;
    }

    private string FormatClickCount(long? clickCount, CultureInfo culture)
    {
        if (clickCount is null)
        {
            return _strings.Translate("Search.Result.Clicks.None");
        }

        var key = clickCount == 1
            ? "Search.Result.Clicks.One"
            : "Search.Result.Clicks.Many";
        return string.Format(culture, _strings.Translate(key), clickCount.Value);
    }

    /// <summary>Occurs when the user selects a screenshot result for the existing inspector.</summary>
    public event EventHandler<ScreenshotPreviewRequestedEventArgs>? ScreenshotRequested;

    /// <summary>Moves keyboard focus to the query field when an existing window is reactivated.</summary>
    public void FocusQuery()
    {
        QueryBox.Focus(FocusState.Programmatic);
        SelectAllQueryText();
    }

    /// <summary>Activates the existing search window without replacing its saved or user-adjusted placement.</summary>
    public void ActivateSearch()
    {
        Activate();
        FocusQuery();
    }

    private async void RootGrid_Loaded(object sender, RoutedEventArgs e)
    {
        _placement.ApplyDefaultBounds(RootGrid);
        try
        {
            await _placement.RestoreOrCenterAsync(RootGrid, _lifetimeCancellation.Token, centerOnCursorDisplay: true);
        }
        catch (OperationCanceledException) when (_closing)
        {
            return;
        }

        _placementReady = true;
        UpdateWindowSize();
        ConfigureQueryInput();
        FocusQuery();
    }

    private void QueryBox_QuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        if (args.QueryText.Trim().Length < SearchViewModel.MinimumQueryLength)
        {
            return;
        }

        CancelDebounce();
        _ = ExecuteSearchAsync(args.QueryText);
    }

    private void QueryBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason != AutoSuggestionBoxTextChangeReason.UserInput)
        {
            return;
        }

        CancelDebounce();
        _queryCancellation?.Cancel();
        var query = sender.Text.Trim();
        if (query.Length < SearchViewModel.MinimumQueryLength)
        {
            _queryCancellation?.Cancel();
            _viewModel.Clear();
            SearchInfoBar.IsOpen = false;
            UpdateResultState(hasExecutedQuery: false);
            return;
        }

        _debounceCancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetimeCancellation.Token);
        var cancellationToken = _debounceCancellation.Token;
        _ = DebounceSearchAsync(query, cancellationToken);
    }

    private async Task DebounceSearchAsync(string query, CancellationToken cancellationToken)
    {
        BeginSearchActivity();
        try
        {
            await Task.Delay(SearchDebounce, cancellationToken);
            await ExecuteSearchAsync(query);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // A newer keystroke owns the next debounce window.
        }
        finally
        {
            EndSearchActivity();
        }
    }

    private async Task ExecuteSearchAsync(string query)
    {
        _queryCancellation?.Cancel();
        _queryCancellation?.Dispose();
        _queryCancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetimeCancellation.Token);
        var cancellationToken = _queryCancellation.Token;
        SearchInfoBar.IsOpen = false;
        BeginSearchActivity();
        try
        {
            var result = await _viewModel.SearchAsync(query, _culture, cancellationToken);
            if (!result.Succeeded)
            {
                SearchInfoBar.Title = _strings.Translate("Search.Error");
                SearchInfoBar.Message = string.Empty;
                SearchInfoBar.IsOpen = true;
            }

            UpdateResultState(hasExecutedQuery: !string.IsNullOrWhiteSpace(query));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // A superseding query or window close owns the next presentation state.
        }
        catch (Exception)
        {
            _viewModel.Clear();
            SearchInfoBar.Title = _strings.Translate("Search.Error");
            SearchInfoBar.Message = string.Empty;
            SearchInfoBar.IsOpen = true;
            UpdateResultState(hasExecutedQuery: true);
        }
        finally
        {
            EndSearchActivity();
        }
    }

    private void BeginSearchActivity()
    {
        var wasIdle = _activeSearchOperationCount == 0;
        _activeSearchOperationCount++;
        if (wasIdle)
        {
            SearchActivityGlow.SetSearching(true);
            UpdateResultState(_hasExecutedQuery);
        }
    }

    private void EndSearchActivity()
    {
        if (_activeSearchOperationCount <= 0)
        {
            throw new InvalidOperationException("Search activity tracking became unbalanced.");
        }

        _activeSearchOperationCount--;
        if (_activeSearchOperationCount == 0)
        {
            SearchActivityGlow.SetSearching(false);
            UpdateResultState(_hasExecutedQuery);
        }
    }

    private void UpdateResultState(bool hasExecutedQuery)
    {
        if (_closing)
        {
            return; // Canceled queries may finish after the native window has closed.
        }

        _hasExecutedQuery = hasExecutedQuery;
        var hasResults = _viewModel.Results.Count > 0;
        var isSearching = _activeSearchOperationCount > 0;
        ResultsSurface.Visibility = hasResults ? Visibility.Visible : Visibility.Collapsed;
        var countKey = _viewModel.TotalCount > _viewModel.Results.Count
            ? "Search.Results.Limited"
            : _viewModel.Results.Count == 1 ? "Search.Results.One" : "Search.Results.Many";
        ResultCountText.Text = string.Format(_culture, _strings.Translate(countKey),
            _viewModel.Results.Count, _viewModel.TotalCount);
        SearchResultsList.SelectedItem = _viewModel.SelectedResult;
        RenderSelectedPreview();
        EmptyStatePanel.Visibility = hasExecutedQuery && !hasResults && !isSearching && !SearchInfoBar.IsOpen
            ? Visibility.Visible
            : Visibility.Collapsed;
        SearchActivityStatus.Visibility = isSearching ? Visibility.Visible : Visibility.Collapsed;
        SearchActivityProgressRing.IsActive = isSearching;
        SearchStatusRow.Visibility = isSearching || EmptyStatePanel.Visibility == Visibility.Visible
            ? Visibility.Visible : Visibility.Collapsed;
        UpdateWindowSize();
    }

    private void SearchInfoBar_Closed(InfoBar sender, InfoBarClosedEventArgs args) => UpdateWindowSize();

    /// <summary>Keeps an empty search compact and remembers user-adjusted result dimensions within this window.</summary>
    private void UpdateWindowSize()
    {
        if (!_placementReady || _closing) return;
        var hasResults = _viewModel.Results.Count > 0;
        if (_expanded && !hasResults)
        {
            var scale = RootGrid.XamlRoot.RasterizationScale;
            _expandedWidth = Math.Max(LogicalWindowWidth, (int)Math.Ceiling(_appWindow.Size.Width / scale));
            _expandedHeight = Math.Max(420, (int)Math.Ceiling(_appWindow.Size.Height / scale));
        }

        if (_appWindow.Presenter is OverlappedPresenter presenter) presenter.IsResizable = hasResults;
        if (hasResults)
        {
            if (!_expanded) _placement.ResizeForContent(RootGrid, _expandedWidth, _expandedHeight);
        }
        else
        {
            // Saved result bounds must not make a newly opened empty search occupy the entire screen.
            var height = LogicalWindowHeight
                + (SearchStatusRow.Visibility == Visibility.Visible ? 36 : 0)
                + (SearchInfoBar.IsOpen ? 80 : 0);
            _placement.ResizeForContent(RootGrid, LogicalWindowWidth, height);
        }

        _expanded = hasResults;
    }

    private void SearchResultsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _viewModel.SelectResult(SearchResultsList.SelectedItem as ScreenshotSearchResult);
        RenderSelectedPreview();
        PreviewScroller.ChangeView(null, 0d, null, disableAnimation: true);
    }

    private void RenderSelectedPreview()
    {
        var result = _viewModel.SelectedResult;
        PreviewPane.DataContext = result;
        SearchTextHighlight.Apply(PreviewTitleText, result?.TitleDisplay, result?.Query);
        SearchTextHighlight.Apply(PreviewBodyText, result?.PreviewText, result?.Query);
        PreviewInstallationText.Visibility = string.IsNullOrWhiteSpace(result?.InstallationDisplay)
            ? Visibility.Collapsed : Visibility.Visible;
        OpenSnapshotButton.IsEnabled = result is not null;
        UpdatePreviewScreenshot(result);
    }

    private void UpdatePreviewScreenshot(ScreenshotSearchResult? result)
    {
        if (!_previewSelection.Select(result?.ScreenshotPath))
        {
            return;
        }

        CancelPreviewImageLoad();
        PreviewScreenshotImage.Source = null;
        PreviewScreenshotImage.Visibility = Visibility.Collapsed;
        PreviewImageUnavailableText.Visibility = Visibility.Collapsed;
        var loading = result is not null;
        PreviewImageLoadingPanel.Visibility = loading ? Visibility.Visible : Visibility.Collapsed;
        PreviewImageProgressRing.IsActive = loading;
        if (result is null)
        {
            return;
        }

        AutomationProperties.SetName(PreviewScreenshotImage,
            _strings.Format("Search.Preview.ImageAccessible", result.SourceDisplay));
        var cancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetimeCancellation.Token);
        _previewImageCancellation = cancellation;
        _ = LoadPreviewScreenshotAsync(result.ScreenshotPath, _previewSelection.Generation, cancellation);
    }

    private async Task LoadPreviewScreenshotAsync(string screenshotPath, int generation, CancellationTokenSource cancellation)
    {
        try
        {
            // The shared loader delegates all validation and file reads to IWorkTrailApplication.
            var result = await _screenshotBitmapLoader.LoadAsync(screenshotPath, PreviewDecodePixelWidth, cancellation.Token);
            if (!IsCurrentPreviewLoad(screenshotPath, generation, cancellation))
            {
                return;
            }

            PreviewScreenshotImage.Source = result.Bitmap;
            PreviewScreenshotImage.Visibility = result.Succeeded && result.Bitmap is not null
                ? Visibility.Visible : Visibility.Collapsed;
            PreviewImageUnavailableText.Visibility = PreviewScreenshotImage.Visibility == Visibility.Visible
                ? Visibility.Collapsed : Visibility.Visible;
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            // A newer selection or closed window owns the visible preview; no stale image or failure is applied.
        }
        catch (Exception)
        {
            if (IsCurrentPreviewLoad(screenshotPath, generation, cancellation))
            {
                PreviewImageUnavailableText.Visibility = Visibility.Visible;
            }
        }
        finally
        {
            if (IsCurrentPreviewLoad(screenshotPath, generation, cancellation))
            {
                PreviewImageLoadingPanel.Visibility = Visibility.Collapsed;
                PreviewImageProgressRing.IsActive = false;
            }

            if (ReferenceEquals(_previewImageCancellation, cancellation))
            {
                _previewImageCancellation = null;
            }

            cancellation.Dispose();
        }
    }

    private bool IsCurrentPreviewLoad(string screenshotPath, int generation, CancellationTokenSource cancellation) =>
        !_closing && !cancellation.IsCancellationRequested
        && ReferenceEquals(_previewImageCancellation, cancellation)
        && _previewSelection.IsCurrent(screenshotPath, generation);

    private void CancelPreviewImageLoad()
    {
        _previewImageCancellation?.Cancel();
        _previewImageCancellation = null;
    }

    private void OpenSnapshotButton_Click(object sender, RoutedEventArgs e) => OpenSelectedSnapshot();

    private void SearchResultsList_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Enter && _viewModel.SelectedResult is not null)
        {
            e.Handled = true;
            OpenSelectedSnapshot();
        }
    }

    private void OpenSelectedSnapshot()
    {
        if (_viewModel.SelectedResult is { } result)
        {
            ScreenshotRequested?.Invoke(
                this,
                new ScreenshotPreviewRequestedEventArgs(result.ScreenshotPath, result.CapturedAt));
        }
    }

    private void RootGrid_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        var stacked = e.NewSize.Width < StackedPreviewWidth;
        ResultsListColumn.Width = new GridLength(stacked ? 1d : 2d, GridUnitType.Star);
        PreviewColumn.Width = stacked ? new GridLength(0d) : new GridLength(3d, GridUnitType.Star);
        ResultsListRow.Height = stacked ? GridLength.Auto : new GridLength(1d, GridUnitType.Star);
        PreviewRow.Height = stacked ? new GridLength(1d, GridUnitType.Star) : new GridLength(0d);
        SearchResultsPane.MaxHeight = stacked ? 200d : double.PositiveInfinity;
        Grid.SetColumn(PreviewPane, stacked ? 0 : 1);
        Grid.SetRow(PreviewPane, stacked ? 1 : 0);
        PreviewPane.BorderThickness = stacked ? new Thickness(0, 1, 0, 0) : new Thickness(1, 0, 0, 0);
    }

    private void QueryBox_GotFocus(object sender, RoutedEventArgs e)
    {
        ConfigureQueryInput();
        SelectAllQueryText();
    }

    private void ConfigureQueryInput()
    {
        if (FindDescendant<TextBox>(QueryBox) is { } textBox)
        {
            textBox.VerticalContentAlignment = VerticalAlignment.Center;
            textBox.Padding = new Thickness(textBox.Padding.Left, 0, textBox.Padding.Right, 0);
            textBox.IsTextPredictionEnabled = false;
            textBox.IsSpellCheckEnabled = false;
        }
    }

    private void SelectAllQueryText()
    {
        if (FindDescendant<TextBox>(QueryBox) is { } textBox)
        {
            textBox.SelectAll();
        }
    }

    private static T? FindDescendant<T>(DependencyObject parent)
        where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is T match)
            {
                return match;
            }

            if (FindDescendant<T>(child) is { } descendant)
            {
                return descendant;
            }
        }

        return null;
    }

    private void CancelDebounce()
    {
        _debounceCancellation?.Cancel();
        _debounceCancellation?.Dispose();
        _debounceCancellation = null;
    }

    private void ConfigureWindowBehavior()
    {
        if (_appWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsResizable = false;
            presenter.IsMinimizable = false;
            presenter.IsMaximizable = false;
        }
    }

    private void SearchWindow_Activated(object sender, WindowActivatedEventArgs args) =>
        SearchActivityGlow.SetMotionEnabled(args.WindowActivationState != WindowActivationState.Deactivated);

    private async void SearchWindow_Closed(object sender, WindowEventArgs args)
    {
        _closing = true;
        Activated -= SearchWindow_Activated;
        SearchActivityGlow.SetMotionEnabled(false);
        SearchActivityProgressRing.IsActive = false;
        SearchActivityGlow.Visibility = Visibility.Collapsed;
        SearchActivityStatus.Visibility = Visibility.Collapsed;
        _lifetimeCancellation.Cancel();
        CancelDebounce();
        _queryCancellation?.Cancel();
        _previewSelection.Invalidate();
        CancelPreviewImageLoad();
        PreviewScreenshotImage.Source = null;
        PreviewImageProgressRing.IsActive = false;
        _ = await _placement.TrySaveForCloseAsync(CancellationToken.None);
        _titleBar.Dispose();
        _placement.Dispose();
        _queryCancellation?.Dispose();
        _lifetimeCancellation.Dispose();
    }
}
