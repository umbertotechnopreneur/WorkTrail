# Task Archive

This is a compact historical index of durable WorkTrail milestones. It intentionally omits command transcripts, temporary artifact paths, repeated test counts, intermediate package versions, and superseded pre-rebrand naming. Older entries may predate the WorkTrail name.

## 2026-10-08

### Compact About layout and current branding

- Replaced obsolete About artwork with an original trail illustration and a native
  WorkTrail title. Reduced the default window and banner height, updated the minimum
  bounds, adopted the compact size after placement restoration, and retained scrolling
  and a single-column layout for constrained displays.
- Added the owner's original transparent VibeWare master, localized GitHub and canonical
  manifesto links, and artwork provenance. The press-kit dark/light symbols have opaque
  backgrounds and are excluded. Removed duplicate report/repository actions and updated
  About copy in every supported language.
- Centered the artwork, reduced the default height to 520, displayed the full package
  version and named the licenses action for accessibility. C# formatting and verification
  passed. Built, signed and installed Debug x64 MSIX 1.0.6.11 with `Ok` registration.
- Reviewed the installed About in Italian at 150% scale in both light and dark themes:
  the trail, transparent logo, text, metadata, diagnostics actions and footer fit without
  clipping. Confirmed accessible button names and retained placement at the compact size.
  Restored the owner's System theme after the review. Other scales, constrained work
  areas, long translations, high contrast and diagnostic failure states remain unverified.

### Process memory recovery guard

- Added a ten-second process sampler with a 1 GiB limit for private or resident memory,
  periodic managed-memory/handle diagnostics, and one native Windows acknowledgement
  before restart. Recovery preserves tracking state, safe mode and runtime launch mode,
  bounds runtime shutdown, flushes logs and records the successor's recovery activation.
- Added localized warning/failure copy. All 14 synthetic recovery cases passed, and C#
  formatting and verification passed. Built, signed and installed Debug x64 MSIX 1.0.6.8;
  verified package version, architecture and `Ok` registration status. Build/test output
  was cleaned and the installer retained. Normal installed-app startup and periodic
  memory logging were observed. Native warning/restart behavior remains unverified.
  This protection does not establish or repair the original leak or explain OS-wide hangs.

## 2026-10-07

### Repeated screenshot failure notifications

- Report only the first capture failure until a complete capture succeeds. Scheduled,
  manual, and AI capture paths share this notification state; retries and local error
  logging continue, including while successful-capture notifications are disabled.
- C# formatting and verification passed. Built, signed, and installed Debug x64 MSIX
  1.0.6.7; verified current-user version, architecture, and `Ok` registration status.
  Project build output was cleaned and the installer retained. Automated tests and
  native notification behavior remain unverified.

## 2026-10-06

### Tray menu shortcuts and open-window reveal

- Added localized tray shortcuts for notification pause/resume, VIP capture, tracking
  pause/resume, international clocks, the astronomical calendar, day/night map and globe,
  Settings, and About, reusing existing application workflows.
- Added Show all windows beside the visibility command. It restores the main window and
  reveals its already-open peers through the existing facade, without creating windows.
- Persisted global notification pause while preserving the screenshot-specific preference
  and normal capture/tracking behavior. Native callbacks defer application work to the UI dispatcher.
- Added a settings persistence regression case. C# formatting and verification passed.
  Built, signed, and installed Debug x64 MSIX 1.0.6.6 with the expanded tray menu;
  verified current-user version, architecture, and `Ok` registration status. Project
  build output was cleaned and the installer retained. Tests and native interaction remain unverified.

### Compact severity toasts

- Removed the decorative countdown progress bar and compacted the shared toast surface.
  Added green, amber, and red palettes for low-severity messages, warnings, and errors,
  with native severity icons, matching borders and text, and high-contrast resources.
- Preserved automatic dismissal, replacement protection, manual closing, and fade transitions;
  the timeout no longer updates a visual progress indicator. Updated the existing surface
  contract to reflect the new presentation. Included in built, signed, and installed
  Debug x64 MSIX 1.0.6.6; tests and native interaction remain unverified.

### VIP snapshots and capture notes

- Moved label management into the player selector and used its second row for a VIP capture
  button. Added a cancellable five-second Mica countdown and an Acrylic confirmation with preview
  and an optional plain-text note, while reusing the existing multi-monitor capture pipeline.
- Persisted VIP metadata by capture identity so all monitor artifacts share importance and
  note. Archive transfer preserves it, and deleting the last retained artifact clears its
  calendar indicator. Added dedicated runtime operations for VIP capture and metadata.
- Added a featured VIP strip, separate gold borders and crown badges in the gallery, notes
  in screenshot details, and calendar crowns even on dates with no recorded activity.
- Added persistence/deletion regression cases and extended the multi-monitor archive case.
- Debug x64 build passed with no warnings or errors; C# formatting and its verification passed.
  The selected presentation checks passed 22 of 24 cases. The two failures match pre-existing
  source inconsistencies on the base commit: duplicate report Premium badges and a missing
  OCR action accessible name. Core validation awaits an approved retry after correcting a
  dispatcher compilation error. Native Windows interaction remains unverified.
- Built, signed, and installed Debug x64 MSIX version 1.0.6.4 with the VIP snapshot changes.
  Verified current-user registration, version, architecture, and `Ok` package status;
  the installer is retained and project build outputs were cleaned.
- Refined both VIP dialog layouts and added localized explanations of immediate manual
  capture and ordinary screenshot handling with VIP prominence in the gallery and calendar.
  Built, signed, and installed these dialog refinements as Debug x64 MSIX 1.0.6.5.
  Verified version, architecture, and `Ok` package status; native interaction remains unverified.

## 2026-10-04

### Shared Premium upgrade prompt

- Replaced purchase and Full-edition wording with "Switch to Premium" across all supported languages.
- Reused one localized dialog with a highlighted Store action for export, timesheet, label,
  clock, schedule, data-transfer, and retention restrictions. Free retention choices and
  the add-clock action now reach the prompt without unlocking the restricted operation.
- Routed Store activation through the application facade using WorkTrail's package family
  identity, with a visible localized error if Windows cannot open the Store.
- C# whitespace formatting and verification passed. Runtime dialog presentation, Store
  navigation, and build/test validation remain unverified.

## 2026-10-03

### One-command Debug MSIX installation

- Added `InstallDebugMsix` to the general script to clear `artifacts`, build, sign, install, and verify a local Debug MSIX. It requires an explicit four-part version, chooses a trusted matching certificate, and cleans project output while preserving the new package.
- The owner granted a feature-freeze exception limited to this script command.
- Built, signed, installed, and verified Debug x64 version 1.0.6.3 with a valid package signature and `Ok` registration status.

### Create the screenshot folder before opening it

- Create the configured screenshot directory before passing it to Windows Explorer. This covers both the maintenance action and the folder button in the screenshot gallery when no capture has created the directory yet.
- The maintenance action creates and opens the configured directory; the gallery action opens the directory containing the selected screenshot.
- Resolve MSIX LocalAppData virtualization to the shell-visible `LocalCache\Local` path before opening Explorer; retain the canonical configured path for unpackaged execution.
- Included the fixes in installed Debug x64 package version 1.0.6.3. Verified that the resolved physical directory exists and contains the retained screenshots; native button clicks remain for owner confirmation.

### Best-effort beta database opening and global error reporting

- Removed exact schema/version preflights from activity database opening and archive
  database access. Initialization adds missing objects without replacing existing
  definitions or history; existing informational database versions are preserved.
- Routed startup, repeated activation, WinUI, unobserved task and application-domain
  exceptions through a shared reporter. It writes a synchronously flushed emergency
  log and shows a localized native error dialog even before a main window exists.
  Concurrent failures do not stack dialogs, and task acknowledgement does not block GC.
- Updated the beta database agreement and existing regression cases for retained history,
  old/unversioned databases, additional objects, missing objects and actual SQLite failures.
- Reviewed source and focused diffs manually. Built and installed the signed Debug x64
  MSIX 1.0.6.0; Windows reports the installed package status as Ok. Cleaned build output
  while retaining the installer. Formatting, hooks, tests, application launch,
  native-dialog interaction and the owner's actual database remain unverified.

## 2026-10-02

### Explicit Excel preview actions

- Replaced the faint preview action with a filled generation button and an inline
  generation status beneath the card. Completed previews expose a selectable absolute
  path, an explicit open command, and an accessible copy-path icon.
- Kept workbook creation, validated file opening, and clipboard access behind the runtime
  facade. Changing report options hides stale preview results; opening or copying reuses
  the existing file without regenerating it or submitting AI work.
- Updated all shipped languages and the UI automation register. Source review and C#
  formatting verification passed; tests, a new build, and live interaction remain unverified.

### Localized UI automation selectors

- Registered stable selectors and localized accessible names for the main window,
  reports, settings, retention, screenshot commands, sensors and maintenance navigation.
- Added a dedicated control-name dictionary backed by the existing language catalogs
  and a conversion register organized by UI surface. Kept state-dependent accessible
  names and gave generated weekly schedule cells invariant selectors.
- Included unopened menus, toolbar commands and expander headers in localization.
  Validated selector coverage, every shipped language and export captions, and built
  the Debug x64 UI. Live automation-tree inspection and interaction testing remain separate.

### Grouped sensor details and visible-page rendering

- Grouped every captured sensor reading by device, including zero and unavailable values,
  with closed device sections instead of a flat expanded list.
- Continued collecting all live trace samples while rendering and copying points only for
  devices on the visible monitor page. Preserved capture timestamps and stored measurements.
- Validated the hardware and sensor presentation checks with synthetic data and built the
  Debug x64 UI. Visual acceptance and live memory profiling remain separate checkpoints.

### Export reliability review

- Distinguished rejected Batch creation from uncertain submissions, released unsubmitted
  quota reservations atomically, and retained recovery identities without retrying paid work.
- Added runtime recovery independent of export-window lifetime, durable result readiness,
  retryable cloud cleanup, and a paged job catalog without the former 200-job cutoff.
- Reconciled usage in one cancellable transaction and included consumed tokens from
  incomplete responses in estimated costs.
- Shared Excel package metadata, indexed day/project source selection, and removed the
  full history projection previously used only to choose a save filename.
- Added synthetic regression cases for rejection, uncertainty, archive paging, runtime
  recovery, download/cleanup failure, quota transactions, source attribution, and costs.
  Validated targeted export, aggregation, batch, and localization checks using simulated
  provider responses, and built the Debug x64 UI. Live provider submission and visual
  acceptance remain outside those checks.

### Batch timesheets and Excel previews

- Added a resumable OpenAI Batch timesheet report with measured day/project durations,
  optional morning/afternoon rows, monthly sheets, billing fields, and source details.
- Persisted job identities and partial results independently of the export window;
  uncertain submissions are reconciled before another paid batch can be created.
  Raw request prompts and API credentials are excluded from persisted job snapshots.
- Replaced embedded export previews with a generated Excel-themed card that opens a
  temporary workbook limited to ten records per sheet without calling AI.
- Kept the export title bar and native close button visible, and added a localized
  in-app explanation of daily, application, and whole-period summary grouping.
- Validated batch transport and recovery with simulated responses, workbook structure,
  measured interval projection, entitlement, and localization contracts.


### Consolidated storage settings and v1 surface cleanup

- Collected screenshot capture preferences, local OCR choices, and saved hardware metadata
  under data retention, using the existing typed settings facade and automatic persistence.
- Removed taskbar widget settings and activation paths from v1, rejected its withdrawn
  catalog keys, and disabled saved visibility when loading settings.
- Renamed the main-window position control consistently in every shipped UI language.
- Removed the redundant separators above data tools and startup options, and unified
  OCR/AI navigation with the existing settings links for consistent pointer feedback.
- Centered the labels editor on its owner at each opening while retaining saved size,
  and kept its title bar visible independently of the global auto-hide setting.
- Reduced captured hardware summaries to CPU total, GPU load and temperature, physical
  RAM capacity, disk space and transfers, and network throughput with compact units.
  Retained unmodified sensor readings in the complete details and persisted snapshots.
- Added colored day, week, and month icons to export-period shortcuts, preserving
  localized labels and accessible names with high-contrast theme support.

### Calendar-month retention refinement

- Limited effective activity and screenshot retention to one month for Free and three
  months for Premium, with matching guards in the shared settings facade.
- Added monthly choices, shared Premium badges, an installation-anchored timeline,
  separate preview counts, and moved the screenshot folder editor, open-folder action,
  and keep-captures toggle out of general settings while preserving automatic persistence.
- Queued due cleanup after workspace readiness and restoration, with hourly due-date checks
  while the UI is running and the shared Mica progress surface for deletion.
- Preserved owned-artifact checks and capture provenance; the monthly checkpoint advances
  only after cleanup and local-search synchronization finish successfully.

## 2026-09-24

### Standalone Excel planner source snapshot

- Added the owner-requested ExcelPlanner folder with the current workbook, six CSV
  datasets, original authoring scripts, source provenance and third-party licenses.
- Kept the template free of credentials and machine-specific data paths. The
  existing launcher now uses an ignored local working copy.
- Preserved the separate Obsidian copy; excluded private plans, keys, installed
  dependencies, reference workbooks, backups and temporary QA output.
- Checked script syntax, copy integrity and preservation of native query parts.
  No app integration, build, test suite, API refresh, commit or push was performed.

### Locale checks after private Store listing removal

- Made repository discovery use the solution file and checked the canonical locale catalog
  against shipped localization files, keeping manifest and duplicate-property validation
  independent of the private Store listing.
- Kept the public MIT contract and checks against automatic Store publication independent
  of removed listing drafts and workflows.

### Guided provider setup and About polish

- Added reusable AI/OpenWeather credential sheets with masked examples, provider guides,
  pricing links, localized validation feedback, and OK/Close outcomes without a step indicator.
- Connected the AI sheet to Quick Setup and the weather sheet to world-clock options.
  AI profiles require a successful check of the current provider configuration and credential;
  rejected or rate-limited candidate keys do not replace the saved credential.
- Made the About hero reach the content edges and replaced the unavailable Store invitation
  with a short GitHub action. Visual acceptance and installer delivery remain separate.

## 2026-09-23

### App identity and Store notes

- Replaced the generated Windows app identity assets with the owner-selected briefcase and trail mark, keeping small target-size icons legible and preserving the previous icon's provenance.
- Moved Store listing drafts and screenshot planning into private MeUp notes, removed the repository listing workflow, and kept local listing validation available through an explicit path.
- Restricted release CI to portable ZIPs for x64 and ARM64; Store MSIX packaging remains a local owner-controlled operation.

### About links and alignment

- Added allowlisted Privacy and Terms links to the About window and grouped the website, GitHub repository, and policy links on the left while retaining the centered Store/GitHub message.

## 2026-09-21

### Reset confirmation reliability

- Replaced the crash-prone illustrated reset confirmation with a dedicated, accessible red WinUI dialog and keep a confirmation-host failure inside the app without preparing a reset.

### Store preparation and product boundaries

- Established the September 21–27 maintenance freeze for Store preparation. Reliability fixes and polish remain in scope; new features require an explicit exception.
- Separated public technical and user documentation from private pricing, launch, Store-account, roadmap, and design notes.
- Standardized the public README around the activity-tracking benefit, source setup, material limitations, MeUp presentation, and solo-maintainer voice.

### Feature access and commercial boundary

- Introduced a runtime-owned feature catalog and Core-enforced Free/Premium policy shared by desktop, CLI, and IPC callers. Production defaults to unlicensed Free; the non-persisted profile simulator is Debug-only and excluded from release contracts.
- Applied Free limits to saved labels, world clocks, screenshot schedules, and archive execution while preserving previews and user data across downgrade. Store purchase and entitlement integration remain deliberately separate.
- Classified the operational CLI as Premium and kept destructive or replacing operations behind previews, confirmation, expiring plans, and runtime authorization.

### Data transfer, export, and AI summaries

- Added native activity export for Excel, zipped CSV, and typed JSON with bounded previews, saved field choices, atomic destination replacement, CSV formula protection, and continuation sheets for long text.
- Kept AI summaries explicit and editable. Ordinary previews and exports remain local; sensitive text is opt-in, provider usage is accounted for, and measured activity stays distinct from screenshot interpretation.
- Added typed archive progress across the facade and runtime protocol. Progress contains phases and counts but no local paths, remains observable outside the mutation lock, and is discarded after completion.
- Made snapshot conflict checks semantic for JSON while retaining strict scalar conflicts and malformed-input rejection.

### Labels, localization, and interface polish

- Added validated saved labels with stable history semantics: renaming the selected label updates selection, while deletion clears selection without rewriting past activity.
- Promoted the interface-language picker. System remains the default, explicit choices remain restart-required, and all user-facing changes continue across the ten supported locales.
- Consolidated label management in a resizable queued dialog and kept export, maintenance, and player surfaces accessible across translated widths, themes, and high contrast.

### AI, weather, and internal structure

- Aligned screenshot prompts with the resolved app language and available context, bounded detail-specific input, and explicit separation between observation and inferred intent, duration, completion, or productivity.
- Made significant NOAA space-weather forecasts global; coordinates and darkness remain relevant only to local astronomy and weather.
- Split the application facade, runtime lifecycle, telemetry, capture, product-link, and CLI interactive responsibilities into focused partials without changing the shared facade contract.
- Strengthened release-aware runtime-catalog checks so Debug-only operations cannot leak into production. Broad solution validation was completed during the PR #42 integration work.

## 2026-09-20

- Repaired cold startup after strict localization validation rejected divergent catalogs. All locales were brought back to key and placeholder parity; cold launch, tray registration on minimize, and redirected Start-menu restoration were verified together.
- Corrected shutdown ownership for celestial rendering and notification timers so late callbacks cannot update closed surfaces. The original crash relationship remained unproven without a complete exception record.
- Restored celestial views by accepting valid faint catalog objects, made Search open compact and expand for results, and retained the shared thin material treatment.
- Added a city-local current-time marker and interval state to the astronomical agenda, plus geographically bounded aurora advisories and separate weather/advisory rows.
- Increased magnetic window-snapping tolerance while retaining intentional off-screen movement and monitor-boundary escape behavior.
- Removed the public roadmap and completed the product-first documentation and author-voice refresh.

## 2026-09-19

### Astronomy and desktop surfaces

- Introduced one Core Astronomy Engine with bounded caches for world clocks, Moon, maps, local sky, agenda, and Earth views. Added versioned runtime operations, offline ephemerides, source-attributed catalogs, computed events, and independent window placement/restoration.
- Moved sky facts and palettes into strict embedded data, expanded the attributed star/constellation subset, and added optional ISS/Tiangong projection from recent elements without compromising offline behavior.
- Kept the globe and flat day/night map as distinct surfaces, simplified celestial controls, removed obsolete sky zoom, and used owner-provided screenshots for public product imagery.
- Completed PR #39 through protected review and checks. The signed MSIX installations from this period were local development validation, not public releases.

### Runtime, tray, hardware, and UI

- Retired interactive/HTML activity reports and their build/runtime routes while preserving calendar aggregation and user data.
- Added optional magnetic snapping through Core-owned geometry and owning-thread native registrations; the disabled setting leaves native movement untouched.
- Classified the activity calendar as a transient dialog so stale open-state flags do not restore it at startup.
- Hardened notification-area recovery: verify Explorer before hiding, use version-4 activation events, restore on `TaskbarCreated`, and keep the main window reachable when icon registration fails.
- Bundled the signed, pinned PawnIO component only for x64 advanced sensors. Installation remains an explicit consented action; ARM64 stays excluded.
- Consolidated compact, paged sensor rows with accessible unavailable states, textual disk capacity, battery level, and high-contrast-safe activity traces.

## 2026-09-18

- Established the current Sensors layout and replaced disk capacity bars with localized free/total text while preserving activity and battery information.
- Consolidated repository guidance around scoped reads, bounded output, explicit validation authorization, and preservation of unrelated work.

## 2026-09-16

- Made astronomical title bars reversible overlays that do not reserve content space and remain bounded for touch, theme, and DPI behavior.
- Repaired archive handling for current screenshot-path storage and legacy missing artifacts. Import now commits data, ledger, and search invalidation together; cleanup failure is reported separately from durable transaction success.
- Made screenshot migration explicit: preserve OCR/AI records and timestamps, use canonical dated storage, support metadata-only histories, and reject unknown layouts.

## 2026-09-15

- Expanded search synonyms to a schema-2 catalog with 300 concepts per supported locale. Matching uses deterministic bounded variants, longest phrases, Unicode-aware boundaries, literal-token protection, and best-variant scoring rather than alias accumulation.
- Kept synonym catalogs independent of the search index so catalog updates require restart, not index rebuild.

## 2026-09-01

- Added explicit loading, empty, and populated world-clock states and allowed an intentionally empty selection.
- Kept weather credentials masked and out of returned state or logs. Provider validation stores only accepted or rate-limited keys through the environment-variable flow.
- Versioned the changed world-clock and provider-probe runtime operations so older runtimes fail explicitly instead of returning stale semantics.
