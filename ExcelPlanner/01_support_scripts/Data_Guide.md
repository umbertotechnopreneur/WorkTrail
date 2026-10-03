# Planner data

The six CSV files use UTF-8 with BOM, `;` separators, dot decimals and ISO dates. Do not rename their headers. Fields containing `;` must be enclosed in double quotes.

`Parameters B25` stores the folder path. **Data → Refresh All** reloads the tables; it does not continuously watch the files. The last cached copies remain in the workbook after a failed refresh. Check **Queries and Connections** and the timestamps.

| File | Schema | Content |
| --- | --- | --- |
| `Cities.csv` | `Code;City;WindowsId;IanaId;Latitude;Longitude;CountryCode` | 206 cities with unique codes, coordinates and ISO country codes |
| `DST_periods.csv` | `Code;FromUtc;UntilUtc;OffsetHours` | 670 UTC intervals for 2026–2028 |
| `Calendars.csv` | `Calendar;Date;Name;Active;Source;Kind;Status;Version;Updated` | 542 holidays and make-up days for 2026–2028 |
| `Saints.csv` | `Month;Day;Name;Source` | 211 entries across 191 dates |
| `Moon_phases.csv` | `Utc;Phase;Between;Source` | 247 USNO lunar events for 2025–2029 |
| `Weather.csv` | `Code;ObservedUtc;FetchedUtc;TemperatureC;FeelsLikeC;Description;WindMs;Status;Source` | Latest OpenWeather observations, initially for seven cities |

## Cities and daylight saving time

The canonical source is the read-only `WorkTrail/Assets/WorldClocks/world-clocks.sqlite3` catalog. The code matches `city.id` exactly. The displayed city name and selected time zone are independent; changing the name does not change the time zone.

City data comes from [GeoNames](https://www.geonames.org/) under [CC BY 4.0](https://creativecommons.org/licenses/by/4.0/), as recorded in the database. The export preserves the source fields and adds latitude, longitude and country. Database SHA-256: `43549bdecff714e9a2b4995b5e0337a6046f440a91485a1035b2de2a7d242670`.

DST intervals are a snapshot of Windows rules from 24 September 2026. The start is inclusive and the end is exclusive. The offset already includes daylight saving time; India is `5.5`. Later legal changes require a new export.

## Calendars and precedence

| Information | Effect |
| --- | --- |
| Saint or religious observance | Information only |
| Applicable national holiday or patron date | Local day is non-working |
| Weekend or Friday rest day | Column's weekly rule |
| Leave, company closure or personal make-up day | **Exceptions**, with priority |

The effective order is **personal exception → holiday → national make-up day → weekly rest → hours**. Hours and margins still apply when a make-up day reopens a date. Two overlapping active exceptions on the same column and date produce a warning rather than an arbitrary winner.

`Active=0` disables a row. With `Active=1`, `Kind=HOLIDAY` closes the day and `Kind=WORKDAY` cancels the weekly rest rule, but not an explicit holiday or personal exception. `Status`, `Version`, `Updated` and `Source` describe snapshot provenance and confidence.

The national base derives from [python-holidays 0.105](https://github.com/vacanza/holidays/tree/v0.105), under the MIT license. It includes CN, IN, KR, JP, BD, IT, FR, NO, SE, US and CA for 2026–2028. It is not individually verified against every official decree. The USA and Canada data is federal. India is a national library base. Sweden excludes generic Sundays. Chinese 2027–2028 make-up days are not final. Vietnam and Belarus remain without loaded data.

In **Calendars A6:F25**, `Data=1` means that data is present, not that it is certified. The manual patron-date table below the imported data remains separate from the CSV and survives refreshes. Add a local patron date there. A new code such as `IT-ROMA` does not inherit `IT` dates automatically.

**Exceptions** use columns 1–7 and inclusive local From/To dates. Use `Rest` or `Activity` and set `Active=1`. The table has 100 prepared rows and can be extended as an Excel table. Weekly Friday rest belongs in **Parameters**, not in hundreds of holiday rows.

## Saints and moon

Saints are fixed-name observances: 191 dates, 211 entries, with 175 recurring dates missing. The source is [LiturgicalCalendarAPI](https://github.com/Liturgical-Calendar/LiturgicalCalendarAPI); its revision and Apache-2.0 notice are preserved in `calendars-2026-09-24/saints-agent`. The list is informational, not a complete martyrology or annual liturgical calendar. Missing rows mean that the information was not loaded.

Moon data comes from [USNO](https://aa.usno.navy.mil/data/MoonPhases) and its [API](https://aa.usno.navy.mil/data/api). `Utc` is the primary phase instant and `Between` is the following interval. The planner shows the phase at view start and the next UTC event. It does not calculate illumination, rise/set or orientation.

## OpenWeather

With a blank **Parameters B32**, Power Query reads `Weather.csv`. The external `weather-agent/fetch_weather.py` script can update it from an environment key or an explicit key file. With a key in B32, the `Weather` query reads distinct selected city codes and requests current weather in Celsius. It does not save API responses back to the CSV and does not use the planner date as a forecast date.

The key is stored as plain text if the workbook is saved. Do not share that copy. `ObservedUtc` is the observation time and `FetchedUtc` is the download time. `stale:*` means previous data was retained after an error; `aged_observation` means the observation is at least 30 minutes old.

## Editing data

- Use a text editor or **Data → From Text/CSV**. A double-click can convert codes and dates.
- Do not edit imported tables in `Da_file`, `Fusi` or `Info`; refresh replaces them. Use `Calendars` and `Exceptions` for personal data.
- Keep the workbook and `Calendar data` together. Update Parameters B25 if they move.
- Scripts, tests, licenses, sources and originals are not runtime dependencies of Excel.
