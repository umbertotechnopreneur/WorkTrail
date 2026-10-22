# WorkTrail privacy

The current app provides desktop clocks and astronomy widgets. It does not record keyboard or mouse activity, inspect the active application, capture screenshots, analyze your work with AI, build timesheets or search activity history.

## Local preferences and existing data

WorkTrail saves the confirmed home city, first-run completion flag, selected cities, language, theme, window placement, open widget state and startup preferences in its local settings file. Welcome suggests a city from the Windows time zone and asks you to confirm it. It does not request precise location or access the network to make that suggestion. The city catalog works offline. The random installation identifier scopes the shared runtime. It is not an account.

Widget startup does not open or alter activity databases, screenshot folders or AI results left by earlier versions. Those files remain on your PC. This change does not erase them or configure an older installed copy that may still be running.

## Network sources

The world-city catalog and bundled holiday/saint catalog are local. Optional current weather uses OpenWeather; satellite and space-weather views can request public external data. The requested cities or astronomical coordinates are used for those features, without sending screenshots or activity history. A failed remote source is reported separately from the local clock or astronomy calculation.

Weather credentials use the Windows user/process environment variable `WORKTRAIL_OPENWEATHER_API_KEY`. They are not saved in the preference file, command arguments or logs.

## Logs and optional error reports

The app keeps local diagnostic logs for startup, persistence, interop and unexpected failures. Log-sharing actions create a bounded, redacted copy only when you request them. Optional Sentry reporting is configured through `WORKTRAIL_SENTRY_DSN`; default personal-information collection is disabled and events pass through the app's sanitizer. No configured DSN means that reporting is disabled.

The public [privacy policy](https://umbertogiacobbi.biz/privacy/) and [terms](https://umbertogiacobbi.biz/terms/) remain separate from these implementation notes. See [third-party notices](../THIRD_PARTY_NOTICES.md) for dependency and data-source attribution.
