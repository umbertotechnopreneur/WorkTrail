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


using System.Collections.Immutable;
using WorkTrail.Search.Internal;
using Xunit;

namespace WorkTrail.Search.Tests;

/// <summary>Specifies phrase, language, precision, and expansion-budget behavior.</summary>
public sealed class SynonymCatalogTests
{
    /// <summary>Embedded phrases and multiple substitutions preserve the rest of the request.</summary>
    [Fact]
    public void Expand_ReplacesTwoOriginalSpansAndPreservesContext()
    {
        var catalog = Create("it-IT", ["copia di sicurezza", "backup"], ["scadenza", "deadline"]);

        var variants = catalog.Expand("copia di sicurezza progetto scadenza", "it-IT");

        Assert.Contains("backup progetto scadenza", variants);
        Assert.Contains("copia di sicurezza progetto deadline", variants);
        Assert.Contains("backup progetto deadline", variants);
        Assert.All(variants, variant => Assert.Contains("progetto", variant));
        Assert.Equal(3, variants.Length);
    }

    /// <summary>A more specific phrase wins over a shorter term inside it.</summary>
    [Fact]
    public void Expand_PrefersLongestPhrase()
    {
        var catalog = Create("en-US", ["screen", "display"], ["screen capture", "screenshot"]);

        Assert.Equal<string>(["screenshot project"], catalog.Expand("screen capture project", "en-US"));
    }

    /// <summary>Word punctuation allows matching, while partial words and identifiers do not.</summary>
    [Theory]
    [InlineData("cartella, progetto", "directory, progetto")]
    [InlineData("cartella.", "directory.")]
    [InlineData("cartelle progetto", "directory progetto")]
    public void Expand_AcceptsPunctuationAndExplicitPlural(string query, string expected)
    {
        var catalog = Create("it-IT", ["cartella", "cartelle", "directory"]);

        Assert.Contains(expected, catalog.Expand(query, "it-IT"));
    }

    /// <summary>Technical tokens and longer words retain their literal meaning.</summary>
    [Theory]
    [InlineData("cartellario")]
    [InlineData("cartella_utente")]
    [InlineData(@"C:\cartella\report.txt")]
    [InlineData("https://example.invalid/cartella")]
    [InlineData("cartella@example.invalid")]
    [InlineData("cartella.txt")]
    public void Expand_DoesNotChangeProtectedTokens(string query)
    {
        Assert.Empty(Create("it-IT", ["cartella", "directory"]).Expand(query, "it-IT"));
    }

    /// <summary>A phrase cannot consume the beginning of a later filename or address.</summary>
    [Theory]
    [InlineData("file name.txt")]
    [InlineData("file name@example.invalid")]
    public void Expand_DoesNotCrossIntoProtectedToken(string query)
    {
        Assert.Empty(Create("en-US", ["file name", "filename"]).Expand(query, "en-US"));
    }

    /// <summary>A literal address does not prevent expansion of a separate query phrase.</summary>
    [Fact]
    public void Expand_PreservesAddressAndExpandsOtherWords()
    {
        var catalog = Create("it-IT", ["cartella", "directory"]);

        Assert.Equal<string>(["directory cartella@example.invalid"],
            catalog.Expand("cartella cartella@example.invalid", "it-IT"));
    }

    /// <summary>Dictionary matches inside unspaced Han text preserve adjacent concepts.</summary>
    [Fact]
    public void Expand_SeparatesLatinAliasFromAdjacentHanText()
    {
        var catalog = Create("zh-Hans", ["屏幕截图", "screenshot"]);

        Assert.Equal<string>(["项目 screenshot"], catalog.Expand("项目屏幕截图", "zh-CN"));
    }

    /// <summary>Supplementary Han characters receive the same boundaries as basic ideographs.</summary>
    [Fact]
    public void Expand_SeparatesAliasFromSupplementaryHan()
    {
        var catalog = Create("zh-Hans", ["屏幕截图", "screenshot"]);

        Assert.Equal<string>(["𠀀 screenshot 𠀁"], catalog.Expand("𠀀屏幕截图𠀁", "zh-Hans"));
    }

    /// <summary>Korean matching respects whole words and embedded phrases.</summary>
    [Fact]
    public void Expand_RespectsKoreanWordBoundaries()
    {
        var catalog = Create("ko-KR", ["데이터베이스", "database"]);

        Assert.Equal<string>(["database 고객"], catalog.Expand("데이터베이스 고객", "ko"));
        Assert.Empty(catalog.Expand("데이터베이스화", "ko"));
    }

    /// <summary>Vietnamese phrases use the same accent folding as indexed query text.</summary>
    [Fact]
    public void Expand_MatchesVietnamesePhraseWithOrWithoutDiacritics()
    {
        var catalog = Create("vi-VN", ["sao lưu", "backup"]);

        Assert.Equal<string>(["backup du an"], catalog.Expand("sao lưu dự án", "vi"));
        Assert.Equal<string>(["backup du an"], catalog.Expand("sao luu du an", "vi-VN"));
    }

    /// <summary>Explicit locale aliases find canonical catalogs without merging language data.</summary>
    [Theory]
    [InlineData("en-US", "en-GB")]
    [InlineData("it-IT", "it")]
    [InlineData("fr-FR", "fr-CA")]
    [InlineData("de-DE", "de-AT")]
    [InlineData("es-ES", "es-MX")]
    [InlineData("vi-VN", "vi")]
    [InlineData("ko-KR", "ko")]
    [InlineData("zh-Hans", "zh-CN")]
    [InlineData("zh-Hans", "zh-Hans-CN")]
    [InlineData("pt-PT", "pt")]
    [InlineData("pt-BR", "pt-BR")]
    public void Expand_ResolvesSupportedLocaleAlias(string catalogLanguage, string queryLanguage)
    {
        Assert.Equal<string>(["beta"], Create(catalogLanguage, ["alpha", "beta"]).Expand("alpha", queryLanguage));
    }

    /// <summary>Unconfigured scripts, regional Portuguese catalogs, and missing languages remain separate.</summary>
    [Theory]
    [InlineData("zh-Hans", "zh-Hant")]
    [InlineData("zh-Hans", "zh-TW")]
    [InlineData("pt-PT", "pt-BR")]
    [InlineData("pt-BR", "pt-PT")]
    [InlineData("it-IT", "en-US")]
    [InlineData("it-IT", null)]
    public void Expand_DoesNotBorrowUnrelatedCatalog(string catalogLanguage, string? queryLanguage)
    {
        Assert.Empty(Create(catalogLanguage, ["alpha", "beta"]).Expand("alpha", queryLanguage));
    }

    /// <summary>New aliases are never fed back through other groups.</summary>
    [Fact]
    public void Expand_DoesNotFollowTransitiveEquivalences()
    {
        var catalog = Create("en", ["alpha", "beta"], ["beta", "gamma"]);

        Assert.Equal<string>(["beta"], catalog.Expand("alpha", "en-US"));
    }

    /// <summary>Catalog input order does not change bounded output, even for a large catalog.</summary>
    [Fact]
    public void Expand_LargeCatalogKeepsDeterministicBudget()
    {
        var sets = Enumerable.Range(0, 3000).Select(index => new SearchSynonymSet
        {
            Language = "en-US",
            Terms = [$"concept{index}", $"alias{index}", $"variant{index}"]
        }).ToImmutableArray();
        var options = new SearchOptions { IndexRootPath = Path.GetTempPath(), SynonymSets = sets, MaxSynonymExpansions = 32 };
        var query = string.Join(' ', Enumerable.Range(0, 8).Select(index => $"concept{index}"));
        var first = new SynonymCatalog(options).Expand(query, "en-US");
        var reversed = new SynonymCatalog(options with { SynonymSets = [.. sets.Reverse()] }).Expand(query, "en-US");

        Assert.Equal(32, first.Length);
        Assert.Equal<string>(first, reversed);
        Assert.Equal(first.Length, first.Distinct(StringComparer.Ordinal).Count());
        Assert.Contains(first, variant => variant.Contains("alias0 alias1", StringComparison.Ordinal));
    }

    /// <summary>Builds an isolated catalog without starting the search service or creating an index.</summary>
    private static SynonymCatalog Create(string language, params string[][] groups) => new(new SearchOptions
    {
        IndexRootPath = Path.GetTempPath(),
        SynonymSets = [.. groups.Select(terms => new SearchSynonymSet { Language = language, Terms = [.. terms] })]
    });
}
