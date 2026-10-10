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
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace WorkTrail.Services;

/// <summary>
/// Anthropic adapter for text + image analysis requests.
/// </summary>
public sealed class AnthropicDecoder : IAIDecoder
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(2) };

    /// <summary>
    /// Provider name.
    /// </summary>
    public string Provider => "anthropic";

    /// <summary>
    /// Sends one request to Anthropic endpoint.
    /// </summary>
    /// <param name="prompt">Prompt to send.</param>
    /// <param name="screenshotPaths">Optional screenshot paths.</param>
    /// <param name="settings">Current settings.</param>
    /// <param name="apiKey">Resolved API key.</param>
    /// <param name="correlationId">Business correlation identifier for the snapshot.</param>
    /// <param name="requestOptions">Optional per-request overrides; Anthropic still requires an explicit output maximum.</param>
    /// <param name="cancellationToken">Cancels local file reads and the provider request.</param>
    /// <returns>Model output plus nullable provider telemetry.</returns>
    public async Task<AiProviderResult> DecodeAsync(
        string prompt,
        IReadOnlyList<string> screenshotPaths,
        AppSettings settings,
        string apiKey,
        string correlationId,
        AiProviderRequestOptions? requestOptions = null,
        CancellationToken cancellationToken = default)
    {
        _ = correlationId; // Anthropic has no documented generic client-correlation header for this endpoint.
        var base64Images = new List<string>();
        foreach (var screenshot in screenshotPaths)
        {
            var image = Convert.ToBase64String(await File.ReadAllBytesAsync(screenshot, cancellationToken));
            base64Images.Add(image);
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, settings.AiEndpoint);
        // Keep auth + protocol version explicit for stable response behavior.
        ApplyRequiredHeaders(request, apiKey);
        request.Content = new StringContent(SerializePayload(prompt, base64Images, settings, requestOptions), Encoding.UTF8, "application/json");

        var timer = AiProviderTelemetry.StartTimer();
        try
        {
            using var response = await Http.SendAsync(request, cancellationToken);
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
            timer.Stop();
            var providerRequestId = AiProviderTelemetry.Header(response, "request-id");
            var providerResponseId = ReadResponseId(responseBody);
            if (!response.IsSuccessStatusCode)
            {
                throw new AiProviderRequestException(
                    ReadApiError(responseBody) ?? $"Anthropic returned {(int)response.StatusCode}.",
                    new AiProviderFailure(
                        AiProviderTelemetry.FailureCode(response.StatusCode),
                        (int)response.StatusCode,
                        timer.ElapsedMilliseconds,
                        providerResponseId,
                        providerRequestId));
            }

            using var document = JsonDocument.Parse(responseBody);
            var root = document.RootElement;
            return new AiProviderResult(
                ReadOutputText(root) ?? "The model did not return text.",
                ReadUsage(root),
                AiProviderTelemetry.ReadString(root, "id"),
                providerRequestId,
                AiProviderTelemetry.ReadString(root, "model"),
                AiProviderTelemetry.ReadString(root, "stop_reason"),
                (int)response.StatusCode,
                timer.ElapsedMilliseconds,
                null);
        }
        catch (AiProviderRequestException)
        {
            throw;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (TaskCanceledException exception)
        {
            timer.Stop();
            throw new AiProviderRequestException(
                "Anthropic request timed out.",
                new AiProviderFailure("timeout", null, timer.ElapsedMilliseconds),
                exception);
        }
        catch (HttpRequestException exception)
        {
            timer.Stop();
            throw new AiProviderRequestException(
                "Anthropic request could not reach the provider.",
                new AiProviderFailure("network", null, timer.ElapsedMilliseconds),
                exception);
        }
        catch (JsonException exception)
        {
            timer.Stop();
            throw new AiProviderRequestException(
                "Anthropic returned an invalid response.",
                new AiProviderFailure("invalid_response", null, timer.ElapsedMilliseconds),
                exception);
        }
    }

    internal static void ApplyRequiredHeaders(HttpRequestMessage request, string apiKey)
    {
        request.Headers.Add("x-api-key", apiKey);
        request.Headers.Add("anthropic-version", "2023-06-01");
    }

    internal static string SerializePayload(
        string prompt,
        IReadOnlyList<string> base64Images,
        AppSettings settings,
        AiProviderRequestOptions? requestOptions = null)
    {
        var profile = AiAnalysisProfileCatalog.Resolve(settings.AiOutputDetail);
        if (requestOptions?.MaxOutputTokens is <= 0)
            throw new ArgumentOutOfRangeException(nameof(requestOptions), "The output token budget must be positive.");
        var content = new List<object>
        {
            new Dictionary<string, object?>
            {
                ["type"] = "text",
                ["text"] = prompt
            }
        };

        foreach (var base64Image in base64Images)
        {
            content.Add(new Dictionary<string, object?>
            {
                ["type"] = "image",
                ["source"] = new Dictionary<string, object?>
                {
                    ["type"] = "base64",
                    ["media_type"] = "image/webp",
                    ["data"] = base64Image
                }
            });
        }

        var payload = new Dictionary<string, object?>
        {
            ["model"] = settings.Model,
            ["max_tokens"] = requestOptions?.MaxOutputTokens ?? profile.MaxOutputTokens,
            ["messages"] = new[]
            {
                new Dictionary<string, object?>
                {
                    ["role"] = "user",
                    ["content"] = content
                }
            }
        };

        return JsonSerializer.Serialize(payload);
    }

    internal static AiUsageMetrics ReadUsage(JsonElement root)
    {
        if (!root.TryGetProperty("usage", out var usage) || usage.ValueKind != JsonValueKind.Object)
        {
            return new AiUsageMetrics();
        }

        var baseInput = AiProviderTelemetry.ReadLong(usage, "input_tokens");
        var cacheRead = AiProviderTelemetry.ReadLong(usage, "cache_read_input_tokens");
        var cacheCreation = AiProviderTelemetry.ReadLong(usage, "cache_creation_input_tokens");
        if (!cacheCreation.HasValue
            && usage.TryGetProperty("cache_creation", out var cacheCreationDetails)
            && cacheCreationDetails.ValueKind == JsonValueKind.Object)
        {
            cacheCreation = SumTokens(
                cacheCreation,
                AiProviderTelemetry.ReadLong(cacheCreationDetails, "ephemeral_5m_input_tokens"),
                AiProviderTelemetry.ReadLong(cacheCreationDetails, "ephemeral_1h_input_tokens"));
        }

        var output = AiProviderTelemetry.ReadLong(usage, "output_tokens");
        var effectiveInput = SumTokens(baseInput, cacheRead, cacheCreation);
        var outputDetails = usage.TryGetProperty("output_tokens_details", out var details) && details.ValueKind == JsonValueKind.Object
            ? details
            : default;
        return new AiUsageMetrics(
            effectiveInput,
            output,
            SumTokens(effectiveInput, output),
            CacheCreationInputTokens: cacheCreation,
            CacheReadInputTokens: cacheRead,
            ThinkingTokens: outputDetails.ValueKind == JsonValueKind.Object ? AiProviderTelemetry.ReadLong(outputDetails, "thinking_tokens") : null);
    }

    private static string? ReadOutputText(JsonElement root)
    {
        if (!root.TryGetProperty("content", out var content))
        {
            return null;
        }

        foreach (var item in content.EnumerateArray())
        {
            if (item.TryGetProperty("type", out var type) && type.GetString() == "text" && item.TryGetProperty("text", out var text))
            {
                return text.GetString();
            }
        }
        return null;
    }

    private static string? ReadResponseId(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            return AiProviderTelemetry.ReadString(document.RootElement, "id");
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static long? SumTokens(params long?[] values)
    {
        var known = values.Where(value => value.HasValue).Select(value => value!.Value).ToArray();
        return known.Length == 0 ? null : known.Aggregate(0L, (current, value) => checked(current + value));
    }

    /// <summary>
    /// Reads error payload from provider response.
    /// </summary>
    private static string? ReadApiError(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.TryGetProperty("error", out var error))
            {
                if (error.TryGetProperty("message", out var message))
                {
                    return message.GetString();
                }

                if (error.TryGetProperty("error", out var nested) && nested.TryGetProperty("message", out var nestedMessage))
                {
                    return nestedMessage.GetString();
                }
            }
        }
        catch
        {
        }

        return null;
    }
}
