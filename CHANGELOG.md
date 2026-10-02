# Changelog

This changelog records notable changes to WorkTrail for users and contributors.
Entries under `Unreleased` describe implemented changes that have not yet been released.

The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and tagged releases will follow [Semantic Versioning](https://semver.org/) once
the first public preview defines what later versions need to stay compatible with.

## [Unreleased]

### Added

- Windows screenshot notifications with image previews and a saved opt-out available in Settings or in the notification itself.
- Stable UI automation selectors, localized accessible names, and a conversion register organized by screen.
- Excel timesheets with daily or morning/afternoon rows, monthly worksheets, measured durations, and optional summaries processed through OpenAI Batch.
- Temporary Excel previews limited to ten records per worksheet.
- In-app help explaining the available AI-summary grouping options.
- Concise SPDX MIT headers and automated source-header verification.
- Records of where the project's AI-generated artwork came from, kept separate from third-party media.
- Forms for reporting bugs, requesting features, and suggesting documentation fixes, plus a pull-request template.
- A guide to how the app is organized for contributors.
- Dependabot updates for NuGet, npm, and GitHub Actions dependencies.

### Changed

- Excel preview generation now shows progress and the completed file path below the card, with separate commands to open the workbook or copy its absolute path.
- Debug builds preserve diagnostic data by disabling scheduled retention; manual cleanup remains available.
- Data retention now uses calendar months: up to one month for Free and three months for Premium, with monthly cleanup scheduled from the installation date after the app is ready.
- Screenshot storage, local OCR, and saved hardware settings are grouped with data retention.
- Hardware summaries focus on CPU, GPU, physical memory, disks, and network. Complete readings remain available in expandable device groups.
- The live sensor monitor updates controls on the visible page while continuing to collect samples for every device.
- Settings navigation uses consistent hover behavior, and the main-window position label is translated consistently.
- The privacy guide explains what happens when you reset the app and delete its saved data.

### Fixed

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
