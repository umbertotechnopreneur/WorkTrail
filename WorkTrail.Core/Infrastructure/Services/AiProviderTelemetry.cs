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


using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Text.Json;

namespace WorkTrail.Services;

/// <summary>Normalizes provider-supplied usage without treating absent fields as zero.</summary>
public sealed record AiUsageMetrics(
    long? InputTokens = null,
    long? OutputTokens = null,
    long? TotalTokens = null,
    long? CachedInputTokens = null,
    long? CacheWriteTokens = null,
    long? CacheCreationInputTokens = null,
    long? CacheReadInputTokens = null,
    long? ReasoningTokens = null,
    long? ThinkingTokens = null,
    decimal? ReportedCostUsd = null,
    decimal? ReportedUpstreamCostUsd = null);

/// <summary>Contains a successful provider response and its allowlisted transport metadata.</summary>
public sealed record AiProviderResult(
    string Text,
    AiUsageMetrics Usage,
    string? ProviderResponseId,
    string? ProviderRequestId,
    string? ReturnedModel,
    string? FinishReason,
    int HttpStatusCode,
    long ElapsedMilliseconds,
    long? ProviderProcessingMilliseconds);

/// <summary>Contains safe metadata that survives a failed provider attempt.</summary>
internal sealed record AiProviderFailure(
    string FailureCode,
    int? HttpStatusCode,
    long ElapsedMilliseconds,
    string? ProviderResponseId = null,
    string? ProviderRequestId = null,
    long? ProviderProcessingMilliseconds = null,
    AiUsageMetrics? Usage = null,
    string? FinishReason = null);

/// <summary>Represents a provider failure while retaining only persistence-safe telemetry.</summary>
internal sealed class AiProviderRequestException : InvalidOperationException
{
    internal AiProviderRequestException(string message, AiProviderFailure failure, Exception? innerException = null)
        : base(message, innerException) => Failure = failure;

    internal AiProviderFailure Failure { get; }
}

/// <summary>Describes one sanitized AI HTTP attempt stored in the shared SQLite database.</summary>
internal sealed record AiRequestUsageRecord(
    string AttemptId,
    string CorrelationId,
    DateTimeOffset OccurredAt,
    DateTimeOffset? CompletedAt,
    string Origin,
    string RequestKind,
    string Provider,
    string EndpointHost,
    string RequestedModel,
    string? ReturnedModel,
    string? ProviderResponseId,
    string? ProviderRequestId,
    int? HttpStatusCode,
    long? ElapsedMilliseconds,
    long? ProviderProcessingMilliseconds,
    int ImageCount,
    int PromptCharacters,
    int MaxOutputTokens,
    AiUsageMetrics Usage,
    string? FinishReason,
    bool Success,
    string? FailureCode);

/// <summary>Contains shared parsing and sanitization for provider response metadata.</summary>
internal static class AiProviderTelemetry
{
    internal static Stopwatch StartTimer() => Stopwatch.StartNew();

    internal static string? Header(HttpResponseMessage response, string name) =>
        response.Headers.TryGetValues(name, out var values) ? values.FirstOrDefault() : null;

    internal static long? HeaderMilliseconds(HttpResponseMessage response, string name) =>
        long.TryParse(Header(response, name), out var value) && value >= 0 ? value : null;

    internal static long? ReadLong(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.Number when value.TryGetInt64(out var number) && number >= 0 => number,
            JsonValueKind.String when long.TryParse(value.GetString(), out var number) && number >= 0 => number,
            _ => null
        };
    }

    internal static decimal? ReadDecimal(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.Number when value.TryGetDecimal(out var number) && number >= 0m => number,
            JsonValueKind.String when decimal.TryParse(value.GetString(), out var number) && number >= 0m => number,
            _ => null
        };
    }

    internal static string? ReadString(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    internal static string EndpointHost(string endpoint) =>
        Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) ? uri.Host : "invalid-endpoint";

    internal static string FailureCode(HttpStatusCode statusCode, string? providerCode = null)
    {
        var transportCode = $"http_{(int)statusCode}";
        var safeProviderCode = SafeToken(providerCode, 64);
        return safeProviderCode is null ? transportCode : $"{transportCode}.{safeProviderCode}";
    }

    internal static string? SafeToken(string? value, int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value) || maximumLength <= 0 || value.Length > maximumLength)
        {
            return null;
        }

        return value.All(character => char.IsAsciiLetterOrDigit(character) || character is '.' or '_' or '-' or ':')
            ? value
            : null;
    }
}
