# Task Archive

This is a compact historical index of durable WorkTrail milestones. It intentionally omits command transcripts, temporary artifact paths, repeated test counts, intermediate package versions, and superseded pre-rebrand naming. Older entries may predate the WorkTrail name.

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
