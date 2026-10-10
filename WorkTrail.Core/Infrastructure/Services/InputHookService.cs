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
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Threading;

namespace WorkTrail.Services;

/// <summary>
/// Tracks keyboard and mouse interactions through low-level system hooks.
/// </summary>
public sealed class InputHookService : IDisposable
{
    private readonly NativeMethods.HookProc _keyboardCallback;
    private readonly NativeMethods.HookProc _mouseCallback;
    private IntPtr _keyboardHook;
    private IntPtr _mouseHook;
    private long _keyPresses;
    private long _mouseClicks;

    /// <summary>
    /// Creates hook callbacks without starting hooks.
    /// </summary>
    public InputHookService()
    {
        _keyboardCallback = KeyboardHook;
        _mouseCallback = MouseHook;
    }

    /// <summary>
    /// Starts keyboard and mouse hooks if not already running.
    /// </summary>
    public void Start()
    {
        if (_keyboardHook != IntPtr.Zero)
        {
            return;
        }

        // Install low-level hooks once; callbacks are lightweight and shared across captures.
        _keyboardHook = NativeMethods.SetWindowsHookEx(NativeMethods.WhKeyboardLl, _keyboardCallback, IntPtr.Zero, 0);
        if (_keyboardHook == IntPtr.Zero)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "WorkTrail could not install the keyboard activity hook.");
        }

        _mouseHook = NativeMethods.SetWindowsHookEx(NativeMethods.WhMouseLl, _mouseCallback, IntPtr.Zero, 0);
        if (_mouseHook == IntPtr.Zero)
        {
            var error = Marshal.GetLastWin32Error();
            Stop();
            throw new Win32Exception(error, "WorkTrail could not install the mouse activity hook.");
        }
    }

    /// <summary>
    /// Stops and releases both hooks.
    /// </summary>
    public void Stop()
    {
        if (_keyboardHook != IntPtr.Zero)
        {
            NativeMethods.UnhookWindowsHookEx(_keyboardHook);
            _keyboardHook = IntPtr.Zero;
        }

        if (_mouseHook != IntPtr.Zero)
        {
            NativeMethods.UnhookWindowsHookEx(_mouseHook);
            _mouseHook = IntPtr.Zero;
        }
    }

    /// <summary>
    /// Reads and resets counted keys and clicks atomically.
    /// </summary>
    /// <returns>Tuple with accumulated key and click counts since last read.</returns>
    public (long Keys, long Clicks) TakeCounts() =>
        (Interlocked.Exchange(ref _keyPresses, 0), Interlocked.Exchange(ref _mouseClicks, 0));

    /// <summary>
    /// Keyboard hook callback: increments on key down messages.
    /// </summary>
    private IntPtr KeyboardHook(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code >= 0 && (wParam.ToInt32() == NativeMethods.WmKeyDown || wParam.ToInt32() == NativeMethods.WmSysKeyDown))
        {
            Interlocked.Increment(ref _keyPresses);
        }

        return NativeMethods.CallNextHookEx(_keyboardHook, code, wParam, lParam);
    }

    /// <summary>
    /// Mouse hook callback: increments on left/right/middle button down events.
    /// </summary>
    private IntPtr MouseHook(int code, IntPtr wParam, IntPtr lParam)
    {
        var message = wParam.ToInt32();
        if (code >= 0 && (message == NativeMethods.WmLButtonDown || message == NativeMethods.WmRButtonDown || message == NativeMethods.WmMButtonDown))
        {
            Interlocked.Increment(ref _mouseClicks);
        }

        return NativeMethods.CallNextHookEx(_mouseHook, code, wParam, lParam);
    }

    /// <summary>
    /// Stops active hooks to avoid leaks.
    /// </summary>
    public void Dispose() => Stop();
}
