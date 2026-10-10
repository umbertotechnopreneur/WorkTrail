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
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using WorkTrail.Application;
using WorkTrail.Presentation;
using WorkTrail.Services;

namespace WorkTrail;

/// <summary>Displays selectable OCR text with debounced, local highlighting in a reusable Mica window.</summary>
internal sealed partial class OcrTextWindow : Window
{
    private const int LogicalWindowWidth = 900;
    private const int LogicalWindowHeight = 680;
    private const int LogicalScreenMargin = 24;
    private const int MinimumQueryLength = 2;
    private static readonly TimeSpan SearchDebounce = TimeSpan.FromMilliseconds(400);
    private readonly AppWindow _appWindow;
    private readonly CustomTitleBarController _titleBar;
    private readonly WindowPlacementService _placement;
    private readonly DispatcherQueueTimer _searchTimer;
    private readonly CancellationTokenSource _lifetimeCancellation = new();
    private LocalizationService _strings;
    private string _ocrText = string.Empty;

    /// <summary>Creates an OCR text window anchored to the screenshot gallery display.</summary>
    internal OcrTextWindow(
        IWorkTrailApplication application,
        AppWindow ownerAppWindow,
        string ocrText,
        ElementTheme theme,
        string language)
    {
        ArgumentNullException.ThrowIfNull(application);
        ArgumentNullException.ThrowIfNull(ownerAppWindow);
        ArgumentNullException.ThrowIfNull(ocrText);
        ArgumentException.ThrowIfNullOrWhiteSpace(language);
        _strings = new LocalizationService(language);
        InitializeComponent();
        _appWindow = AppWindow.GetFromWindowId(
            Win32Interop.GetWindowIdFromWindow(WinRT.Interop.WindowNative.GetWindowHandle(this)));
        _titleBar = new CustomTitleBarController(
            this,
            _appWindow,
            RootGrid,
            TitleBarDragRegion,
            TitleBarLeftInsetColumn,
            TitleBarRightInsetColumn,
            () => []);
        _placement = new WindowPlacementService(
            application,
            this,
            _appWindow,
            WindowStateKeys.OcrText,
            LogicalWindowWidth,
            LogicalWindowHeight,
            LogicalScreenMargin,
            ownerAppWindow.Id);
        _searchTimer = DispatcherQueue.CreateTimer();
        _searchTimer.Interval = SearchDebounce;
        _searchTimer.IsRepeating = false;
        _searchTimer.Tick += SearchTimer_Tick;

        UpdateContent(ocrText, theme, language);
        Closed += OcrTextWindow_Closed;
    }

    /// <summary>Replaces the OCR text and resets the themed, localized search surface for a new snapshot.</summary>
    public void UpdateContent(string ocrText, ElementTheme theme, string language)
    {
        ArgumentNullException.ThrowIfNull(ocrText);
        ArgumentException.ThrowIfNullOrWhiteSpace(language);

        _searchTimer.Stop();
        _ocrText = ocrText;
        _strings = new LocalizationService(language);
        RootGrid.RequestedTheme = theme;
        _titleBar.ApplyTheme(theme == ElementTheme.Default ? RootGrid.ActualTheme : theme);
        UiLocalization.Apply(RootGrid, _strings);

        var windowTitle = T("Screenshots.OcrText.WindowTitle");
        Title = windowTitle;
        TitleBarText.Text = windowTitle.ToUpper(_strings.Culture);
        SearchBox.PlaceholderText = T("Screenshots.OcrText.Search.Placeholder");
        AutomationProperties.SetName(SearchBox, T("Screenshots.OcrText.Search.AccessibleName"));
        ToolTipService.SetToolTip(SearchBox, T("Screenshots.OcrText.Search.Tooltip"));

        SearchBox.Text = string.Empty;
        OcrTextBlock.Text = _ocrText;
        ClearHighlights();
    }

    private async void RootGrid_Loaded(object sender, RoutedEventArgs e)
    {
        _placement.ApplyDefaultBounds(RootGrid);
        try
        {
            await _placement.RestoreOrCenterAsync(RootGrid, _lifetimeCancellation.Token);
        }
        catch (OperationCanceledException) when (_lifetimeCancellation.IsCancellationRequested)
        {
            return;
        }
        catch (InvalidOperationException) when (_lifetimeCancellation.IsCancellationRequested)
        {
            return;
        }

    }

    private void SearchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        _searchTimer.Stop();
        ClearHighlights();
        if (sender.Text.Trim().Length < MinimumQueryLength)
        {
            return;
        }

        _searchTimer.Start();
    }

    private void SearchTimer_Tick(DispatcherQueueTimer sender, object args)
    {
        sender.Stop();
        ApplyHighlights(SearchBox.Text.Trim());
    }

    private void ApplyHighlights(string query)
    {
        ClearHighlights();
        if (query.Length < MinimumQueryLength)
        {
            return;
        }

        var matches = OcrTextSearch.FindMatches(_ocrText, query);
        if (matches.Count == 0)
        {
            return;
        }

        var highlighter = new TextHighlighter
        {
            Background = new SolidColorBrush(Colors.Yellow),
            Foreground = new SolidColorBrush(Colors.Black)
        };
        foreach (var match in matches)
        {
            highlighter.Ranges.Add(new TextRange
            {
                StartIndex = match.StartIndex,
                Length = match.Length
            });
        }

        OcrTextBlock.TextHighlighters.Add(highlighter);
    }

    private void ClearHighlights() => OcrTextBlock.TextHighlighters.Clear();

    private async void OcrTextWindow_Closed(object sender, WindowEventArgs args)
    {
        _lifetimeCancellation.Cancel();
        _searchTimer.Stop();
        _searchTimer.Tick -= SearchTimer_Tick;
        _titleBar.Dispose();
        _ = await _placement.TrySaveForCloseAsync(CancellationToken.None);
        _placement.Dispose();
        _lifetimeCancellation.Dispose();
    }

    private string T(string key) => _strings.Translate(key);
}
