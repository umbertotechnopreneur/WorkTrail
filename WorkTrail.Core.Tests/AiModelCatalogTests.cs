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
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using WorkTrail.Application;
using Xunit;

namespace WorkTrail.Core.Tests;

public sealed class AiModelCatalogTests
{
    [Fact]
    public void DeployedCatalog_LoadsRequiredModelsAndAlias()
    {
        var deployedPath = Path.Combine(AppContext.BaseDirectory, AiModelCatalog.DefaultFileName);

        Assert.True(File.Exists(deployedPath), $"Required catalog asset was not copied to '{deployedPath}'.");
        var catalog = AiModelCatalog.LoadDefault();

        Assert.Equal(1, catalog.SchemaVersion);
        Assert.Contains(catalog.Models, model => model.Key == "gpt-5.6-terra");
        Assert.Contains(catalog.Models, model => model.Key == "gpt-5.6-luna");
        var sol = Assert.Single(catalog.Models, model => model.Key == "gpt-5.6-sol");
        Assert.Equal(["auto", "none", "low", "medium", "high", "xhigh", "max"], sol.SupportedThinkingEfforts);
        Assert.True(catalog.TryResolve("gpt-5.6", out var aliased));
        Assert.Same(sol, aliased);

        var spark = Assert.Single(catalog.Models, model => model.Key == "gpt-5.3-codex-spark");
        Assert.True(spark.IsPreview);
        Assert.False(spark.SupportsImageInput);
        Assert.Equal("research-preview", spark.Availability);
        Assert.Contains("xhigh", spark.SupportedThinkingEfforts);
    }

    [Fact]
    public void Read_RejectsMalformedJson()
    {
        Assert.Throws<JsonException>(() => Read("{ invalid json"));
    }

    [Fact]
    public void Read_RejectsDuplicateCanonicalKeys()
    {
        var json = $$"""
        {
          "aiModelCatalog": {
            "schemaVersion": 1,
            "models": [
              {{ValidModel("gpt-test")}},
              {{ValidModel("gpt-test")}}
            ]
          }
        }
        """;

        var error = Assert.Throws<InvalidDataException>(() => Read(json));

        Assert.Contains("Duplicate model key", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Read_RejectsAliasThatCollidesWithCanonicalKey()
    {
        var json = $$"""
        {
          "aiModelCatalog": {
            "schemaVersion": 1,
            "models": [
              {{ValidModel("gpt-test", "[\"gpt-other\"]")}},
              {{ValidModel("gpt-other")}}
            ]
          }
        }
        """;

        var error = Assert.Throws<InvalidDataException>(() => Read(json));

        Assert.Contains("Duplicate model identifier", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("\"color\": \"#123456\"", "\"color\": \"blue\"", "invalid color")]
    [InlineData("\"supportedThinkingEfforts\": [\"medium\"]", "\"supportedThinkingEfforts\": [\"extreme\"]", "invalid thinking effort")]
    public void Read_RejectsInvalidModelMetadata(string original, string replacement, string expectedMessage)
    {
        var json = CatalogWithSingleModel().Replace(original, replacement, StringComparison.Ordinal);

        var error = Assert.Throws<InvalidDataException>(() => Read(json));

        Assert.Contains(expectedMessage, error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Read_RejectsUnknownProperties()
    {
        var json = CatalogWithSingleModel().Replace(
            "\"schemaVersion\": 1,",
            "\"schemaVersion\": 1, \"unexpected\": true,",
            StringComparison.Ordinal);

        Assert.Throws<JsonException>(() => Read(json));
    }

    [Fact]
    public void Read_RejectsMissingImageInputCapability()
    {
        var catalog = Assert.IsType<JsonObject>(JsonNode.Parse(CatalogWithSingleModel()));
        var models = Assert.IsType<JsonArray>(catalog["aiModelCatalog"]?["models"]);
        var model = Assert.IsType<JsonObject>(Assert.Single(models));
        Assert.True(model.Remove("supportsImageInput"));

        var error = Assert.Throws<InvalidDataException>(() => Read(catalog.ToJsonString()));

        Assert.Contains("supportsImageInput", error.Message, StringComparison.Ordinal);
    }

    private static AiModelCatalog Read(string json)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        return AiModelCatalog.Read(stream);
    }

    private static string CatalogWithSingleModel() => $$"""
    {
      "aiModelCatalog": {
        "schemaVersion": 1,
        "models": [
          {{ValidModel("gpt-test")}}
        ]
      }
    }
    """;

    private static string ValidModel(string key, string aliases = "[]") => $$"""
    {
      "key": "{{key}}",
      "aliases": {{aliases}},
      "name": "Test model",
      "description": "Model used by catalog validation tests.",
      "color": "#123456",
      "supportedThinkingEfforts": ["medium"],
      "supportsImageInput": true,
      "availability": "general",
      "isPreview": false
    }
    """;
}
