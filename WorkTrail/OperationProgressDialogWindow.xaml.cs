// SPDX-License-Identifier: MIT

using Microsoft.UI;
using System.Diagnostics;
using System.Globalization;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using WorkTrail.Application;
using WorkTrail.Services;

namespace WorkTrail;

/// <summary>Shows an owned operation with optional phase progress read from the shared facade.</summary>
internal sealed partial class OperationProgressDialogWindow : Window
{
    private const int LogicalWidth = 520;
    private const int LogicalHeight = 340;
    private const int LogicalScreenMargin = 24;
    private readonly Func<CancellationToken, Task> _operation;
    private readonly TaskCompletionSource _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly WindowSurfaceLifecycle _lifecycle = new();
    private readonly AppWindow _appWindow;
    private readonly CustomTitleBarController _titleBar;
    private readonly WindowPlacementService _placement;
    private bool _loaded;
    private bool _allowClose;
    private bool _closed;
    private readonly IWorkTrailApplication _application;
    private readonly Guid? _archiveOperationId;
    private readonly Guid? _retentionOperationId;
    private readonly LocalizationService _strings;
    private readonly DispatcherQueueTimer _progressTimer;
    private readonly Stopwatch _elapsed = new();
    private bool _readingProgress;

    /// <summary>Creates an owned progress surface whose supplied operation uses the shared application facade.</summary>
    /// <param name="application">Shared facade supplying phase progress.</param>
    /// <param name="theme">Actual theme used by the owner.</param>
    /// <param name="title">Localized operation heading.</param>
    /// <param name="description">Localized operation explanation.</param>
    /// <param name="language">Resolved UI language for progress labels.</param>
    /// <param name="ownerAppWindow">Native owner used to place this transient surface.</param>
    /// <param name="ownerHandle">Valid native owner handle.</param>
    /// <param name="operation">Request to execute after the surface is visible.</param>
    /// <param name="archiveOperationId">Archive job to observe, if any.</param>
    /// <param name="retentionOperationId">Retention job to observe, if any.</param>
    /// <exception cref="ArgumentException">A required owner handle or operation caption is invalid.</exception>
    internal OperationProgressDialogWindow(
        IWorkTrailApplication application,
        ElementTheme theme,
        string title,
        string description,
        string language,
        AppWindow ownerAppWindow,
        IntPtr ownerHandle,
        Func<CancellationToken, Task> operation,
        Guid? archiveOperationId = null,
        Guid? retentionOperationId = null)
    {
        ArgumentNullException.ThrowIfNull(application);
        ArgumentNullException.ThrowIfNull(ownerAppWindow);
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentException.ThrowIfNullOrWhiteSpace(description);
        ArgumentException.ThrowIfNullOrWhiteSpace(language);
        _operation = operation ?? throw new ArgumentNullException(nameof(operation));
        _application = application;
        _archiveOperationId = archiveOperationId;
        _retentionOperationId = retentionOperationId;
        _strings = new LocalizationService(language);
        if (ownerHandle == IntPtr.Zero)
        {
            throw new ArgumentException("The progress window requires a valid owner handle.", nameof(ownerHandle));
        }

        InitializeComponent();
        Title = title;
        RootGrid.RequestedTheme = theme;
        RootGrid.Language = language;
        DialogTitleText.Text = title;
        DescriptionText.Text = description;
        AutomationProperties.SetName(RootGrid, title);
        AutomationProperties.SetName(DescriptionText, description);
        AutomationProperties.SetName(OperationProgress, title);
        PhaseText.Text = _strings.Translate("Archive.Progress.Waiting");
        PhaseText.Visibility = archiveOperationId.HasValue || retentionOperationId.HasValue ? Visibility.Visible : Visibility.Collapsed;
        _progressTimer = DispatcherQueue.CreateTimer();
        _progressTimer.Interval = TimeSpan.FromSeconds(1);
        _progressTimer.Tick += ProgressTimer_Tick;
        WindowHandle = WinRT.Interop.WindowNative.GetWindowHandle(this);
        _appWindow = AppWindow.GetFromWindowId(Win32Interop.GetWindowIdFromWindow(WindowHandle));
        _titleBar = new CustomTitleBarController(
            this,
            _appWindow,
            RootGrid,
            TitleDragRegion,
            TitleBarLeftInsetColumn,
            TitleBarRightInsetColumn,
            static () => Array.Empty<FrameworkElement>());
        _placement = new WindowPlacementService(
            application,
            this,
            _appWindow,
            WindowStateKeys.Dialog,
            LogicalWidth,
            LogicalHeight,
            LogicalScreenMargin,
            ownerAppWindow.Id,
            deferNativeClose: false);
        WindowInteropService.SetOwner(WindowHandle, ownerHandle);
        if (_appWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsResizable = false;
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
            presenter.IsAlwaysOnTop = true;
            presenter.SetBorderAndTitleBar(hasBorder: true, hasTitleBar: false);
        }

        _lifecycle.InitializationFailed += CompleteWithException;
        _appWindow.Closing += AppWindow_Closing;
        Closed += OperationProgressDialogWindow_Closed;
    }

    /// <summary>Gets the native handle used by the shared modal queue.</summary>
    internal IntPtr WindowHandle { get; }

    /// <summary>Shows the surface and observes the operation until completion, failure, or shutdown.</summary>
    internal Task ShowAsync()
    {
        _lifecycle.StartInitialization(RunOperationAsync);
        WindowInteropService.MakeTopmostWithoutActivation(WindowHandle);
        Activate();
        return _completion.Task;
    }

    /// <summary>Allows owner or application shutdown to cancel the request and release the modal surface.</summary>
    internal void CloseForShutdown()
    {
        if (_closed)
        {
            return;
        }

        _allowClose = true;
        _lifecycle.Cancel();
        Close();
    }

    /// <summary>Releases placement and any remaining window after the modal session ends.</summary>
    internal void DisposePlacement()
    {
        if (!_closed)
        {
            CloseForShutdown();
        }
        _placement.Dispose();
    }

    private void RootGrid_Loaded(object sender, RoutedEventArgs e)
    {
        if (_loaded || _closed)
        {
            return;
        }

        _loaded = true;
        try
        {
            // Progress is transient: use shared DPI-aware bounds without reading or writing another dialog's geometry.
            _placement.ApplyDefaultBounds(RootGrid);
            _lifecycle.SignalLoaded();
        }
        catch (Exception exception)
        {
            // Failed placement is reported to the caller; the operation must not run on an unavailable surface.
            CompleteWithException(exception);
        }
    }

    private async Task RunOperationAsync(CancellationToken cancellationToken)
    {
        await _lifecycle.WaitUntilLoadedAsync(cancellationToken);
        var visibleFrame = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        // Defer the facade call until Loaded has returned and WinUI has had a chance to compose progress.
        if (!DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () => visibleFrame.TrySetResult()))
        {
            throw new InvalidOperationException("The progress operation could not be queued.");
        }
        await visibleFrame.Task.WaitAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        _elapsed.Start();
        _progressTimer.Start();
        await _operation(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        _allowClose = true;
        _completion.TrySetResult();
        Close();
    }

    private void CompleteWithException(Exception exception)
    {
        if (_closed)
        {
            return;
        }

        // The caller owns the error message; close progress and propagate the observed failure unchanged.
        _allowClose = true;
        _completion.TrySetException(exception);
        Close();
    }

    private async void ProgressTimer_Tick(DispatcherQueueTimer sender, object args)
    {
        if (_closed) return;
        ElapsedText.Text = _strings.Format("Archive.Progress.Elapsed", _elapsed.Elapsed.ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture));
        if ((_archiveOperationId is null && _retentionOperationId is null) || _readingProgress) return;
        _readingProgress = true;
        try
        {
            if (_retentionOperationId is { } retentionId)
            {
                var status = await _application.GetRetentionStatusAsync(_lifecycle.Token);
                if (_closed) return;
                if (!status.Succeeded) throw new InvalidOperationException("Retention progress is unavailable.");
                if (status.Value?.Progress is { } retention && retention.OperationId == retentionId)
                {
                    PhaseText.Text = _strings.Translate($"Operations.Retention.Phase.{retention.Phase}");
                    AutomationProperties.SetName(PhaseText, PhaseText.Text);
                    OperationProgress.IsIndeterminate = retention.TotalItems <= 0;
                    if (retention.TotalItems > 0) OperationProgress.Value = 100d * retention.CompletedItems / retention.TotalItems;
                    CountText.Text = _strings.Format("Archive.Progress.Items", retention.CompletedItems, retention.TotalItems);
                }
                return;
            }
            var id = _archiveOperationId!.Value;
            var result = await _application.GetDataArchiveProgressAsync(new DataArchiveProgressRequest(id), _lifecycle.Token);
            if (_closed) return;
            if (!result.Succeeded) throw new InvalidOperationException("Archive progress is unavailable.");
            if (result.Value is not { } progress) return;
            PhaseText.Text = _strings.Translate($"Archive.Progress.{progress.Phase}");
            AutomationProperties.SetName(PhaseText, PhaseText.Text);
            var hasTotal = progress.TotalItems is > 0;
            OperationProgress.IsIndeterminate = !hasTotal;
            if (hasTotal) OperationProgress.Value = 100d * progress.CompletedItems / progress.TotalItems!.Value;
            CountText.Text = hasTotal
                ? _strings.Format("Archive.Progress.Items", progress.CompletedItems, progress.TotalItems)
                : progress.CompletedItems > 0 ? _strings.Format("Archive.Progress.Processed", progress.CompletedItems) : string.Empty;
        }
        catch (OperationCanceledException) when (_closed || _lifecycle.Token.IsCancellationRequested)
        {
            // Closing the modal surface cancels polling; it must not render into the detached tree.
        }
        catch (Exception)
        {
            // Progress reads are independent: a reporting failure must not cancel a durable import.
            if (!_closed)
            {
                PhaseText.Text = _strings.Translate("Archive.Progress.Unavailable");
                CountText.Text = string.Empty;
                OperationProgress.IsIndeterminate = true;
            }
        }
        finally { _readingProgress = false; }
    }

    private void AppWindow_Closing(AppWindow sender, AppWindowClosingEventArgs args) =>
        args.Cancel = !_allowClose;

    private void OperationProgressDialogWindow_Closed(object sender, WindowEventArgs args)
    {
        _closed = true;
        _progressTimer.Stop();
        _progressTimer.Tick -= ProgressTimer_Tick;
        _elapsed.Stop();
        _appWindow.Closing -= AppWindow_Closing;
        Closed -= OperationProgressDialogWindow_Closed;
        _lifecycle.Cancel();
        _completion.TrySetCanceled(_lifecycle.Token);
        _titleBar.Dispose();
        _lifecycle.Dispose();
    }
}
