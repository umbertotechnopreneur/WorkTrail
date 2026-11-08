# Changelog

This changelog records notable implemented changes that have not yet been released.
The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).
Release history will begin with the first explicitly authorized tagged public preview.

## [Unreleased]

### Added

- Thirty additional national calendars, updated rule-based data from holidays 0.107, and a live calendar count in About WorkTrail.

- Published Hong Kong and Indonesia holiday calendars for 2026 and 2027, with per-country coverage so unpublished periods remain unverified.

- OpenWeather key configuration and verification, plus the current-weather toggle, directly in launcher settings through the existing credential editor.
- Direct launcher access to the availability planner, native language names with color flags, responsive settings with explanations, and confirmation before explicit application exit.
- Independent availability planner with reusable city headers and virtualized timelines, existing skyline and Moon artwork, current OpenWeather observations, configurable working hours and personal exceptions, and source-backed holiday coverage indicators.
- Direct A4 landscape PDF export of the planner interval, with vector text and timetable geometry, embedded skyline illustrations and repeated headers across pages.
- First-run home-city setup with a reusable offline city selector and a suggestion based on the Windows time zone.
- Stable UI automation selectors and localized accessible names for the launcher and weather credential editor.
- SPDX MIT headers, source-header verification, artwork provenance records and contributor guidance.
- Issue forms, a pull-request template and dependency update automation.

### Changed

- World Timesheet marks the current time slot in red and provides a command to jump to it.
- Timesheet city columns resize together from any header edge and can fill the available width. Reference-city midnight separators continue through the timeline and vector PDF.
- World Clock city settings open in a separate window from either World Clock or World Timesheet. The city picker shows a searchable list.
- World Clock can group cities that share the displayed UTC offset into a single clock column. Expand a group in settings to manage its cities individually.
- World Timesheet can collapse city headers and starts with empty cells. The UTC column, availability colors, hover details and selected interval summary remain visible.
- World Timesheet and the city settings window use Acrylic. WorkTrail windows display the app icon.
- Refresh .NET, Windows App SDK, rendering and test dependencies used by the source build.
- Widget and tray navigation share all seven views, consistent commands and localized calendar-country selection with search. Window menus keep taskbar visibility, opacity and always-on-top preferences independent and persistent for each widget.
- Timesheet duration and time-step preferences are saved, and both calendar city selectors retain the chosen city.

- Timesheet text is larger, local weekends, saved rest days and holidays are marked in red, and its menu adds icons, a saved Monday/Sunday week preference and direct World Clock configuration.

- World Timesheet applies reference changes immediately, centers day navigation and groups view settings and PDF export in an overflow menu. Its dedicated watercolor tile and weather link lead to the timesheet and global weather settings.

- Refocused project documentation on time-zone availability planning and desktop widgets; GitHub distribution uses portable ZIPs, separately from the Store channel.
- Main is a compact launcher for independent world clocks, the astronomical calendar, local sky, Moon, day/night map and globe. Essential preferences, Windows startup and tray commands remain.
- Closing Main offers an Acrylic dialog to keep widgets running in the notification area, exit the app or cancel. The tray can reopen the launcher, reveal existing windows or exit the app.
- The launcher pairs an Acrylic city backdrop with a left-aligned reference-city heading, responsive watercolor tiles, larger titles and short localized descriptions. Artwork fades between resting and hover saturation over 300 milliseconds.
- Launcher tiles grow vertically with the viewport, clip at the scroll boundary and replace their small icons with larger watercolor artwork. Footer actions fill the available width.
- First-party C# classes and methods have clearer documentation and responsibility regions; dense calculations and control flow use named steps and more readable spacing. Active PowerShell helpers include function documentation and expanded resource cleanup.
- Widget startup reads preferences and bundled calendar data without opening existing activity history. Saved cities, window placement and the separate ExcelPlanner prototype are preserved.
- Application services and IPC expose the retained widget operations only. The local runtime protocol is version 9.

### Fixed

- Committed settings refresh the launcher and every open widget, including weather changes made from another view.

- Weather windows share the saved user key ahead of inherited process values and identify a configured key separately from unavailable observations.

- Weather verification shows progress and persistent success or error feedback. Provider outcomes use valid translations, and pasted keys tolerate surrounding whitespace.

- Availability planner headings and cells share the same horizontal origin; every slot shows its date, with status symbols separate from local times and clearer holiday-coverage explanations in every language.
- Launcher destinations reuse the former World Time panel with visible localized captions, accent icons and shared window chrome. Empty text-button content now receives its visible translation, including the weather credential editor.
- Restored dynamic astronomical translations and the clock week label so valid city data can render in every shipped language. Widget rendering failures are also written to the local diagnostic log.
- Saved taskbar visibility is applied when an astronomy window becomes visible; menu checkmarks follow the committed preference.
- Window placement adapts saved geometry to current DPI and keeps disconnected-monitor positions visible.
- The launcher opens on the display containing the pointer. The lunar widget uses a cropped surface image and a stable title-bar inset to keep the Moon visible.
- Old saved window flags cannot prevent the current widget workspace from opening.

### Removed

- Feature tiers and upgrade prompts. World Clock supports up to 12 saved clocks.
- Activity tracking, input hooks, screenshots and gallery, AI connections, timesheets and reports, history search, activity calendar, CLI and their supporting projects, settings and UI.
- The tracking taskbar widget, hardware sensor collection, CPU/GPU readings, LibreHardwareMonitor, bundled PawnIO installation and OCR processing.
- Copilot instructions and repository prompts; development rules live in AGENTS.md.

### Security

- Workflows use scoped permissions and GitHub Actions pinned to specific commits.
