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


namespace WorkTrail;

/// <summary>Tracks one window's loaded gate, initialization task, and cancellation lifetime.</summary>
internal sealed class WindowSurfaceLifecycle : IDisposable
{
    private readonly CancellationTokenSource _cancellation = new();
    private readonly TaskCompletionSource _loaded = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private Task? _initializationTask;
    private bool _disposed;

    /// <summary>Gets the token cancelled when the owning window closes.</summary>
    internal CancellationToken Token => _cancellation.Token;

    /// <summary>Gets whether the owning window has started closing.</summary>
    internal bool IsCancellationRequested => _cancellation.IsCancellationRequested;

    /// <summary>Occurs when tracked initialization fails for a reason other than window cancellation.</summary>
    internal event Action<Exception>? InitializationFailed;

    /// <summary>Starts and observes the single initialization operation owned by the window.</summary>
    internal void StartInitialization(Func<CancellationToken, Task> initializeAsync)
    {
        ArgumentNullException.ThrowIfNull(initializeAsync);
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_initializationTask is not null)
        {
            throw new InvalidOperationException("Window initialization has already started.");
        }

        _initializationTask = ObserveInitializationAsync(initializeAsync(Token));
    }

    /// <summary>Completes the loaded gate after placement and first layout are ready.</summary>
    internal void SignalLoaded() => _loaded.TrySetResult();

    /// <summary>Waits until the window has completed its first loaded callback.</summary>
    internal Task WaitUntilLoadedAsync(CancellationToken cancellationToken) =>
        _loaded.Task.WaitAsync(cancellationToken);

    /// <summary>Cancels work owned by the window without blocking its native close callback.</summary>
    internal void Cancel()
    {
        if (!_cancellation.IsCancellationRequested)
        {
            _cancellation.Cancel();
        }
    }

    private async Task ObserveInitializationAsync(Task initializationTask)
    {
        try
        {
            await initializationTask;
        }
        catch (OperationCanceledException) when (_cancellation.IsCancellationRequested)
        {
            // Window shutdown is the expected cancellation path for initialization and interop calls.
        }
        catch (Exception exception)
        {
            // Surface initialization failures are observed and handed back to the UI for visible reporting.
            InitializationFailed?.Invoke(exception);
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        Cancel();
        _loaded.TrySetCanceled(Token);
        InitializationFailed = null;
        _cancellation.Dispose();
        _disposed = true;
    }
}
