// SPDX-License-Identifier: MIT

using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using WorkTrail.Application;

namespace WorkTrail.Services;

/// <summary>Owns local, UI-thread window snapping without hooks in another process or persistence during a drag.</summary>
internal sealed class WindowSnappingService
{
    private readonly Dictionary<IntPtr, Registration> _windows = [];
    private bool _enabled = true;
    private uint _uiThreadId;

    internal void Configure(bool enabled) => Volatile.Write(ref _enabled, enabled);

    internal IWindowSnappingRegistration Register(long windowHandle, Action<Exception> reportFailure)
    {
        ArgumentNullException.ThrowIfNull(reportFailure);
        var handle = new IntPtr(windowHandle);
        var thread = GetWindowThreadProcessId(handle, out var process);
        var currentThread = GetCurrentThreadId();
        if (thread == 0 || thread != currentThread || process != Environment.ProcessId)
        {
            // Native subclass callbacks must be registered and removed on their owning UI thread.
            throw new ArgumentException("Window snapping requires a window on the current process and thread.", nameof(windowHandle));
        }

        if (_uiThreadId != 0 && _uiThreadId != currentThread)
        {
            throw new InvalidOperationException("Window snapping registrations must share one UI thread.");
        }

        if (_windows.ContainsKey(handle)) throw new InvalidOperationException("The window is already registered for snapping.");
        var registration = new Registration(this, handle, reportFailure);
        _windows.Add(handle, registration);
        _uiThreadId = currentThread;
        return registration;
    }

    private WindowSnapRectangle[] ReadPeers(IntPtr movingWindow)
    {
        var peers = new List<WindowSnapRectangle>(_windows.Count);
        foreach (var handle in _windows.Keys)
        {
            if (handle == movingWindow || !IsWindowVisible(handle) || IsIconic(handle) || IsZoomed(handle)) continue;
            var result = DwmGetWindowAttribute(handle, 14, out int cloaked, sizeof(int));
            Marshal.ThrowExceptionForHR(result);
            if (cloaked != 0) continue;
            peers.Add(ReadFrame(handle));
        }

        return peers.ToArray();
    }

    private static WindowSnapRectangle ReadFrame(IntPtr handle)
    {
        // DWM bounds exclude invisible resize borders: DPI-scaled thresholds refer to visible edges.
        Marshal.ThrowExceptionForHR(DwmGetWindowAttribute(handle, 9, out NativeRectangle frame, Marshal.SizeOf<NativeRectangle>()));
        return frame.ToRectangle();
    }

    private static NativeRectangle ReadOuterBounds(IntPtr handle)
    {
        if (!GetWindowRect(handle, out var bounds)) throw new Win32Exception(Marshal.GetLastPInvokeError(), "Unable to read window bounds for snapping.");
        return bounds;
    }

    private static NativePoint ReadCursor()
    {
        if (!GetCursorPos(out var point)) throw new Win32Exception(Marshal.GetLastPInvokeError(), "Unable to read the window drag position.");
        return point;
    }

    private static WindowSnapSession CreateSession(IntPtr handle)
    {
        var monitor = MonitorFromWindow(handle, 2);
        var info = new MonitorInformation { Size = Marshal.SizeOf<MonitorInformation>() };
        if (monitor == IntPtr.Zero || !GetMonitorInfo(monitor, ref info))
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "Unable to read the snapping monitor.");
        }

        return new WindowSnapSession(info.Bounds.ToRectangle(), info.WorkArea.ToRectangle(), ReadDpi(handle));
    }

    private static uint ReadDpi(IntPtr handle)
    {
        var dpi = GetDpiForWindow(handle);
        if (dpi == 0) throw new Win32Exception("Unable to read the snapping window DPI.");
        return dpi;
    }

    private static NativeMinMaxInformation ReadResizeLimits(IntPtr handle, uint dpi)
    {
        var limits = new NativeMinMaxInformation
        {
            MinTrackSize = new NativePoint { X = ReadTrackMetric(34, dpi), Y = ReadTrackMetric(35, dpi) },
            MaxTrackSize = new NativePoint { X = ReadTrackMetric(59, dpi), Y = ReadTrackMetric(60, dpi) }
        };
        // Query the owning window's existing native constraints, including its shared WinUI minimum-size subclass.
        _ = SendMessage(handle, 0x0024, IntPtr.Zero, ref limits);
        limits.MinTrackSize.X = Math.Max(1, limits.MinTrackSize.X);
        limits.MinTrackSize.Y = Math.Max(1, limits.MinTrackSize.Y);
        limits.MaxTrackSize.X = Math.Max(limits.MinTrackSize.X, limits.MaxTrackSize.X);
        limits.MaxTrackSize.Y = Math.Max(limits.MinTrackSize.Y, limits.MaxTrackSize.Y);
        return limits;
    }

    private static int ReadTrackMetric(int index, uint dpi)
    {
        var metric = GetSystemMetricsForDpi(index, dpi);
        if (metric <= 0) throw new Win32Exception(Marshal.GetLastPInvokeError(), "Unable to read native resize constraints.");
        return metric;
    }

    private sealed class Registration : IWindowSnappingRegistration
    {
        private const uint EnterSizeMove = 0x0231;
        private const uint Moving = 0x0216;
        private const uint Sizing = 0x0214;
        private const uint ExitSizeMove = 0x0232;
        private const uint NonClientDestroy = 0x0082;
        private readonly WindowSnappingService _owner;
        private readonly IntPtr _handle;
        private readonly Action<Exception> _reportFailure;
        private readonly SubclassProcedure _callback;
        private readonly uint _threadId;
        private WindowSnapSession? _session;
        private WindowSnapGuideOverlay? _guides;
        private NativeRectangle _anchorBounds;
        private NativePoint _anchorCursor;
        private NativeRectangle _resizeAnchorBounds;
        private NativePoint _resizeAnchorCursor;
        private NativeMinMaxInformation _resizeLimits;
        private WindowSnapEdges _resizeEdges;
        private uint _resizeDpi;
        private bool _hasResizeAnchor;
        private bool _inMoveLoop;
        private bool _faulted;
        private bool _disposed;
        private bool _preservePosition;
        private bool _hasMoveAnchor;
        private Exception? _pendingFailure;

        internal Registration(WindowSnappingService owner, IntPtr handle, Action<Exception> reportFailure)
        {
            _owner = owner;
            _handle = handle;
            _reportFailure = reportFailure;
            _callback = WindowProcedure;
            _threadId = GetCurrentThreadId();
            // The delegate remains strongly held until the subclass is removed on this same thread.
            if (!SetWindowSubclass(handle, _callback, 1, 0))
            {
                throw new Win32Exception(Marshal.GetLastPInvokeError(), "Unable to register window snapping.");
            }
        }

        /// <inheritdoc />
        public bool PreserveUserPosition => _inMoveLoop || _preservePosition;

        private IntPtr WindowProcedure(IntPtr handle, uint message, IntPtr wParam, IntPtr lParam, nuint id, nuint data)
        {
            try
            {
                if (message == NonClientDestroy)
                {
                    try
                    {
                        Dispose();
                    }
                    finally
                    {
                        // The HWND is being destroyed; Windows discards any remaining subclass with it.
                        // Do not retain a dead peer/delegate if explicit native removal reported a failure.
                        _owner._windows.Remove(handle);
                        _disposed = true;
                        _inMoveLoop = false;
                        _session = null;
                    }
                }
                else if (message == EnterSizeMove)
                {
                    _guides?.Hide();
                    _faulted = false;
                    _inMoveLoop = true;
                    _hasMoveAnchor = false;
                    _hasResizeAnchor = false;
                    _pendingFailure = null;
                    _session = Volatile.Read(ref _owner._enabled) ? CreateSession(handle) : null;
                }
                else if (message == ExitSizeMove)
                {
                    _guides?.Hide();
                    _inMoveLoop = false;
                    _session = null;
                    try
                    {
                        if (_faulted)
                        {
                            // Discard a failed painter so a later drag can retry fresh native resources.
                            _guides?.Dispose();
                            _guides = null;
                        }
                    }
                    finally
                    {
                        if (_pendingFailure is { } failure)
                        {
                            _pendingFailure = null;
                            ReportFailure(failure);
                        }
                    }
                }
                else if (message == Moving && _inMoveLoop && !_faulted)
                {
                    if (ApplyMove(lParam)) return new IntPtr(1);
                }
                else if (message == Sizing && _inMoveLoop && !_faulted)
                {
                    if (ApplyResize(wParam, lParam)) return new IntPtr(1);
                }
                else if (message == 0x001F || (message == 0x0006 && (wParam.ToInt64() & 0xFFFF) == 0))
                {
                    // Cancellation or activation of another surface must not leave desktop guides behind.
                    _guides?.Hide();
                }
            }
            catch (Exception exception)
            {
                // Never unwind through Win32: stop snapping for this operation and surface the failure to the host.
                // Native free movement remains available; the next drag retries fresh monitor/frame reads.
                _faulted = true;
                _guides?.Hide();
                _preservePosition = true;
                Trace.TraceError("Native window snapping failed: {0}", exception);
                if (_inMoveLoop) _pendingFailure = exception;
                else ReportFailure(exception);
            }

            return DefSubclassProc(handle, message, wParam, lParam);
        }

        private bool ApplyMove(IntPtr rectanglePointer)
        {
            if (!Volatile.Read(ref _owner._enabled))
            {
                _guides?.Hide();
                // Off means native movement is untouched: no DWM reads or RECT rewrites.
                // User-controlled free placement must also survive a queued DPI layout adjustment.
                _preservePosition = true;
                _hasMoveAnchor = false;
                return false;
            }

            var proposed = Marshal.PtrToStructure<NativeRectangle>(rectanglePointer);
            var cursor = ReadCursor();
            if (!_hasMoveAnchor || proposed.Right - proposed.Left != _anchorBounds.Right - _anchorBounds.Left
                || proposed.Bottom - proposed.Top != _anchorBounds.Bottom - _anchorBounds.Top)
            {
                // Windows may restore a maximized window or apply a new DPI size during the drag.
                // Retain the escape latch, but rebase the pointer anchor to the new native dimensions.
                _anchorBounds = proposed;
                _anchorCursor = cursor;
                _hasMoveAnchor = true;
            }

            var deltaX = checked(cursor.X - _anchorCursor.X);
            var deltaY = checked(cursor.Y - _anchorCursor.Y);
            var raw = new NativeRectangle
            {
                Left = checked(_anchorBounds.Left + deltaX),
                Top = checked(_anchorBounds.Top + deltaY),
                Right = checked(_anchorBounds.Right + deltaX),
                Bottom = checked(_anchorBounds.Bottom + deltaY)
            };
            if (_session is { IsSuppressed: true })
            {
                _guides?.Hide();
                // Once the user exceeds the monitor's snap margin, peers and DWM frames no longer participate in this drag.
                _preservePosition = true;
                Marshal.StructureToPtr(raw, rectanglePointer, false);
                return true;
            }

            var outer = ReadOuterBounds(_handle);
            var visible = ReadFrame(_handle);
            var leftInset = visible.Left - outer.Left;
            var topInset = visible.Top - outer.Top;
            var rightInset = outer.Right - visible.Right;
            var bottomInset = outer.Bottom - visible.Bottom;
            var rawVisible = new WindowSnapRectangle(raw.Left + leftInset, raw.Top + topInset,
                raw.Right - rightInset, raw.Bottom - bottomInset);
            _session ??= CreateSession(_handle);
            _session.UpdateDpi(ReadDpi(_handle));
            var snapped = _session.Move(rawVisible, _owner.ReadPeers(_handle));
            // Preserve explicit off-screen/edge placement through queued DPI layout, not unrelated later interior moves.
            _preservePosition = _session.IsSuppressed || _session.IsSnapped;
            var snapX = snapped.Left - rawVisible.Left;
            var snapY = snapped.Top - rawVisible.Top;
            raw.Left += snapX;
            raw.Right += snapX;
            raw.Top += snapY;
            raw.Bottom += snapY;

            // Change the native proposal before Windows paints it; no SetWindowPos feedback loop or cumulative drift.
            Marshal.StructureToPtr(raw, rectanglePointer, false);
            UpdateGuides();
            return true;
        }

        private bool ApplyResize(IntPtr sizingEdge, IntPtr rectanglePointer)
        {
            if (!Volatile.Read(ref _owner._enabled))
            {
                _guides?.Hide();
                _preservePosition = true;
                _hasResizeAnchor = false;
                return false;
            }

            var edges = sizingEdge.ToInt64() switch
            {
                1 => WindowSnapEdges.Left,
                2 => WindowSnapEdges.Right,
                3 => WindowSnapEdges.Top,
                4 => WindowSnapEdges.Top | WindowSnapEdges.Left,
                5 => WindowSnapEdges.Top | WindowSnapEdges.Right,
                6 => WindowSnapEdges.Bottom,
                7 => WindowSnapEdges.Bottom | WindowSnapEdges.Left,
                8 => WindowSnapEdges.Bottom | WindowSnapEdges.Right,
                _ => throw new ArgumentOutOfRangeException(nameof(sizingEdge))
            };
            _session ??= CreateSession(_handle);
            var dpi = ReadDpi(_handle);
            _session.UpdateDpi(dpi);
            if (_session.IsSuppressed)
            {
                _guides?.Hide();
                _preservePosition = true;
                return false;
            }

            var proposed = Marshal.PtrToStructure<NativeRectangle>(rectanglePointer);
            var cursor = ReadCursor();
            if (!_hasResizeAnchor || _resizeDpi != dpi || _resizeEdges != edges)
            {
                _resizeAnchorBounds = proposed;
                _resizeAnchorCursor = cursor;
                _resizeDpi = dpi;
                _resizeEdges = edges;
                _resizeLimits = ReadResizeLimits(_handle, dpi);
                _hasResizeAnchor = true;
            }

            // Use raw pointer displacement rather than the previous snapped dimensions so an edge releases naturally.
            var raw = _resizeAnchorBounds;
            var deltaX = checked(cursor.X - _resizeAnchorCursor.X);
            var deltaY = checked(cursor.Y - _resizeAnchorCursor.Y);
            if ((edges & WindowSnapEdges.Left) != 0) raw.Left = checked(raw.Left + deltaX);
            if ((edges & WindowSnapEdges.Right) != 0) raw.Right = checked(raw.Right + deltaX);
            if ((edges & WindowSnapEdges.Top) != 0) raw.Top = checked(raw.Top + deltaY);
            if ((edges & WindowSnapEdges.Bottom) != 0) raw.Bottom = checked(raw.Bottom + deltaY);
            ConstrainResize(ref raw, edges, _resizeLimits);

            var outer = ReadOuterBounds(_handle);
            var visible = ReadFrame(_handle);
            var leftInset = visible.Left - outer.Left;
            var topInset = visible.Top - outer.Top;
            var rightInset = outer.Right - visible.Right;
            var bottomInset = outer.Bottom - visible.Bottom;
            var widthInset = checked(leftInset + rightInset);
            var heightInset = checked(topInset + bottomInset);
            var rawVisible = new WindowSnapRectangle(checked(raw.Left + leftInset), checked(raw.Top + topInset),
                checked(raw.Right - rightInset), checked(raw.Bottom - bottomInset));
            var minimumWidth = Math.Max(1, checked(_resizeLimits.MinTrackSize.X - widthInset));
            var minimumHeight = Math.Max(1, checked(_resizeLimits.MinTrackSize.Y - heightInset));
            var snapped = _session.Resize(rawVisible, _owner.ReadPeers(_handle), edges, minimumWidth, minimumHeight,
                Math.Max(minimumWidth, checked(_resizeLimits.MaxTrackSize.X - widthInset)),
                Math.Max(minimumHeight, checked(_resizeLimits.MaxTrackSize.Y - heightInset)));
            _preservePosition = _session.IsSuppressed || _session.IsSnapped;
            raw.Left = checked((int)((long)raw.Left + snapped.Left - rawVisible.Left));
            raw.Top = checked((int)((long)raw.Top + snapped.Top - rawVisible.Top));
            raw.Right = checked((int)((long)raw.Right + snapped.Right - rawVisible.Right));
            raw.Bottom = checked((int)((long)raw.Bottom + snapped.Bottom - rawVisible.Bottom));
            Marshal.StructureToPtr(raw, rectanglePointer, false);
            UpdateGuides();
            return true;
        }

        private static void ConstrainResize(ref NativeRectangle raw, WindowSnapEdges edges, NativeMinMaxInformation limits)
        {
            if ((edges & WindowSnapEdges.Left) != 0)
                raw.Left = checked((int)(raw.Right - Math.Clamp((long)raw.Right - raw.Left, limits.MinTrackSize.X, limits.MaxTrackSize.X)));
            else if ((edges & WindowSnapEdges.Right) != 0)
                raw.Right = checked((int)(raw.Left + Math.Clamp((long)raw.Right - raw.Left, limits.MinTrackSize.X, limits.MaxTrackSize.X)));
            if ((edges & WindowSnapEdges.Top) != 0)
                raw.Top = checked((int)(raw.Bottom - Math.Clamp((long)raw.Bottom - raw.Top, limits.MinTrackSize.Y, limits.MaxTrackSize.Y)));
            else if ((edges & WindowSnapEdges.Bottom) != 0)
                raw.Bottom = checked((int)(raw.Top + Math.Clamp((long)raw.Bottom - raw.Top, limits.MinTrackSize.Y, limits.MaxTrackSize.Y)));
        }

        private void UpdateGuides()
        {
            if (_session is { } session && (session.VerticalGuide.HasValue || session.HorizontalGuide.HasValue))
            {
                (_guides ??= new WindowSnapGuideOverlay(_handle)).Update(session);
            }
            else
            {
                _guides?.Hide();
            }
        }

        private void ReportFailure(Exception exception)
        {
            try
            {
                _reportFailure(exception);
            }
            catch (Exception notificationFailure)
            {
                // A shutting-down or failed notification sink must not throw across the unmanaged callback boundary.
                Trace.TraceError($"Window snapping failed: {exception}. Failure notification also failed: {notificationFailure}.");
            }
        }

        /// <inheritdoc />
        public void Dispose()
        {
            if (_disposed) return;
            if (GetCurrentThreadId() != _threadId) throw new InvalidOperationException("Window snapping must be removed on its owning UI thread.");
            _guides?.Dispose();
            if (!RemoveWindowSubclass(_handle, _callback, 1))
            {
                throw new Win32Exception(Marshal.GetLastPInvokeError(), "Unable to remove window snapping.");
            }

            _disposed = true;
            _owner._windows.Remove(_handle);
            _inMoveLoop = false;
            _session = null;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRectangle
    {
        internal int Left;
        internal int Top;
        internal int Right;
        internal int Bottom;

        internal readonly WindowSnapRectangle ToRectangle() => new(Left, Top, Right, Bottom);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        internal int X;
        internal int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInformation
    {
        internal int Size;
        internal NativeRectangle Bounds;
        internal NativeRectangle WorkArea;
        internal uint Flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeMinMaxInformation
    {
        internal NativePoint Reserved;
        internal NativePoint MaxSize;
        internal NativePoint MaxPosition;
        internal NativePoint MinTrackSize;
        internal NativePoint MaxTrackSize;
    }

    private delegate IntPtr SubclassProcedure(IntPtr window, uint message, IntPtr wParam, IntPtr lParam, nuint id, nuint data);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr window);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetSystemMetricsForDpi(int index, uint dpi);

    [DllImport("user32.dll", EntryPoint = "SendMessageW")]
    private static extern IntPtr SendMessage(IntPtr window, uint message, IntPtr wParam, ref NativeMinMaxInformation limits);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr window, out NativeRectangle rectangle);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out NativePoint point);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr window, uint flags);

    [DllImport("user32.dll", EntryPoint = "GetMonitorInfoW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInformation info);

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(IntPtr window, int attribute, out NativeRectangle value, int size);

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(IntPtr window, int attribute, out int value, int size);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(IntPtr window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsZoomed(IntPtr window);

    [DllImport("comctl32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowSubclass(IntPtr window, SubclassProcedure callback, nuint id, nuint data);

    [DllImport("comctl32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RemoveWindowSubclass(IntPtr window, SubclassProcedure callback, nuint id);

    [DllImport("comctl32.dll")]
    private static extern IntPtr DefSubclassProc(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
}
