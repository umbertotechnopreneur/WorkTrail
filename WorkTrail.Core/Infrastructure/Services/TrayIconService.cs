// SPDX-License-Identifier: MIT

using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;

namespace WorkTrail.Services;

/// <summary>Provides localized labels for window visibility and application exit in the notification-area menu.</summary>
public sealed record TrayIconMenuLabels(string ShowMainWindow, string HideMainWindow, string CloseApplication);

/// <summary>Identifies application commands forwarded from the native notification-area menu.</summary>
public enum TrayIconMenuCommand : uint
{
    /// <summary>Pauses or enables notifications.</summary>
    ToggleNotifications = 3,
    /// <summary>Runs the existing manual VIP capture workflow.</summary>
    TakeVipSnapshot = 4,
    /// <summary>Pauses or resumes tracking.</summary>
    ToggleTracking = 5,
    /// <summary>Shows international clocks.</summary>
    WorldClocks = 6,
    /// <summary>Shows the astronomical calendar.</summary>
    AstronomicalCalendar = 7,
    /// <summary>Shows the day/night map.</summary>
    DayNightMap = 8,
    /// <summary>Shows the day/night globe.</summary>
    DayNightGlobe = 9,
    /// <summary>Shows application settings.</summary>
    Settings = 10,
    /// <summary>Shows About.</summary>
    About = 11,
    /// <summary>Reveals all existing WorkTrail windows.</summary>
    ShowAllWindows = 12
}

/// <summary>Contains current localized action labels and availability for one tray menu opening.</summary>
public sealed record TrayIconMenuState(
    string ShowAllWindows,
    string ToggleNotifications,
    string TakeVipSnapshot,
    string ToggleTracking,
    string WorldClocks,
    string AstronomicalCalendar,
    string DayNightMap,
    string DayNightGlobe,
    string Settings,
    string About,
    bool WorkspaceReady,
    bool CanCaptureVip);

/// <summary>Owns the notification-area icon that can hide and restore one top-level WorkTrail window.</summary>
public sealed class TrayIconService : IDisposable
{
    private const uint NotificationIconAdd = 0x00000000;
    private const uint NotificationIconModify = 0x00000001;
    private const uint NotificationIconDelete = 0x00000002;
    private const uint NotificationIconSetVersion = 0x00000004;
    private const uint NotificationIconMessage = 0x00000001;
    private const uint NotificationIconIcon = 0x00000002;
    private const uint NotificationIconTip = 0x00000004;
    private const uint NotificationIconShowTip = 0x00000080;
    private const uint NotificationIconVersion4 = 4;
    private const uint TrayCallbackMessage = 0x8000 + 0x350;
    private const uint IconSelectMessage = 0x0400;
    private const uint IconKeySelectMessage = 0x0401;
    private const uint ContextMenuMessage = 0x007B;
    private const uint ImageIcon = 1;
    private const uint LoadImageFromFile = 0x0010;
    private const uint MenuString = 0x0000;
    private const uint MenuDisabled = 0x0003;
    private const uint MenuSeparator = 0x0800;
    private const uint TrackPopupMenuReturnCommand = 0x0100;
    private const uint TrackPopupMenuNoNotify = 0x0080;
    private const uint TrackPopupMenuRightButton = 0x0002;
    private const uint TrackPopupMenuWorkArea = 0x10000;
    private const uint MenuCommandToggleWindow = 1;
    private const uint MenuCommandCloseApplication = 2;
    private const uint NullWindowMessage = 0;
    private const int ShowWindowHide = 0;
    private const int ShowWindowNormal = 1;
    private const int ShowWindowRestore = 9;
    private static readonly UIntPtr SubclassId = new(1);
    private static readonly SubclassProcDelegate SubclassProcedure = WindowSubclassProcedure;
    private readonly ILogger _logger;
    private readonly NotifyIconDelegate _notifyIcon;
    private GCHandle _selfHandle;
    private IntPtr _windowHandle;
    private IntPtr _iconHandle;
    private TrayIconMenuLabels? _menuLabels;
    private string _toolTip = string.Empty;
    private uint _taskbarCreatedMessage;
    private bool _iconRegistered;
    private bool _disposed;

    /// <summary>Creates the native notification-area owner with process-local diagnostics.</summary>
    public TrayIconService(ILogger logger)
        : this(logger, ShellNotifyIcon)
    {
    }

    internal TrayIconService(ILogger logger, NotifyIconDelegate notifyIcon)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _notifyIcon = notifyIcon ?? throw new ArgumentNullException(nameof(notifyIcon));
    }

    /// <summary>Occurs after the user explicitly selects Close app in the notification-area context menu.</summary>
    public event EventHandler? ExitRequested;

    /// <summary>Occurs when an application action is selected; handlers must defer work until the native callback returns.</summary>
    public event Action<TrayIconMenuCommand>? CommandRequested;

    /// <summary>Gets or sets the UI-owned snapshot provider invoked immediately before the native menu opens.</summary>
    public Func<TrayIconMenuState>? MenuStateProvider { get; set; }

    /// <summary>Registers the notification-area icon without changing the main window's visibility.</summary>
    public void ShowInNotificationArea(IntPtr windowHandle, string iconPath, string toolTip, TrayIconMenuLabels menuLabels)
    {
        ThrowIfDisposed();
        EnsureAttached(windowHandle, iconPath, toolTip, menuLabels);
    }

    /// <summary>Registers the notification-area icon if necessary, then hides the main window from the taskbar.</summary>
    public void HideToNotificationArea(IntPtr windowHandle, string iconPath, string toolTip, TrayIconMenuLabels menuLabels)
    {
        ShowInNotificationArea(windowHandle, iconPath, toolTip, menuLabels);

        // Hiding the real top-level window removes its taskbar button while leaving its message queue available for the tray callback.
        _ = ShowWindow(_windowHandle, ShowWindowHide);
    }

    /// <summary>Restores and foregrounds the attached main window when it was hidden or minimized.</summary>
    public void ShowMainWindow()
    {
        ThrowIfDisposed();
        if (_windowHandle == IntPtr.Zero)
        {
            return;
        }

        RestoreMainWindowIfHidden();
        // A minimized window is still visible to USER32 and needs an explicit restore before foregrounding.
        if (IsIconic(_windowHandle)) _ = ShowWindow(_windowHandle, ShowWindowRestore);
        _ = SetForegroundWindow(_windowHandle);
    }

    /// <summary>Removes the icon and releases the native subclass before the owning window is destroyed.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        MenuStateProvider = null;
        ReleaseNativeResources();
    }

    private void EnsureAttached(IntPtr windowHandle, string iconPath, string toolTip, TrayIconMenuLabels menuLabels)
    {
        if (windowHandle == IntPtr.Zero)
        {
            throw new ArgumentException("A valid top-level window handle is required for the notification-area icon.", nameof(windowHandle));
        }

        if (string.IsNullOrWhiteSpace(iconPath))
        {
            throw new ArgumentException("The notification-area icon path is required.", nameof(iconPath));
        }

        if (string.IsNullOrWhiteSpace(toolTip))
        {
            throw new ArgumentException("The notification-area tooltip is required.", nameof(toolTip));
        }

        ArgumentNullException.ThrowIfNull(menuLabels);
        if (string.IsNullOrWhiteSpace(menuLabels.ShowMainWindow)
            || string.IsNullOrWhiteSpace(menuLabels.HideMainWindow)
            || string.IsNullOrWhiteSpace(menuLabels.CloseApplication))
        {
            throw new ArgumentException("Every notification-area context-menu label is required.", nameof(menuLabels));
        }

        if (_windowHandle != IntPtr.Zero)
        {
            if (_windowHandle != windowHandle)
            {
                throw new InvalidOperationException("The notification-area icon is already attached to a different window.");
            }

            _menuLabels = menuLabels;
            _toolTip = toolTip;
            try
            {
                // Explorer may have lost the icon even when no TaskbarCreated callback has reached this window yet.
                EnsureIconRegistered();
            }
            catch (Exception exception)
            {
                RestoreMainWindowIfHidden();
                _logger.LogError(exception, "Notification-area icon verification failed; the main window remains available.");
                throw;
            }

            return;
        }

        if (!File.Exists(iconPath))
        {
            throw new FileNotFoundException("The notification-area icon file is unavailable.", iconPath);
        }

        _windowHandle = windowHandle;
        _menuLabels = menuLabels;
        _toolTip = toolTip;
        _selfHandle = GCHandle.Alloc(this);
        try
        {
            _taskbarCreatedMessage = RegisterWindowMessage("TaskbarCreated");
            if (_taskbarCreatedMessage == 0)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "WorkTrail could not register for taskbar recreation messages.");
            }

            if (!SetWindowSubclass(_windowHandle, SubclassProcedure, SubclassId, GCHandle.ToIntPtr(_selfHandle)))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "WorkTrail could not receive notification-area icon activation messages.");
            }

            _iconHandle = LoadImage(IntPtr.Zero, iconPath, ImageIcon, 0, 0, LoadImageFromFile);
            if (_iconHandle == IntPtr.Zero)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "WorkTrail could not load its notification-area icon.");
            }

            EnsureIconRegistered();
        }
        catch (Exception exception)
        {
            // A failed registration leaves the player visible and removes every partially registered native resource.
            ReleaseNativeResources();
            _logger.LogError(exception, "Notification-area icon initialization failed.");
            throw;
        }
    }

    private void ReleaseNativeResources()
    {
        RemoveNotificationIcon();

        if (_windowHandle != IntPtr.Zero)
        {
            _ = RemoveWindowSubclass(_windowHandle, SubclassProcedure, SubclassId);
        }

        if (_iconHandle != IntPtr.Zero)
        {
            _ = DestroyIcon(_iconHandle);
            _iconHandle = IntPtr.Zero;
        }

        if (_selfHandle.IsAllocated)
        {
            _selfHandle.Free();
        }

        _windowHandle = IntPtr.Zero;
        _menuLabels = null;
        _toolTip = string.Empty;
    }

    private void EnsureIconRegistered()
    {
        var notification = CreateNotificationData();
        if (_iconRegistered)
        {
            if (_notifyIcon(NotificationIconModify, ref notification))
            {
                return;
            }

            // A successful earlier NIM_ADD describes the old shell; only a current shell response permits hiding.
            _iconRegistered = false;
            _logger.LogWarning("The notification-area icon is no longer available; registering it again. Win32Error={Win32Error}", Marshal.GetLastWin32Error());
        }

        if (!_notifyIcon(NotificationIconAdd, ref notification))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "WorkTrail could not add its notification-area icon.");
        }

        _iconRegistered = true;
        if (!_notifyIcon(NotificationIconSetVersion, ref notification))
        {
            var error = Marshal.GetLastWin32Error();
            // A partially configured icon must not strand the player with unusable activation messages.
            RemoveNotificationIcon();
            throw new Win32Exception(error, "WorkTrail could not configure notification-area icon activation.");
        }

        _logger.LogInformation("Notification-area icon attached to the WorkTrail main window.");
    }

    private void RemoveNotificationIcon()
    {
        if (!_iconRegistered)
        {
            return;
        }

        var notification = CreateNotificationData();
        if (!_notifyIcon(NotificationIconDelete, ref notification))
        {
            // Cleanup cannot retain ownership indefinitely when the shell is shutting down or unavailable.
            _logger.LogWarning("The notification-area icon could not be removed. Win32Error={Win32Error}", Marshal.GetLastWin32Error());
        }

        _iconRegistered = false;
    }

    private void OnTaskbarCreated()
    {
        try
        {
            // Reuse the live window subclass and HICON; repeated broadcasts must not create duplicate native owners.
            EnsureIconRegistered();
            _logger.LogInformation("Notification-area icon verified after taskbar recreation.");
        }
        catch (Exception exception)
        {
            // Recovery is best-effort inside a native callback. Expose the player and keep its owner attached for a later retry.
            RestoreMainWindowIfHidden();
            _logger.LogError(exception, "Notification-area icon recovery after taskbar recreation failed; the main window remains available.");
        }
    }

    private void RestoreMainWindowIfHidden()
    {
        if (!IsWindowVisible(_windowHandle))
        {
            _ = ShowWindow(_windowHandle, ShowWindowNormal);
            if (!IsWindowVisible(_windowHandle))
            {
                _logger.LogError("The main window could not be restored after notification-area icon failure.");
            }
        }
    }

    private void ToggleMainWindowVisibility()
    {
        if (_disposed || _windowHandle == IntPtr.Zero)
        {
            return;
        }

        if (IsWindowVisible(_windowHandle))
        {
            EnsureIconRegistered();
            _ = ShowWindow(_windowHandle, ShowWindowHide);
            return;
        }

        // Restoring the same native window preserves the active WinUI surface and places it in the foreground after a tray click.
        ShowMainWindow();
    }

    private void ShowContextMenu()
    {
        if (_disposed || _windowHandle == IntPtr.Zero || _menuLabels is null)
        {
            return;
        }

        var menu = CreatePopupMenu();
        if (menu == IntPtr.Zero)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "WorkTrail could not create its notification-area context menu.");
        }

        try
        {
            var toggleText = IsWindowVisible(_windowHandle)
                ? _menuLabels.HideMainWindow
                : _menuLabels.ShowMainWindow;
            AppendCommand(menu, MenuCommandToggleWindow, toggleText);
            if (MenuStateProvider?.Invoke() is { } state)
            {
                AppendCommand(menu, (uint)TrayIconMenuCommand.ShowAllWindows, state.ShowAllWindows, state.WorkspaceReady);
                AppendSeparator(menu);
                AppendCommand(menu, (uint)TrayIconMenuCommand.ToggleNotifications, state.ToggleNotifications, state.WorkspaceReady);
                AppendCommand(menu, (uint)TrayIconMenuCommand.TakeVipSnapshot, state.TakeVipSnapshot, state.CanCaptureVip);
                AppendCommand(menu, (uint)TrayIconMenuCommand.ToggleTracking, state.ToggleTracking, state.WorkspaceReady);
                AppendSeparator(menu);
                AppendCommand(menu, (uint)TrayIconMenuCommand.WorldClocks, state.WorldClocks, state.WorkspaceReady);
                AppendCommand(menu, (uint)TrayIconMenuCommand.AstronomicalCalendar, state.AstronomicalCalendar, state.WorkspaceReady);
                AppendCommand(menu, (uint)TrayIconMenuCommand.DayNightMap, state.DayNightMap, state.WorkspaceReady);
                AppendCommand(menu, (uint)TrayIconMenuCommand.DayNightGlobe, state.DayNightGlobe, state.WorkspaceReady);
                AppendSeparator(menu);
                AppendCommand(menu, (uint)TrayIconMenuCommand.Settings, state.Settings, state.WorkspaceReady);
                AppendCommand(menu, (uint)TrayIconMenuCommand.About, state.About, state.WorkspaceReady);
                AppendSeparator(menu);
            }
            AppendCommand(menu, MenuCommandCloseApplication, _menuLabels.CloseApplication);

            if (!GetCursorPos(out var cursorPosition))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "WorkTrail could not position its notification-area context menu.");
            }

            // Windows requires the owner to be foreground before a tray popup so outside clicks dismiss it correctly.
            _ = SetForegroundWindow(_windowHandle);
            var command = TrackPopupMenuEx(
                menu,
                TrackPopupMenuReturnCommand | TrackPopupMenuNoNotify | TrackPopupMenuRightButton | TrackPopupMenuWorkArea,
                cursorPosition.X,
                cursorPosition.Y,
                _windowHandle,
                IntPtr.Zero);
            if (command == MenuCommandToggleWindow)
            {
                ToggleMainWindowVisibility();
            }
            else if (command == MenuCommandCloseApplication)
            {
                ExitRequested?.Invoke(this, EventArgs.Empty);
            }
            else if (Enum.IsDefined(typeof(TrayIconMenuCommand), command))
            {
                CommandRequested?.Invoke((TrayIconMenuCommand)command);
            }
        }
        finally
        {
            _ = DestroyMenu(menu);
            _ = PostMessage(_windowHandle, NullWindowMessage, IntPtr.Zero, IntPtr.Zero);
        }
    }

    // menu identifies the owned native popup being populated.
    // command identifies the action returned by TrackPopupMenuEx.
    // label contains the current localized action text.
    // enabled indicates whether the existing application surface can handle this action now.
    private static void AppendCommand(IntPtr menu, uint command, string label, bool enabled = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        var flags = MenuString | (enabled ? 0u : MenuDisabled);
        if (!AppendMenu(menu, flags, new UIntPtr(command), label))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "WorkTrail could not populate its notification-area context menu.");
        }
    }

    // menu identifies the owned native popup whose next action group is being separated.
    private static void AppendSeparator(IntPtr menu)
    {
        if (!AppendMenu(menu, MenuSeparator, UIntPtr.Zero, string.Empty))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "WorkTrail could not separate notification-area menu groups.");
        }
    }

    private NotifyIconData CreateNotificationData() => new()
    {
        Size = Marshal.SizeOf<NotifyIconData>(),
        WindowHandle = _windowHandle,
        Id = 1,
        Flags = _iconHandle == IntPtr.Zero
            ? NotificationIconMessage
            : NotificationIconMessage | NotificationIconIcon | NotificationIconTip | NotificationIconShowTip,
        CallbackMessage = TrayCallbackMessage,
        IconHandle = _iconHandle,
        ToolTip = _toolTip,
        Info = string.Empty,
        InfoTitle = string.Empty,
        Version = NotificationIconVersion4
    };

    private static IntPtr WindowSubclassProcedure(
        IntPtr windowHandle,
        uint message,
        UIntPtr wParam,
        IntPtr lParam,
        UIntPtr subclassId,
        IntPtr referenceData)
    {
        if (referenceData != IntPtr.Zero
            && GCHandle.FromIntPtr(referenceData).Target is TrayIconService service
            && !service._disposed)
        {
            try
            {
                if (message == service._taskbarCreatedMessage)
                {
                    service.OnTaskbarCreated();
                }
                else if (message == TrayCallbackMessage)
                {
                    var activationMessage = (uint)lParam.ToInt64() & 0xFFFF;
                    // Version 4 emits semantic selection/context-menu events; handling raw button-up as well toggles twice.
                    if (activationMessage is IconSelectMessage or IconKeySelectMessage)
                    {
                        service.ToggleMainWindowVisibility();
                    }
                    else if (activationMessage == ContextMenuMessage)
                    {
                        service.ShowContextMenu();
                    }
                }
            }
            catch (Exception exception)
            {
                // Native window callbacks must always return control to Windows, even if the tray state is no longer usable.
                service._logger.LogError(exception, "Notification-area icon activation failed.");
            }
        }

        return DefSubclassProc(windowHandle, message, wParam, lParam);
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct NotifyIconData
    {
        public int Size;
        public IntPtr WindowHandle;
        public uint Id;
        public uint Flags;
        public uint CallbackMessage;
        public IntPtr IconHandle;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string ToolTip;

        public uint State;
        public uint StateMask;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string Info;

        public uint TimeoutOrVersion;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string InfoTitle;

        public uint InfoFlags;
        public Guid GuidItem;
        public IntPtr BalloonIconHandle;

        public uint Version
        {
            readonly get => TimeoutOrVersion;
            set => TimeoutOrVersion = value;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate IntPtr SubclassProcDelegate(
        IntPtr windowHandle,
        uint message,
        UIntPtr wParam,
        IntPtr lParam,
        UIntPtr subclassId,
        IntPtr referenceData);

    internal delegate bool NotifyIconDelegate(uint message, ref NotifyIconData notificationData);

    [DllImport("user32.dll", EntryPoint = "RegisterWindowMessageW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint RegisterWindowMessage(string message);

    [DllImport("shell32.dll", EntryPoint = "Shell_NotifyIconW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShellNotifyIcon(uint message, ref NotifyIconData notificationData);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr LoadImage(IntPtr instance, string name, uint type, int desiredWidth, int desiredHeight, uint loadFlags);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr iconHandle);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr windowHandle);

    // windowHandle identifies the main window whose minimized state is being read.
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(IntPtr windowHandle);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(IntPtr windowHandle, int command);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr windowHandle);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr CreatePopupMenu();

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AppendMenu(IntPtr menuHandle, uint flags, UIntPtr itemId, string text);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint TrackPopupMenuEx(IntPtr menuHandle, uint flags, int x, int y, IntPtr ownerWindowHandle, IntPtr parameters);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out NativePoint point);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyMenu(IntPtr menuHandle);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessage(IntPtr windowHandle, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("comctl32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowSubclass(IntPtr windowHandle, SubclassProcDelegate procedure, UIntPtr subclassId, IntPtr referenceData);

    [DllImport("comctl32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RemoveWindowSubclass(IntPtr windowHandle, SubclassProcDelegate procedure, UIntPtr subclassId);

    [DllImport("comctl32.dll")]
    private static extern IntPtr DefSubclassProc(IntPtr windowHandle, uint message, UIntPtr wParam, IntPtr lParam);
}
