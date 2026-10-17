# Internal screenshot capture rules

`WorkTrail.Core/Data/screenshot-capture-rules.json` is embedded in Core. Maintainers edit this file and rebuild the app; there is no settings editor or external rules file. Rules select pixels and destinations, not AI response length.

Each item in `rules` has these fields:

| Field | Accepted values |
| --- | --- |
| `id` | Unique nonempty identifier |
| `enabled` | Boolean |
| `subject` | `active-window`, `secondary-windows`, `active-monitor`, `secondary-monitors` |
| `capture` | `window` for window subjects; `monitor` for monitor subjects |
| `priority` | `primary`, `secondary`, `context`, in that order |
| `destination` | `analysis`, `gallery`, `both` |
| `processNames` | Optional window-only list of exact process names, without `.exe`; case-insensitive, no wildcard or regex |
| `requireSecondaryMatch` | For secondary-window rules only: require an eligible application or context profile |
| `minimumVisibleFraction` | Number from 0 to 1; visible unoccluded pixels divided by the window area intersecting the displays |

An omitted or empty process list matches all processes. Unknown properties, invalid field combinations and duplicate IDs fail validation. A plan selecting no visible image fails explicitly.

The defaults send the active crop and eligible secondary crops to AI and retain them in the gallery. Secondary crops need an eligible profile and at least 15% of their on-screen area visible. Unknown and low-attention background windows do not get separate crops by default. Monitor panoramas still go to the gallery only, including incidental visible windows. The user-facing active-window capture mode restricts the rules to `active-window`; it never expands capture to other subjects.

Rules are additive. Priority determines image order; equal priorities preserve rule order and desktop window order. Matching the same image through several rules unions its destinations and keeps its highest priority. Identical window and monitor pixels share one image while retaining the window identity. Different window identities are not merged.

Window crops contain only visible desktop regions. Higher windows occlude lower windows even when they do not match a rule. Occluded areas and gaps between monitors are black; completely occluded windows are skipped. Shell and helper windows do not become secondary application targets, but still occlude other targets. A spanning window remains one image with its related monitor IDs. Monitor numbers are preserved rather than renumbered by priority.

Rules do not override capture privacy checks. Capture validates the desktop scene around pixel acquisition; a changed scene is rejected rather than retaining misleading visibility masks. Panoramas contain the visible monitor, so a process filter on a window rule is not a privacy exclusion for a separate monitor rule. Privacy exclusions remain the authority for both destinations.

## Automatic foreground stability

Before scheduled acquisition, `stability` waits for the foreground handle, process,
title and bounds to stay unchanged. Defaults are `enabled: true`,
`settleMilliseconds: 1000`, `maximumWaitMilliseconds: 3000`, `pollMilliseconds: 100`,
`retrySeconds: 3` and `maxRetries: 2`. This bounded metadata-only wait runs on the
capture worker, not the UI. Manual requests bypass the wait; privacy checks still apply.

Returning from A to B to A restarts the quiet period. The capture timestamp is taken
after settling. A scene change detected during acquisition discards partial files
and becomes a quiet automatic retry; manual acquisition still reports the failure.
No continuous polling, new tracking runtime or window-event capture trigger is added.

The scheduler allows at most two short retries per regular due capture, then returns
to the configured interval. It never replaces a newer schedule or disabled/paused
tracking state with a stale retry. An unchanged image or input-idle skip uses the
regular interval, not the short retry. Native idle-probe errors remain quiet skips;
only a detected foreground transition is classified as transient instability.

Timing validation accepts 100–5000 ms to settle, a maximum wait at least as large and
at most 10000 ms, polling from 25–1000 ms no slower than the settle interval,
1–60 seconds between retries, and 0–5 retries. Zero retries disables fast retry;
`enabled: false` disables the initial wait, not the post-acquisition scene safety check.

## Automatic near-duplicate filtering

The `similarity` object controls a local, in-memory RGB thumbnail comparison. It runs
after pixel acquisition and privacy/scene checks, but before WEBP encoding, disk writes
or AI submission. No OCR or model is involved. Manual captures bypass it.

| Field | Default | Meaning |
| --- | --- | --- |
| `enabled` | `true` | Enable the comparison for scheduled captures |
| `sampleWidth`, `sampleHeight` | `128`, `96` | Thumbnail dimensions, each 16–512 |
| `maxMeanDifference` | `0.25` | Maximum mean absolute RGB channel difference, 0–255 |
| `maxChangedPixelFraction` | `0.0001` | Maximum fraction of sampled pixels above the channel threshold |
| `pixelDifferenceThreshold` | `6` | A sampled pixel changes when any channel differs by more than this |
| `maxPixelDifference` | `24` | Keep an image when even one sampled channel exceeds this difference |
| `forceRefreshMinutes` | `15` | Keep the next eligible scheduled image at or after this age |
| `maxEntries` | `128` | Maximum remembered fingerprints; oldest retained entries are evicted |

All three difference limits must pass to skip an image. The baseline advances only
after successful publication of a retained gallery image, never after a skipped or
failed capture. Deleting its retained file invalidates that baseline. Transient
analysis-only images do not seed the cache. The cache is
process-local and starts empty after restart; no thumbnails are stored in the database.

An application profile or matching context rule can supply a complete `similarity`
object. Precedence is context, application, global; omitted fields inside an override
use the standard defaults, not a field-by-field merge. Spotify uses title-only collection
instead of a similarity preset; no Spotify image reaches this comparison.

Identity includes window handle, title, geometry, monitor, visible regions and rule
destinations. A matching identity is necessary, never sufficient: pixels are compared
too. Monitor panoramas use the global conservative settings and their visible scene
identity. Sampling is lossy; tiny text edits can be missed,
so these defaults need native validation and sensitive profiles can disable filtering.

Images are filtered independently. Kept images retain their actual monitor IDs; old
images are never relabeled with the new timestamp. If every image is unchanged, the
scheduled attempt ends quietly without files, a notification or an AI request. A
partially changed pass submits only its newly captured eligible images.

## Application attention

`applicationProfiles` contains `id`, `label`, `processNames`, `activeAttention`, `backgroundAttention` and optional `captureSecondary`, `idleTimeoutMinutes` and `microPromptIds`. Attention accepts `high`, `normal` or `low`. Process aliases use exact case-insensitive matching; duplicate aliases across profiles are rejected. Unrecognized foreground processes get normal attention. If `captureSecondary` is omitted, a known profile is eligible unless its background attention is low. Rule priority controls image ordering; attention controls reading depth.

These are editable judgment presets, not measurements of productivity or evidence that an application is installed. Process aliases are best effort and can change between application editions. Low-attention foreground images remain eligible, but the prompt requests only the title and brief top-level state, not body content. Manual capture intent forces Complete/high attention and bypasses the title-only micro-prompt for that request, without changing the saved preference or overriding privacy/blacklist rules.

High attention when active, normally normal in the background:

- Original examples: Word, Excel, Teams.
- Office, Visual Studio, VS Code/Insiders, Notepad/Notepad++, Obsidian, Notion, Slack, Zoom, Acrobat, Figma, Photoshop, Adobe Audition, Illustrator, AutoCAD, Blender and DBeaver.
- Windows Terminal (including WSL sessions), Command Prompt (`cmd`), console hosts, PuTTY, mintty, WezTerm, Alacritty, Tabby and MobaXterm. Hosted command prompts use the existing terminal/console-host profile when that host owns the visible window; no shell detection is inferred from the host process.
- SSMS, Oracle SQL Developer, DataGrip, pgAdmin, MySQL Workbench and HeidiSQL; JetBrains IDEs, Android Studio, Sublime Text, Postman and Power BI Desktop.
- Browser processes default to normal attention and no separate secondary crop. Process-scoped title hints can select a visible work page; this is not verified URL/domain recognition.

Low attention both active and in the background:

- Original examples: File Explorer, Windows Settings. Spotify is title-only, not a low-attention image.
- Twenty additional presets: Calculator, Clock, Photos, Media Player, VLC, Windows Media Player, iTunes, Apple Music, Steam, Epic Games Launcher, GOG Galaxy, Battle.net, Ubisoft Connect, Xbox, Discord, Telegram, WhatsApp, Phone Link, OneDrive, Dropbox.

## Context rules and micro-prompts

`contextRules` require `id`, `enabled`, integer `priority`, exact `processNames` and `titleContainsAny`. Process and title groups are ANDed; each list matches any entry, case-insensitively. There are no regular expressions or unbounded automation-tree scans. Optional overrides are `activeAttention`, `backgroundAttention`, `captureSecondary`, `idleTimeoutMinutes` and `microPromptIds`.

The highest-priority matching context wins; ties use ordinal ID order, so reordering JSON does not change a tie. Unspecified fields inherit the application profile. A context micro-prompt list replaces the profile list; include both IDs when both are needed. Disabled detail collectors prevent title matching for that process. Generic Java hosts match Oracle SQL Developer only with the configured product-title hint, never solely because a Java process exists.

Shipped hints cover IDE/debugger windows, SQL query editors, terminal status, Teams/Zoom pop-outs, and browser titles mentioning code review, work-item tracking, documents, diagrams or dashboards. News-title hints request title-only attention; they are not a privacy blacklist. Language, renamed titles and app versions can prevent a match or produce false positives. Browser hints do not inspect inactive tabs, retrieve pages or prove domain identity. Unknown pages stay normal; do not infer work/leisure from a browser process.

`microPrompts` is the internal ID-to-instruction dictionary. Profiles select small reusable blocks such as `sql`, `debugger`, `terminal`, `code-review`, `work-items`, `dashboard` or `titles-only`. The prompt renderer attaches only relevant blocks to the correct submitted image number. Saved/imported metadata carries IDs, not executable instructions; unknown IDs are ignored. Gallery-only or unattached images do not add blocks. Reanalysis uses the current internal dictionary for the recorded IDs.

Notepad adds `text-editor` guidance for substantive text, notes and configuration.
Photoshop adds `image-editing` guidance for the canvas, layers, masks and visible tools.
Adobe Audition adds `audio-editing` guidance for sessions, tracks, selections and effects;
waveforms do not reveal speech or audible content. Android Studio combines `code`
and `android-development` for source, UI previews, Gradle, Logcat and visible debugging
state. These profiles retain normal background attention and secondary-crop eligibility,
without extended idle timeouts or assumptions that a save, export or build succeeded.

`RuleReason` records `app-profile:<id>`, `title-hint:<id>`, `default` or `capture-rule`, and `MicroPromptIds` records routing IDs. Both travel with image metadata in database/archive roundtrips. They are selection provenance, not activity classification. A debugger hint must still be confirmed by readable image evidence; a SQL editor is not proof that a query ran. These deterministic decisions work without AI. Semantic descriptions still require the configured AI connection.

## Scheduled inactivity policy

`defaultIdleTimeoutSeconds` defaults to 60. Each application or context can override it with `idleTimeoutMinutes` (1 to 120). The shipped Teams profile uses 10 minutes and Zoom uses 15. These are total times since the last Windows user input, not extra active minutes. The profile must match the current foreground context; a background meeting app alone cannot keep captures alive indefinitely.

Scheduled captures check the fresh foreground/input state at the native pixel boundary. At or beyond the timeout, the attempt is quietly skipped until a later configured interval. Missing or changing native context fails closed and is logged. Captures/analysis do not renew the budget. Capture cadence, pause, active-hours and privacy rules remain authoritative. This is not a video stream or call detector; a 15-minute capture cadence is not made more frequent by a 10-minute inactivity tolerance. Activity samples and report accounting are unchanged. Manual captures bypass the inactivity gate.

## Sources and limits

These presets are engineering choices, informed by documented capabilities rather than claims of usage popularity:

- [Teams pop-out meeting content](https://support.microsoft.com/en-us/teams/meetings/multitask-during-a-microsoft-teams-meeting), [Zoom dual-window mode](https://support.zoom.com/hc/en/article?id=zm_kb&sysparm_article=KB0064500).
- [VS Code custom layouts](https://code.visualstudio.com/docs/configure/custom-layout), [Visual Studio floating windows](https://learn.microsoft.com/en-us/visualstudio/ide/customizing-window-layouts-in-visual-studio?view=visualstudio).
- [Windows Terminal versus shell](https://learn.microsoft.com/en-us/windows/terminal/faq), [customizable terminal titles](https://learn.microsoft.com/en-us/windows/terminal/tutorials/tab-title).
- [Oracle SQL Developer Windows launch](https://www.oracle.com/database/technologies/appdev/sqldev/sqldev-install-windows.html), [DataGrip command-line executable](https://www.jetbrains.com/help/datagrip/opening-files-from-command-line.html).
- [Adobe Audition Windows executable](https://helpx.adobe.com/audition/desktop/introduction/known-issues.html) documents `Adobe Audition.exe`; [Android Studio installation](https://developer.android.com/studio/install) documents `studio64.exe`. Profiles use these process names without the extension; shared Java hosts and Adobe helper processes are not matched.
- [Chrome URL and title are separate fields](https://developer.chrome.com/docs/extensions/reference/api/tabs); this implementation has no browser extension or verified-domain collector.
- [OpenAI prompt engineering](https://developers.openai.com/api/docs/guides/prompt-engineering) informs the separation of local instructions and untrusted visual context.

## Title-only applications

`titleOnlyProcesses` lists exact case-insensitive process aliases without `.exe`.
The shipped list contains `Spotify`. These applications never receive an image crop.
Their visible regions are subtracted from every monitor panorama before pixels are
copied, leaving black areas rather than capturing content behind the excluded window.
Covered title-only windows do not hide the unrelated windows above them. Monitor IDs
and the other eligible crops are preserved, including across multiple monitors.

This applies to scheduled and manual capture, regardless of priority, destination or
Complete analysis. If nothing remains, automatic acquisition skips quietly; manual
acquisition reports that the selected applications only allow window titles. No image,
AI request, preview or empty day folder is produced for that attempt.

The existing foreground activity sampler keeps only the literal window title and
process identity as application context, bypassing richer context providers. It uses
the existing `window_title`, context and attributes fields, so their existing storage
and export paths need no schema change. Privacy exclusions and disabled detail
collectors still take precedence. This requires tracking and a foreground sample:
it is not a background playback history, audio capture, or an artist/track parser.

Title-only app details are not attributed to another application's screenshot or
included in its AI image context. Their title changes do not invalidate a masked
panorama's duplicate key; geometry changes still do. The list matches native process
names, not browser tabs, websites, taskbar thumbnails or content mirrored by another
application. Existing retained history is not rewritten or deleted.

## Analysis blacklist

`analysisBlacklist` is a separate list of exact process aliases. Unlike low attention, this is an absolute analysis veto applied after matching rules have merged their destinations. A matching window never goes to AI. A monitor panorama containing visible pixels from a blacklisted window also loses its analysis destination, preventing a panorama from bypassing the window veto. Fully covered blacklisted windows do not block unrelated visible content. Occlusion-masked crops do not contain the blocked overlay and can still be analyzed.

The gallery destination remains allowed by this blacklist. Use `titleOnlyProcesses` to retain foreground titles without images, or privacy exclusions to reject the context entirely. Analysis-only targets that become blocked are discarded from the plan. The shipped blacklist contains `SearchHost`, `SearchApp`, `StartMenuExperienceHost` and `LockApp`. Generic hosts such as `ApplicationFrameHost` and `msedgewebview2` are deliberately not blacklisted because unrelated applications share them.
