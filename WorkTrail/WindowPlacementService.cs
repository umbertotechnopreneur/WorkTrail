// SPDX-License-Identifier: MIT

using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using WorkTrail.Application;
using WorkTrail.Presentation;
using WorkTrail.Services;
using Windows.Graphics;

namespace WorkTrail;

/// <summary>Applies default bounds and delegates persisted placement to the shared application facade.</summary>
internal sealed class WindowPlacementService : IDisposable
{
    private static readonly HashSet<WindowPlacementService> ActivePlacements = [];
    private static bool s_preservingWorkspace;
    private static Task? s_shutdownPreparation;
    private const uint WmGetMinMaxInfo = 0x0024;
    private static int s_nextSubclassId;
    private readonly IWorkTrailApplication _application;
    private readonly Window _window;
    private readonly FrameworkElement _root;
    private readonly AppWindow _appWindow;
    private readonly IntPtr _windowHandle;
    private readonly string _windowKey;
    private readonly int _logicalDefaultWidth;
    private readonly int _logicalDefaultHeight;
    private readonly int _logicalScreenMargin;
    private readonly WindowMinimumSize _logicalMinimumSize;
    private readonly WindowId _displayAnchorId;
    private readonly NativeWindowSubclassProc _subclassProc;
    private readonly nuint _subclassId;
    private readonly IWindowSnappingRegistration? _snapping;
    private bool _restoreAttempted;
    private bool _subclassInstalled;
    private bool _disposed;
    private SizeInt32 _minimumPhysicalSize = new(1, 1);
    private double _rasterizationScale = 1d;
    private double _appliedDisplayScale;
    private XamlRoot? _xamlRoot;
    private bool _dpiRefreshQueued;
    private bool _dpiRefreshAgain;
    private readonly DispatcherQueueTimer _saveTimer;
    private Task _pendingSave = Task.CompletedTask;
    private Task _pendingClose = Task.CompletedTask;
    private Task<bool>? _restoreTask;
    private bool _placementReady;
    private bool _closeSaveStarted;
    private bool _closeSaveCompleted;
    private bool _shutdownPrepared;
    private bool _failureNotificationPending;

    /// <summary>Lets the composition root report recoverable persistence failures on the owning window.</summary>
    internal static event Func<Window, string, Exception, Task>? PersistenceFailed;

    /// <summary>Lets the composition root report a failed native snap without interrupting free movement.</summary>
    internal static event Func<Window, Exception, Task>? SnappingFailed;

    /// <summary>Snapshots the already-created peers on the UI dispatcher without reopening persisted workspace entries.</summary>
    internal static IReadOnlyList<long> GetOpenPeerWindowHandles(long mainWindowHandle) => s_preservingWorkspace
        ? []
        : ActivePlacements
            .Where(placement => !placement._disposed && placement._placementReady
                && !placement._closeSaveStarted && !placement._shutdownPrepared
                && placement._windowHandle.ToInt64() != mainWindowHandle)
            .Select(placement => placement._windowHandle.ToInt64())
            .ToArray();

    internal WindowPlacementService(
        IWorkTrailApplication application,
        Window window,
        AppWindow appWindow,
        string windowKey,
        int logicalDefaultWidth,
        int logicalDefaultHeight,
        int logicalScreenMargin,
        WindowId? displayAnchorId = null,
        bool deferNativeClose = true)
    {
        _application = application ?? throw new ArgumentNullException(nameof(application));
        _window = window ?? throw new ArgumentNullException(nameof(window));
        _root = window.Content as FrameworkElement
            ?? throw new ArgumentException("Window content must be a framework element.", nameof(window));
        _appWindow = appWindow ?? throw new ArgumentNullException(nameof(appWindow));
        _windowHandle = WinRT.Interop.WindowNative.GetWindowHandle(_window);
        _windowKey = string.IsNullOrWhiteSpace(windowKey) ? throw new ArgumentException("A window key is required.", nameof(windowKey)) : windowKey;
        _logicalDefaultWidth = logicalDefaultWidth > 0 ? logicalDefaultWidth : throw new ArgumentOutOfRangeException(nameof(logicalDefaultWidth));
        _logicalDefaultHeight = logicalDefaultHeight > 0 ? logicalDefaultHeight : throw new ArgumentOutOfRangeException(nameof(logicalDefaultHeight));
        _logicalScreenMargin = logicalScreenMargin >= 0 ? logicalScreenMargin : throw new ArgumentOutOfRangeException(nameof(logicalScreenMargin));
        _logicalMinimumSize = WindowStateService.GetMinimumSize(_windowKey);
        _displayAnchorId = displayAnchorId ?? _appWindow.Id;
        _subclassProc = WindowSubclassProc;
        _subclassId = (nuint)Interlocked.Increment(ref s_nextSubclassId);
        _appliedDisplayScale = WindowInteropService.GetRasterizationScale(_windowHandle);
        _rasterizationScale = _appliedDisplayScale;
        InstallMinimumSizeSubclass();
        _root.Loaded += Root_Loaded;
        _window.Closed += Window_Closed;
        _appWindow.Changed += AppWindow_Changed;
        if (deferNativeClose)
        {
            _appWindow.Closing += AppWindow_Closing;
        }
        _saveTimer = _root.DispatcherQueue.CreateTimer();
        _saveTimer.Interval = TimeSpan.FromMilliseconds(500);
        _saveTimer.IsRepeating = false;
        _saveTimer.Tick += SaveTimer_Tick;
        ActivePlacements.Add(this);
        AttachXamlRoot();
        try
        {
            // Only independent work surfaces participate, both as moving windows and alignment targets.
            // Dialogs, About, licenses and temporary operations retain native free movement.
            if (_windowKey is WindowStateKeys.Main or WindowStateKeys.WorldClocks or WindowStateKeys.Sensors
                or WindowStateKeys.WorldMap or WindowStateKeys.LunarPhase or WindowStateKeys.LocalSky
                or WindowStateKeys.AstronomyAgenda or WindowStateKeys.CelestialMap or WindowStateKeys.Search
                or WindowStateKeys.Screenshots or WindowStateKeys.OcrText or WindowStateKeys.Schedule
                or WindowStateKeys.ReportExport)
            {
                _snapping = _application.RegisterWindowSnapping(_windowHandle.ToInt64(), ReportSnappingFailure);
            }
        }
        catch
        {
            // Do not retain event handlers or the existing minimum-size hook after a partial construction failure.
            Dispose();
            throw;
        }
    }

    private void ReportSnappingFailure(Exception exception)
    {
        Trace.TraceError("Window snapping failed. WindowKey={0} Exception={1}", _windowKey, exception);
        // A native callback only queues presentation; a modal notification must never run inside the drag hook.
        if (!_root.DispatcherQueue.TryEnqueue(async () =>
        {
            try
            {
                if (!_disposed && SnappingFailed is { } report) await report(_window, exception);
            }
            catch (Exception notificationFailure)
            {
                // Closure or an unavailable dialog must not turn a recoverable drag failure into a dispatcher crash.
                Trace.TraceError("Window snapping notification failed. WindowKey={0} Exception={1}", _windowKey, notificationFailure);
            }
        }))
        {
            // A stopping dispatcher cannot show UI; diagnostics still preserve the explicit failure.
            Trace.TraceError("Window snapping notification could not be queued during dispatcher shutdown.");
        }
    }

    /// <summary>Occurs after native and XAML DPI agree, before final layout and work-area clamping.</summary>
    internal event Action? DpiChanged;

    /// <summary>Identifies native DPI resizes before WinUI has delivered its corresponding layout event.</summary>
    internal bool IsDpiChangePending => !_disposed
        && (_dpiRefreshQueued
            || Math.Abs(WindowInteropService.GetRasterizationScale(_windowHandle) - _appliedDisplayScale) >= 0.001d
            || (_xamlRoot is not null && Math.Abs(_xamlRoot.RasterizationScale - _appliedDisplayScale) >= 0.001d));

    private void Root_Loaded(object sender, RoutedEventArgs args)
    {
        AttachXamlRoot();
        QueueDpiRefresh();
    }

    private void Window_Closed(object sender, WindowEventArgs args) => Dispose();

    private void AppWindow_Changed(AppWindow sender, AppWindowChangedEventArgs args)
    {
        QueueDpiRefresh();
        if (_placementReady && !_disposed && !_closeSaveStarted && !s_preservingWorkspace
            && (args.DidPositionChange || args.DidSizeChange || args.DidVisibilityChange))
        {
            _saveTimer.Stop();
            _saveTimer.Start();
        }
    }

    private async void SaveTimer_Tick(DispatcherQueueTimer sender, object args)
    {
        try
        {
            await SaveAsync(CancellationToken.None);
        }
        catch (Exception exception)
        {
            // Event callbacks must report failed writes without terminating the tracking runtime.
            await NotifyPersistenceFailureAsync(exception);
        }
    }

    private async void AppWindow_Closing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        // Main owns its confirmation and flushes the whole workspace before shutdown.
        if (_windowKey == WindowStateKeys.Main || _closeSaveCompleted || _shutdownPrepared)
        {
            return;
        }

        args.Cancel = true;
        if (_closeSaveStarted || s_preservingWorkspace)
        {
            return;
        }

        _closeSaveStarted = true;
        _saveTimer.Stop();
        _pendingClose = CloseAfterSaveAsync();
        try
        {
            await _pendingClose;
        }
        catch (Exception exception)
        {
            // CloseAfterSaveAsync has restored the flags, so the live window can be closed again later.
            await NotifyPersistenceFailureAsync(exception);
        }
    }

    private async Task NotifyPersistenceFailureAsync(Exception exception)
    {
        Trace.TraceError("Window persistence failed. WindowKey={0} Exception={1}", _windowKey, exception);
        if (_disposed || _failureNotificationPending || PersistenceFailed is not { } reportFailure)
        {
            return;
        }

        _failureNotificationPending = true;
        try
        {
            await reportFailure(_window, _windowKey, exception);
        }
        catch (Exception notificationException)
        {
            // A closed owner or unavailable notification surface must not turn the original save failure into a crash.
            Trace.TraceError("Window persistence notification failed. WindowKey={0} Exception={1}", _windowKey, notificationException);
        }
        finally
        {
            _failureNotificationPending = false;
        }
    }

    private async Task CloseAfterSaveAsync()
    {
        try
        {
            await QueueSaveAsync(CancellationToken.None);
            if (WorkspaceWindowState.IsRestorable(_windowKey))
            {
                await SaveOpenStateAsync(false, CancellationToken.None);
            }

            _closeSaveCompleted = true;
            // Core can complete synchronously; defer the final close until the original native Closing event unwinds.
            if (!_root.DispatcherQueue.TryEnqueue(() =>
                {
                    if (!_disposed) _window.Close();
                }))
            {
                throw new InvalidOperationException("Unable to queue the persisted window close.");
            }
        }
        catch
        {
            // A failed save keeps the native handle alive so closing can be retried safely.
            _closeSaveStarted = false;
            _closeSaveCompleted = false;
            throw;
        }
    }

    /// <summary>Flushes every live window before shutdown closes owners and their dependent windows.</summary>
    internal static Task PrepareForShutdownAsync()
    {
        if (s_preservingWorkspace)
        {
            return s_shutdownPreparation ?? Task.CompletedTask;
        }

        s_preservingWorkspace = true;
        return s_shutdownPreparation = FlushWorkspaceAsync();
    }

    private static async Task FlushWorkspaceAsync()
    {
        try
        {
            // Newly opened child surfaces join the next pass while existing native handles remain protected.
            while (ActivePlacements.Any(static placement => !placement._shutdownPrepared))
            {
                foreach (var placement in ActivePlacements.Where(static placement => !placement._shutdownPrepared).ToArray())
                {
                    if (placement._disposed) continue;
                    placement._saveTimer.Stop();
                    if (placement._closeSaveStarted)
                    {
                        // A manual close keeps its explicit closed flag even when application exit begins mid-save.
                        await placement._pendingClose;
                    }
                    else
                    {
                        await placement.QueueSaveAsync(CancellationToken.None);
                    }

                    placement._shutdownPrepared = true;
                }
            }
        }
        catch
        {
            // Preserve the running workspace when any persistence operation fails.
            s_preservingWorkspace = false;
            foreach (var placement in ActivePlacements)
            {
                placement._shutdownPrepared = false;
            }

            throw;
        }
    }

    /// <summary>Stops queued writes after an explicit data reset has discarded the persisted workspace.</summary>
    internal static void DiscardForReset()
    {
        s_preservingWorkspace = true;
        foreach (var placement in ActivePlacements)
        {
            placement._saveTimer.Stop();
            placement._shutdownPrepared = true;
        }
    }

    private void XamlRoot_Changed(XamlRoot sender, XamlRootChangedEventArgs args) => QueueDpiRefresh();

    private void AttachXamlRoot()
    {
        if (ReferenceEquals(_xamlRoot, _root.XamlRoot))
        {
            return;
        }

        if (_xamlRoot is not null)
        {
            _xamlRoot.Changed -= XamlRoot_Changed;
        }

        _xamlRoot = _root.XamlRoot;
        if (_xamlRoot is not null)
        {
            _xamlRoot.Changed += XamlRoot_Changed;
        }
    }

    private void QueueDpiRefresh()
    {
        if (_disposed)
        {
            return;
        }

        if (_dpiRefreshQueued)
        {
            _dpiRefreshAgain = true;
            return;
        }

        if (!_root.IsLoaded || _xamlRoot is null || !IsDpiChangePending)
        {
            return;
        }

        _dpiRefreshQueued = true;
        if (!_root.DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, RefreshDpiLayout))
        {
            _dpiRefreshQueued = false;
            // A live window must not silently keep stale DPI geometry if its dispatcher rejects the refresh.
            throw new InvalidOperationException("Unable to queue the window DPI layout refresh.");
        }
    }

    private void RefreshDpiLayout()
    {
        try
        {
            if (_disposed || !_root.IsLoaded || _xamlRoot is null)
            {
                // Closure or unloading cancels queued presentation work.
                return;
            }

            var scale = _xamlRoot.RasterizationScale;
            if (Math.Abs(scale - WindowInteropService.GetRasterizationScale(_windowHandle)) >= 0.001d)
            {
                // Native bounds can arrive before XAML DPI; the next change event resumes the refresh.
                return;
            }

            _appliedDisplayScale = scale;
            UpdateMinimumSize(scale, DisplayArea.GetFromWindowId(_appWindow.Id, DisplayAreaFallback.Primary).WorkArea);
            _root.InvalidateMeasure();
            _root.InvalidateArrange();
            DpiChanged?.Invoke();
            // WinUI applies the native suggested bounds. Never multiply those physical bounds a second time.
            KeepCurrentBoundsInWorkArea(_root);
            _root.UpdateLayout();
        }
        finally
        {
            _dpiRefreshQueued = false;
            if (_dpiRefreshAgain)
            {
                // A content resize can move Search to another monitor while this refresh is still running.
                _dpiRefreshAgain = false;
                QueueDpiRefresh();
            }
        }
    }

    /// <summary>Applies the requested default size without choosing a screen position.</summary>
    internal void ApplyDefaultSize(FrameworkElement root)
    {
        ArgumentNullException.ThrowIfNull(root);
        var scale = ResolveScale(root);
        var area = OpeningWorkArea();
        var margin = (int)Math.Ceiling(_logicalScreenMargin * scale);
        var availableWidth = Math.Max(1, area.Width - (margin * 2));
        var availableHeight = Math.Max(1, area.Height - (margin * 2));
        var minimumSize = UpdateMinimumSize(scale, area);
        var width = Math.Min(availableWidth, Math.Max(minimumSize.Width, (int)Math.Ceiling(_logicalDefaultWidth * scale)));
        var height = Math.Min(availableHeight, Math.Max(minimumSize.Height, (int)Math.Ceiling(_logicalDefaultHeight * scale)));
        _appWindow.Resize(new SizeInt32(width, height));
    }

    internal void ApplyDefaultBounds(FrameworkElement root)
    {
        ArgumentNullException.ThrowIfNull(root);
        var scale = ResolveScale(root);
        var area = OpeningWorkArea();
        var margin = (int)Math.Ceiling(_logicalScreenMargin * scale);
        var availableWidth = Math.Max(1, area.Width - (margin * 2));
        var availableHeight = Math.Max(1, area.Height - (margin * 2));
        var minimumSize = UpdateMinimumSize(scale, area);
        var width = Math.Min(availableWidth, Math.Max(minimumSize.Width, (int)Math.Ceiling(_logicalDefaultWidth * scale)));
        var height = Math.Min(availableHeight, Math.Max(minimumSize.Height, (int)Math.Ceiling(_logicalDefaultHeight * scale)));
        _appWindow.Resize(new SizeInt32(width, height));
        CenterInWorkArea(area);
    }

    /// <summary>Restores saved bounds without replacing the user's position with an application-selected anchor.</summary>
    internal Task<bool> RestoreAsync(FrameworkElement root, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(root);
        return _restoreTask ??= RestoreCoreAsync(root, cancellationToken);
    }

    private async Task<bool> RestoreCoreAsync(FrameworkElement root, CancellationToken cancellationToken)
    {
        _restoreAttempted = true;
        var result = await _application.RestoreWindowStateAsync(
            _windowKey,
            _windowHandle.ToInt64(),
            cancellationToken);
        if (_disposed)
        {
            // A guarded owner can close while its restore request is completing; native geometry is no longer usable.
            return false;
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (!result.Succeeded)
        {
            throw new InvalidOperationException($"Window state could not be restored ({result.Code}).");
        }

        // Restored coordinates are authoritative; only clamp bounds that no longer fit the active display topology.
        KeepCurrentBoundsInWorkArea(root);
        _placementReady = true;
        if (!_closeSaveStarted && !s_preservingWorkspace) _saveTimer.Start();
        return result.Value is not null;
    }

    /// <summary>Retains saved placement, centering only the first opening without saved bounds.</summary>
    internal async Task RestoreOrCenterAsync(
        FrameworkElement root,
        CancellationToken cancellationToken,
        bool centerOnCursorDisplay = false)
    {
        ArgumentNullException.ThrowIfNull(root);
        if (_restoreAttempted)
        {
            return;
        }

        if (await RestoreAsync(root, cancellationToken))
        {
            return;
        }

        if (_disposed || _closeSaveStarted || s_preservingWorkspace) return;
        var area = centerOnCursorDisplay ? CursorWorkArea() : OpeningWorkArea();
        KeepCurrentBoundsInWorkArea(root, area);
        CenterInWorkArea(area);
    }

    /// <summary>Retains saved dialog size and centers each opening on its owner within the owner's work area.</summary>
    // root supplies the dialog's current layout scale.
    // cancellationToken cancels restoration before owner-relative placement.
    // Throws InvalidOperationException when the owner window is unavailable.
    internal async Task RestoreAndCenterOnOwnerAsync(FrameworkElement root, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(root);
        await RestoreAsync(root, cancellationToken);
        if (_disposed || _closeSaveStarted || s_preservingWorkspace) return;

        var owner = AppWindow.GetFromWindowId(_displayAnchorId)
            ?? throw new InvalidOperationException("The dialog owner is unavailable for centering.");
        var area = OpeningWorkArea();
        KeepCurrentBoundsInWorkArea(root, area);
        var margin = (int)Math.Ceiling(_logicalScreenMargin * ResolveScale(root));
        var left = area.X + margin;
        var top = area.Y + margin;
        var right = Math.Max(left, area.X + area.Width - margin - _appWindow.Size.Width);
        var bottom = Math.Max(top, area.Y + area.Height - margin - _appWindow.Size.Height);
        // An owner near a screen edge must not place the dialog's caption or content outside the work area.
        _appWindow.Move(new PointInt32(
            Math.Clamp(owner.Position.X + (owner.Size.Width - _appWindow.Size.Width) / 2, left, right),
            Math.Clamp(owner.Position.Y + (owner.Size.Height - _appWindow.Size.Height) / 2, top, bottom)));
    }

    /// <summary>Resizes content within the shared native minimum without replacing the user's placement.</summary>
    internal void ResizeForContent(
        FrameworkElement root,
        int preferredLogicalWidth,
        int preferredLogicalHeight)
    {
        ArgumentNullException.ThrowIfNull(root);
        if (preferredLogicalWidth <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(preferredLogicalWidth));
        }

        if (preferredLogicalHeight <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(preferredLogicalHeight));
        }

        if (_logicalMinimumSize.Width > preferredLogicalWidth || _logicalMinimumSize.Height > preferredLogicalHeight)
        {
            throw new ArgumentException("The minimum content size cannot exceed the preferred size.");
        }

        var scale = ResolveScale(root);
        var area = DisplayArea.GetFromWindowId(_appWindow.Id, DisplayAreaFallback.Primary).WorkArea;
        var margin = (int)Math.Ceiling(_logicalScreenMargin * scale);
        var availableWidth = Math.Max(1, area.Width - (margin * 2));
        var availableHeight = Math.Max(1, area.Height - (margin * 2));
        var minimumSize = UpdateMinimumSize(scale, area);
        var width = Math.Clamp((int)Math.Ceiling(preferredLogicalWidth * scale), minimumSize.Width, availableWidth);
        var height = Math.Clamp((int)Math.Ceiling(preferredLogicalHeight * scale), minimumSize.Height, availableHeight);
        // Resize first, then retain the user's monitor and position while clamping only an off-screen edge.
        _appWindow.Resize(new SizeInt32(width, height));
        KeepCurrentBoundsInWorkArea(root);
    }

    internal Task SaveAsync(CancellationToken cancellationToken)
    {
        if (_disposed || _closeSaveStarted || _closeSaveCompleted || _shutdownPrepared || s_preservingWorkspace)
        {
            return _pendingSave;
        }

        return QueueSaveAsync(cancellationToken);
    }

    private Task QueueSaveAsync(CancellationToken cancellationToken)
    {
        if (_pendingSave.IsFaulted || _pendingSave.IsCanceled)
        {
            // The original caller observes its failure; a later explicit save can retry instead of inheriting it forever.
            _pendingSave = Task.CompletedTask;
        }

        _pendingSave = PersistAsync(_pendingSave, cancellationToken);
        return _pendingSave;
    }

    /// <summary>Flushes a guarded dialog's placement and freezes queued writes before its own completion path closes it.</summary>
    internal Task SaveForExplicitCloseAsync(CancellationToken cancellationToken)
    {
        if (_disposed || _closeSaveCompleted || _shutdownPrepared) return _pendingSave;
        if (s_preservingWorkspace) return s_shutdownPreparation ?? _pendingSave;
        if (_closeSaveStarted) return _pendingClose;
        _closeSaveStarted = true;
        _saveTimer.Stop();
        return _pendingClose = PersistExplicitCloseAsync(cancellationToken);
    }

    private async Task PersistExplicitCloseAsync(CancellationToken cancellationToken)
    {
        try
        {
            await QueueSaveAsync(cancellationToken);
            if (WorkspaceWindowState.IsRestorable(_windowKey))
            {
                await SaveOpenStateAsync(false, cancellationToken);
            }

            _closeSaveCompleted = true;
        }
        catch
        {
            // Persistence must succeed before a guarded dialog releases its result or allows native closure.
            _closeSaveStarted = false;
            throw;
        }
    }

    private async Task PersistAsync(Task previousSave, CancellationToken cancellationToken)
    {
        await previousSave;
        if (_restoreTask is { } restoreTask)
        {
            try
            {
                await restoreTask;
            }
            catch (OperationCanceledException) when (restoreTask.IsCanceled)
            {
                // Closing during initial loading preserves the previous valid bounds instead of saving defaults.
            }
            catch (Exception) when (restoreTask.IsFaulted)
            {
                // The restore owner has reported this error. Preserve its last valid bounds and allow closing;
                // reopening starts a fresh restore instead of making every later save inherit the failed task.
            }
        }

        if (_disposed) return;
        if (_placementReady)
        {
            var result = await _application.SaveWindowStateAsync(
                _windowKey,
                _windowHandle.ToInt64(),
                cancellationToken);
            if (!result.Succeeded)
            {
                throw new InvalidOperationException($"Window state could not be saved ({result.Code}).");
            }
        }

        if (WorkspaceWindowState.IsRestorable(_windowKey))
        {
            await SaveOpenStateAsync(WorkspaceWindowState.IsOpenWhileAlive(_windowKey, _appWindow.IsVisible), cancellationToken);
        }
    }

    private async Task SaveOpenStateAsync(bool isOpen, CancellationToken cancellationToken)
    {
        var result = await _application.SetWindowOpenStateAsync(_windowKey, isOpen, cancellationToken);
        if (!result.Succeeded)
        {
            throw new InvalidOperationException($"Window visibility could not be saved ({result.Code}).");
        }
    }

    /// <summary>Saves placement during close without allowing a persistence failure to escape an event callback.</summary>
    internal async Task<bool> TrySaveForCloseAsync(CancellationToken cancellationToken)
    {
        try
        {
            await SaveAsync(cancellationToken);
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return false;
        }
        catch (Exception exception)
        {
            // A window that is already closing cannot present a reliable error surface. Trace the failure and
            // let its deterministic cleanup continue; the next open uses the last valid persisted placement.
            Trace.TraceError(
                "Window placement save failed during close. WindowKey={0} ExceptionType={1} Message={2}",
                _windowKey,
                exception.GetType().Name,
                exception.Message);
            return false;
        }
    }

    internal void KeepCurrentBoundsInWorkArea(FrameworkElement root)
    {
        ArgumentNullException.ThrowIfNull(root);
        var area = DisplayArea.GetFromWindowId(_appWindow.Id, DisplayAreaFallback.Primary).WorkArea;
        KeepCurrentBoundsInWorkArea(root, area);
    }

    private void KeepCurrentBoundsInWorkArea(FrameworkElement root, RectInt32 area)
    {
        var scale = ResolveScale(root);
        if (_snapping is { PreserveUserPosition: true })
        {
            // Native dragging and intentional off-screen/edge placement take precedence over layout clamping.
            UpdateMinimumSize(scale, area);
            return;
        }

        if (_appWindow.Presenter is OverlappedPresenter { State: not OverlappedPresenterState.Restored })
        {
            // Windows owns maximized and minimized geometry; only refresh the restore-size constraints.
            UpdateMinimumSize(scale, area);
            return;
        }

        var margin = (int)Math.Ceiling(_logicalScreenMargin * scale);
        var availableWidth = Math.Max(1, area.Width - (margin * 2));
        var availableHeight = Math.Max(1, area.Height - (margin * 2));
        var minimumSize = UpdateMinimumSize(scale, area);
        var width = Math.Clamp(Math.Max(1, _appWindow.Size.Width), minimumSize.Width, availableWidth);
        var height = Math.Clamp(Math.Max(1, _appWindow.Size.Height), minimumSize.Height, availableHeight);
        if (width != _appWindow.Size.Width || height != _appWindow.Size.Height)
        {
            _appWindow.Resize(new SizeInt32(width, height));
        }

        var left = area.X + margin;
        var top = area.Y + margin;
        var right = Math.Max(left, area.X + area.Width - margin - width);
        var bottom = Math.Max(top, area.Y + area.Height - margin - height);
        var x = Math.Clamp(_appWindow.Position.X, left, right);
        var y = Math.Clamp(_appWindow.Position.Y, top, bottom);
        if (x != _appWindow.Position.X || y != _appWindow.Position.Y)
        {
            _appWindow.Move(new PointInt32(x, y));
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _root.Loaded -= Root_Loaded;
        _window.Closed -= Window_Closed;
        _appWindow.Changed -= AppWindow_Changed;
        _appWindow.Closing -= AppWindow_Closing;
        _saveTimer.Stop();
        _saveTimer.Tick -= SaveTimer_Tick;
        ActivePlacements.Remove(this);
        if (_xamlRoot is not null)
        {
            _xamlRoot.Changed -= XamlRoot_Changed;
            _xamlRoot = null;
        }

        DpiChanged = null;
        if (_subclassInstalled)
        {
            _ = RemoveWindowSubclass(_windowHandle, _subclassProc, _subclassId);
            _subclassInstalled = false;
        }

        // The existing timers/events are already detached if native snap removal reports a failure.
        // Core retains a failed registration until WM_NCDESTROY makes its final owning-thread cleanup attempt.
        _snapping?.Dispose();
    }

    private double ResolveScale(FrameworkElement root)
    {
        AttachXamlRoot();
        var scale = Math.Max(0.1d, root.XamlRoot?.RasterizationScale ?? _rasterizationScale);
        _rasterizationScale = scale;
        return scale;
    }

    private SizeInt32 UpdateMinimumSize(double scale, RectInt32 workArea)
    {
        var margin = (int)Math.Ceiling(_logicalScreenMargin * scale);
        var availableWidth = Math.Max(1, workArea.Width - (margin * 2));
        var availableHeight = Math.Max(1, workArea.Height - (margin * 2));
        var width = Math.Min(availableWidth, Math.Max(1, (int)Math.Ceiling(_logicalMinimumSize.Width * scale)));
        var height = Math.Min(availableHeight, Math.Max(1, (int)Math.Ceiling(_logicalMinimumSize.Height * scale)));
        _minimumPhysicalSize = new SizeInt32(width, height);
        return _minimumPhysicalSize;
    }

    private void CenterInWorkArea(RectInt32 workArea)
    {
        _appWindow.Move(new PointInt32(
            workArea.X + Math.Max(0, (workArea.Width - _appWindow.Size.Width) / 2),
            workArea.Y + Math.Max(0, (workArea.Height - _appWindow.Size.Height) / 2)));
    }

    private RectInt32 OpeningWorkArea() =>
        DisplayArea.GetFromWindowId(_displayAnchorId, DisplayAreaFallback.Primary).WorkArea;

    private static RectInt32 CursorWorkArea()
    {
        if (!GetCursorPos(out var cursorPosition))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "WorkTrail could not locate the pointer display.");
        }

        // The cursor point is resolved before the window is activated, so multi-monitor launches follow the user's current display.
        return DisplayArea.GetFromPoint(
            new PointInt32(cursorPosition.X, cursorPosition.Y),
            DisplayAreaFallback.Primary).WorkArea;
    }

    private void InstallMinimumSizeSubclass()
    {
        if (_subclassInstalled)
        {
            return;
        }

        if (!SetWindowSubclass(_windowHandle, _subclassProc, _subclassId, 0))
        {
            throw new InvalidOperationException($"Unable to install minimum window size constraint (Win32 error {Marshal.GetLastWin32Error()}).");
        }

        _subclassInstalled = true;
    }

    private IntPtr WindowSubclassProc(IntPtr windowHandle, uint message, IntPtr wParam, IntPtr lParam, nuint subclassId, nuint referenceData)
    {
        if (message == WmGetMinMaxInfo)
        {
            // Native resizing precedes XAML DPI events. In particular, stale larger minima must not block a DPI decrease.
            UpdateMinimumSize(
                WindowInteropService.GetRasterizationScale(windowHandle),
                DisplayArea.GetFromWindowId(_appWindow.Id, DisplayAreaFallback.Primary).WorkArea);
            var minMaxInfo = Marshal.PtrToStructure<MinMaxInfo>(lParam);
            minMaxInfo.MinTrackSize.X = Math.Max(minMaxInfo.MinTrackSize.X, _minimumPhysicalSize.Width);
            minMaxInfo.MinTrackSize.Y = Math.Max(minMaxInfo.MinTrackSize.Y, _minimumPhysicalSize.Height);
            Marshal.StructureToPtr(minMaxInfo, lParam, false);
            return IntPtr.Zero;
        }

        return DefSubclassProc(windowHandle, message, wParam, lParam);
    }

    private delegate IntPtr NativeWindowSubclassProc(IntPtr windowHandle, uint message, IntPtr wParam, IntPtr lParam, nuint subclassId, nuint referenceData);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MinMaxInfo
    {
        public NativePoint Reserved;
        public NativePoint MaxSize;
        public NativePoint MaxPosition;
        public NativePoint MinTrackSize;
        public NativePoint MaxTrackSize;
    }

    [DllImport("Comctl32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowSubclass(IntPtr hWnd, NativeWindowSubclassProc pfnSubclass, nuint uIdSubclass, nuint dwRefData);

    [DllImport("Comctl32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RemoveWindowSubclass(IntPtr hWnd, NativeWindowSubclassProc pfnSubclass, nuint uIdSubclass);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out NativePoint point);

    [DllImport("Comctl32.dll", SetLastError = true)]
    private static extern IntPtr DefSubclassProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam);
}
