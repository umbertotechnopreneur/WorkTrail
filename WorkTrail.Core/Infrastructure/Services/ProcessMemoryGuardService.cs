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


using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;

namespace WorkTrail.Services;

/// <summary>Contains process memory measurements without activity content or private identifiers.</summary>
public sealed record ProcessMemorySnapshot(long PrivateBytes, long WorkingSetBytes, long ManagedBytes, int HandleCount);

/// <summary>Samples process memory and requests recovery after acknowledgement of a native Windows warning.</summary>
public sealed class ProcessMemoryGuardService : IDisposable
{
    internal const long MemoryLimitBytes = 1_073_741_824;
    private static readonly TimeSpan SamplingInterval = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan DiagnosticInterval = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan RecoveryRetryInterval = TimeSpan.FromMinutes(1);
    private readonly ILogger<ProcessMemoryGuardService> _logger;
    private readonly Func<ProcessMemorySnapshot> _readMemory;
    private readonly Action _showRestartNotice;
    private readonly Func<ProcessMemorySnapshot, bool> _requestRestart;
    private readonly Action<Exception> _reportFailure;
    private readonly Timer _timer;
    private readonly object _lifetimeGate = new();
    private DateTimeOffset _lastDiagnosticAt;
    private DateTimeOffset _nextRecoveryAttemptAt;
    private long _lastDiagnosticBytes;
    private int _checkActive;
    private bool _started;
    private bool _disposed;
    private bool _restartRequested;
    private bool _failureReported;

    /// <summary>Creates the process guard with the composition root's restart dispatcher and current locale.</summary>
    /// <param name="logger">The durable application logger.</param>
    /// <param name="requestRestart">Queues recovery and returns whether the dispatcher accepted it.</param>
    /// <param name="language">Reads the current application language when a warning is needed.</param>
    public ProcessMemoryGuardService(
        ILogger<ProcessMemoryGuardService> logger,
        Func<ProcessMemorySnapshot, bool> requestRestart,
        Func<string> language)
        : this(logger, ReadCurrentMemory, () => ShowRestartNotice(language()), requestRestart,
            exception => ApplicationErrorService.Report(exception, "ProcessMemoryGuard", language(), deferDialog: true))
    {
        ArgumentNullException.ThrowIfNull(language);
    }

    internal ProcessMemoryGuardService(
        ILogger<ProcessMemoryGuardService> logger,
        Func<ProcessMemorySnapshot> readMemory,
        Action showRestartNotice,
        Func<ProcessMemorySnapshot, bool> requestRestart,
        Action<Exception> reportFailure)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _readMemory = readMemory ?? throw new ArgumentNullException(nameof(readMemory));
        _showRestartNotice = showRestartNotice ?? throw new ArgumentNullException(nameof(showRestartNotice));
        _requestRestart = requestRestart ?? throw new ArgumentNullException(nameof(requestRestart));
        _reportFailure = reportFailure ?? throw new ArgumentNullException(nameof(reportFailure));
        _timer = new Timer(_ => CheckMemory(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
    }

    /// <summary>Starts one background sampler, independently of WinUI responsiveness.</summary>
    public void Start()
    {
        lock (_lifetimeGate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_started) return;
            _started = true;
            _logger.LogInformation("Process memory guard started. LimitBytes={LimitBytes} SamplingSeconds={SamplingSeconds}",
                MemoryLimitBytes, SamplingInterval.TotalSeconds);
            _timer.Change(SamplingInterval, SamplingInterval);
        }
    }

    /// <summary>Stops sampling and prevents an outstanding warning from restarting a closing application.</summary>
    public void Dispose()
    {
        lock (_lifetimeGate)
        {
            if (_disposed) return;
            Volatile.Write(ref _disposed, true);
            // Do not wait for user acknowledgement during ordinary application shutdown.
            _timer.Dispose();
        }
    }

    internal void CheckMemory()
    {
        if (Volatile.Read(ref _disposed) || Volatile.Read(ref _restartRequested)
            || Interlocked.CompareExchange(ref _checkActive, 1, 0) != 0) return;
        try
        {
            if (Volatile.Read(ref _disposed)) return;
            var memory = _readMemory();
            var now = DateTimeOffset.UtcNow;
            var currentBytes = Math.Max(memory.PrivateBytes, memory.WorkingSetBytes);
            if (now - _lastDiagnosticAt >= DiagnosticInterval
                || currentBytes > MemoryLimitBytes
                || currentBytes - _lastDiagnosticBytes >= 134_217_728)
            {
                _logger.LogInformation(
                    "Process memory sample. PrivateBytes={PrivateBytes} WorkingSetBytes={WorkingSetBytes} ManagedBytes={ManagedBytes} HandleCount={HandleCount}",
                    memory.PrivateBytes, memory.WorkingSetBytes, memory.ManagedBytes, memory.HandleCount);
                _lastDiagnosticAt = now;
                _lastDiagnosticBytes = currentBytes;
            }
            _failureReported = false;
            if (currentBytes <= MemoryLimitBytes || now < _nextRecoveryAttemptAt) return;
            _nextRecoveryAttemptAt = now + RecoveryRetryInterval;
            _logger.LogWarning(
                "Process memory limit exceeded. Event=memory.limit.exceeded LimitBytes={LimitBytes} PrivateBytes={PrivateBytes} WorkingSetBytes={WorkingSetBytes} ManagedBytes={ManagedBytes} HandleCount={HandleCount}",
                MemoryLimitBytes, memory.PrivateBytes, memory.WorkingSetBytes, memory.ManagedBytes, memory.HandleCount);

            // The native warning runs on this worker, so a congested UI cannot hide the memory warning.
            _showRestartNotice();
            if (Volatile.Read(ref _disposed)) return;
            _logger.LogWarning("Process memory recovery acknowledged. Event=memory.restart.acknowledged");
            if (!_requestRestart(memory))
                throw new InvalidOperationException("The application dispatcher could not queue memory recovery.");
            Volatile.Write(ref _restartRequested, true);
        }
        catch (Exception exception)
        {
            if (Volatile.Read(ref _disposed) || _failureReported) return;
            _failureReported = true;
            _logger.LogError(exception, "Process memory guard failed. Event=memory.guard.failed");
            _reportFailure(exception);
        }
        finally
        {
            Volatile.Write(ref _checkActive, 0);
        }
    }

    private static ProcessMemorySnapshot ReadCurrentMemory()
    {
        // Dispose each Process wrapper so the sampler does not retain process handles or cached snapshots.
        using var process = Process.GetCurrentProcess();
        process.Refresh();
        return new ProcessMemorySnapshot(process.PrivateMemorySize64, process.WorkingSet64,
            GC.GetTotalMemory(forceFullCollection: false), process.HandleCount);
    }

    private static void ShowRestartNotice(string language)
    {
        var strings = new LocalizationService(language);
        const uint warningIcon = 0x00000030;
        const uint systemModal = 0x00001000;
        const uint foreground = 0x00010000;
        // One OK acknowledgement initiates recovery; no WinUI window or XamlRoot is required.
        if (MessageBox(IntPtr.Zero, strings.Translate("MemoryGuard.Message"), strings.Translate("MemoryGuard.Title"),
                warningIcon | systemModal | foreground) == 0)
            throw new Win32Exception(Marshal.GetLastWin32Error());
    }

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("user32.dll", EntryPoint = "MessageBoxW", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    private static extern int MessageBox(IntPtr owner, string text, string caption, uint flags);
}
