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
using System.Text.Json;
using System.Text.Json.Serialization;
using WorkTrail.Search;

namespace WorkTrail.Services;

/// <summary>Loads and validates the versioned, per-locale search equivalence catalogs.</summary>
internal static class SearchSynonymConfiguration
{
    private const int SupportedSchemaVersion = 2;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };
    private static readonly HashSet<string> Categories =
    [
        "files", "communication", "calendar", "productivity",
        "development", "web", "systems", "office", "data",
        "administration", "multimedia", "security"
    ];

    /// <summary>Loads every supported locale and rejects invalid files, groups, or ambiguous terms.</summary>
    internal static ImmutableArray<SearchSynonymSet> Load(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!Path.IsPathFullyQualified(path))
        {
            throw new ArgumentException("The search synonym manifest path must be absolute.", nameof(path));
        }

        var manifest = ReadJson<SynonymManifest>(path);
        var locales = ProductLanguageCatalog.SearchChoices
            .Where(locale => locale != ProductLanguageCatalog.SystemLanguage).ToArray();
        if (manifest.SchemaVersion != SupportedSchemaVersion
            || manifest.Languages is null
            || !manifest.Languages.Order(StringComparer.Ordinal).SequenceEqual(locales.Order(StringComparer.Ordinal)))
        {
            throw new InvalidDataException($"Search synonym manifest '{path}' must use schema {SupportedSchemaVersion} and list every supported locale exactly once.");
        }

        var result = ImmutableArray.CreateBuilder<SearchSynonymSet>();
        Dictionary<string, string>? referenceConcepts = null;
        foreach (var locale in locales)
        {
            // Filenames come only from canonical product locales, never from untrusted manifest paths.
            // Missing files and malformed catalogs abort startup; an incomplete catalog is not substituted.
            var catalogPath = Path.Combine(Path.GetDirectoryName(path)!, "search-synonyms", locale + ".json");
            var catalog = ReadJson<LocalizedCatalog>(catalogPath);
            if (catalog.SchemaVersion != SupportedSchemaVersion || catalog.Language != locale
                || catalog.Sets is null || catalog.Sets.Count == 0)
            {
                throw new InvalidDataException($"Search synonym catalog '{catalogPath}' must use schema {SupportedSchemaVersion}, language '{locale}', and non-empty sets.");
            }

            var concepts = new Dictionary<string, string>(StringComparer.Ordinal);
            var owners = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var set in catalog.Sets)
            {
                ValidateSet(set, catalogPath, concepts, owners);
                result.Add(new SearchSynonymSet { Language = locale, Terms = [.. set!.Terms!.Select(term => term!)] });
            }

            if (referenceConcepts is not null
                && (concepts.Count != referenceConcepts.Count
                    || referenceConcepts.Any(pair => !concepts.TryGetValue(pair.Key, out var category) || category != pair.Value)))
            {
                throw new InvalidDataException($"Search synonym catalog '{catalogPath}' must contain the same concept identifiers and categories as '{locales[0]}'.");
            }

            referenceConcepts ??= concepts;
        }

        return result.ToImmutable();
    }

    /// <summary>Rejects malformed metadata, normalization collisions, and overlapping equivalence groups.</summary>
    private static void ValidateSet(
        CatalogSet? set,
        string path,
        IDictionary<string, string> concepts,
        IDictionary<string, string> owners)
    {
        var context = $"Search synonym catalog '{path}', group '{set?.Id ?? "<missing>"}'";
        if (set is null || string.IsNullOrWhiteSpace(set.Id)
            || set.Id.Length > 100 || !set.Id.All(character => char.IsAsciiLetterLower(character) || char.IsAsciiDigit(character) || character is '.' or '-')
            || set.Category is null || !Categories.Contains(set.Category))
        {
            throw new InvalidDataException($"{context}: an English concept identifier and a supported category are required.");
        }

        if (!concepts.TryAdd(set.Id, set.Category))
        {
            throw new InvalidDataException($"{context}: duplicate concept identifier.");
        }

        if (set.Terms is null || set.Terms.Count is < 2 or > 5)
        {
            throw new InvalidDataException($"{context}: each group requires two to five equivalent terms.");
        }

        foreach (var term in set.Terms)
        {
            if (string.IsNullOrWhiteSpace(term) || term != term.Trim() || term.Length > 160 || term.Any(char.IsControl))
            {
                throw new InvalidDataException($"{context}: terms must be non-empty, trimmed, single-line expressions of at most 160 characters.");
            }

            var normalized = SearchSynonymSet.NormalizeTerm(term);
            if (normalized.Length == 0)
            {
                throw new InvalidDataException($"{context}: a term is empty after normalization.");
            }

            if (owners.TryGetValue(normalized, out var previous))
            {
                throw new InvalidDataException($"{context}: term '{term}' duplicates a normalized term in group '{previous}'.");
            }

            owners.Add(normalized, set.Id);
        }
    }

    /// <summary>Reads a required UTF-8 catalog and attaches its path to parsing failures.</summary>
    private static T ReadJson<T>(string path) where T : class
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("Required search synonym configuration is missing.", path);
        }

        try
        {
            return JsonSerializer.Deserialize<T>(File.ReadAllText(path), JsonOptions)
                ?? throw new InvalidDataException($"Search synonym configuration '{path}' is empty.");
        }
        catch (JsonException exception)
        {
            // Invalid deployed JSON is actionable configuration failure, with no silent fallback.
            throw new InvalidDataException($"Search synonym configuration '{path}' contains invalid JSON: {exception.Message}", exception);
        }
    }

    private sealed record SynonymManifest(int SchemaVersion, IReadOnlyList<string>? Languages);
    private sealed record LocalizedCatalog(int SchemaVersion, string? Language, IReadOnlyList<CatalogSet?>? Sets);
    private sealed record CatalogSet(string? Id, string? Category, IReadOnlyList<string?>? Terms);
}
