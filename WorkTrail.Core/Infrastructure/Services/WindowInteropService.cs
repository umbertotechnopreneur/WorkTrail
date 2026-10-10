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

namespace WorkTrail.Services;

/// <summary>Owns the native window operations shared by WinUI presentation surfaces.</summary>
public static class WindowInteropService
{
    private const int GwlHwndParent = -8;
    private const int DwmWindowAttributeCornerPreference = 33;
    private const int DwmWindowAttributeBorderColor = 34;
    private const uint DwmWindowCornerPreferenceRound = 2;
    private const uint DwmColorNone = 0xFFFFFFFE;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoActivate = 0x0010;
    private static readonly IntPtr HwndTopMost = new(-1);

    /// <summary>Gets the current native window DPI as a scale relative to 96 DPI.</summary>
    public static double GetRasterizationScale(IntPtr windowHandle)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(windowHandle, IntPtr.Zero);

        // Read the native DPI while XAML may still be processing the monitor transition.
        var dpi = GetDpiForWindow(windowHandle);
        if (dpi == 0)
        {
            // An invalid window cannot supply usable DPI; fail instead of retaining a stale scale.
            throw new InvalidOperationException("Unable to read the native window DPI.");
        }

        return dpi / 96d;
    }

    /// <summary>Applies the optional native chrome used by the compact player window.</summary>
    public static void ApplyPlayerWindowChrome(IntPtr windowHandle)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(windowHandle, IntPtr.Zero);

        var borderColor = DwmColorNone;
        _ = DwmSetWindowAttribute(
            windowHandle,
            DwmWindowAttributeBorderColor,
            ref borderColor,
            Marshal.SizeOf<uint>());

        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
        {
            // Rounded-corner preference is a Windows 11 visual enhancement; older supported
            // systems intentionally retain their native default window shape.
            return;
        }

        var cornerPreference = DwmWindowCornerPreferenceRound;
        _ = DwmSetWindowAttribute(
            windowHandle,
            DwmWindowAttributeCornerPreference,
            ref cornerPreference,
            Marshal.SizeOf<uint>());
    }

    /// <summary>Assigns one native window as the owner of another window.</summary>
    public static void SetOwner(IntPtr windowHandle, IntPtr ownerHandle)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(windowHandle, IntPtr.Zero);
        ArgumentOutOfRangeException.ThrowIfEqual(ownerHandle, IntPtr.Zero);

        // SetWindowLongPtr can legitimately return zero, so only a nonzero native error means failure.
        Marshal.SetLastPInvokeError(0);
        var previousOwner = IntPtr.Size == 8
            ? SetWindowLongPtr64(windowHandle, GwlHwndParent, ownerHandle)
            : new IntPtr(SetWindowLongPtr32(windowHandle, GwlHwndParent, ownerHandle.ToInt32()));
        var error = Marshal.GetLastPInvokeError();
        if (previousOwner == IntPtr.Zero && error != 0)
        {
            throw new Win32Exception(error, "Unable to assign the native owner window.");
        }
    }

    /// <summary>Places a window in the topmost band without moving, resizing, or activating it.</summary>
    public static void MakeTopmostWithoutActivation(IntPtr windowHandle)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(windowHandle, IntPtr.Zero);
        if (!SetWindowPos(windowHandle, HwndTopMost, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoActivate))
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "Unable to place the dialog in the topmost window band.");
        }
    }

    /// <summary>Disables the other enabled native windows owned by the current UI thread.</summary>
    public static IReadOnlyList<IntPtr> DisableCurrentThreadPeerWindows(IntPtr dialogHandle)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(dialogHandle, IntPtr.Zero);
        var disabled = new List<IntPtr>();
        Marshal.SetLastPInvokeError(0);
        var enumerated = EnumThreadWindows(GetCurrentThreadId(), (windowHandle, parameter) =>
        {
            if (windowHandle == dialogHandle || !IsWindowEnabled(windowHandle))
            {
                return true;
            }

            _ = EnableWindow(windowHandle, false);
            disabled.Add(windowHandle);
            return true;
        }, IntPtr.Zero);

        var error = Marshal.GetLastPInvokeError();
        if (!enumerated && error != 0)
        {
            // Roll back the partial modal state before exposing the interop failure to the caller.
            RestoreWindows(disabled);
            throw new Win32Exception(error, "Unable to enumerate peer windows for the modal dialog.");
        }

        return disabled;
    }

    /// <summary>Re-enables native windows previously disabled for a modal dialog.</summary>
    public static void RestoreWindows(IEnumerable<IntPtr> windowHandles)
    {
        ArgumentNullException.ThrowIfNull(windowHandles);
        foreach (var windowHandle in windowHandles)
        {
            if (windowHandle != IntPtr.Zero && IsWindow(windowHandle))
            {
                _ = EnableWindow(windowHandle, true);
            }
        }
    }

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr windowHandle);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongW", SetLastError = true)]
    private static extern int SetWindowLongPtr32(IntPtr windowHandle, int index, int newValue);

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(
        IntPtr windowHandle,
        int attribute,
        ref uint attributeValue,
        int attributeSize);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr SetWindowLongPtr64(IntPtr windowHandle, int index, IntPtr newValue);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr windowHandle, IntPtr insertAfter, int x, int y, int width, int height, uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnableWindow(IntPtr windowHandle, bool enable);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumThreadWindows(uint threadId, EnumThreadDelegate callback, IntPtr parameter);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(IntPtr windowHandle);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowEnabled(IntPtr windowHandle);

    private delegate bool EnumThreadDelegate(IntPtr windowHandle, IntPtr parameter);
}
