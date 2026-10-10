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
using System.Runtime.InteropServices;
using WorkTrail.Application;

namespace WorkTrail.Services;

/// <summary>Reveals live UI peers without creating windows or transferring foreground focus.</summary>
internal sealed class OpenWindowVisibilityService(IOpenWindowVisibilityInterop? interop = null)
{
    private readonly IOpenWindowVisibilityInterop _interop = interop ?? new NativeOpenWindowVisibilityInterop();

    internal int Reveal(WindowRevealRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentOutOfRangeException.ThrowIfEqual(request.MainWindowHandle, 0);
        ArgumentNullException.ThrowIfNull(request.OpenWindowHandles);
        var handles = request.OpenWindowHandles.ToArray();
        if (handles.Any(handle => handle == 0 || handle == request.MainWindowHandle)
            || handles.Distinct().Count() != handles.Length)
        {
            throw new ArgumentException("Open peer window handles must be nonzero, unique, and exclude the main window.", nameof(request));
        }

        cancellationToken.ThrowIfCancellationRequested();
        var main = _interop.Read(request.MainWindowHandle);
        if (main is null || !CanReveal(request.MainWindowHandle, main.Identity))
        {
            // Activation can end while IPC is in flight. A disabled main also indicates an active modal dialog.
            return 0;
        }

        foreach (var handle in handles)
        {
            if (_interop.Read(handle) is { } peer) ValidatePeer(main, peer);
        }

        var revealed = 0;
        var insertAfter = main.Topmost ? 0 : request.MainWindowHandle;
        foreach (var handle in handles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!CanReveal(request.MainWindowHandle, main.Identity)) break;
            var peer = _interop.Read(handle);
            if (peer is null || !peer.Enabled)
            {
                // A peer may have closed since the UI snapshot. Never recreate it or enable a modal-disabled window.
                continue;
            }

            ValidatePeer(main, peer);
            try
            {
                if (!peer.Visible || peer.Minimized)
                {
                    if (!CanReveal(request.MainWindowHandle, main.Identity)) break;
                    _interop.ShowWithoutActivation(handle, peer.Minimized);
                }

                if (!CanReveal(request.MainWindowHandle, main.Identity)) break;
                // Existing always-on-top choices keep their own band; ordinary peers stay below the main window.
                _interop.PlaceWithoutActivation(handle, insertAfter, preserveZOrder: peer.Topmost);
                if (!peer.Topmost) insertAfter = handle;
                revealed++;
            }
            catch (Win32Exception) when (_interop.Read(handle) is null)
            {
                // Closing between the last check and USER32 is expected. Other interop failures propagate.
            }
        }

        return revealed;
    }

    private bool CanReveal(long mainHandle, NativeWindowIdentity expectedIdentity) =>
        _interop.ForegroundWindow == mainHandle
        && _interop.Read(mainHandle) is { Enabled: true, Visible: true, Minimized: false } main
        && main.Identity == expectedIdentity;

    private static void ValidatePeer(OpenNativeWindowState main, OpenNativeWindowState peer)
    {
        // The runtime may be in another process: compare peers to the supplied UI main, not to this runtime's PID.
        if (peer.Identity != main.Identity)
        {
            throw new ArgumentException("Peer windows must belong to the main window's process and UI thread.");
        }
    }
}

internal sealed record NativeWindowIdentity(uint ProcessId, uint ThreadId);
internal sealed record OpenNativeWindowState(NativeWindowIdentity Identity, bool Enabled, bool Visible, bool Minimized, bool Topmost = false);

internal interface IOpenWindowVisibilityInterop
{
    long ForegroundWindow { get; }
    /// <summary>Reads a live native window or returns null when it has already closed.</summary>
    OpenNativeWindowState? Read(long handle);
    /// <summary>Shows a hidden window or restores a minimized window without activating it.</summary>
    void ShowWithoutActivation(long handle, bool minimized);
    /// <summary>Raises a peer without activation, movement, resizing, or changing an existing topmost preference.</summary>
    void PlaceWithoutActivation(long handle, long insertAfter, bool preserveZOrder);
}

internal sealed class NativeOpenWindowVisibilityInterop : IOpenWindowVisibilityInterop
{
    private const int GwlExStyle = -20;
    private const long WsExTopmost = 0x00000008;
    private const int SwShowNoActivate = 4;
    private const int SwShowNa = 8;
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpShowWindow = 0x0040;
    private const uint SwpNoOwnerZOrder = 0x0200;
    private const uint SwpAsyncWindowPos = 0x4000;

    public long ForegroundWindow => GetForegroundWindow().ToInt64();

    /// <inheritdoc />
    public OpenNativeWindowState? Read(long handle)
    {
        var native = new IntPtr(handle);
        if (!IsWindow(native)) return null;
        var thread = GetWindowThreadProcessId(native, out var process);
        if (thread == 0)
        {
            var identityError = Marshal.GetLastPInvokeError();
            // A UI close may race any native read after IsWindow. Only a still-live handle represents an interop failure.
            if (!IsWindow(native)) return null;
            throw new Win32Exception(identityError, "Unable to read the native window identity.");
        }
        Marshal.SetLastPInvokeError(0);
        var extendedStyle = GetWindowLongPtr(native, GwlExStyle).ToInt64();
        var error = Marshal.GetLastPInvokeError();
        if (extendedStyle == 0 && error != 0)
        {
            if (!IsWindow(native)) return null;
            throw new Win32Exception(error, "Unable to read the native window style.");
        }
        return new(new(process, thread), IsWindowEnabled(native), IsWindowVisible(native), IsIconic(native), (extendedStyle & WsExTopmost) != 0);
    }

    /// <inheritdoc />
    public void ShowWithoutActivation(long handle, bool minimized)
    {
        // SW_RESTORE activates the target. SW_SHOWNOACTIVATE restores its saved normal bounds instead;
        // a previously maximized minimized window therefore returns to its normal, non-maximized placement.
        if (!ShowWindowAsync(new IntPtr(handle), minimized ? SwShowNoActivate : SwShowNa))
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "Unable to request native window visibility.");
        }
    }

    /// <inheritdoc />
    public void PlaceWithoutActivation(long handle, long insertAfter, bool preserveZOrder)
    {
        // Asynchronous positioning avoids blocking the UI thread when this facade is served through IPC.
        var flags = SwpNoMove | SwpNoSize | SwpNoActivate | SwpShowWindow | SwpNoOwnerZOrder | SwpAsyncWindowPos;
        if (preserveZOrder) flags |= SwpNoZOrder;
        if (!SetWindowPos(new IntPtr(handle), new IntPtr(insertAfter), 0, 0, 0, 0, flags))
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "Unable to reveal the native window without activation.");
        }
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr GetWindowLongPtr(IntPtr window, int index);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(IntPtr window);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowEnabled(IntPtr window);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(IntPtr window);
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindowAsync(IntPtr window, int command);
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr window, IntPtr insertAfter, int x, int y, int width, int height, uint flags);
}
