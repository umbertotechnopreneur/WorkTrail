# Engineering lessons

- A service constructed before Main can prevent both the window and tray from existing. Check the cold-start composition path; a successful build does not establish startup behavior.
- Removing a UI feature also requires removing its startup factory construction, timers, IPC operations and settings. Hidden entry points can keep collecting data after their buttons disappear.
- Assembly-wide attributes must live in a neutral file. Keeping test visibility in a feature-specific source file makes unrelated tests fail when that feature is removed.
- Old saved visibility flags must not stop the current widget workspace from opening. Select current surfaces without opening or rewriting unrelated history.
- Verify tray registration before hiding Main. An unavailable Explorer icon must leave the launcher reachable.
- Redirected activation can arrive before initial startup finishes. Retain the reveal request and explicitly restore the existing hidden window.
- Persist before publishing a settings snapshot. Serialize geometry, visibility and preference mutations so an unrelated change cannot overwrite a newer window placement.
- Change runtime contracts, dispatch, clients and test doubles together. Reject protocol mismatches before an older runtime can return different semantics.
- Localization resources and accessible-name registries are startup dependencies. Keep key/placeholder parity and an error path that works if localization fails.
- Formula injection, bounded previews and atomic destination replacement remain relevant to the separate ExcelPlanner prototype. Do not run one-time workbook migration scripts over its template without reviewing their inputs.