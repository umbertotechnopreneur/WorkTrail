# Changelog

This changelog records notable changes to WorkTrail for users and contributors.
Entries under `Unreleased` describe implemented changes that have not yet been released.

The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and tagged releases will follow [Semantic Versioning](https://semver.org/) once
the first public preview defines what later versions need to stay compatible with.

## [Unreleased]

### Added

- Configurable automatic-capture stabilization for rapid window switching, with bounded quiet retries and no added wait for manual captures.
- Configurable near-duplicate filtering for automatic screenshots, with conservative visual comparison, a Spotify preset and periodic refresh; manual captures remain unfiltered.
- Context-specific screenshot micro-prompts, process-scoped title hints, selected secondary-window crops and configurable inactivity timeouts, including Teams and Zoom presets. Browser titles remain heuristic hints, not verified domains; low-attention analysis reads titles only.
- Internal screenshot capture rules with active/secondary window crops, monitor identities, separate AI/gallery destinations, application attention presets and an analysis blacklist.
- Codex CLI screenshot descriptions using ChatGPT sign-in, with installation detection, setup guidance and actionable connection errors.
- Inline history settings with a configurable storage warning (1 GB by default) and confirmed deletion of an inclusive date range.
- Process memory monitoring with a 1 GiB limit, a native Windows warning before restart, logged recovery events, and preservation of the tracking state.
- VibeWare symbol in CLI output, with a tracked source asset.
- VIP snapshots with a cancellable countdown, optional notes, a featured gallery strip, gold crown badges, and calendar indicators.
- Windows screenshot notifications with image previews and a saved opt-out available in Settings or in the notification itself.
- Stable UI automation selectors, localized accessible names, and a conversion register organized by screen.
- Excel timesheets with daily, morning/afternoon or weekly rows, measured monthly totals, and optional summaries processed through OpenAI Batch.
- Three Excel report themes selectable in the export wizard and editable through Excel's native themes, including for saved batches.
- Temporary Excel previews limited to ten records per worksheet.
- In-app help explaining the available AI-summary grouping options.
- Concise SPDX MIT headers and automated source-header verification.
- Records of where the project's AI-generated artwork came from, kept separate from third-party media.
- Forms for reporting bugs, requesting features, and suggesting documentation fixes, plus a pull-request template.
- A guide to how the app is organized for contributors.
- Dependabot updates for NuGet, npm, and GitHub Actions dependencies.

### Changed

- Screenshot descriptions use Balanced or Complete profiles, inspect visible content by monitor and omit application-imposed response caps. Manual captures request Complete analysis with high attention without changing the saved preference or privacy exclusions.
- Activating any WorkTrail window brings the other open app windows forward while keeping focus on the selected window.
- AI settings offer CLI or API connections through Codex CLI or an OpenAI API key; switching connections requires explicit AI activation.
- Guided setup offers the same CLI/API choice, and history settings keep storage controls and deletion dates on compact rows.
- Free history is retained for one calendar month; Premium can retain one to twelve months. Automatic cleanup checks the policy daily while the app is running.
- The main player shows a VibeWare footer with an accessible link to the manifesto and accounts for the footer when fitting its contents.
- The world-clock reference header, window title, tooltip, and accessible name show the reference date's ISO week number and week-year.
- Moving and resizing primary work surfaces show transient alignment guides and snap eligible edges using DPI-scaled ten/twenty logical-pixel snap/preview distances; dialogs, About and temporary windows do not participate.
- About uses a compact layout, a trail banner, updated localized copy, the full package version, and transparent VibeWare branding with project and manifesto links.
- Premium restrictions across exports, labels, clocks, schedules, data transfer, and retention share a localized upgrade prompt with a Microsoft Store action.
- The tray menu provides notification and tracking toggles, VIP capture, clocks, astronomical views, Settings, About, and a command to reveal all already-open WorkTrail windows.
- In-app toasts use compact severity-colored surfaces with icons and no countdown progress bar.
- Excel exports share a branded A4 introduction, worksheet navigation, readable supporting tables, and a full-text appendix. Timesheets combine projects and apps in each daily or weekly row while retaining dated measured details.
- Export screens use compact option grids, explain the four report tabs, and provide a collapsible Excel preview card with artwork for light and dark themes.
- AI summaries remain visible in an editable text area, and ordinary file-format or column changes preserve the draft.
- Excel preview generation now shows progress and the completed file path below the card, with separate commands to open the workbook or copy its absolute path.
- Debug builds preserve diagnostic data by disabling scheduled retention; manual cleanup remains available.
- Activity scores use input and active time only. Screenshot monitor identity and the visual gallery are preserved.
- Settings navigation uses consistent hover behavior, and the main-window position label is translated consistently.
- The privacy guide explains what happens when you reset the app and delete its saved data.

### Fixed

- Windows retain their monitor, position and dimensions across restarts, adapting saved geometry to current display DPI and keeping disconnected-monitor placements visible.
- Each snapshot shows one Windows notification using the active-window or focused-monitor image, including multi-monitor captures.
- Icon-only Windows controls declare accessible names matching their tooltips, the Excel theme selector shows its localized header, and the export window keeps a single Premium badge in its title bar.
- Repeated screenshot capture failures show one notification until capture succeeds, while retries and local error logging continue.
- Timesheet amounts recalculate from the editable hourly rate; missing or invalid rates leave amounts blank instead of implying zero charges.
- Switching between ordinary export tabs preserves the generated preview, while changing report type or source data invalidates it.
- Automatic batch metadata refreshes no longer disable the export wizard or overwrite a newer job selection.
- Export grouping help uses its localized title, and saved batch lists no longer report an empty archive when no job is selected.
- Cleanup progress windows keep their title bar and close button visible; Close and Cancel stop remaining work before dismissing the dialog.
- Interrupted screenshot publication and deletion recovery no longer prevent startup, and incomplete capture provenance is reported without blocking the rest of a gallery day.
- Retention cleanup reuses a single screenshot inventory and supports cancellation during SQLite deletion.
- Batch report jobs retain their recovery state independently of the export window, and uncertain submissions are reconciled before another paid submission is attempted.
- Export windows keep their title bar and close button visible; the labels editor restores its saved placement and centers on its owner on first opening.

### Removed

- Monthly AI spending, price-list downloads, pricing dialogs and cost estimates in settings, storage and exports. Request and token counts remain available.
- Hardware sensor collection, CPU/GPU readings, the sensor window, LibreHardwareMonitor and bundled PawnIO installation.
- OCR capture processing, refinement calls, stored OCR text, controls, search fields and export columns. AI visual descriptions remain available through CLI or API.

- Taskbar-widget controls from Settings.

### Security

- Automated workflows use only the permissions they need and pin GitHub Actions to specific commits.

Release history will begin with the first tagged public preview.
