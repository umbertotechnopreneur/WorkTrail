// SPDX-License-Identifier: MIT
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using WorkTrail.Services;

namespace WorkTrail.Application;

public sealed partial class WorkTrailApplication
{
    private readonly object _timesheetSessionGate = new();
    private Task _timesheetSessionWork = Task.CompletedTask;
    private bool _timesheetSystemEventsEnabled;

    private void InitializeTimesheetSessionEvents(bool enabled)
    {
        // Startup is exclusive, before this runtime starts timers or accepts commands.
        PersistTimesheetSessionSignal("runtime_start", DateTimeOffset.UtcNow);
        PersistTimesheetSessionSignal(_tracking.IsTracking ? "started" : "paused", DateTimeOffset.UtcNow);
        if (!enabled) return;
        try
        {
            SystemEvents.SessionSwitch += TimesheetSessionSwitch;
            SystemEvents.PowerModeChanged += TimesheetPowerChanged;
            _timesheetSystemEventsEnabled = true;
        }
        catch (Exception exception) { ReportTimesheetEvidenceFailure("session.subscription", exception); }
    }

    private void StopTimesheetSessionEvents()
    {
        if (!_timesheetSystemEventsEnabled) return;
        SystemEvents.SessionSwitch -= TimesheetSessionSwitch;
        SystemEvents.PowerModeChanged -= TimesheetPowerChanged;
        _timesheetSystemEventsEnabled = false;
    }

    private void TimesheetSessionSwitch(object sender, SessionSwitchEventArgs args)
    {
        var kind = args.Reason switch
        {
            SessionSwitchReason.SessionLock or SessionSwitchReason.ConsoleDisconnect or SessionSwitchReason.RemoteDisconnect => "locked",
            SessionSwitchReason.SessionUnlock or SessionSwitchReason.ConsoleConnect or SessionSwitchReason.RemoteConnect => "unlocked",
            SessionSwitchReason.SessionLogoff => "stopped",
            _ => null
        };
        if (kind is not null) QueueTimesheetSessionSignal(kind);
    }

    private void TimesheetPowerChanged(object sender, PowerModeChangedEventArgs args)
    {
        if (args.Mode == PowerModes.Suspend) QueueTimesheetSessionSignal("suspended");
        if (args.Mode == PowerModes.Resume) QueueTimesheetSessionSignal("resumed");
    }

    private void QueueTimesheetSessionSignal(string kind)
    {
        var at = DateTimeOffset.UtcNow;
        lock (_timesheetSessionGate)
        {
            if (_disposed) return;
            _timesheetSessionWork = _timesheetSessionWork.ContinueWith(async _ =>
            {
                await MutateAsync(() =>
                {
                    PersistTimesheetSessionSignal(kind, at);
                    return Task.FromResult(OperationResult<bool>.Success("timesheet.session.saved", "Export.Completed", true));
                }, CancellationToken.None).ConfigureAwait(false);
            }, CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default).Unwrap();
        }
    }

    private void PersistTimesheetSessionSignal(string kind, DateTimeOffset at)
    {
        try { _store.SaveTimesheetSessionEvent(new(Guid.NewGuid().ToString("N"), _settingsSnapshot.Value.InstallationId, at, kind)); }
        catch (Exception exception) { ReportTimesheetEvidenceFailure("session.persistence", exception); }
    }

    private void PersistTimesheetCaptureEvidence(ScreenshotCaptureResult capture, AppSettings settings)
    {
        try { _store.SaveTimesheetCaptureContext(capture.CaptureId, Math.Max(1, settings.ScreenshotIntervalMinutes) * 60, settings.SpanLabel); }
        catch (Exception exception) { ReportTimesheetEvidenceFailure("capture.context", exception); }
        PersistTimesheetCheckpoint(capture.UnchangedEvidence, settings);
    }

    private void PersistTimesheetCheckpoint(TimesheetUnchangedEvidence? evidence, AppSettings settings)
    {
        if (evidence is null || !settings.KeepScreenshots) return;
        try { _store.SaveTimesheetCheckpoint(evidence, settings); }
        catch (Exception exception) { ReportTimesheetEvidenceFailure("checkpoint.persistence", exception); }
    }

    private void ReportTimesheetEvidenceFailure(string code, Exception exception)
    {
        // Never log source paths, titles, labels, descriptions or exception messages.
        _logger.LogError("Timesheet evidence failed. Code={Code} ExceptionType={ExceptionType}", code, exception.GetType().Name);
        EnqueueNotification(new(Guid.NewGuid(), DateTimeOffset.UtcNow, ApplicationNotificationSeverity.Error,
            "Timesheet.EvidenceErrorTitle", "Timesheet.EvidenceError", "timesheet." + code, exception.GetType().Name));
    }
}
