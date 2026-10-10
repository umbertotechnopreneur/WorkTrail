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


namespace WorkTrail.Runtime;

/// <summary>
/// Keeps named-mutex acquisition and release on one dedicated thread because Windows mutex ownership is thread-affine.
/// </summary>
internal sealed class RuntimeMutexLease : IDisposable
{
    private readonly ManualResetEventSlim _ready = new(false);
    private readonly ManualResetEventSlim _release = new(false);
    private readonly Thread _ownerThread;
    private Exception? _failure;
    private bool _disposed;

    internal RuntimeMutexLease(string mutexName)
    {
        _ownerThread = new Thread(() => Own(mutexName))
        {
            IsBackground = true,
            Name = "WorkTrail runtime mutex"
        };
        _ownerThread.Start();
        _ready.Wait();
        if (_failure is not null)
        {
            Dispose();
            throw new InvalidOperationException("Unable to acquire the WorkTrail runtime mutex.", _failure);
        }
    }

    internal bool Acquired { get; private set; }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _release.Set();
        _ownerThread.Join();
        _release.Dispose();
        _ready.Dispose();
    }

    private void Own(string mutexName)
    {
        try
        {
            // Windows mutex ownership is bound to this thread; release must run here as well.
            using var mutex = new Mutex(false, mutexName);
            try
            {
                Acquired = mutex.WaitOne(0);
            }
            catch (AbandonedMutexException)
            {
                Acquired = true;
            }

            _ready.Set();
            if (!Acquired)
            {
                return;
            }

            _release.Wait();
            mutex.ReleaseMutex();
        }
        catch (Exception exception)
        {
            // Acquisition failures are surfaced synchronously by the constructor; there is no ownership fallback.
            _failure = exception;
            _ready.Set();
        }
    }
}
