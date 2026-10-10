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
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using WorkTrail.Application;
using WorkTrail.Presentation;
using WorkTrail.Search;
using Xunit;

namespace WorkTrail.Presentation.Tests;

public sealed class SearchViewModelTests
{
    [Fact]
    public async Task Preview_UsesBoundedInertIndexTextBeyondTheListSnippet()
    {
        var application = DispatchProxy.Create<IWorkTrailApplication, SearchApplicationProxy>();
        var proxy = (SearchApplicationProxy)(object)application;
        proxy.PreviewOverride = "## Notes\n\n**riunione** [agenda](https://example.test/private) " + string.Concat(Enumerable.Repeat("Testo della riunione da consultare. ", 200));
        var viewModel = CreateViewModel(application);

        await viewModel.SearchAsync("riunione", CultureInfo.GetCultureInfo("it-IT"), CancellationToken.None);
        var result = viewModel.Results[0];

        Assert.InRange(result.PreviewText.Length, 181, 4001);
        Assert.True(result.TextSnippet.Length < result.PreviewText.Length);
        Assert.Contains("riunione", result.PreviewText, StringComparison.Ordinal);
        Assert.DoesNotContain("**", result.PreviewText, StringComparison.Ordinal);
        Assert.DoesNotContain("https://", result.PreviewText, StringComparison.Ordinal);
        Assert.Equal("Project review", result.TitleDisplay);
        Assert.StartsWith("Teams · ", result.SourceDisplay, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Selection_TracksCurrentResultsAndClearsWithoutAnotherQuery()
    {
        var application = DispatchProxy.Create<IWorkTrailApplication, SearchApplicationProxy>();
        var proxy = (SearchApplicationProxy)(object)application;
        var viewModel = CreateViewModel(application);
        await viewModel.SearchAsync("riunione", CultureInfo.InvariantCulture, CancellationToken.None);
        Assert.Equal(viewModel.Results[0], viewModel.SelectedResult);

        var selected = viewModel.Results[1];
        viewModel.SelectResult(selected);
        Assert.Equal(selected, viewModel.SelectedResult);
        Assert.Equal(1, proxy.SearchCalls);

        viewModel.Clear();
        Assert.Null(viewModel.SelectedResult);
        Assert.Empty(viewModel.Results);
        Assert.Throws<ArgumentException>(() => viewModel.SelectResult(selected));
    }

    [Fact]
    public async Task CanceledLateResponse_DoesNotReplaceTheCurrentPreview()
    {
        var application = DispatchProxy.Create<IWorkTrailApplication, SearchApplicationProxy>();
        var proxy = (SearchApplicationProxy)(object)application;
        var viewModel = CreateViewModel(application);
        await viewModel.SearchAsync("riunione", CultureInfo.InvariantCulture, CancellationToken.None);
        viewModel.SelectResult(viewModel.Results[1]);
        var selected = viewModel.SelectedResult;
        using var cancellation = new CancellationTokenSource();
        proxy.CancelOnSearch = cancellation;

        await Assert.ThrowsAsync<OperationCanceledException>(() => viewModel.SearchAsync("another query", CultureInfo.InvariantCulture, cancellation.Token));

        Assert.Same(selected, viewModel.SelectedResult);
        Assert.False(viewModel.IsSearching);
    }

    [Fact]
    public async Task SearchAsync_RequestsAtMostTwentyScreenshotsAndProjectsDateAndUri()
    {
        var application = DispatchProxy.Create<IWorkTrailApplication, SearchApplicationProxy>();
        var proxy = (SearchApplicationProxy)(object)application;
        var viewModel = CreateViewModel(application);

        var result = await viewModel.SearchAsync("riunione", CultureInfo.GetCultureInfo("it-IT"), CancellationToken.None);

        Assert.True(result.Succeeded);
        var request = Assert.IsType<SearchRequest>(proxy.Request);
        Assert.Equal(SearchViewModel.MaximumResults, request.Limit);
        Assert.Equal(0, request.Offset);
        Assert.True(request.IncludeTextContent);
        Assert.Equal(["screenshot"], request.Kinds.ToArray());
        Assert.Equal(3, viewModel.Results.Count);
        var item = viewModel.Results[0];
        Assert.Equal(@"C:\captures\meeting.png", item.ScreenshotPath);
        Assert.StartsWith("file:///C:/captures/meeting.png", item.ScreenshotUri, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("2026", item.CapturedAtDisplay, StringComparison.Ordinal);
        Assert.Equal("Teams · Project review", item.ActiveWindowDisplay);
        Assert.Equal("riunione", item.Query);
        Assert.Contains("riunione", item.TextSnippet, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("#", item.TextSnippet, StringComparison.Ordinal);
        Assert.DoesNotContain("**", item.TextSnippet, StringComparison.Ordinal);
        Assert.Equal("42 localized clicks · CPU 37% · GPU 61%", item.ActivityDisplay);
        Assert.Equal("LOCALIZED MATCH", item.MatchLabel);
        Assert.Equal(4.5f, item.Score);
        Assert.Equal(100, item.MatchPercent);
        Assert.Equal("100%", item.MatchPercentDisplay);
        Assert.Equal("Office laptop", item.InstallationName);
        Assert.Equal("WORKTRAIL-OFFICE", item.InstallationMachineName);
        Assert.Equal("Office laptop · WORKTRAIL-OFFICE", item.InstallationDisplay);
        Assert.Equal("#5B8DEF", item.InstallationColor);
        Assert.Equal("laptop", item.InstallationIcon);
        Assert.Equal("1 localized click · CPU — · GPU —", viewModel.Results[1].ActivityDisplay);
        Assert.Equal(3f, viewModel.Results[1].Score);
        Assert.Equal(67, viewModel.Results[1].MatchPercent);
        Assert.Equal("Localized clicks — · CPU — · GPU —", viewModel.Results[2].ActivityDisplay);
        Assert.Equal(2.25f, viewModel.Results[2].Score);
        Assert.Equal(50, viewModel.Results[2].MatchPercent);
        Assert.Equal(23, viewModel.TotalCount);
    }

    private static SearchViewModel CreateViewModel(IWorkTrailApplication application) =>
        new(application, "LOCALIZED MATCH", FormatClickCount);

    private static string FormatClickCount(long? clickCount, CultureInfo culture) => clickCount switch
    {
        null => "Localized clicks —",
        1 => string.Format(culture, "{0:N0} localized click", clickCount.Value),
        _ => string.Format(culture, "{0:N0} localized clicks", clickCount.Value)
    };

    public class SearchApplicationProxy : DispatchProxy
    {
        public string? PreviewOverride { get; set; }
        public int SearchCalls { get; private set; }
        public CancellationTokenSource? CancelOnSearch { get; set; }

        public SearchRequest? Request { get; private set; }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod?.Name == nameof(IWorkTrailApplication.SearchAsync))
            {
                SearchCalls++;
                CancelOnSearch?.Cancel();
                Request = Assert.IsType<SearchRequest>(args![0]);
                var response = new SearchResponse
                {
                    Hits =
                    [
                        new SearchHit
                        {
                            Document = new SearchDocument
                            {
                                Id = "screenshot:single-click",
                                Kind = "screenshot",
                                Timestamp = new DateTimeOffset(2026, 8, 9, 9, 0, 0, TimeSpan.Zero),
                                Application = "Browser",
                                WindowTitle = "Notes",
                                OcrRawText = "Una riunione con un solo clic",
                                AttributesRaw = ImmutableDictionary<string, string?>.Empty
                                    .Add(SearchAttributeKeys.MouseClicks, "1"),
                                CapturePath = @"C:\captures\single-click.png"
                            },
                            Score = 3f
                        },
                        new SearchHit
                        {
                            Document = new SearchDocument
                            {
                                Id = "screenshot:lower-score",
                                Kind = "screenshot",
                                Timestamp = new DateTimeOffset(2026, 8, 9, 8, 30, 0, TimeSpan.Zero),
                                Application = "Outlook",
                                WindowTitle = "Planning",
                                OcrRawText = "Promemoria per la riunione settimanale",
                                AttributesRaw = ImmutableDictionary<string, string?>.Empty,
                                CapturePath = @"C:\captures\planning.png"
                            },
                            Score = 2.25f
                        },
                        new SearchHit
                        {
                            Document = new SearchDocument
                            {
                                Id = "screenshot:meeting",
                                Kind = "screenshot",
                                Timestamp = new DateTimeOffset(2026, 8, 9, 9, 30, 0, TimeSpan.Zero),
                                Application = "Teams",
                                WindowTitle = "Project review",
                                OcrRawText = PreviewOverride ?? "## Activity\n\nAppunti della **riunione** di progetto",
                                AttributesRaw = ImmutableDictionary<string, string?>.Empty
                                    .Add(SearchAttributeKeys.MouseClicks, "42")
                                    .Add(SearchAttributeKeys.CpuUsagePercent, "37")
                                    .Add(SearchAttributeKeys.GpuUsagePercent, "61")
                                    .Add(SearchAttributeKeys.InstallationId, "11111111111111111111111111111111")
                                    .Add(SearchAttributeKeys.InstallationFriendlyName, "Office laptop")
                                    .Add(SearchAttributeKeys.InstallationMachineName, "WORKTRAIL-OFFICE")
                                    .Add(SearchAttributeKeys.InstallationColor, "#5B8DEF")
                                    .Add(SearchAttributeKeys.InstallationIcon, "laptop"),
                                CapturePath = @"C:\captures\meeting.png"
                            },
                            Score = 4.5f
                        }
                    ],
                    TotalCount = 23,
                    Offset = 0
                };
                return Task.FromResult(OperationResult<SearchResponse>.Success(
                    "search.completed",
                    "SearchCompleted",
                    response));
            }


            throw new NotSupportedException(targetMethod?.Name);
        }
    }
}
