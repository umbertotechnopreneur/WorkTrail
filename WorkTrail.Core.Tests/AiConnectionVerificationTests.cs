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


using WorkTrail.Application;
using Xunit;

namespace WorkTrail.Core.Tests;

public sealed class AiConnectionVerificationTests
{
    [Fact]
    public void CompletionRequiresASuccessForTheExactConfigurationAndCredential()
    {
        var verification = new AiConnectionVerification();
        var settings = new AppSettings();
        var syntheticCredential = new string('a', 32);

        Assert.False(verification.Matches(settings, syntheticCredential));
        verification.Record(settings, syntheticCredential);
        Assert.True(verification.Matches(settings with { OpenAiEnabled = true }, syntheticCredential));
        Assert.False(verification.Matches(settings, new string('b', 32)));
        Assert.False(verification.Matches(settings, null));
        Assert.False(verification.Matches(settings with { Model = "changed-model" }, syntheticCredential));
        Assert.False(verification.Matches(settings with { AiEndpoint = "https://example.invalid" }, syntheticCredential));
        Assert.False(verification.Matches(settings with { AiProvider = "anthropic" }, syntheticCredential));
        Assert.False(verification.Matches(settings with { AiApiKeyName = "OTHER_KEY" }, syntheticCredential));
        Assert.False(verification.Matches(settings with { AiReasoningEffort = "changed-effort" }, syntheticCredential));
        verification.Invalidate();
        Assert.False(verification.Matches(settings, syntheticCredential));
    }
}
