# Open work

Keep only unfinished work here, grouped by outcome. Add a completion condition;
remove completed items. Authorizations and operating rules live in [AGENTS.md](../../AGENTS.md).
Past checks are not current acceptance evidence. Recheck the installed version before a live trial.

## Current observation period

- [ ] Collect one or two days of normal use, then review the saved observations.
  Obtain explicit approval before a real provider reconstruction. Verify reading
  checkpoints, lock/suspend/pause/restart boundaries, daily reconstruction, corrections
  after reopening, and Excel reconciliation. Visually accept week/day spans and the
  screenshot activity rail. The implementation plan stays in the private product notes.

## Reliability and data

- [ ] Reproduce the close-time crash with the original exception. Ensure every shutdown
  step is attempted after a failure, and the WinUI close path reports errors and flushes
  logs. Cover close during capture and injected disposal failures before a live trial.
- [ ] Verify cold and repeated Start-menu launch, restoration of an existing hidden or
  minimized Main window, tray lifetime and Explorer restart recovery. Add focused
  activation/startup coverage where missing.
- [ ] Confirm a missing, incompatible or corrupt search index rebuilds without blocking
  startup or replacing activity data. Verify the restored profile's screenshot roots
  outside product code; do not introduce path aliases or compatibility layers.
- [ ] Bound gallery memory with paged metadata and selected-capture details. Measure
  large synthetic days before claiming an improvement. Profile the memory guard only
  with approval; verify warning, bounded recovery, tracking state and retained data.
  The original leak and OS-wide hangs have no established cause.
- [ ] Complete the pending synthetic publication/deletion/retention validation after
  the duplicate progress fix. Preserve failed recovery intents and real screenshots.
- [ ] Validate archive preview, export and import with synthetic schema-9 data:
  reject malformed/unsupported input before destination changes; retain the source;
  check conflict comparison, progress counts, cancellation, shutdown and recovery.
- [ ] Validate history settings and automatic retention: date bounds, Free/Premium
  choices, storage warning, confirmation, cancellation, startup catch-up and shutdown.
- [ ] Update the Debug MSIX helper to use isolated output paths and verified cleanup.
  Remove obsolete output according to AGENTS.md while retaining the latest valid
  installer until its replacement is installed. Never delete application data.

## Capture and AI

- [ ] Validate native keyboard/click increments, pause/resume and runtime-command
  startup; check structured continuation/ending assessments with an approved provider.
  Keep measured input separate from estimated activity and screenshot eligibility.
- [ ] Validate crops and panoramas on one, two and three mixed-DPI monitors: covered,
  partly off-screen, minimized and protected windows; secondary selection; frame
  timeouts; transparent backgrounds; and auxiliary-panel exclusion. Preserve the
  no-redraw/no-desktop-fallback boundary.
- [ ] Validate privacy, blacklist and title-only rules across foreground changes,
  manual captures, empty selections, hidden windows and masked panoramas. Check
  destination isolation, monitor identities and archive roundtrips.
- [ ] Validate similarity thresholds on real 4K text, unchanged-image checkpoints,
  partial monitor skips, forced refresh, publication failure, retry bounds and
  pause/setting races. Check quiet skips and notification recovery.
- [ ] Validate Balanced/Complete descriptions, application-language output, title-hint
  precedence, micro-prompt routing, disabled details and truncated-response failures.
- [ ] Validate API/CLI selection and guided setup: retained API key, missing/outdated
  CLI, expired login, account limits, cancellation and output. Confirm one visual
  request per analysis and no fallback from CLI to API.
- [ ] Validate removal of spending estimates with a fresh disposable database:
  request/token reports, daily limits and exports remain; monetary UI stays absent.

## Native interface acceptance

Use light, dark and high contrast, disabled transparency, keyboard navigation,
long translations, narrow layouts and 100–200% text/display scaling where relevant.
These checks need owner authorization; this list does not grant it.

- [ ] Window lifecycle: taskbar preferences on first show and after restart; peer
  reveal; saved placement with changed DPI, reordered/disconnected monitors; shutdown
  placement flush; movement/resize snap guides, all dragged edges, limits, escape,
  negative coordinates, peer eligibility and retained position after release.
- [ ] Main navigation and Search: top-level history/gallery/search actions, no calendar
  auto-open, compact empty search, expansion/collapse, busy/error states and focus.
- [ ] World Time: flyout dismissal, six destinations, saved-city preview, rapid reopen,
  title-bar ISO week/year, reference-city/date changes and empty clock selection.
- [ ] Sky surfaces: agenda country/holiday/saint filters, artwork, sunrise ordering,
  polar day/night, reference marker, Local Sky, Earth, zodiac/stars, ISS/Tiangong,
  city-local aurora, weather rows, cancellation and independent restoration.
- [ ] NOAA: location filtering, immediate refresh in clocks and open agenda, rollback,
  low latitude, expired alerts, the 48-hour forecast window and unchanged other alerts.
- [ ] Gallery/VIP: countdown cancellation, multi-monitor capture without the player,
  optional note, featured and ordinary images, deletion and calendar crowns.
- [ ] Tray: restore already-open hidden/minimized windows, pause tracking/notifications,
  persist notification preference, and open each shortcut without duplicating windows.
- [ ] Export: title bar, grouping help, preview width and selected table across filters,
  rapid changes and errors; editable summaries; localized CSV/JSON/Excel; workbook
  layout in Excel. Paid Batch submission, recovery and cancellation need approval.
- [ ] Archive/reset UI: responsive phased progress and both Nuclearize confirmations;
  Escape cancels, Enter defaults to Cancel, and any reset uses a disposable profile.
- [ ] Premium/labels: shared upgrade prompt and Store failure handling; catalog limits,
  downgrade preservation, stale dialogs, create/rename/delete/icon/color/no-label,
  restart and persistence failure. Keep the selector position stable.
- [ ] Setup/settings: accessible Quick Setup cards and Start with Windows, no retired
  Sensors entry, AI/OpenWeather key sheets, success/rejection/offline/close paths,
  no-AI profiles, readable storage/data-transfer controls and no monetary AI UI.
- [ ] About/footer: compact and expanded player, original transparent branding,
  diagnostics, licenses, external links and failure dialogs. Earlier About acceptance
  covered Italian at 150% in light/dark only; other conditions remain open.
- [ ] Inspect the converted controls in the native UI Automation tree; update
  [UI_AUTOMATION.md](../../docs/UI_AUTOMATION.md) only for surfaces actually exercised.

## Automated checks and delivery

- [ ] Run the pending `QuickSetup_UsesFourAccessibleProfileControlsOnVisibleAcrylic`
  case after its scrollbar expectation fix, plus the outstanding world-clock
  ambiguous/invalid-time, archive comparison and affected UI contract cases.
- [ ] Run the pending space-weather/search checks and focused activity export,
  archive operations, Premium CLI and screenshot maintenance scenarios.
- [ ] Once source changes settle, run the required x64 solution checks and Debug build;
  include the ARM64 gate for packaging, assets or project-structure changes.
- [ ] Connect and verify the commercial entitlement source before Premium purchases
  ship. Production stays Free by default until that integration is proven.
- [ ] Validate ARM64 Debug MSIX packaging with the authorized matching certificate.
  Confirm required CI/review, configuration-aware runtime contracts and clean-checkout
  date catalogs before delivery. Local x64 installation does not cover these checks.
- [ ] Deliver the AI report-summary guide with its matching source when Git delivery
  is authorized; ensure the linked document exists on the target branch.
