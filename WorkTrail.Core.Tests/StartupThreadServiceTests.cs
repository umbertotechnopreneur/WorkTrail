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
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using WorkTrail.Services;
using Xunit;

namespace WorkTrail.Core.Tests;

public sealed class StartupThreadServiceTests
{
    /// <summary>Checks that startup stays on STA while redirection executes on MTA and completes before returning.</summary>
    [Fact]
    public void CompleteActivationRedirection_RunsOnMtaAndWaitsWithoutChangingTheStaCaller()
    {
        RunOnStaThread(() =>
        {
            var callerThreadId = Environment.CurrentManagedThreadId;
            var completed = false;

            StartupThreadService.CompleteActivationRedirection(async () =>
            {
                Assert.Equal(ApartmentState.MTA, Thread.CurrentThread.GetApartmentState());
                Assert.NotEqual(callerThreadId, Environment.CurrentManagedThreadId);
                await Task.Yield();
                completed = true;
            });

            Assert.True(completed);
            Assert.Equal(callerThreadId, Environment.CurrentManagedThreadId);
            Assert.Equal(ApartmentState.STA, Thread.CurrentThread.GetApartmentState());
        });
    }

    /// <summary>Checks that both immediate and asynchronous redirection failures reach the caller unchanged.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CompleteActivationRedirection_PropagatesTheOriginalFailure(bool failAsynchronously)
    {
        RunOnStaThread(() =>
        {
            var expected = new InvalidOperationException("Synthetic activation redirection failure.");
            Func<Task> redirectAsync = failAsynchronously
                ? async () =>
                {
                    await Task.Yield();
                    throw expected;
                }
            : () => throw expected;

            var actual = Assert.Throws<InvalidOperationException>(() =>
                StartupThreadService.CompleteActivationRedirection(redirectAsync));

            Assert.Same(expected, actual);
        });
    }

    /// <summary>Checks that an absent redirection callback fails before a worker starts.</summary>
    [Fact]
    public void CompleteActivationRedirection_RejectsMissingOperation()
    {
        Assert.Throws<ArgumentNullException>(() => StartupThreadService.CompleteActivationRedirection(null!));
    }

    /// <summary>Checks that a broken callback cannot silently report a completed redirection.</summary>
    [Fact]
    public void CompleteActivationRedirection_RejectsMissingTask()
    {
        Assert.Throws<InvalidOperationException>(() => StartupThreadService.CompleteActivationRedirection(() => null!));
    }

    private static void RunOnStaThread(Action action)
    {
        ExceptionDispatchInfo? failure = null;
        var callerThread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception exception)
            {
                failure = ExceptionDispatchInfo.Capture(exception);
            }
        })
        {
            IsBackground = true
        };

        callerThread.SetApartmentState(ApartmentState.STA);
        callerThread.Start();
        Assert.True(callerThread.Join(TimeSpan.FromSeconds(15)), "The STA redirection caller did not complete.");
        failure?.Throw();
    }
}
