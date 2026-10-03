# WorkTrail Excel Planner

Compare local working hours across seven cities, including availability margins, holidays and personal exceptions.

This folder contains the standalone Excel prototype, its six CSV datasets and its authoring scripts. It is not integrated with the WorkTrail application or its build.

## Start

Windows, desktop Microsoft Excel and PowerShell 7 are required for the existing launcher. From this folder, run:

```powershell
pwsh -NoProfile -File ./01_support_scripts/calendars-2026-09-24/Open-Planner.ps1
```

The launcher creates or reopens an ignored working copy under `.local/`, sets its CSV folder and opens Excel. It does not refresh data automatically. The versioned workbook remains a clean template, with no API key or machine-specific data-folder path.

Set your cities and hours in **Parameters**. Use **Exceptions** for leave and personal overrides. **Data > Refresh All** reads the CSV files; an optional OpenWeather key in the local copy enables current-weather requests. Never commit a workbook containing a key.

You can also open the template directly to inspect its cached data. Its data-folder input is intentionally blank; use the launcher for a working copy with refresh configured.

## Contents

- `Planner_Availability_A4.xlsx`: current template, including weather, moon and country Unicode symbols.
- `Calendar data/`: cities, UTC/DST periods, holidays, saints, moon phases and a dated weather snapshot.
- `01_support_scripts/`: original builders, data fetchers, Power Query source, migration and verification scripts, provenance and licenses.

Planner is configured for one A4 landscape page. Extended uses two pages. Printer settings can alter the exported paper size. Emoji appearance depends on Excel and its fonts.

## Scope and limits

Existing coverage is unchanged: national holidays for eleven countries plus England and Wales, an incomplete informational saint selection, and current-weather snapshots rather than forecasts. Vietnam and Belarus remain unpopulated. See the [data guide](01_support_scripts/Data_Guide.md).

The dated script folders preserve development history. Some scripts are one-time migrations or need intermediate files from earlier steps. Do not run them over the current template as a rebuild command; no complete clean-checkout rebuild has been verified. See the [script notes](01_support_scripts/README.md).

The separate Obsidian copy is preserved. This is a one-time import, not automatic synchronization. Private implementation plans, API keys, reference workbooks, backups, installed dependencies and temporary QA output are not included.

## License

Project-authored source and documentation follow the repository's [MIT license](../LICENSE). Dataset provenance and third-party notices remain in [01_support_scripts](01_support_scripts/README.md#data-and-licenses). MIT does not replace third-party data terms.

Built by Umberto Giacobbi, with help from contributors.
