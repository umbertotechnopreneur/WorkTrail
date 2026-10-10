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
using System.Text.Json;
using System.Text.Json.Nodes;
using WorkTrail.Services;
using Xunit;

namespace WorkTrail.Core.Tests;

/// <summary>Specifies actionable failures for deployed multilingual synonym catalogs.</summary>
public sealed class SearchSynonymConfigurationTests
{
    /// <summary>All canonical files load and retain the same concept coverage.</summary>
    [Fact]
    public void Load_ReadsAllCanonicalLocales()
    {
        using var fixture = new CatalogFixture();

        var sets = SearchSynonymConfiguration.Load(fixture.ManifestPath);

        Assert.Equal(20, sets.Length);
        Assert.All(fixture.Locales, locale => Assert.Equal(2, sets.Count(set => set.Language == locale)));
    }

    /// <summary>Obsolete schema and incomplete or duplicated locale manifests fail explicitly.</summary>
    [Theory]
    [InlineData("schema")]
    [InlineData("missing")]
    [InlineData("duplicate")]
    public void Load_RejectsInvalidManifest(string failure)
    {
        using var fixture = new CatalogFixture();
        var manifest = JsonNode.Parse(File.ReadAllText(fixture.ManifestPath))!.AsObject();
        if (failure == "schema")
        {
            manifest["schemaVersion"] = 1;
        }
        else if (failure == "missing")
        {
            manifest["languages"]!.AsArray().RemoveAt(0);
        }
        else
        {
            manifest["languages"]!.AsArray().Add(fixture.Locales[0]);
        }

        File.WriteAllText(fixture.ManifestPath, manifest.ToJsonString());

        var error = Assert.Throws<InvalidDataException>(() => SearchSynonymConfiguration.Load(fixture.ManifestPath));
        Assert.Contains(fixture.ManifestPath, error.Message);
    }

    /// <summary>Missing deployed files identify the absent locale instead of loading partial data.</summary>
    [Fact]
    public void Load_RejectsMissingCatalog()
    {
        using var fixture = new CatalogFixture();
        var path = fixture.CatalogPath("it-IT");
        File.Delete(path);

        var error = Assert.Throws<FileNotFoundException>(() => SearchSynonymConfiguration.Load(fixture.ManifestPath));

        Assert.Equal(path, error.FileName);
    }

    /// <summary>Malformed JSON includes the exact configuration path in its error.</summary>
    [Fact]
    public void Load_RejectsMalformedJson()
    {
        using var fixture = new CatalogFixture();
        var path = fixture.CatalogPath("it-IT");
        File.WriteAllText(path, "{");

        var error = Assert.Throws<InvalidDataException>(() => SearchSynonymConfiguration.Load(fixture.ManifestPath));

        Assert.Contains(path, error.Message);
        Assert.IsType<JsonException>(error.InnerException);
    }

    /// <summary>Accent and case collisions cannot silently merge different concepts.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Load_RejectsNormalizedDuplicates(bool sameGroup)
    {
        using var fixture = new CatalogFixture();
        fixture.Edit("it-IT", catalog =>
        {
            var group = catalog["sets"]![sameGroup ? 0 : 1]!;
            group["terms"]![1] = "ÀLPHA";
        });

        var error = Assert.Throws<InvalidDataException>(() => SearchSynonymConfiguration.Load(fixture.ManifestPath));

        Assert.Contains("it-IT.json", error.Message);
        Assert.Contains("test.alpha", error.Message);
        Assert.Contains("duplicates", error.Message);
    }

    /// <summary>Null groups, invalid categories, and oversized equivalence sets produce contextual errors.</summary>
    [Theory]
    [InlineData("null")]
    [InlineData("category")]
    [InlineData("terms")]
    [InlineData("id")]
    public void Load_RejectsInvalidGroup(string failure)
    {
        using var fixture = new CatalogFixture();
        fixture.Edit("it-IT", catalog =>
        {
            var sets = catalog["sets"]!.AsArray();
            if (failure == "null")
            {
                sets[0] = null;
            }
            else if (failure == "category")
            {
                sets[0]!["category"] = "unsupported";
            }
            else if (failure == "terms")
            {
                sets[0]!["terms"] = new JsonArray("one", "two", "three", "four", "five", "six");
            }
            else
            {
                sets[1]!["id"] = "test.alpha";
            }
        });

        var error = Assert.Throws<InvalidDataException>(() => SearchSynonymConfiguration.Load(fixture.ManifestPath));

        Assert.Contains("it-IT.json", error.Message);
        Assert.Contains("group", error.Message);
    }

    /// <summary>Every locale must cover the same concept IDs and categories.</summary>
    [Fact]
    public void Load_RejectsDifferentConceptCoverage()
    {
        using var fixture = new CatalogFixture();
        fixture.Edit("it-IT", catalog => catalog["sets"]![1]!["id"] = "test.different");

        var error = Assert.Throws<InvalidDataException>(() => SearchSynonymConfiguration.Load(fixture.ManifestPath));

        Assert.Contains("it-IT.json", error.Message);
        Assert.Contains("same concept", error.Message);
    }

    /// <summary>Creates a disposable minimal deployment with no application or user data.</summary>
    private sealed class CatalogFixture : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "WorkTrail-synonym-catalog-" + Guid.NewGuid().ToString("N"));
        internal string[] Locales { get; } = ProductLanguageCatalog.SearchChoices
            .Where(locale => locale != ProductLanguageCatalog.SystemLanguage).ToArray();
        internal string ManifestPath => Path.Combine(_root, "search-synonyms.json");

        /// <summary>Writes a complete ten-locale fixture using the current schema.</summary>
        internal CatalogFixture()
        {
            Directory.CreateDirectory(Path.Combine(_root, "search-synonyms"));
            File.WriteAllText(ManifestPath, JsonSerializer.Serialize(new { schemaVersion = 2, languages = Locales }));
            foreach (var locale in Locales)
            {
                File.WriteAllText(CatalogPath(locale), JsonSerializer.Serialize(new
                {
                    schemaVersion = 2,
                    language = locale,
                    sets = new[]
                    {
                        new { id = "test.alpha", category = "files", terms = new[] { "alpha", "beta" } },
                        new { id = "test.gamma", category = "files", terms = new[] { "gamma", "delta" } }
                    }
                }));
            }
        }

        /// <summary>Returns the filename of one canonical locale in this fixture.</summary>
        internal string CatalogPath(string locale) => Path.Combine(_root, "search-synonyms", locale + ".json");

        /// <summary>Applies one intentional configuration error to a fixture catalog.</summary>
        internal void Edit(string locale, Action<JsonObject> edit)
        {
            var path = CatalogPath(locale);
            var catalog = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
            edit(catalog);
            File.WriteAllText(path, catalog.ToJsonString());
        }

        /// <summary>Removes only the uniquely named temporary directory owned by this fixture.</summary>
        public void Dispose() => Directory.Delete(_root, recursive: true);
    }
}
