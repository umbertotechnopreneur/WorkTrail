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


namespace WorkTrail.Application;

/// <summary>Coordinates one shared dashboard acquisition stream for all subscribed presentation surfaces.</summary>
public sealed class DashboardRefreshCoordinator : IDisposable
{
    private static readonly TimeSpan DefaultPollInterval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan MaximumFailureDelay = TimeSpan.FromSeconds(30);
    private readonly IWorkTrailApplication _application;
    private readonly TimeSpan _pollInterval;
    private readonly object _gate = new();
    private readonly List<Subscription> _subscriptions = [];
    private readonly SemaphoreSlim _refreshSignal = new(0, 1);
    private CancellationTokenSource? _runCancellation;
    private long _runGeneration;
    private int _signalPending;
    private bool _disposed;

    /// <summary>Initializes a dashboard coordinator for the supplied application facade.</summary>
    public DashboardRefreshCoordinator(IWorkTrailApplication application, TimeSpan? pollInterval = null)
    {
        _application = application ?? throw new ArgumentNullException(nameof(application));
        _pollInterval = pollInterval is { } configuredInterval && configuredInterval > TimeSpan.Zero
            ? configuredInterval
            : DefaultPollInterval;
        _application.RuntimeStateChanged += Application_RuntimeStateChanged;
    }

    /// <summary>Subscribes one presentation surface and starts the stream when it is the first subscriber.</summary>
    public IDisposable Subscribe(Action<DashboardState> subscriber)
    {
        ArgumentNullException.ThrowIfNull(subscriber);
        var subscription = new Subscription(this, subscriber);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _subscriptions.Add(subscription);
            if (_runCancellation is null)
            {
                _runCancellation = new CancellationTokenSource();
                _runGeneration++;
                var generation = _runGeneration;
                _ = Task.Run(() => RunAsync(generation, _runCancellation.Token));
            }
        }

        SignalRefresh();
        return subscription;
    }

    /// <summary>Requests one immediate refresh; concurrent requests are coalesced.</summary>
    public void RequestRefresh()
    {
        lock (_gate)
        {
            if (_disposed || _subscriptions.Count == 0)
            {
                return;
            }
        }

        SignalRefresh();
    }

    /// <summary>Stops the stream and detaches all presentation callbacks.</summary>
    public void Dispose()
    {
        CancellationTokenSource? cancellation;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _runGeneration++;
            cancellation = _runCancellation;
            _runCancellation = null;
            foreach (var subscription in _subscriptions)
            {
                subscription.Deactivate();
            }

            _subscriptions.Clear();
        }

        _application.RuntimeStateChanged -= Application_RuntimeStateChanged;
        cancellation?.Cancel();
    }

    private async Task RunAsync(long generation, CancellationToken cancellationToken)
    {
        var delay = TimeSpan.Zero;
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                if (delay > TimeSpan.Zero)
                {
                    await WaitForRefreshOrDelayAsync(delay, cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    DrainRefreshSignal();
                }

                if (!IsGenerationActive(generation))
                {
                    return;
                }

                try
                {
                    var result = await _application.GetDashboardAsync(cancellationToken).ConfigureAwait(false);
                    if (!result.Succeeded || result.Value is null)
                    {
                        delay = NextFailureDelay(delay);
                        continue;
                    }

                    delay = _pollInterval;
                    NotifySubscribers(result.Value, generation, cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    return;
                }
                catch
                {
                    // A failed read keeps the last rendered state and backs off before retrying.
                    delay = NextFailureDelay(delay);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Removing the final subscriber cancels the loop without surfacing a late UI failure.
        }
    }

    private async Task WaitForRefreshOrDelayAsync(TimeSpan delay, CancellationToken cancellationToken)
    {
        using var winnerCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var delayTask = Task.Delay(delay, winnerCancellation.Token);
        var signalTask = _refreshSignal.WaitAsync(winnerCancellation.Token);
        var completed = await Task.WhenAny(delayTask, signalTask).ConfigureAwait(false);
        var signalObserved = ReferenceEquals(completed, signalTask) || signalTask.IsCompletedSuccessfully;

        // Cancel and observe the losing wait so each polling iteration owns exactly one
        // semaphore waiter; otherwise timer wins would accumulate abandoned waiters.
        winnerCancellation.Cancel();
        var losingTask = ReferenceEquals(completed, delayTask) ? signalTask : delayTask;
        try
        {
            await losingTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (winnerCancellation.IsCancellationRequested)
        {
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (signalObserved)
        {
            _refreshSignal.Wait(0);
            Interlocked.Exchange(ref _signalPending, 0);
        }
    }

    private void NotifySubscribers(DashboardState state, long generation, CancellationToken cancellationToken)
    {
        Subscription[] subscribers;
        lock (_gate)
        {
            if (_disposed || _runGeneration != generation)
            {
                return;
            }

            subscribers = _subscriptions.ToArray();
        }

        foreach (var subscription in subscribers)
        {
            if (cancellationToken.IsCancellationRequested || subscription.IsDeactivated)
            {
                continue;
            }

            try
            {
                subscription.Notify(state);
            }
            catch
            {
                // One disposed or failed surface must not stop refreshes for the remaining subscribers.
            }
        }
    }

    private bool IsGenerationActive(long generation)
    {
        lock (_gate)
        {
            return !_disposed && _runGeneration == generation && _subscriptions.Count > 0;
        }
    }

    private void SignalRefresh()
    {
        if (Interlocked.Exchange(ref _signalPending, 1) == 0)
        {
            _refreshSignal.Release();
        }
    }

    private void DrainRefreshSignal()
    {
        while (_refreshSignal.Wait(0))
        {
        }

        Interlocked.Exchange(ref _signalPending, 0);
    }

    private TimeSpan NextFailureDelay(TimeSpan currentDelay)
    {
        var baseline = currentDelay <= TimeSpan.Zero ? _pollInterval : currentDelay;
        var nextTicks = Math.Min(MaximumFailureDelay.Ticks, checked(baseline.Ticks * 2));
        return TimeSpan.FromTicks(nextTicks);
    }

    private void Application_RuntimeStateChanged(object? sender, RuntimeStateChangedEventArgs e) => RequestRefresh();

    private void Remove(Subscription subscription)
    {
        CancellationTokenSource? cancellation = null;
        lock (_gate)
        {
            _subscriptions.Remove(subscription);
            if (_subscriptions.Count == 0)
            {
                cancellation = _runCancellation;
                _runCancellation = null;
                _runGeneration++;
            }
        }

        cancellation?.Cancel();
    }

    private sealed class Subscription : IDisposable
    {
        private readonly DashboardRefreshCoordinator _owner;
        private readonly Action<DashboardState> _subscriber;
        private int _deactivated;

        internal Subscription(DashboardRefreshCoordinator owner, Action<DashboardState> subscriber)
        {
            _owner = owner;
            _subscriber = subscriber;
        }

        internal bool IsDeactivated => Volatile.Read(ref _deactivated) != 0;

        internal void Deactivate() => Interlocked.Exchange(ref _deactivated, 1);

        internal void Notify(DashboardState state)
        {
            if (!IsDeactivated)
            {
                _subscriber(state);
            }
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _deactivated, 1) == 0)
            {
                _owner.Remove(this);
            }
        }
    }
}
