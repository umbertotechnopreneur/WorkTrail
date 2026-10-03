# Saints data agent

This folder contains the source and helper code used to prepare the saints and fixed-observance snapshot.

## Scope

- The workbook uses an informational selection of fixed observances.
- It is not a complete sanctoral or annual liturgical calendar.
- Observances do not close planner availability; use the holiday calendars or personal exceptions for closures.

## Provenance

Keep `LICENSE-PROVENANCE.md`, `provenance.json`, and the files under `raw/` together with the generated data. The raw Italian and Latin source filenames are preserved because they identify the original source material.

## Refreshing data

Run `fetch_saints.py` only when the source snapshot is intentionally updated. Review the generated data and provenance before replacing the workbook input. Do not remove or rewrite third-party notices.
