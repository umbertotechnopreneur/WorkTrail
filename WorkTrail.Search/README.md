# WorkTrail.Search

`WorkTrail.Search` is an independent, mandatory local-search service. It does not
depend on the WinUI application, `WorkTrail.Core`, or an AI provider.

## Public API

- `ILocalSearchService` / `LocalSearchService`
- revisioned `ApplyBatchAsync` and `RebuildAsync`, plus `SearchAsync` and `SuggestAsync`
- Immutable `SearchDocument`, `SearchRequest`, `SearchHit`, `SearchResponse`,
  `SearchOptions`, and `SearchSynonymSet` records

The caller supplies an absolute `SearchOptions.IndexRootPath`. The service stores
derived data below `lucene-v5`, validates the schema marker on reopen, serializes
all operations, and explicitly commits every mutation. Mutations always carry the
authoritative source revision. Suggestions keep their own revision marker and are
rebuilt lazily before lookup when missing or stale, coalescing multiple mutations
without serving suggestions from an older source snapshot.

Synonyms are caller-provided equivalence groups and are expanded only while
building a query. WorkTrail supplies 300 curated concepts for each of its ten
search locales: 3,000 localized groups across twelve everyday work topics.
The library itself embeds no general-purpose dictionary. Saved AI descriptions
are optional; activity, application names, window titles, and screenshot metadata
remain searchable without an AI description.

## Search synonyms

WorkTrail enables synonyms by default. For example, with Italian search selected,
`copia di sicurezza progetto` can also find `backup progetto`. The other words
in your search remain required. You can turn this off with **Use synonyms**.

The application reads a schema-2 manifest from
[`search-synonyms.json`](../WorkTrail.Core/search-synonyms.json) and a separate
[`search-synonyms/<locale>.json`](../WorkTrail.Core/search-synonyms) file for each
supported locale. Missing files, unknown schemas, repeated normalized terms,
and inconsistent concept coverage stop loading with a file and group diagnostic.
The files ship with the application and require no network requests.

The catalog covers files, communication, calendar, productivity, development,
the web, systems, Office documents, data, administration, multimedia, and security.
See the [catalog editing guide](../WorkTrail.Core/search-synonyms/README.md) for
the schema, coverage, and review rules.

The query matcher chooses the longest non-overlapping expressions in the original
query. It handles phrases embedded in longer text and can replace several spans
in one variant. Replacements are never expanded again. Word boundaries prevent
matches inside Latin identifiers and Korean words; dictionary boundaries allow
matching Han expressions in unspaced Chinese text. Paths, addresses, URLs, and
dotted filenames remain literal, including when a phrase would cross into one.

Expansion favors fewer substitutions, then query position and ordinal alias
order. At most `MaxSynonymExpansions` original spans are considered and at most
that many variants are returned (32 by default). Searches with more combinations
use this bounded subset. Synonym alternatives contribute their best score,
instead of accumulating scores for every alias. Original matches retain their
higher scoring weights.

Exact language catalogs take precedence over caller-defined primary-language
catalogs. If neither exists, documented product aliases select the corresponding
shipped locale: `en → en-US`, `it → it-IT`, `fr → fr-FR`, `de → de-DE`,
`es → es-ES`, `vi → vi-VN`, `ko → ko-KR`, and `pt → pt-PT`.
Other regional tags for these primary languages use the same defaults.
`pt-BR` stays separate. `zh`, `zh-CN`, `zh-SG`, `zh-MY`, and
`zh-Hans-…` may select `zh-Hans`; traditional Chinese tags do not.
An unconfigured language receives no synonyms.

Catalog matching folds case, accents, and whitespace using
`SearchSynonymSet.NormalizeTerm`. It does not automatically add plurals,
translate queries, or infer related concepts. English loanwords and useful
inflections must be explicit catalog entries. Updating synonym data requires an
application restart, but no index rebuild or index schema change.

## Verification Checklist

- [x] Search across raw activity and screenshot fields without an AI description
- [x] Italian, English, French, German, Spanish, Vietnamese, Simplified Chinese,
      Korean, European Portuguese, and Brazilian Portuguese analysis
- [x] Unicode fallback plus case and diacritic normalization, including Vietnamese `đ`
- [x] Query-time synonyms and controlled typo matching
- [ ] Run the prepared catalog and search regression tests: all ten locale files,
      metadata and normalization failures, embedded phrases, multiple replacements,
      locale isolation, literal filenames and addresses, Han boundaries, bounded
      large-catalog expansion, disabled synonyms, and exact-match ranking.
- [x] Exact and phrase ranking above synonym and fuzzy matches
- [x] Kind and timestamp filters
- [x] Batched upsert/delete, full rebuild, and explicit-commit persistence
- [x] Lazy suggestion repair after a stale marker, failed marker persistence, and reopen
- [x] Fail-fast UTF-16 field budgets and Lucene UTF-8 exact-term limits
- [x] Fail-fast validation for documents, requests, options, and committed source revisions
