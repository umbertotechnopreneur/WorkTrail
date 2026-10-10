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
/// OpenAI-compatible decoder for text + image payloads.
/// </summary>
public sealed class OpenAiDecoder : IAIDecoder
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(2) };

    /// <summary>
    /// Provider name.
    /// </summary>
    public string Provider => "openai";

    /// <summary>
    /// Sends one analysis request to the configured OpenAI endpoint.
    /// </summary>
    /// <param name="prompt">Prompt to send.</param>
    /// <param name="screenshotPaths">Optional screenshot paths.</param>
    /// <param name="settings">Current settings.</param>
    /// <param name="apiKey">Resolved API key.</param>
    /// <param name="correlationId">Business identifier echoed to OpenAI through the documented client-request header.</param>
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
        var imageDataUrls = new List<string>();
        foreach (var screenshot in screenshotPaths)
        {
            var image = Convert.ToBase64String(await File.ReadAllBytesAsync(screenshot, cancellationToken));
            imageDataUrls.Add($"data:image/webp;base64,{image}");
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, settings.AiEndpoint);

        // Keep auth/configuration in request headers so body remains deterministic for snapshots.
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        request.Headers.TryAddWithoutValidation("X-Client-Request-Id", correlationId);
        request.Content = new StringContent(SerializePayload(prompt, imageDataUrls, settings, requestOptions), Encoding.UTF8, "application/json");

        var timer = AiProviderTelemetry.StartTimer();
        try
        {
            using var response = await Http.SendAsync(request, cancellationToken);
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
            timer.Stop();
            var providerRequestId = AiProviderTelemetry.Header(response, "x-request-id");
            var providerProcessingMilliseconds = AiProviderTelemetry.HeaderMilliseconds(response, "openai-processing-ms");
            var providerResponseId = ReadResponseId(responseBody);
            if (!response.IsSuccessStatusCode)
            {
                throw new AiProviderRequestException(
                    ReadApiError(responseBody) ?? $"OpenAI returned {(int)response.StatusCode}.",
                    new AiProviderFailure(
                        AiProviderTelemetry.FailureCode(response.StatusCode, ReadApiErrorCode(responseBody)),
                        (int)response.StatusCode,
                        timer.ElapsedMilliseconds,
                        providerResponseId,
                        providerRequestId,
                        providerProcessingMilliseconds));
            }

            return ParseSuccessfulResponse(
                responseBody,
                (int)response.StatusCode,
                timer.ElapsedMilliseconds,
                providerRequestId,
                providerProcessingMilliseconds);
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
                "OpenAI request timed out.",
                new AiProviderFailure("timeout", null, timer.ElapsedMilliseconds),
                exception);
        }
        catch (HttpRequestException exception)
        {
            timer.Stop();
            throw new AiProviderRequestException(
                "OpenAI request could not reach the provider.",
                new AiProviderFailure("network", null, timer.ElapsedMilliseconds),
                exception);
        }
        catch (JsonException exception)
        {
            timer.Stop();
            throw new AiProviderRequestException(
                "OpenAI returned an invalid response.",
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
                ["type"] = "input_text",
                ["text"] = prompt
            }
        };

        foreach (var imageDataUrl in imageDataUrls)
        {
            content.Add(new Dictionary<string, object?>
            {
                ["type"] = "input_image",
                ["image_url"] = imageDataUrl,
                ["detail"] = profile.ImageDetail
            });
        }

        var payload = new Dictionary<string, object?>
        {
            ["model"] = settings.Model,
            ["input"] = new[]
            {
                new Dictionary<string, object?>
                {
                    ["role"] = "user",
                    ["content"] = content
                }
            },
            ["text"] = new Dictionary<string, object?>
            {
                ["verbosity"] = profile.TextVerbosity
            }
        };

        if (requestOptions?.OmitOutputTokenLimitWhenSupported != true)
        {
            payload["max_output_tokens"] = requestOptions?.MaxOutputTokens ?? profile.MaxOutputTokens;
        }

        var reasoningEffort = AiAnalysisProfileCatalog.ResolveReasoningEffort(
            requestOptions?.ReasoningEffort ?? settings.AiReasoningEffort);
        if (reasoningEffort is not null)
        {
            payload["reasoning"] = new Dictionary<string, object?>
            {
                ["effort"] = reasoningEffort
            };
        }

        return JsonSerializer.Serialize(payload);
    }

    internal static AiProviderResult ParseSuccessfulResponse(
        string responseBody,
        int httpStatusCode,
        long elapsedMilliseconds,
        string? providerRequestId = null,
        long? providerProcessingMilliseconds = null)
    {
        using var document = JsonDocument.Parse(responseBody);
        var root = document.RootElement;
        var usage = ReadUsage(root);
        var status = AiProviderTelemetry.ReadString(root, "status");
        var providerResponseId = AiProviderTelemetry.ReadString(root, "id");
        if (string.Equals(status, "incomplete", StringComparison.OrdinalIgnoreCase))
        {
            var incompleteReason = ReadIncompleteReason(root);
            var failureCode = incompleteReason is null ? "incomplete" : $"incomplete.{incompleteReason}";
            throw new AiProviderRequestException(
                "OpenAI returned an incomplete response.",
                new AiProviderFailure(
                    failureCode,
                    httpStatusCode,
                    elapsedMilliseconds,
                    providerResponseId,
                    providerRequestId,
                    providerProcessingMilliseconds,
                    usage,
                    status));
        }

        return new AiProviderResult(
            ReadOutputText(root) ?? "The model did not return text.",
            usage,
            providerResponseId,
            providerRequestId,
            AiProviderTelemetry.ReadString(root, "model"),
            status,
            httpStatusCode,
            elapsedMilliseconds,
            providerProcessingMilliseconds);
    }

    private static string? ReadIncompleteReason(JsonElement root)
    {
        if (!root.TryGetProperty("incomplete_details", out var details) || details.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        return AiProviderTelemetry.SafeToken(AiProviderTelemetry.ReadString(details, "reason"), 64);
    }

    /// <summary>
    /// Parses the textual output from OpenAI structured responses.
    /// </summary>
    internal static AiUsageMetrics ReadUsage(JsonElement root)
    {
        if (!root.TryGetProperty("usage", out var usage) || usage.ValueKind != JsonValueKind.Object)
        {
            return new AiUsageMetrics();
        }

        var inputDetails = usage.TryGetProperty("input_tokens_details", out var input) && input.ValueKind == JsonValueKind.Object
            ? input
            : default;
        var outputDetails = usage.TryGetProperty("output_tokens_details", out var output) && output.ValueKind == JsonValueKind.Object
            ? output
            : default;
        return new AiUsageMetrics(
            AiProviderTelemetry.ReadLong(usage, "input_tokens"),
            AiProviderTelemetry.ReadLong(usage, "output_tokens"),
            AiProviderTelemetry.ReadLong(usage, "total_tokens"),
            inputDetails.ValueKind == JsonValueKind.Object ? AiProviderTelemetry.ReadLong(inputDetails, "cached_tokens") : null,
            inputDetails.ValueKind == JsonValueKind.Object ? AiProviderTelemetry.ReadLong(inputDetails, "cache_write_tokens") : null,
            ReasoningTokens: outputDetails.ValueKind == JsonValueKind.Object ? AiProviderTelemetry.ReadLong(outputDetails, "reasoning_tokens") : null);
    }

    private static string? ReadOutputText(JsonElement root)
    {
        if (!root.TryGetProperty("output", out var output))
        {
            return null;
        }

        foreach (var item in output.EnumerateArray())
        {
            if (!item.TryGetProperty("content", out var content))
            {
                continue;
            }

            foreach (var part in content.EnumerateArray())
            {
                if (!part.TryGetProperty("type", out var type) || type.GetString() != "output_text")
                {
                    continue;
                }

                if (part.TryGetProperty("text", out var text))
                {
                    return text.GetString();
                }
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

    /// <summary>
    /// Reads provider error details from response payload.
    /// </summary>
    private static string? ReadApiError(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.GetProperty("error").GetProperty("message").GetString();
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Reads only the provider's allowlisted machine-readable error token.</summary>
    internal static string? ReadApiErrorCode(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var error = document.RootElement.GetProperty("error");
            var code = AiProviderTelemetry.ReadString(error, "code");
            var type = AiProviderTelemetry.ReadString(error, "type");
            return AiProviderTelemetry.SafeToken(code, 64) ?? AiProviderTelemetry.SafeToken(type, 64);
        }
        catch
        {
            return null;
        }
    }
}
