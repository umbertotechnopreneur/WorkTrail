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


using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using WorkTrail.Application;
using WorkTrail.Search;

namespace WorkTrail.Services;

/// <summary>Builds the mandatory Lucene projection from durable WorkTrail sources and executes local queries.</summary>
internal sealed class LocalSearchCoordinator : IAsyncDisposable
{
    private const int MaximumIncrementalChanges = 2_000;
    private readonly LocalStore _store;
    private readonly ILocalSearchService _search;
    private readonly SemaphoreSlim _indexGate = new(1, 1);
    private readonly SettingsSnapshot _settings;
    private readonly ILogger _logger;
    private readonly CancellationTokenSource _shutdown = new();
    private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private Task? _worker;
    private Exception? _failure;
    private bool _disposed;

    /// <summary>Creates the runtime-owned local search coordinator.</summary>
    internal LocalSearchCoordinator(LocalStore store, ILocalSearchService search, SettingsSnapshot? settings = null, ILogger? logger = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _search = search ?? throw new ArgumentNullException(nameof(search));
        _settings = settings ?? new SettingsSnapshot(store.LoadSettings());
        _logger = logger ?? NullLogger.Instance;
        if (search.CommittedSourceRevision > 0) _ready.TrySetResult();
    }

    /// <summary>Starts the single owned updater after application initialization and deletion recovery.</summary>
    internal void Start()
    {
        ThrowIfDisposed();
        if (_worker is not null) throw new InvalidOperationException("The search updater is already started.");
        _worker = Task.Run(() => RunUpdaterAsync(_shutdown.Token));
    }

    private async Task RunUpdaterAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        do
        {
            try
            {
                // A failed projection is surfaced to queries and requires an explicit rebuild;
                // retrying invalid data every second would conceal the fault and consume I/O.
                if (Volatile.Read(ref _failure) is null)
                    await SynchronizeAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { return; }
            catch (Exception exception)
            {
                _logger.LogError("Background search index update failed. ExceptionType={ExceptionType}", exception.GetType().Name);
            }

            try
            {
                if (!await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false)) return;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { return; }
        } while (true);
    }

    /// <summary>Queries the last committed snapshot; only first-time initialization can delay a read.</summary>
    internal Task<SearchResponse> SearchAsync(SearchRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();

        // Lucene reads are synchronous. Enter the pool before any work can run on a WinUI dispatcher.
        return Task.Run(() => SearchCoreAsync(request, cancellationToken), cancellationToken);
    }

    private async Task<SearchResponse> SearchCoreAsync(
        SearchRequest request,
        CancellationToken cancellationToken)
    {
        if (_worker is null && !_ready.Task.IsCompleted)
            throw new InvalidOperationException("Start or synchronize the search index before querying it.");
        await _ready.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        ThrowIfIndexingFailed();
        var settings = _settings.Value;
        var query = request with
        {
            QueryLanguage = string.IsNullOrWhiteSpace(request.QueryLanguage)
                ? ResolveDefaultLanguage(settings)
                : request.QueryLanguage,
            EnableSynonyms = settings.SearchSynonymsEnabled,
            EnableFuzzyMatching = settings.SearchTypoToleranceEnabled
        };
        var response = await _search.SearchAsync(query, cancellationToken).ConfigureAwait(false);
        ThrowIfIndexingFailed();
        return response;
    }

    /// <summary>Replaces the complete derived index and returns the number of indexed documents.</summary>
    internal async Task<int> RebuildAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        await _indexGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var count = await RebuildCurrentSnapshotAsync(cancellationToken).ConfigureAwait(false);
            Volatile.Write(ref _failure, null);
            _ready.TrySetResult();
            return count;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Explicitly cancelling a manual rebuild preserves the previous committed snapshot.
            throw;
        }
        catch (Exception exception)
        {
            Volatile.Write(ref _failure, exception);
            _ready.TrySetResult();
            throw;
        }
        finally
        {
            _indexGate.Release();
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _shutdown.Cancel();
        if (_worker is not null) await _worker.ConfigureAwait(false);
        _ready.TrySetCanceled();
        await _indexGate.WaitAsync().ConfigureAwait(false);
        try { await _search.DisposeAsync().ConfigureAwait(false); }
        finally { _indexGate.Release(); }
        _shutdown.Dispose();
    }

    private async Task EnsureCurrentAsync(CancellationToken cancellationToken)
    {
        await _indexGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var sourceRevision = _store.GetSearchSourceRevision();
            var committedRevision = _search.CommittedSourceRevision;
            if (sourceRevision == committedRevision)
            {
                return;
            }

            if (committedRevision > sourceRevision)
            {
                _ = await RebuildCurrentSnapshotAsync(cancellationToken).ConfigureAwait(false);
                return;
            }

            // Drain bounded batches through this captured revision. Large ordinary backlogs do
            // not trigger a full rebuild, and concurrent new writes cannot move our checkpoint.
            while (committedRevision < sourceRevision)
            {
                var changes = _store.LoadSearchSourceChanges(committedRevision, MaximumIncrementalChanges)
                    .TakeWhile(change => change.Revision <= sourceRevision).ToArray();
                if (!CanReplayIncrementally(changes, committedRevision, sourceRevision))
                {
                    _ = await RebuildCurrentSnapshotAsync(cancellationToken).ConfigureAwait(false);
                    return;
                }

                var mutations = BuildIncrementalMutations(changes, cancellationToken);
                if (mutations is null)
                {
                    _ = await RebuildCurrentSnapshotAsync(cancellationToken).ConfigureAwait(false);
                    return;
                }

                var batchRevision = changes[^1].Revision;
                await _search.ApplyBatchAsync(mutations, batchRevision, cancellationToken).ConfigureAwait(false);
                _store.PruneSearchSourceChanges(batchRevision);
                committedRevision = batchRevision;
            }
        }
        catch (Exception exception)
        {
            // Publish failures under the same gate as successful rebuilds: a delayed worker
            // continuation must not overwrite recovery with an older failure.
            if (!_shutdown.IsCancellationRequested)
            {
                Volatile.Write(ref _failure, exception);
                _ready.TrySetResult();
            }
            throw;
        }
        finally
        {
            _indexGate.Release();
        }
    }

    /// <summary>Commits pending source deletions before a destructive operation can report success.</summary>
    internal Task SynchronizeAsync(CancellationToken cancellationToken) => Task.Run(async () =>
    {
        ThrowIfDisposed();
        await EnsureCurrentAsync(cancellationToken).ConfigureAwait(false);
        _ready.TrySetResult();
    });

    private void ThrowIfIndexingFailed()
    {
        if (Volatile.Read(ref _failure) is { } failure)
            throw new InvalidOperationException("Search indexing failed. Rebuild the local index to recover.", failure);
    }

    private async Task<int> RebuildCurrentSnapshotAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var sourceRevision = _store.GetSearchSourceRevision();
        var documents = BuildDocuments(cancellationToken);
        await _search.RebuildAsync(documents, sourceRevision, cancellationToken).ConfigureAwait(false);
        _store.PruneSearchSourceChanges(sourceRevision);
        return documents.Count;
    }

    private static bool CanReplayIncrementally(
        IReadOnlyList<SearchSourceChange> changes,
        long committedRevision,
        long sourceRevision)
    {
        if (changes.Count == 0 || changes.Count > MaximumIncrementalChanges)
        {
            return false;
        }

        var expectedRevision = committedRevision + 1;
        foreach (var change in changes)
        {
            if (change.Revision != expectedRevision++)
            {
                return false;
            }
        }

        return changes[^1].Revision <= sourceRevision
            && changes.All(change => !string.Equals(change.Kind, "rebuild", StringComparison.Ordinal));
    }

    private IReadOnlyCollection<SearchIndexMutation>? BuildIncrementalMutations(
        IReadOnlyList<SearchSourceChange> changes,
        CancellationToken cancellationToken)
    {
        // Capture deletion lacks its former artifact identities and explicitly requires a rebuild.
        if (changes.Any(change => change.Kind == "capture" && change.Operation == "delete")) return null;
        var latestChanges = changes.GroupBy(change => (change.Kind, change.EntityId))
            .Select(group => group.Last()).OrderBy(change => change.Revision).ToArray();
        var captureIds = latestChanges.Where(change => change.Kind is "capture" or "screenshot")
            .Select(change => change.Kind == "capture" ? change.EntityId : LocalStore.TryGetCaptureId(change.EntityId))
            .Where(id => id is not null).Cast<string>().Distinct(StringComparer.Ordinal).ToArray();
        var gallery = _store.GetScreenshotGalleryItemsForCaptures(captureIds, cancellationToken);
        var galleryByIdentity = gallery.ToDictionary(item => ScreenshotIdentity(item.Path), StringComparer.OrdinalIgnoreCase);
        var galleryByCapture = gallery.ToLookup(item => LocalStore.TryGetCaptureId(ScreenshotIdentity(item.Path)), StringComparer.Ordinal);
        var settings = _settings.Value;
        var defaultLanguage = ResolveDefaultLanguage(settings);
        var installationProfiles = _store.GetInstallationProfiles()
            .ToDictionary(profile => profile.InstallationId, StringComparer.Ordinal);
        InstallationProfile RequireInstallation(string installationId) =>
            installationProfiles.TryGetValue(installationId, out var profile)
                ? profile
                : throw new InvalidDataException($"Search source references an unknown installation '{installationId}'.");

        var mutations = new Dictionary<string, SearchIndexMutation>(StringComparer.Ordinal);
        void Set(SearchIndexMutation mutation) => mutations[mutation.Id] = mutation;

        foreach (var change in latestChanges)
        {
            cancellationToken.ThrowIfCancellationRequested();
            switch (change.Kind)
            {
                case "activity":
                    if (!long.TryParse(change.EntityId, NumberStyles.None, CultureInfo.InvariantCulture, out var activityId)
                        || activityId <= 0)
                    {
                        return null;
                    }

                    var activityDocumentId = $"activity:{activityId}";
                    var sample = string.Equals(change.Operation, "delete", StringComparison.Ordinal)
                        ? null
                        : _store.LoadActivitySample(activityId);
                    Set(sample is null
                        ? SearchIndexMutation.Delete(activityDocumentId)
                        : SearchIndexMutation.Upsert(BuildActivityDocument(
                            activityId,
                            sample,
                            defaultLanguage,
                            RequireInstallation(sample.InstallationId))));
                    break;

                case "analysis":
                    if (string.IsNullOrWhiteSpace(change.EntityId))
                    {
                        return null;
                    }

                    var analysisDocumentId = $"analysis:{change.EntityId}";
                    var analysis = string.Equals(change.Operation, "delete", StringComparison.Ordinal)
                        ? null
                        : _store.LoadAiAnalysis(change.EntityId);
                    Set(analysis is null
                        ? SearchIndexMutation.Delete(analysisDocumentId)
                        : SearchIndexMutation.Upsert(BuildAnalysisDocument(
                            analysis,
                            defaultLanguage,
                            RequireInstallation(analysis.InstallationId))));
                    break;

                case "screenshot":
                    if (string.IsNullOrWhiteSpace(change.EntityId))
                    {
                        return null;
                    }

                    Set(SearchIndexMutation.Delete($"screenshot:{change.EntityId}"));
                    Set(SearchIndexMutation.Delete($"screenshot-text:{change.EntityId}"));
                    // A text or telemetry delete does not imply that the retained image disappeared.
                    // Re-read the authoritative artifact state for every mutation so analysis-only
                    // deletion replaces the searchable document instead of removing it.
                    if (galleryByIdentity.TryGetValue(change.EntityId, out var current))
                    {
                        Set(SearchIndexMutation.Upsert(BuildScreenshotDocument(current, defaultLanguage)));
                    }
                    else if (!string.Equals(change.Operation, "delete", StringComparison.Ordinal)
                        && _store.LoadScreenshotTextSnapshotByIdentity(change.EntityId) is { } text)
                    {
                        Set(SearchIndexMutation.Upsert(BuildOrphanTextDocument(change.EntityId, null, text, defaultLanguage)));
                    }
                    break;

                // A capture deletion does not contain the former artifact identities. Rebuild is
                // the only deterministic way to remove every derived document in that case.
                case "capture":
                    if (string.Equals(change.Operation, "delete", StringComparison.Ordinal))
                    {
                        return null;
                    }

                    foreach (var item in galleryByCapture[change.EntityId])
                    {
                        Set(SearchIndexMutation.Delete($"screenshot-text:{ScreenshotIdentity(item.Path)}"));
                        Set(SearchIndexMutation.Upsert(BuildScreenshotDocument(item, defaultLanguage)));
                    }

                    foreach (var pair in _store.LoadScreenshotTextSnapshotsForCapture(change.EntityId, cancellationToken))
                    {
                        if (!mutations.ContainsKey($"screenshot:{pair.Key}"))
                        {
                            Set(SearchIndexMutation.Upsert(BuildOrphanTextDocument(
                                pair.Key,
                                change.EntityId,
                                pair.Value,
                                defaultLanguage)));
                        }
                    }
                    break;

                default:
                    return null;
            }
        }

        return mutations.Values.OrderBy(mutation => mutation.Id, StringComparer.Ordinal).ToArray();
    }

    private IReadOnlyList<SearchDocument> BuildDocuments(CancellationToken cancellationToken)
    {
        var documents = new List<SearchDocument>();
        var settings = _store.LoadSettings();
        var defaultLanguage = ResolveDefaultLanguage(settings);
        var installationProfiles = _store.GetInstallationProfiles()
            .ToDictionary(profile => profile.InstallationId, StringComparer.Ordinal);
        InstallationProfile RequireInstallation(string installationId) =>
            installationProfiles.TryGetValue(installationId, out var profile)
                ? profile
                : throw new InvalidDataException($"Search source references an unknown installation '{installationId}'.");

        _store.VisitAllActivitySamples((id, sample) => documents.Add(BuildActivityDocument(
            id,
            sample,
            defaultLanguage,
            RequireInstallation(sample.InstallationId))), cancellationToken);

        var retainedScreenshotIdentities = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        _store.VisitAllScreenshotGalleryItems(item =>
        {
            var artifactIdentity = ScreenshotIdentity(item.Path);
            retainedScreenshotIdentities.Add(artifactIdentity);
            documents.Add(BuildScreenshotDocument(item, defaultLanguage));
        }, cancellationToken);

        _store.VisitScreenshotTextSnapshots((artifactIdentity, captureId, text) =>
        {
            if (retainedScreenshotIdentities.Contains(artifactIdentity))
            {
                return;
            }

            documents.Add(BuildOrphanTextDocument(artifactIdentity, captureId, text, defaultLanguage));
        }, cancellationToken);

        _store.VisitAllAiAnalyses(analysis => documents.Add(BuildAnalysisDocument(
            analysis,
            defaultLanguage,
            RequireInstallation(analysis.InstallationId))), cancellationToken);
        return documents;
    }

    private static SearchDocument BuildActivityDocument(
        long id,
        ActivitySample sample,
        string defaultLanguage,
        InstallationProfile installation) => new()
        {
            Id = $"activity:{id}",
            Kind = "activity",
            Timestamp = sample.Timestamp,
            Language = defaultLanguage,
            Application = sample.Application,
            ProcessName = sample.ProcessName,
            Context = sample.Context,
            WindowTitle = sample.WindowTitle,
            AttributesRaw = AddInstallationAttributes(
            sample.Attributes?.ToImmutableDictionary(
                    pair => pair.Key,
                    pair => (string?)pair.Value,
                    StringComparer.OrdinalIgnoreCase)
                ?? ImmutableDictionary<string, string?>.Empty,
            installation),
            SpanLabels = sample.Attributes is not null
            && sample.Attributes.TryGetValue(ActivityAttributeKeys.SpanLabel, out var label)
            && !string.IsNullOrWhiteSpace(label)
                ? [label.Trim()]
                : []
        };

    private static SearchDocument BuildScreenshotDocument(
        ScreenshotGalleryItem item,
        string defaultLanguage)
    {
        var text = item.TextSnapshot;
        return new SearchDocument
        {
            Id = $"screenshot:{ScreenshotIdentity(item.Path)}",
            Kind = "screenshot",
            Timestamp = item.CapturedAt,
            Language = text?.Ocr.LanguageTag ?? defaultLanguage,
            Application = item.ForegroundApplication,
            WindowTitle = item.ForegroundWindowTitle,
            AttributesRaw = BuildOcrAttributes(
                null,
                text,
                item.ActivityIndex,
                item.MouseClicks,
                item.CpuUsagePercent,
                item.GpuUsagePercent,
                item.Installation ?? throw new InvalidDataException("Screenshot search source has no installation provenance.")),
            SpanLabels = (item.SpanLabels ?? Array.Empty<ActivityLabelSample>())
                .Select(label => label.Label)
                .Where(label => !string.IsNullOrWhiteSpace(label))
                .ToImmutableArray(),
            CaptureKind = item.CaptureKind,
            CaptureOrigin = item.CaptureOrigin,
            CapturePath = item.Path,
            OcrRawText = text?.Ocr.RawText,
            OcrCorrectedText = text?.AiRefinement?.CorrectedText,
            OcrStructuredSummary = StructuredSummary(text),
            AiDescription = item.AiDescriptionMarkdown
        };
    }

    private static SearchDocument BuildOrphanTextDocument(
        string artifactIdentity,
        string? captureId,
        ScreenshotTextSnapshot text,
        string defaultLanguage) => new()
        {
            Id = $"screenshot-text:{artifactIdentity}",
            Kind = "screenshot-text",
            Timestamp = text.Ocr.ExtractedAt,
            Language = text.AiRefinement?.LanguageTag ?? text.Ocr.LanguageTag ?? defaultLanguage,
            AttributesRaw = BuildOcrAttributes(captureId, text, null, null, null, null, null),
            CapturePath = text.SourceScreenshotPath,
            OcrRawText = text.Ocr.RawText,
            OcrCorrectedText = text.AiRefinement?.CorrectedText,
            OcrStructuredSummary = StructuredSummary(text)
        };

    private static SearchDocument BuildAnalysisDocument(
        AiAnalysis analysis,
        string defaultLanguage,
        InstallationProfile installation) => new()
        {
            Id = $"analysis:{analysis.CorrelationId}",
            Kind = "analysis",
            Timestamp = analysis.Timestamp,
            Language = defaultLanguage,
            Application = analysis.Application,
            Context = analysis.Context,
            CaptureOrigin = analysis.Origin,
            AttributesRaw = AddInstallationAttributes(ImmutableDictionary<string, string?>.Empty, installation),
            AiDescription = analysis.Summary
        };

    private static string ResolveDefaultLanguage(AppSettings settings) =>
        ProductLanguageCatalog.ResolveSearchLanguage(settings.SearchLanguage, CultureInfo.CurrentUICulture);

    private static ImmutableDictionary<string, string?> BuildOcrAttributes(
        string? captureId,
        ScreenshotTextSnapshot? text,
        int? activityIndex,
        long? mouseClicks,
        int? cpuUsagePercent,
        int? gpuUsagePercent,
        InstallationProfile? installation)
    {
        var attributes = ImmutableDictionary.CreateBuilder<string, string?>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(captureId))
        {
            attributes["capture.id"] = captureId;
        }

        if (activityIndex is not null)
        {
            attributes["activity.index"] = activityIndex.Value.ToString(CultureInfo.InvariantCulture);
        }

        if (mouseClicks is not null)
        {
            attributes[SearchAttributeKeys.MouseClicks] = mouseClicks.Value.ToString(CultureInfo.InvariantCulture);
        }


        if (cpuUsagePercent is not null)
        {
            attributes[SearchAttributeKeys.CpuUsagePercent] = cpuUsagePercent.Value.ToString(CultureInfo.InvariantCulture);
        }

        if (gpuUsagePercent is not null)
        {
            attributes[SearchAttributeKeys.GpuUsagePercent] = gpuUsagePercent.Value.ToString(CultureInfo.InvariantCulture);
        }

        if (installation is not null)
        {
            AddInstallationAttributes(attributes, installation);
        }

        if (text is null)
        {
            return attributes.ToImmutable();
        }

        attributes["ocr.status"] = text.Ocr.Status.ToString();
        attributes["ocr.engine"] = text.Ocr.Engine;
        attributes["ocr.width"] = text.Ocr.PixelWidth is { } width
            ? width.ToString(CultureInfo.InvariantCulture)
            : null;
        attributes["ocr.height"] = text.Ocr.PixelHeight is { } height
            ? height.ToString(CultureInfo.InvariantCulture)
            : null;
        attributes["ocr.angle"] = text.Ocr.TextAngleDegrees?.ToString(CultureInfo.InvariantCulture);
        attributes["ocr.failure"] = text.Ocr.FailureCode;
        return attributes.ToImmutable();
    }

    private static ImmutableDictionary<string, string?> AddInstallationAttributes(
        ImmutableDictionary<string, string?> attributes,
        InstallationProfile installation)
    {
        var builder = attributes.ToBuilder();
        AddInstallationAttributes(builder, installation);
        return builder.ToImmutable();
    }

    private static void AddInstallationAttributes(
        ImmutableDictionary<string, string?>.Builder attributes,
        InstallationProfile installation)
    {
        var validated = InstallationProfileCatalog.ValidatePersisted(installation);
        attributes[SearchAttributeKeys.InstallationId] = validated.InstallationId;
        attributes[SearchAttributeKeys.InstallationFriendlyName] = validated.FriendlyName;
        attributes[SearchAttributeKeys.InstallationMachineName] = validated.MachineName;
        attributes[SearchAttributeKeys.InstallationColor] = validated.Color;
        attributes[SearchAttributeKeys.InstallationIcon] = validated.Icon;
    }

    private static string? StructuredSummary(ScreenshotTextSnapshot? text) =>
        text?.AiRefinement is null
            ? null
            : JsonSerializer.Serialize(text.AiRefinement.Summary);

    private static string ScreenshotIdentity(string path)
    {
        var identity = Path.GetFileNameWithoutExtension(path);
        return identity.EndsWith("-raw", StringComparison.OrdinalIgnoreCase)
            ? identity[..^4]
            : identity;
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(LocalSearchCoordinator));
        }
    }
}
