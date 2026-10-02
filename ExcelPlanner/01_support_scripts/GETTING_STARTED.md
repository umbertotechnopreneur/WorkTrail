# Availability Planner

This is the versioned copy of the prototype. Run `calendars-2026-09-24/Open-Planner.ps1` to open an ignored `.local/` working copy and configure the CSV folder. The template in the parent folder contains no keys or personal paths.

## In one minute

1. In **Parameters**, set the date, time, margins and the seven columns. For weekly rules, use `1 = rest` and `0 = activity`.
2. In **Exceptions**, enter a column number, inclusive local dates, `Rest` or `Activity`, and `Active = 1`.
3. Use **Data → Refresh All** to reload the data. A blank weather key reads `Weather.csv`; a populated key calls OpenWeather. There is no refresh on open.
4. Print only **Planner** or **Extended**, not the entire workbook. A PDF is a snapshot and does not update itself.

The key is saved as plain text in the workbook. Do not share a copy that contains it. Weather is current weather, not a forecast for the planner date; each city shows the UTC observation time.

## Included

- **Cover** with the A4 quick guide.
- **Parameters** for inputs.
- **Planner** on one landscape A4 page.
- **Extended** on two A4 pages.
- Seven independent city and time-zone columns with local hours, weekly rest and calendar rules.
- **Exceptions** for leave, closures and personal make-up days.
- 206 cities and 670 DST periods for 2026–2028.
- National calendar data for mainland China, India, South Korea, Japan, Bangladesh, Italy, France, Norway, Sweden, the United States and Canada.
- Informational saints and USNO moon phases.
- OpenWeather current-weather support for the selected cities.

## Limits

Regional, company and automatically applied patron calendars are not included. USA and Canada use a federal base. Vietnam and Belarus are prepared but not loaded. The saint list is incomplete. Estimated dates and future make-up days can change; check `Status` in the imported holiday table.

The dated script folders preserve development history. Some scripts are one-time migrations or require intermediate files. They are not a clean rebuild pipeline and should not be run over the current template without review.

## Keep together

Keep the workbook and `Calendar data` with its six CSV files. The launcher updates Parameters B25 in the local copy. Scripts, sources and licenses are in `01_support_scripts`. Private plans, verification PDFs and backups remain outside this folder.
