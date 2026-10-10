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
using System.Runtime.ExceptionServices;
using Lucene.Net.Index;
using Lucene.Net.Search;
using Lucene.Net.Store;
using Lucene.Net.Util;
using WorkTrail.Search.Internal;

namespace WorkTrail.Search;

/// <summary>
/// Implements mandatory local search with a versioned, reconstructible Lucene index.
/// </summary>
public sealed class LocalSearchService : ILocalSearchService
{
    private const LuceneVersion Version = LuceneVersion.LUCENE_48;
    private const string SchemaCommitKey = "worktrail.search.schema";
    private const string SourceRevisionCommitKey = "worktrail.search.source_revision";
    private readonly SearchOptions _options;
    private readonly LanguageAnalyzerCatalog _analyzers;
    private readonly SynonymCatalog _synonyms;
    private readonly FSDirectory _directory;
    private readonly SearcherManager _searchers;
    private readonly ReaderWriterLockSlim _readerGate = new();
    private readonly SemaphoreSlim _operationGate = new(1, 1);
    private IndexWriter _writer;
    private volatile Exception? _fault;
    private long _committedSourceRevision;
    private int _disposeStarted;

    /// <summary>Gets the current on-disk index schema version.</summary>
    public const int IndexSchemaVersion = 4;

    /// <summary>Gets the versioned directory name created below <see cref="SearchOptions.IndexRootPath"/>.</summary>
    public const string IndexDirectoryName = "lucene-v4";

    /// <summary>
    /// Initializes a local search service and exclusively opens its Lucene writer.
    /// </summary>
    /// <param name="options">Validated storage, paging, synonym, and fuzzy-search options.</param>
    public LocalSearchService(SearchOptions options)
    {
        var rootPath = SearchValidation.ValidateOptions(options);
        _options = options;
        IndexPath = Path.Combine(rootPath, IndexDirectoryName);

        LanguageAnalyzerCatalog? analyzers = null;
        FSDirectory? directory = null;
        SearcherManager? searchers = null;
        IndexWriter? writer = null;
        try
        {
            // Index files are derived data. Directory or writer failures are fatal and surfaced to the caller.
            System.IO.Directory.CreateDirectory(IndexPath);
            analyzers = new LanguageAnalyzerCatalog();
            directory = FSDirectory.Open(new DirectoryInfo(IndexPath));
            var indexExists = DirectoryReader.IndexExists(directory);
            if (indexExists)
            {
                _committedSourceRevision = ValidateExistingSchema(directory);
            }

            writer = CreateWriter(directory, analyzers);
            if (!indexExists)
            {
                StampAndCommit(writer, 0);
            }

            _analyzers = analyzers;
            _directory = directory;
            _writer = writer;
            _synonyms = new SynonymCatalog(options);

            // Readers see explicit commits only, including after writer rollback/recovery.
            searchers = new SearcherManager(directory, null);
            _searchers = searchers;
        }
        catch
        {
            searchers?.Dispose();
            writer?.Rollback();
            directory?.Dispose();
            analyzers?.Dispose();
            _operationGate.Dispose();
            throw;
        }
    }

    /// <summary>Gets the absolute path of the versioned Lucene index.</summary>
    public string IndexPath { get; }

    /// <inheritdoc />
    public long CommittedSourceRevision => Interlocked.Read(ref _committedSourceRevision);

    /// <inheritdoc />
    public async Task ApplyBatchAsync(
        IReadOnlyCollection<SearchIndexMutation> mutations,
        long sourceRevision,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(mutations);
        if (sourceRevision < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sourceRevision));
        }
        var prepared = new List<(string Id, Lucene.Net.Documents.Document? Document)>(mutations.Count);
        foreach (var mutation in mutations)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ArgumentNullException.ThrowIfNull(mutation);
            SearchValidation.ValidateMutationId(mutation.Id, _options);
            if (mutation.Document is not { } document)
            {
                prepared.Add((mutation.Id, null));
                continue;
            }

            SearchValidation.ValidateDocument(document, _options);
            if (!string.Equals(mutation.Id, document.Id, StringComparison.Ordinal))
            {
                throw new ArgumentException("An upsert mutation id must match its document id.", nameof(mutations));
            }

            prepared.Add((mutation.Id, SearchDocumentMapper.ToLucene(document)));
        }

        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfUnavailable();
            cancellationToken.ThrowIfCancellationRequested();
            var committedRevision = CommittedSourceRevision;
            if (sourceRevision < committedRevision
                || (prepared.Count > 0 && sourceRevision == committedRevision))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(sourceRevision),
                    "A non-empty batch must advance the source revision, and revisions cannot move backwards.");
            }

            if (prepared.Count == 0 && sourceRevision == committedRevision)
            {
                return;
            }

            var uniqueChanges = prepared.GroupBy(change => change.Id, StringComparer.Ordinal).Select(group => group.Last()).ToList();
            CommitWrite(() =>
            {
                foreach (var mutation in uniqueChanges)
                {
                    var term = new Term(SearchFields.IdKey, mutation.Id);
                    if (mutation.Document is null)
                    {
                        _writer.DeleteDocuments(term);
                    }
                    else
                    {
                        _writer.UpdateDocument(term, mutation.Document);
                    }
                }
            }, sourceRevision);
        }
        finally
        {
            _operationGate.Release();
        }
    }

    /// <inheritdoc />
    public async Task RebuildAsync(
        IEnumerable<SearchDocument> documents,
        long sourceRevision,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(documents);
        if (sourceRevision < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sourceRevision));
        }

        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfUnavailable();
            cancellationToken.ThrowIfCancellationRequested();
            if (sourceRevision < CommittedSourceRevision)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(sourceRevision),
                    "A rebuild source revision cannot move backwards.");
            }

            CommitWrite(() =>
            {
                _writer.DeleteAll();
                var identifiers = new HashSet<string>(StringComparer.Ordinal);
                foreach (var document in documents)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    SearchValidation.ValidateDocument(document, _options);
                    if (!identifiers.Add(document.Id))
                    {
                        throw new ArgumentException(
                            $"The rebuild source contains duplicate document id '{document.Id}'.",
                            nameof(documents));
                    }

                    // Add each mapped document directly to the writer. Holding a second list of
                    // every Lucene document was the largest avoidable allocation during a full
                    // OCR index rebuild.
                    _writer.AddDocument(SearchDocumentMapper.ToLucene(document));
                }
            }, sourceRevision);
        }
        finally
        {
            _operationGate.Release();
        }
    }

    /// <inheritdoc />
    public Task<SearchResponse> SearchAsync(
        SearchRequest request,
        CancellationToken cancellationToken = default)
    {
        var limit = SearchValidation.ValidateRequest(request, _options);

        ThrowIfUnavailable();
        // Concurrent reads reuse one committed snapshot. Only publication/disposal needs the
        // write lock, so preparing and committing an index batch does not queue queries behind it.
        _readerGate.EnterReadLock();
        try
        {
            ThrowIfUnavailable();
            cancellationToken.ThrowIfCancellationRequested();

            var query = new SearchQueryBuilder(_options, _analyzers, _synonyms).Build(request);
            var searcher = _searchers.Acquire();
            try
            {
                var topDocuments = searcher.Search(query, checked(request.Offset + limit));
                var hits = ImmutableArray.CreateBuilder<SearchHit>(Math.Min(limit, topDocuments.ScoreDocs.Length));

                foreach (var scoreDocument in topDocuments.ScoreDocs.Skip(request.Offset).Take(limit))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var document = SearchDocumentMapper.FromLucene(searcher.Doc(scoreDocument.Doc));
                    hits.Add(new SearchHit
                    {
                        Document = request.IncludeTextContent ? document : WithoutTextContent(document),
                        Score = scoreDocument.Score,
                    });
                }

                cancellationToken.ThrowIfCancellationRequested();
                return Task.FromResult(new SearchResponse
                {
                    Hits = hits.MoveToImmutable(),
                    TotalCount = topDocuments.TotalHits,
                    Offset = request.Offset,
                });
            }
            finally
            {
                _searchers.Release(searcher);
            }
        }
        finally
        {
            _readerGate.ExitReadLock();
        }
    }

    /// <summary>
    /// Closes the exclusive writer and directory after any active operation completes.
    /// </summary>
    /// <returns>A task-like value representing disposal completion.</returns>
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposeStarted, 1) != 0)
        {
            return;
        }

        await _operationGate.WaitAsync().ConfigureAwait(false);
        _readerGate.EnterWriteLock();
        try
        {
            _searchers.Dispose();
            _writer.Dispose();
            _directory.Dispose();
            _analyzers.Dispose();
        }
        finally
        {
            _readerGate.ExitWriteLock();
            _operationGate.Release();
            GC.SuppressFinalize(this);
        }
    }

    private static IndexWriter CreateWriter(FSDirectory directory, LanguageAnalyzerCatalog analyzers)
    {
        var configuration = new IndexWriterConfig(Version, analyzers.IndexAnalyzer)
        {
            OpenMode = OpenMode.CREATE_OR_APPEND,
        };
        return new IndexWriter(directory, configuration);
    }

    private static SearchDocument WithoutTextContent(SearchDocument document) => document with
    {
        ProcessName = null,
        Context = null,
        WindowTitle = null,
        AttributesRaw = [],
        SpanLabels = [],
        OcrRawText = null,
        OcrCorrectedText = null,
        OcrStructuredSummary = null,
        AiDescription = null,
    };

    private static long ValidateExistingSchema(FSDirectory directory)
    {
        using var reader = DirectoryReader.Open(directory);
        var userData = reader.IndexCommit.UserData;
        if (!userData.TryGetValue(SchemaCommitKey, out var version) ||
            !string.Equals(version, IndexSchemaVersion.ToString(), StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"The Lucene index does not declare supported schema version {IndexSchemaVersion}. Rebuild it from source data.");
        }

        if (!userData.TryGetValue(SourceRevisionCommitKey, out var revision)
            || !long.TryParse(revision, NumberStyles.None, CultureInfo.InvariantCulture, out var parsedRevision)
            || parsedRevision < 0)
        {
            throw new InvalidDataException(
                "The Lucene index does not declare a valid non-negative source revision. Rebuild it from source data.");
        }

        return parsedRevision;
    }

    private static void StampAndCommit(IndexWriter writer, long sourceRevision)
    {
        writer.SetCommitData(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [SchemaCommitKey] = IndexSchemaVersion.ToString(),
            [SourceRevisionCommitKey] = sourceRevision.ToString(CultureInfo.InvariantCulture),
        });
        writer.Commit();
    }

    private void CommitWrite(Action mutation, long sourceRevision)
    {
        try
        {
            mutation();
            StampAndCommit(_writer, sourceRevision);
        }
        catch (Exception operationException)
        {
            try
            {
                // Rollback preserves the last explicit commit; reopening is the only supported recovery path.
                _writer.Rollback();
                _writer = CreateWriter(_directory, _analyzers);
            }
            catch (Exception recoveryException)
            {
                _fault = new AggregateException(
                    "The Lucene mutation failed and the last committed index could not be reopened.",
                    operationException,
                    recoveryException);
                throw _fault;
            }

            ExceptionDispatchInfo.Capture(operationException).Throw();
            throw;
        }

        _readerGate.EnterWriteLock();
        try
        {
            // Publishing waits for old readers to finish. Once a deletion synchronizes, no
            // subsequent query can acquire a snapshot that still contains deleted documents.
            _searchers.MaybeRefreshBlocking();
            Interlocked.Exchange(ref _committedSourceRevision, sourceRevision);
        }
        catch (Exception exception)
        {
            // A committed write with failed reader publication must never serve a stale snapshot.
            _fault = exception;
            throw;
        }
        finally
        {
            _readerGate.ExitWriteLock();
        }
    }

    private void ThrowIfUnavailable()
    {
        if (Volatile.Read(ref _disposeStarted) != 0)
        {
            throw new ObjectDisposedException(nameof(LocalSearchService));
        }

        if (_fault is not null)
        {
            throw new InvalidOperationException("The local search service is faulted.", _fault);
        }
    }
}
