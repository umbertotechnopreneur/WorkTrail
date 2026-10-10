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


using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using WorkTrail.Application;

namespace WorkTrail.Services;

/// <summary>Draws transient desktop guides without activation, input interception or full-monitor buffers.</summary>
internal sealed class WindowSnapGuideOverlay(IntPtr owner) : IDisposable
{
    private GuideLine? _vertical;
    private GuideLine? _horizontal;

    internal void Update(WindowSnapSession session)
    {
        if (session.VerticalGuide is { } vertical)
        {
            (_vertical ??= new GuideLine(owner, true)).Update(vertical, session.MonitorBounds);
        }
        else
        {
            _vertical?.Hide();
        }

        if (session.HorizontalGuide is { } horizontal)
        {
            (_horizontal ??= new GuideLine(owner, false)).Update(horizontal, session.MonitorBounds);
        }
        else
        {
            _horizontal?.Hide();
        }
    }

    internal void Hide()
    {
        _vertical?.Hide();
        _horizontal?.Hide();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        try
        {
            _vertical?.Dispose();
        }
        finally
        {
            _horizontal?.Dispose();
        }
    }

    private sealed class GuideLine : IDisposable
    {
        private const uint ExtendedStyles = 0x00080000 | 0x00000020 | 0x08000000 | 0x00000080;
        private const uint PopupDisabled = 0x80000000 | 0x08000000;
        private const uint PaintMessage = 0x000F;
        private const uint GuideColor = 0x00E0A8BC;
        private readonly IntPtr _owner;
        private readonly bool _vertical;
        private readonly SubclassProcedure _callback;
        private IntPtr _handle;
        private int _width;
        private int _height;
        private int _dashLength;
        private bool _visible;
        private (WindowSnapGuide Guide, WindowSnapRectangle Monitor, uint Dpi)? _last;
        private Exception? _paintFailure;

        internal GuideLine(IntPtr owner, bool vertical)
        {
            _owner = owner;
            _vertical = vertical;
            _callback = WindowProcedure;
            // A layered, transparent, disabled tool window passes input through and never enters switchers.
            // Ownership bounds its lifetime to the dragged surface, even during failed opening or shutdown.
            _handle = CreateWindowEx(ExtendedStyles, "STATIC", string.Empty, PopupDisabled,
                0, 0, 1, 1, owner, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
            if (_handle == IntPtr.Zero) throw NativeFailure("Unable to create a window snap guide.");
            try
            {
                if (!SetWindowSubclass(_handle, _callback, 1, 0))
                    throw NativeFailure("Unable to register window snap guide painting.");
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        internal void Update(WindowSnapGuide guide, WindowSnapRectangle monitor)
        {
            if (_paintFailure is { } previousFailure) throw previousFailure;
            var dpi = GetDpiForWindow(_owner);
            if (dpi == 0) throw NativeFailure("Unable to read window snap guide scaling.");
            var state = (guide, monitor, dpi);
            if (_visible && _last == state) return;

            var thickness = Math.Max(1, (int)Math.Round(dpi / 96d));
            _dashLength = Math.Max(4, (int)Math.Round(6 * dpi / 96d));
            _width = _vertical ? Math.Min(thickness, monitor.Width) : monitor.Width;
            _height = _vertical ? monitor.Height : Math.Min(thickness, monitor.Height);
            // Right/bottom edges are exclusive; put their stroke on the final visible monitor pixels.
            var x = _vertical ? Math.Clamp(guide.Coordinate, monitor.Left, monitor.Right - _width) : monitor.Left;
            var y = _vertical ? monitor.Top : Math.Clamp(guide.Coordinate, monitor.Top, monitor.Bottom - _height);
            if (!SetLayeredWindowAttributes(_handle, 0, guide.IsSnapped ? (byte)240 : (byte)140, 3))
                throw NativeFailure("Unable to set window snap guide transparency.");
            if (!SetWindowPos(_handle, new IntPtr(-1), x, y, _width, _height, 0x0010 | 0x0200))
                throw NativeFailure("Unable to position a window snap guide.");
            if (!InvalidateRect(_handle, IntPtr.Zero, false))
                throw NativeFailure("Unable to refresh a window snap guide.");
            // Paint synchronously inside the native move loop: queued XAML work need not run during a drag.
            if (!UpdateWindow(_handle)) throw NativeFailure("Unable to paint a window snap guide.");
            if (_paintFailure is { } failure) throw failure;
            _ = ShowWindow(_handle, 8);
            _visible = true;
            _last = state;
        }

        internal void Hide()
        {
            if (_handle != IntPtr.Zero) _ = ShowWindow(_handle, 0);
            _visible = false;
        }

        private IntPtr WindowProcedure(IntPtr window, uint message, IntPtr wParam, IntPtr lParam, nuint id, nuint data)
        {
            // Painting clears the transparency key itself; suppress the STATIC class's opaque erase.
            if (message == 0x0014) return new IntPtr(1);
            if (message != PaintMessage) return DefSubclassProc(window, message, wParam, lParam);
            try
            {
                Paint(window);
            }
            catch (Exception exception)
            {
                // Never throw through Win32; the drag registration reports this after leaving its move loop.
                _paintFailure = exception;
                Trace.TraceError("Window snap guide painting failed: {0}", exception);
                Hide();
            }

            return IntPtr.Zero;
        }

        private void Paint(IntPtr window)
        {
            var dc = BeginPaint(window, out var paint);
            if (dc == IntPtr.Zero) throw NativeFailure("Unable to begin window snap guide painting.");
            try
            {
                var bounds = new NativeRectangle { Right = _width, Bottom = _height };
                // Black is the transparency key; only the small dashed stroke is composited.
                if (FillRect(dc, ref bounds, GetStockObject(4)) == 0)
                    throw NativeFailure("Unable to clear a window snap guide.");
                var brush = CreateSolidBrush(GuideColor);
                if (brush == IntPtr.Zero) throw NativeFailure("Unable to create a window snap guide brush.");
                try
                {
                    var extent = _vertical ? _height : _width;
                    for (long start = 0; start < extent; start += _dashLength * 2)
                    {
                        var end = (int)Math.Min(start + _dashLength, extent);
                        var dash = _vertical
                            ? new NativeRectangle { Top = (int)start, Right = _width, Bottom = end }
                            : new NativeRectangle { Left = (int)start, Right = end, Bottom = _height };
                        if (FillRect(dc, ref dash, brush) == 0)
                            throw NativeFailure("Unable to draw a window snap guide.");
                    }
                }
                finally
                {
                    _ = DeleteObject(brush);
                }
            }
            finally
            {
                _ = EndPaint(window, ref paint);
            }
        }

        /// <inheritdoc />
        public void Dispose()
        {
            if (_handle == IntPtr.Zero) return;
            Hide();
            // DestroyWindow removes the subclass while this object still holds its callback delegate.
            // The owner may already have destroyed its owned guide during native teardown.
            if (IsWindow(_handle) && !DestroyWindow(_handle))
                throw NativeFailure("Unable to destroy a window snap guide.");
            _handle = IntPtr.Zero;
            GC.KeepAlive(_callback);
        }
    }

    private static Win32Exception NativeFailure(string message) => new(Marshal.GetLastPInvokeError(), message);

    private delegate IntPtr SubclassProcedure(IntPtr window, uint message, IntPtr wParam, IntPtr lParam, nuint id, nuint data);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRectangle
    {
        internal int Left;
        internal int Top;
        internal int Right;
        internal int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PaintInformation
    {
        internal IntPtr DeviceContext;
        internal int Erase;
        internal NativeRectangle Bounds;
        internal int Restore;
        internal int IncrementalUpdate;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
        internal byte[] Reserved;
    }

    [DllImport("user32.dll", EntryPoint = "CreateWindowExW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateWindowEx(uint extendedStyle, string className, string windowName, uint style,
        int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr parameter);

    [DllImport("comctl32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowSubclass(IntPtr window, SubclassProcedure callback, nuint id, nuint data);

    [DllImport("comctl32.dll")]
    private static extern IntPtr DefSubclassProc(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetLayeredWindowAttributes(IntPtr window, uint colorKey, byte alpha, uint flags);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr window, IntPtr insertAfter, int x, int y, int width, int height, uint flags);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool InvalidateRect(IntPtr window, IntPtr rectangle, [MarshalAs(UnmanagedType.Bool)] bool erase);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UpdateWindow(IntPtr window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(IntPtr window, int command);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr window);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr BeginPaint(IntPtr window, out PaintInformation paint);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EndPaint(IntPtr window, ref PaintInformation paint);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int FillRect(IntPtr dc, ref NativeRectangle rectangle, IntPtr brush);

    [DllImport("gdi32.dll")]
    private static extern IntPtr GetStockObject(int index);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern IntPtr CreateSolidBrush(uint color);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(IntPtr value);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(IntPtr window);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyWindow(IntPtr window);
}
