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

## Application attention

`applicationProfiles` contains `id`, `label`, `processNames`, `activeAttention`, `backgroundAttention` and optional `captureSecondary`, `idleTimeoutMinutes` and `microPromptIds`. Attention accepts `high`, `normal` or `low`. Process aliases use exact case-insensitive matching; duplicate aliases across profiles are rejected. Unrecognized foreground processes get normal attention. If `captureSecondary` is omitted, a known profile is eligible unless its background attention is low. Rule priority controls image ordering; attention controls reading depth.

These are editable judgment presets, not measurements of productivity or evidence that an application is installed. Process aliases are best effort and can change between application editions. Low-attention foreground images remain eligible, but the prompt requests only the title and brief top-level state, not body content. Manual capture intent forces Complete/high attention and bypasses the title-only micro-prompt for that request, without changing the saved preference or overriding privacy/blacklist rules.

High attention when active, normally normal in the background:

- Original examples: Word, Excel, Teams.
- Office, Visual Studio, VS Code/Insiders, Notepad/Notepad++, Obsidian, Notion, Slack, Zoom, Acrobat, Figma, Photoshop, Illustrator, AutoCAD, Blender and DBeaver.
- Windows Terminal (including WSL sessions), console hosts, PuTTY, mintty, WezTerm, Alacritty, Tabby and MobaXterm.
- SSMS, Oracle SQL Developer, DataGrip, pgAdmin, MySQL Workbench and HeidiSQL; JetBrains IDEs, Sublime Text, Postman and Power BI Desktop.
- Browser processes default to normal attention and no separate secondary crop. Process-scoped title hints can select a visible work page; this is not verified URL/domain recognition.

Low attention both active and in the background:

- Original examples: Spotify, File Explorer, Windows Settings.
- Twenty additional presets: Calculator, Clock, Photos, Media Player, VLC, Windows Media Player, iTunes, Apple Music, Steam, Epic Games Launcher, GOG Galaxy, Battle.net, Ubisoft Connect, Xbox, Discord, Telegram, WhatsApp, Phone Link, OneDrive, Dropbox.

## Context rules and micro-prompts

`contextRules` require `id`, `enabled`, integer `priority`, exact `processNames` and `titleContainsAny`. Process and title groups are ANDed; each list matches any entry, case-insensitively. There are no regular expressions or unbounded automation-tree scans. Optional overrides are `activeAttention`, `backgroundAttention`, `captureSecondary`, `idleTimeoutMinutes` and `microPromptIds`.

The highest-priority matching context wins; ties use ordinal ID order, so reordering JSON does not change a tie. Unspecified fields inherit the application profile. A context micro-prompt list replaces the profile list; include both IDs when both are needed. Disabled detail collectors prevent title matching for that process. Generic Java hosts match Oracle SQL Developer only with the configured product-title hint, never solely because a Java process exists.

Shipped hints cover IDE/debugger windows, SQL query editors, terminal status, Teams/Zoom pop-outs, and browser titles mentioning code review, work-item tracking, documents, diagrams or dashboards. News-title hints request title-only attention; they are not a privacy blacklist. Language, renamed titles and app versions can prevent a match or produce false positives. Browser hints do not inspect inactive tabs, retrieve pages or prove domain identity. Unknown pages stay normal; do not infer work/leisure from a browser process.

`microPrompts` is the internal ID-to-instruction dictionary. Profiles select small reusable blocks such as `sql`, `debugger`, `terminal`, `code-review`, `work-items`, `dashboard` or `titles-only`. The prompt renderer attaches only relevant blocks to the correct submitted image number. Saved/imported metadata carries IDs, not executable instructions; unknown IDs are ignored. Gallery-only or unattached images do not add blocks. Reanalysis uses the current internal dictionary for the recorded IDs.

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
- [Chrome URL and title are separate fields](https://developer.chrome.com/docs/extensions/reference/api/tabs); this implementation has no browser extension or verified-domain collector.
- [OpenAI prompt engineering](https://developers.openai.com/api/docs/guides/prompt-engineering) informs the separation of local instructions and untrusted visual context.

## Analysis blacklist

`analysisBlacklist` is a separate list of exact process aliases. Unlike low attention, this is an absolute analysis veto applied after matching rules have merged their destinations. A matching window never goes to AI. A monitor panorama containing visible pixels from a blacklisted window also loses its analysis destination, preventing a panorama from bypassing the window veto. Fully covered blacklisted windows do not block unrelated visible content. Occlusion-masked crops do not contain the blocked overlay and can still be analyzed.

The gallery destination remains allowed by this blacklist; use privacy exclusions when content must not be captured or retained at all. Analysis-only targets that become blocked are discarded from the plan. The shipped blacklist contains `SearchHost`, `SearchApp`, `StartMenuExperienceHost` and `LockApp`. Generic hosts such as `ApplicationFrameHost` and `msedgewebview2` are deliberately not blacklisted because unrelated applications share them.
