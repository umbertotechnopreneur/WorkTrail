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
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using WorkTrail.Application;
using Xunit;

namespace WorkTrail.Core.Tests;

public sealed class DashboardRefreshCoordinatorTests
{
    [Fact]
    public async Task TwoSubscribers_ShareOneSingleFlightLoop_AndFinalDisposeStopsIt()
    {
        var application = DispatchProxy.Create<IWorkTrailApplication, DashboardApplicationProxy>();
        var proxy = (DashboardApplicationProxy)(object)application;
        var firstEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirst = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        proxy.GetDashboard = async cancellationToken =>
        {
            firstEntered.TrySetResult();
            await releaseFirst.Task.WaitAsync(cancellationToken);
            return SuccessState();
        };

        using var coordinator = new DashboardRefreshCoordinator(application, TimeSpan.FromMilliseconds(20));
        var firstUpdates = 0;
        var secondUpdates = 0;
        var first = coordinator.Subscribe(_ => Interlocked.Increment(ref firstUpdates));
        var second = coordinator.Subscribe(_ => Interlocked.Increment(ref secondUpdates));

        await firstEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        coordinator.RequestRefresh();
        coordinator.RequestRefresh();
        await Task.Delay(60);
        Assert.Equal(1, proxy.CallCount);
        Assert.Equal(1, proxy.MaximumConcurrency);

        releaseFirst.TrySetResult();
        await WaitUntilAsync(() => Volatile.Read(ref firstUpdates) > 0 && Volatile.Read(ref secondUpdates) > 0);
        first.Dispose();
        second.Dispose();
        var stoppedAt = proxy.CallCount;
        await Task.Delay(100);

        Assert.Equal(stoppedAt, proxy.CallCount);
        Assert.Equal(1, proxy.MaximumConcurrency);
    }

    [Fact]
    public async Task ImmediateRefresh_RemainsResponsiveAfterTimerWonPreviousWait()
    {
        var application = DispatchProxy.Create<IWorkTrailApplication, DashboardApplicationProxy>();
        var proxy = (DashboardApplicationProxy)(object)application;
        proxy.GetDashboard = _ => Task.FromResult(SuccessState());

        using var coordinator = new DashboardRefreshCoordinator(application, TimeSpan.FromMilliseconds(500));
        using var subscription = coordinator.Subscribe(_ => { });
        await WaitUntilAsync(() => proxy.CallCount >= 2, TimeSpan.FromSeconds(2));
        var beforeSignal = proxy.CallCount;

        coordinator.RequestRefresh();
        await WaitUntilAsync(() => proxy.CallCount > beforeSignal, TimeSpan.FromMilliseconds(250));

        Assert.True(proxy.CallCount > beforeSignal);
    }

    private static OperationResult<DashboardState> SuccessState() =>
        OperationResult<DashboardState>.Success(
            "dashboard.loaded",
            "DashboardLoaded",
            new DashboardState(
                "READY",
                "test",
                0,
                0,
                0,
                0,
                false,
                null,
                DateTimeOffset.Now,
                DateTimeOffset.UtcNow));

    private static async Task WaitUntilAsync(Func<bool> predicate, TimeSpan? timeout = null)
    {
        var deadline = DateTimeOffset.UtcNow + (timeout ?? TimeSpan.FromSeconds(2));
        while (!predicate())
        {
            if (DateTimeOffset.UtcNow >= deadline)
            {
                throw new TimeoutException("The dashboard coordinator did not reach the expected state.");
            }

            await Task.Delay(10);
        }
    }

    public class DashboardApplicationProxy : DispatchProxy
    {
        private readonly object _gate = new();
        private int _activeCalls;
        private int _callCount;
        private int _maximumConcurrency;
        private EventHandler<RuntimeStateChangedEventArgs>? _runtimeStateChanged;

        internal Func<CancellationToken, Task<OperationResult<DashboardState>>> GetDashboard { get; set; } =
            _ => Task.FromResult(SuccessState());

        internal int CallCount => Volatile.Read(ref _callCount);

        internal int MaximumConcurrency => Volatile.Read(ref _maximumConcurrency);

        internal void EnterCall()
        {
            Interlocked.Increment(ref _callCount);
            var active = Interlocked.Increment(ref _activeCalls);
            lock (_gate)
            {
                _maximumConcurrency = Math.Max(_maximumConcurrency, active);
            }
        }

        internal void ExitCall() => Interlocked.Decrement(ref _activeCalls);

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            return targetMethod?.Name switch
            {
                "add_RuntimeStateChanged" => AddHandler((EventHandler<RuntimeStateChangedEventArgs>)args![0]!),
                "remove_RuntimeStateChanged" => RemoveHandler((EventHandler<RuntimeStateChangedEventArgs>)args![0]!),
                nameof(IWorkTrailApplication.GetDashboardAsync) => InvokeDashboard((CancellationToken)args![0]!),
                _ => throw new NotSupportedException($"Unexpected application call: {targetMethod?.Name}")
            };
        }

        private object? AddHandler(EventHandler<RuntimeStateChangedEventArgs> handler)
        {
            _runtimeStateChanged += handler;
            return null;
        }

        private object? RemoveHandler(EventHandler<RuntimeStateChangedEventArgs> handler)
        {
            _runtimeStateChanged -= handler;
            return null;
        }

        private async Task<OperationResult<DashboardState>> InvokeDashboard(CancellationToken cancellationToken)
        {
            EnterCall();
            try
            {
                return await GetDashboard(cancellationToken);
            }
            finally
            {
                ExitCall();
            }
        }
    }
}
