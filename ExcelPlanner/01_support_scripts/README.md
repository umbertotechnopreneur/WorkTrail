# Planner scripts

The current workbook is the template in the parent folder. These files preserve its authoring history, not a newly verified build pipeline.

- `planner_authoring_sources_2026-09-24/`: original builder, UTC/DST preparation and WorldClocks import. The base builder writes to ignored `artifacts/base-planner/`.
- `calendars-2026-09-24/`: national calendars, saint sources, weather fetcher and Power Query source, cover builder and one-time migration.
- `unicode_symbols-2026-09-24/`: Unicode authoring and the original narrow-edit verification scripts.
- Root relocation/archive utilities are historical; provide explicit locations where requested.

One-time migrations expect their earlier workbook version and generated intermediate files. Reports, backups and QA output stay outside Git. The current template already includes the calendar, exception, cover, weather and Unicode revisions.

## Dependencies

Excel COM scripts need Windows, desktop Excel and PowerShell 7. Data scripts use Python 3; holiday generation uses the versions in `requirements.txt`. Installed packages are not vendored.

The `.mjs` builders depend on `@oai/artifact-tool`, supplied by the Codex artifact runtime. That runtime is not distributed here. Provide it through the authoring environment; do not commit `node_modules` or runtime junctions.

Verification scripts were preserved but not rerun for this import. Review parameters and obtain the repository's required approval before running tests or API checks. Script syntax and copy-integrity checks are separate from a complete Excel runtime validation.

## Data and licenses

- Cities: derived from the repository's read-only `WorkTrail/Assets/WorldClocks/world-clocks.sqlite3` catalog, based on GeoNames (CC BY 4.0). Keep its existing attribution.
- Holidays: python-holidays 0.105. Source URLs and generation metadata remain in `calendars-2026-09-24/holiday-manifest.json`; library notices are in `licenses/`.
- Saints: original source data, Apache 2.0 license and provenance remain in `calendars-2026-09-24/saints-agent/`. Original public-fact examples retain their source URLs.
- Moon: USNO phase data, with source URLs in the CSV and original input JSON.
- Weather: OpenWeather observations are a dated sample; source and timestamps are in `Weather.csv`. No API key is included.

See [Data_Guide.md](Data_Guide.md) for fields, priorities and limits. The private mini-tool/IPC proposal remains in Obsidian.
