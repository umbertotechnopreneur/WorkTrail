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

An omitted or empty process list matches all processes. Unknown properties, invalid field combinations and duplicate IDs fail validation. A plan selecting no visible image fails explicitly.

The defaults send visible active and secondary application-window crops to AI and retain them in the gallery. Monitor panoramas go to the gallery only. The user-facing active-window capture mode restricts the rules to `active-window`; it never expands capture to other subjects.

Rules are additive. Priority determines image order; equal priorities preserve rule order and desktop window order. Matching the same image through several rules unions its destinations and keeps its highest priority. Identical window and monitor pixels share one image while retaining the window identity. Different window identities are not merged.

Window crops contain only visible desktop regions. Higher windows occlude lower windows even when they do not match a rule. Occluded areas and gaps between monitors are black; completely occluded windows are skipped. Shell and helper windows do not become secondary application targets, but still occlude other targets. A spanning window remains one image with its related monitor IDs. Monitor numbers are preserved rather than renumbered by priority.

Rules do not override capture privacy checks. Capture validates the desktop scene around pixel acquisition; a changed scene is rejected rather than retaining misleading visibility masks. Panoramas contain the visible monitor, so a process filter on a window rule is not a privacy exclusion for a separate monitor rule. Privacy exclusions remain the authority for both destinations.

## Application attention

`applicationProfiles` is an internal list of `{ id, label, processNames, activeAttention, backgroundAttention }` records. Attention accepts `high`, `normal` or `low`. Process aliases use exact case-insensitive matching; duplicate aliases across profiles are rejected. Unrecognized processes get normal attention. Rule priority still controls image ordering; attention tells the AI how much emphasis to give the actual application content.

These are editable judgment presets, not a measurement of productivity or evidence that an application is installed. Process aliases are best effort and can change between application editions. Low attention does not remove an image from AI input and must not suppress meaningful errors, questions or other relevant visible content.

High attention when active, normal in the background:

- Original examples: Word, Excel, Teams.
- Twenty additional presets: PowerPoint, Outlook, OneNote, Visual Studio, Visual Studio Code, Notepad++, Obsidian, Notion, Slack, Zoom, Acrobat, Microsoft Edge, Google Chrome, Firefox, Figma, Photoshop, Illustrator, AutoCAD, Blender, DBeaver.

Low attention both active and in the background:

- Original examples: Spotify, File Explorer, Windows Settings.
- Twenty additional presets: Calculator, Clock, Photos, Media Player, VLC, Windows Media Player, iTunes, Apple Music, Steam, Epic Games Launcher, GOG Galaxy, Battle.net, Ubisoft Connect, Xbox, Discord, Telegram, WhatsApp, Phone Link, OneDrive, Dropbox.

## Analysis blacklist

`analysisBlacklist` is a separate list of exact process aliases. Unlike low attention, this is an absolute analysis veto applied after matching rules have merged their destinations. A matching window never goes to AI. A monitor panorama containing visible pixels from a blacklisted window also loses its analysis destination, preventing a panorama from bypassing the window veto. Fully covered blacklisted windows do not block unrelated visible content. Occlusion-masked crops do not contain the blocked overlay and can still be analyzed.

The gallery destination remains allowed by this blacklist; use privacy exclusions when content must not be captured or retained at all. Analysis-only targets that become blocked are discarded from the plan. The shipped blacklist contains `SearchHost`, `SearchApp`, `StartMenuExperienceHost` and `LockApp`. Generic hosts such as `ApplicationFrameHost` and `msedgewebview2` are deliberately not blacklisted because unrelated applications share them.
