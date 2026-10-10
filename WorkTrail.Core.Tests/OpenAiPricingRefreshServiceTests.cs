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
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using WorkTrail.Services;
using Xunit;

namespace WorkTrail.Core.Tests;

public sealed class OpenAiPricingRefreshServiceTests
{
    [Fact]
    public void Parser_ExtractsStandardPricingRowsFromOpenAiMarkdown()
    {
        const string markdown = """
            # Pricing

            ### Standard pricing data

            | Model | Input | Cached input | Cache write | Output | Long context input | Long context cached input | Long context cache write | Long context output |
            | --- | --- | --- | --- | --- | --- | --- | --- | --- |
            | `gpt-test` | $1.250 | $0.125 | - | $10.000 | - | - | - | - |
            | `gpt-long` (>128K tokens) | $2.000 | $0.200 | $1.000 | $12.000 | $3.000 | $0.300 | $1.500 | $18.000 |

            ### Other pricing data
            """;
        var retrievedAt = new DateTimeOffset(2026, 2, 1, 8, 0, 0, TimeSpan.Zero);

        var prices = OpenAiPricingMarkdownParser.ParseStandardPricingData(
            markdown,
            retrievedAt,
            "https://developers.openai.com/api/docs/pricing.md");

        Assert.Equal(3, prices.Count);
        var shortPrice = Assert.Single(prices, price => price.Model == "gpt-test");
        Assert.Equal(AiPricingProviders.OpenAi, shortPrice.Provider);
        Assert.Equal(AiPricingServiceTiers.Standard, shortPrice.ServiceTier);
        Assert.Equal(AiPricingContextWindows.Short, shortPrice.ContextWindow);
        Assert.Equal(1.250m, shortPrice.InputUsdPerMillionTokens);
        Assert.Equal(0.125m, shortPrice.CachedInputUsdPerMillionTokens);
        Assert.Null(shortPrice.CacheWriteUsdPerMillionTokens);
        Assert.Equal(10.000m, shortPrice.OutputUsdPerMillionTokens);
        Assert.Equal(retrievedAt, shortPrice.SourceRetrievedAt);

        var longPrice = Assert.Single(prices, price =>
            price.Model == "gpt-long" && price.ContextWindow == AiPricingContextWindows.Long);
        Assert.Equal(3.000m, longPrice.InputUsdPerMillionTokens);
        Assert.Equal(0.300m, longPrice.CachedInputUsdPerMillionTokens);
        Assert.Equal(1.500m, longPrice.CacheWriteUsdPerMillionTokens);
        Assert.Equal(18.000m, longPrice.OutputUsdPerMillionTokens);
    }

    [Fact]
    public void Parser_RejectsMarkdownWithoutStandardPricingTable()
    {
        var exception = Assert.Throws<InvalidDataException>(() =>
            OpenAiPricingMarkdownParser.ParseStandardPricingData("# Pricing", DateTimeOffset.UtcNow));

        Assert.Contains("Standard pricing data", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DisposeAsync_PreventsNewRefreshWork()
    {
        var dataDirectory = Path.Combine(Path.GetTempPath(), "WorkTrail.Tests", Guid.NewGuid().ToString("N"));
        try
        {
            var service = new OpenAiPricingRefreshService(new LocalStore(dataDirectory));

            await service.DisposeAsync();

            Assert.Throws<ObjectDisposedException>(service.Start);
            await Assert.ThrowsAsync<ObjectDisposedException>(() => service.RefreshAsync(CancellationToken.None));
        }
        finally
        {
            if (Directory.Exists(dataDirectory))
            {
                Directory.Delete(dataDirectory, recursive: true);
            }
        }
    }
}
