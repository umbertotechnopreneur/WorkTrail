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
using System.Linq;
using System.Text.Json;
using WorkTrail.Services;
using Xunit;

namespace WorkTrail.Core.Tests;

public sealed class AiPromptAndPayloadTests
{
    [Theory]
    [InlineData("compact")]
    [InlineData("balanced")]
    [InlineData("detailed")]
    public void MissingHardware_PreservesDeviceContextAndRequestedLanguage(string profile)
    {
        var snapshot = new SystemSnapshot(DateTimeOffset.UtcNow, "disabled", [], DeviceContext: new(
            new("Europe/Rome", "windows", "available"),
            new("it-IT", "windows", "available"),
            new("it-IT", "windows", "available"),
            new(null, null, null, "windows", "disabled_by_setting")));
        var context = new AnalysisContextSnapshot("App", "Task", "Window", "active", null, snapshot);
        var prompt = AiPromptCatalog.RenderScreenshotAnalysis(profile, context, language: "it-IT");
        Assert.Contains("in it-IT", prompt);
        Assert.Contains("Europe/Rome", prompt);
        Assert.DoesNotContain("Hardware at capture", prompt);
        Assert.DoesNotContain("{{", prompt);
        var empty = AiPromptCatalog.RenderScreenshotAnalysis(profile, null, _ => "{{SYSTEM_TELEMETRY}}");
        Assert.Empty(empty);
    }

    [Fact]
    public void CompactPrompt_OmitsSensorReadings()
    {
        var now = DateTimeOffset.UtcNow;
        var snapshot = new SystemSnapshot(now, "ready", [new("/cpu/0", "CPU", "Cpu", now,
            [new("/cpu/0/load", "CPU Total", "Load", "%", 0)])]);
        var context = new AnalysisContextSnapshot("App", "Task", "Window", "active", null, snapshot);
        Assert.Empty(AiPromptCatalog.RenderScreenshotAnalysis("compact", context, _ => "{{SYSTEM_TELEMETRY}}"));
        Assert.Contains("=0 %", AiPromptCatalog.RenderScreenshotAnalysis("balanced", context, _ => "{{SYSTEM_TELEMETRY}}"));
    }

    [Theory]
    [InlineData(2000)]
    [InlineData(6000)]
    public void SummaryBudget_OverridesScreenshotProfileForEveryProvider(int budget)
    {
        var settings = new AppSettings(AiOutputDetail: "compact");
        var options = new AiProviderRequestOptions(ReasoningEffort: "none", MaxOutputTokens: budget);
        using var openAi = JsonDocument.Parse(OpenAiDecoder.SerializePayload("summary", [], settings, options));
        using var router = JsonDocument.Parse(OpenRouterDecoder.SerializePayload("summary", [], settings, options));
        using var anthropic = JsonDocument.Parse(AnthropicDecoder.SerializePayload("summary", [], settings, options));
        Assert.Equal(budget, openAi.RootElement.GetProperty("max_output_tokens").GetInt32());
        Assert.Equal(budget, router.RootElement.GetProperty("max_tokens").GetInt32());
        Assert.Equal(budget, anthropic.RootElement.GetProperty("max_tokens").GetInt32());
    }

    [Fact]
    public void PromptRenderer_UsesHistoricalBatteryReadingsAndPreservesPowerCapacityDistinction()
    {
        var timestamp = new DateTimeOffset(2026, 9, 12, 10, 30, 0, TimeSpan.Zero);
        var sampledAt = timestamp.AddSeconds(-30);
        var snapshot = new SystemSnapshot(timestamp, "partial",
        [
            new("/battery/0", "Portable battery", "Battery", sampledAt,
            [
                new("/battery/0/power/0", "Discharge Rate", "Power", "W", 8.4),
                new("/battery/0/energy/0", "Remaining Capacity", "Energy", "mWh", 43000),
                new("/battery/0/temp/0", "Temperature", "Temperature", "°C", null)
            ])
        ]);
        var context = new AnalysisContextSnapshot("App", "Task", "Window", "active", null, snapshot);

        var prompt = AiPromptCatalog.RenderScreenshotAnalysis("detailed", context, _ => "{{SYSTEM_TELEMETRY}}");

        Assert.Contains($"collection_completed={timestamp:O}", prompt, StringComparison.Ordinal);
        Assert.Contains($"updated={sampledAt:O}", prompt, StringComparison.Ordinal);
        Assert.Contains("Discharge Rate (Power)=8.4 W", prompt, StringComparison.Ordinal);
        Assert.Contains("Remaining Capacity (Energy)=43000 mWh", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("Temperature (Temperature)", prompt, StringComparison.Ordinal);
        Assert.Contains("battery mWh is capacity", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("Device context", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void PromptRenderer_BoundsHardwareContextAndReportsOmittedMeasurements()
    {
        var timestamp = new DateTimeOffset(2026, 9, 12, 10, 30, 0, TimeSpan.Zero);
        var devices = Enumerable.Range(0, 20).Select(index => new HardwareDeviceSnapshot(
            $"/cpu/{index}", new string('C', 256), "Cpu", timestamp,
            Enumerable.Range(0, 100).Select(sensor => new HardwareSensorSnapshot(
                $"/cpu/{index}/temperature/{sensor}", new string('T', 256), "Temperature", "°C", 45)).ToArray())).ToArray();
        var snapshot = new SystemSnapshot(timestamp, "ready", devices);
        var context = new AnalysisContextSnapshot("App", "Task", "Window", "active", null, snapshot);

        var prompt = AiPromptCatalog.RenderScreenshotAnalysis("detailed", context, _ => "{{SYSTEM_TELEMETRY}}");

        Assert.True(prompt.Length < 3_100);
        Assert.Contains("[hardware summary truncated]", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain(new string('T', 97), prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("Device context", prompt, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("{\"error\":{\"code\":\"insufficient_quota\",\"type\":\"requests\"}}", "insufficient_quota")]
    [InlineData("{\"error\":{\"code\":\"rate_limit_exceeded\",\"type\":\"requests\"}}", "rate_limit_exceeded")]
    [InlineData("{\"error\":{\"code\":\"unsafe markup\",\"type\":\"rate_limit_error\"}}", "rate_limit_error")]
    [InlineData("{\"error\":{\"code\":\"<script>\",\"type\":\"also unsafe\"}}", null)]
    public void OpenAiErrorCode_OnlyReturnsAllowlistedMachineTokens(string response, string? expected)
    {
        Assert.Equal(expected, OpenAiDecoder.ReadApiErrorCode(response));
    }

    [Fact]
    public void Profiles_HaveDeterministicBudgetsAndDetailLevels()
    {
        var compact = AiAnalysisProfileCatalog.Resolve(" COMPACT ");
        var balanced = AiAnalysisProfileCatalog.Resolve("balanced");
        var detailed = AiAnalysisProfileCatalog.Resolve("Detailed");

        Assert.Equal(("compact", 512, "low", "low"),
            (compact.Name, compact.MaxOutputTokens, compact.ImageDetail, compact.TextVerbosity));
        Assert.Equal(("balanced", 1024, "auto", "medium"),
            (balanced.Name, balanced.MaxOutputTokens, balanced.ImageDetail, balanced.TextVerbosity));
        Assert.Equal(("detailed", 2048, "high", "high"),
            (detailed.Name, detailed.MaxOutputTokens, detailed.ImageDetail, detailed.TextVerbosity));
        Assert.Equal("balanced", AiAnalysisProfileCatalog.Resolve("unknown").Name);
    }

    [Fact]
    public void PromptRenderer_LoadsProfileAssetAndRendersContext()
    {
        var context = new AnalysisContextSnapshot(
            "Visual Studio",
            "WorkTrail",
            "AiPromptCatalog.cs",
            "active",
            new Dictionary<string, string>
            {
                ["FocusedScreen"] = "Monitor 2",
                ["FocusedCapture"] = "monitor-2"
            });

        var prompt = AiPromptCatalog.RenderScreenshotAnalysis("balanced", context);

        Assert.Contains("## Visible data", prompt, StringComparison.Ordinal);
        Assert.Contains("application=Visual Studio", prompt, StringComparison.Ordinal);
        Assert.Contains("detail=WorkTrail", prompt, StringComparison.Ordinal);
        Assert.Contains("focused_screen=Monitor 2", prompt, StringComparison.Ordinal);
        Assert.Contains("focused_capture=monitor-2", prompt, StringComparison.Ordinal);
        Assert.Contains("1024", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("{{LOCAL_CONTEXT}}", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void PromptRenderer_FailsWhenRequiredAssetCannotBeLoaded()
    {
        Assert.Throws<InvalidOperationException>(() => AiPromptCatalog.RenderScreenshotAnalysis(
            "compact",
            new AnalysisContextSnapshot("App", "Task", "Window", "active", null),
            _ => throw new InvalidOperationException("simulated loader failure")));
    }

    [Fact]
    public void PromptRenderer_AppendsConfiguredCustomInstructionAfterBuiltInPrompt()
    {
        var prompt = AiPromptCatalog.RenderScreenshotAnalysis(
            "compact",
            new AnalysisContextSnapshot("App", "Task", "Window", "active", null, InformationalSchedule: "Monday: planned active hours 09:00-18:00."),
            customPrompt: "Prefer concise findings.");

        Assert.Contains("informational_schedule=Monday: planned active hours 09:00-18:00.", prompt, StringComparison.Ordinal);
        Assert.Contains("## Additional user instruction", prompt, StringComparison.Ordinal);
        Assert.EndsWith("Prefer concise findings.", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void ProviderUsageReaders_PreserveNullableAndProviderSpecificFields()
    {
        using var openAi = JsonDocument.Parse("""
            {"usage":{"input_tokens":10,"output_tokens":5,"total_tokens":15,"input_tokens_details":{"cached_tokens":3,"cache_write_tokens":2},"output_tokens_details":{"reasoning_tokens":4}}}
            """);
        using var anthropic = JsonDocument.Parse("""
            {"usage":{"input_tokens":10,"output_tokens":5,"cache_read_input_tokens":3,"cache_creation_input_tokens":2,"output_tokens_details":{"thinking_tokens":4}}}
            """);
        using var openRouter = JsonDocument.Parse("""
            {"usage":{"prompt_tokens":10,"completion_tokens":5,"total_tokens":15,"prompt_tokens_details":{"cached_tokens":3,"cache_write_tokens":2},"completion_tokens_details":{"reasoning_tokens":4},"cost":0.0125,"cost_details":{"upstream_inference_cost":0.01}}}
            """);

        var openAiUsage = OpenAiDecoder.ReadUsage(openAi.RootElement);
        var anthropicUsage = AnthropicDecoder.ReadUsage(anthropic.RootElement);
        var openRouterUsage = OpenRouterDecoder.ReadUsage(openRouter.RootElement);

        Assert.Equal(3, openAiUsage.CachedInputTokens);
        Assert.Equal(2, openAiUsage.CacheWriteTokens);
        Assert.Equal(4, openAiUsage.ReasoningTokens);
        Assert.Equal(15, anthropicUsage.InputTokens);
        Assert.Equal(20, anthropicUsage.TotalTokens);
        Assert.Equal(4, anthropicUsage.ThinkingTokens);
        Assert.Equal(0.0125m, openRouterUsage.ReportedCostUsd);
        Assert.Equal(0.01m, openRouterUsage.ReportedUpstreamCostUsd);
    }

    [Fact]
    public void OpenAiIncompleteResponse_IsTypedAndPreservesUsage()
    {
        const string response = """
            {
              "id": "resp_test",
              "model": "gpt-5.4",
              "status": "incomplete",
              "incomplete_details": { "reason": "max_output_tokens" },
              "usage": {
                "input_tokens": 120,
                "output_tokens": 2048,
                "total_tokens": 2168,
                "output_tokens_details": { "reasoning_tokens": 2048 }
              },
              "output": []
            }
            """;

        var exception = Assert.Throws<AiProviderRequestException>(() =>
            OpenAiDecoder.ParseSuccessfulResponse(response, 200, 37, "req_test", 11));

        Assert.Equal("incomplete.max_output_tokens", exception.Failure.FailureCode);
        Assert.Equal(200, exception.Failure.HttpStatusCode);
        Assert.Equal("resp_test", exception.Failure.ProviderResponseId);
        Assert.Equal("req_test", exception.Failure.ProviderRequestId);
        Assert.Equal("incomplete", exception.Failure.FinishReason);
        Assert.Equal(2048, exception.Failure.Usage?.OutputTokens);
        Assert.Equal(2048, exception.Failure.Usage?.ReasoningTokens);
    }

    [Fact]
    public void AnthropicHeaders_UseProviderRequiredApiKeyHeader()
    {
        using var request = new System.Net.Http.HttpRequestMessage();

        AnthropicDecoder.ApplyRequiredHeaders(request, "test-key");

        Assert.Null(request.Headers.Authorization);
        Assert.Equal("test-key", Assert.Single(request.Headers.GetValues("x-api-key")));
        Assert.Equal("2023-06-01", Assert.Single(request.Headers.GetValues("anthropic-version")));
    }

    [Fact]
    public void OpenAiPayload_AppliesDetailedProfileAndReasoningEffort()
    {
        var settings = new AppSettings(
            Model: "gpt-5.6",
            AiOutputDetail: "detailed",
            AiReasoningEffort: "high");

        using var payload = JsonDocument.Parse(OpenAiDecoder.SerializePayload(
            "analyze",
            ["data:image/webp;base64,AAAA"],
            settings));

        var root = payload.RootElement;
        Assert.Equal(2048, root.GetProperty("max_output_tokens").GetInt32());
        Assert.Equal("high", root.GetProperty("text").GetProperty("verbosity").GetString());
        Assert.Equal("high", root.GetProperty("reasoning").GetProperty("effort").GetString());
        Assert.Equal("high", root.GetProperty("input")[0].GetProperty("content")[1].GetProperty("detail").GetString());
    }

    [Fact]
    public void OpenAiPayload_OcrOverrideOmitsAppOutputLimitAndDisablesReasoning()
    {
        var settings = new AppSettings(
            Model: "gpt-5.4",
            AiOutputDetail: "detailed",
            AiReasoningEffort: "medium");

        using var payload = JsonDocument.Parse(OpenAiDecoder.SerializePayload(
            "refine OCR",
            [],
            settings,
            new AiProviderRequestOptions(
                OmitOutputTokenLimitWhenSupported: true,
                ReasoningEffort: "none")));

        var root = payload.RootElement;
        Assert.False(root.TryGetProperty("max_output_tokens", out _));
        Assert.Equal("none", root.GetProperty("reasoning").GetProperty("effort").GetString());
    }

    [Fact]
    public void ThirdPartyPayloads_OcrOverrideOmitsOpenRouterLimitButKeepsAnthropicLimit()
    {
        var settings = new AppSettings(
            AiOutputDetail: "detailed",
            AiReasoningEffort: "medium");
        var options = new AiProviderRequestOptions(
            OmitOutputTokenLimitWhenSupported: true,
            ReasoningEffort: "none");

        using var openRouterPayload = JsonDocument.Parse(OpenRouterDecoder.SerializePayload(
            "refine OCR",
            [],
            settings,
            options));
        Assert.False(openRouterPayload.RootElement.TryGetProperty("max_tokens", out _));
        Assert.Equal("none", openRouterPayload.RootElement.GetProperty("reasoning").GetProperty("effort").GetString());

        using var anthropicPayload = JsonDocument.Parse(AnthropicDecoder.SerializePayload(
            "refine OCR",
            [],
            settings,
            options));
        Assert.Equal(2048, anthropicPayload.RootElement.GetProperty("max_tokens").GetInt32());
        Assert.False(anthropicPayload.RootElement.TryGetProperty("reasoning", out _));
    }

    [Theory]
    [InlineData("auto")]
    [InlineData("unsupported")]
    public void OpenAiPayload_OmitsReasoningWhenAutomaticOrInvalid(string effort)
    {
        var settings = new AppSettings(AiReasoningEffort: effort);

        using var payload = JsonDocument.Parse(OpenAiDecoder.SerializePayload(
            "analyze",
            [],
            settings));

        Assert.False(payload.RootElement.TryGetProperty("reasoning", out _));
    }

    [Fact]
    public void ThirdPartyPayloads_ApplyOnlyCompatibleBudgetFields()
    {
        var openRouterSettings = new AppSettings(
            AiOutputDetail: "detailed",
            AiReasoningEffort: "max");
        using var openRouterPayload = JsonDocument.Parse(OpenRouterDecoder.SerializePayload(
            "analyze",
            ["data:image/webp;base64,AAAA"],
            openRouterSettings));

        Assert.Equal(2048, openRouterPayload.RootElement.GetProperty("max_tokens").GetInt32());
        Assert.Equal("high", openRouterPayload.RootElement.GetProperty("messages")[0]
            .GetProperty("content")[1].GetProperty("image_url").GetProperty("detail").GetString());
        Assert.False(openRouterPayload.RootElement.TryGetProperty("reasoning", out _));
        Assert.False(openRouterPayload.RootElement.TryGetProperty("text", out _));

        var anthropicSettings = new AppSettings(
            AiOutputDetail: "compact",
            AiReasoningEffort: "high");
        using var anthropicPayload = JsonDocument.Parse(AnthropicDecoder.SerializePayload(
            "analyze",
            ["AAAA"],
            anthropicSettings));

        Assert.Equal(512, anthropicPayload.RootElement.GetProperty("max_tokens").GetInt32());
        Assert.False(anthropicPayload.RootElement.TryGetProperty("reasoning", out _));
        Assert.False(anthropicPayload.RootElement.TryGetProperty("text", out _));
    }
}
