// SPDX-License-Identifier: MIT
using WorkTrail.Application;

namespace WorkTrail.Services;

public sealed partial class LocalStore
{
    internal void SaveTimesheetCaptureContext(string captureId, int seconds, string project) =>
        _activity.SaveTimesheetCaptureContext(captureId, seconds, project);

    internal void SaveTimesheetCheckpoint(TimesheetUnchangedEvidence evidence, AppSettings settings)
    {
        var observation = _activity.CreateCheckpoint(evidence.AttemptId, settings.InstallationId, evidence.At,
            evidence.SourcePath, evidence.Application, evidence.WindowTitle, settings.SpanLabel,
            Math.Max(1, settings.ScreenshotIntervalMinutes) * 60);
        if (observation is not null) _activity.SaveTimesheetCheckpoint(observation);
    }

    internal void SaveTimesheetSessionEvent(TimesheetSessionEvent signal) => _activity.SaveTimesheetSessionEvent(signal);
    internal List<TimesheetSessionEvent> ReadTimesheetSessionEvents(DateTimeOffset from, DateTimeOffset to) =>
        _activity.ReadTimesheetSessionEvents(from, to);

    internal List<TimesheetObservation> ReadTimesheetObservations(TimesheetOptions options, CancellationToken token)
    {
        var sources = options.Sources;
        var settings = LoadSettings();
        var zone = TimeZoneInfo.FindSystemTimeZoneById(sources.Options.TimeZoneId);
        var from = ReportAggregationService.ConvertLocalBoundaryToUtc(sources.Options.From, zone);
        var to = ReportAggregationService.ConvertLocalBoundaryToUtc(sources.Options.ToInclusive.AddDays(1), zone);
        var contexts = _activity.ReadTimesheetCaptureContexts(from, to);
        var captures = new ReportExportService(this).ReadCaptures(sources.Options, token);
        var result = new List<TimesheetObservation>();
        foreach (var group in captures.Where(c => c.ImageMetadata?.ForAnalysis != false)
            .GroupBy(c => TryGetCaptureId(ScreenshotIdentity(Path.GetFileName(c.Path))), StringComparer.Ordinal))
        {
            token.ThrowIfCancellationRequested();
            if (group.Key is null) continue;
            var capture = group.OrderBy(c => c.CaptureKind == "active-window" ? 0 : 1)
                .FirstOrDefault(c => !string.IsNullOrWhiteSpace(c.AiDescriptionMarkdown));
            if (capture?.Installation is null || capture.AiAnalyzedAt is null) continue;
            var description = sources.IncludeCompleteDescription ? capture.AiDescriptionMarkdown!
                : sources.IncludeDescriptionExcerpt ? ReportExportService.Excerpt(ReportExportService.PlainText(capture.AiDescriptionMarkdown), 240) : "";
            var contextKnown = contexts.TryGetValue(group.Key, out var context);
            var labels = capture.SpanLabels ?? [];
            var hint = contextKnown ? context.Project : labels.OrderBy(l => Math.Abs((l.SampledAt - capture.CapturedAt).TotalSeconds)).FirstOrDefault()?.Label ?? "";
            var observation = new TimesheetObservation("obs-" + group.Key, capture.Installation.InstallationId,
                capture.CapturedAt.ToUniversalTime(), group.Key, group.Key, "capture", description,
                capture.ForegroundApplication, sources.IncludeWindowTitles ? capture.ForegroundWindowTitle : null,
                hint, contextKnown ? context.Seconds : 300, !contextKnown);
            if (!Private(capture.ForegroundApplication, capture.ForegroundWindowTitle ?? "", observation.Description)) result.Add(observation);
        }
        var retainedSources = captures.Where(c => c.ImageMetadata?.ForAnalysis != false)
            .Select(c => TryGetCaptureId(ScreenshotIdentity(Path.GetFileName(c.Path)))).OfType<string>().ToHashSet(StringComparer.Ordinal);
        foreach (var checkpoint in _activity.ReadTimesheetCheckpoints(from, to))
        {
            token.ThrowIfCancellationRequested();
            if (sources.Options.InstallationId is { } installation && checkpoint.InstallationId != installation) continue;
            // The reused source may precede the selected day; verify its durable retained metadata separately.
            if (!retainedSources.Contains(checkpoint.CaptureId)
                && GetScreenshotGalleryItemsForCaptures([checkpoint.CaptureId], token).All(c => c.ImageMetadata?.ForAnalysis == false)) continue;
            if (Private(checkpoint.Application, checkpoint.WindowTitle ?? "", checkpoint.Description)) continue;
            result.Add(checkpoint with
            {
                Description = sources.IncludeCompleteDescription ? checkpoint.Description
                    : sources.IncludeDescriptionExcerpt ? ReportExportService.Excerpt(ReportExportService.PlainText(checkpoint.Description), 240) : "",
                WindowTitle = sources.IncludeWindowTitles ? checkpoint.WindowTitle : null
            });
        }
        return result.Where(o => !string.IsNullOrWhiteSpace(o.Description) || !string.IsNullOrWhiteSpace(o.WindowTitle))
            .DistinctBy(o => o.Id).OrderBy(o => o.InstallationId, StringComparer.Ordinal).ThenBy(o => o.ObservedAt).ThenBy(o => o.Id, StringComparer.Ordinal).ToList();

        bool Private(string app, string title, string description) => TrackingDomainService.IsHistoricalContextPrivate(settings, app,
            new AnalysisContextSnapshot(app, description, title, "active", null));
    }
}
