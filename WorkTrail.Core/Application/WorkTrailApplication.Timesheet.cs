// SPDX-License-Identifier: MIT

using WorkTrail.Services;

namespace WorkTrail.Application;

public sealed partial class WorkTrailApplication
{
    private readonly SemaphoreSlim _timesheetGate = new(1, 1);

    /// <inheritdoc />
    public Task<OperationResult<ReportExportResult>> OpenReportFilePreviewAsync(ReportFilePreviewRequest request, CancellationToken cancellationToken) =>
        RunCancellableLiveWorkAsync(token => ExportOperationAsync(() => Task.Run(() =>
        {
            var result = ReportFilePreviewService.Create(_store, request, token);
            token.ThrowIfCancellationRequested();
            ReportFilePreviewService.Open(result.Path);
            return result;
        }, token), token), cancellationToken);

    /// <inheritdoc />
    public Task<OperationResult<TimesheetBatchView>> ManageTimesheetBatchAsync(TimesheetBatchCommand command, CancellationToken cancellationToken) =>
        RunCancellableLiveWorkAsync(token => ExportOperationAsync(async () =>
        {
            ArgumentNullException.ThrowIfNull(command);
            await _timesheetGate.WaitAsync(token).ConfigureAwait(false);
            try
            {
                if (command.Action is TimesheetBatchAction.Start or TimesheetBatchAction.Export
                    && !FeatureCatalog.IsAllowed(ProductFeature.ReportExport, _featureAccess.Snapshot))
                    throw new ReportExportValidationException("Premium.Required");
                // The dedicated job gate serializes files; HTTP never holds the global mutation or visual-analysis gate.
                return await Task.Run(() => new TimesheetBatchService(_store).ExecuteAsync(command, _settingsSnapshot.Value,
                    ReserveTimesheetUsageAsync, ReconcileTimesheetUsageAsync, token), token).ConfigureAwait(false);
            }
            finally { _timesheetGate.Release(); }
        }, token), cancellationToken);

    // job contains the exact request count being submitted.
    // token cancels before the reserved requests can be sent.
    private async Task ReserveTimesheetUsageAsync(TimesheetJob job, CancellationToken token)
    {
        await MutateAsync(() =>
        {
            token.ThrowIfCancellationRequested();
            var settings = _settingsSnapshot.Value;
            TimesheetBatchService.ValidateProvider(settings, requireEnabled: true);
            if (!FeatureCatalog.IsAllowed(ProductFeature.ReportExport, _featureAccess.Snapshot))
                throw new ReportExportValidationException("Premium.Required");
            var count = job.Rows.Count(row => row.Row.State != "no_sources");
            if (!BuildCostGate(settings).Allowed
                || (settings.OpenAiDailyLimit > 0 && (long)_store.GetTodayAnalysisCount() + count > settings.OpenAiDailyLimit))
                throw new ReportExportValidationException("Export.AiDailyLimit");
            foreach (var row in job.Rows.Where(row => row.Row.State != "no_sources"))
                _store.SaveTimesheetUsage(TimesheetUsage(job, row));
            return Task.FromResult(OperationResult<bool>.Success("timesheet.reserved", "Export.Completed", true));
        }, token).ConfigureAwait(false);
    }

    // job contains a durable result snapshot, allowing idempotent telemetry recovery after restart.
    // token cancels before reconciliation; stable IDs permit retry.
    private async Task ReconcileTimesheetUsageAsync(TimesheetJob job, CancellationToken token)
    {
        await MutateAsync(() =>
        {
            foreach (var row in job.Rows.Where(row => row.Row.State != "no_sources"))
                _store.SaveTimesheetUsage(TimesheetUsage(job, row));
            return Task.FromResult(OperationResult<bool>.Success("timesheet.usage.saved", "Export.Completed", true));
        }, token).ConfigureAwait(false);
    }

    // job identifies the paid submission and its original local quota date.
    // row identifies one remote request, independent of result ordering or polling count.
    private static AiRequestUsageRecord TimesheetUsage(TimesheetJob job, TimesheetWorkRow row) => new(
        $"{job.Id:N}.{row.Row.Id}", job.Id.ToString("N"), job.CreatedAt, job.ResultsSaved ? DateTimeOffset.UtcNow : null,
        "report.timesheet", "report_timesheet_batch", "openai", "api.openai.com", ReportSummaryModelPolicy.OpenAiModel,
        ReportSummaryModelPolicy.OpenAiModel, row.ResponseId, null, row.Row.State == "completed" ? 200 : null,
        null, null, 0, row.Prompt?.Length ?? 0, 1_000, row.Usage ?? new AiUsageMetrics(),
        job.ResultsSaved ? row.Row.State : null, row.Row.State == "completed", row.Row.State == "completed" ? null : "batch_" + row.Row.State);
}
