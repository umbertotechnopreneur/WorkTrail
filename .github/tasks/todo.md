# WorkTrail pre-release backlog

This file contains only open, actionable work. Completed implementation notes, command
transcripts, test totals, package hashes, and historical handoffs belong in commits, pull
requests, or release records—not in this backlog.

When adding an item, give it an observable completion condition and an owner checkpoint when
the action needs authorization. Remove the item when it is complete.

## Active now: startup and restored-data recovery

- [ ] Complete native screenshot coverage on the final Debug package: covered/partly
  off-screen windows across mixed-DPI gaps, secondary selection, minimized/protected
  targets and frame timeouts. Debug x64 1.0.6.26 passes installed-runtime captures of
  active WorkTrail and VoiceDucker, auxiliary-panel exclusion and two-monitor
  panoramas with unchanged panel bounds. Covered-window backend capture also passes.
  Preserve the no-redraw/no-desktop-fallback boundary and the documented neutral
  backing for transparent areas whose Acrylic Windows omits.

- [ ] After owner authorization, validate the native World Time menu in the app.
  Check opening, Escape/light dismiss,
  all six existing destinations, saved-city preview refresh, rapid close/reopen,
  theme/language changes, keyboard access and small mixed-DPI work areas in the app.
  Strict Debug x64 compilation and Presentation surface contracts pass; installation
  and native behavior remain unverified.

- [ ] After owner authorization, validate the rule-driven screenshot
  engine and Balanced/Complete analysis: native crop/privacy behavior on one, two
  and three mixed-DPI monitors.
  Check live and historical API/CLI descriptions, gallery monitor sections,
  metadata archive roundtrips, occluded secondary windows, destination isolation,
  application attention, title-hint precedence, micro-prompt routing, title-only/manual
  overrides, idle boundaries and clock rollover, blacklist image/context isolation,
  interrupted transient cleanup and response-truncation failures. Check lock/unlock,
  pause/resume, renamed/translated titles and disabled detail collectors on Windows.
  Check near-duplicate thresholds on real 4K text,
  partial multi-monitor skips, manual bypass, publication failures and forced refresh.
  Check rapid foreground changes, maximum wait, bounded retries, pause/setting races
  and scene changes during acquisition without recurrent error notifications.
  Check title-only Spotify foreground data, panorama masks across monitors, hidden
  windows, manual exclusion, empty selections and unchanged masked-title noise.
  The October 17 strict Debug x64 build and all four test suites pass after the
  regression fixes. The earlier 1.0.6.22 Debug package predates these changes and the manual override.

- [ ] After owner authorization, build and validate AI-spending removal: fresh
  database creation, request/token reports, daily request limits, exports and
  absence of the monthly total and pricing window. No schema migration is provided.

- [ ] Validate native CLI/API selection in Settings and guided setup, API-key
  retention, missing/outdated CLI,
  ChatGPT login expiry, account limits, timeout/cancellation and screenshot output.
  Confirm no API fallback and one visual-description request per analysis.
- [ ] After owner authorization, validate restart placement on mixed-DPI displays:
  unchanged topology, reordered monitors, a disconnected saved monitor and changed
  scaling. Check main-window manual size, dialogs and workspace shutdown flushing.
  Existing placements without saved DPI retain physical size on their first restore.

- [ ] After owner authorization, validate the inline history settings on Windows at
  narrow widths, supported text scales and themes: retention selection and upgrade
  prompt, storage warning threshold, inclusive date selection, deletion confirmation,
  cancellation and recovery. The focused automated checks and Debug x64 build passed;
  native interaction and production automatic maintenance remain unverified.

- [ ] Replace the Debug MSIX installation helper's whole-artifacts deletion with
  verified task-owned cleanup. Preserve pending installers and unrelated output;
  use isolated build/package paths until this is corrected.

- [ ] After owner authorization, validate the main-window VibeWare footer at narrow widths,
  supported text scales and light/dark/high-contrast themes. Check its manifesto link,
  keyboard focus, failure dialog and placement with expanded or compact player details.
  Native UI checks require separate authorization.

- [ ] After owner authorization, validate the world-clock title-bar week at narrow widths
  and supported text scales, with live and custom reference dates, reference-city changes,
  the ISO week-year boundary, and an empty clock selection. Automated and native
  checks require separate authorization.

- [ ] After owner authorization, validate native movement and resizing with the installed
  primary-window snap guides: check both guide orientations, preview/active opacity,
  all eight resize directions, retained opposite edges and minimum/maximum sizes,
  multiple monitors and DPI scales, disabled snapping, cancellation, window closure,
  and exclusion of dialogs and About as both moving surfaces and peer targets.

- [ ] Extend the compact About review to other supported scaling levels, constrained work
  areas, long localized text, high contrast and diagnostic failure states. Debug x64 MSIX
  1.0.6.11 was installed and visually reviewed in Italian at 150% in both light and dark
  themes, using the original alpha logo; its compact size and retained position were
  observed. The GitHub and canonical manifesto destinations were checked against their
  public pages; native external-link activation remains unverified. Additional tests
  require owner authorization.

- [ ] After explicit native-test authorization, validate the installed process memory guard:
  Windows warning acknowledgement, bounded runtime shutdown, active/paused tracking after
  recovery, retained activity and durable memory/recovery events. Do not allocate excessive
  memory in the owner's active profile. The October 8 log cannot establish the cause of the
  reported 3 GB peak; correlate process samples with system memory and storage events
  before attributing OS-wide hangs to WorkTrail. Live profiling requires separate approval.

- [ ] Validate the expanded tray menu on Windows: show all already-open windows from hidden
  and minimized states, pause/resume tracking and notifications, restart to verify the saved
  notification preference, and open VIP capture, clocks, astronomy views, Settings, and About.
  Build, automated checks, and native interaction require owner authorization.

- [ ] Validate the VIP snapshot flow on an isolated Windows desktop: cancel the five-second
  countdown, capture multiple monitors without the player, save or skip a note, reopen the
  gallery, select VIP and ordinary images, and verify calendar crowns after deletion.
  Installation and native UI interaction require owner authorization.

- [ ] Inspect the converted controls in the installed app's UI Automation tree and
  exercise the theme, capture, gallery and export paths on an isolated Windows desktop.
  Update `docs/UI_AUTOMATION.md` by UI surface only after the relevant interactions pass.

- [ ] Attempt every runtime shutdown step even after one fails, and protect the normal WinUI
  close handler with error reporting and logging shutdown. Verify close during capture
  and injected disposal failures using synthetic services before a separately approved live run.
- [ ] Bound gallery memory with paged metadata and selected-capture detail loading while
  retaining capture provenance and AI descriptions in SQLite. Measure large synthetic days before
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

- [ ] Visually accept the astronomical agenda's 17-country holiday filters, Latin
  saints, per-entry artwork and same-window settings at Windows x64 scale levels;
  confirm sunrise ordering, reference marker and polar-day presentation. Local
  Debug build and targeted tests are authorized; installation still needs owner approval.

- [ ] Visually accept the guided AI/OpenWeather key sheets and edge-to-edge About hero at
  Windows scaling levels, including success, rejected key, offline/timeout, close during a
  request, and returning to a profile without AI. The owner authorized this OOBE work as an
  exception to the September freeze; package installation remains separately controlled.

- [ ] A Start-menu launch must always restore, show, and activate the existing Main window,
  including when the process is already running, hidden, or minimized.
- [ ] Register the notification-area icon for the application lifetime. Its primary action
  must restore Main, and shutdown must remove it cleanly.
- [ ] Treat a missing, incompatible, or corrupt local search index as a rebuildable cache.
  Rebuild it without blocking application startup or replacing stored activity data.
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
- [ ] Verify the shared Premium prompt and Microsoft Store action from exports, timesheets,
  labels, clocks, schedules, data transfer, and two-/three-month retention choices; closing
  the prompt must preserve Free limits, and a Store launch failure must show a visible error.
- [ ] Verify the label editor and player selector for creation, rename, icon/color, duplicate
  names, delete-active, no-label, restart, persistence failure, compact widths, long captions,
  and stable placement beside the timer.
- [ ] Verify the streamlined Quick Setup cards, selected profile, Start with Windows, and the
  single confirmation action. The removed Sensors entry point must not reappear.
- [ ] Verify simplified AI settings, AI spend visibility, world-clock options, data-transfer
  tabs, and maintenance material without horizontal scrolling or clipped actions.

### Activity, clocks, and sky

- [ ] Verify Activity history, Screenshot gallery, and Search are top-level actions; calendar
  startup remains closed; the current-time marker and active-interval borders are correct.
- [ ] Verify NOAA location filtering and immediate refresh in clocks and an already-open agenda,
  including rollback when the option cannot be saved.
- [ ] Verify Local Sky, Astronomical Agenda, Earth globe, zodiac/stellar labels, ISS/Tiangong
  visibility, city-local aurora, weather/advisory rows, Moonrise/Moonset artwork, responsive
  centering, cancellation, and independent window restoration.

## Delivery checkpoints

- [ ] Do not commit, push, open a pull request, publish a package, upload to the Store, or change
  trust-store state without the owner's explicit authorization for that action.
- [ ] Before delivery, review only the intended diff, preserve unrelated work, use explicit-path
  staging, and resolve required checks and review conversations.
- [ ] Keep Windows distribution MSIX-only for x64 and ARM64. Linux and macOS remain source-only.
