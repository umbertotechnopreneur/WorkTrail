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


using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace WorkTrail.Services;

/// <summary>Hosts the compact alpha-capable WorkTrail control inside the Windows taskbar without owning tracking state.</summary>
public sealed class TaskbarWidgetHost : IDisposable
{
    private readonly ILogger<TaskbarWidgetHost> _logger;
    /// <summary>Gets the widget width in device-independent pixels.</summary>
    public const int LogicalWidth = 288;

    /// <summary>Gets the widget height in device-independent pixels.</summary>
    public const int LogicalHeight = 40;

    private const int GwlStyle = -16;
    private const long WsChild = 0x40000000L;
    private const long WsPopup = unchecked((long)0x80000000);
    private const long WsCaption = 0x00C00000L;
    private const long WsThickFrame = 0x00040000L;
    private const long WsSysMenu = 0x00080000L;
    private const long WsMinimizeBox = 0x00020000L;
    private const long WsMaximizeBox = 0x00010000L;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpFrameChanged = 0x0020;
    private const uint SwpShowWindow = 0x0040;
    private const uint SwpAsyncWindowPos = 0x4000;
    private const int SwHide = 0;

    private readonly object _gate = new();
    private IntPtr _widgetHandle;
    private string _position = TaskbarWidgetPositions.Left;
    private int _regionWidth;
    private int _regionHeight;
    private bool _disposed;

    /// <summary>Initializes the taskbar host.</summary>
    public TaskbarWidgetHost(ILogger<TaskbarWidgetHost>? logger = null)
    {
        _logger = logger ?? NullLogger<TaskbarWidgetHost>.Instance;
    }

    /// <summary>Gets whether Explorer still owns the current widget HWND.</summary>
    public bool HasValidWidgetHandle
    {
        get
        {
            lock (_gate)
            {
                return !_disposed && _widgetHandle != IntPtr.Zero && IsWindow(_widgetHandle);
            }
        }
    }

    /// <summary>Embeds an alpha-capable control in the primary Windows taskbar.</summary>
    public bool Attach(IntPtr widgetHandle, string position)
    {
        if (widgetHandle == IntPtr.Zero)
        {
            _logger.LogWarning("Taskbar widget attach rejected: zero handle.");
            return false;
        }

        lock (_gate)
        {
            ThrowIfDisposed();
            _widgetHandle = widgetHandle;
            _position = NormalizePosition(position);
            _logger.LogInformation("Attempting taskbar attach. Handle={Handle} Position={Position}", widgetHandle, _position);
            var attached = TryAttachAndPosition(useAsyncPositioning: false, showWindow: false);
            _logger.LogInformation("Taskbar attach outcome={Attached}.", attached);
            return attached;
        }
    }

    /// <summary>Changes the placement of the embedded control without changing any tracking setting.</summary>
    public void Configure(string position)
    {
        lock (_gate)
        {
            ThrowIfDisposed();
            _position = NormalizePosition(position);
            TryAttachAndPosition(useAsyncPositioning: false, showWindow: true);
        }
    }

    /// <summary>Reattaches and repositions the visible widget after Explorer or display changes.</summary>
    public bool Recover()
    {
        lock (_gate)
        {
            ThrowIfDisposed();
            return TryAttachAndPosition(useAsyncPositioning: true, showWindow: true);
        }
    }

    /// <summary>Computes the taskbar client bounds where the widget should appear, scaled to the taskbar monitor.</summary>
    public static TaskbarWidgetBounds GetDesiredBounds(string position)
    {
        var taskbarHandle = FindWindow("Shell_TrayWnd", null);
        if (taskbarHandle == IntPtr.Zero || !IsWindow(taskbarHandle) || !GetWindowRect(taskbarHandle, out var taskbarScreenBounds) || !GetClientRect(taskbarHandle, out var taskbarClientBounds))
        {
            return new TaskbarWidgetBounds(0, 0, 0, 0, LogicalWidth, LogicalHeight, 1d);
        }

        var scale = Math.Max(1d, GetDpiForWindow(taskbarHandle) / 96d);
        var taskbarClientWidth = taskbarClientBounds.Right - taskbarClientBounds.Left;
        var taskbarClientHeight = taskbarClientBounds.Bottom - taskbarClientBounds.Top;
        var maxWidgetWidth = (int)Math.Ceiling(LogicalWidth * scale);
        var maxWidgetHeight = (int)Math.Ceiling(LogicalHeight * scale);
        var widgetScale = Math.Min(1d, Math.Min(taskbarClientWidth / (double)maxWidgetWidth, taskbarClientHeight / (double)maxWidgetHeight));
        var widgetWidth = Math.Max(1, (int)Math.Round(maxWidgetWidth * widgetScale));
        var widgetHeight = Math.Max(1, (int)Math.Round(maxWidgetHeight * widgetScale));
        if (taskbarClientWidth <= 0 || taskbarClientHeight <= 0)
        {
            return new TaskbarWidgetBounds(0, 0, 0, 0, LogicalWidth, LogicalHeight, 1d);
        }

        var normalized = NormalizePosition(position);
        var isHorizontalTaskbar = taskbarClientWidth >= taskbarClientHeight;
        var taskbarScreenHeight = taskbarScreenBounds.Bottom - taskbarScreenBounds.Top;
        var x = isHorizontalTaskbar ? normalized switch
        {
            TaskbarWidgetPositions.Right => Math.Max(0, taskbarClientWidth - widgetWidth - (int)Math.Ceiling(320 * scale)),
            _ => (int)Math.Ceiling(12 * scale)
        } : Math.Max(0, (taskbarClientWidth - widgetWidth) / 2);
        // Explorer can report a shorter client area than the visible horizontal taskbar; center within its actual screen bounds.
        var y = isHorizontalTaskbar ? Math.Max(0, (taskbarScreenHeight - widgetHeight) / 2) : normalized switch
        {
            TaskbarWidgetPositions.Right => Math.Max(0, taskbarClientHeight - widgetHeight - (int)Math.Ceiling(320 * scale)),
            _ => (int)Math.Ceiling(12 * scale)
        };

        var taskbarScreenX = taskbarScreenBounds.Left;
        var taskbarScreenY = taskbarScreenBounds.Top;
        return new TaskbarWidgetBounds(taskbarScreenX + x, taskbarScreenY + y, x, y, widgetWidth, widgetHeight, scale);
    }

    /// <summary>Removes taskbar parenting before the widget window is closed.</summary>
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            if (_widgetHandle != IntPtr.Zero && IsWindow(_widgetHandle))
            {
                ShowWindow(_widgetHandle, SwHide);
                SetParent(_widgetHandle, IntPtr.Zero);
            }

            _widgetHandle = IntPtr.Zero;
            _regionWidth = 0;
            _regionHeight = 0;
        }
    }

    private bool TryAttachAndPosition(bool useAsyncPositioning, bool showWindow)
    {
        if (_widgetHandle == IntPtr.Zero || !IsWindow(_widgetHandle))
        {
            _logger.LogWarning("Taskbar widget handle is invalid.");
            return false;
        }

        var taskbarHandle = FindWindow("Shell_TrayWnd", null);
        _logger.LogDebug("Found taskbar handle={TaskbarHandle}.", taskbarHandle);
        if (taskbarHandle == IntPtr.Zero || !IsWindow(taskbarHandle))
        {
            _logger.LogWarning("Taskbar window is not available for widget attachment.");
            return false;
        }

        var frameChanged = false;
        if (GetParent(_widgetHandle) != taskbarHandle)
        {
            var style = GetWindowStyle(_widgetHandle);
            var childStyle = (style | WsChild) & ~(WsPopup | WsCaption | WsThickFrame | WsSysMenu | WsMinimizeBox | WsMaximizeBox);
            SetWindowStyle(_widgetHandle, childStyle);
            frameChanged = true;
            var previousParent = SetParent(_widgetHandle, taskbarHandle);
            _logger.LogInformation("SetParent result={PreviousParent} NewParent={NewParent}.", previousParent, GetParent(_widgetHandle));
            if (GetParent(_widgetHandle) != taskbarHandle)
            {
                _logger.LogWarning("SetParent did not reparent the widget to the taskbar.");
                return false;
            }
        }

        var bounds = GetDesiredBounds(_position);
        _logger.LogDebug("Positioning taskbar widget. Bounds=({X},{Y},{Width},{Height}) Scale={Scale}.", bounds.ClientX, bounds.ClientY, bounds.Width, bounds.Height, bounds.Scale);
        var flags = SwpNoZOrder | SwpNoActivate;
        if (frameChanged)
        {
            flags |= SwpFrameChanged;
        }

        if (showWindow)
        {
            flags |= SwpShowWindow;
        }

        if (useAsyncPositioning)
        {
            flags |= SwpAsyncWindowPos;
        }

        if ((frameChanged || _regionWidth != bounds.Width || _regionHeight != bounds.Height) && !ApplyWindowRegion(bounds.Width, bounds.Height))
        {
            return false;
        }

        var positioned = SetWindowPos(_widgetHandle, IntPtr.Zero, bounds.ClientX, bounds.ClientY, bounds.Width, bounds.Height, flags);
        _logger.LogDebug("SetWindowPos result={Positioned}.", positioned);
        return positioned;
    }

    private bool ApplyWindowRegion(int width, int height)
    {
        var region = CreateRectRgn(0, 0, width, height);
        if (region == IntPtr.Zero)
        {
            _logger.LogWarning("CreateRectRgn failed while sizing the taskbar widget.");
            return false;
        }

        // After a successful SetWindowRgn call Windows owns the HRGN; delete it only on failure.
        if (SetWindowRgn(_widgetHandle, region, redraw: true) == 0)
        {
            _ = DeleteObject(region);
            _logger.LogWarning("SetWindowRgn failed while sizing the taskbar widget.");
            return false;
        }

        _regionWidth = width;
        _regionHeight = height;
        return true;
    }

    private static string NormalizePosition(string? position) => position == TaskbarWidgetPositions.Right ? position : TaskbarWidgetPositions.Left;

    private static long GetWindowStyle(IntPtr windowHandle) => IntPtr.Size == 8 ? GetWindowLongPtr64(windowHandle, GwlStyle).ToInt64() : GetWindowLong32(windowHandle, GwlStyle);

    private static void SetWindowStyle(IntPtr windowHandle, long style)
    {
        if (IntPtr.Size == 8)
        {
            _ = SetWindowLongPtr64(windowHandle, GwlStyle, new IntPtr(style));
        }
        else
        {
            _ = SetWindowLong32(windowHandle, GwlStyle, unchecked((int)style));
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(TaskbarWidgetHost));
        }
    }

    /// <summary>Describes the bounds and scale for the embedded taskbar widget.</summary>
    /// <param name="ScreenX">Screen-left physical pixel coordinate.</param>
    /// <param name="ScreenY">Screen-top physical pixel coordinate.</param>
    /// <param name="ClientX">Taskbar-client-left physical pixel coordinate.</param>
    /// <param name="ClientY">Taskbar-client-top physical pixel coordinate.</param>
    /// <param name="Width">Physical pixel width.</param>
    /// <param name="Height">Physical pixel height.</param>
    /// <param name="Scale">Taskbar monitor scale factor.</param>
    public sealed record TaskbarWidgetBounds(int ScreenX, int ScreenY, int ClientX, int ClientY, int Width, int Height, double Scale);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindow(string className, string? windowName);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(IntPtr windowHandle);

    [DllImport("user32.dll")]
    private static extern IntPtr GetParent(IntPtr windowHandle);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetParent(IntPtr childWindow, IntPtr newParent);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr64(IntPtr windowHandle, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr64(IntPtr windowHandle, int index, IntPtr newValue);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    private static extern int GetWindowLong32(IntPtr windowHandle, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongW")]
    private static extern int SetWindowLong32(IntPtr windowHandle, int index, int newValue);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetClientRect(IntPtr windowHandle, out NativeRect rectangle);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr windowHandle, out NativeRect rectangle);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr windowHandle);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr windowHandle, IntPtr insertAfter, int x, int y, int width, int height, uint flags);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int SetWindowRgn(IntPtr windowHandle, IntPtr region, [MarshalAs(UnmanagedType.Bool)] bool redraw);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern IntPtr CreateRectRgn(int left, int top, int right, int bottom);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(IntPtr objectHandle);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(IntPtr windowHandle, int command);
}
