# Changelog

This changelog records notable changes to WorkTrail for users and contributors.
Entries under `Unreleased` describe implemented changes that have not yet been released.

The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and tagged releases will follow [Semantic Versioning](https://semver.org/) once
the first public preview defines what later versions need to stay compatible with.

## [Unreleased]

### Added

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

- About uses a compact layout, a trail banner, updated localized copy, the full package version, and transparent VibeWare branding with project and manifesto links.
- Premium restrictions across exports, labels, clocks, schedules, data transfer, and retention share a localized upgrade prompt with a Microsoft Store action.
- The tray menu provides notification and tracking toggles, VIP capture, clocks, astronomical views, Settings, About, and a command to reveal all already-open WorkTrail windows.
- In-app toasts use compact severity-colored surfaces with icons and no countdown progress bar.
- Excel exports share a branded A4 introduction, worksheet navigation, readable supporting tables, and a full-text appendix. Timesheets combine projects and apps in each daily or weekly row while retaining dated measured details.
- Export screens use compact option grids, explain the four report tabs, and provide a collapsible Excel preview card with artwork for light and dark themes.
- AI summaries remain visible in an editable text area, and ordinary file-format or column changes preserve the draft.
- Excel preview generation now shows progress and the completed file path below the card, with separate commands to open the workbook or copy its absolute path.
- Debug builds preserve diagnostic data by disabling scheduled retention; manual cleanup remains available.
- Data retention now uses calendar months: up to one month for Free and three months for Premium, with monthly cleanup scheduled from the installation date after the app is ready.
- Screenshot storage, local OCR, and saved hardware settings are grouped with data retention.
- Hardware summaries focus on CPU, GPU, physical memory, disks, and network. Complete readings remain available in expandable device groups.
- The live sensor monitor updates controls on the visible page while continuing to collect samples for every device.
- Settings navigation uses consistent hover behavior, and the main-window position label is translated consistently.
- The privacy guide explains what happens when you reset the app and delete its saved data.

### Fixed

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
- Export windows keep their title bar and close button visible; the labels editor opens centered on its owner.

### Removed

- Taskbar-widget controls from Settings.

### Security

- Automated workflows use only the permissions they need and pin GitHub Actions to specific commits.

Release history will begin with the first tagged public preview.
