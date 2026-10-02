# WorkTrail pre-release backlog

This file contains only open, actionable work. Completed implementation notes, command
transcripts, test totals, package hashes, and historical handoffs belong in commits, pull
requests, or release records—not in this backlog.

When adding an item, give it an observable completion condition and an owner checkpoint when
the action needs authorization. Remove the item when it is complete.

## Active now: startup and restored-data recovery

- [ ] Inspect the converted controls in the installed app's UI Automation tree and
  exercise the theme, capture, gallery and export paths on an isolated Windows desktop.
  Update `docs/UI_AUTOMATION.md` by UI surface only after the relevant interactions pass.

- [ ] Drain pending hardware requests before disposing tracking and its snapshot gate.
  Attempt every runtime shutdown step even after one fails, and protect the normal WinUI
  close handler with error reporting and logging shutdown. Verify close during collection
  and injected disposal failures using synthetic services before a separately approved live run.
- [ ] Restore previously enabled advanced sensors after the application is ready, so UAC
  consent or its timeout cannot hold the initial window. Keep fresh installation consent explicit.
- [ ] Preserve PawnIO's pending-reboot requirement across WorkTrail restarts until Windows
  has rebooted or the loaded driver is positively verified. Registry package version alone
  must not authorize activation after installer exit code 3010.
- [ ] Bound gallery memory with paged metadata and selected-capture detail loading while
  retaining all hardware, OCR, and AI data in SQLite. Measure large synthetic days before
  claiming a memory improvement; live profiling requires separate owner authorization.

- [ ] Finish final synthetic Core validation of the implemented screenshot publication,
  deletion recovery, shared retention inventory, and cancellable SQLite transaction.
  The previous run passed 150 of 151 cases and exposed duplicate raw/stored progress;
  the fix and final publication cleanup review are complete, with a rerun awaiting owner
  approval. Keep real screenshots untouched and preserve failed recovery intents.
- [ ] Visually accept the export title bar, grouping help dialog, and temporary Excel
  previews at supported Windows scaling levels and themes. Confirm worksheet layout
  in Excel. A live paid Batch submission, interruption/recovery, and cloud cancellation
  require separate owner authorization; automated validation uses simulated responses.

- [ ] Visually accept monthly retention at Windows scaling levels in light, dark, and high
  contrast themes, including Free/Premium choices, a missed monthly cleanup after startup,
  and shutdown while the shared progress dialog is active. Package installation remains
  separately authorized.

- [ ] Visually accept the guided AI/OpenWeather key sheets and edge-to-edge About hero at
  Windows scaling levels, including success, rejected key, offline/timeout, close during a
  request, and returning to a profile without AI. The owner authorized this OOBE work as an
  exception to the September freeze; package installation remains separately controlled.

- [ ] A Start-menu launch must always restore, show, and activate the existing Main window,
  including when the process is already running, hidden, or minimized.
- [ ] Register the notification-area icon for the application lifetime. Its primary action
  must restore Main, and shutdown must remove it cleanly.
- [ ] Treat a missing, incompatible, or corrupt local search index as a rebuildable cache.
  Rebuild it without blocking application startup or weakening database validation.
- [ ] Repair the current pre-release profile's stored screenshot roots once outside product
  code, then verify that Screenshot gallery opens the restored files. Do not add migration,
  fallback aliases, legacy product names, or compatibility code.
- [ ] Owner checkpoint: finish the remaining requested source changes before any build, test,
  package, installation, or application restart.
- [ ] After explicit approval, produce and install the Debug x64 MSIX, then verify cold launch,
  repeated Start-menu launch, tray restoration, ordinary shutdown, search recovery, and the
  restored Screenshot gallery.

## Release blockers

- [ ] Reproduce the close-time crash under a debugger with a diagnostic artifact that includes
  the original exception. Verify the shutdown fixes before declaring the crash resolved.
- [ ] Connect and verify the commercial entitlement source before shipping Premium purchases.
  Production must remain Free by default until that integration is proven.
- [ ] On an owner-authorized package run, verify Debug MSIX packaging for x64 and ARM64 with an
  explicit, unexpired certificate whose subject exactly matches the package manifest.
- [ ] Confirm required CI and review for the active delivery branch, including the
  configuration-aware runtime catalog and clean-checkout important-date catalog checks.
- [ ] Preview and import a schema-9 archive using synthetic data. Unsupported or corrupt input
  must fail before destination data changes, and the source archive must remain unchanged.
- [ ] Publish the AI report-summary guide with its matching source only when Git delivery is
  explicitly authorized and the linked document is ready to exist on the target branch.

## Automated verification after owner approval

- [ ] Rerun only `QuickSetup_UsesFourAccessibleProfileControlsOnVisibleAcrylic` after owner
  approval: its obsolete hidden-scrollbar expectation has been updated for the visible
  automatic scrollbar. The initial targeted run passed its other checks.

- [ ] Add or update focused coverage for single-instance activation, notification-area
  lifetime, startup recovery, rebuildable search-index state, and restored gallery paths.
- [ ] Run the corrected quota, world-clock invalid/ambiguous-time, archive-preview comparison,
  and affected UI contract tests that were compile-checked but not rerun.
- [ ] Run the space-weather location-filter and search-surface checks, including persistence,
  low latitude, polar day/night, expired alerts, the 48-hour forecast window, and unchanged
  non-geomagnetic alerts.
- [ ] Run the sensor-startup and shared-accessibility presentation scenarios. Declined or
  failed advanced-sensor activation must fall back to basic readings without repeated consent.
- [ ] Run the focused activity export, archive operations, Premium CLI, and screenshot
  maintenance scenarios with synthetic data only.
- [ ] Once the source has settled, run the required x64 solution tests and Debug build. Run the
  ARM64 gate when packaging, assets, or project structure changes. Record exact failures; do
  not turn a partial run into a pass.

## Owner visual acceptance

### Accessibility and window behavior

- [ ] Check all affected windows in light, dark, high contrast, disabled transparency,
  keyboard-only navigation, narrow layouts, and 100–200% text/display scaling.
- [ ] Verify ten-pixel edge/window snapping on mixed-DPI and negative-coordinate monitors:
  catch at ten physical pixels, no catch at eleven, no drift after release, and no influence
  from hidden, minimized, or closing peer windows.
- [ ] Verify Search remains compact when empty, expands and collapses with results, handles
  busy/error states, and retains keyboard focus and readable Acrylic.

### Export, AI, and archive

- [ ] Verify screenshot analysis follows the application language, detailed output uses
  readable bullets, and report summaries honor brief/detailed selection. Use only explicitly
  approved provider requests.
- [ ] Verify Export, File contents, and AI summary use the shared wider preview layout; preserve
  the selected export table across filter, period, refresh, rapid-change, and error-recovery
  paths.
- [ ] Verify localized export actions, editable previews, generated CSV/JSON/Excel content,
  Free-to-upgrade behavior, and synthetic Premium output. Confirm the generated workbook in
  Excel before sign-off.
- [ ] Verify native archive preview, export, and import remain responsive while reporting
  localized phases, real per-phase counts, elapsed time, remote-runtime polling, failures,
  cancellation, and shutdown.
- [ ] Verify both Nuclearize confirmations. Cancel or Escape must stop either step, Enter must
  default to Cancel, and any approved reset must use an isolated disposable profile.

### Premium, labels, onboarding, and settings

- [ ] Verify Free limits of three labels and three clocks, upgrade dialogs, Premium badges,
  downgrade behavior, preservation of existing over-limit catalogs, and stale-dialog handling
  after a profile change.
- [ ] Verify the label editor and player selector for creation, rename, icon/color, duplicate
  names, delete-active, no-label, restart, persistence failure, compact widths, long captions,
  and stable placement beside the timer.
- [ ] Verify the streamlined Quick Setup cards, selected profile, Start with Windows, and the
  single confirmation action. The removed Sensors entry point must not reappear.
- [ ] Verify simplified OCR/AI settings, AI spend visibility, world-clock options, data-transfer
  tabs, and maintenance material without horizontal scrolling or clipped actions.

### Activity, clocks, sky, and sensors

- [ ] Verify Activity history, Screenshot gallery, and Search are top-level actions; calendar
  startup remains closed; the current-time marker and active-interval borders are correct.
- [ ] Verify NOAA location filtering and immediate refresh in clocks and an already-open agenda,
  including rollback when the option cannot be saved.
- [ ] Verify Local Sky, Astronomical Agenda, Earth globe, zodiac/stellar labels, ISS/Tiangong
  visibility, city-local aurora, weather/advisory rows, Moonrise/Moonset artwork, responsive
  centering, cancellation, and independent window restoration.
- [ ] Verify advanced-sensor prerequisite consent, offline installation, required restart,
  first advanced reading, basic-reading fallback, and compact graph layout at narrow and wide
  sizes.

## Delivery checkpoints

- [ ] Do not commit, push, open a pull request, publish a package, upload to the Store, or change
  trust-store state without the owner's explicit authorization for that action.
- [ ] Before delivery, review only the intended diff, preserve unrelated work, use explicit-path
  staging, and resolve required checks and review conversations.
- [ ] Keep Windows distribution MSIX-only for x64 and ARM64. Linux and macOS remain source-only.
