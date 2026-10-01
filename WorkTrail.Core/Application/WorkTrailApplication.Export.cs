// SPDX-License-Identifier: MIT

using Microsoft.Extensions.Logging;
using WorkTrail.Services;

namespace WorkTrail.Application;

public sealed partial class WorkTrailApplication
{
    /// <inheritdoc />
    public Task<OperationResult<ReportExportSetup>> GetReportExportSetupAsync(CancellationToken cancellationToken) =>
        ExportOperationAsync(() => Task.Run(() => new ReportExportService(_store).Setup(), cancellationToken), cancellationToken);

    /// <inheritdoc />
    public Task<OperationResult<ReportExportPreview>> PreviewReportExportAsync(ReportExportOptions options, CancellationToken cancellationToken) =>
        ExportOperationAsync(() => Task.Run(() => ReportExportService.Preview(
            new ReportExportService(_store).Build(options, null, cancellationToken), cancellationToken), cancellationToken), cancellationToken);

    /// <inheritdoc />
    public Task<OperationResult<bool>> SaveReportExportPreferencesAsync(ReportExportOptions options, CancellationToken cancellationToken) =>
        MutateVisualStateAsync(() => ExportOperationAsync(() => Task.Run(() =>
        {
            new ReportExportService(_store).SavePreferences(options, cancellationToken);
            return true;
        }, cancellationToken), cancellationToken), cancellationToken);

    /// <inheritdoc />
    public Task<OperationResult<ReportExportResult>> ExportReportAsync(ReportExportRequest request, CancellationToken cancellationToken) =>
        MutateVisualStateAsync(() =>
        {
            // Check inside the serialized runtime operation before reading export data or touching the destination.
            if (!FeatureCatalog.IsAllowed(ProductFeature.ReportExport, _featureAccess.Snapshot))
                return Task.FromResult(OperationResult<ReportExportResult>.Failure("feature.premium_required", "Premium.Required"));
            return ExportOperationAsync(() => Task.Run(() =>
            {
                ArgumentNullException.ThrowIfNull(request);
                var document = new ReportExportService(_store).Build(request.Options, request.Summary, cancellationToken);
                return ReportExportWriter.Write(document, request.DestinationPath, request.Overwrite, cancellationToken);
            }, cancellationToken), cancellationToken);
        }, cancellationToken);

    /// <inheritdoc />
    public Task<OperationResult<ReportSummaryResult>> GenerateReportSummaryAsync(ReportSummaryRequest request, CancellationToken cancellationToken) =>
        RunCancellableLiveWorkAsync(token => MutateAsync(() => ExportOperationAsync(async () =>
            await RunLiveAnalysisOutsideMutationAsync(async () =>
            {
                var settings = _settingsSnapshot.Value;
                if (!settings.OpenAiEnabled || !TryValidateOpenAiConfiguration(settings, false, out var validated, out _))
                    throw new ReportExportValidationException("Export.AiNotReady");
                if (!BuildCostGate(validated).Allowed) throw new ReportExportValidationException("Export.AiDailyLimit");
                return await new ReportSummaryService(_store).GenerateAsync(request, validated,
                    // Live analysis already holds the visual gate. Re-entering it here deadlocks the summary window.
                    usage => MutateAsync(() =>
                    {
                        _store.AppendAiUsage(usage);
                        return Task.FromResult(OperationResult<bool>.Success("export.usage.saved", "Export.Completed", true));
                    }, CancellationToken.None), token).ConfigureAwait(false);
            }, token).ConfigureAwait(false), token), token), cancellationToken);

    private async Task<OperationResult<T>> ExportOperationAsync<T>(Func<Task<T>> operation, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try { return OperationResult<T>.Success("export.completed", "Export.Completed", await operation().ConfigureAwait(false)); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (ReportExportValidationException exception)
        {
            return exception.ActualLength is { } actualLength && exception.Limit is { } limit
                ? OperationResult<T>.Failure("export.invalid", exception.MessageKey,
                    new ValidationIssue("SummarySource", "export.summary_too_large", exception.MessageKey, actualLength, limit))
                : OperationResult<T>.Failure("export.invalid", exception.MessageKey);
        }
        catch (Exception exception)
        {
            // Paths, OCR, generated text and provider exception messages must not enter diagnostics or IPC errors.
            _logger.LogWarning("Export operation failed. ExceptionType={ExceptionType}", exception.GetType().Name);
            return OperationResult<T>.Failure("export.failed", "Export.Failed");
        }
    }
}
