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
using WorkTrail.Application;
using WorkTrail.Services;

namespace WorkTrail;

/// <summary>Displays a cancellable Acrylic progress surface for the two derived local search indexes.</summary>
internal sealed partial class SearchIndexingWindow : Window
{
    private const int LogicalWidth = 700;
    private const int LogicalHeight = 520;
    private const int LogicalScreenMargin = 24;
    private readonly IWorkTrailApplication _application;
    private readonly AppWindow _appWindow;
    private readonly CustomTitleBarController _titleBar;
    private readonly WindowPlacementService _placement;
    private readonly CancellationTokenSource _lifetimeCancellation = new();
    private LocalizationService _strings;
    private CancellationTokenSource? _rebuildCancellation;
    private IndexingWindowState _state = IndexingWindowState.Running;
    private int _documentCount;
    private bool _started;
    private bool _closing;

    /// <summary>Creates an owned, topmost indexing window anchored to the settings display.</summary>
    internal SearchIndexingWindow(
        IWorkTrailApplication application,
        ElementTheme theme,
        string language,
        AppWindow ownerAppWindow,
        IntPtr ownerHandle)
    {
        _application = application ?? throw new ArgumentNullException(nameof(application));
        ArgumentNullException.ThrowIfNull(ownerAppWindow);
        if (ownerHandle == IntPtr.Zero)
        {
            throw new ArgumentException("The indexing window requires a valid owner handle.", nameof(ownerHandle));
        }

        _strings = new LocalizationService(language);
        InitializeComponent();
        var windowHandle = WinRT.Interop.WindowNative.GetWindowHandle(this);
        _appWindow = AppWindow.GetFromWindowId(Win32Interop.GetWindowIdFromWindow(windowHandle));
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
            WindowStateKeys.SearchIndexing,
            LogicalWidth,
            LogicalHeight,
            LogicalScreenMargin,
            ownerAppWindow.Id);
        WindowInteropService.SetOwner(windowHandle, ownerHandle);
        ConfigureWindowBehavior();
        ApplyTheme(theme);
        ApplyLanguage(language);
        _placement.ApplyDefaultBounds(RootGrid);
        Closed += SearchIndexingWindow_Closed;
    }

    /// <summary>Applies the current effective application theme to the indexing surface.</summary>
    internal void ApplyTheme(ElementTheme theme)
    {
        RootGrid.RequestedTheme = theme;
        _titleBar.ApplyTheme(theme == ElementTheme.Default ? RootGrid.ActualTheme : theme);
    }

    /// <summary>Relocalizes static and current-state content without restarting indexing.</summary>
    internal void ApplyLanguage(string language)
    {
        _strings = new LocalizationService(language);
        UiLocalization.Apply(RootGrid, _strings);
        Title = T("SearchIndex.Title");
        AutomationProperties.SetName(CancelOrCloseButton, T(_state is IndexingWindowState.Running or IndexingWindowState.Cancelling
            ? "SearchIndex.Cancel"
            : "SearchIndex.Close"));
        RenderState();
    }

    private async void RootGrid_Loaded(object sender, RoutedEventArgs e)
    {
        _placement.ApplyDefaultBounds(RootGrid);
        try
        {
            await _placement.RestoreOrCenterAsync(RootGrid, _lifetimeCancellation.Token);
        }
        catch (OperationCanceledException) when (_closing)
        {
            return;
        }

        if (!_started && !_closing)
        {
            _started = true;
            if (!DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, RunIndexingFromVisibleWindow))
            {
                SetState(IndexingWindowState.Failed);
            }
        }
    }

    /// <summary>Starts the facade request only after Loaded returns so WinUI can compose the visible window first.</summary>
    private async void RunIndexingFromVisibleWindow() => await RunIndexingAsync();

    private async Task RunIndexingAsync()
    {
        using var rebuildCancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetimeCancellation.Token);
        _rebuildCancellation = rebuildCancellation;
        SetState(IndexingWindowState.Running);
        try
        {
            // The facade owns data access and Lucene mutation; closing or Cancel disconnects the same request through IPC.
            var result = await _application.RebuildSearchIndexAsync(rebuildCancellation.Token);
            if (_closing)
            {
                return;
            }

            if (result.Succeeded && result.Value is { } count)
            {
                _documentCount = count;
                SetState(IndexingWindowState.Completed);
            }
            else if (result.Code == "operation.cancelled" || rebuildCancellation.IsCancellationRequested)
            {
                SetState(IndexingWindowState.Cancelled);
            }
            else
            {
                SetState(IndexingWindowState.Failed);
            }
        }
        catch (OperationCanceledException) when (!_closing)
        {
            SetState(IndexingWindowState.Cancelled);
        }
        catch (OperationCanceledException) when (_closing)
        {
        }
        catch (Exception) when (!_closing)
        {
            // The previous committed derived indexes remain available when the facade reports an unexpected failure.
            SetState(IndexingWindowState.Failed);
        }
        catch (Exception) when (_closing)
        {
        }
        finally
        {
            if (ReferenceEquals(_rebuildCancellation, rebuildCancellation))
            {
                _rebuildCancellation = null;
            }
        }
    }

    private void CancelOrCloseButton_Click(object sender, RoutedEventArgs e)
    {
        if (_state == IndexingWindowState.Running)
        {
            SetState(IndexingWindowState.Cancelling);
            _rebuildCancellation?.Cancel();
            return;
        }

        if (_state != IndexingWindowState.Cancelling)
        {
            Close();
        }
    }

    private void SetState(IndexingWindowState state)
    {
        _state = state;
        RenderState();
    }

    private void RenderState()
    {
        var active = _state is IndexingWindowState.Running or IndexingWindowState.Cancelling;
        var completed = _state == IndexingWindowState.Completed;
        OverallProgressBar.IsIndeterminate = active;
        ResultsProgressBar.IsIndeterminate = active;
        OverallProgressBar.Value = completed ? 100 : 0;
        ResultsProgressBar.Value = completed ? 100 : 0;
        CancelOrCloseButton.IsEnabled = _state != IndexingWindowState.Cancelling;

        var statusKey = _state switch
        {
            IndexingWindowState.Running => "SearchIndex.Status.Running",
            IndexingWindowState.Cancelling => "SearchIndex.Status.Cancelling",
            IndexingWindowState.Completed => "SearchIndex.Status.Completed",
            IndexingWindowState.Cancelled => "SearchIndex.Status.Cancelled",
            _ => "SearchIndex.Status.Failed"
        };
        ResultsStateText.Text = T(statusKey);

        switch (_state)
        {
            case IndexingWindowState.Running:
                HeadingText.Text = T("SearchIndex.Heading");
                DescriptionText.Text = T("SearchIndex.Description");
                SummaryText.Text = T("SearchIndex.Summary.Running");
                SetActionLabel("SearchIndex.Cancel");
                break;
            case IndexingWindowState.Cancelling:
                HeadingText.Text = T("SearchIndex.Cancelling.Title");
                DescriptionText.Text = T("SearchIndex.Cancelling.Description");
                SummaryText.Text = T("SearchIndex.Summary.Cancelling");
                SetActionLabel("SearchIndex.Cancel");
                break;
            case IndexingWindowState.Completed:
                HeadingText.Text = T("SearchIndex.Completed.Title");
                DescriptionText.Text = _strings.Format("SearchIndex.Completed.Description", _documentCount);
                SummaryText.Text = T("SearchIndex.Summary.Completed");
                SetActionLabel("SearchIndex.Close");
                break;
            case IndexingWindowState.Cancelled:
                HeadingText.Text = T("SearchIndex.Cancelled.Title");
                DescriptionText.Text = T("SearchIndex.Cancelled.Description");
                SummaryText.Text = T("SearchIndex.Summary.Cancelled");
                SetActionLabel("SearchIndex.Close");
                break;
            default:
                HeadingText.Text = T("SearchIndex.Failed.Title");
                DescriptionText.Text = T("SearchIndex.Failed.Description");
                SummaryText.Text = T("SearchIndex.Summary.Failed");
                SetActionLabel("SearchIndex.Close");
                break;
        }
    }

    private void SetActionLabel(string key)
    {
        var label = T(key);
        CancelOrCloseButton.Content = label;
        AutomationProperties.SetName(CancelOrCloseButton, label);
    }

    private void ConfigureWindowBehavior()
    {
        if (_appWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsResizable = false;
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
            presenter.IsAlwaysOnTop = true;
        }

    }

    private async void SearchIndexingWindow_Closed(object sender, WindowEventArgs args)
    {
        _closing = true;
        _rebuildCancellation?.Cancel();
        _lifetimeCancellation.Cancel();
        _ = await _placement.TrySaveForCloseAsync(CancellationToken.None);
        _titleBar.Dispose();
        _placement.Dispose();
        _lifetimeCancellation.Dispose();
    }

    private string T(string key) => _strings.Translate(key);

    private enum IndexingWindowState
    {
        Running,
        Cancelling,
        Completed,
        Cancelled,
        Failed
    }
}
