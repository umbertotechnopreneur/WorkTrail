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
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using WorkTrail.Services;
using Xunit;

namespace WorkTrail.Core.Tests;

public sealed class ProcessMemoryGuardServiceTests
{
    [Theory]
    [InlineData(268_435_456, 402_653_184)]
    [InlineData(1_073_741_824, 1_073_741_824)]
    public void AtOrBelowLimit_OnlyRecordsDiagnostics(long privateBytes, long workingSetBytes)
    {
        var logger = new RecordingLogger();
        using var guard = new ProcessMemoryGuardService(logger,
            () => new ProcessMemorySnapshot(privateBytes, workingSetBytes, 100, 20),
            () => throw new InvalidOperationException("Unexpected warning."),
            _ => throw new InvalidOperationException("Unexpected restart."),
            exception => throw new InvalidOperationException("Unexpected failure.", exception));

        guard.CheckMemory();
        guard.CheckMemory();

        Assert.Single(logger.Entries);
        Assert.Contains("Process memory sample", logger.Entries[0], StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(1_073_741_825, 268_435_456)]
    [InlineData(268_435_456, 1_073_741_825)]
    public void PrivateOrResidentLimit_LogsThenWarnsThenRequestsOneRestart(long privateBytes, long workingSetBytes)
    {
        var logger = new RecordingLogger();
        var memory = new ProcessMemorySnapshot(privateBytes, workingSetBytes, 100, 20);
        var notices = 0;
        var requests = 0;
        using var guard = new ProcessMemoryGuardService(logger, () => memory,
            () =>
            {
                Assert.Contains(logger.Entries, entry => entry.Contains("memory.limit.exceeded", StringComparison.Ordinal));
                Assert.Equal(0, requests);
                notices++;
            },
            snapshot =>
            {
                Assert.Equal(memory, snapshot);
                Assert.Equal(1, notices);
                Assert.Contains(logger.Entries, entry => entry.Contains("memory.restart.acknowledged", StringComparison.Ordinal));
                requests++;
                return true;
            },
            exception => throw new InvalidOperationException("Unexpected failure.", exception));

        guard.CheckMemory();
        guard.CheckMemory();
        guard.CheckMemory();

        Assert.Equal(1, notices);
        Assert.Equal(1, requests);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PendingWarning_PreventsOverlapAndShutdownPreventsRestart(bool closeDuringWarning)
    {
        var logger = new RecordingLogger();
        using var entered = new ManualResetEventSlim();
        using var acknowledge = new ManualResetEventSlim();
        var notices = 0;
        var requests = 0;
        using var guard = new ProcessMemoryGuardService(logger,
            () => new ProcessMemorySnapshot(2L * ProcessMemoryGuardService.MemoryLimitBytes, 100, 50, 20),
            () =>
            {
                Interlocked.Increment(ref notices);
                entered.Set();
                Assert.True(acknowledge.Wait(TimeSpan.FromSeconds(5)));
            },
            _ => { Interlocked.Increment(ref requests); return true; },
            exception => throw new InvalidOperationException("Unexpected failure.", exception));
        var check = Task.Run(guard.CheckMemory);
        try
        {
            Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
            guard.CheckMemory();
            Assert.Equal(1, notices);
            Assert.Equal(0, requests);
            if (closeDuringWarning) guard.Dispose();
        }
        finally { acknowledge.Set(); }
        await check.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(closeDuringWarning ? 0 : 1, requests);
    }

    [Fact]
    public void WarningFailure_DoesNotRestartOrImmediatelyOpenAnotherWarning()
    {
        var notices = 0;
        var requests = 0;
        var failures = new List<Exception>();
        using var guard = new ProcessMemoryGuardService(new RecordingLogger(),
            () => new ProcessMemorySnapshot(ProcessMemoryGuardService.MemoryLimitBytes + 1, 100, 50, 20),
            () => { notices++; throw new InvalidOperationException("Simulated native dialog failure."); },
            _ => { requests++; return true; }, failures.Add);

        guard.CheckMemory();
        guard.CheckMemory();

        Assert.Equal(1, notices);
        Assert.Equal(0, requests);
        Assert.Single(failures);
    }

    [Fact]
    public void RejectedDispatcher_ReportsFailureAndBoundsRecoveryRetries()
    {
        var logger = new RecordingLogger();
        var requests = 0;
        var failures = new List<Exception>();
        using var guard = new ProcessMemoryGuardService(logger,
            () => new ProcessMemorySnapshot(ProcessMemoryGuardService.MemoryLimitBytes + 1, 100, 50, 20),
            () => { }, _ => { requests++; return false; }, failures.Add);

        guard.CheckMemory();
        guard.CheckMemory();

        Assert.Equal(1, requests);
        Assert.Single(failures);
        Assert.Contains(logger.Entries, entry => entry.Contains("memory.guard.failed", StringComparison.Ordinal));
    }

    [Fact]
    public void RepeatedSamplingFailure_IsReportedOnceUntilSamplingRecovers()
    {
        var readFails = true;
        var failures = new List<Exception>();
        using var guard = new ProcessMemoryGuardService(new RecordingLogger(),
            () => readFails ? throw new InvalidOperationException("Simulated process read failure.")
                : new ProcessMemorySnapshot(100, 100, 50, 20),
            () => { }, _ => false, failures.Add);

        guard.CheckMemory();
        guard.CheckMemory();
        Assert.Single(failures);
        readFails = false;
        guard.CheckMemory();
        readFails = true;
        guard.CheckMemory();
        Assert.Equal(2, failures.Count);
    }

    [Fact]
    public void DisposedGuard_DoesNotReadMemoryOrRequestRecovery()
    {
        using var guard = new ProcessMemoryGuardService(new RecordingLogger(),
            () => throw new InvalidOperationException("Unexpected memory read."),
            () => throw new InvalidOperationException("Unexpected warning."),
            _ => throw new InvalidOperationException("Unexpected restart."),
            exception => throw new InvalidOperationException("Unexpected failure.", exception));
        guard.Dispose();

        guard.CheckMemory();
    }

    private sealed class RecordingLogger : ILogger<ProcessMemoryGuardService>
    {
        internal List<string> Entries { get; } = new();
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => Entries.Add(formatter(state, exception));
    }
}
