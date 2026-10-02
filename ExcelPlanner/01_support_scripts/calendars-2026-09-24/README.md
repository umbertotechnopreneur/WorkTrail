# 24 September 2026 round

The operational workbook is two folders above, `Planner_Availability_A4.xlsx`, with the six CSV files in `Calendar data`. This folder contains sources, provenance and verification material.

## Result

- Informational A4 cover, one-page A4 planner and a two-page extended view.
- Eleven national calendars for 2026–2028, Chinese make-up days and personal-exception precedence.
- 211 saint entries across 191 dates. Informational and incomplete.
- A preloaded weather CSV and an optional blank OpenWeather key in Parameters B32.
- Six native queries preserved. The CSV path and the optional API branch were verified for seven cities in the original round.
- Native checks for holidays, margins, overnight periods, weekends, make-up days, exceptions, overlaps and input retention.

## Sources

- `prepare-datasets.py`, `holiday-manifest.json` and vendor data build the holiday snapshot with holidays 0.105 and its MIT notice.
- `saints-agent` contains observances, scripts, raw sources, manifests and the Apache-2.0 notice for LiturgicalCalendarAPI.
- `weather-agent` contains the read-only city export, cached fetcher, Power Query source and OpenWeather notes.
- `cover-agent` contains the cover source and preview.
- `build-revision.mjs` and `apply-revision.ps1` create the Exceptions sheet and preserve Power Query during the original migration.
- Verification scripts and previews are retained for historical reference. They are not required to use the planner.

The delivered workbook contains no API key. The privacy settings were not changed globally. Fast Combine was authorized for this workbook only.

Holiday data is a rule-derived base, not an official certification for every employer. Vietnam and Belarus remain without calendars. The saint list has 175 recurring dates missing and does not apply annual liturgical transfers.
