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
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace WorkTrail.Presentation.Tests;

public sealed class DialogSessionQueueTests
{
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(5);

    /// <summary>Only the current lease can occupy the presentation slot.</summary>
    [Fact]
    public async Task Sessions_DoNotOverlap()
    {
        var queue = new DialogSessionQueue();
        var first = await queue.EnterAsync();
        Assert.NotNull(first);
        var pending = queue.EnterAsync();
        Assert.False(pending.IsCompleted);

        first.Dispose();
        using var second = await pending.WaitAsync(TestTimeout);
        Assert.NotNull(second);
    }

    /// <summary>Shutdown cancels waiters even while a dialog still owns the slot.</summary>
    [Fact]
    public async Task Shutdown_CancelsAllWaitersWithoutWaitingForTheActiveDialog()
    {
        var queue = new DialogSessionQueue();
        using var active = await queue.EnterAsync();
        var pending = Enumerable.Range(0, 8).Select(_ => queue.EnterAsync()).ToArray();
        queue.Shutdown();

        Assert.True(queue.IsShuttingDown);
        var results = await Task.WhenAll(pending).WaitAsync(TestTimeout);
        Assert.All(results, result => Assert.Null(result));
        Assert.Null(await queue.EnterAsync().WaitAsync(TestTimeout));
        queue.Shutdown();
    }

    /// <summary>Closing one owner does not discard another owner's request.</summary>
    [Fact]
    public async Task ClosedOwner_CancelsItsQueuedDialogWithoutCancellingOtherOwners()
    {
        var queue = new DialogSessionQueue();
        var active = await queue.EnterAsync();
        using var owner = new CancellationTokenSource();
        var cancelled = queue.EnterAsync(owner.Token);
        var next = queue.EnterAsync();

        owner.Cancel();
        Assert.Null(await cancelled.WaitAsync(TestTimeout));
        Assert.False(next.IsCompleted);
        Assert.False(queue.IsShuttingDown);
        active!.Dispose();

        using var nextSession = await next.WaitAsync(TestTimeout);
        Assert.NotNull(nextSession);
    }

    /// <summary>An expired owner never acquires the slot.</summary>
    [Fact]
    public async Task AlreadyClosedOwner_DoesNotAcquireTheModalSlot()
    {
        var queue = new DialogSessionQueue();
        Assert.Null(await queue.EnterAsync(new CancellationToken(canceled: true)));
        using var next = await queue.EnterAsync().WaitAsync(TestTimeout);
        Assert.NotNull(next);
    }

    /// <summary>Repeated disposal cannot make two later sessions overlap.</summary>
    [Fact]
    public async Task DisposingALeaseTwice_DoesNotReleaseAnotherSessionsSlot()
    {
        var queue = new DialogSessionQueue();
        var first = await queue.EnterAsync();
        first!.Dispose();
        var second = await queue.EnterAsync();
        var pending = queue.EnterAsync();
        first.Dispose();

        Assert.False(pending.IsCompleted);
        second!.Dispose();
        using var third = await pending.WaitAsync(TestTimeout);
        Assert.NotNull(third);
    }

    /// <summary>A failed presentation releases the slot through deterministic cleanup.</summary>
    [Fact]
    public async Task PresentationFailure_ReleasesTheSlot()
    {
        var queue = new DialogSessionQueue();
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            using var session = await queue.EnterAsync();
            throw new InvalidOperationException("Presentation failed.");
        });

        using var next = await queue.EnterAsync().WaitAsync(TestTimeout);
        Assert.NotNull(next);
    }
}
