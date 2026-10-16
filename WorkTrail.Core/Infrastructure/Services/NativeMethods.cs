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


using System;
using System.Runtime.InteropServices;
using System.Text;

namespace WorkTrail.Services;

/// <summary>
/// Declares the narrowly scoped Windows interop functions and binary structures used by infrastructure services.
/// </summary>
internal static class NativeMethods
{
    internal const int WhKeyboardLl = 13;
    internal const int WhMouseLl = 14;
    internal const int WmKeyDown = 0x0100;
    internal const int WmSysKeyDown = 0x0104;
    internal const int WmLButtonDown = 0x0201;
    internal const int WmRButtonDown = 0x0204;
    internal const int WmMButtonDown = 0x0207;

    /// <summary>
    /// Represents a low-level Windows hook callback.
    /// </summary>
    internal delegate IntPtr HookProc(int code, IntPtr wParam, IntPtr lParam);

    /// <summary>
    /// Represents a display-monitor enumeration callback.
    /// </summary>
    internal delegate bool MonitorEnumProc(IntPtr monitor, IntPtr hdc, ref Rect rectangle, IntPtr data);

    /// <summary>
    /// Represents a top-level window enumeration callback.
    /// </summary>
    internal delegate bool WindowEnumProc(IntPtr window, IntPtr data);

    /// <summary>
    /// Enumerates top-level windows through User32.
    /// </summary>
    [DllImport("user32.dll")]
    internal static extern bool EnumWindows(WindowEnumProc callback, IntPtr data);

    /// <summary>
    /// Returns whether a window is visible through User32.
    /// </summary>
    [DllImport("user32.dll")]
    internal static extern bool IsWindowVisible(IntPtr window);

    /// <summary>
    /// Returns whether a window is minimized through User32.
    /// </summary>
    [DllImport("user32.dll")]
    internal static extern bool IsIconic(IntPtr window);

    /// <summary>
    /// Reads a Desktop Window Manager attribute for a window.
    /// </summary>
    [DllImport("dwmapi.dll")]
    internal static extern int DwmGetWindowAttribute(IntPtr window, int attribute, out int value, int size);

    /// <summary>
    /// Represents a Win32 rectangle in screen coordinates.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct Rect
    {
        internal int Left;
        internal int Top;
        internal int Right;
        internal int Bottom;
    }

    /// <summary>
    /// Represents the Win32 structure used to read the last user-input tick count.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct LastInputInfo
    {
        internal uint Size;
        internal uint Time;
    }

    /// <summary>
    /// Represents the two-part Win32 FILETIME value used by system-time counters.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct FileTime
    {
        internal uint LowDateTime;
        internal uint HighDateTime;
    }

    /// <summary>
    /// Represents the Win32 physical and virtual memory-status structure.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct MemoryStatusEx
    {
        internal uint Length;
        internal uint MemoryLoad;
        internal ulong TotalPhysicalMemory;
        internal ulong AvailablePhysicalMemory;
        internal ulong TotalPageFile;
        internal ulong AvailablePageFile;
        internal ulong TotalVirtual;
        internal ulong AvailableVirtual;
        internal ulong AvailableExtendedVirtual;
    }

    /// <summary>
    /// Installs a low-level Windows hook.
    /// </summary>
    [DllImport("user32.dll")]
    internal static extern IntPtr SetWindowsHookEx(int hookId, HookProc callback, IntPtr module, uint threadId);

    /// <summary>
    /// Removes a previously installed Windows hook.
    /// </summary>
    [DllImport("user32.dll")]
    internal static extern bool UnhookWindowsHookEx(IntPtr hook);

    /// <summary>
    /// Forwards a hook event to the next hook in the Windows chain.
    /// </summary>
    [DllImport("user32.dll")]
    internal static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr wParam, IntPtr lParam);

    /// <summary>
    /// Reads the last user-input time through User32.
    /// </summary>
    [DllImport("user32.dll")]
    internal static extern bool GetLastInputInfo(ref LastInputInfo info);

    /// <summary>
    /// Returns the current foreground window handle through User32.
    /// </summary>
    [DllImport("user32.dll")]
    internal static extern IntPtr GetForegroundWindow();

    /// <summary>
    /// Retrieves the process identifier that owns a window.
    /// </summary>
    [DllImport("user32.dll")]
    internal static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

    /// <summary>
    /// Reads a window title into a caller-provided Unicode buffer.
    /// </summary>
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern int GetWindowText(IntPtr window, StringBuilder text, int maxCount);

    /// <summary>
    /// Reads a window rectangle in screen coordinates.
    /// </summary>
    [DllImport("user32.dll")]
    internal static extern bool GetWindowRect(IntPtr hWnd, out Rect rectangle);

    /// <summary>
    /// Enumerates display monitors through User32.
    /// </summary>
    [DllImport("user32.dll")]
    internal static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clip, MonitorEnumProc callback, IntPtr data);

    /// <summary>
    /// Reads aggregate idle, kernel, and user system times through Kernel32.
    /// </summary>
    [DllImport("kernel32.dll")]
    internal static extern bool GetSystemTimes(out FileTime idleTime, out FileTime kernelTime, out FileTime userTime);

    /// <summary>
    /// Reads physical and virtual memory availability through Kernel32.
    /// </summary>
    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx buffer);
}
