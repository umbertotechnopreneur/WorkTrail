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
using Lucene.Net.Analysis.Standard;
using Lucene.Net.Index;
using Lucene.Net.Util;
using WorkTrail.Search;
using Xunit;
using FSDirectory = Lucene.Net.Store.FSDirectory;

namespace WorkTrail.Search.Tests;

public sealed class LocalSearchServiceTests
{
    /// <summary>Queries keep reading the committed snapshot while the next index is built.</summary>
    [Fact]
    public async Task SearchAsync_ReadsCommittedSnapshotDuringBlockedRebuild()
    {
        await using var harness = new SearchHarness();
        await harness.Service.RebuildAsync([CreateDocument("old") with { Context = "committedmarker" }], 1);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        IEnumerable<SearchDocument> BlockedDocuments()
        {
            yield return CreateDocument("new") with { Context = "replacementmarker" };
            entered.SetResult();
            release.Task.GetAwaiter().GetResult();
        }

        var rebuild = Task.Run(() => harness.Service.RebuildAsync(BlockedDocuments(), 2));
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            var reads = Enumerable.Range(0, 8).Select(_ => Task.Run(() => harness.Service.SearchAsync(
                new SearchRequest { Text = "committedmarker" })));
            var responses = await Task.WhenAll(reads).WaitAsync(TimeSpan.FromSeconds(5));
            Assert.All(responses, response => Assert.Equal("old", Assert.Single(response.Hits).Document.Id));
            Assert.Equal(1, harness.Service.CommittedSourceRevision);
        }
        finally
        {
            release.TrySetResult();
            await rebuild.WaitAsync(TimeSpan.FromSeconds(10));
        }

        Assert.Empty((await harness.Service.SearchAsync(new SearchRequest { Text = "committedmarker" })).Hits);
        Assert.Equal("new", Assert.Single((await harness.Service.SearchAsync(new SearchRequest { Text = "replacementmarker" })).Hits).Document.Id);
    }

    /// <summary>Rollback keeps the shared reader usable and later commits publish normally.</summary>
    [Fact]
    public async Task FailedRebuild_PreservesSharedReaderAndAllowsNextCommit()
    {
        await using var harness = new SearchHarness();
        var original = CreateDocument("original") with { Context = "originalmarker" };
        await harness.Service.RebuildAsync([original], 1);

        await Assert.ThrowsAsync<ArgumentException>(() => harness.Service.RebuildAsync([original, original], 2));
        Assert.Equal("original", Assert.Single((await harness.Service.SearchAsync(new SearchRequest { Text = "originalmarker" })).Hits).Document.Id);
        await harness.Service.ApplyBatchAsync([SearchIndexMutation.Delete("original")], 2);
        Assert.Empty((await harness.Service.SearchAsync(new SearchRequest { Text = "originalmarker" })).Hits);
    }

    /// <summary>The current v4 index leaves obsolete derived indexes untouched and writes only source documents.</summary>
    [Fact]
    public async Task SourceOnlySchema_PreservesOldDerivedIndexAndContainsNoSuggestionDocuments()
    {
        var root = SearchHarness.CreateRoot();
        try
        {
            var oldIndex = Path.Combine(root, "lucene-v3");
            Directory.CreateDirectory(oldIndex);
            File.WriteAllText(Path.Combine(oldIndex, "synthetic-suggestion.txt"), "obsolete test data");
            await using var service = new LocalSearchService(new SearchOptions { IndexRootPath = root });
            await service.RebuildAsync([CreateDocument("one"), CreateDocument("two")], 1);

            Assert.True(Directory.Exists(oldIndex));
            Assert.Equal(4, LocalSearchService.IndexSchemaVersion);
            using var directory = FSDirectory.Open(new DirectoryInfo(service.IndexPath));
            using var reader = DirectoryReader.Open(directory);
            Assert.Equal(2, reader.NumDocs);
            Assert.All(Enumerable.Range(0, reader.MaxDoc), id => Assert.Null(reader.Document(id).Get("suggestion_text")));
        }
        finally { SearchHarness.DeleteRoot(root); }
    }

    [Fact]
    public async Task ApplyBatchAsync_AppliesOrderedUpsertsAndDeletes()
    {
        await using var harness = new SearchHarness();
        await harness.RebuildAsync(
        [
            CreateDocument("replace-me") with { Context = "old context" },
            CreateDocument("delete-me") with { Context = "obsolete context" }
        ]);

        await harness.Service.ApplyBatchAsync(
        [
            SearchIndexMutation.Upsert(CreateDocument("replace-me") with { Context = "new context" }),
            SearchIndexMutation.Delete("delete-me")
        ], 7);

        var replacement = await harness.Service.SearchAsync(new SearchRequest { Text = "new context", QueryLanguage = "en" });
        var deleted = await harness.Service.SearchAsync(new SearchRequest { Text = "obsolete context", QueryLanguage = "en" });
        Assert.Equal("replace-me", Assert.Single(replacement.Hits).Document.Id);
        Assert.Empty(deleted.Hits);
        Assert.Equal(7, harness.Service.CommittedSourceRevision);
    }

    [Fact]
    public async Task RebuildAsync_RejectsABackwardSourceRevision()
    {
        await using var harness = new SearchHarness();
        await harness.Service.ApplyBatchAsync(
            [SearchIndexMutation.Upsert(CreateDocument("current") with { Context = "current marker" })],
            5);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => harness.Service.RebuildAsync([], 4));

        Assert.Equal(5, harness.Service.CommittedSourceRevision);
        Assert.Equal(
            "current",
            Assert.Single((await harness.Service.SearchAsync(new SearchRequest { Text = "current marker" })).Hits).Document.Id);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("-1")]
    [InlineData("not-a-revision")]
    public async Task Constructor_RejectsMissingOrInvalidCommittedSourceRevision(string? invalidRevision)
    {
        var root = SearchHarness.CreateRoot();
        var options = new SearchOptions { IndexRootPath = root };
        try
        {
            await using (var service = new LocalSearchService(options))
            {
                Assert.Equal(0, service.CommittedSourceRevision);
            }

            using (var directory = FSDirectory.Open(new DirectoryInfo(Path.Combine(
                root,
                LocalSearchService.IndexDirectoryName))))
            using (var analyzer = new StandardAnalyzer(LuceneVersion.LUCENE_48))
            using (var writer = new IndexWriter(
                directory,
                new IndexWriterConfig(LuceneVersion.LUCENE_48, analyzer)
                {
                    OpenMode = OpenMode.APPEND,
                }))
            {
                var commitData = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["worktrail.search.schema"] = LocalSearchService.IndexSchemaVersion.ToString(
                        CultureInfo.InvariantCulture),
                };
                if (invalidRevision is not null)
                {
                    commitData["worktrail.search.source_revision"] = invalidRevision;
                }

                writer.SetCommitData(commitData);
                writer.Commit();
            }

            Assert.Throws<InvalidDataException>(() => new LocalSearchService(options));
        }
        finally
        {
            SearchHarness.DeleteRoot(root);
        }
    }

    [Fact]
    public async Task SearchAsync_SearchesEveryRawFieldWithoutAiDescription()
    {
        await using var harness = new SearchHarness();
        var document = CreateDocument("raw-fields") with
        {
            Application = "LibreOffice Calc",
            ProcessName = "soffice.bin",
            Context = "Bilancio trimestrale",
            WindowTitle = "Forecast Q3",
            AttributesRaw = ImmutableDictionary<string, string?>.Empty
                .Add("Workbook", "RevenuePlan.xlsx"),
            SpanLabels = ["Finance review"],
            CaptureKind = "active-window",
            CaptureOrigin = "manual-capture",
            CapturePath = @"C:\captures\quarterly-budget.png",
            OcrRawText = "Totale vendite Europa",
            OcrCorrectedText = "Totale delle vendite europee",
            OcrStructuredSummary = "Categoria: ricavi",
            AiDescription = null,
        };
        await harness.UpsertAsync(document);

        var queries = new[]
        {
            "libreoffice",
            "soffice",
            "bilancio",
            "forecast",
            "revenueplan",
            "finance review",
            "active-window",
            "manual-capture",
            @"C:\captures\quarterly-budget.png",
            "vendite europa",
            "vendite europee",
            "categoria ricavi",
        };

        foreach (var query in queries)
        {
            var response = await harness.Service.SearchAsync(new SearchRequest
            {
                Text = query,
                QueryLanguage = "it",
            });

            Assert.True(
                response.Hits.Any(hit => hit.Document.Id == document.Id),
                $"Expected query '{query}' to match the raw-fields document.");
        }

        var stored = Assert.Single((await harness.Service.SearchAsync(new SearchRequest
        {
            Text = "revenueplan",
            QueryLanguage = "it",
        })).Hits).Document;
        Assert.Null(stored.AiDescription);
        Assert.Equal("RevenuePlan.xlsx", stored.AttributesRaw["Workbook"]);
        Assert.Equal(new[] { "Finance review" }, stored.SpanLabels.ToArray());
        Assert.Equal("Totale vendite Europa", stored.OcrRawText);
    }

    [Fact]
    public async Task SearchAsync_NormalizesCaseAndDiacritics()
    {
        await using var harness = new SearchHarness();
        await harness.UpsertAsync(CreateDocument("diacritics") with
        {
            Language = "it-IT",
            Context = "Riunione al Caffè già confermata",
        });

        var response = await harness.Service.SearchAsync(new SearchRequest
        {
            Text = "CAFFE GIA",
            QueryLanguage = "IT",
        });

        Assert.Equal("diacritics", Assert.Single(response.Hits).Document.Id);
    }

    [Fact]
    public async Task SearchAsync_UsesAllSupportedLanguageAnalyzers()
    {
        await using var harness = new SearchHarness();
        var cases = new[]
        {
            (Language: "it-IT", Indexed: "fatture", Query: "fattura"),
            (Language: "en-US", Indexed: "reports", Query: "report"),
            (Language: "fr-FR", Indexed: "factures", Query: "facture"),
            (Language: "de-DE", Indexed: "Rechnungen", Query: "Rechnung"),
            (Language: "es-ES", Indexed: "facturas", Query: "factura"),
            (Language: "vi-VN", Indexed: "cuộc họp điện thoại", Query: "CUOC HOP DIEN THOAI"),
            (Language: "zh-Hans", Indexed: "项目会议", Query: "项目"),
            (Language: "ko-KR", Indexed: "프로젝트회의", Query: "프로젝트"),
            (Language: "pt-PT", Indexed: "faturas", Query: "fatura"),
            (Language: "pt-BR", Indexed: "relatórios", Query: "relatório"),
        };

        foreach (var item in cases)
        {
            await harness.UpsertAsync(CreateDocument($"language-{item.Language}") with
            {
                Language = item.Language,
                Context = item.Indexed,
            });
        }

        foreach (var item in cases)
        {
            var response = await harness.Service.SearchAsync(new SearchRequest
            {
                Text = item.Query,
                QueryLanguage = item.Language,
            });
            Assert.Contains(response.Hits, hit => hit.Document.Id == $"language-{item.Language}");
        }
    }

    [Fact]
    public async Task SearchAsync_UsesUnicodeFallbackForUnknownLanguage()
    {
        await using var harness = new SearchHarness();
        await harness.UpsertAsync(CreateDocument("unicode-fallback") with
        {
            Language = "pl",
            Context = "Zażółć gęślą jaźń",
        });

        var response = await harness.Service.SearchAsync(new SearchRequest
        {
            Text = "ZAZOLC GESLA",
            QueryLanguage = "pl",
        });

        Assert.Equal("unicode-fallback", Assert.Single(response.Hits).Document.Id);
    }

    [Fact]
    public async Task SearchAsync_ExpandsConfiguredSynonymsAtQueryTime()
    {
        await using var harness = new SearchHarness(options => options with
        {
            SynonymSets =
            [
                new SearchSynonymSet
                {
                    Language = "it",
                    Terms = ["computer", "pc", "calcolatore"],
                },
            ],
        });
        await harness.UpsertAsync(CreateDocument("synonym") with
        {
            Language = "it",
            Context = "Configurazione del computer aziendale",
        });

        var response = await harness.Service.SearchAsync(new SearchRequest
        {
            Text = "pc",
            QueryLanguage = "it",
        });

        Assert.Equal("synonym", Assert.Single(response.Hits).Document.Id);
    }

    /// <summary>Several equivalent phrases retrieve matching context without dropping other query words.</summary>
    [Fact]
    public async Task SearchAsync_ExpandsEmbeddedPhrasesAndMultipleSpans()
    {
        await using var harness = new SearchHarness(options => options with
        {
            SynonymSets =
            [
                new SearchSynonymSet { Language = "it-IT", Terms = ["copia di sicurezza", "backup"] },
                new SearchSynonymSet { Language = "it-IT", Terms = ["scadenza", "deadline"] }
            ]
        });
        await harness.RebuildAsync(
        [
            CreateDocument("relevant") with { Language = "it-IT", Context = "backup progetto deadline" },
            CreateDocument("unrelated") with { Language = "it-IT", Context = "backup personale deadline" }
        ]);
        var request = new SearchRequest
        {
            Text = "copia di sicurezza progetto scadenza",
            QueryLanguage = "it-IT",
            EnableFuzzyMatching = false
        };

        Assert.Equal("relevant", Assert.Single((await harness.Service.SearchAsync(request)).Hits).Document.Id);
        Assert.Empty((await harness.Service.SearchAsync(request with { EnableSynonyms = false })).Hits);
    }

    /// <summary>Many matching aliases do not outrank a literal match in comparable fields.</summary>
    [Fact]
    public async Task SearchAsync_PrefersExactMatchToManyAliases()
    {
        await using var harness = new SearchHarness(options => options with
        {
            SynonymSets =
            [
                new SearchSynonymSet { Language = "en-US", Terms = ["screenshot", "screen capture", "screen grab", "screencap", "screen shot"] }
            ]
        });
        await harness.RebuildAsync(
        [
            CreateDocument("exact") with { Language = "en-US", Context = "screenshot" },
            CreateDocument("aliases") with { Language = "en-US", Context = "screen capture screen grab screencap screen shot" }
        ]);

        var response = await harness.Service.SearchAsync(new SearchRequest
        {
            Text = "screenshot",
            QueryLanguage = "en-US",
            EnableFuzzyMatching = false
        });

        Assert.Equal(2, response.TotalCount);
        Assert.Equal("exact", response.Hits[0].Document.Id);
        Assert.True(response.Hits[0].Score > response.Hits[1].Score);
    }

    [Fact]
    public async Task SearchAsync_CanDisableConfiguredSynonymsPerRequest()
    {
        await using var harness = new SearchHarness(options => options with
        {
            SynonymSets =
            [
                new SearchSynonymSet
                {
                    Language = "it",
                    Terms = ["computer", "pc"],
                },
            ],
        });
        await harness.UpsertAsync(CreateDocument("synonym-disabled") with
        {
            Language = "it",
            Context = "computer aziendale",
        });

        var response = await harness.Service.SearchAsync(new SearchRequest
        {
            Text = "pc",
            QueryLanguage = "it",
            EnableSynonyms = false,
        });

        Assert.Empty(response.Hits);
    }

    [Fact]
    public async Task SearchAsync_MatchesControlledTypo()
    {
        await using var harness = new SearchHarness();
        await harness.UpsertAsync(CreateDocument("typo") with
        {
            Language = "it",
            WindowTitle = "Fatturazione cliente",
        });

        var response = await harness.Service.SearchAsync(new SearchRequest
        {
            Text = "fatturazone",
            QueryLanguage = "it",
        });

        Assert.Equal("typo", Assert.Single(response.Hits).Document.Id);
    }

    [Fact]
    public async Task SearchAsync_CanDisableTypoTolerancePerRequest()
    {
        await using var harness = new SearchHarness();
        await harness.UpsertAsync(CreateDocument("typo-disabled") with
        {
            Language = "it",
            WindowTitle = "Fatturazione cliente",
        });

        var response = await harness.Service.SearchAsync(new SearchRequest
        {
            Text = "fatturazone",
            QueryLanguage = "it",
            EnableFuzzyMatching = false,
        });

        Assert.Empty(response.Hits);
    }

    [Fact]
    public async Task SearchAsync_RanksExactThenSynonymThenFuzzyMatches()
    {
        await using var harness = new SearchHarness(options => options with
        {
            SynonymSets =
            [
                new SearchSynonymSet
                {
                    Language = "en",
                    Terms = ["notebook", "laptop"],
                },
            ],
        });
        await harness.RebuildAsync(
        [
            CreateDocument("exact") with { Context = "notebook" },
            CreateDocument("synonym") with { Context = "laptop" },
            CreateDocument("fuzzy") with { Context = "notebokk" },
        ]);

        var response = await harness.Service.SearchAsync(new SearchRequest
        {
            Text = "notebook",
            QueryLanguage = "en",
        });

        Assert.Equal(new[] { "exact", "synonym", "fuzzy" }, response.Hits.Select(hit => hit.Document.Id).ToArray());
        Assert.True(response.Hits[0].Score > response.Hits[1].Score);
        Assert.True(response.Hits[1].Score > response.Hits[2].Score);
    }

    [Theory]
    [InlineData("cat", "cot")]
    [InlineData("12345", "12346")]
    [InlineData("report-final.docx", "report-finel.docx")]
    public async Task SearchAsync_DoesNotFuzzyMatchUnsafeTerms(string indexed, string query)
    {
        await using var harness = new SearchHarness();
        await harness.UpsertAsync(CreateDocument("not-fuzzy") with { Context = indexed });

        var response = await harness.Service.SearchAsync(new SearchRequest { Text = query });

        Assert.Empty(response.Hits);
    }

    [Fact]
    public async Task SearchAsync_AppliesKindAndDateFilters()
    {
        await using var harness = new SearchHarness();
        var start = new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero);
        await harness.RebuildAsync(
        [
            CreateDocument("before", "activity", start) with { Context = "budget" },
            CreateDocument("match", "screenshot", start.AddHours(2)) with { Context = "budget" },
            CreateDocument("exclusive-end", "screenshot", start.AddHours(3)) with { Context = "budget" },
            CreateDocument("wrong-kind", "activity", start.AddHours(2)) with { Context = "budget" },
        ]);

        var response = await harness.Service.SearchAsync(new SearchRequest
        {
            Text = "budget",
            Kinds = ImmutableHashSet.Create("SCREENSHOT"),
            FromInclusive = start.AddHours(1),
            ToExclusive = start.AddHours(3),
        });

        Assert.Equal("match", Assert.Single(response.Hits).Document.Id);
    }

    [Fact]
    public async Task SearchAsync_CanReturnMetadataOnlyForBoundedUiTransport()
    {
        await using var harness = new SearchHarness();
        await harness.UpsertAsync(CreateDocument("metadata-only", "screenshot") with
        {
            Application = "Teams",
            CapturePath = @"C:\captures\meeting.png",
            OcrRawText = new string('x', 10_000),
            AiDescription = "Private analysis body"
        });

        var response = await harness.Service.SearchAsync(new SearchRequest
        {
            Text = "private",
            Kinds = ImmutableHashSet.Create("screenshot"),
            IncludeTextContent = false,
            Limit = 20
        });

        var document = Assert.Single(response.Hits).Document;
        Assert.Equal("metadata-only", document.Id);
        Assert.Equal("Teams", document.Application);
        Assert.Equal(@"C:\captures\meeting.png", document.CapturePath);
        Assert.Null(document.OcrRawText);
        Assert.Null(document.AiDescription);
        Assert.Empty(document.AttributesRaw);
        Assert.Empty(document.SpanLabels);
    }

    [Fact]
    public async Task ApplyBatchAsync_ReplacesDocumentWithSameCaseSensitiveId()
    {
        await using var harness = new SearchHarness();
        await harness.UpsertAsync(CreateDocument("replace") with { Context = "obsolete" });
        await harness.UpsertAsync(CreateDocument("replace") with { Context = "current" });

        Assert.Empty((await harness.Service.SearchAsync(new SearchRequest { Text = "obsolete" })).Hits);
        Assert.Equal(
            "replace",
            Assert.Single((await harness.Service.SearchAsync(new SearchRequest { Text = "current" })).Hits).Document.Id);
    }

    [Fact]
    public async Task ApplyBatchAsync_RemovesCommittedDocument()
    {
        await using var harness = new SearchHarness();
        await harness.UpsertAsync(CreateDocument("delete") with { Context = "erasable" });

        await harness.DeleteAsync("delete");

        Assert.Empty((await harness.Service.SearchAsync(new SearchRequest { Text = "erasable" })).Hits);
    }

    [Fact]
    public async Task RebuildAsync_ReplacesCompleteIndex()
    {
        await using var harness = new SearchHarness();
        await harness.UpsertAsync(CreateDocument("stale") with { Context = "legacyterm" });

        await harness.RebuildAsync(
        [
            CreateDocument("fresh-one") with { Context = "modernterm" },
            CreateDocument("fresh-two") with { Context = "modernterm" },
        ]);

        Assert.Empty((await harness.Service.SearchAsync(new SearchRequest { Text = "legacyterm" })).Hits);
        var response = await harness.Service.SearchAsync(new SearchRequest { Text = "modernterm" });
        Assert.Equal(2, response.TotalCount);
        Assert.Equal(
            new[] { "fresh-one", "fresh-two" },
            response.Hits.Select(hit => hit.Document.Id).Order().ToArray());
    }

    [Fact]
    public async Task Index_IsReopenedFromExplicitCommit()
    {
        var root = SearchHarness.CreateRoot();
        var options = new SearchOptions { IndexRootPath = root };
        await using (var first = new LocalSearchService(options))
        {
            await first.ApplyBatchAsync(
                [SearchIndexMutation.Upsert(CreateDocument("persisted") with { Context = "durableterm" })],
                1);
        }

        await using (var second = new LocalSearchService(options))
        {
            var response = await second.SearchAsync(new SearchRequest { Text = "durableterm" });
            Assert.Equal("persisted", Assert.Single(response.Hits).Document.Id);
        }

        SearchHarness.DeleteRoot(root);
    }

    [Fact]
    public async Task ApplyBatchAsync_CommitsManyDocumentsTogether()
    {
        await using var harness = new SearchHarness();
        await harness.Service.ApplyBatchAsync(
            Enumerable.Range(0, 20)
                .Select(index => SearchIndexMutation.Upsert(CreateDocument($"batch-{index}") with
                {
                    Context = $"batch marker {index}",
                }))
                .ToArray(),
            1);

        var response = await harness.Service.SearchAsync(new SearchRequest
        {
            Kinds = ImmutableHashSet.Create("activity"),
            Limit = 25,
        });

        Assert.Equal(20, response.TotalCount);
        Assert.Equal(20, response.Hits.Length);
    }

    [Fact]
    public async Task Operations_RejectExactTermsBeyondLuceneUtf8LimitBeforeLuceneMutation()
    {
        await using var harness = new SearchHarness();
        var oversizedExactTerm = new string('界', (IndexWriter.MAX_TERM_LENGTH / 3) + 1);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => harness.Service.ApplyBatchAsync(
            [SearchIndexMutation.Upsert(CreateDocument("oversized-field") with
            {
                Application = oversizedExactTerm,
            })],
            1));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => harness.Service.ApplyBatchAsync(
            [SearchIndexMutation.Delete(oversizedExactTerm)],
            1));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => harness.Service.SearchAsync(new SearchRequest
        {
            Kinds = ImmutableHashSet.Create(oversizedExactTerm),
        }));

        Assert.Equal(0, harness.Service.CommittedSourceRevision);
    }

    [Fact]
    public async Task ApplyBatchAsync_RejectsAggregateStructuredTextBeyondConfiguredFieldLimit()
    {
        await using var harness = new SearchHarness(options => options with { MaxTextFieldLength = 10 });
        var document = CreateDocument("aggregate") with
        {
            AttributesRaw = ImmutableDictionary<string, string?>.Empty.Add("alpha", "bravo"),
        };

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => harness.Service.ApplyBatchAsync(
            [SearchIndexMutation.Upsert(document)],
            1));

        Assert.Equal(0, harness.Service.CommittedSourceRevision);
    }

    [Fact]
    public void Constructor_RejectsInvalidOptions()
    {
        Assert.Throws<ArgumentException>(() => new LocalSearchService(new SearchOptions
        {
            IndexRootPath = "relative-path",
        }));

        Assert.Throws<ArgumentOutOfRangeException>(() => new LocalSearchService(new SearchOptions
        {
            IndexRootPath = SearchHarness.CreateRoot(),
            FuzzyMaxEdits = 3,
        }));

        Assert.Throws<ArgumentException>(() => new LocalSearchService(new SearchOptions
        {
            IndexRootPath = SearchHarness.CreateRoot(),
            SynonymSets =
            [
                new SearchSynonymSet { Language = "it", Terms = ["solo"] },
            ],
        }));

        Assert.Throws<ArgumentOutOfRangeException>(() => new LocalSearchService(new SearchOptions
        {
            IndexRootPath = SearchHarness.CreateRoot(),
            MaxTextFieldLength = 4,
            SynonymSets =
            [
                new SearchSynonymSet { Language = "en", Terms = ["desk", "office"] },
            ],
        }));
    }

    [Fact]
    public async Task Operations_RejectInvalidDocumentsAndRequests()
    {
        await using var harness = new SearchHarness();

        await Assert.ThrowsAsync<ArgumentException>(() =>
            harness.UpsertAsync(CreateDocument(" ")));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            harness.Service.SearchAsync(new SearchRequest()));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            harness.Service.SearchAsync(new SearchRequest
            {
                Text = "valid",
                FromInclusive = DateTimeOffset.UtcNow,
                ToExclusive = DateTimeOffset.UtcNow.AddDays(-1),
            }));
    }

    private static SearchDocument CreateDocument(
        string id,
        string kind = "activity",
        DateTimeOffset? timestamp = null)
    {
        return new SearchDocument
        {
            Id = id,
            Kind = kind,
            Timestamp = timestamp ?? new DateTimeOffset(2026, 8, 1, 12, 0, 0, TimeSpan.Zero),
            Language = "en",
        };
    }

    private sealed class SearchHarness : IAsyncDisposable
    {
        private static readonly string TestRoot = Path.Combine(Path.GetTempPath(), "WorkTrail.Search.Tests");

        internal SearchHarness(Func<SearchOptions, SearchOptions>? configure = null)
        {
            Root = CreateRoot();
            var options = new SearchOptions { IndexRootPath = Root };
            Service = new LocalSearchService(configure?.Invoke(options) ?? options);
        }

        internal string Root { get; }

        internal LocalSearchService Service { get; }

        private long SourceRevision { get; set; }

        internal Task UpsertAsync(SearchDocument document) => Service.ApplyBatchAsync(
            [SearchIndexMutation.Upsert(document)],
            ++SourceRevision);

        internal Task DeleteAsync(string id) => Service.ApplyBatchAsync(
            [SearchIndexMutation.Delete(id)],
            ++SourceRevision);

        internal Task RebuildAsync(IEnumerable<SearchDocument> documents) =>
            Service.RebuildAsync(documents, ++SourceRevision);

        internal static string CreateRoot()
        {
            return Path.Combine(TestRoot, Guid.NewGuid().ToString("N"));
        }

        internal static void DeleteRoot(string root)
        {
            var expectedPrefix = Path.GetFullPath(TestRoot).TrimEnd(Path.DirectorySeparatorChar) +
                Path.DirectorySeparatorChar;
            var resolvedRoot = Path.GetFullPath(root);
            if (!resolvedRoot.StartsWith(expectedPrefix, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Refusing to delete a directory outside the test root.");
            }

            if (Directory.Exists(resolvedRoot))
            {
                Directory.Delete(resolvedRoot, recursive: true);
            }
        }

        public async ValueTask DisposeAsync()
        {
            await Service.DisposeAsync();
            DeleteRoot(Root);
        }
    }
}
