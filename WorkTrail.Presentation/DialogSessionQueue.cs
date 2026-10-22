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


namespace WorkTrail.Presentation;

/// <summary>Serializes modal presentation and cancels pending sessions when their owner closes or the app stops.</summary>
public sealed class DialogSessionQueue
{
    #region Fields

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly CancellationTokenSource _shutdown = new();

    #endregion

    #region Properties

    /// <summary>Gets whether this queue permanently rejects new dialog sessions.</summary>
    public bool IsShuttingDown => _shutdown.IsCancellationRequested;

    #endregion

    #region Implementation

    /// <summary>Acquires the modal slot, or returns null when the owner or application has closed.</summary>
    /// <param name="ownerCancellation">Cancellation tied to the lifetime of the requesting window.</param>
    /// <returns>An idempotent lease that must be disposed after presentation, or null for a cancelled session.</returns>
    public async Task<IDisposable?> EnterAsync(CancellationToken ownerCancellation = default)
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(_shutdown.Token, ownerCancellation);
        try
        {
            await _gate.WaitAsync(cancellation.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            // Closing an owner or shutting down cancels presentation, never an affirmative user decision.
            return null;
        }

        if (cancellation.IsCancellationRequested)
        {
            _gate.Release();

            return null;
        }

        return new Lease(_gate);
    }

    /// <summary>Rejects new sessions and immediately releases all pending waiters without interrupting lease cleanup.</summary>
    public void Shutdown() => _shutdown.Cancel();

    #endregion

    #region Lease implementation

    /// <summary>Returns the modal slot once, even when several cleanup paths dispose the same lease.</summary>
    private sealed class Lease(SemaphoreSlim gate) : IDisposable
    {
        #region Fields

        private SemaphoreSlim? _gate = gate;

        #endregion

        #region Implementation

        /// <summary>
        /// Releases the modal slot exactly once.
        /// </summary>
        public void Dispose() => Interlocked.Exchange(ref _gate, null)?.Release();

        #endregion
    }

    #endregion
}
