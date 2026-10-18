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
using System.Threading.Tasks;

namespace WorkTrail.Services;

/// <summary>
/// Tracks keyboard and mouse interactions through low-level system hooks.
/// </summary>
public sealed class InputHookService : IDisposable
{
    private readonly NativeMethods.HookProc _keyboardCallback;
    private readonly NativeMethods.HookProc _mouseCallback;
    private readonly object _lifecycleGate = new();
    private static readonly TimeSpan LifecycleTimeout = TimeSpan.FromSeconds(5);
    private Thread? _thread;
    private uint _threadId;
    private int _stopRequested;
    private Exception? _workerFailure;
    private bool _disposed;
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
        lock (_lifecycleGate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_thread?.IsAlive == true)
            {
                ThrowIfWorkerFailed();
                return;
            }

            Volatile.Write(ref _workerFailure, null);
            Volatile.Write(ref _stopRequested, 0);
            Volatile.Write(ref _threadId, 0);
            var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            // WH_KEYBOARD_LL/WH_MOUSE_LL deliver callbacks through the installing thread's message loop.
            // Runtime commands may arrive on pool threads; those threads must never own these hooks.
            _thread = new Thread(() => RunMessageLoop(ready)) { IsBackground = true, Name = "WorkTrail input hooks" };
            try
            {
                _thread.Start();
                ready.Task.WaitAsync(LifecycleTimeout).GetAwaiter().GetResult();
            }
            catch (Exception startupFailure)
            {
                if ((_thread.ThreadState & ThreadState.Unstarted) != 0)
                {
                    _thread = null;
                    throw;
                }
                try { StopWorker(); }
                catch (Exception stopFailure) { throw new AggregateException(startupFailure, stopFailure); }
                throw;
            }
        }
    }

    private void RunMessageLoop(TaskCompletionSource ready)
    {
        Exception? failure = null;
        try
        {
            // Force queue creation before publishing the thread ID used by StopWorker.
            _ = NativeMethods.PeekMessage(out _, IntPtr.Zero, 0, 0, 0);
            Volatile.Write(ref _threadId, NativeMethods.GetCurrentThreadId());
            if (Volatile.Read(ref _stopRequested) != 0) return;
            _keyboardHook = NativeMethods.SetWindowsHookEx(NativeMethods.WhKeyboardLl, _keyboardCallback, IntPtr.Zero, 0);
            if (_keyboardHook == IntPtr.Zero)
                throw new Win32Exception(Marshal.GetLastWin32Error(), "WorkTrail could not install the keyboard activity hook.");
            _mouseHook = NativeMethods.SetWindowsHookEx(NativeMethods.WhMouseLl, _mouseCallback, IntPtr.Zero, 0);
            if (_mouseHook == IntPtr.Zero)
                throw new Win32Exception(Marshal.GetLastWin32Error(), "WorkTrail could not install the mouse activity hook.");
            ready.TrySetResult();
            while (Volatile.Read(ref _stopRequested) == 0)
            {
                var result = NativeMethods.GetMessage(out var message, IntPtr.Zero, 0, 0);
                if (result == -1) throw new Win32Exception(Marshal.GetLastWin32Error(), "The input activity message loop failed.");
                if (result == 0)
                {
                    if (Volatile.Read(ref _stopRequested) == 0)
                        throw new InvalidOperationException("The input activity message loop stopped unexpectedly.");
                    break;
                }
                _ = NativeMethods.TranslateMessage(ref message);
                _ = NativeMethods.DispatchMessage(ref message);
            }
        }
        catch (Exception exception)
        {
            failure = exception;
            ready.TrySetException(exception);
        }
        finally
        {
            failure = ReleaseHook(ref _mouseHook, failure);
            failure = ReleaseHook(ref _keyboardHook, failure);
            if (failure is not null)
            {
                Volatile.Write(ref _workerFailure, failure);
                if (ready.Task.IsCompletedSuccessfully)
                    ApplicationErrorService.Report(failure, "InputHooks.MessageLoop", deferDialog: true);
                else ready.TrySetException(failure);
            }
            ready.TrySetCanceled();
        }
    }

    private static Exception? ReleaseHook(ref IntPtr hook, Exception? failure)
    {
        if (hook == IntPtr.Zero) return failure;
        var handle = hook;
        hook = IntPtr.Zero;
        if (!NativeMethods.UnhookWindowsHookEx(handle))
            return failure ?? new Win32Exception(Marshal.GetLastWin32Error(), "WorkTrail could not release an input activity hook.");
        return failure;
    }

    /// <summary>
    /// Stops and releases both hooks.
    /// </summary>
    public void Stop()
    {
        lock (_lifecycleGate) StopWorker();
    }

    private void StopWorker()
    {
        var thread = _thread;
        if (thread is null) return;
        Volatile.Write(ref _stopRequested, 1);
        var threadId = Volatile.Read(ref _threadId);
        // WM_QUIT wakes GetMessage; unhooking happens on the same thread that installed both hooks.
        if (thread.IsAlive && threadId != 0 && !NativeMethods.PostThreadMessage(threadId, 0x0012, UIntPtr.Zero, IntPtr.Zero))
        {
            var error = Marshal.GetLastWin32Error();
            if (thread.IsAlive) throw new Win32Exception(error, "WorkTrail could not stop its input activity message loop.");
        }
        if (!thread.Join(LifecycleTimeout)) throw new TimeoutException("The input activity thread did not stop within five seconds.");
        _thread = null;
        Volatile.Write(ref _threadId, 0);
    }

    /// <summary>
    /// Reads and resets counted keys and clicks atomically.
    /// </summary>
    /// <returns>Tuple with accumulated key and click counts since last read.</returns>
    public (long Keys, long Clicks) TakeCounts()
    {
        ThrowIfWorkerFailed();
        return (Interlocked.Exchange(ref _keyPresses, 0), Interlocked.Exchange(ref _mouseClicks, 0));
    }

    private void ThrowIfWorkerFailed()
    {
        if (Volatile.Read(ref _workerFailure) is { } failure)
            throw new InvalidOperationException("Input activity collection is unavailable.", failure);
    }

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
    public void Dispose()
    {
        lock (_lifecycleGate)
        {
            if (_disposed) return;
            StopWorker();
            _disposed = true;
        }
    }
}
