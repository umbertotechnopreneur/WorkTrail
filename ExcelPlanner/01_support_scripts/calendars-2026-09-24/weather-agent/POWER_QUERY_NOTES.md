# Weather query integration

Import `Weather.query.pq` as the Weather query. Required workbook names:

- `WeatherApiKey`: one cell, intended as `Parametri!B32`, initially blank.
- `DataFolder`: one cell containing the local CSV folder.
- `ZoneKeys`: one column named `Column1`, at most seven rows. Blank slots are ignored;
  repeated selected codes produce one result each. Changes take effect on refresh.

With a blank or whitespace-only key, only local `Weather.csv` is evaluated. Its nine
headers must match exactly, Code must be unique/nonblank, numeric/date conversion
must succeed, and status/source must be present. UTC timestamps are returned as
nullable datetime values (without a timezone suffix); numeric culture is en-US.

With a key, the query reads the seven-column `Cities.csv` directly from DataFolder.
The staged export must be deployed by the parent workflow before this mode is used.
It validates unique catalog codes, one matching catalog row per selection, coordinates
and country before requesting current weather. Output preserves the nine Meteo headers
with Status `api` and Source `OpenWeather API`. No data is written back to CSV.
All returned errors are generic Error.Record values; no raw error details, keyed URLs,
credentials or HTTP metadata are included in the returned table.

## Native Power Query caveats

- This mode passes the user's cell value via Web.Contents Query/appid. Use Anonymous
  authentication for the static OpenWeather HTTPS data source in Excel; do not also
  configure a second API-key credential. The alternative Microsoft ApiKeyName design
  stores the key in the Web API credential instead, but is not the requested cell flow.
- A worksheet cell is not a secret vault: if the user enters a key and saves the
  workbook, that value is saved with it. Excel/Power Query can retain source metadata,
  caches and diagnostics internally. This M source does not log or expose the key in
  results, but cannot guarantee that the host never caches an authenticated request.
  The prepared workbook must retain the blank field; the supplied demo key is never
  copied into it. The external fetcher plus CSV remains the default path.
- Combining workbook input, a local file and a web source can trigger the privacy
  firewall. Assign truthful privacy levels; do not mark the key-bearing workbook
  Public or disable privacy checks as a blanket workaround. If the host blocks this
  intentional transfer, use CSV mode or redesign around credential storage.
- Microsoft documents that 401/403 can cause credential prompts and cannot be manually
  handled by ordinary M queries. Generic try/error handling does not suppress all
  host-level authentication prompts or diagnostics.
- ManualStatusHandling uses explicit supported HTTP errors (400/404/408/429/500/502/503/504/509),
  excluding 401/403. The broad 300..599 list was rejected by native Excel and replaced.
  Row-level errors are explicitly rejected to avoid Excel loading blank result rows.
  The query defines one logical fetch per
  unique code per evaluation, with no explicit retry. Excel previews, dependency
  evaluations, refreshes and host caching can still affect physical request counts;
  this query is not an enforceable account-wide seven-request quota.

Validation: the 206-row city export is checked against both original CSV and SQLite.
The parent verified both the empty-key CSV branch and the real native API branch:
seven cities, seven nonempty results with Status `api`. The demo key was used only
in unsaved local workbook changes, discarded after verification. The delivered
workbook retains the blank key field and CSV snapshot. See
`../verifiche/weather-api-native.json` and `../verifiche/native-verification.json`.
The owner explicitly authorized Fast Combine for this prototype; no global
privacy setting was changed.

Official references:

- https://learn.microsoft.com/en-us/powerquery-m/web-contents
- https://learn.microsoft.com/en-us/power-query/handling-status-codes
- https://learn.microsoft.com/en-us/power-query/privacy-levels
- https://openweathermap.org/api/current
