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


using Microsoft.Extensions.Logging;
using WorkTrail.Services;

namespace WorkTrail.Application;

/// <summary>Provides system and hardware telemetry through the shared runtime facade.</summary>
public sealed partial class WorkTrailApplication
{
    private async Task<SystemSnapshot> CaptureAndRecordSystemSnapshotAsync(CancellationToken cancellationToken)
    {
        await _systemSnapshotGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // The sole collector provides bounded immutable readings; no secondary OS readers are used.
            var snapshot = await _snapshot.CaptureAsync(cancellationToken).ConfigureAwait(false);
            SystemSnapshotValidator.Validate(snapshot);
            _tracking.RecordSystemSnapshot(snapshot);
            return snapshot;
        }
        finally
        {
            _systemSnapshotGate.Release();
        }
    }

    /// <inheritdoc />
    public async Task<OperationResult<SystemSnapshot>> CaptureSystemSnapshotAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var settings = _settingsSnapshot.Value;
            var snapshot = await CaptureAndRecordSystemSnapshotAsync(cancellationToken).ConfigureAwait(false);
            var deviceContext = await _deviceContext.CaptureAsync(
                settings.OpenAiEnabled && settings.IncludeDeviceLocation, cancellationToken).ConfigureAwait(false);
            var scheduleNote = ActiveHoursSchedule.BuildInformationalNote(settings.ActiveHours, snapshot.Timestamp);
            return OperationResult<SystemSnapshot>.Success(
                "system.snapshot.captured",
                "SystemSnapshotCaptured",
                snapshot with { DeviceContext = deviceContext, InformationalSchedule = scheduleNote });
        }
        catch (Exception exception)
        {
            // OS telemetry can be unavailable; surface a stable failure without leaking host details.
            _logger.LogWarning("System snapshot capture failed. ExceptionType={ExceptionType}", exception.GetType().Name);
            return OperationResult<SystemSnapshot>.Failure("system.snapshot.failed", "SystemSnapshotFailed");
        }
    }

    /// <inheritdoc />
    public async Task<OperationResult<SystemSnapshot>> CaptureHardwareSnapshotAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            // The live window uses the sole collector/cache; no device context or location is captured here.
            var snapshot = await CaptureAndRecordSystemSnapshotAsync(cancellationToken).ConfigureAwait(false);
            return OperationResult<SystemSnapshot>.Success("hardware.snapshot.captured", "SystemSnapshotCaptured", snapshot);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception)
        {
            // An unavailable collector stays a visible failure; there is no second hardware runtime.
            _logger.LogWarning("Hardware snapshot capture failed. ExceptionType={ExceptionType}", exception.GetType().Name);
            return OperationResult<SystemSnapshot>.Failure("hardware.snapshot.failed", "SystemSnapshotFailed");
        }
    }

    /// <inheritdoc />
    public async Task<OperationResult<SystemSnapshot>> EnableAdvancedHardwareTelemetryAsync(CancellationToken cancellationToken)
    {
        try
        {
            // Explicit activation may install the driver or retry a session that was not restored at startup.
            await _snapshot.EnableAdvancedAsync(cancellationToken).ConfigureAwait(false);
            return await CaptureSystemSnapshotAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (PawnIoSetupException exception)
        {
            // Setup cancellation, failure, and a required restart each provide a specific next step.
            _logger.LogWarning("Advanced hardware setup incomplete. MessageKey={MessageKey}", exception.MessageKey);
            return OperationResult<SystemSnapshot>.Failure("hardware.advanced.setup.failed", exception.MessageKey);
        }
        catch (Exception exception)
        {
            _logger.LogWarning("Advanced hardware access failed. ExceptionType={ExceptionType}", exception.GetType().Name);
            return OperationResult<SystemSnapshot>.Failure("hardware.advanced.failed", "HardwareAdvancedFailed");
        }
    }
}
