# Meteo staging protocol

This folder is staging only. Nothing here installs or updates the live calendar.
The standalone Python 3 script uses only the standard library, including urllib.
All generated files remain beside the script: `Weather.csv` and `weather-report.json`.

## Run

Pass `--catalog <canonical-world-clocks.sqlite3>`. Set `OPENWEATHER_API_KEY` in the
calling process environment, or pass `--key-file <existing-key-file>` explicitly.
Environment takes precedence. The key file must contain only the key, optionally
surrounded by whitespace or a UTF-8 BOM. The script never edits, moves or deletes it.
Do not pass the actual key on the command line or save it in a workbook, CSV,
Power Query definition, script, report, transcript or URL.

Use `--check-only` before fetching to validate coordinates and cache without reading
the key or making requests. Use `--verify-output` after fetching to inspect the
staged CSV/report and scan them for the selected key without requests or writes.
Exit codes: 0 success; 1 completed with per-city fetch failures; 2 local/configuration
failure; 130 interrupted. Exceptions and HTTP response bodies are never logged.

Exactly these catalog IDs are supported, in order: hanoi, mumbai, minsk, rome,
london, new-york, los-angeles. Each ID and exact city label must resolve uniquely
in the canonical SQLite `city` table; expected country codes must also agree.
The catalog is read-only. There is no geocoding request or coordinate fallback.

Each invocation makes at most seven OpenWeather **current-weather** requests, one
per expired/missing city. No forecasts, network geocoding, redirects or retries.
The authenticated URL is constructed only in memory for the HTTPS request.
Units are metric; descriptions are Italian. The account must enable this endpoint.

## CSV contract

UTF-8 with BOM, semicolon delimiter, CRLF records, dot decimal separator:

```text
Code;ObservedUtc;FetchedUtc;TemperatureC;FeelsLikeC;Description;WindMs;Status;Source
```

`Code` joins the calendar's city selector. `ObservedUtc` is OpenWeather's observation
timestamp; `FetchedUtc` is the last successful download timestamp. Both are UTC
strings `YYYY-MM-DDTHH:mm:ss`, deliberately without Z, for the receiving query's
datetime cast. Preserve the UTC meaning when displaying local time. Numeric fields
use degrees Celsius and metres/second. Missing weather values are empty, never zero.
The receiving query should use an explicit dot-decimal culture for numeric parsing.
`Source` is `OpenWeather`; attribution belongs beside the displayed weather.

`Weather.csv` also acts as the cache, keyed by Code. A successful fetch stays reusable
for 1800 seconds from FetchedUtc. There is no automatic scheduler.

| Status | Meaning |
| --- | --- |
| fetched | Download succeeded this run. |
| cached | Last successful fetch is less than 30 minutes old; no call made. |
| stale:ERROR_CODE | Refresh failed; previous values and both timestamps retained. |
| unavailable:ERROR_CODE | Refresh failed and there is no successful cached observation. |
| any status ending in `\|aged_observation` | Observation is at least 30 minutes old, even if just fetched. |

Error codes are controlled values such as HTTP_401, HTTP_429, TIMEOUT, NETWORK_ERROR,
RESPONSE_COUNTRY_MISMATCH and INVALID_RESPONSE. They contain no provider message,
key, URL or response body. The JSON report contains per-city coordinates, timestamps,
age, status, error and total request/error counts. Files are replaced atomically
individually; `--verify-output` detects a CSV/report mismatch after an interrupted run.
Statuses describe the most recent script run: the receiving display must also compute
age from UTC timestamps so that an unchanged CSV does not appear perpetually current.

## Sources and future integration

- Current endpoint and parameters: https://openweathermap.org/api/current
- Current Weather and 5-day/3-hour forecasts are in the free product list;
  actual access and charges depend on endpoint, account/key subscription and usage:
  https://openweathermap.org/price . This script uses current weather only.
- OpenWeather currently describes self-service licensing as ODbL, with visible
  attribution; external distribution of an adapted weather database can bring
  share-alike duties: https://openweathermap.org/full-price . The attribution FAQ
  requests visible “Weather data provided by OpenWeather”, a website link and logo:
  https://docs.openweather.co.uk/faq . Apply the account's actual licence.
- Country flags: https://github.com/lipis/flag-icons is MIT; preserve its copyright
  and licence notice when redistributing assets:
  https://github.com/lipis/flag-icons/blob/main/LICENSE . No flag files are downloaded here.
- For future offline flags, prefer embedded PNG pictures in cells on a supported
  Excel version, selected by country-code lookup after verifying that version's
  formula support. Floating pictures are separate objects; a simple lookup does
  not automatically select them. Linked-picture/Camera techniques need separate
  compatibility checks. `IMAGE()` requires an HTTPS source, not a local file path:
  https://support.microsoft.com/en-us/excel/insert-picture-in-cell-in-excel and
  https://support.microsoft.com/en-us/excel/functions/image-function .
- Moon glyphs are display symbols, not a lunar calculation. Unicode defines
  U+1F311 through U+1F318; choose the symbol from separately calculated phase data:
  https://unicode.org/charts/nameslist/n_1F300.html . These current-weather rows do
  not include a lunar phase.
