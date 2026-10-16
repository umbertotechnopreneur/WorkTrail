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
using WorkTrail.Services;

namespace WorkTrail.Application;

/// <summary>Stable identifiers for the user-facing top-level windows.</summary>
public static class WindowStateKeys
{
    /// <summary>Identifies the main player window.</summary>
    public const string Main = "main";

    /// <summary>Identifies the world-clock city picker dialog.</summary>
    public const string WorldClockCityPicker = "world-clock-city-picker";

    /// <summary>Identifies the independent world-clock comparison window.</summary>
    public const string WorldClocks = "world-clocks";

    /// <summary>Identifies the independent geographical day-and-night map window.</summary>
    public const string WorldMap = "world-map";

    /// <summary>Identifies the independent lunar-phase window.</summary>
    public const string LunarPhase = "lunar-phase";

    /// <summary>Identifies the observer's local sky window.</summary>
    public const string LocalSky = "local-sky";

    /// <summary>Identifies the astronomical event agenda.</summary>
    public const string AstronomyAgenda = "astronomy-agenda";

    /// <summary>Identifies the independent planar/spherical Earth window.</summary>
    public const string CelestialMap = "celestial-map";

    /// <summary>Identifies the native activity-calendar dialog window.</summary>
    public const string ActivityCalendar = "activity-calendar";

    /// <summary>Identifies the historical screenshot AI-reprocessing dialog window.</summary>
    public const string AiScreenshotReprocessing = "ai-screenshot-reprocessing";

    /// <summary>Identifies the screenshot gallery window.</summary>
    public const string Screenshots = "screenshots";


    /// <summary>Identifies the about window.</summary>
    public const string About = "about";

    /// <summary>Identifies the third-party licenses window.</summary>
    public const string Licenses = "licenses";

    /// <summary>Identifies the local search window.</summary>
    public const string Search = "search";

    /// <summary>Identifies the local search-index progress window.</summary>
    public const string SearchIndexing = "search-indexing";

    /// <summary>Identifies the screenshot schedule window.</summary>
    public const string Schedule = "schedule";

    /// <summary>Identifies the first-run and reusable Quick Setup window.</summary>
    public const string QuickSetup = "quick-setup";

    /// <summary>Identifies the reusable message dialog window.</summary>
    public const string Dialog = "dialog";


    /// <summary>Identifies the responsive label-management dialog.</summary>
    public const string ActivityLabels = "activity-labels";

    /// <summary>Identifies the Mica note editor for an already retained VIP capture.</summary>
    public const string VipSnapshotNote = "vip-snapshot-note";
    /// <summary>The VIP capture countdown window.</summary>
    public const string VipSnapshotCountdown = "vip-snapshot-countdown";

    /// <summary>Identifies the analytical export workspace.</summary>
    public const string ReportExport = "report-export";

    /// <summary>Identifies the AI provider connection test dialog window.</summary>
    public const string AiConnectionTest = "ai-connection-test";
}

/// <summary>Persists and restores native top-level window placement.</summary>
public sealed class WindowStateService
{
    private const uint MonitorDefaultToNearest = 2;
    private const uint SetWindowPosNoActivate = 0x0010;
    private const uint SetWindowPosNoZOrder = 0x0004;
    private const uint ShowNormal = 1;
    private const int ExtendedWindowStyle = -20;
    private const int ToolWindowStyle = 0x00000080;
    private readonly LocalStore _store;

    /// <summary>Creates the window-state service over the shared settings store.</summary>
    public WindowStateService(LocalStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    /// <summary>Gets the minimum usable logical size for the supplied persisted window key.</summary>
    public static WindowMinimumSize GetMinimumSize(string windowKey)
    {
        if (string.IsNullOrWhiteSpace(windowKey))
        {
            throw new ArgumentException("A window key is required.", nameof(windowKey));
        }

        return windowKey switch
        {
            WindowStateKeys.Main => new(470, 240),
            WindowStateKeys.ActivityCalendar => new(760, 560),
            WindowStateKeys.ReportExport => new(660, 520),
            WindowStateKeys.AiScreenshotReprocessing => new(640, 560),
            WindowStateKeys.Screenshots => new(760, 540),
            WindowStateKeys.About => new(640, 480),
            WindowStateKeys.Licenses => new(720, 520),
            WindowStateKeys.Search => new(560, 156),
            WindowStateKeys.SearchIndexing => new(560, 420),
            WindowStateKeys.Schedule => new(620, 480),
            WindowStateKeys.QuickSetup => new(760, 560),
            WindowStateKeys.Dialog => new(320, 196),
            WindowStateKeys.WorldClocks => new(480, 240),
            WindowStateKeys.WorldMap => new(192, 160),
            WindowStateKeys.LunarPhase => new(192, 192),
            WindowStateKeys.LocalSky => new(320, 320),
            WindowStateKeys.AstronomyAgenda => new(280, 320),
            WindowStateKeys.CelestialMap => new(320, 280),
            WindowStateKeys.WorldClockCityPicker => new(500, 560),
            WindowStateKeys.ActivityLabels => new(340, 280),
            WindowStateKeys.VipSnapshotNote => new(500, 480),
            WindowStateKeys.VipSnapshotCountdown => new(480, 440),
            WindowStateKeys.AiConnectionTest => new(480, 480),
            _ => throw new ArgumentException("The window key is not supported.", nameof(windowKey))
        };
    }

    /// <summary>Persists one window's open or closed state without changing its saved placement.</summary>
    public AppSettings SetOpenState(string windowKey, bool isOpen)
    {
        _ = GetMinimumSize(windowKey);
        var settings = _store.LoadSettings();
        var openStates = settings.WindowOpenStates is null
            ? new Dictionary<string, bool>(StringComparer.Ordinal)
            : new Dictionary<string, bool>(settings.WindowOpenStates, StringComparer.Ordinal);
        openStates[windowKey] = isOpen;
        var updated = settings with { WindowOpenStates = openStates };
        // LocalStore writes atomically; persistence failures propagate and the facade retains its previous snapshot.
        _store.SaveSettings(updated);
        return updated;
    }

    internal static void ValidateOpenStates(IReadOnlyDictionary<string, bool>? openStates)
    {
        if (openStates is null) return;
        foreach (var key in openStates.Keys)
        {
            // Unsupported persisted keys are a contract error; never silently drop a saved window.
            _ = GetMinimumSize(key);
        }
    }

    /// <summary>Persists native placement and returns its committed settings for the runtime snapshot.</summary>
    public (WindowState State, AppSettings Settings) Save(string windowKey, long windowHandle)
    {
        ValidateRequest(windowKey, windowHandle);
        var handle = new IntPtr(windowHandle);
        var monitor = GetMonitorForWindow(handle);
        var rect = GetNormalWindowRect(handle, monitor);
        var state = new WindowState(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top, monitor.DeviceName)
        {
            Dpi = ReadWindowDpi(handle),
            MonitorWorkArea = monitor.WorkArea,
            MonitorId = monitor.DeviceId
        };
        if (state.Width <= 0 || state.Height <= 0)
        {
            throw new InvalidOperationException("The native window returned invalid bounds.");
        }

        var settings = _store.LoadSettings();
        var placements = settings.WindowStates is null
            ? new Dictionary<string, WindowState>(StringComparer.Ordinal)
            : new Dictionary<string, WindowState>(settings.WindowStates, StringComparer.Ordinal);
        placements[windowKey] = state;
        var updated = settings with { WindowStates = placements };
        // A failed write propagates before the facade can publish these settings; no reread is needed after success.
        _store.SaveSettings(updated);
        return (state, updated);
    }

    /// <summary>Restores the saved placement and keeps it inside the selected monitor work area.</summary>
    public WindowState? Restore(string windowKey, long windowHandle)
    {
        ValidateRequest(windowKey, windowHandle);
        var settings = _store.LoadSettings();
        if (settings.WindowStates is null || !settings.WindowStates.TryGetValue(windowKey, out var savedState))
        {
            return null;
        }

        ValidatePersistedState(savedState);
        var handle = new IntPtr(windowHandle);
        var currentMonitor = GetMonitorForWindow(handle);
        var monitors = EnumerateMonitors();
        // Device identity takes precedence over DISPLAY1/2, whose numbering can change after docking.
        var targetMonitor = (!string.IsNullOrWhiteSpace(savedState.MonitorId)
            ? monitors.FirstOrDefault(candidate => string.Equals(candidate.DeviceId, savedState.MonitorId, StringComparison.OrdinalIgnoreCase))
            : monitors.FirstOrDefault(candidate => candidate.DeviceName.Equals(savedState.MonitorDeviceName, StringComparison.OrdinalIgnoreCase)))
            ?? monitors.FirstOrDefault(candidate => candidate.IsPrimary) ?? currentMonitor;
        var minimumSize = GetMinimumSize(windowKey);
        // Move to the target before querying effective HWND DPI. The final bounds replace native
        // WM_DPICHANGED suggestions, so saved dimensions are scaled exactly once.
        if (!string.Equals(currentMonitor.DeviceName, targetMonitor.DeviceName, StringComparison.OrdinalIgnoreCase))
        {
            var landing = WindowStateCalculator.ClampToWorkArea(
                savedState with { X = targetMonitor.WorkArea.X, Y = targetMonitor.WorkArea.Y },
                targetMonitor.WorkArea, targetMonitor.DeviceName);
            ApplyBounds(handle, landing);
        }
        var safeState = WindowStateCalculator.RebaseToMonitor(
            savedState, targetMonitor.WorkArea, targetMonitor.DeviceName, ReadWindowDpi(handle), minimumSize)
            with
        { MonitorId = targetMonitor.DeviceId };
        // Geometry restoration must retain visibility, including a main window intentionally hidden in the notification area.
        ApplyBounds(handle, safeState);
        return safeState;
    }

    private static void ApplyBounds(IntPtr handle, WindowState state)
    {
        if (!SetWindowPos(handle, IntPtr.Zero, state.X, state.Y, state.Width, state.Height, SetWindowPosNoActivate | SetWindowPosNoZOrder))
        {
            throw new InvalidOperationException($"Unable to restore window bounds (Win32 error {Marshal.GetLastWin32Error()}).");
        }
    }

    private static uint ReadWindowDpi(IntPtr handle)
    {
        var dpi = GetDpiForWindow(handle);
        return dpi != 0 ? dpi : throw new InvalidOperationException("Unable to read the window DPI.");
    }

    private static void ValidateRequest(string windowKey, long windowHandle)
    {
        _ = GetMinimumSize(windowKey);

        if (windowHandle == 0)
        {
            throw new ArgumentException("A native window handle is required.", nameof(windowHandle));
        }
    }

    private static void ValidatePersistedState(WindowState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (state.Width <= 0 || state.Height <= 0 || string.IsNullOrWhiteSpace(state.MonitorDeviceName)
            || state.Dpi > 960 || (state.Dpi != 0 && state.Dpi < 48)
            || state.MonitorWorkArea is { Width: <= 0 } or { Height: <= 0 })
        {
            throw new InvalidOperationException("Persisted window state is invalid.");
        }
    }

    private static NativeMonitor GetMonitorForWindow(IntPtr handle)
    {
        var monitorHandle = MonitorFromWindow(handle, MonitorDefaultToNearest);
        if (monitorHandle == IntPtr.Zero)
        {
            throw new InvalidOperationException($"Unable to identify the window monitor (Win32 error {Marshal.GetLastWin32Error()}).");
        }

        return ReadMonitor(monitorHandle);
    }

    private static IReadOnlyList<NativeMonitor> EnumerateMonitors()
    {
        var monitors = new List<NativeMonitor>();
        MonitorEnumProc callback = (monitor, _, _, _) =>
        {
            monitors.Add(ReadMonitor(monitor));
            return true;
        };
        if (!EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, callback, IntPtr.Zero))
        {
            throw new InvalidOperationException($"Unable to enumerate display monitors (Win32 error {Marshal.GetLastWin32Error()}).");
        }

        return monitors;
    }

    private static NativeMonitor ReadMonitor(IntPtr monitorHandle)
    {
        var monitorInfo = new MonitorInfoEx { CbSize = (uint)Marshal.SizeOf<MonitorInfoEx>() };
        if (!GetMonitorInfo(monitorHandle, ref monitorInfo) || string.IsNullOrWhiteSpace(monitorInfo.DeviceName))
        {
            throw new InvalidOperationException($"Unable to read monitor information (Win32 error {Marshal.GetLastWin32Error()}).");
        }

        var work = monitorInfo.WorkArea;
        var device = new DisplayDevice { Size = (uint)Marshal.SizeOf<DisplayDevice>() };
        // Virtual/remote displays may not expose interface identity; preserve the GDI name as a legacy fallback.
        var deviceId = EnumDisplayDevices(monitorInfo.DeviceName, 0, ref device, 1)
            && !string.IsNullOrWhiteSpace(device.DeviceId) ? device.DeviceId : null;
        return new NativeMonitor(
            monitorInfo.DeviceName,
            new WindowWorkArea(work.Left, work.Top, work.Right - work.Left, work.Bottom - work.Top),
            monitorInfo.MonitorArea, deviceId, (monitorInfo.Flags & 1) != 0);
    }

    private static NativeRect GetNormalWindowRect(IntPtr handle, NativeMonitor monitor)
    {
        var placement = new NativeWindowPlacement { Length = (uint)Marshal.SizeOf<NativeWindowPlacement>() };
        if (!GetWindowPlacement(handle, ref placement))
        {
            // A failed native read must not overwrite the previous valid placement with synthetic bounds.
            throw new InvalidOperationException($"Unable to read window placement (Win32 error {Marshal.GetLastWin32Error()}).");
        }

        if (placement.ShowCommand == ShowNormal) return GetWindowRect(handle);

        Marshal.SetLastPInvokeError(0);
        var extendedStyle = GetWindowLong(handle, ExtendedWindowStyle);
        var error = Marshal.GetLastWin32Error();
        if (extendedStyle == 0 && error != 0)
        {
            throw new InvalidOperationException($"Unable to read window style (Win32 error {error}).");
        }

        var normal = placement.NormalPosition;
        if ((extendedStyle & ToolWindowStyle) == 0)
        {
            // WINDOWPLACEMENT uses workspace coordinates for ordinary top-level windows. Our stored bounds
            // and SetWindowPos use screen coordinates, so account for taskbars on the monitor's top or left.
            var offsetX = monitor.WorkArea.X - monitor.MonitorArea.Left;
            var offsetY = monitor.WorkArea.Y - monitor.MonitorArea.Top;
            normal.Left += offsetX;
            normal.Right += offsetX;
            normal.Top += offsetY;
            normal.Bottom += offsetY;
        }

        return normal;
    }

    private static NativeRect GetWindowRect(IntPtr handle)
    {
        if (!GetWindowRectNative(handle, out var rect))
        {
            throw new InvalidOperationException($"Unable to read window bounds (Win32 error {Marshal.GetLastWin32Error()}).");
        }

        return rect;
    }

    private sealed record NativeMonitor(string DeviceName, WindowWorkArea WorkArea, NativeRect MonitorArea, string? DeviceId, bool IsPrimary);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DisplayDevice
    {
        public uint Size;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceString;
        public uint StateFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceId;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceKey;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeWindowPlacement
    {
        public uint Length;
        public uint Flags;
        public uint ShowCommand;
        public NativePoint MinimumPosition;
        public NativePoint MaximumPosition;
        public NativeRect NormalPosition;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MonitorInfoEx
    {
        public uint CbSize;
        public NativeRect MonitorArea;
        public NativeRect WorkArea;
        public uint Flags;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string DeviceName;
    }

    private delegate bool MonitorEnumProc(IntPtr monitor, IntPtr hdc, IntPtr monitorRect, IntPtr data);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr handle);

    [DllImport("user32.dll", EntryPoint = "EnumDisplayDevicesW", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumDisplayDevices(string deviceName, uint deviceIndex, ref DisplayDevice device, uint flags);

    [DllImport("user32.dll", EntryPoint = "EnumDisplayMonitors", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clipRect, MonitorEnumProc callback, IntPtr data);

    [DllImport("user32.dll", EntryPoint = "GetMonitorInfoW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfoEx monitorInfo);

    [DllImport("user32.dll", EntryPoint = "MonitorFromWindow", SetLastError = true)]
    private static extern IntPtr MonitorFromWindow(IntPtr handle, uint flags);

    [DllImport("user32.dll", EntryPoint = "GetWindowRect", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRectNative(IntPtr handle, out NativeRect rect);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowPlacement(IntPtr handle, ref NativeWindowPlacement placement);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW", SetLastError = true)]
    private static extern int GetWindowLong(IntPtr handle, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowPos", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr handle, IntPtr insertAfter, int x, int y, int width, int height, uint flags);
}
