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
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace WorkTrail.Services;

/// <summary>
/// OpenRouter adapter for text + image analysis requests.
/// </summary>
public sealed class OpenRouterDecoder : IAIDecoder
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(2) };

    /// <summary>
    /// Provider name.
    /// </summary>
    public string Provider => "openrouter";

    /// <summary>
    /// Sends one request to OpenRouter endpoint.
    /// </summary>
    /// <param name="prompt">Prompt to send.</param>
    /// <param name="screenshotPaths">Optional screenshot paths.</param>
    /// <param name="settings">Current settings.</param>
    /// <param name="apiKey">Resolved API key.</param>
    /// <param name="correlationId">Business correlation identifier for the snapshot.</param>
    /// <param name="requestOptions">Optional per-request output and reasoning overrides.</param>
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
        _ = correlationId; // Correlation remains local because this endpoint documents no generic client-request header.
        var imageDataUrls = new List<string>();
        foreach (var screenshot in screenshotPaths)
        {
            var image = Convert.ToBase64String(await File.ReadAllBytesAsync(screenshot, cancellationToken));
            imageDataUrls.Add($"data:image/webp;base64,{image}");
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, settings.AiEndpoint);
        // OpenRouter requires standard bearer auth plus optional app metadata.
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        request.Headers.Add("HTTP-Referer", "https://worktrail.local");
        request.Headers.Add("X-Title", "WorkTrail");
        request.Content = new StringContent(SerializePayload(prompt, imageDataUrls, settings, requestOptions), Encoding.UTF8, "application/json");

        var timer = AiProviderTelemetry.StartTimer();
        try
        {
            using var response = await Http.SendAsync(request, cancellationToken);
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
            timer.Stop();
            var providerRequestId = AiProviderTelemetry.Header(response, "x-request-id");
            var providerResponseId = ReadResponseId(responseBody);
            // Keep failures explicit: return provider message first, then fallback generic status.
            if (!response.IsSuccessStatusCode)
            {
                throw new AiProviderRequestException(
                    ReadApiError(responseBody) ?? $"OpenRouter returned {(int)response.StatusCode}.",
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
                ReadFinishReason(root),
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
                "OpenRouter request timed out.",
                new AiProviderFailure("timeout", null, timer.ElapsedMilliseconds),
                exception);
        }
        catch (HttpRequestException exception)
        {
            timer.Stop();
            throw new AiProviderRequestException(
                "OpenRouter request could not reach the provider.",
                new AiProviderFailure("network", null, timer.ElapsedMilliseconds),
                exception);
        }
        catch (JsonException exception)
        {
            timer.Stop();
            throw new AiProviderRequestException(
                "OpenRouter returned an invalid response.",
                new AiProviderFailure("invalid_response", null, timer.ElapsedMilliseconds),
                exception);
        }
    }

    internal static string SerializePayload(
        string prompt,
        IReadOnlyList<string> imageDataUrls,
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

        foreach (var imageDataUrl in imageDataUrls)
        {
            content.Add(new Dictionary<string, object?>
            {
                ["type"] = "image_url",
                ["image_url"] = new Dictionary<string, object?>
                {
                    ["url"] = imageDataUrl,
                    ["detail"] = profile.ImageDetail
                }
            });
        }

        var payload = new Dictionary<string, object?>
        {
            ["model"] = settings.Model,
            ["messages"] = new[]
            {
                new Dictionary<string, object?>
                {
                    ["role"] = "user",
                    ["content"] = content
                }
            }
        };

        if (requestOptions?.OmitOutputTokenLimitWhenSupported != true)
        {
            payload["max_tokens"] = requestOptions?.MaxOutputTokens ?? profile.MaxOutputTokens;
        }

        var reasoningEffort = AiAnalysisProfileCatalog.ResolveReasoningEffort(requestOptions?.ReasoningEffort);
        if (reasoningEffort is not null)
        {
            payload["reasoning"] = new Dictionary<string, object?>
            {
                ["effort"] = reasoningEffort
            };
        }

        return JsonSerializer.Serialize(payload);
    }

    /// <summary>
    /// Parses text from OpenRouter-style responses.
    /// </summary>
    internal static AiUsageMetrics ReadUsage(JsonElement root)
    {
        if (!root.TryGetProperty("usage", out var usage) || usage.ValueKind != JsonValueKind.Object)
        {
            return new AiUsageMetrics();
        }

        var promptDetails = usage.TryGetProperty("prompt_tokens_details", out var inputDetails) && inputDetails.ValueKind == JsonValueKind.Object
            ? inputDetails
            : default;
        var completionDetails = usage.TryGetProperty("completion_tokens_details", out var outputDetails) && outputDetails.ValueKind == JsonValueKind.Object
            ? outputDetails
            : default;
        var costDetails = usage.TryGetProperty("cost_details", out var details) && details.ValueKind == JsonValueKind.Object
            ? details
            : default;
        return new AiUsageMetrics(
            AiProviderTelemetry.ReadLong(usage, "prompt_tokens"),
            AiProviderTelemetry.ReadLong(usage, "completion_tokens"),
            AiProviderTelemetry.ReadLong(usage, "total_tokens"),
            promptDetails.ValueKind == JsonValueKind.Object ? AiProviderTelemetry.ReadLong(promptDetails, "cached_tokens") : null,
            promptDetails.ValueKind == JsonValueKind.Object ? AiProviderTelemetry.ReadLong(promptDetails, "cache_write_tokens") : null,
            ReasoningTokens: completionDetails.ValueKind == JsonValueKind.Object ? AiProviderTelemetry.ReadLong(completionDetails, "reasoning_tokens") : null,
            ReportedCostUsd: AiProviderTelemetry.ReadDecimal(usage, "cost"),
            ReportedUpstreamCostUsd: costDetails.ValueKind == JsonValueKind.Object ? AiProviderTelemetry.ReadDecimal(costDetails, "upstream_inference_cost") : null);
    }

    private static string? ReadOutputText(JsonElement root)
    {
        if (root.TryGetProperty("choices", out var choices))
        {
            foreach (var item in choices.EnumerateArray())
            {
                if (!item.TryGetProperty("message", out var message)) continue;
                if (message.TryGetProperty("content", out var content))
                {
                    return content.ValueKind == JsonValueKind.Array ? ReadFirstTextFromArray(content) : content.GetString();
                }
            }
        }

        if (root.TryGetProperty("output", out var output))
        {
            foreach (var item in output.EnumerateArray())
            {
                if (!item.TryGetProperty("content", out var content)) continue;
                foreach (var part in content.EnumerateArray())
                {
                    if (part.TryGetProperty("type", out var type) && type.GetString() == "output_text" && part.TryGetProperty("text", out var text))
                    {
                        return text.GetString();
                    }
                }
            }
        }

        return null;
    }

    private static string? ReadFinishReason(JsonElement root)
    {
        if (!root.TryGetProperty("choices", out var choices))
        {
            return null;
        }

        foreach (var choice in choices.EnumerateArray())
        {
            var reason = AiProviderTelemetry.ReadString(choice, "finish_reason");
            if (!string.IsNullOrWhiteSpace(reason))
            {
                return reason;
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

    private static string? ReadFirstTextFromArray(JsonElement content)
    {
        foreach (var part in content.EnumerateArray())
        {
            if (part.TryGetProperty("type", out var type) && type.GetString() == "text" && part.TryGetProperty("text", out var text))
            {
                return text.GetString();
            }
        }

        return null;
    }

    /// <summary>
    /// Reads error payload for actionable details.
    /// </summary>
    private static string? ReadApiError(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.TryGetProperty("error", out var error)
                ? error.TryGetProperty("message", out var message) ? message.GetString() : error.GetString()
                : null;
        }
        catch
        {
            return null;
        }
    }
}
