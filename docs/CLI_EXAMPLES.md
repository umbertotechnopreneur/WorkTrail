# CLI examples

Search history, query reports and control WorkTrail Premium from PowerShell 7. These
examples use the installed `worktrail.exe` command and preserve its exit code.
For an unpackaged build, replace it with the quoted executable path after `&`.

The entire CLI requires Premium: Free includes no commands, help, version or interactive shell. Every invocation verifies access through the shared runtime and can start it with its saved startup settings when absent. Free returns `cli.premium.required` and exit code `11`; unavailable access verification blocks the command. The expected results below describe the output; they are not output captured from a user's installation.

The commercial license source is not connected yet, so the current production default is Free. Developers can select Premium simulation in the desktop Debug menu before trying these commands. The override ends when the runtime restarts; the CLI cannot grant access. See [Premium access](PREMIUM_FEATURES.md).

## Search, reports and clocks

```powershell
pwsh -NoProfile -Command '& worktrail.exe -cli search status --json; exit $LASTEXITCODE'
pwsh -NoProfile -Command '& worktrail.exe -cli search query --text "project notes" --limit 20 --offset 0 --json; exit $LASTEXITCODE'
pwsh -NoProfile -Command '& worktrail.exe -cli search query --from 2026-09-01T00:00:00+07:00 --to 2026-10-01T00:00:00+07:00 --json; exit $LASTEXITCODE'
pwsh -NoProfile -Command '& worktrail.exe -cli report --from 2026-09-01 --to 2026-09-21 --timezone UTC --view applications --json; exit $LASTEXITCODE'
pwsh -NoProfile -Command '& worktrail.exe -cli world-clock list --json; exit $LASTEXITCODE'
pwsh -NoProfile -Command '& worktrail.exe -cli world-clock cities --json; exit $LASTEXITCODE'
```

Search requires text or a filter. `--from` includes its timestamp and `--to`
excludes it; use ISO timestamps with seconds and `Z` or an explicit offset.
`--kind` selects a document kind, `--query-language` controls query analysis,
and `--include-text` includes text bodies and raw attributes. Bodies are omitted
by default, but result metadata can still contain private context. The shared
search service enforces result limits.

Reports use inclusive `yyyy-MM-dd` dates and an explicit time-zone ID.
Choose `calendar` (default), `hour-of-week`, `trend` or `applications`.
For conversion, use a city ID from `world-clock cities` in
`world-clock convert --city <id> --local-time 2026-09-21T14:30:00`.
This is civil time without an offset; Core handles daylight-saving validation.
Clock output omits celestial maps and artwork.

## Screenshot gallery and maintenance

```powershell
pwsh -NoProfile -Command '& worktrail.exe -cli screenshots gallery --date 2026-09-21 --json; exit $LASTEXITCODE'
```

Omit `--date` to read the latest gallery. `screenshots` aliases `screenshot`.
Gallery results can contain private titles and AI descriptions.

Copy an exact absolute path from the gallery into
`screenshots delete --date 2026-09-21 --path "<absolute-path>" --json` to preview
deletion. The item must belong to that date's gallery. The preview identifies the
image and indicates that associated analysis is removed too. Review it, then
repeat with `--yes`. Core resolves owned artifacts; the CLI never deletes
arbitrary paths. Preview does not reserve the file or freeze state.

## Export and import private data

Export defaults to a request preview. It does not validate filesystem availability,
count records or reserve the exported data set. Use an absolute `.tmuarchive` path:

```powershell
pwsh -NoProfile -Command '& worktrail.exe -cli data export --destination "C:\Backups\workday.tmuarchive" --from 2026-09-01 --to 2026-09-21 --json; exit $LASTEXITCODE'
```

Review the request, then repeat with `--yes`. **Confirmed export replaces an
existing archive at that destination.** Omit both dates for all data or provide
both for an inclusive local-date range. `--no-screenshots` excludes image files.
The archive remains private activity data, not an anonymized report.

```powershell
pwsh -NoProfile -Command '& worktrail.exe -cli data import preview --path "C:\Backups\workday.tmuarchive" --json --timeout 120; exit $LASTEXITCODE'
```

Review `value.planId`, `expiresAt`, counts and `alreadyImported`. Then run
`data import run --plan <planId> --yes --json --timeout 120`.
The plan belongs to the current runtime and expires. Core rechecks the fingerprint
and collisions before merging. Review a new preview when a plan expires; do not
silently replace it. Choose a per-request timeout up to 300 seconds for longer
operations. A timeout does not prove no data was committed; inspect state before
retrying a write.

## AI diagnostics and historical reprocessing

```powershell
pwsh -NoProfile -Command '& worktrail.exe -cli ai models --json; exit $LASTEXITCODE'
pwsh -NoProfile -Command '& worktrail.exe -cli ai reprocess preview --date 2026-09-21 --json; exit $LASTEXITCODE'
```

`ai models` reads the validated model catalog. `ai test --yes` sends the minimal non-image
connection check to the configured AI provider and can incur cost.

Reprocessing preview sends no analysis requests. Review eligibility, blocked
items, remaining daily request allowance, `canStart`, `expiresAt` and `planId`.

| Action | Command after `worktrail.exe -cli` |
| --- | --- |
| Queue the reviewed plan | `ai reprocess start --plan <planId> --yes --json` |
| Read progress | `ai reprocess status --job <jobId> --json` |
| Request a pause | `ai reprocess pause --job <jobId> --json` |
| Resume provider calls | `ai reprocess resume --job <jobId> --yes --json` |

Use the job ID returned by start. Start returns a job snapshot, not completed
analysis. Pause is cooperative and may finish the current capture. The shared
worker enforces privacy and cost gates. Closing the CLI or losing CLI access does
not itself pause the background job.

## Logs, feature access and reset

```powershell
pwsh -NoProfile -Command '& worktrail.exe -cli logs open-folder --format plain; exit $LASTEXITCODE'
pwsh -NoProfile -Command '& worktrail.exe -cli access status --json; exit $LASTEXITCODE'
pwsh -NoProfile -Command '& worktrail.exe -cli reset preview --json; exit $LASTEXITCODE'
```

`logs open` opens the latest application log; `logs open-folder` opens its folder.
These commands do not upload or share logs. `access status` reports the tier and
Debug simulation state. It requires Premium and cannot change entitlement.

Reset preview describes scope and the screenshot directory without stopping
tracking or preparing deletion. Reset deletes all local app data and app-owned
screenshots, disables startup and relaunches WorkTrail. API keys remain in their
environment variables. Back up data you want to retain before confirming.

The destructive syntax is `reset run --yes --confirm DELETE-ALL-DATA`. Both flags
are required in scripts and the command center. There is no automatic prompt
answer. `--yes` alone never authorizes reset. Missing or incorrect confirmation
returns exit code `3` before calling the reset service.

Success returns `app.reset.accepted` with `completionVerified: false`. The runtime
owner deletes and relaunches after responding. Exit code `0` confirms acceptance,
not completed deletion; inspect the relaunched app before assuming completion or
retrying.

## Status and diagnostics

Read the current dashboard, runtime health, and combined diagnostics:

```powershell
pwsh -NoProfile -Command '& worktrail.exe -cli status --format rich; exit $LASTEXITCODE'
pwsh -NoProfile -Command '& worktrail.exe -cli runtime health --format plain; exit $LASTEXITCODE'
pwsh -NoProfile -Command '& worktrail.exe -cli doctor --format plain; exit $LASTEXITCODE'
```

`status` shows tracking and current activity. `runtime health` checks the shared
tracker; `doctor` adds AI, retention, startup, and plugin checks. A complete
diagnostic result uses `doctor.healthy`; a partial result uses `doctor.partial`
and exit code `10`.

For a dashboard refreshed every five seconds:

```powershell
pwsh -NoProfile -Command '& worktrail.exe -cli status --watch --interval 5 --format rich; exit $LASTEXITCODE'
```

Press Ctrl+C to stop watching. Intervals must be integers from `1` through `60`, and `--interval` requires `--watch`. JSON supports a single snapshot, so it cannot be combined with `--watch`.

## Start, pause, or toggle tracking

Choose the command for the state change you want; these commands act on the shared runtime:

```powershell
pwsh -NoProfile -Command '& worktrail.exe -cli tracking start --format plain; exit $LASTEXITCODE'
pwsh -NoProfile -Command '& worktrail.exe -cli tracking pause --format plain; exit $LASTEXITCODE'
pwsh -NoProfile -Command '& worktrail.exe -cli tracking toggle --format plain; exit $LASTEXITCODE'
```

Each successful command returns the resulting dashboard state. `toggle` switches from the current state, so use `start` or `pause` when a script needs a specific outcome. `--start`, `--pause`, and `--toggle` are equivalent shortcuts after `-cli`.

## Check AI setup

```powershell
pwsh -NoProfile -Command '& worktrail.exe -cli ai status --format plain; exit $LASTEXITCODE'
```

See whether AI is on, which provider and model are selected, and whether the key
and cost allowance are ready. This command shows no key value and sends no
analysis request.

## Retain a local screenshot

Screenshot capture follows the current AI provider setting. For a local-only example, the following script checks that AI provider features are disabled before capturing. If they are enabled, disable them in the app and rerun this example. Keep them disabled while the command runs. `--keep` retains the screenshot; it does not disable AI analysis.

```powershell
pwsh -NoProfile -Command '
    $providerJson = & worktrail.exe -cli ai status --json
    $providerExit = $LASTEXITCODE
    if ($providerExit -ne 0) {
        $providerJson
        exit $providerExit
    }
    $provider = ($providerJson -join "`n") | ConvertFrom-Json
    if ($provider.value.enabled) {
        throw "Disable AI provider features before running this local-only capture."
    }
    & worktrail.exe -cli screenshot capture --mode active-window --keep --format plain
    exit $LASTEXITCODE
'
```

Use a window containing synthetic content for a trial capture. The command returns capture details for the retained local screenshot when screenshot capture is enabled and privacy rules permit it. The active window is the foreground window at capture time, which may be the terminal. `--mode all-screens` selects all displays; omitting `--mode` uses the saved setting.

Inspect the latest retained screenshot's details:

```powershell
pwsh -NoProfile -Command '& worktrail.exe -cli screenshot latest --format plain; exit $LASTEXITCODE'
```

## Preview retention before deciding on deletion

Read the configured retention periods, then inspect the candidates:

```powershell
pwsh -NoProfile -Command '& worktrail.exe -cli retention status --format plain; exit $LASTEXITCODE'
pwsh -NoProfile -Command '& worktrail.exe -cli retention preview --json; exit $LASTEXITCODE'
```

The preview deletes nothing. Read its fields as follows:

- `fileCount` counts both expired screenshots and database records, including
  activity and AI records.
- `totalBytes` estimates the affected content, not the disk space you will regain.
- `paths` lists affected screenshots and, where relevant, the activity database.
  Cleanup removes expired records, not the entire database.

Review both retention periods before running cleanup.

Use command help before confirming deletion with `retention run --yes`:

```powershell
pwsh -NoProfile -Command '& worktrail.exe -cli retention --help --format plain; exit $LASTEXITCODE'
```

Calling `retention run` without confirmation fails with `retention.confirmation.required` and exit code `3`. A preview does not freeze the candidate set; a confirmed run evaluates retention again.

## Output formats, locale, and timeout

| Option | Behavior |
| --- | --- |
| `--format rich` | Spectre.Console panels and tables for an interactive terminal. This is the default when output is not redirected. |
| `--format plain` | A localized result line and, when present, an indented JSON representation of its value, without ANSI controls. This is the default when output/error is redirected. |
| `--format json` or `--json` | One JSON result document suitable for a script. |
| `--quiet` | Suppresses successful human-readable results. JSON still emits its result document. |
| `--language it-IT` | Selects the human-readable language. The default is `system`. |
| `--timeout 15` | Sets the default timeout for each shared-runtime request in seconds; accepted values are `1` through `300`, with default `5`. Background-host discovery uses a separate retry window capped at five seconds. This is not a total-duration limit for a multi-request workflow. |

For Italian plain-text status with a longer timeout:

```powershell
pwsh -NoProfile -Command '& worktrail.exe -cli status --format plain --language it-IT --timeout 15; exit $LASTEXITCODE'
```

Supported locale choices are `system`, `en-US`, `it-IT`, `fr-FR`, `de-DE`, `es-ES`, `zh-Hans`, `vi-VN`, `ko-KR`, `pt-PT`, and `pt-BR`. Use the full supported tags; values such as `en` or `pt` are rejected.

For automation, check the process result before parsing successful output:

```powershell
pwsh -NoProfile -Command '
    $json = & worktrail.exe -cli status --json --language en-US --timeout 15
    $cliExit = $LASTEXITCODE
    if ($cliExit -ne 0) {
        $json
        exit $cliExit
    }
    $result = ($json -join "`n") | ConvertFrom-Json
    [pscustomobject]@{
        Code = $result.code
        IsTracking = $result.value.isTracking
    }
    exit 0
'
```

The JSON envelope has stable field names: `succeeded`, `code`, `messageKey`, `value`, and `issues`. Each validation issue has `field`, `code`, and `messageKey`. Locale selection does not rename these fields or stable result codes; human-facing strings inside values can still be localized. Branch on the exit code and `code`, not translated messages. Invalid global options can fail before JSON rendering and write a diagnostic to standard error instead.

| Exit code | Meaning |
| --- | --- |
| `0` | Success. |
| `2` | Unknown command, invalid command arguments, or invalid global options. |
| `3` | Validation or required-value/confirmation failure; includes `ai.configuration.invalid`. |
| `4` | Shared runtime unavailable. |
| `5` | Privacy rules blocked the operation. |
| `6` | AI provider features disabled (`ai.disabled`). |
| `7` | AI cost guardrail blocked the operation. |
| `8` | An operation returned a failure code ending in `.failed`. |
| `9` | Unsupported runtime IPC protocol. |
| `10` | Other application failure, including partial diagnostics. |
| `11` | CLI requires Premium (`cli.premium.required`); the requested command is not executed. |
| `130` | Command cancellation, including stopping a watch with Ctrl+C. |

## Discover additional commands

```powershell
pwsh -NoProfile -Command '& worktrail.exe -cli --help --format plain; exit $LASTEXITCODE'
pwsh -NoProfile -Command '& worktrail.exe -cli /ai --help --format plain; exit $LASTEXITCODE'
pwsh -NoProfile -Command '& worktrail.exe -cli --version --json; exit $LASTEXITCODE'
```

The leading slash is optional: `/status` and `status` select the same command. Omitting a command opens the interactive command center when rich output and interactive input are available.
