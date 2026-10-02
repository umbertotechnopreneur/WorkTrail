// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using WorkTrail.Application;

namespace WorkTrail.Services;

/// <summary>
/// Handles current-format SQLite activity history and local settings persistence.
/// </summary>
public sealed class LocalStore
{
    private readonly UtilityService _utilities = new();
    private readonly SqliteActivityStore _activity;
    private readonly string _dataDirectory;
    private readonly string _settingsPath;
    private readonly string _settingsBootstrapMutexName;
    private readonly object _fileLock = new();
    private readonly SemaphoreSlim _screenshotProjectionGate = new(1, 1);
    private readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);
    private long _activityRevision;

    /// <summary>
    /// Initializes persistence file paths in the user app data folder.
    /// </summary>
    /// <param name="dataDirectory">Optional data directory override intended for isolated hosts and tests.</param>
    public LocalStore(string? dataDirectory = null)
    {
        var resolvedDataDirectory = string.IsNullOrWhiteSpace(dataDirectory)
            ? _utilities.AppDataDirectory
            : Path.GetFullPath(dataDirectory);
        Directory.CreateDirectory(resolvedDataDirectory);
        _dataDirectory = resolvedDataDirectory;
        var unsupportedLegacyPath = new[]
        {
            Path.Combine(resolvedDataDirectory, "activity.jsonl"),
            Path.Combine(resolvedDataDirectory, "analyses.jsonl")
        }.FirstOrDefault(File.Exists);
        if (unsupportedLegacyPath is not null)
        {
            // Greenfield storage is intentional: never import, read, delete, or silently ignore legacy history.
            throw new InvalidOperationException($"Legacy storage '{Path.GetFileName(unsupportedLegacyPath)}' is not supported; remove it before starting WorkTrail.");
        }

        _settingsPath = Path.Combine(resolvedDataDirectory, "appsettings.json");
        var settingsFingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(_settingsPath.ToUpperInvariant())))[..32];
        _settingsBootstrapMutexName = $"Local\\WorkTrail.Settings.{settingsFingerprint}";
        var settings = LoadSettings();
        var activityPath = Path.Combine(resolvedDataDirectory, SqliteActivityStore.DatabaseFileName);
        var activityExisted = File.Exists(activityPath);
        _activity = new SqliteActivityStore(activityPath);
        InitializeInstallationMetadata(settings, activityExisted);
    }

    /// <summary>
    /// Appends one activity sample to the SQLite activity store.
    /// </summary>
    public void AppendSample(ActivitySample sample)
    {
        _activity.Append(sample);
        // The revision changes only after SQLite commits, so readers can safely refresh from durable state.
        Interlocked.Increment(ref _activityRevision);
    }

    /// <summary>Gets the in-process revision of the durable activity rows used by the live dashboard cache.</summary>
    internal long ActivityRevision => Interlocked.Read(ref _activityRevision);

    /// <summary>Invalidates in-process projections after the import and its search marker commit together.</summary>
    internal void NotifyHistoryImportCommitted()
    {
        // This notification must remain memory-only: post-commit I/O cannot change the import's durable outcome.
        Interlocked.Increment(ref _activityRevision);
    }

    /// <summary>Gets the dedicated directory used by the reconstructible Lucene search index.</summary>
    internal string SearchIndexRootDirectory => Path.Combine(_dataDirectory, "search");

    /// <summary>Gets the absolute root containing all application-owned local data.</summary>
    internal string DataDirectory => _dataDirectory;

    /// <summary>Gets the absolute path of the current SQLite history store.</summary>
    internal string ActivityDatabasePath => _activity.DatabasePath;

    /// <summary>Imports the reviewed calendar asset into normalized tables of the existing activity database.</summary>
    internal void EnsureCelestialCalendar(CelestialCalendarDataset dataset) => _activity.EnsureCelestialCalendar(dataset);

    /// <summary>Reads national calendar entries for a bounded local-date interval.</summary>
    internal IReadOnlyList<CelestialCalendarHoliday> LoadCalendarHolidays(DateOnly start, DateOnly end) =>
        _activity.LoadCalendarHolidays(start, end);

    /// <summary>Reads fixed-date Latin sanctoral entries from the activity database.</summary>
    internal IReadOnlyList<CelestialCalendarSaint> LoadCalendarSaints(DateOnly date) =>
        _activity.LoadCalendarSaints(date);

    /// <summary>Lists local and imported installation profiles without exposing settings persistence.</summary>
    internal IReadOnlyList<InstallationProfile> GetInstallationProfiles()
    {
        var currentInstallationId = LoadSettings().InstallationId;
        return _activity.ListInstallationProfiles(currentInstallationId);
    }

    /// <summary>Loads one installation profile and marks it when it owns this runtime.</summary>
    internal InstallationProfile? GetInstallationProfile(string installationId)
    {
        var currentInstallationId = LoadSettings().InstallationId;
        return _activity.LoadInstallationProfile(installationId, currentInstallationId);
    }

    /// <summary>Persists a validated optimistic profile update.</summary>
    internal InstallationProfile SaveInstallationProfile(InstallationProfile profile, long previousRevision)
    {
        var saved = _activity.SaveInstallationProfile(profile with { IsCurrent = false }, previousRevision);
        return saved with
        {
            IsCurrent = string.Equals(saved.InstallationId, LoadSettings().InstallationId, StringComparison.Ordinal)
        };
    }

    /// <summary>Registers immutable screenshot capture provenance before dependent telemetry is persisted.</summary>
    internal void RegisterScreenshotCapture(
        string captureId,
        string installationId,
        DateTimeOffset capturedAt,
        string origin) =>
        _activity.RegisterScreenshotCapture(captureId, installationId, capturedAt, origin);

    private void InitializeInstallationMetadata(AppSettings settings, bool activityDatabaseExisted)
    {
        var observedAt = DateTimeOffset.UtcNow;
        var firstSeenAt = activityDatabaseExisted
            ? _activity.LoadEarliestInstallationHistoryTimestamp(settings.InstallationId) ?? observedAt
            : observedAt;
        var profile = InstallationProfileCatalog.CreateDefault(
            settings.InstallationId,
            _utilities.GetMachineName(),
            firstSeenAt) with
        {
            UpdatedAt = firstSeenAt > observedAt ? firstSeenAt : observedAt
        };
        _activity.EnsureCurrentInstallationProfile(profile);
    }

    /// <summary>Persists raw local OCR and optional AI refinement for one owned screenshot source.</summary>
    internal void UpsertScreenshotTextSnapshot(string captureId, ScreenshotTextSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (!ScreenCaptureService.IsOwnedArtifact(snapshot.SourceScreenshotPath))
        {
            throw new ArgumentException("Screenshot text can only reference a WorkTrail-owned artifact.", nameof(snapshot));
        }

        _activity.UpsertScreenshotTextSnapshot(
            ScreenshotIdentity(Path.GetFileName(snapshot.SourceScreenshotPath)),
            captureId,
            snapshot);
    }

    /// <summary>Loads raw OCR and optional AI refinement for one owned screenshot artifact.</summary>
    internal ScreenshotTextSnapshot? LoadScreenshotTextSnapshot(string screenshotPath)
    {
        if (!ScreenCaptureService.IsOwnedArtifact(screenshotPath))
        {
            return null;
        }

        return _activity.LoadScreenshotTextSnapshot(ScreenshotIdentity(Path.GetFileName(screenshotPath)));
    }

    /// <summary>Deletes OCR and AI text refinement data for one owned screenshot artifact.</summary>
    internal int DeleteScreenshotTextSnapshot(string screenshotPath)
    {
        if (!ScreenCaptureService.IsOwnedArtifact(screenshotPath))
        {
            return 0;
        }

        return _activity.DeleteScreenshotTextSnapshot(ScreenshotIdentity(Path.GetFileName(screenshotPath)));
    }

    /// <summary>Persists immutable hardware evidence for one already-registered screenshot capture.</summary>
    internal void UpsertCaptureHardwareSnapshot(string captureId, SystemSnapshot snapshot) =>
        _activity.UpsertCaptureHardwareSnapshot(captureId, snapshot);

    /// <summary>Loads retained hardware evidence without sampling the current machine.</summary>
    internal SystemSnapshot? LoadCaptureHardwareSnapshot(string captureId) =>
        _activity.LoadCaptureHardwareSnapshot(captureId);

    /// <summary>Persists the same capture-interval telemetry for every retained artifact in one screenshot pass.</summary>
    internal void UpsertScreenshotIntervalTelemetry(
        string captureId,
        IReadOnlyList<string> screenshotPaths,
        ScreenshotIntervalTelemetry telemetry,
        SystemSnapshot? hardwareSnapshot = null)
    {
        if (string.IsNullOrWhiteSpace(captureId) || screenshotPaths.Count == 0)
        {
            throw new ArgumentException("Screenshot telemetry requires a capture identifier and retained artifacts.");
        }

        var distinctPaths = screenshotPaths.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var origins = new HashSet<string>(StringComparer.Ordinal);
        foreach (var path in distinctPaths)
        {
            if (!ScreenCaptureService.IsOwnedArtifact(path))
            {
                throw new ArgumentException("Screenshot telemetry can only reference WorkTrail-owned artifacts.", nameof(screenshotPaths));
            }

            var artifactIdentity = ScreenshotIdentity(Path.GetFileName(path));
            if (!string.Equals(TryGetCaptureId(artifactIdentity), captureId, StringComparison.Ordinal))
            {
                throw new InvalidDataException("Screenshot telemetry capture identity does not match its artifact names.");
            }

            origins.Add(GetCaptureOrigin(Path.GetFileName(path)));
        }

        if (origins.Count != 1)
        {
            throw new InvalidDataException("Screenshot telemetry artifacts contain conflicting capture origins.");
        }

        _activity.UpsertScreenshotCaptureTelemetry(
            captureId,
            LoadSettings().InstallationId,
            origins.Single(),
            distinctPaths.Select(path => ScreenshotIdentity(Path.GetFileName(path))).ToArray(),
            telemetry,
            hardwareSnapshot);
    }

    /// <summary>Loads persisted interval telemetry for one retained screenshot.</summary>
    internal ScreenshotIntervalTelemetry? LoadScreenshotIntervalTelemetry(string screenshotPath) =>
        ScreenCaptureService.IsOwnedArtifact(screenshotPath)
            ? _activity.LoadScreenshotIntervalTelemetry(ScreenshotIdentity(Path.GetFileName(screenshotPath)))
            : null;

    /// <summary>Loads the last successfully persisted screenshot boundary.</summary>
    internal DateTimeOffset? LoadLatestScreenshotTelemetryCapturedAt() =>
        _activity.LoadLatestScreenshotTelemetryCapturedAt();

    /// <summary>Lists historical screenshot captures without AI descriptions inside a half-open UTC interval.</summary>
    internal IReadOnlyList<AiScreenshotReprocessCandidate> ListAiReprocessCandidates(
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        CancellationToken cancellationToken)
    {
        if (fromUtc >= toUtc)
        {
            throw new ArgumentOutOfRangeException(nameof(toUtc), "The screenshot reprocessing interval must be positive.");
        }

        var settings = LoadSettings();
        var retainedByIdentity = EnumerateRetainedScreenshotArtifacts(settings, cancellationToken);
        var retainedByCapture = retainedByIdentity
            .Select(entry => new { entry.Key, entry.Value, CaptureId = TryGetCaptureId(entry.Key) })
            .Where(entry => entry.CaptureId is not null)
            .GroupBy(entry => entry.CaptureId!, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.ToDictionary(entry => entry.Key, entry => entry.Value, StringComparer.OrdinalIgnoreCase),
                StringComparer.Ordinal);
        var persisted = _activity.ListScreenshotCapturesForAiReprocessing(fromUtc, toUtc, cancellationToken);
        var persistedStates = _activity.LoadAiReprocessCaptureStates(retainedByCapture.Keys, cancellationToken);
        var records = new List<AiReprocessCatalogRecord>(persisted.Count + retainedByCapture.Count);
        foreach (var record in persisted)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var identities = retainedByCapture.TryGetValue(record.CaptureId, out var retained)
                ? record.ArtifactIdentities.Concat(retained.Keys).Distinct(StringComparer.OrdinalIgnoreCase).ToArray()
                : record.ArtifactIdentities;
            records.Add(record with { ArtifactIdentities = identities });
        }

        var persistedCaptureIds = persisted.Select(record => record.CaptureId).ToHashSet(StringComparer.Ordinal);
        foreach (var retained in retainedByCapture)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (persistedCaptureIds.Contains(retained.Key))
            {
                continue;
            }

            persistedStates.TryGetValue(retained.Key, out var state);
            if (state?.CapturedAt is not null)
            {
                // Persisted telemetry is the capture-time source of truth; a changed file timestamp cannot move it to another day.
                continue;
            }

            var capturedAt = retained.Value.Values.Max(file => new DateTimeOffset(file.LastWriteTimeUtc, TimeSpan.Zero));
            if (capturedAt < fromUtc || capturedAt >= toUtc)
            {
                continue;
            }

            records.Add(new AiReprocessCatalogRecord(
                retained.Key,
                InstallationId: null,
                capturedAt,
                capturedAt,
                retained.Value.Keys.Order(StringComparer.OrdinalIgnoreCase).ToArray(),
                HasTelemetry: false,
                state?.HasAiDescription ?? false));
        }

        return MaterializeAiReprocessCandidates(
            records.Where(record => !record.HasAiDescription).ToArray(),
            retainedByIdentity,
            cancellationToken);
    }

    /// <summary>Loads one historical screenshot capture, including its current AI-description state.</summary>
    internal AiScreenshotReprocessCandidate? LoadAiReprocessCandidate(
        string captureId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var record = _activity.LoadScreenshotCapture(captureId);
        return record is null
            ? null
            : MaterializeAiReprocessCandidates(
                [record],
                ResolveRetainedScreenshotArtifacts(LoadSettings(), record.ArtifactIdentities, cancellationToken),
                cancellationToken)[0];
    }

    /// <summary>Checks whether a successful AI description is already linked to one capture.</summary>
    internal bool HasAiDescription(string captureId) => _activity.HasAiDescription(captureId);

    /// <summary>Creates one durable AI screenshot reprocessing job and its frozen item plan.</summary>
    internal void CreateAiReprocessJob(
        AiReprocessJobRecord job,
        IReadOnlyList<AiReprocessJobItemRecord> items) =>
        _activity.CreateAiReprocessJob(job, items);

    /// <summary>Loads one durable AI screenshot reprocessing job.</summary>
    internal AiReprocessJobRecord? LoadAiReprocessJob(Guid jobId) => _activity.LoadAiReprocessJob(jobId);

    /// <summary>Loads the single non-terminal AI screenshot reprocessing job.</summary>
    internal AiReprocessJobRecord? LoadActiveAiReprocessJob() => _activity.LoadActiveAiReprocessJob();

    /// <summary>Lists the frozen checkpoint items for one AI screenshot reprocessing job.</summary>
    internal IReadOnlyList<AiReprocessJobItemRecord> ListAiReprocessJobItems(Guid jobId) =>
        _activity.ListAiReprocessJobItems(jobId);

    /// <summary>Loads the next pending AI screenshot reprocessing item.</summary>
    internal AiReprocessJobItemRecord? LoadNextAiReprocessItem(Guid jobId) =>
        _activity.LoadNextAiReprocessJobItem(jobId);

    /// <summary>Transitions the top-level AI screenshot reprocessing checkpoint.</summary>
    internal void TransitionAiReprocessJob(
        Guid jobId,
        string state,
        string? pauseReason,
        DateTimeOffset updatedAt) =>
        _activity.UpdateAiReprocessJobState(jobId, state, pauseReason, updatedAt);

    /// <summary>Transitions one AI screenshot reprocessing work-item checkpoint.</summary>
    internal void TransitionAiReprocessItem(
        Guid jobId,
        string captureId,
        string state,
        int attemptCount,
        string? lastCode,
        DateTimeOffset updatedAt) =>
        _activity.UpdateAiReprocessJobItemState(jobId, captureId, state, attemptCount, lastCode, updatedAt);

    /// <summary>Converts an interrupted running item into a resumable paused checkpoint.</summary>
    internal void RecoverInterruptedAiReprocessJob(Guid jobId, DateTimeOffset updatedAt) =>
        _activity.RecoverInterruptedAiReprocessJob(jobId, updatedAt);

    /// <summary>Prunes completed historical-reprocessing checkpoints outside screenshot retention.</summary>
    internal int PruneTerminalAiReprocessJobs(DateTimeOffset screenshotCutoffUtc) =>
        _activity.DeleteTerminalAiReprocessJobsBefore(screenshotCutoffUtc);

    /// <summary>Prunes expired capture provenance after all retained references have disappeared.</summary>
    internal int PruneOrphanedScreenshotCaptures(DateTimeOffset screenshotCutoffUtc) =>
        _activity.DeleteOrphanedScreenshotCapturesBefore(screenshotCutoffUtc);

    /// <summary>Deletes persisted interval telemetry for one retained screenshot.</summary>
    internal int DeleteScreenshotIntervalTelemetry(string screenshotPath) =>
        ScreenCaptureService.IsOwnedArtifact(screenshotPath)
            ? _activity.DeleteScreenshotIntervalTelemetry(ScreenshotIdentity(Path.GetFileName(screenshotPath)))
            : 0;

    /// <summary>Visits every retained activity sample with its stable local identifier.</summary>
    internal void VisitAllActivitySamples(Action<long, ActivitySample> visitor, CancellationToken cancellationToken) =>
        _activity.VisitAllActivitySamples(visitor, cancellationToken);

    /// <summary>Visits every durable screenshot text snapshot for search-index rebuilds.</summary>
    internal void VisitScreenshotTextSnapshots(
        Action<string, string, ScreenshotTextSnapshot> visitor,
        CancellationToken cancellationToken) =>
        _activity.VisitScreenshotTextSnapshots(visitor, cancellationToken);

    /// <summary>Visits every persisted successful AI analysis for search-index rebuilds.</summary>
    internal void VisitAllAiAnalyses(Action<AiAnalysis> visitor, CancellationToken cancellationToken) =>
        _activity.VisitAllAiAnalyses(visitor, cancellationToken);

    /// <summary>Returns the latest durable revision of all supported search sources.</summary>
    internal long GetSearchSourceRevision() => _activity.GetSearchSourceRevision();

    /// <summary>Loads an ordered bounded range of search-source changes.</summary>
    internal IReadOnlyList<SearchSourceChange> LoadSearchSourceChanges(long afterRevision, int limit) =>
        _activity.LoadSearchSourceChanges(afterRevision, limit);

    /// <summary>Marks a non-database search source change that requires a full projection rebuild.</summary>
    internal long MarkSearchSourceRebuild(string reason) => _activity.MarkSearchSourceRebuild(reason);

    /// <summary>Prunes durable changes older than the committed search checkpoint.</summary>
    internal int PruneSearchSourceChanges(long throughRevision) =>
        _activity.PruneSearchSourceChanges(throughRevision);

    /// <summary>Loads one retained activity sample for incremental search projection.</summary>
    internal ActivitySample? LoadActivitySample(long id) => _activity.LoadActivitySample(id);

    /// <summary>Loads one retained successful analysis for incremental search projection.</summary>
    internal AiAnalysis? LoadAiAnalysis(string correlationId) => _activity.LoadAiAnalysis(correlationId);

    /// <summary>Loads OCR snapshots for one capture without scanning unrelated captures.</summary>
    internal IReadOnlyDictionary<string, ScreenshotTextSnapshot> LoadScreenshotTextSnapshotsForCapture(
        string captureId,
        CancellationToken cancellationToken) =>
        _activity.LoadScreenshotTextSnapshotsForCapture(captureId, cancellationToken);

    /// <summary>Projects requested captures together, scanning each affected day once and loading only their metadata.</summary>
    internal IReadOnlyList<ScreenshotGalleryItem> GetScreenshotGalleryItemsForCaptures(
        IReadOnlyCollection<string> captureIds, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(captureIds);
        cancellationToken.ThrowIfCancellationRequested();
        if (captureIds.Count == 0) return [];
        foreach (var captureId in captureIds) ArgumentException.ThrowIfNullOrWhiteSpace(captureId);
        var provenance = _activity.LoadScreenshotCaptures(captureIds);
        var items = new List<ScreenshotGalleryItem>();
        _screenshotProjectionGate.Wait(cancellationToken);
        try
        {
            // A missing capture is a deleted source. Filter before deserializing OCR or reading
            // activity intervals so unrelated screenshots cannot multiply each index update.
            foreach (var day in provenance.Values.GroupBy(capture => DateOnly.FromDateTime(capture.CapturedAt.LocalDateTime)))
            {
                var selected = day.Select(capture => capture.CaptureId).ToHashSet(StringComparer.Ordinal);
                items.AddRange(GetScreenshotGalleryCore(day.Key, cancellationToken, selected).Items);
            }
        }
        finally { _screenshotProjectionGate.Release(); }
        return items;
    }

    /// <summary>
    /// Resolves persisted capture timestamps for retained screenshot paths. Missing entries identify
    /// true orphaned artifacts whose filesystem timestamp may be used only as a fallback policy.
    /// </summary>
    internal IReadOnlyDictionary<string, DateTimeOffset> LoadScreenshotCaptureTimes(
        IEnumerable<string> screenshotPaths,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(screenshotPaths);
        var pathsByCaptureId = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var path in screenshotPaths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var identity = ScreenshotIdentity(Path.GetFileName(path));
            var captureId = TryGetCaptureId(identity);
            if (captureId is null)
            {
                continue;
            }

            if (!pathsByCaptureId.TryGetValue(captureId, out var paths))
            {
                paths = [];
                pathsByCaptureId.Add(captureId, paths);
            }

            paths.Add(path);
        }

        if (pathsByCaptureId.Count == 0)
        {
            return new Dictionary<string, DateTimeOffset>(StringComparer.OrdinalIgnoreCase);
        }

        var provenance = _activity.LoadScreenshotCaptures(pathsByCaptureId.Keys);
        var timestamps = new Dictionary<string, DateTimeOffset>(StringComparer.OrdinalIgnoreCase);
        foreach (var (captureId, paths) in pathsByCaptureId)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!provenance.TryGetValue(captureId, out var capture))
            {
                continue;
            }

            foreach (var path in paths)
            {
                timestamps[path] = capture.CapturedAt;
            }
        }

        return timestamps;
    }

    /// <summary>Loads one OCR snapshot by its already-normalized stable artifact identity.</summary>
    internal ScreenshotTextSnapshot? LoadScreenshotTextSnapshotByIdentity(string artifactIdentity) =>
        _activity.LoadScreenshotTextSnapshot(artifactIdentity);

    /// <summary>Persists one sanitized standalone AI request-usage record in SQLite.</summary>
    internal void AppendAiUsage(AiRequestUsageRecord usage) => _activity.AppendStandaloneAiRequest(usage);

    /// <summary>Replaces the cached AI pricing rows for one provider.</summary>
    internal void ReplaceAiModelPricing(string provider, IReadOnlyList<AiModelPricing> prices) =>
        _activity.ReplaceAiModelPricing(provider, prices);

    /// <summary>Lists cached AI model prices for one provider.</summary>
    internal IReadOnlyList<AiModelPricing> ListAiModelPricing(string provider) =>
        _activity.ListAiModelPricing(provider);

    /// <summary>Gets the newest cached AI pricing timestamp for one provider.</summary>
    internal DateTimeOffset? GetLatestAiModelPricingRetrievedAt(string provider) =>
        _activity.GetLatestAiModelPricingRetrievedAt(provider);

    /// <summary>Persists provider usage and the corresponding analysis in one SQLite transaction.</summary>
    internal void AppendAiAnalysisAndUsage(AiRequestUsageRecord usage, AiAnalysis analysis)
    {
        // The current SQLite schema owns the correlated request/result pair.
        _activity.AppendSuccessfulAiRequestAndAnalysis(usage, analysis);
    }

    /// <summary>
    /// Loads application settings and rejects malformed persisted configuration.
    /// </summary>
    public AppSettings LoadSettings() => WithSettingsMutex(() =>
    {
        var settings = new AppSettings();
        if (File.Exists(_settingsPath))
        {
            var document = JsonSerializer.Deserialize<JsonObject>(File.ReadAllText(_settingsPath), _json)
                ?? throw new InvalidOperationException("The WorkTrail settings file must contain a JSON object.");
            RejectRetiredReportSettings(document);
            settings = document.Deserialize<AppSettings>(_json)
                ?? throw new InvalidOperationException("The WorkTrail settings file must contain a JSON object.");
        }

        var normalized = SettingsCatalog.NormalizePersisted(settings, _utilities.GetDefaultScreenshotDirectory());
        return EnsureInstallationId(normalized);
    });

    /// <summary>Rejects persisted settings that require a removed report-feature migration.</summary>
    private static void RejectRetiredReportSettings(JsonObject settings)
    {
        foreach (var property in settings)
        {
            if (property.Key.Equals("dailyDigestEnabled", StringComparison.OrdinalIgnoreCase)
                || property.Key.Equals("dailyDigestDirectory", StringComparison.OrdinalIgnoreCase)
                || property.Key.Equals("lastDailyDigestDate", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"The WorkTrail settings file contains unsupported retired report setting '{property.Key}'.");
            }

            if ((property.Key.Equals("windowStates", StringComparison.OrdinalIgnoreCase)
                    || property.Key.Equals("windowOpenStates", StringComparison.OrdinalIgnoreCase))
                && property.Value is JsonObject windows
                && windows.Any(window => window.Key.Equals("reports", StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidOperationException(
                    "The WorkTrail settings file contains unsupported retired report window state.");
            }
        }
    }

    /// <summary>
    /// Reads the persisted installation identifier without creating or rewriting settings.
    /// </summary>
    /// <returns>The existing installation identifier, or null when settings have not been initialized by the runtime.</returns>
    public string? TryLoadInstallationId()
    {
        if (!File.Exists(_settingsPath))
        {
            return null;
        }

        var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_settingsPath), _json)
            ?? throw new InvalidOperationException("The WorkTrail settings file must contain a JSON object.");
        if (!IsValidInstallationId(settings.InstallationId))
        {
            throw new InvalidOperationException("The WorkTrail settings file has an invalid installation identity.");
        }

        return settings.InstallationId;
    }

    /// <summary>
    /// Persists application settings to JSON file.
    /// </summary>
    /// <param name="settings">Settings payload.</param>
    public void SaveSettings(AppSettings settings) => WithSettingsMutex(() =>
    {
        var normalized = SettingsCatalog.NormalizePersisted(settings, _utilities.GetDefaultScreenshotDirectory());
        var payload = EnsureInstallationId(normalized);
        WriteSettingsFile(payload);
        return true;
    });

    /// <summary>Writes a fully normalized settings snapshot using an atomic same-directory replacement.</summary>
    private void WriteSettingsFile(AppSettings payload)
    {
        var temporaryPath = _settingsPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        lock (_fileLock)
        {
            // Write and flush in the target directory so a completed replacement is never a torn JSON file.
            using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var writer = new StreamWriter(stream))
            {
                writer.Write(JsonSerializer.Serialize(payload, _json));
                writer.Flush();
                stream.Flush(flushToDisk: true);
            }

            try
            {
                if (File.Exists(_settingsPath))
                {
                    File.Replace(temporaryPath, _settingsPath, null, ignoreMetadataErrors: true);
                }
                else
                {
                    File.Move(temporaryPath, _settingsPath);
                }
            }
            finally
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }
        }
    }

    /// <summary>
    /// Reads an environment variable from process/user/machine scopes.
    /// </summary>
    /// <param name="keyName">Environment variable key.</param>
    /// <returns>Value when found, null otherwise.</returns>
    public string? LoadApiKey(string keyName)
    {
        if (string.IsNullOrWhiteSpace(keyName))
        {
            return null;
        }

        var trimmedKeyName = keyName.Trim();
        return Environment.GetEnvironmentVariable(trimmedKeyName, EnvironmentVariableTarget.Process)
            ?? Environment.GetEnvironmentVariable(trimmedKeyName, EnvironmentVariableTarget.User)
            ?? Environment.GetEnvironmentVariable(trimmedKeyName, EnvironmentVariableTarget.Machine);
    }
    /// <summary>
    /// Loads the default API key variable using configured provider name.
    /// </summary>
    public string? LoadApiKey() => LoadApiKey("OPENAI_API_KEY");

    /// <summary>Counts today's persisted visual AI provider attempts, including failed requests.</summary>
    public int GetTodayAnalysisCount()
    {
        var (startUtc, endUtc) = ConvertLocalDateRangeToUtc(DateOnly.FromDateTime(DateTime.Today));
        return _activity.CountAiVisualProviderRequests(startUtc, endUtc);
    }

    /// <summary>Loads the most recent analysis from the current SQLite store.</summary>
    public AiAnalysis? LoadLatestAnalysis() => _activity.LoadLatestAiAnalysis();

    /// <summary>Deletes local snapshot-analysis records that reference one retained screenshot.</summary>
    internal int DeleteAiAnalysesReferencingScreenshot(string screenshotPath)
        => _activity.DeleteAiAnalysesReferencingScreenshot(screenshotPath);

    /// <summary>Returns the most recent retained screenshot independently of AI-analysis availability.</summary>
    public string? LoadLatestPrimaryScreenshot()
    {
        var settings = LoadSettings();
        var directory = string.IsNullOrWhiteSpace(settings.ScreenshotDirectory)
            ? _utilities.GetDefaultScreenshotDirectory()
            : settings.ScreenshotDirectory;
        if (!Directory.Exists(directory))
        {
            return null;
        }

        // Retained files are the source of truth; AI rows enrich them but do not control their visibility after restart.
        return ScreenshotStorageLayout.EnumerateOwnedArtifacts(directory)
            .Select(path => new FileInfo(path))
            .GroupBy(file => ScreenshotIdentity(file.Name), StringComparer.OrdinalIgnoreCase)
            .Select(group => group
                .OrderByDescending(file => IsPreferredStoredArtifact(file.Name))
                .ThenByDescending(file => file.LastWriteTimeUtc)
                .First())
            .Select(file => new
            {
                File = file,
                Day = ScreenshotStorageLayout.GetDay(directory, file.FullName)
            })
            .OrderByDescending(candidate => candidate.Day)
            .ThenByDescending(candidate => candidate.File.LastWriteTimeUtc)
            .Select(candidate => candidate.File.FullName)
            .FirstOrDefault();
    }

    /// <summary>Finds every owned image artifact generated by the same screenshot capture.</summary>
    /// <param name="screenshotPath">Absolute path to one retained capture artifact.</param>
    /// <returns>The stored and raw artifacts for the capture, or an empty list for an invalid path.</returns>
    public IReadOnlyList<string> FindScreenshotArtifacts(string screenshotPath)
    {
        if (!ScreenCaptureService.IsOwnedArtifact(screenshotPath) || !Path.IsPathFullyQualified(screenshotPath))
        {
            return Array.Empty<string>();
        }

        var fullPath = Path.GetFullPath(screenshotPath);
        if (!File.Exists(fullPath))
        {
            return Array.Empty<string>();
        }

        var settings = LoadSettings();
        var directory = string.IsNullOrWhiteSpace(settings.ScreenshotDirectory)
            ? _utilities.GetDefaultScreenshotDirectory()
            : settings.ScreenshotDirectory;
        var configuredDirectory = ScreenshotStorageLayout.NormalizeRoot(directory);
        var artifactDirectory = Path.GetDirectoryName(fullPath) is { } parentDirectory
            ? ScreenshotStorageLayout.NormalizeRoot(parentDirectory)
            : null;
        if (artifactDirectory is null || !ScreenshotStorageLayout.IsSameOrDescendant(artifactDirectory, configuredDirectory))
        {
            return Array.Empty<string>();
        }

        var identity = ScreenshotIdentity(Path.GetFileName(fullPath));
        return ScreenshotStorageLayout.EnumerateOwnedArtifactsInDirectory(artifactDirectory)
            .Where(path => string.Equals(ScreenshotIdentity(Path.GetFileName(path)), identity, StringComparison.OrdinalIgnoreCase))
            .ToArray();
    }

    /// <summary>Removes capture provenance after the last physical artifact and persisted child are gone.</summary>
    internal int DeleteScreenshotCaptureIfOrphaned(string screenshotPath)
    {
        if (!ScreenCaptureService.IsOwnedArtifact(screenshotPath) || !Path.IsPathFullyQualified(screenshotPath))
        {
            return 0;
        }

        var artifactIdentity = ScreenshotIdentity(Path.GetFileName(screenshotPath));
        var captureId = TryGetCaptureId(artifactIdentity);
        if (captureId is null)
        {
            return 0;
        }

        var settings = LoadSettings();
        var screenshotRoot = string.IsNullOrWhiteSpace(settings.ScreenshotDirectory)
            ? _utilities.GetDefaultScreenshotDirectory()
            : settings.ScreenshotDirectory;
        var hasPhysicalArtifact = ScreenshotStorageLayout.EnumerateOwnedArtifacts(screenshotRoot)
            .Any(path => string.Equals(
                TryGetCaptureId(ScreenshotIdentity(Path.GetFileName(path))),
                captureId,
                StringComparison.Ordinal));
        return hasPhysicalArtifact ? 0 : _activity.DeleteOrphanedScreenshotCapture(captureId);
    }

    /// <summary>
    /// Lists owned screenshot artifacts for one local calendar date without exposing unrelated files.
    /// </summary>
    /// <param name="date">The local date represented by the gallery.</param>
    /// <param name="cancellationToken">Stops directory enumeration and SQLite projection work.</param>
    /// <returns>A presentation-neutral screenshot projection ordered newest first.</returns>
    public ScreenshotGallery GetScreenshotGallery(DateOnly date, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // Search-index refreshes and the gallery can otherwise project the same retained captures
        // concurrently. Serialize this bounded, read-only workload so opening two surfaces cannot
        // multiply SQLite, JSON, and filesystem pressure on the workstation.
        _screenshotProjectionGate.Wait(cancellationToken);
        try
        {
            return GetScreenshotGalleryCore(date, cancellationToken);
        }
        finally
        {
            _screenshotProjectionGate.Release();
        }
    }

    private ScreenshotGallery GetScreenshotGalleryCore(DateOnly date, CancellationToken cancellationToken, IReadOnlySet<string>? captureFilter = null)
    {
        var settings = LoadSettings();
        var directory = string.IsNullOrWhiteSpace(settings.ScreenshotDirectory)
            ? _utilities.GetDefaultScreenshotDirectory()
            : settings.ScreenshotDirectory;
        var dayDirectory = ScreenshotStorageLayout.GetDayDirectory(directory, date);
        if (!Directory.Exists(dayDirectory))
        {
            return new ScreenshotGallery(date, Array.Empty<ScreenshotGalleryItem>());
        }

        var files = new List<FileInfo>();
        foreach (var path in ScreenshotStorageLayout.EnumerateOwnedArtifactsInDirectory(dayDirectory))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (captureFilter is not null
                && !captureFilter.Contains(TryGetCaptureId(ScreenshotIdentity(Path.GetFileName(path))) ?? string.Empty))
                continue;
            var file = new FileInfo(path);
            files.Add(file);
        }

        var retainedFiles = files
            .GroupBy(file => ScreenshotIdentity(file.Name), StringComparer.OrdinalIgnoreCase)
            .Select(group => group
                .OrderByDescending(file => IsPreferredStoredArtifact(file.Name))
                .ThenByDescending(file => file.LastWriteTimeUtc)
                .First())
            .OrderByDescending(file => file.LastWriteTimeUtc)
            .ToArray();
        cancellationToken.ThrowIfCancellationRequested();
        var analyses = _activity.LoadLatestAiAnalysesForScreenshots(retainedFiles.Select(file => file.FullName));
        var artifactIdentities = retainedFiles
            .ToDictionary(
                file => file.FullName,
                file => ScreenshotIdentity(file.Name),
                StringComparer.OrdinalIgnoreCase);
        var telemetryByIdentity = _activity.LoadScreenshotIntervalTelemetry(
            artifactIdentities.Values,
            cancellationToken);
        var textByIdentity = _activity.LoadScreenshotTextSnapshots(
            artifactIdentities.Values,
            cancellationToken);
        var captureIds = artifactIdentities.Values
            .Select(identity => TryGetCaptureId(identity)
                ?? throw new InvalidDataException($"Screenshot artifact has no valid capture identity: {identity}"))
            .ToArray();
        var provenanceByCapture = _activity.LoadScreenshotCaptures(captureIds);
        var hardwareByCapture = _activity.LoadCaptureHardwareSnapshots(captureIds, cancellationToken);
        var sources = retainedFiles
            .Select(file =>
            {
                var artifactIdentity = artifactIdentities[file.FullName];
                var captureId = TryGetCaptureId(artifactIdentity)
                    ?? throw new InvalidDataException($"Screenshot artifact has no valid capture identity: {artifactIdentity}");
                if (!provenanceByCapture.TryGetValue(captureId, out var persistedProvenance))
                {
                    throw new InvalidDataException($"Screenshot capture has no persisted installation provenance: {captureId}");
                }

                var provenance = persistedProvenance with
                {
                    Installation = persistedProvenance.Installation with
                    {
                        IsCurrent = string.Equals(
                            persistedProvenance.Installation.InstallationId,
                            settings.InstallationId,
                            StringComparison.Ordinal)
                    }
                };
                telemetryByIdentity.TryGetValue(artifactIdentity, out var telemetry);
                return CreateScreenshotGallerySource(
                    file,
                    artifactIdentity,
                    settings.ScreenshotIntervalMinutes,
                    telemetry,
                    provenance);
            })
            .ToArray();
        var activitySamples = new List<ActivitySample>();
        if (sources.Length > 0)
        {
            // A gallery day shares one SQLite read. Querying the same activity table once per
            // screenshot caused hundreds of indexed scans and blocked the presentation caller.
            _activity.VisitOverlapping(
                sources.Min(source => source.FromUtc),
                sources.Max(source => source.ToUtc),
                activitySamples.Add,
                cancellationToken);
        }

        var samplesBySource = MatchActivitySamples(
            sources.Select(source => new ScreenshotActivityInterval(
                source.Provenance.Installation.InstallationId,
                source.FromUtc,
                source.ToUtc)).ToArray(),
            activitySamples,
            cancellationToken);
        var items = new List<ScreenshotGalleryItem>(sources.Length);
        for (var sourceIndex = 0; sourceIndex < sources.Length; sourceIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var source = sources[sourceIndex];
            var samples = samplesBySource[sourceIndex];
            var activity = BuildScreenshotActivity(
                source.CapturedAt,
                source.Telemetry,
                source.FromUtc,
                source.ToUtc,
                samples,
                source.Provenance.Installation);
            analyses.TryGetValue(source.File.FullName, out var analysis);
            textByIdentity.TryGetValue(source.ArtifactIdentity, out var textSnapshot);
            hardwareByCapture.TryGetValue(source.Provenance.CaptureId, out var hardwareSnapshot);
            // Analysis-only deletion is per artifact; a sibling monitor may still retain the
            // capture's shared hardware evidence without exposing it on this cleared artifact.
            hardwareSnapshot = source.Telemetry is null ? null : hardwareSnapshot;
            items.Add(new ScreenshotGalleryItem(
                source.CapturedAt,
                source.File.FullName,
                activity.ForegroundApplication,
                GetCaptureKind(source.File.Name),
                source.Provenance.Origin,
                activity.SpanLabels,
                analysis?.Summary,
                analysis?.Timestamp,
                activity.ActivityIndex,
                textSnapshot,
                activity.ForegroundWindowTitle,
                GetScreenIndex(source.File.Name),
                GetScreenName(source.File.Name),
                activity.MouseClicks,
                source.Telemetry?.CpuUsagePercent,
                source.Telemetry?.GpuUsagePercent,
                source.Provenance.Installation,
                analysis is not null || textSnapshot is not null || source.Telemetry is not null || hardwareSnapshot is not null,
                HardwareSnapshot: hardwareSnapshot));
        }

        return new ScreenshotGallery(date, items);
    }

    /// <summary>Loads the most recent local day that still has retained screenshot artifacts.</summary>
    /// <param name="cancellationToken">Stops gallery projection work.</param>
    /// <returns>The latest populated gallery, or today's empty gallery when no retained capture exists.</returns>
    public ScreenshotGallery GetLatestScreenshotGallery(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var latestPath = LoadLatestPrimaryScreenshot();
        if (latestPath is null)
        {
            return new ScreenshotGallery(DateOnly.FromDateTime(DateTime.Today), Array.Empty<ScreenshotGalleryItem>());
        }

        var settings = LoadSettings();
        var directory = string.IsNullOrWhiteSpace(settings.ScreenshotDirectory)
            ? _utilities.GetDefaultScreenshotDirectory()
            : settings.ScreenshotDirectory;
        return GetScreenshotGallery(ScreenshotStorageLayout.GetDay(directory, latestPath), cancellationToken);
    }

    /// <summary>Counts retained screenshot captures without loading their SQLite-backed gallery metadata.</summary>
    internal (int TotalSnapshotCount, int TodaySnapshotCount) GetScreenshotAvailabilityCounts(
        DateOnly today,
        CancellationToken cancellationToken)
    {
        var settings = LoadSettings();
        var directory = string.IsNullOrWhiteSpace(settings.ScreenshotDirectory)
            ? _utilities.GetDefaultScreenshotDirectory()
            : settings.ScreenshotDirectory;
        if (!Directory.Exists(directory))
        {
            return (0, 0);
        }

        var retainedFiles = new Dictionary<string, FileInfo>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in ScreenshotStorageLayout.EnumerateOwnedArtifacts(directory))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var candidate = new FileInfo(path);
            var identity = ScreenshotIdentity(candidate.Name);
            if (!retainedFiles.TryGetValue(identity, out var current) ||
                IsPreferredStoredArtifact(candidate.Name) && !IsPreferredStoredArtifact(current.Name) ||
                IsPreferredStoredArtifact(candidate.Name) == IsPreferredStoredArtifact(current.Name) &&
                candidate.LastWriteTimeUtc > current.LastWriteTimeUtc)
            {
                retainedFiles[identity] = candidate;
            }
        }

        var todaySnapshotCount = 0;
        foreach (var file in retainedFiles.Values)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (ScreenshotStorageLayout.GetDay(directory, file.FullName) == today)
            {
                todaySnapshotCount++;
            }
        }

        return (retainedFiles.Count, todaySnapshotCount);
    }

    /// <summary>Loads every retained screenshot projection for deterministic search-index rebuilds.</summary>
    internal IReadOnlyList<ScreenshotGalleryItem> GetAllScreenshotGalleryItems()
    {
        var settings = LoadSettings();
        if (!Directory.Exists(settings.ScreenshotDirectory))
        {
            return Array.Empty<ScreenshotGalleryItem>();
        }

        var dates = ScreenshotStorageLayout.EnumerateOwnedArtifacts(settings.ScreenshotDirectory)
            .Select(path => ScreenshotStorageLayout.GetDay(settings.ScreenshotDirectory, path))
            .Distinct()
            .Order()
            .ToArray();
        return dates
            .SelectMany(date => GetScreenshotGallery(date).Items)
            .OrderBy(item => item.CapturedAt)
            .ToArray();
    }

    /// <summary>Visits retained screenshot projections one gallery day at a time.</summary>
    /// <remarks>
    /// A full materialization keeps every OCR and AI payload alive while the search index is
    /// rebuilt. The visitor keeps the projection lifetime bounded to one day instead.
    /// </remarks>
    internal void VisitAllScreenshotGalleryItems(
        Action<ScreenshotGalleryItem> visitor,
        CancellationToken cancellationToken,
        DateOnly? fromDate = null,
        DateOnly? toDate = null)
    {
        ArgumentNullException.ThrowIfNull(visitor);
        cancellationToken.ThrowIfCancellationRequested();

        var settings = LoadSettings();
        if (!Directory.Exists(settings.ScreenshotDirectory))
        {
            return;
        }

        var dates = ScreenshotStorageLayout.EnumerateOwnedArtifacts(settings.ScreenshotDirectory)
            .Select(path => ScreenshotStorageLayout.GetDay(settings.ScreenshotDirectory, path))
            .Where(date => (!fromDate.HasValue || date >= fromDate.Value) && (!toDate.HasValue || date <= toDate.Value))
            .Distinct()
            .Order()
            .ToArray();
        foreach (var date in dates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var gallery = GetScreenshotGallery(date, cancellationToken);
            foreach (var item in gallery.Items)
            {
                cancellationToken.ThrowIfCancellationRequested();
                visitor(item);
            }
        }
    }

    private IReadOnlyList<AiScreenshotReprocessCandidate> MaterializeAiReprocessCandidates(
        IReadOnlyList<AiReprocessCatalogRecord> records,
        IReadOnlyDictionary<string, FileInfo> retainedByIdentity,
        CancellationToken cancellationToken)
    {
        if (records.Count == 0)
        {
            return Array.Empty<AiScreenshotReprocessCandidate>();
        }

        var requestedIdentities = records
            .SelectMany(record => record.ArtifactIdentities)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var textByIdentity = _activity.LoadScreenshotTextSnapshots(requestedIdentities, cancellationToken);
        var hardwareByCapture = _activity.LoadCaptureHardwareSnapshots(records.Select(record => record.CaptureId), cancellationToken);
        var activitySamples = new List<ActivitySample>();
        var recordsWithProvenance = records
            .Select(record => (Record: record, InstallationId: NormalizeInstallationId(record.InstallationId)))
            .Where(entry => entry.Record.HasTelemetry && entry.InstallationId is not null)
            .ToArray();
        if (recordsWithProvenance.Length > 0)
        {
            _activity.VisitOverlapping(
                recordsWithProvenance.Min(entry => entry.Record.CapturedAt),
                recordsWithProvenance.Max(entry => entry.Record.CapturedAt).AddTicks(1),
                activitySamples.Add,
                cancellationToken);
        }

        var activityByCapture = MatchActivitySamples(
                recordsWithProvenance
                    .Select(entry => new ScreenshotActivityInterval(
                        entry.InstallationId!,
                        entry.Record.CapturedAt,
                        entry.Record.CapturedAt.AddTicks(1)))
                    .ToArray(),
                activitySamples,
                cancellationToken)
            .Select((samples, index) => (recordsWithProvenance[index].Record.CaptureId, Samples: samples))
            .ToDictionary(entry => entry.CaptureId, entry => entry.Samples, StringComparer.Ordinal);

        var candidates = new List<AiScreenshotReprocessCandidate>(records.Count);
        foreach (var record in records)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var paths = record.ArtifactIdentities
                .Select(identity => retainedByIdentity.TryGetValue(identity, out var file) ? file.FullName : null)
                .Where(path => path is not null)
                .Cast<string>()
                .ToArray();
            var texts = record.ArtifactIdentities
                .Select(identity => textByIdentity.TryGetValue(identity, out var text) ? text : null)
                .Where(text => text is not null)
                .Cast<ScreenshotTextSnapshot>()
                .ToArray();
            var installationId = NormalizeInstallationId(record.InstallationId);
            var coveringSamples = installationId is not null
                && activityByCapture.TryGetValue(record.CaptureId, out var matchedSamples)
                    ? matchedSamples
                    : [];
            // Overlapping samples provide conflicting foreground identities. Without a unique source,
            // or without valid capture provenance, replay cannot prove privacy was evaluated safely.
            var historicalSample = coveringSamples.Count == 1 ? coveringSamples[0] : null;
            var historicalContext = historicalSample is null || string.IsNullOrWhiteSpace(historicalSample.ProcessName)
                ? null
                : new AnalysisContextSnapshot(
                    historicalSample.Application,
                    historicalSample.Context,
                    historicalSample.WindowTitle,
                    historicalSample.State,
                    TrackingDomainService.FilterAnalysisAttributes(historicalSample.Attributes),
                    Snapshot: hardwareByCapture.GetValueOrDefault(record.CaptureId));
            var captureOrigin = GetCaptureOrigin(record.ArtifactIdentities[0] + ".webp");
            candidates.Add(new AiScreenshotReprocessCandidate(
                record.CaptureId,
                installationId,
                record.CapturedAt,
                captureOrigin,
                paths,
                texts,
                historicalContext,
                record.HasAiDescription,
                record.ArtifactIdentities.Count - paths.Length,
                historicalSample?.ProcessName ?? string.Empty));
        }

        return candidates;
    }

    private IReadOnlyDictionary<string, FileInfo> EnumerateRetainedScreenshotArtifacts(
        AppSettings settings,
        CancellationToken cancellationToken)
    {
        var directory = string.IsNullOrWhiteSpace(settings.ScreenshotDirectory)
            ? _utilities.GetDefaultScreenshotDirectory()
            : settings.ScreenshotDirectory;
        var retainedByIdentity = new Dictionary<string, FileInfo>(StringComparer.OrdinalIgnoreCase);
        if (!Directory.Exists(directory))
        {
            return retainedByIdentity;
        }

        foreach (var path in ScreenshotStorageLayout.EnumerateOwnedArtifacts(directory))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var candidate = new FileInfo(path);
            var identity = ScreenshotIdentity(candidate.Name);
            if (!retainedByIdentity.TryGetValue(identity, out var current)
                || IsPreferredStoredArtifact(candidate.Name) && !IsPreferredStoredArtifact(current.Name)
                || IsPreferredStoredArtifact(candidate.Name) == IsPreferredStoredArtifact(current.Name)
                && candidate.LastWriteTimeUtc > current.LastWriteTimeUtc)
            {
                retainedByIdentity[identity] = candidate;
            }
        }

        return retainedByIdentity;
    }

    private IReadOnlyDictionary<string, FileInfo> ResolveRetainedScreenshotArtifacts(
        AppSettings settings,
        IReadOnlyList<string> artifactIdentities,
        CancellationToken cancellationToken)
    {
        var retainedByIdentity = new Dictionary<string, FileInfo>(StringComparer.OrdinalIgnoreCase);
        var requestedIdentities = artifactIdentities
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(identity =>
                !string.IsNullOrWhiteSpace(identity)
                && string.Equals(Path.GetFileName(identity), identity, StringComparison.Ordinal)
                && ScreenCaptureService.IsOwnedArtifact(identity + ".webp"))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var (identity, candidate) in EnumerateRetainedScreenshotArtifacts(settings, cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (requestedIdentities.Contains(identity))
            {
                retainedByIdentity[identity] = candidate;
            }
        }

        return retainedByIdentity;
    }

    /// <summary>Extracts the capture identity shared by retained artifacts and search-source changes.</summary>
    internal static string? TryGetCaptureId(string artifactIdentity)
    {
        var separator = artifactIdentity.IndexOf('_', StringComparison.Ordinal);
        if (separator <= 0)
        {
            return null;
        }

        var captureId = artifactIdentity[..separator];
        return Guid.TryParseExact(captureId, "N", out _) ? captureId : null;
    }

    private static ScreenshotGallerySource CreateScreenshotGallerySource(
        FileInfo file,
        string artifactIdentity,
        int screenshotIntervalMinutes,
        ScreenshotIntervalTelemetry? telemetry,
        ScreenshotCaptureProvenance provenance)
    {
        var capturedAt = provenance.CapturedAt.ToUniversalTime();
        if (telemetry is { } persistedTelemetry
            && persistedTelemetry.CapturedAt.ToUniversalTime() != capturedAt)
        {
            throw new InvalidDataException("Screenshot capture provenance conflicts with interval telemetry.");
        }

        var fromUtc = telemetry?.IntervalStartedAt.ToUniversalTime()
            ?? capturedAt.ToUniversalTime().AddMinutes(-Math.Max(1, screenshotIntervalMinutes));
        var toUtc = telemetry?.CapturedAt.ToUniversalTime() ?? capturedAt.ToUniversalTime();
        return new ScreenshotGallerySource(file, artifactIdentity, capturedAt, telemetry, fromUtc, toUtc, provenance);
    }

    private static ScreenshotActivityContext BuildScreenshotActivity(
        DateTimeOffset capturedAt,
        ScreenshotIntervalTelemetry? telemetry,
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        IReadOnlyList<ActivitySample> samples,
        InstallationProfile installation)
    {
        var foregroundSample = samples
            .OrderBy(sample => Math.Abs((sample.Timestamp - capturedAt).TotalMilliseconds))
            .FirstOrDefault(sample => !string.IsNullOrWhiteSpace(sample.Application) || !string.IsNullOrWhiteSpace(sample.WindowTitle));
        var foregroundApplication = string.IsNullOrWhiteSpace(foregroundSample?.Application)
            ? "Desktop"
            : foregroundSample.Application;
        var foregroundWindowTitle = string.IsNullOrWhiteSpace(foregroundSample?.WindowTitle)
            ? null
            : foregroundSample.WindowTitle;
        var labels = samples
            .OrderBy(sample => sample.Timestamp)
            .Select(sample => new
            {
                sample.Timestamp,
                Label = sample.Attributes is not null
                    && sample.Attributes.TryGetValue(ActivityAttributeKeys.SpanLabel, out var label)
                    ? label.Trim()
                    : string.Empty
            })
            .Where(entry => !string.IsNullOrWhiteSpace(entry.Label))
            .Aggregate(
                new List<ActivityLabelSample>(),
                (distinct, entry) =>
                {
                    if (distinct.Count == 0 || !string.Equals(distinct[^1].Label, entry.Label, StringComparison.Ordinal))
                    {
                        distinct.Add(new ActivityLabelSample(entry.Timestamp, entry.Label, installation));
                    }

                    return distinct;
                });
        int? activityIndex = samples.Count == 0 && telemetry is null
            ? null
            : ActivityScoreService.CalculateIntervalActivityIndex(
                samples,
                (toUtc - fromUtc).TotalMinutes,
                telemetry?.CpuUsagePercent,
                telemetry?.GpuUsagePercent);
        long? mouseClicks = samples.Count == 0
            ? null
            : samples.Aggregate(0L, (total, sample) => checked(total + sample.MouseClicks));
        return new ScreenshotActivityContext(foregroundApplication, labels, activityIndex, foregroundWindowTitle, mouseClicks);
    }

    private static bool SampleOverlaps(ActivitySample sample, DateTimeOffset fromUtc, DateTimeOffset toUtc)
    {
        var sampleEnd = sample.Timestamp.ToUniversalTime();
        var sampleStart = sampleEnd.AddSeconds(-sample.DurationSeconds);
        return sampleEnd > fromUtc && sampleStart < toUtc;
    }

    internal static IReadOnlyList<IReadOnlyList<ActivitySample>> MatchActivitySamples(
        IReadOnlyList<ScreenshotActivityInterval> intervals,
        IReadOnlyList<ActivitySample> samples,
        CancellationToken cancellationToken)
    {
        var result = new IReadOnlyList<ActivitySample>[intervals.Count];
        var samplesByInstallation = samples
            .GroupBy(sample => sample.InstallationId, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.OrderBy(sample => sample.Timestamp.ToUniversalTime()).ToArray(),
                StringComparer.Ordinal);

        foreach (var installationGroup in intervals.Select((interval, index) => (Interval: interval, Index: index)).GroupBy(
                     entry => entry.Interval.InstallationId,
                     StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            samplesByInstallation.TryGetValue(installationGroup.Key, out var installationSamples);
            installationSamples ??= [];
            var orderedIntervals = installationGroup
                .GroupBy(entry => (entry.Interval.FromUtc, entry.Interval.ToUtc))
                .OrderBy(group => group.Key.FromUtc)
                .ToArray();
            if (orderedIntervals.Zip(orderedIntervals.Skip(1), (left, right) => left.Key.ToUtc <= right.Key.ToUtc).Any(monotonic => !monotonic))
            {
                // Imported/custom intervals can theoretically invert their end order. That is
                // not the capture runtime's monotonic shape, but correctness still wins here.
                foreach (var intervalGroup in orderedIntervals)
                {
                    var matches = installationSamples
                        .Where(sample => SampleOverlaps(sample, intervalGroup.Key.FromUtc, intervalGroup.Key.ToUtc))
                        .ToArray();
                    foreach (var entry in intervalGroup)
                    {
                        result[entry.Index] = matches;
                    }
                }

                continue;
            }

            var orderedSamples = installationSamples
                .Select((sample, order) => new
                {
                    Sample = sample,
                    Order = order,
                    StartUtc = sample.Timestamp.ToUniversalTime().AddSeconds(-sample.DurationSeconds),
                    EndUtc = sample.Timestamp.ToUniversalTime()
                })
                .OrderBy(entry => entry.StartUtc)
                .ThenBy(entry => entry.Order)
                .ToArray();
            var active = new HashSet<int>();
            var endings = new PriorityQueue<int, (long EndTicks, int Order)>();
            var nextSample = 0;
            foreach (var intervalGroup in orderedIntervals)
            {
                cancellationToken.ThrowIfCancellationRequested();
                while (nextSample < orderedSamples.Length && orderedSamples[nextSample].StartUtc < intervalGroup.Key.ToUtc)
                {
                    active.Add(nextSample);
                    endings.Enqueue(
                        nextSample,
                        (orderedSamples[nextSample].EndUtc.UtcTicks, orderedSamples[nextSample].Order));
                    nextSample++;
                }

                while (endings.TryPeek(out var endedIndex, out var ending)
                       && ending.EndTicks <= intervalGroup.Key.FromUtc.UtcTicks)
                {
                    _ = endings.Dequeue();
                    active.Remove(endedIndex);
                }

                var frozenMatches = active
                    .Select(index => orderedSamples[index])
                    .OrderBy(entry => entry.EndUtc)
                    .ThenBy(entry => entry.Order)
                    .Select(entry => entry.Sample)
                    .ToArray();
                foreach (var entry in intervalGroup)
                {
                    result[entry.Index] = frozenMatches;
                }
            }
        }

        return result;
    }

    private static string? NormalizeInstallationId(string? installationId) =>
        Guid.TryParseExact(installationId, "N", out var parsed) ? parsed.ToString("N") : null;

    private sealed record ScreenshotGallerySource(
        FileInfo File,
        string ArtifactIdentity,
        DateTimeOffset CapturedAt,
        ScreenshotIntervalTelemetry? Telemetry,
        DateTimeOffset FromUtc,
        DateTimeOffset ToUtc,
        ScreenshotCaptureProvenance Provenance);

    private sealed record ScreenshotActivityContext(
        string ForegroundApplication,
        IReadOnlyList<ActivityLabelSample> SpanLabels,
        int? ActivityIndex,
        string? ForegroundWindowTitle,
        long? MouseClicks);

    /// <summary>Returns the stable artifact identity shared by raw and retained variants of one screen.</summary>
    internal static string ScreenshotIdentity(string fileName)
    {
        var withoutExtension = Path.GetFileNameWithoutExtension(fileName);
        return withoutExtension.EndsWith("-raw", StringComparison.OrdinalIgnoreCase)
            ? withoutExtension[..^4]
            : withoutExtension;
    }

    private static bool IsPreferredStoredArtifact(string fileName) =>
        !Path.GetFileNameWithoutExtension(fileName).EndsWith("-raw", StringComparison.OrdinalIgnoreCase);

    private static string GetCaptureKind(string fileName)
    {
        var withoutExtension = Path.GetFileNameWithoutExtension(fileName);
        if (withoutExtension.Contains("_active-window", StringComparison.OrdinalIgnoreCase))
        {
            return "active-window";
        }

        if (withoutExtension.Contains("_monitor-", StringComparison.OrdinalIgnoreCase))
        {
            return "monitor";
        }

        throw new InvalidDataException($"Screenshot artifact has no valid capture kind: {fileName}");
    }

    private static int? GetScreenIndex(string fileName)
    {
        var withoutExtension = Path.GetFileNameWithoutExtension(fileName);
        if (withoutExtension.EndsWith("-raw", StringComparison.OrdinalIgnoreCase))
        {
            withoutExtension = withoutExtension[..^4];
        }

        var marker = "_monitor-";
        var markerIndex = withoutExtension.LastIndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (markerIndex < 0)
        {
            return null;
        }

        var indexText = withoutExtension[(markerIndex + marker.Length)..];
        return int.TryParse(indexText, CultureInfo.InvariantCulture, out var index) && index > 0
            ? index
            : null;
    }

    private static string? GetScreenName(string fileName)
        => GetScreenIndex(fileName) is { } index ? $"Monitor {index}" : null;

    private static string GetCaptureOrigin(string fileName)
    {
        var withoutExtension = Path.GetFileNameWithoutExtension(fileName);
        if (withoutExtension.Contains("_manual_", StringComparison.OrdinalIgnoreCase))
        {
            return ScreenshotCaptureOrigins.Manual;
        }

        if (withoutExtension.Contains("_scheduled_", StringComparison.OrdinalIgnoreCase))
        {
            return ScreenshotCaptureOrigins.Scheduled;
        }

        throw new InvalidDataException($"Screenshot artifact has no valid capture origin: {fileName}");
    }

    /// <summary>
    /// Lists data files that contain at least one expired, parseable record without deleting them.
    /// </summary>
    /// <param name="cutoffUtc">Oldest allowed last-write timestamp.</param>
    /// <returns>Files that would be compacted by the requested cutoff.</returns>
    public IReadOnlyList<string> GetRetentionCandidates(DateTimeOffset cutoffUtc)
    {
        return GetRetentionPreview(cutoffUtc).Paths;
    }

    /// <summary>Counts expired local records and their estimated or encoded size without changing the data files.</summary>
    /// <param name="cutoffUtc">Records older than this instant are counted.</param>
    /// <returns>Record-level retention preview with affected file paths.</returns>
    public DataRetentionPreview GetRetentionPreview(DateTimeOffset cutoffUtc)
    {
        var samples = _activity.GetRetentionPreview(cutoffUtc);
        var paths = samples.Count > 0 ? new[] { _activity.DatabasePath } : Array.Empty<string>();
        return new DataRetentionPreview(samples.Count, samples.Bytes, paths);
    }

    /// <summary>Removes expired records from the current SQLite store.</summary>
    /// <param name="cutoffUtc">Records older than this instant are removed.</param>
    /// <returns>Number of expired records removed across local data files.</returns>
    public int ApplyRetention(DateTimeOffset cutoffUtc)
    {
        var removed = _activity.ApplyRetention(cutoffUtc);
        if (removed > 0)
        {
            // Retention can invalidate an already materialized dashboard window; force one bounded reload.
            Interlocked.Increment(ref _activityRevision);
        }

        return removed;
    }

    /// <summary>
    /// Builds hourly active-time levels for the trailing 24-hour window when persisted samples cover that entire window.
    /// </summary>
    /// <param name="windowEndUtc">Optional inclusive-end instant for deterministic callers and tests; defaults to the current UTC time.</param>
    /// <returns>A 24-point trend whose levels are percentages of active seconds per hour.</returns>
    public ActivityTrendState Get24HourActivityTrend(DateTimeOffset? windowEndUtc = null)
    {
        var windowEnd = (windowEndUtc ?? DateTimeOffset.UtcNow).ToUniversalTime();
        var samples = LoadDashboardActivitySamples(windowEnd.AddHours(-24), windowEnd);
        return Build24HourActivityTrend(samples, windowEnd);
    }

    /// <summary>Loads the minimal persisted activity projection needed by the in-memory dashboard cache.</summary>
    internal IReadOnlyList<ReportSourceSample> LoadDashboardActivitySamples(DateTimeOffset windowEndUtc)
    {
        var windowEnd = windowEndUtc.ToUniversalTime();
        var localDate = DateOnly.FromDateTime(windowEnd.ToLocalTime().DateTime);
        var todayStartUtc = ConvertLocalBoundaryToUtc(localDate);
        var todayEndUtc = ConvertLocalBoundaryToUtc(localDate.AddDays(1));
        var trendStartUtc = windowEnd.AddHours(-24);
        var windowStart = todayStartUtc < trendStartUtc ? todayStartUtc : trendStartUtc;
        var queryEnd = todayEndUtc > windowEnd ? todayEndUtc : windowEnd;
        return LoadDashboardActivitySamples(windowStart, queryEnd);
    }

    private IReadOnlyList<ReportSourceSample> LoadDashboardActivitySamples(
        DateTimeOffset windowStartUtc,
        DateTimeOffset windowEndUtc)
    {
        var samples = new List<ReportSourceSample>();
        _activity.VisitReportOverlapping(windowStartUtc, windowEndUtc, samples.Add, CancellationToken.None);
        samples.Sort(static (left, right) => left.Timestamp.CompareTo(right.Timestamp));
        return samples;
    }

    /// <summary>Builds today's exact counters from the minimal cached activity projection.</summary>
    internal static DailySummary BuildDailySummary(
        IReadOnlyList<ReportSourceSample> samples,
        DateOnly date)
    {
        ArgumentNullException.ThrowIfNull(samples);
        var fromUtcTicks = ConvertLocalBoundaryToUtc(date).UtcDateTime.Ticks;
        var toUtcTicks = ConvertLocalBoundaryToUtc(date.AddDays(1)).UtcDateTime.Ticks;
        long activeTicks = 0;
        long idleTicks = 0;
        long keyPresses = 0;
        long mouseClicks = 0;
        var applicationTicks = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);

        foreach (var sample in samples)
        {
            var originalDurationTicks = checked((long)sample.DurationSeconds * TimeSpan.TicksPerSecond);
            var originalEndTicks = sample.Timestamp.UtcDateTime.Ticks;
            var originalStartTicks = checked(originalEndTicks - originalDurationTicks);
            var clippedStartTicks = Math.Max(originalStartTicks, fromUtcTicks);
            var clippedEndTicks = Math.Min(originalEndTicks, toUtcTicks);
            if (clippedStartTicks >= clippedEndTicks)
            {
                continue;
            }

            var includedTicks = clippedEndTicks - clippedStartTicks;
            keyPresses += ScaleCount(sample.KeyPresses, includedTicks, originalDurationTicks);
            mouseClicks += ScaleCount(sample.MouseClicks, includedTicks, originalDurationTicks);
            if (string.Equals(sample.State, "active", StringComparison.OrdinalIgnoreCase))
            {
                activeTicks += includedTicks;
                var application = string.IsNullOrWhiteSpace(sample.Application) ? "Unknown" : sample.Application.Trim();
                applicationTicks[application] = applicationTicks.GetValueOrDefault(application) + includedTicks;
            }
            else if (string.Equals(sample.State, "idle", StringComparison.OrdinalIgnoreCase))
            {
                idleTicks += includedTicks;
            }
        }

        var applications = applicationTicks
            .Select(pair => new ApplicationSummary(pair.Key, pair.Value / TimeSpan.TicksPerSecond))
            .Where(application => application.ActiveSeconds > 0)
            .OrderByDescending(application => application.ActiveSeconds)
            .ThenBy(application => application.Application, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return new DailySummary(
            activeTicks / TimeSpan.TicksPerSecond,
            idleTicks / TimeSpan.TicksPerSecond,
            keyPresses,
            mouseClicks,
            applications);
    }

    /// <summary>Builds the exact rolling trend from a timestamp-ordered minimal activity projection.</summary>
    internal static ActivityTrendState Build24HourActivityTrend(
        IReadOnlyList<ReportSourceSample> samples,
        DateTimeOffset windowEndUtc)
    {
        ArgumentNullException.ThrowIfNull(samples);
        const int hourCount = 24;
        var windowEnd = windowEndUtc.ToUniversalTime();
        var windowStart = windowEnd.AddHours(-hourCount);

        var activeSecondsByHour = new double[hourCount];
        var coveredSeconds = 0d;
        var coveredUntil = windowStart;
        foreach (var sample in samples)
        {
            var sampleEnd = sample.Timestamp.ToUniversalTime();
            var sampleStart = sampleEnd.AddSeconds(-sample.DurationSeconds);
            var overlapStart = sampleStart < windowStart ? windowStart : sampleStart;
            var overlapEnd = sampleEnd > windowEnd ? windowEnd : sampleEnd;
            if (overlapEnd <= overlapStart)
            {
                continue;
            }

            if (overlapEnd > coveredUntil)
            {
                var uncoveredStart = overlapStart > coveredUntil ? overlapStart : coveredUntil;
                if (overlapEnd > uncoveredStart)
                {
                    coveredSeconds += (overlapEnd - uncoveredStart).TotalSeconds;
                    coveredUntil = overlapEnd;
                }
            }

            if (!string.Equals(sample.State, "active", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var activeCursor = overlapStart;
            while (activeCursor < overlapEnd)
            {
                var hourIndex = Math.Clamp((int)(activeCursor - windowStart).TotalHours, 0, hourCount - 1);
                var bucketEnd = windowStart.AddHours(hourIndex + 1);
                var activeSegmentEnd = overlapEnd < bucketEnd ? overlapEnd : bucketEnd;
                activeSecondsByHour[hourIndex] += (activeSegmentEnd - activeCursor).TotalSeconds;
                activeCursor = activeSegmentEnd;
            }
        }

        var hasCompleteCoverage = coveredSeconds >= TimeSpan.FromHours(hourCount).TotalSeconds;
        var hourlyLevels = activeSecondsByHour
            .Select(seconds => Math.Min(100d, seconds / TimeSpan.FromHours(1).TotalSeconds * 100d))
            .ToArray();
        return new ActivityTrendState(windowStart, windowEnd, hasCompleteCoverage, hourlyLevels);
    }

    private static long ScaleCount(long count, long includedTicks, long originalTicks) =>
        includedTicks == originalTicks
            ? count
            : decimal.ToInt64(decimal.Round(
                (decimal)count * includedTicks / originalTicks,
                0,
                MidpointRounding.AwayFromZero));

    /// <summary>Converts one selected local calendar day to its robust half-open UTC interval.</summary>
    internal static (DateTimeOffset FromUtc, DateTimeOffset ToUtc) ConvertLocalDateRangeToUtc(DateOnly date) =>
        (ConvertLocalBoundaryToUtc(date), ConvertLocalBoundaryToUtc(date.AddDays(1)));

    private static DateTimeOffset ConvertLocalBoundaryToUtc(DateOnly date)
    {
        var timeZone = TimeZoneInfo.Local;
        var local = DateTime.SpecifyKind(date.ToDateTime(TimeOnly.MinValue), DateTimeKind.Unspecified);
        while (timeZone.IsInvalidTime(local))
        {
            local = local.AddMinutes(1);
        }

        if (timeZone.IsAmbiguousTime(local))
        {
            // The larger offset selects the first occurrence of a repeated local boundary.
            return new DateTimeOffset(local, timeZone.GetAmbiguousTimeOffsets(local).Max()).ToUniversalTime();
        }

        return new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(local, timeZone));
    }

    /// <summary>
    /// Returns latest sample, if any.
    /// </summary>
    public ActivitySample? LoadLatestSample() => _activity.LoadLatest();


    /// <summary>
    /// Calculates summary counters for today.
    /// </summary>
    public DailySummary GetTodaySummary()
    {
        return GetSummary(DateOnly.FromDateTime(DateTime.Today));
    }

    /// <summary>
    /// Calculates summary counters for one local calendar date.
    /// </summary>
    /// <param name="date">The local date represented by the summary.</param>
    /// <returns>Aggregated activity values for the requested date.</returns>
    public DailySummary GetSummary(DateOnly date)
    {
        var fromUtc = ConvertLocalBoundaryToUtc(date);
        var toUtc = ConvertLocalBoundaryToUtc(date.AddDays(1));
        var samples = LoadDashboardActivitySamples(fromUtc, toUtc);
        return BuildDailySummary(samples, date);
    }

    /// <summary>Streams privacy-minimized activity and AI usage from one consistent SQLite snapshot.</summary>
    internal void VisitReportData(
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        Action<ReportSourceSample> activityVisitor,
        Action<AiRequestUsageRecord> aiUsageVisitor,
        CancellationToken cancellationToken) =>
        _activity.VisitReportData(fromUtc, toUtc, activityVisitor, aiUsageVisitor, cancellationToken);

    /// <summary>Streams only AI usage rows for callers that do not need activity aggregation.</summary>
    internal void VisitAiUsage(
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        Action<AiRequestUsageRecord> visitor,
        CancellationToken cancellationToken) =>
        _activity.VisitAiUsage(fromUtc, toUtc, visitor, cancellationToken);

    private AppSettings EnsureInstallationId(AppSettings settings)
    {
        if (IsValidInstallationId(settings.InstallationId))
        {
            return settings;
        }

        using var bootstrapMutex = new Mutex(false, _settingsBootstrapMutexName);
        var acquired = false;
        try
        {
            try
            {
                acquired = bootstrapMutex.WaitOne(TimeSpan.FromSeconds(10));
            }
            catch (AbandonedMutexException)
            {
                acquired = true;
            }

            if (!acquired)
            {
                throw new TimeoutException("Timed out while initializing the WorkTrail installation identity.");
            }

            var persistedInstallationId = TryLoadInstallationId();
            if (persistedInstallationId is not null)
            {
                return settings with { InstallationId = persistedInstallationId };
            }

            var initialized = settings with { InstallationId = _utilities.GenerateInstallationId() };
            // The fixed per-settings-file mutex makes first-launch identity initialization process-safe.
            WriteSettingsFile(initialized);
            return initialized;
        }
        finally
        {
            if (acquired)
            {
                bootstrapMutex.ReleaseMutex();
            }
        }
    }

    private T WithSettingsMutex<T>(Func<T> operation)
    {
        using var settingsMutex = new Mutex(false, _settingsBootstrapMutexName);
        var acquired = false;
        try
        {
            try
            {
                acquired = settingsMutex.WaitOne(TimeSpan.FromSeconds(10));
            }
            catch (AbandonedMutexException)
            {
                acquired = true;
            }

            if (!acquired)
            {
                throw new TimeoutException("Timed out while accessing WorkTrail settings.");
            }

            // Read/replace operations share one process-safe mutex so readers never observe an in-progress settings write.
            return operation();
        }
        finally
        {
            if (acquired)
            {
                settingsMutex.ReleaseMutex();
            }
        }
    }

    private static bool IsValidInstallationId(string? value) =>
        value is { Length: >= 16 and <= 160 }
        && value.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '.');

}

/// <summary>One normalized screenshot interval used by the linear activity join.</summary>
internal sealed record ScreenshotActivityInterval(
    string InstallationId,
    DateTimeOffset FromUtc,
    DateTimeOffset ToUtc);

/// <summary>Describes record-level local-data retention impact without exposing record contents.</summary>
public sealed record DataRetentionPreview(int RecordCount, long TotalBytes, IReadOnlyList<string> Paths);
