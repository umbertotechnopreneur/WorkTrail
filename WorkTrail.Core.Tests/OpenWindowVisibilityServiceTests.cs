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
using System.Collections.Generic;
using System.ComponentModel;
using System.Threading;
using WorkTrail.Application;
using WorkTrail.Services;
using Xunit;

namespace WorkTrail.Core.Tests;

public sealed class OpenWindowVisibilityServiceTests
{
    /// <summary>Reveals only live enabled peers while retaining the main window's foreground position.</summary>
    [Fact]
    public void Reveal_ShowsHiddenAndMinimizedPeersWithoutCreatingOrActivatingWindows()
    {
        var interop = new FakeInterop();
        interop.Windows[2] = State(visible: false);
        interop.Windows[3] = State(minimized: true);
        interop.Windows[4] = State();
        interop.Windows[5] = State(enabled: false);

        var revealed = new OpenWindowVisibilityService(interop).Reveal(new(1, [2, 3, 4, 5, 6]), CancellationToken.None);

        Assert.Equal(3, revealed);
        Assert.Equal(new[] { (2L, false), (3L, true) }, interop.Shown);
        Assert.Equal(new[] { (2L, 1L, false), (3L, 2L, false), (4L, 3L, false) }, interop.Placed);
        Assert.Equal(1, interop.ForegroundWindow);
        Assert.False(interop.Windows.ContainsKey(6));
    }

    /// <summary>A background, modal-disabled, hidden, or minimized main must not reveal peers.</summary>
    [Theory]
    [InlineData(false, true, false, 1)]
    [InlineData(true, false, false, 1)]
    [InlineData(true, true, true, 1)]
    [InlineData(true, true, false, 99)]
    public void InactiveMain_LeavesPeersUnchanged(bool enabled, bool visible, bool minimized, long foreground)
    {
        var interop = new FakeInterop { ForegroundWindow = foreground };
        interop.Windows[1] = State(enabled, visible, minimized);
        interop.Windows[2] = State(visible: false);

        Assert.Equal(0, new OpenWindowVisibilityService(interop).Reveal(new(1, [2]), CancellationToken.None));
        Assert.Empty(interop.Shown);
        Assert.Empty(interop.Placed);
    }

    /// <summary>A main window closed during the IPC round trip is a harmless no-op.</summary>
    [Fact]
    public void ClosedMain_DoesNotRevealPeers()
    {
        var interop = new FakeInterop();
        interop.Windows.Remove(1);
        interop.Windows[2] = State();
        Assert.Equal(0, new OpenWindowVisibilityService(interop).Reveal(new(1, [2]), CancellationToken.None));
        Assert.Empty(interop.Placed);
    }

    /// <summary>Validates all live peer ownership before changing any window.</summary>
    [Theory]
    [InlineData(1001, 100)]
    [InlineData(1000, 101)]
    public void OtherProcessOrUiThread_IsRejectedBeforeAnyMutation(uint processId, uint threadId)
    {
        var interop = new FakeInterop();
        interop.Windows[2] = State(visible: false);
        interop.Windows[3] = new(new(processId, threadId), true, true, false);

        Assert.Throws<ArgumentException>(() => new OpenWindowVisibilityService(interop).Reveal(new(1, [2, 3]), CancellationToken.None));
        Assert.Empty(interop.Shown);
        Assert.Empty(interop.Placed);
    }

    /// <summary>Rejects ambiguous or invalid handle lists before querying native window state.</summary>
    [Theory]
    [InlineData(0, 2, 3)]
    [InlineData(1, 0, 3)]
    [InlineData(1, 1, 3)]
    [InlineData(1, 2, 2)]
    public void InvalidHandles_FailBeforeInterop(long main, long first, long second)
    {
        var interop = new FakeInterop();
        Assert.ThrowsAny<ArgumentException>(() => new OpenWindowVisibilityService(interop).Reveal(new(main, [first, second]), CancellationToken.None));
        Assert.Equal(0, interop.ReadCount);
    }

    /// <summary>Rejects missing request data instead of treating it as an empty peer list.</summary>
    [Fact]
    public void MissingRequestOrPeerList_IsRejected()
    {
        var service = new OpenWindowVisibilityService(new FakeInterop());
        Assert.Throws<ArgumentNullException>(() => service.Reveal(null!, CancellationToken.None));
        Assert.Throws<ArgumentNullException>(() => service.Reveal(new(1, null!), CancellationToken.None));
    }

    /// <summary>Preserves the existing topmost band while raising ordinary peers only below the main.</summary>
    [Theory]
    [InlineData(false, 1)]
    [InlineData(true, 0)]
    public void TopmostPreference_IsNeitherAddedNorRemoved(bool mainTopmost, long normalInsertAfter)
    {
        var interop = new FakeInterop();
        interop.Windows[1] = State(topmost: mainTopmost);
        interop.Windows[2] = State(topmost: true);
        interop.Windows[3] = State();

        Assert.Equal(2, new OpenWindowVisibilityService(interop).Reveal(new(1, [2, 3]), CancellationToken.None));
        Assert.Equal(new[] { (2L, normalInsertAfter, true), (3L, normalInsertAfter, false) }, interop.Placed);
        Assert.Equal(1, interop.ForegroundWindow);
    }

    /// <summary>Does not perform further positioning when another application gains focus during a show request.</summary>
    [Fact]
    public void ForegroundChangesDuringShow_StopsWithoutPositioning()
    {
        var interop = new FakeInterop();
        interop.Windows[2] = State(visible: false);
        interop.Windows[3] = State();
        interop.OnShow = _ => interop.ForegroundWindow = 99;

        Assert.Equal(0, new OpenWindowVisibilityService(interop).Reveal(new(1, [2, 3]), CancellationToken.None));
        Assert.Single(interop.Shown);
        Assert.Empty(interop.Placed);
    }

    /// <summary>Checks the foreground again between peers rather than completing a stale activation batch.</summary>
    [Fact]
    public void ForegroundChangesBetweenPeers_StopsTheBatch()
    {
        var interop = new FakeInterop();
        interop.Windows[2] = State();
        interop.Windows[3] = State();
        interop.OnPlace = _ => interop.ForegroundWindow = 99;

        Assert.Equal(1, new OpenWindowVisibilityService(interop).Reveal(new(1, [2, 3]), CancellationToken.None));
        Assert.Equal(new[] { (2L, 1L, false) }, interop.Placed);
    }

    /// <summary>Only races with a confirmed closed peer are tolerated; genuine native failures propagate.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NativeFailure_IsIgnoredOnlyWhenPeerHasClosed(bool closed)
    {
        var interop = new FakeInterop();
        interop.Windows[2] = State();
        interop.OnPlace = handle =>
        {
            if (closed) interop.Windows.Remove(handle);
            throw new Win32Exception(5);
        };
        var service = new OpenWindowVisibilityService(interop);

        if (closed) Assert.Equal(0, service.Reveal(new(1, [2]), CancellationToken.None));
        else Assert.Throws<Win32Exception>(() => service.Reveal(new(1, [2]), CancellationToken.None));
    }

    /// <summary>Cancellation prevents native reads or stops after the already-completed peer.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Cancellation_StopsBeforeFurtherInterop(bool duringBatch)
    {
        var interop = new FakeInterop();
        interop.Windows[2] = State();
        interop.Windows[3] = State();
        using var cancellation = new CancellationTokenSource();
        if (duringBatch) interop.OnPlace = _ => cancellation.Cancel();
        else cancellation.Cancel();

        Assert.Throws<OperationCanceledException>(() => new OpenWindowVisibilityService(interop).Reveal(new(1, [2, 3]), cancellation.Token));
        Assert.Equal(duringBatch ? 1 : 0, interop.Placed.Count);
        if (!duringBatch) Assert.Equal(0, interop.ReadCount);
    }

    private static OpenNativeWindowState State(bool enabled = true, bool visible = true, bool minimized = false, bool topmost = false) =>
        new(new(1000, 100), enabled, visible, minimized, topmost);

    private sealed class FakeInterop : IOpenWindowVisibilityInterop
    {
        internal Dictionary<long, OpenNativeWindowState> Windows { get; } = new() { [1] = State() };
        internal List<(long Handle, bool Minimized)> Shown { get; } = [];
        internal List<(long Handle, long InsertAfter, bool PreserveZOrder)> Placed { get; } = [];
        internal Action<long>? OnShow { get; set; }
        internal Action<long>? OnPlace { get; set; }
        internal int ReadCount { get; private set; }
        public long ForegroundWindow { get; set; } = 1;

        /// <inheritdoc />
        public OpenNativeWindowState? Read(long handle)
        {
            ReadCount++;
            return Windows.GetValueOrDefault(handle);
        }

        /// <inheritdoc />
        public void ShowWithoutActivation(long handle, bool minimized)
        {
            Shown.Add((handle, minimized));
            OnShow?.Invoke(handle);
        }

        /// <inheritdoc />
        public void PlaceWithoutActivation(long handle, long insertAfter, bool preserveZOrder)
        {
            Placed.Add((handle, insertAfter, preserveZOrder));
            OnPlace?.Invoke(handle);
        }
    }
}
