# Changelog

This changelog records notable implemented changes that have not yet been released.
The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).
Release history will begin with the first explicitly authorized tagged public preview.
Historical implementation milestones remain in [.github/tasks/archive.md](.github/tasks/archive.md).

## [Unreleased]

### Added

- First-run home-city setup with a reusable offline city selector and a suggestion based on the Windows time zone.
- Stable UI automation selectors and localized accessible names for the launcher and weather credential editor.
- SPDX MIT headers, source-header verification, artwork provenance records and contributor guidance.
- Issue forms, a pull-request template and dependency update automation.

### Changed

- Main is a compact launcher for independent world clocks, the astronomical calendar, local sky, Moon, day/night map and globe. Essential preferences, Windows startup and tray commands remain.
- Closing Main offers an Acrylic dialog to keep widgets running in the notification area, exit the app or cancel. The tray can reopen the launcher, reveal existing windows or exit the app.
- The launcher pairs an Acrylic city backdrop with a left-aligned reference-city heading, responsive watercolor tiles, larger titles and short localized descriptions. Artwork fades between resting and hover saturation over 300 milliseconds.
- Launcher tiles grow vertically with the viewport, clip at the scroll boundary and replace their small icons with larger watercolor artwork. Footer actions fill the available width.
- First-party C# classes and methods have clearer documentation and responsibility regions; dense calculations and control flow use named steps and more readable spacing. Active PowerShell helpers include function documentation and expanded resource cleanup.
- Widget startup reads preferences and bundled calendar data without opening existing activity history. Saved cities, window placement and the separate ExcelPlanner prototype are preserved.
- Application services and IPC expose the retained widget operations only. The local runtime protocol is version 8.
- Repository task records and current product documentation describe the reduced app.

### Fixed

- Launcher destinations reuse the former World Time panel with visible localized captions, accent icons and shared window chrome. Empty text-button content now receives its visible translation, including the weather credential editor.
- Restored dynamic astronomical translations and the clock week label so valid city data can render in every shipped language. Widget rendering failures are also written to the local diagnostic log.
- Saved taskbar visibility is applied when an astronomy window becomes visible; menu checkmarks follow the committed preference.
- Window placement adapts saved geometry to current DPI and keeps disconnected-monitor positions visible.
- The launcher opens on the display containing the pointer. The lunar widget uses a cropped surface image and a stable title-bar inset to keep the Moon visible.
- Old saved window flags cannot prevent the current widget workspace from opening.

### Removed

- Activity tracking, input hooks, screenshots and gallery, AI connections, timesheets and reports, history search, activity calendar, CLI and their supporting projects, settings and UI.
- The tracking taskbar widget, hardware sensor collection, CPU/GPU readings, LibreHardwareMonitor, bundled PawnIO installation and OCR processing.
- Copilot instructions and repository prompts; development rules live in AGENTS.md.

### Security

- Workflows use scoped permissions and GitHub Actions pinned to specific commits.
