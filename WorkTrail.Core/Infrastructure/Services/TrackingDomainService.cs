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
using System.Diagnostics;
using System.Linq;
using WorkTrail.Application;

namespace WorkTrail.Services;

/// <summary>
/// Coordinates monitoring sessions and translates sample events into UI/dashboard state.
/// </summary>
public sealed class TrackingDomainService : IDisposable
{
    private readonly LocalStore _store;
    private readonly InputHookService _inputHooks = new();
    private readonly ActivityMonitorService _monitor;
    private readonly ActivityScoreService _activityScore = new();
    private readonly object _dashboardActivityCacheLock = new();
    private readonly Task<long> _dataDirectorySizeTask;
    private ActivitySample? _latestSample;
    private DateTimeOffset? _trackingStartedAt;
    private DashboardActivityCache? _dashboardActivityCache;
    private readonly SettingsSnapshot _settingsSnapshot;

    /// <summary>
    /// Initializes a new tracking domain service.
    /// </summary>
    /// <param name="store">Persistent store used for summaries and settings.</param>
    /// <param name="settingsSnapshot">Runtime-owned settings snapshot shared with the application facade.</param>
    public TrackingDomainService(LocalStore store, SettingsSnapshot? settingsSnapshot = null)
    {
        _store = store;
        _settingsSnapshot = settingsSnapshot ?? new SettingsSnapshot(store.LoadSettings());
        _monitor = new ActivityMonitorService(_store, _inputHooks, _settingsSnapshot);
        _monitor.SampleRecorded += HandleSampleRecorded;
        _monitor.SampleSuppressed += HandleSampleSuppressed;
        _monitor.RuntimeHealthChanged += HandleRuntimeHealthChanged;
        // Directory enumeration can be slow for long screenshot histories, so it starts once on a background thread.
        _dataDirectorySizeTask = Task.Run(() => CalculateDirectorySize(store.DataDirectory));
        _ = _dataDirectorySizeTask.ContinueWith(
            task => Trace.TraceWarning("WorkTrail data directory size calculation failed: {0}", task.Exception?.GetBaseException().Message),
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
        _ = _dataDirectorySizeTask.ContinueWith(
            _ => DashboardStateChanged?.Invoke(LoadCurrentDashboardState()),
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnRanToCompletion,
            TaskScheduler.Default);
    }

    public event Action<DashboardState>? DashboardStateChanged;

    /// <summary>Raised only when durable activity sampling enters or leaves degraded state.</summary>
    public event Action<TrackingRuntimeHealth>? RuntimeHealthChanged;

    /// <summary>
    /// Raised after tracking starts or stops. The value is true when tracking is active.
    /// </summary>
    public event Action<bool>? TrackingStateChanged;

    public bool IsTracking => _trackingStartedAt is not null;

    /// <summary>Gets the durable activity-sampling health without touching the activity database.</summary>
    public TrackingRuntimeHealth RuntimeHealth => _monitor.RuntimeHealth;

    public AnalysisContextSnapshot? LatestAnalysisContext => _latestSample is null
        ? null
        : ToAnalysisContext(_latestSample);

    /// <summary>Gets the process identity paired with <see cref="LatestAnalysisContext"/> for privacy checks.</summary>
    internal string? LatestProcessName => _latestSample?.ProcessName;

    /// <summary>
    /// Starts the input hooks and periodic sample collection when not already running.
    /// </summary>
    public void Start()
    {
        if (IsTracking)
        {
            // Keep start idempotent: callers may trigger Start multiple times without side effects.
            return;
        }

        try
        {
            _inputHooks.Start();
            _monitor.Start();
            _trackingStartedAt = DateTimeOffset.Now;
        }
        catch
        {
            // A partial hook/monitor start is rolled back so callers never see a false running state.
            _monitor.Stop();
            _inputHooks.Stop();
            throw;
        }
        TrackingStateChanged?.Invoke(true);
        DashboardStateChanged?.Invoke(LoadCurrentDashboardState());
    }

    /// <summary>
    /// Stops input hooks and sample collection safely.
    /// </summary>
    public void Stop()
    {
        if (!IsTracking)
        {
            return;
        }

        // Stop all collectors before clearing the session marker, so stale UI updates are avoided.
        _monitor.Stop();
        _inputHooks.Stop();
        _trackingStartedAt = null;
        TrackingStateChanged?.Invoke(false);
        DashboardStateChanged?.Invoke(LoadCurrentDashboardState());
    }

    /// <summary>
    /// Reads current values used by the main UI panel.
    /// </summary>
    public DashboardState LoadCurrentDashboardState()
    {
        var settings = _settingsSnapshot.Value;
        var sample = _latestSample;
        var status = IsTracking && sample?.State == "active" ? "RUNNING" : "PAUSED";
        var context = sample is null ? "STATE_READY" : sample.State == "idle" ? "STATE_IDLE" : $"{sample.Application} · {sample.Context}";
        var intensity = sample?.State == "active"
            ? Math.Min(100, 30 + sample.KeyPresses + sample.MouseClicks * 2)
            : IsTracking ? 5 : 5;
        var utcNow = DateTimeOffset.UtcNow;
        var activity = LoadDashboardActivityProjection(utcNow);

        return new DashboardState(
            status,
            context,
            activity.Summary.KeyPresses,
            activity.Summary.MouseClicks,
            activity.Summary.ActiveSeconds,
            intensity,
            IsTracking,
            sample?.Timestamp,
            utcNow.ToLocalTime(),
            utcNow,
            activity.Trend,
            ActivityScore: _activityScore.GetState(settings.ScreenshotIntervalMinutes, utcNow),
            SpanLabel: settings.SpanLabel)
        {
            CurrentApplicationIconPixels = sample?.State == "active" ? sample.ApplicationIconPixels : null,
            DataDirectorySizeBytes = _dataDirectorySizeTask.IsCompletedSuccessfully
                ? _dataDirectorySizeTask.Result
                : null
        };
    }

    private static long CalculateDirectorySize(string dataDirectory)
    {
        if (!Directory.Exists(dataDirectory))
        {
            return 0;
        }

        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = false,
            AttributesToSkip = FileAttributes.ReparsePoint,
            ReturnSpecialDirectories = false,
        };
        long totalBytes = 0;
        foreach (var path in Directory.EnumerateFiles(dataDirectory, "*", options))
        {
            try
            {
                totalBytes = checked(totalBytes + new FileInfo(path).Length);
            }
            catch (FileNotFoundException)
            {
                // Runtime-owned logs and temporary files may disappear while the snapshot is being measured.
            }
            catch (DirectoryNotFoundException)
            {
                // A retention operation may remove a directory after it has been enumerated.
            }
        }

        return totalBytes;
    }

    /// <summary>
    /// Loads the latest completed sample and its latest screenshot path from local history.
    /// </summary>
    public LastSessionState? LoadLastSessionState()
    {
        var sample = _store.LoadLatestSample();
        if (sample is null)
        {
            return null;
        }

        var screenshotPath = _store.LoadLatestPrimaryScreenshot();
        DateTimeOffset? screenshotCapturedAt = screenshotPath is not null && File.Exists(screenshotPath)
            ? new DateTimeOffset(File.GetLastWriteTimeUtc(screenshotPath), TimeSpan.Zero)
            : null;
        if (screenshotCapturedAt is null)
        {
            screenshotPath = null;
        }

        return new LastSessionState(sample.Timestamp, sample.Application, sample.Context, sample.InstallationId, sample.Attributes, screenshotPath, screenshotCapturedAt);
    }

    /// <summary>
    /// Loads application settings from local storage.
    /// </summary>
    public AppSettings LoadSettings() => _settingsSnapshot.Value;

    /// <summary>
    /// Persists provided application settings to local storage.
    /// </summary>
    /// <param name="settings">Settings instance to persist.</param>
    public void SaveSettings(AppSettings settings)
    {
        _store.SaveSettings(settings);
        _settingsSnapshot.Replace(settings);
    }

    /// <summary>Records application-owned telemetry for the live score without exposing OS access to the UI.</summary>
    public void RecordSystemSnapshot(SystemSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        _activityScore.RecordSystemSnapshot(snapshot);
        DashboardStateChanged?.Invoke(LoadCurrentDashboardState());
    }

    /// <summary>Calculates the telemetry averages that must be persisted with one screenshot.</summary>
    public ScreenshotIntervalTelemetry BuildScreenshotIntervalTelemetry(
        DateTimeOffset intervalStartedAt,
        DateTimeOffset capturedAt) =>
        _activityScore.BuildScreenshotIntervalTelemetry(intervalStartedAt, capturedAt);

    /// <summary>
    /// Handles each new sample and publishes updated UI state.
    /// </summary>
    /// <param name="sample">New sample to publish.</param>
    private void HandleSampleRecorded(ActivitySample sample)
    {
        _latestSample = sample;
        _activityScore.RecordSample(sample);
        UpdateDashboardActivityCache(sample);
        DashboardStateChanged?.Invoke(BuildDashboardState(sample));
    }

    private void HandleRuntimeHealthChanged(TrackingRuntimeHealth health) => RuntimeHealthChanged?.Invoke(health);

    private void HandleSampleSuppressed() => _latestSample = null;

    /// <summary>Persists one already-captured sample through the same contained timer path.</summary>
    /// <remarks>Used by focused runtime verification without starting global input hooks.</remarks>
    internal bool TryPersistActivitySample(ActivitySample sample) => _monitor.TryPersistSample(sample);

    /// <summary>
    /// Builds dashboard state from the latest sample and today's counters.
    /// </summary>
    /// <param name="sample">Sample currently in focus.</param>
    /// <returns>Computed dashboard representation.</returns>
    private DashboardState BuildDashboardState(ActivitySample sample)
    {
        var settings = _settingsSnapshot.Value;
        var status = sample.State == "active" ? "RUNNING" : "PAUSED";
        var context = sample.State == "idle" ? "STATE_IDLE" : $"{sample.Application} · {sample.Context}";
        var intensity = sample.State == "active" ? Math.Min(100, 30 + sample.KeyPresses + sample.MouseClicks * 2) : 5;
        var utcNow = DateTimeOffset.UtcNow;
        var activity = LoadDashboardActivityProjection(utcNow);

        return new DashboardState(
            status,
            context,
            activity.Summary.KeyPresses,
            activity.Summary.MouseClicks,
            activity.Summary.ActiveSeconds,
            intensity,
            true,
            sample.Timestamp,
            utcNow.ToLocalTime(),
            utcNow,
            activity.Trend,
            ActivityScore: _activityScore.GetState(settings.ScreenshotIntervalMinutes, utcNow),
            SpanLabel: settings.SpanLabel)
        {
            CurrentApplicationIconPixels = sample.State == "active" ? sample.ApplicationIconPixels : null
        };
    }

    private DashboardActivityProjection LoadDashboardActivityProjection(DateTimeOffset utcNow)
    {
        lock (_dashboardActivityCacheLock)
        {
            var localDate = DateOnly.FromDateTime(utcNow.ToLocalTime().DateTime);
            var revision = _store.ActivityRevision;
            if (_dashboardActivityCache is null ||
                _dashboardActivityCache.Revision != revision ||
                _dashboardActivityCache.LocalDate != localDate ||
                utcNow < _dashboardActivityCache.LastWindowEndUtc)
            {
                _dashboardActivityCache = LoadStableDashboardActivityCache(utcNow, localDate);
            }

            var cache = _dashboardActivityCache
                ?? throw new InvalidOperationException("Dashboard activity cache initialization did not complete.");
            var retentionBoundary = utcNow.AddHours(-26);
            var expiredCount = 0;
            while (expiredCount < cache.Samples.Count && cache.Samples[expiredCount].Timestamp <= retentionBoundary)
            {
                expiredCount++;
            }

            if (expiredCount > 0)
            {
                cache.Samples.RemoveRange(0, expiredCount);
            }

            var summary = cache.Summary;
            if (summary is null || cache.SummaryRevision != cache.Revision)
            {
                summary = LocalStore.BuildDailySummary(cache.Samples, localDate);
                cache.Summary = summary;
                cache.SummaryRevision = cache.Revision;
            }

            cache.LastWindowEndUtc = utcNow;
            return new DashboardActivityProjection(
                summary,
                LocalStore.Build24HourActivityTrend(cache.Samples, utcNow));
        }
    }

    private DashboardActivityCache LoadStableDashboardActivityCache(DateTimeOffset utcNow, DateOnly localDate)
    {
        while (true)
        {
            var revisionBeforeRead = _store.ActivityRevision;
            // One minimal SQLite projection seeds the cache; subsequent one-second reads remain in memory.
            var samples = _store.LoadDashboardActivitySamples(utcNow).ToList();
            var revisionAfterRead = _store.ActivityRevision;
            if (revisionBeforeRead == revisionAfterRead)
            {
                return new DashboardActivityCache(revisionAfterRead, localDate, utcNow, samples);
            }

            // A concurrent durable append makes this read ambiguous; retry from the new committed revision.
        }
    }

    private void UpdateDashboardActivityCache(ActivitySample sample)
    {
        lock (_dashboardActivityCacheLock)
        {
            if (_dashboardActivityCache is null)
            {
                return;
            }

            var revision = _store.ActivityRevision;
            if (revision == _dashboardActivityCache.Revision)
            {
                // A concurrent cache seed already observed this committed sample.
                return;
            }

            if (revision != _dashboardActivityCache.Revision + 1)
            {
                _dashboardActivityCache = null;
                return;
            }

            var projection = new ReportSourceSample(
                sample.Timestamp,
                sample.DurationSeconds,
                sample.State,
                sample.Application,
                sample.KeyPresses,
                sample.MouseClicks,
                sample.InstallationId);
            var samples = _dashboardActivityCache.Samples;
            if (samples.Count == 0 || samples[^1].Timestamp <= projection.Timestamp)
            {
                samples.Add(projection);
            }
            else
            {
                var insertionIndex = samples.BinarySearch(projection, DashboardSampleTimestampComparer.Instance);
                samples.Insert(insertionIndex < 0 ? ~insertionIndex : insertionIndex, projection);
            }

            _dashboardActivityCache.Revision = revision;
            _dashboardActivityCache.Summary = null;
        }
    }

    private sealed record DashboardActivityProjection(DailySummary Summary, ActivityTrendState Trend);

    private sealed class DashboardActivityCache(
        long revision,
        DateOnly localDate,
        DateTimeOffset lastWindowEndUtc,
        List<ReportSourceSample> samples)
    {
        internal long Revision { get; set; } = revision;
        internal DateOnly LocalDate { get; } = localDate;
        internal DateTimeOffset LastWindowEndUtc { get; set; } = lastWindowEndUtc;
        internal List<ReportSourceSample> Samples { get; } = samples;
        internal long SummaryRevision { get; set; } = -1;
        internal DailySummary? Summary { get; set; }
    }

    private sealed class DashboardSampleTimestampComparer : IComparer<ReportSourceSample>
    {
        internal static DashboardSampleTimestampComparer Instance { get; } = new();

        public int Compare(ReportSourceSample? left, ReportSourceSample? right) =>
            Nullable.Compare(left?.Timestamp, right?.Timestamp);
    }

    /// <summary>
    /// Converts the activity sample into a context object used by AI analysis.
    /// </summary>
    /// <param name="sample">Source sample.</param>
    /// <returns>Normalized AI input context.</returns>
    private static AnalysisContextSnapshot ToAnalysisContext(ActivitySample sample) => new(
        sample.Application,
        sample.Context,
        sample.WindowTitle,
        sample.State,
        FilterAnalysisAttributes(sample.Attributes));

    internal static IReadOnlyDictionary<string, string>? FilterAnalysisAttributes(IReadOnlyDictionary<string, string>? attributes)
    {
        if (attributes is null)
        {
            return null;
        }

        var safeAttributes = attributes
            .Where(attribute => !string.Equals(attribute.Key, ActivityAttributeKeys.SpanLabel, StringComparison.Ordinal))
            .ToDictionary(attribute => attribute.Key, attribute => attribute.Value, StringComparer.Ordinal);
        return safeAttributes.Count == 0 ? null : safeAttributes;
    }

    /// <summary>Evaluates current privacy rules against separated process and presentation context metadata.</summary>
    internal static bool IsHistoricalContextPrivate(
        AppSettings settings,
        string processName,
        AnalysisContextSnapshot context)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(context);
        return MatchesPersistedPrivacyRule(settings.PrivacyProcessNames, processName)
            || MatchesPersistedPrivacyRule(settings.PrivacyWindowTitles, context.WindowTitle)
            || MatchesPersistedPrivacyRule(settings.PrivacyWindowHints, context.Context);
    }

    /// <summary>Evaluates screenshot policy from the foreground metadata captured at the pixel boundary.</summary>
    internal static ScreenshotCaptureDecision EvaluateScreenshotCapture(
        AppSettings settings,
        ScreenshotCaptureContext context)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(context);
        if (!settings.ScreenshotsEnabled)
        {
            return ScreenshotCaptureDecision.ScreenshotsDisabled;
        }

        if (HasConfiguredPrivacyRules(settings)
            && string.IsNullOrWhiteSpace(context.ProcessName)
            && string.IsNullOrWhiteSpace(context.WindowTitle))
        {
            // Provider labels alone cannot prove a capture safe when Windows foreground metadata vanished.
            return ScreenshotCaptureDecision.PrivacyBlocked;
        }

        if (!string.IsNullOrWhiteSpace(settings.PrivacyWindowHints) && string.IsNullOrWhiteSpace(context.WindowTitle))
        {
            // Generic labels such as "No details" cannot prove a missing title-derived context safe.
            return ScreenshotCaptureDecision.PrivacyBlocked;
        }

        var analysisContext = new AnalysisContextSnapshot(
            context.ApplicationName,
            context.Context,
            context.WindowTitle,
            "active",
            Attributes: null);
        return IsHistoricalContextPrivate(settings, context.ProcessName, analysisContext)
            ? ScreenshotCaptureDecision.PrivacyBlocked
            : ScreenshotCaptureDecision.Allowed;
    }

    internal static bool HasConfiguredPrivacyRules(AppSettings settings) =>
        !string.IsNullOrWhiteSpace(settings.PrivacyProcessNames)
        || !string.IsNullOrWhiteSpace(settings.PrivacyWindowTitles)
        || !string.IsNullOrWhiteSpace(settings.PrivacyWindowHints);

    private static bool MatchesPersistedPrivacyRule(string serializedRules, string target)
    {
        if (string.IsNullOrWhiteSpace(serializedRules))
        {
            return false;
        }

        var rules = new List<string>();
        foreach (var rawRow in serializedRules.Split('\n'))
        {
            var row = rawRow.Trim();
            if (row.Length == 0)
            {
                continue;
            }

            var separator = row.IndexOf('|', StringComparison.Ordinal);
            if (separator <= 0
                || separator != row.LastIndexOf('|')
                || string.IsNullOrWhiteSpace(row[..separator])
                || string.IsNullOrWhiteSpace(row[(separator + 1)..]))
            {
                // Historical replay must not bypass a rule that cannot be interpreted safely.
                return true;
            }

            rules.Add(row[(separator + 1)..].Trim());
        }

        if (rules.Count == 0)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(target))
        {
            // A configured rule without its corresponding historical target is private by default.
            return true;
        }

        return rules.Any(rule => target.Contains(rule, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Disposes active hooks and unsubscribes events.
    /// </summary>
    public void Dispose()
    {
        _monitor.Stop();
        _inputHooks.Stop();
        _monitor.SampleRecorded -= HandleSampleRecorded;
        _monitor.SampleSuppressed -= HandleSampleSuppressed;
        _monitor.RuntimeHealthChanged -= HandleRuntimeHealthChanged;
    }
}
