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


using System.Globalization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace WorkTrail.Services;

internal static class AiPricingProviders
{
    internal const string OpenAi = "openai";
}

internal static class AiPricingServiceTiers
{
    internal const string Standard = "standard";
}

internal static class AiPricingContextWindows
{
    internal const string Short = "short";
    internal const string Long = "long";
}

internal sealed record AiModelPricing(
    string Provider,
    string Model,
    string ServiceTier,
    string ContextWindow,
    string Currency,
    decimal InputUsdPerMillionTokens,
    decimal? CachedInputUsdPerMillionTokens,
    decimal? CacheWriteUsdPerMillionTokens,
    decimal OutputUsdPerMillionTokens,
    string SourceUrl,
    DateTimeOffset SourceRetrievedAt);

internal static class OpenAiPricingMarkdownParser
{
    internal const string PricingMarkdownUrl = "https://developers.openai.com/api/docs/pricing.md";

    internal static IReadOnlyList<AiModelPricing> ParseStandardPricingData(
        string markdown,
        DateTimeOffset retrievedAt,
        string sourceUrl = PricingMarkdownUrl)
    {
        if (string.IsNullOrWhiteSpace(markdown))
        {
            throw new InvalidDataException("OpenAI pricing markdown is empty.");
        }

        var lines = markdown.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var headingIndex = Array.FindIndex(
            lines,
            line => string.Equals(line.Trim(), "### Standard pricing data", StringComparison.Ordinal));
        if (headingIndex < 0)
        {
            throw new InvalidDataException("OpenAI pricing markdown does not contain Standard pricing data.");
        }

        var tableIndex = Array.FindIndex(
            lines,
            headingIndex + 1,
            line => line.TrimStart().StartsWith("| Model |", StringComparison.Ordinal));
        if (tableIndex < 0)
        {
            throw new InvalidDataException("OpenAI Standard pricing table was not found.");
        }

        var prices = new List<AiModelPricing>();
        for (var index = tableIndex + 1; index < lines.Length; index++)
        {
            var line = lines[index].Trim();
            if (!line.StartsWith('|'))
            {
                break;
            }

            var cells = ParseTableCells(line);
            if (cells.Count != 9)
            {
                break;
            }

            if (cells.All(cell => cell.Length == 0 || cell.All(character => character == '-')))
            {
                continue;
            }

            var model = NormalizeModelName(cells[0]);
            if (string.IsNullOrWhiteSpace(model))
            {
                continue;
            }

            AddPrice(
                prices,
                model,
                AiPricingContextWindows.Short,
                ParsePrice(cells[1], required: true)!.Value,
                ParsePrice(cells[2], required: false),
                ParsePrice(cells[3], required: false),
                ParsePrice(cells[4], required: true)!.Value,
                retrievedAt,
                sourceUrl);

            var longInput = ParsePrice(cells[5], required: false);
            var longOutput = ParsePrice(cells[8], required: false);
            if (longInput.HasValue && longOutput.HasValue)
            {
                AddPrice(
                    prices,
                    model,
                    AiPricingContextWindows.Long,
                    longInput.Value,
                    ParsePrice(cells[6], required: false),
                    ParsePrice(cells[7], required: false),
                    longOutput.Value,
                    retrievedAt,
                    sourceUrl);
            }
        }

        if (prices.Count == 0)
        {
            throw new InvalidDataException("OpenAI Standard pricing table did not contain model prices.");
        }

        return prices;
    }

    private static void AddPrice(
        ICollection<AiModelPricing> prices,
        string model,
        string contextWindow,
        decimal input,
        decimal? cachedInput,
        decimal? cacheWrite,
        decimal output,
        DateTimeOffset retrievedAt,
        string sourceUrl) =>
        prices.Add(new AiModelPricing(
            AiPricingProviders.OpenAi,
            model,
            AiPricingServiceTiers.Standard,
            contextWindow,
            "usd",
            input,
            cachedInput,
            cacheWrite,
            output,
            sourceUrl,
            retrievedAt));

    private static IReadOnlyList<string> ParseTableCells(string line)
    {
        var trimmed = line.Trim();
        if (trimmed.StartsWith('|'))
        {
            trimmed = trimmed[1..];
        }

        if (trimmed.EndsWith('|'))
        {
            trimmed = trimmed[..^1];
        }

        return trimmed.Split('|').Select(cell => cell.Trim()).ToArray();
    }

    private static string NormalizeModelName(string value)
    {
        var normalized = value.Replace("`", string.Empty, StringComparison.Ordinal).Trim();
        var parenthetical = normalized.IndexOf(" (", StringComparison.Ordinal);
        return parenthetical > 0 ? normalized[..parenthetical].Trim() : normalized;
    }

    private static decimal? ParsePrice(string value, bool required)
    {
        var normalized = value.Trim();
        if (normalized == "-")
        {
            if (required)
            {
                throw new InvalidDataException("OpenAI Standard pricing table contains a required missing price.");
            }

            return null;
        }

        if (!normalized.StartsWith('$')
            || !decimal.TryParse(normalized[1..], NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var parsed)
            || parsed < 0m)
        {
            throw new InvalidDataException("OpenAI Standard pricing table contains an invalid price.");
        }

        return parsed;
    }
}

/// <summary>Refreshes the cached OpenAI pricing table on a background daily cadence.</summary>
public sealed class OpenAiPricingRefreshService : IAsyncDisposable
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromDays(1);
    private readonly LocalStore _store;
    private readonly HttpClient _http;
    private readonly ILogger<OpenAiPricingRefreshService> _logger;
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private readonly CancellationTokenSource _lifetime = new();
    private readonly object _scheduledRefreshLock = new();
    private Timer? _timer;
    private Task _scheduledRefreshTask = Task.CompletedTask;
    private int _disposed;

    internal OpenAiPricingRefreshService(
        LocalStore store,
        ILogger<OpenAiPricingRefreshService>? logger = null,
        HttpClient? httpClient = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _http = httpClient ?? Http;
        _logger = logger ?? NullLogger<OpenAiPricingRefreshService>.Instance;
    }

    internal void Start()
    {
        lock (_scheduledRefreshLock)
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
            _timer ??= new Timer(HandleTimerTick, null, TimeSpan.FromSeconds(15), RefreshInterval);
        }
    }

    internal async Task<int> RefreshIfStaleAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var latest = _store.GetLatestAiModelPricingRetrievedAt(AiPricingProviders.OpenAi);
        if (latest is not null && DateTimeOffset.UtcNow - latest.Value < RefreshInterval)
        {
            return 0;
        }

        return await RefreshAsync(cancellationToken).ConfigureAwait(false);
    }

    internal async Task<int> RefreshAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            _lifetime.Token);
        await _refreshGate.WaitAsync(linkedCancellation.Token).ConfigureAwait(false);
        try
        {
            var settings = _store.LoadSettings();
            if (!settings.OpenAiEnabled || !string.Equals(settings.AiProvider, AiPricingProviders.OpenAi, StringComparison.OrdinalIgnoreCase))
            {
                // Pricing is an optional provider-specific network flow; local-only/other-provider mode never starts it.
                return 0;
            }

            var markdown = await _http.GetStringAsync(
                OpenAiPricingMarkdownParser.PricingMarkdownUrl,
                linkedCancellation.Token).ConfigureAwait(false);
            var retrievedAt = DateTimeOffset.UtcNow;
            var prices = OpenAiPricingMarkdownParser.ParseStandardPricingData(markdown, retrievedAt);
            _store.ReplaceAiModelPricing(AiPricingProviders.OpenAi, prices);
            _logger.LogInformation(
                "OpenAI pricing table refreshed. ModelPriceRows={ModelPriceRows}, RetrievedAtUtc={RetrievedAtUtc:o}",
                prices.Count,
                retrievedAt);
            return prices.Count;
        }
        finally
        {
            _refreshGate.Release();
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _lifetime.Cancel();
        var timer = Interlocked.Exchange(ref _timer, null);
        if (timer is not null)
        {
            await timer.DisposeAsync().ConfigureAwait(false);
        }

        Task scheduledRefresh;
        lock (_scheduledRefreshLock)
        {
            scheduledRefresh = _scheduledRefreshTask;
        }

        try
        {
            await scheduledRefresh.ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
            // Expected when shutdown interrupts the scheduled HTTP refresh.
        }

        // Drain any explicit refresh that raced with disposal before releasing the shared gate.
        await _refreshGate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        _refreshGate.Release();
        _refreshGate.Dispose();
        _lifetime.Dispose();
    }

    private void HandleTimerTick(object? state)
    {
        lock (_scheduledRefreshLock)
        {
            if (Volatile.Read(ref _disposed) != 0 || !_scheduledRefreshTask.IsCompleted)
            {
                return;
            }

            _scheduledRefreshTask = RunScheduledRefreshAsync(_lifetime.Token);
        }
    }

    private async Task RunScheduledRefreshAsync(CancellationToken cancellationToken)
    {
        try
        {
            await RefreshIfStaleAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Expected when the runtime is shutting down.
        }
        catch (Exception exception)
        {
            // Pricing refresh is best-effort: existing prices remain usable and snapshots disclose their last update time.
            _logger.LogWarning(
                exception,
                "OpenAI pricing refresh failed. ExceptionType={ExceptionType}",
                exception.GetType().Name);
        }
    }
}
