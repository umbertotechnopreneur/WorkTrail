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


using System.Collections.Concurrent;
using System.IO.Pipes;
using Microsoft.Extensions.Logging;
using WorkTrail.Application;

namespace WorkTrail.Runtime;

/// <summary>Accepts same-user runtime connections and isolates the lifetime of each request.</summary>
internal sealed class RuntimePipeServer
{
    private readonly RuntimeEndpoint _endpoint;
    private readonly RuntimeRequestDispatcher _dispatcher;
    private readonly ILogger _logger;
    private readonly Action<AtomicResetPlan> _atomicResetPrepared;
    private readonly ConcurrentDictionary<int, Task> _activeRequests = new();
    private readonly SemaphoreSlim _connectionSlots;
    private readonly TimeSpan _frameTimeout;
    private int _requestSequence;

    internal RuntimePipeServer(
        RuntimeEndpoint endpoint,
        RuntimeRequestDispatcher dispatcher,
        ILogger logger,
        Action<AtomicResetPlan> atomicResetPrepared,
        int maximumConnections = 4,
        TimeSpan? frameTimeout = null)
    {
        _endpoint = endpoint;
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _atomicResetPrepared = atomicResetPrepared ?? throw new ArgumentNullException(nameof(atomicResetPrepared));
        if (maximumConnections is < 1 or > 4) throw new ArgumentOutOfRangeException(nameof(maximumConnections));
        _frameTimeout = frameTimeout ?? TimeSpan.FromSeconds(5);
        if (_frameTimeout <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(frameTimeout));
        // Four slots bound simultaneous 16 MiB input buffers to 64 MiB, including incomplete frames.
        _connectionSlots = new SemaphoreSlim(maximumConnections, maximumConnections);
    }

    /// <summary>Serves connections until host shutdown cancels the accept loop.</summary>
    internal async Task ServeAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            await _connectionSlots.WaitAsync(cancellationToken);
            NamedPipeServerStream? pipe = null;
            var handedOff = false;
            try
            {
                // CurrentUserOnly is the OS boundary that prevents another Windows user from calling the local runtime.
                pipe = new NamedPipeServerStream(
                    _endpoint.PipeName,
                    PipeDirection.InOut,
                    NamedPipeServerStream.MaxAllowedServerInstances,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await pipe.WaitForConnectionAsync(cancellationToken);
                TrackRequest(HandleBoundedConnectionAsync(pipe, cancellationToken));
                handedOff = true;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                // A transient accept failure is isolated; the next loop iteration remains the documented fallback.
                _logger.LogWarning("Runtime pipe acceptance failed; continuing to serve requests. ExceptionType={ExceptionType}", exception.GetType().Name);
                await Task.Delay(TimeSpan.FromMilliseconds(250), cancellationToken);
            }
            finally
            {
                if (!handedOff)
                {
                    if (pipe is not null) await pipe.DisposeAsync();
                    _connectionSlots.Release();
                }
            }
        }
    }

    /// <summary>Waits for the requests accepted before shutdown to finish.</summary>
    internal async Task DrainRequestsAsync()
    {
        var activeRequests = _activeRequests.Values.ToArray();
        if (activeRequests.Length > 0)
        {
            await Task.WhenAll(activeRequests);
        }
    }

    private void TrackRequest(Task requestTask)
    {
        var requestId = Interlocked.Increment(ref _requestSequence);
        _activeRequests[requestId] = requestTask;
        _ = requestTask.ContinueWith(
            completedTask =>
            {
                _activeRequests.TryRemove(requestId, out _);
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private async Task HandleConnectionAsync(NamedPipeServerStream pipe, CancellationToken shutdownToken)
    {
        await using (pipe)
        {
            try
            {
                using var readDeadline = CancellationTokenSource.CreateLinkedTokenSource(shutdownToken);
                readDeadline.CancelAfter(_frameTimeout);
                var request = await RuntimeProtocol.ReadAsync<RuntimeRequestEnvelope>(pipe, readDeadline.Token);
                using var requestCancellation = CancellationTokenSource.CreateLinkedTokenSource(shutdownToken);
                var disconnectMonitor = MonitorDisconnectAsync(pipe, requestCancellation);
                RuntimeResponseEnvelope response;
                try
                {
                    response = await _dispatcher.DispatchAsync(request, requestCancellation.Token);
                }
                finally
                {
                    requestCancellation.Cancel();
                    await disconnectMonitor;
                }

                using var writeDeadline = CancellationTokenSource.CreateLinkedTokenSource(shutdownToken);
                writeDeadline.CancelAfter(_frameTimeout);
                await RuntimeProtocol.WriteAsync(pipe, response, writeDeadline.Token);
                if (request.Operation == RuntimeOperationCatalog.GetWireName(RuntimeOperation.AppAtomicResetV1)
                    && response.Succeeded
                    && response.Payload is AtomicResetPlan resetPlan)
                {
                    // The runtime owner begins shutdown only after the destructive-operation result reaches the caller.
                    _atomicResetPrepared(resetPlan);
                }
            }
            catch (OperationCanceledException) when (shutdownToken.IsCancellationRequested)
            {
                // Host shutdown cancels every active request before releasing runtime ownership.
            }
            catch (OperationCanceledException)
            {
                // Client disconnect/timeout cancellation stops long-running reads without affecting the host.
            }
            catch (Exception exception)
            {
                // Invalid/disconnected clients are isolated so the long-lived local runtime remains available.
                _logger.LogWarning("Runtime pipe request failed; continuing to serve requests. ExceptionType={ExceptionType}", exception.GetType().Name);
            }
        }
    }

    private async Task HandleBoundedConnectionAsync(NamedPipeServerStream pipe, CancellationToken shutdownToken)
    {
        try { await HandleConnectionAsync(pipe, shutdownToken); }
        finally { _connectionSlots.Release(); }
    }

    private static async Task MonitorDisconnectAsync(
        NamedPipeServerStream pipe,
        CancellationTokenSource requestCancellation)
    {
        var probe = new byte[1];
        try
        {
            _ = await pipe.ReadAsync(probe, requestCancellation.Token);
            requestCancellation.Cancel();
        }
        catch (OperationCanceledException) when (requestCancellation.IsCancellationRequested)
        {
            // Normal completion cancels the pending disconnect read before the response is written.
        }
        catch (IOException)
        {
            // A disconnected client cancels its operation; the server continues accepting other requests.
            requestCancellation.Cancel();
        }
    }
}
