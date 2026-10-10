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

/// <summary>Reports pointer presence across a native window and its content islands.</summary>
public static class WindowPointerService
{
    private const uint RootAncestor = 2;

    /// <summary>Reads hover and mouse contact without installing global input hooks or changing focus.</summary>
    public static WindowPointerState Read(IntPtr windowHandle)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(windowHandle, IntPtr.Zero);
        if (!GetCursorPos(out var point))
        {
            var error = Marshal.GetLastPInvokeError();
            if (error == 5)
            {
                // Windows temporarily denies desktop input while locked or showing a secure UAC prompt.
                // Report unavailable input so presentation retains its current chrome until input returns.
                return new WindowPointerState(IsAvailable: false, IsInside: false, IsPressed: false);
            }

            // Unexpected interop failures are not interpreted as a pointer exit.
            throw new Win32Exception(error, "Unable to read the window pointer position.");
        }

        // WindowFromPoint accounts for overlapping apps and child HWNDs such as embedded WebView2 content.
        // Owned top-level windows remain independent: hovering About must not reveal the main player.
        var target = GetAncestor(WindowFromPoint(point), RootAncestor);
        var inside = target == windowHandle;

        var pressed = (GetAsyncKeyState(0x01) & 0x8000) != 0
            || (GetAsyncKeyState(0x02) & 0x8000) != 0
            || (GetAsyncKeyState(0x04) & 0x8000) != 0;
        return new WindowPointerState(IsAvailable: true, inside, pressed);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out NativePoint point);

    [DllImport("user32.dll")]
    private static extern IntPtr WindowFromPoint(NativePoint point);

    [DllImport("user32.dll")]
    private static extern IntPtr GetAncestor(IntPtr windowHandle, uint flags);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int virtualKey);
}

/// <summary>Contains the read-only native pointer state needed by adaptive window chrome.</summary>
public sealed record WindowPointerState(bool IsAvailable, bool IsInside, bool IsPressed);
