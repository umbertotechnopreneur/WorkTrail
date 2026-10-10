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
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace WorkTrail.Services;

/// <summary>Serializes the sole isolated hardware collector and shares immutable snapshots across consumers.</summary>
public sealed class HardwareTelemetryService : IHardwareTelemetryService
{
    private static readonly TimeSpan FailureRetryInterval = TimeSpan.FromSeconds(10);
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly SemaphoreSlim _trackingGate = new(1, 1);
    private readonly CancellationTokenSource _lifetime = new();
    private readonly string _helperPath;
    private readonly PawnIoInstaller _installer;
    private readonly ILogger<HardwareTelemetryService> _logger;
    private readonly TimeProvider _time;
    private readonly Func<CancellationToken, ValueTask<SystemSnapshot>>? _testReader;
    private readonly Func<CancellationToken, Task> _startCollector;
    private CancellationTokenSource? _polling;
    private Task? _pollTask;
    private NamedPipeServerStream? _pipe;
    private Process? _helper;
    private SystemSnapshot? _snapshot;
    private DateTimeOffset _lastAttempt;
    private HardwareTelemetryConfiguration _configuration = new();
    private HardwareSamplingProfile _profile = HardwareSamplingProfiles.Get("normal");
    private bool _trackingRequested;
    private bool _advanced;
    private bool _configured;
    private volatile bool _disposed;

    /// <summary>Creates the application-owned collector with the packaged helper beside the executable.</summary>
    public HardwareTelemetryService(ILogger<HardwareTelemetryService>? logger = null)
        : this(Path.Combine(AppContext.BaseDirectory, "Hardware", "WorkTrail.Hardware.exe"), TimeProvider.System, logger) { }

    internal HardwareTelemetryService(string helperPath, TimeProvider timeProvider, ILogger<HardwareTelemetryService>? logger = null,
        Func<CancellationToken, ValueTask<SystemSnapshot>>? reader = null, PawnIoInstaller? installer = null,
        Func<CancellationToken, Task>? startCollector = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(helperPath);
        _helperPath = Path.GetFullPath(helperPath);
        _installer = installer ?? new PawnIoInstaller(Path.Combine(Path.GetDirectoryName(_helperPath)!, "PawnIO", "PawnIO_setup.exe"));
        _time = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _logger = logger ?? NullLogger<HardwareTelemetryService>.Instance;
        _testReader = reader;
        _startCollector = startCollector ?? StartCollectorAsync;
    }

    /// <summary>Applies settings and, on initialization, restores opted-in advanced access when PawnIO is already installed.</summary>
    public async ValueTask ConfigureAsync(HardwareTelemetryConfiguration configuration, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var profile = HardwareSamplingProfiles.Get(configuration.SamplingProfile);
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _trackingGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_configured && _configuration == configuration) return;
            // Stop only the timer, not an in-flight read: changing rates must retain an elevated session.
            await StopPollingAsync().ConfigureAwait(false);
            try
            {
                await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    ObjectDisposedException.ThrowIf(_disposed, this);
                    var initializing = !_configured;
                    // Consume the startup attempt before awaiting consent; later settings changes never repeat it.
                    _configured = true;
                    var resetSnapshot = _configuration.Enabled != configuration.Enabled
                        || _configuration.SamplingProfile != configuration.SamplingProfile
                        || _advanced && !configuration.UseAdvancedSensors;
                    if (!configuration.Enabled || _advanced && !configuration.UseAdvancedSensors)
                        await StopCollectorAsync().ConfigureAwait(false);
                    _configuration = configuration;
                    _profile = profile;
                    if (!configuration.Enabled)
                        _snapshot = new SystemSnapshot(_time.GetUtcNow(), "disabled", [], "disabled");
                    else if (resetSnapshot)
                        _snapshot = null;
                    if (initializing && configuration.Enabled && configuration.UseAdvancedSensors
                        && RuntimeInformation.ProcessArchitecture == Architecture.X64)
                        await RestoreAdvancedSessionAsync(cancellationToken).ConfigureAwait(false);
                }
                finally { _gate.Release(); }
            }
            finally { StartPollingIfRequested(); }
        }
        finally { _trackingGate.Release(); }
    }

    /// <summary>Returns a recent immutable reading or collects one with a strict process/IPC deadline.</summary>
    public async ValueTask<SystemSnapshot> CaptureAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!_configuration.Enabled)
                return _snapshot ??= new SystemSnapshot(_time.GetUtcNow(), "disabled", [], "disabled");
            var now = _time.GetUtcNow();
            var retry = _snapshot?.Status is "error" or "unavailable" or "unsupported" ? FailureRetryInterval : _profile.CpuInterval;
            if (_snapshot is not null && now - _lastAttempt < retry) return _snapshot;
            _lastAttempt = now;
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
            deadline.CancelAfter(_pipe is null ? TimeSpan.FromSeconds(8) : TimeSpan.FromSeconds(5));
            try
            {
                var snapshot = _testReader is null
                    ? await ReadCollectorAsync(deadline.Token).ConfigureAwait(false)
                    : await _testReader(deadline.Token).ConfigureAwait(false);
                SystemSnapshotValidator.Validate(snapshot);
                // Own every collection so no consumer can mutate the snapshot seen by another consumer.
                _snapshot = snapshot with
                {
                    Devices = Array.AsReadOnly(snapshot.Devices.Select(device => device with
                    {
                        Sensors = Array.AsReadOnly(device.Sensors.ToArray())
                    }).ToArray())
                };
                // A fatal library failure can leave initialization partially complete. Retain the
                // explicit error, but discard that session; a later retry never requests UAC automatically.
                if (snapshot.Status == "error") await StopCollectorAsync().ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested || _lifetime.IsCancellationRequested)
            {
                await StopCollectorAsync().ConfigureAwait(false);
                throw;
            }
            catch (Exception exception) when (exception is IOException or InvalidDataException or Win32Exception or UnauthorizedAccessException or OperationCanceledException or System.Text.Json.JsonException)
            {
                // Optional telemetry failure is visible in the snapshot; screenshot capture remains available.
                await StopCollectorAsync().ConfigureAwait(false);
                var code = exception switch
                {
                    FileNotFoundException => "helper-missing",
                    OperationCanceledException => "collector-timeout",
                    UnauthorizedAccessException => "collector-access-denied",
                    _ => "collector-failed"
                };
                _logger.LogWarning("Hardware telemetry unavailable. ErrorCode={ErrorCode} ExceptionType={ExceptionType}", code, exception.GetType().Name);
                _snapshot = new SystemSnapshot(_time.GetUtcNow(), "unavailable", [], GetDriverStatus(), code, now);
            }
            // Admit the next poll relative to completion, matching the helper's device timestamps.
            _lastAttempt = _time.GetUtcNow();
            return _snapshot;
        }
        finally { _gate.Release(); }
    }

    /// <summary>Polls while tracking is active, and cancels periodic work when tracking stops.</summary>
    public async ValueTask SetTrackingAsync(bool isTracking, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _trackingGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _trackingRequested = isTracking;
            if (isTracking) StartPollingIfRequested();
            else await StopPollingAsync().ConfigureAwait(false);
        }
        finally { _trackingGate.Release(); }
    }

    /// <summary>Installs the bundled driver if needed and explicitly requests elevation for this collector session.</summary>
    public async Task EnableAdvancedAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!_configuration.Enabled || !_configuration.UseAdvancedSensors)
                throw new InvalidOperationException("Advanced telemetry requires enabled sensors and explicit advanced-sensor configuration.");
            if (RuntimeInformation.ProcessArchitecture != Architecture.X64) throw new PlatformNotSupportedException("Advanced hardware telemetry requires x64.");
            if (_advanced && _pipe is { IsConnected: true }) return;
            // The installer is bundled for offline use and is launched only by this explicit action.
            using var setupDeadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
            setupDeadline.CancelAfter(TimeSpan.FromMinutes(5));
            await _installer.EnsureInstalledAsync(setupDeadline.Token).ConfigureAwait(false);
            await StartAdvancedCollectorAsync(cancellationToken).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }

    private async Task RestoreAdvancedSessionAsync(CancellationToken cancellationToken)
    {
        try
        {
            // Startup only reuses a compatible installed driver. Setup remains an explicit user action.
            if (_installer.CanActivateWithoutSetup)
                await StartAdvancedCollectorAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested || _lifetime.IsCancellationRequested) { throw; }
        catch (Exception exception) when (exception is IOException or InvalidDataException or Win32Exception or UnauthorizedAccessException or OperationCanceledException or System.Text.Json.JsonException)
        {
            // Optional sensor activation must not block app startup. Report the failure, then permit basic
            // readings after the normal cooldown; no automatic UAC retry occurs in this service lifetime.
            var code = exception switch
            {
                Win32Exception { NativeErrorCode: 1223 } => "advanced-consent-cancelled",
                OperationCanceledException => "collector-timeout",
                UnauthorizedAccessException => "collector-access-denied",
                _ => "advanced-start-failed"
            };
            _logger.LogWarning("Advanced sensor startup failed. ErrorCode={ErrorCode} ExceptionType={ExceptionType}", code, exception.GetType().Name);
            _lastAttempt = _time.GetUtcNow();
            var driverStatus = code is "advanced-consent-cancelled" or "collector-access-denied" ? "access-denied" : "blocked";
            _snapshot = new SystemSnapshot(_lastAttempt, "unavailable", [], driverStatus, code);
        }
    }

    private async Task StartAdvancedCollectorAsync(CancellationToken cancellationToken)
    {
        await StopCollectorAsync().ConfigureAwait(false);
        _advanced = true;
        _snapshot = null;
        try
        {
            // Windows still owns elevation consent, whether activation is explicit or restored at startup.
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
            deadline.CancelAfter(TimeSpan.FromSeconds(60));
            await _startCollector(deadline.Token).ConfigureAwait(false);
        }
        catch
        {
            await StopCollectorAsync().ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>Stops polling, closes the pipe and releases the only helper owned by this service.</summary>
    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        await _lifetime.CancelAsync().ConfigureAwait(false);
        await _trackingGate.WaitAsync().ConfigureAwait(false);
        try
        {
            await StopPollingAsync().ConfigureAwait(false);
            await _gate.WaitAsync().ConfigureAwait(false);
            try { await StopCollectorAsync().ConfigureAwait(false); }
            finally { _gate.Release(); }
            _lifetime.Dispose();
        }
        finally { _trackingGate.Release(); }
    }

    private void StartPollingIfRequested()
    {
        if (_disposed || !_trackingRequested || !_configuration.Enabled || _polling is not null) return;
        _polling = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _pollTask = PollAsync(_profile.CpuInterval, _polling.Token);
    }

    private async Task StopPollingAsync()
    {
        if (_polling is null) return;
        await _polling.CancelAsync().ConfigureAwait(false);
        if (_pollTask is not null) await _pollTask.ConfigureAwait(false);
        _polling.Dispose();
        _polling = null;
        _pollTask = null;
    }

    private async Task PollAsync(TimeSpan interval, CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                // A schedule change cancels the timer only. Disposal still cancels bounded IPC work.
                await CaptureAsync(_lifetime.Token).ConfigureAwait(false);
                // Delay from completion so native read time cannot make every second base-rate poll miss its cache boundary.
                await Task.Delay(interval, _time, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (ObjectDisposedException) when (_disposed) { }
    }

    private async ValueTask<SystemSnapshot> ReadCollectorAsync(CancellationToken cancellationToken)
    {
        if (RuntimeInformation.ProcessArchitecture != Architecture.X64)
            return new SystemSnapshot(_time.GetUtcNow(), "unsupported", [], "unsupported-architecture", "collector-architecture-unsupported");
        if (_pipe is null) await StartCollectorAsync(cancellationToken).ConfigureAwait(false);
        await HardwareTelemetryProtocol.WriteAsync(_pipe!, new HardwareCollectorRequest(HardwareTelemetryProtocol.Version, "sample", _profile.Key), cancellationToken).ConfigureAwait(false);
        return await HardwareTelemetryProtocol.ReadAsync<SystemSnapshot>(_pipe!, cancellationToken).ConfigureAwait(false);
    }

    private async Task StartCollectorAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_helperPath)) throw new FileNotFoundException("The packaged hardware collector is missing.");
        var pipeName = "WorkTrail.Hardware." + Guid.NewGuid().ToString("N");
        using var identity = WindowsIdentity.GetCurrent();
        var user = identity.User ?? throw new UnauthorizedAccessException("Windows user identity is unavailable.");
        var security = new PipeSecurity();
        security.SetAccessRuleProtection(true, false);
        security.SetOwner(user);
        security.AddAccessRule(new PipeAccessRule(user, PipeAccessRights.FullControl, AccessControlType.Allow));
        // A same-user ACL permits this user's elevated token without granting other users access.
        _pipe = NamedPipeServerStreamAcl.Create(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous, 4096, 4096, security);
        var start = new ProcessStartInfo
        {
            FileName = _helperPath,
            WorkingDirectory = Path.GetDirectoryName(_helperPath)!,
            UseShellExecute = _advanced,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden
        };
        start.ArgumentList.Add("--pipe");
        start.ArgumentList.Add(pipeName);
        start.ArgumentList.Add("--parent");
        start.ArgumentList.Add(Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        if (_advanced)
        {
            start.Verb = "runas";
            start.ArgumentList.Add("--advanced");
        }
        var launchTask = Task.Run(() => Process.Start(start), cancellationToken);
        try
        {
            _helper = await launchTask.WaitAsync(cancellationToken).ConfigureAwait(false)
                ?? throw new IOException("The hardware collector could not be started.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Windows consent can outlive this request: release the gate now and observe the
            // late launch without retaining its process handle. Its closed pipe expires on connect.
            _ = launchTask.ContinueWith(completed =>
            {
                if (completed.Status == TaskStatus.RanToCompletion) completed.Result?.Dispose();
                else if (completed.IsFaulted) _ = completed.Exception;
            }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            throw;
        }
        await _pipe.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);
        HardwareTelemetryProtocol.VerifyClientProcess(_pipe, _helper.Id);
        var hello = await HardwareTelemetryProtocol.ReadAsync<HardwareCollectorHello>(_pipe, cancellationToken).ConfigureAwait(false);
        if (hello.Version != HardwareTelemetryProtocol.Version || _advanced && !hello.Elevated)
            throw new InvalidDataException("Hardware collector version or privilege mode is invalid.");
    }

    private async Task StopCollectorAsync()
    {
        // A disconnected advanced session never causes a later background poll to request UAC.
        _advanced = false;
        if (_pipe is not null)
        {
            // Closing the parent pipe is terminal for the helper, including an elevated helper.
            await _pipe.DisposeAsync().ConfigureAwait(false);
            _pipe = null;
        }
        if (_helper is not null)
        {
            try
            {
                using var deadline = new CancellationTokenSource(TimeSpan.FromMilliseconds(750));
                await _helper.WaitForExitAsync(deadline.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Terminate only the exact child process this service launched, never unrelated sensor applications.
                try { if (!_helper.HasExited) _helper.Kill(); }
                catch (Win32Exception) { /* An elevated child will exit on pipe disconnect; the watchdog bounds blocked reads. */ }
            }
            catch (InvalidOperationException) { /* The child already exited before process observation. */ }
            finally { _helper.Dispose(); _helper = null; }
        }
    }

    private static string GetDriverStatus() => PawnIoInstaller.ReadInstalledVersion() is null ? "not-installed" : "available";
}
