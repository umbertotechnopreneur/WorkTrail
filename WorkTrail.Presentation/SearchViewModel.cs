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
using WorkTrail.Application;
using WorkTrail.Search;

namespace WorkTrail.Presentation;

/// <summary>Represents one screenshot match rendered by the local-search surface.</summary>
public sealed record ScreenshotSearchResult(
    string ScreenshotPath,
    string ScreenshotUri,
    DateTimeOffset CapturedAt,
    string CapturedAtDisplay,
    string Application,
    string WindowTitle,
    string ActiveWindowDisplay,
    string TextSnippet,
    string PreviewText,
    string Query,
    string ActivityDisplay,
    string MatchLabel,
    float Score,
    int MatchPercent,
    string? InstallationName = null,
    string? InstallationMachineName = null,
    string? InstallationColor = null,
    string? InstallationIcon = null)
{
    /// <summary>Gets the captured window title, or its application when no title was recorded.</summary>
    public string TitleDisplay => WindowTitle == "—" ? Application : WindowTitle;

    /// <summary>Gets the source and capture time shown in the list and selected preview.</summary>
    public string SourceDisplay => $"{Application} · {CapturedAtDisplay}";

    /// <summary>Gets the Lucene relevance normalized against the best hit in the current query.</summary>
    public string MatchPercentDisplay => $"{MatchPercent}%";

    /// <summary>Gets the compact installation provenance rendered beside the capture timestamp.</summary>
    public string InstallationDisplay => string.IsNullOrWhiteSpace(InstallationName)
        ? string.Empty
        : string.Equals(InstallationName, InstallationMachineName, StringComparison.Ordinal)
            ? InstallationName
            : $"{InstallationName} · {InstallationMachineName}";
}

/// <summary>Executes bounded screenshot queries through the shared application facade.</summary>
public sealed class SearchViewModel : ViewModelBase
{
    private readonly IWorkTrailApplication _application;
    private readonly string _matchLabel;
    private readonly Func<long?, CultureInfo, string> _formatClickCount;
    private IReadOnlyList<ScreenshotSearchResult> _results = Array.Empty<ScreenshotSearchResult>();
    private bool _isSearching;
    private int _totalCount;
    private ScreenshotSearchResult? _selectedResult;

    /// <summary>Gets the maximum number of screenshot rows rendered by the floating window.</summary>
    public const int MaximumResults = 20;

    /// <summary>Gets the minimum query length used by the live search surface.</summary>
    public const int MinimumQueryLength = 3;

    /// <summary>Creates a local-search presentation model with host-provided localized result formatting.</summary>
    public SearchViewModel(
        IWorkTrailApplication application,
        string matchLabel,
        Func<long?, CultureInfo, string> formatClickCount)
    {
        _application = application ?? throw new ArgumentNullException(nameof(application));
        ArgumentException.ThrowIfNullOrWhiteSpace(matchLabel);
        _matchLabel = matchLabel;
        _formatClickCount = formatClickCount ?? throw new ArgumentNullException(nameof(formatClickCount));
    }

    /// <summary>Gets the current bounded screenshot result list.</summary>
    public IReadOnlyList<ScreenshotSearchResult> Results
    {
        get => _results;
        private set => Set(ref _results, value);
    }

    /// <summary>Gets whether a local query is currently running.</summary>
    public bool IsSearching
    {
        get => _isSearching;
        private set => Set(ref _isSearching, value);
    }

    /// <summary>Gets the total match count before the 20-result window limit.</summary>
    public int TotalCount
    {
        get => _totalCount;
        private set => Set(ref _totalCount, value);
    }

    /// <summary>Gets the current preview selection from the active result set.</summary>
    public ScreenshotSearchResult? SelectedResult
    {
        get => _selectedResult;
        private set => Set(ref _selectedResult, value);
    }

    /// <summary>Selects an active result for preview without loading its screenshot or querying a provider.</summary>
    public void SelectResult(ScreenshotSearchResult? result)
    {
        if (result is not null && !Results.Contains(result))
        {
            throw new ArgumentException("The preview must belong to the current search results.", nameof(result));
        }

        SelectedResult = result;
    }

    /// <summary>Clears the current query results without accessing application state.</summary>
    public void Clear()
    {
        SelectedResult = null;
        Results = Array.Empty<ScreenshotSearchResult>();
        TotalCount = 0;
    }

    /// <summary>Searches retained screenshots and projects the bounded response for WinUI.</summary>
    public async Task<OperationResult<IReadOnlyList<ScreenshotSearchResult>>> SearchAsync(
        string text,
        CultureInfo culture,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(culture);
        if (string.IsNullOrWhiteSpace(text))
        {
            Clear();
            return OperationResult<IReadOnlyList<ScreenshotSearchResult>>.Success(
                "search.query.cleared",
                "SearchQueryCleared",
                Results);
        }

        var query = text.Trim();
        if (query.Length < MinimumQueryLength)
        {
            Clear();
            return OperationResult<IReadOnlyList<ScreenshotSearchResult>>.Success(
                "search.query.too-short",
                "SearchQueryTooShort",
                Results);
        }

        IsSearching = true;
        try
        {
            var response = await _application.SearchAsync(
                new SearchRequest
                {
                    Text = query,
                    Kinds = ImmutableHashSet.Create(StringComparer.Ordinal, "screenshot"),
                    IncludeTextContent = true,
                    Offset = 0,
                    Limit = MaximumResults
                },
                cancellationToken);
            // A superseded query must not replace the current preview even if the facade completes late.
            cancellationToken.ThrowIfCancellationRequested();
            if (!response.Succeeded || response.Value is null)
            {
                Clear();
                return new OperationResult<IReadOnlyList<ScreenshotSearchResult>>(
                    false,
                    response.Code,
                    response.MessageKey,
                    null,
                    response.Issues);
            }

            if (response.Value.Hits.Any(hit => !float.IsFinite(hit.Score) || hit.Score < 0f))
            {
                throw new InvalidDataException("The application returned an invalid Lucene relevance score.");
            }

            var rankedHits = response.Value.Hits
                .OrderByDescending(hit => hit.Score)
                .ToArray();
            var highestScore = rankedHits.Length == 0 ? 0f : rankedHits[0].Score;
            var projected = rankedHits
                .Select(hit => Project(
                    hit,
                    culture,
                    query,
                    CalculateMatchPercent(hit.Score, highestScore),
                    _matchLabel,
                    _formatClickCount))
                .ToArray();
            if (projected.Length > MaximumResults)
            {
                throw new InvalidDataException("The application returned more screenshot search results than requested.");
            }

            Results = projected;
            TotalCount = response.Value.TotalCount;
            SelectedResult = projected.FirstOrDefault();
            return OperationResult<IReadOnlyList<ScreenshotSearchResult>>.Success(
                "search.query.completed",
                "SearchQueryCompleted",
                projected);
        }
        finally
        {
            IsSearching = false;
        }
    }

    private static ScreenshotSearchResult Project(
        SearchHit hit,
        CultureInfo culture,
        string query,
        int matchPercent,
        string matchLabel,
        Func<long?, CultureInfo, string> formatClickCount)
    {
        var document = hit.Document;
        if (!string.Equals(document.Kind, "screenshot", StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(document.CapturePath)
            || !Uri.TryCreate(document.CapturePath, UriKind.Absolute, out var screenshotUri)
            || !screenshotUri.IsFile)
        {
            throw new InvalidDataException("A screenshot search hit must contain an absolute local capture path.");
        }

        var application = string.IsNullOrWhiteSpace(document.Application) ? "WorkTrail" : document.Application;
        var windowTitle = string.IsNullOrWhiteSpace(document.WindowTitle) ? "—" : document.WindowTitle;
        var localTimestamp = document.Timestamp.ToLocalTime();
        var mouseClicks = ReadNonNegativeInt64(document, SearchAttributeKeys.MouseClicks);
        var cpuUsagePercent = ReadPercentage(document, SearchAttributeKeys.CpuUsagePercent);
        var gpuUsagePercent = ReadPercentage(document, SearchAttributeKeys.GpuUsagePercent);
        var installation = ReadInstallation(document);
        var preview = BuildPreview(document, query);
        var snippet = BuildSnippet(preview, query);
        var activeWindowDisplay = windowTitle == "—" || SnippetRepresentsWindowTitle(snippet, windowTitle)
            ? application
            : $"{application} · {windowTitle}";
        return new ScreenshotSearchResult(
            document.CapturePath,
            screenshotUri.AbsoluteUri,
            document.Timestamp,
            $"{localTimestamp.ToString("d MMM yyyy", culture)} · {localTimestamp.ToString("t", culture)}",
            application,
            windowTitle,
            activeWindowDisplay,
            snippet,
            preview,
            query,
            FormatActivity(mouseClicks, cpuUsagePercent, gpuUsagePercent, culture, formatClickCount),
            matchLabel,
            hit.Score,
            matchPercent,
            installation?.FriendlyName,
            installation?.MachineName,
            installation?.Color,
            installation?.Icon);
    }

    private static SearchInstallation? ReadInstallation(SearchDocument document)
    {
        document.AttributesRaw.TryGetValue(SearchAttributeKeys.InstallationId, out var installationId);
        document.AttributesRaw.TryGetValue(SearchAttributeKeys.InstallationFriendlyName, out var friendlyName);
        document.AttributesRaw.TryGetValue(SearchAttributeKeys.InstallationMachineName, out var machineName);
        document.AttributesRaw.TryGetValue(SearchAttributeKeys.InstallationColor, out var color);
        document.AttributesRaw.TryGetValue(SearchAttributeKeys.InstallationIcon, out var icon);
        if (installationId is null && friendlyName is null && machineName is null && color is null && icon is null)
        {
            return null;
        }

        if (!Guid.TryParseExact(installationId, "N", out _)
            || string.IsNullOrWhiteSpace(friendlyName)
            || string.IsNullOrWhiteSpace(machineName)
            || !InstallationProfileCatalog.Colors.Contains(color, StringComparer.Ordinal)
            || !InstallationProfileCatalog.Icons.Contains(icon, StringComparer.Ordinal))
        {
            throw new InvalidDataException($"Search document '{document.Id}' has invalid installation provenance.");
        }

        return new SearchInstallation(friendlyName, machineName, color!, icon!);
    }

    private static int CalculateMatchPercent(float score, float highestScore)
    {
        if (score <= 0f || highestScore <= 0f)
        {
            return 0;
        }

        var relativeScore = score / highestScore * 100d;
        return Math.Clamp((int)Math.Round(relativeScore, MidpointRounding.AwayFromZero), 1, 100);
    }

    private static bool SnippetRepresentsWindowTitle(string snippet, string windowTitle) =>
        string.Equals(snippet.Trim('…'), windowTitle.Trim(), StringComparison.OrdinalIgnoreCase);

    private static long? ReadNonNegativeInt64(SearchDocument document, string key)
    {
        if (!document.AttributesRaw.TryGetValue(key, out var raw) || string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        if (!long.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out var value) || value < 0)
        {
            throw new InvalidDataException($"Search document '{document.Id}' has an invalid '{key}' attribute.");
        }

        return value;
    }

    private static int? ReadPercentage(SearchDocument document, string key)
    {
        var value = ReadNonNegativeInt64(document, key);
        if (value is > 100)
        {
            throw new InvalidDataException($"Search document '{document.Id}' has an invalid '{key}' attribute.");
        }

        return value is null ? null : checked((int)value.Value);
    }

    private static string FormatActivity(
        long? mouseClicks,
        int? cpuUsagePercent,
        int? gpuUsagePercent,
        CultureInfo culture,
        Func<long?, CultureInfo, string> formatClickCount)
    {
        var clicks = formatClickCount(mouseClicks, culture);
        if (string.IsNullOrWhiteSpace(clicks))
        {
            throw new InvalidOperationException("The host click-count formatter returned an empty result.");
        }

        return $"{clicks} · CPU {(cpuUsagePercent is { } cpu ? $"{cpu}%" : "—")} · GPU {(gpuUsagePercent is { } gpu ? $"{gpu}%" : "—")}";
    }

    private static string BuildPreview(SearchDocument document, string query)
    {
        var candidates = new[]
        {
            document.OcrCorrectedText,
            document.OcrRawText,
            document.OcrStructuredSummary,
            document.AiDescription,
            document.Context,
            document.WindowTitle
        };
        var source = candidates.FirstOrDefault(value =>
            !string.IsNullOrWhiteSpace(value)
            && value.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)
            ?? candidates.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
        if (string.IsNullOrWhiteSpace(source))
        {
            return "—";
        }

        var plainText = ScreenshotDetailsProjection.ToPlainTextPreview(source, maximumCharacters: 4_000);
        if (plainText.Length == 0)
        {
            return "—";
        }

        return string.Join(' ', plainText.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }

    private static string BuildSnippet(string compact, string query)
    {
        var matchIndex = compact.IndexOf(query, StringComparison.OrdinalIgnoreCase);
        if (matchIndex < 0 || compact.Length <= 180)
        {
            return compact.Length <= 180 ? compact : $"{compact[..180].TrimEnd()}…";
        }

        const int radius = 78;
        var start = Math.Max(0, matchIndex - radius);
        var length = Math.Min(180, compact.Length - start);
        var snippet = compact.Substring(start, length).Trim();
        return $"{(start > 0 ? "…" : string.Empty)}{snippet}{(start + length < compact.Length ? "…" : string.Empty)}";
    }

    private sealed record SearchInstallation(
        string FriendlyName,
        string MachineName,
        string Color,
        string Icon);
}
