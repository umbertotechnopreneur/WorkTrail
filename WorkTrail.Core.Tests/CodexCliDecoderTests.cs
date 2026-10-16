// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using WorkTrail.Application;
using WorkTrail.Services;
using Xunit;

namespace WorkTrail.Core.Tests;

public sealed class CodexCliDecoderTests
{
    [Fact]
    public void CompletedTurn_PreservesDescriptionAndUsageWithoutInventingHttpStatusOrCost()
    {
        var result = CodexCliDecoder.ParseResponse("""
            {"type":"thread.started","thread_id":"thread-1"}
            {"type":"item.completed","item":{"type":"agent_message","text":"Reviewing the application settings."}}
            {"type":"turn.completed","usage":{"input_tokens":120,"output_tokens":12,"cached_input_tokens":30}}
            """, 250);

        Assert.Equal("Reviewing the application settings.", result.Text);
        Assert.Equal(120L, result.Usage.InputTokens);
        Assert.Equal(12L, result.Usage.OutputTokens);
        Assert.Equal(30L, result.Usage.CachedInputTokens);
        Assert.Null(result.HttpStatusCode);
        Assert.Null(result.ReturnedModel);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not JSON")]
    [InlineData("{\"type\":\"turn.completed\"}")]
    [InlineData("{\"type\":\"item.completed\",\"item\":{\"type\":\"agent_message\",\"text\":\"Partial\"}}")]
    public void IncompleteOrInvalidOutput_IsNeverAccepted(string output)
    {
        var exception = Assert.Throws<AiProviderRequestException>(() => CodexCliDecoder.ParseResponse(output, 10));
        Assert.Equal("codex.invalid_response", exception.Failure.FailureCode);
    }

    [Theory]
    [InlineData("401 unauthorized", "codex.authentication")]
    [InlineData("Usage limit reached", "codex.limit")]
    [InlineData("unexpected argument --ignore-user-config", "codex.update_required")]
    [InlineData("403 access denied", "codex.access")]
    [InlineData("connection failed", "codex.network")]
    [InlineData("timed out", "codex.timeout")]
    public void ProviderFailures_HaveStableActionableCodes(string diagnostic, string expected)
    {
        Assert.Equal(expected, CodexCliDecoder.ClassifyFailure(diagnostic));
    }

    [Fact]
    public void FailedTurn_DoesNotAcceptEarlierPartialText()
    {
        var exception = Assert.Throws<AiProviderRequestException>(() => CodexCliDecoder.ParseResponse("""
            {"type":"item.completed","item":{"type":"agent_message","text":"Partial"}}
            {"type":"turn.failed","error":{"message":"Usage limit reached"}}
            """, 10));
        Assert.Equal("codex.limit", exception.Failure.FailureCode);
        Assert.DoesNotContain("Usage limit", exception.Message);
    }

    [Fact]
    public void ProcessArguments_AreNoninteractiveAndDoNotInheritApiCredentials()
    {
        var folder = Path.GetTempPath();
        var image = Path.Combine(folder, "screen with spaces & punctuation.png");
        var info = CodexCliDecoder.CreateStartInfo(new CodexCliInstallation("codex.exe"), folder, [image]);
        var args = info.ArgumentList.ToArray();

        Assert.False(info.UseShellExecute);
        Assert.True(info.CreateNoWindow);
        Assert.True(info.RedirectStandardInput);
        Assert.Contains("--ephemeral", args);
        Assert.Contains("--ignore-user-config", args);
        Assert.Contains("read-only", args);
        Assert.Contains("forced_login_method=\"chatgpt\"", args);
        Assert.Contains("features.shell_tool=false", args);
        Assert.Contains(image, args);
        Assert.Equal("-", args[^1]);
        Assert.False(info.Environment.ContainsKey("OPENAI_API_KEY"));
        Assert.False(info.Environment.ContainsKey("CODEX_API_KEY"));
        Assert.False(info.Environment.ContainsKey("WORKTRAIL_OPENAI_APIKEY"));
    }

    [Fact]
    public void SwitchingTransport_RetainsApiKeyVariableAndRequiresActivation()
    {
        var original = new AppSettings(AiApiKeyName: "WORKTRAIL_OPENAI_APIKEY", OpenAiEnabled: true);
        var cli = SettingsCatalog.Apply(original, new SettingsPatch(new Dictionary<string, string?> { ["ai.provider"] = "codex-cli" }));
        Assert.True(cli.Succeeded);
        Assert.False(cli.Value!.OpenAiEnabled);
        Assert.Equal(original.AiApiKeyName, cli.Value.AiApiKeyName);
        Assert.IsType<CodexCliDecoder>(AIDecoderFactory.Create(cli.Value));
        var api = SettingsCatalog.Apply(cli.Value, new SettingsPatch(new Dictionary<string, string?> { ["ai.provider"] = "openai" }));
        Assert.True(api.Succeeded);
        Assert.Equal(original.AiApiKeyName, api.Value!.AiApiKeyName);
        Assert.IsType<OpenAiDecoder>(AIDecoderFactory.Create(api.Value));
        Assert.Throws<InvalidOperationException>(() => AIDecoderFactory.Create(original with { AiProvider = "anthropic" }));
    }

    [Fact]
    public void CliConnectionVerification_RequiresSuccessfulRecordButNoApiCredential()
    {
        var settings = new AppSettings(AiProvider: "codex-cli");
        var verification = new AiConnectionVerification();
        Assert.False(verification.Matches(settings, string.Empty));
        verification.Record(settings, string.Empty);
        Assert.True(verification.Matches(settings, string.Empty));
        Assert.False(verification.Matches(settings with { AiProvider = "openai" }, string.Empty));
        verification.Invalidate();
        Assert.False(verification.Matches(settings, string.Empty));
    }

    [Fact]
    public async Task AlreadyCancelledRequest_DoesNotDiscoverOrStartCli()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new CodexCliDecoder().DecodeAsync(
            "Describe the screen.", Array.Empty<string>(), new AppSettings(AiProvider: "codex-cli"),
            string.Empty, "test", cancellationToken: cancellation.Token));
    }
}
