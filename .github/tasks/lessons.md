# Engineering lessons

Keep reusable failure mechanisms here, not task transcripts or copies of
[AGENTS.md](../../AGENTS.md). Add a lesson only when it changes a future decision.

## Startup and windows

- A service constructed before MainWindow can prevent both the window and tray
  from existing. Localization, storage, search and runtime composition need a
  cold-launch check; a successful build cannot establish startup behavior.
- Redirected activation can reach a hidden background instance. Preserve requests
  during startup and explicitly show the window; Activate alone may not restore it.
- Verify tray registration before hiding Main. Recover after Explorer restarts and
  keep Main reachable if registration fails.
- A new executable can route to an older running runtime. Check the actual installed
  version and runtime when investigating behavior.
- Transient dialogs must not share workspace-window restoration semantics. Otherwise
  stale visibility state can reopen dialogs or strand hidden owners.

## Persistence and contracts

- Commit related data, deletion intent and search invalidation together. A cleanup
  failure after commit does not mean the durable operation rolled back.
- Database opening and archive validation are different boundaries. Best-effort beta
  opening adds missing objects without resetting history; explicit archive operations
  still validate their supported input before writing to the destination.
- Change typed contracts, dispatch, clients and test doubles together. A protocol
  version change must prevent an older runtime from returning stale semantics.
- UI restrictions do not secure CLI/IPC calls. Enforce feature access and destructive
  authorization in Core; keep Debug access simulation non-persisted and out of Release.
- Treat exported text as untrusted spreadsheet cells. Protect formulas, bound previews
  and replace destinations atomically so a failed export cannot destroy an existing file.
- Localization keys and placeholders are startup dependencies. Keep locale parity
  and a diagnostic path that works even when localization initialization fails.

## Evidence and projections

- Screenshot count and input activity do not measure task duration. Estimate spans
  from adjacent UTC observations, retain unknown gaps and overlaps, and preserve the
  original reconstruction separately from corrections.
- Calendar/gallery reads must use saved reconstructions. A failed regeneration must
  not erase the preceding result; clip midnight spans without duplicating duration
  and reject mixed revisions when assembling paged results.
- Preserve literal paths, filenames, addresses and URLs in multilingual search.
  Prefer longest phrases and the best bounded synonym variant. Catalog-only changes
  should not force an index rebuild.
- Capture-policy checks with synthetic rectangles cannot prove native composition,
  privacy masking or image quality. Package registration likewise does not prove
  startup, visual behavior or successful provider work.
