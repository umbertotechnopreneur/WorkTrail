# WorkTrail — See time zones and availability at a glance

[![Build status](https://github.com/umbertotechnopreneur/WorkTrail/actions/workflows/build.yml/badge.svg?branch=main)](https://github.com/umbertotechnopreneur/WorkTrail/actions/workflows/build.yml)
![Platform: Windows 11](https://img.shields.io/badge/platform-Windows_11-0078D4)
![Status: pre-production](https://img.shields.io/badge/status-pre--production-F9665B)
[![License: MIT](https://img.shields.io/badge/license-MIT-2EA44F)](LICENSE)

I work with people in different time zones. Converting one hour at a time does not tell me when our working days overlap, or whether an appointment falls on someone else's rest day. I built WorkTrail around a visual availability grid so I can compare the same moment across cities and choose a meeting time at a glance.

Choose your home city, open **World availability**, and scroll across five days around the date you select. Compare local times, working hours, reserve margins, holidays and personal exceptions in one grid. Use it to plan meetings and appointments across time zones, then export the selected 16 or 24 hours to a PDF with selectable text and vector timetable cells.

WorkTrail uses native **WinUI 3** windows. Keep the planner, world clocks, calendars, Moon and sky views open as independent desktop widgets. Windows remember their placement and can snap to screen edges or other WorkTrail windows. Hide the launcher in the notification area while your widgets stay open. I keep memory use, performance and resource cleanup in view as I develop; the source is open for inspection.

**[Get started](#get-worktrail)** · [Desktop tools](docs/CELESTIAL_DESKTOP.md) · [Privacy](docs/PRIVACY.md) · [Build guide](docs/DEVELOPMENT.md)

<p align="center">
  <img src="docs/images/readme/trackmeup-world-clocks-it.png" alt="WorkTrail world clocks in Italian" width="100%" />
</p>

*Actual world-clock window from an earlier build, shown in Italian. The new launcher is not pictured. See the [screenshot provenance](docs/images/readme/celestial-screenshots-PROVENANCE.md).*

## Get WorkTrail

WorkTrail targets **Windows 11 x64 and ARM64**. The Microsoft Store channel is separate from the [GitHub Releases](https://github.com/umbertotechnopreneur/WorkTrail/releases) channel. GitHub release downloads use **portable ZIPs**: extract the complete archive and run `WorkTrail.exe`. Keep its DLLs and resource folders together. Linux and macOS are source-only, without binaries or compatibility promises.

The next portable release is being prepared. Its publication and download availability are separate from the source features described here.

To work on the source, install PowerShell 7, the .NET 10 SDK and the Windows build tools described in the [development guide](docs/DEVELOPMENT.md). Start with:

```powershell
git clone https://github.com/umbertotechnopreneur/WorkTrail.git WorkTrail
Set-Location .\WorkTrail
pwsh -NoProfile -File ./scripts/Install-GitHooks.ps1
```

On the first import of an external dependency, scan its archive and extracted files with Microsoft Defender before use. Reuse the evidence for the same unchanged import; see [AGENTS.md](AGENTS.md).

## Desktop tools

- Compare availability across cities on a shared timeline, with 15-, 30- or 60-minute intervals and 16- or 24-hour views.
- Convert a local date and time across saved cities, with their local dates and UTC offsets.
- Save working hours, weekly rest days, reserve margins and personal day exceptions. Missing holiday coverage is shown explicitly.
- Export the planner to A4 landscape PDF with repeated city headers, selectable text, vector cells and embedded skyline artwork.
- Open the astronomical agenda with local solar and lunar events, national holidays and saints.
- Inspect the local sky, lunar phase, day/night map and globe in separate windows.
- Keep window placement, snapping, theme, language and Windows startup preferences.
- Configure and verify an optional OpenWeather API key from launcher Settings, world-clock options or the planner. Current weather remains separate from the selected planning date.

Weather, satellite data and space-weather information use external sources when requested by their views; details are in the [privacy notes](docs/PRIVACY.md). The planner helps you choose meeting and appointment times; it does not create or synchronize events in an external calendar.

## ExcelPlanner

[`ExcelPlanner/`](ExcelPlanner/README.md) remains a separate availability-planning prototype. It includes the workbook, calendar CSVs, support scripts and source credits. It is not part of the app build or launcher.

## Build and contribute

The Windows views call `IWorkTrailApplication`. `WorkTrail.Core` owns settings, astronomy and Windows integration; `WorkTrail.Presentation` contains passive projection and window-state helpers. See [architecture](docs/ARCHITECTURE.md), [development](docs/DEVELOPMENT.md), [contributing](CONTRIBUTING.md) and [licenses](THIRD_PARTY_NOTICES.md).

Project-authored code and documentation use the [MIT License](LICENSE). The WorkTrail name and brand assets follow [TRADEMARKS.md](TRADEMARKS.md).

## More from MeUp

- [AgentInbox](https://github.com/umbertotechnopreneur/AgentInbox): connect your inboxes to your AI assistant.
- [PromptMeUp](https://github.com/umbertotechnopreneur/PromptMeUp): Can't remember that command? Git, Bash, or PowerShell: describe what you want to do. hm is here to help.
- [WorkTrail](https://github.com/umbertotechnopreneur/WorkTrail): compare time zones and availability at a glance, with independent desktop widgets.

I’m Umberto Giacobbi. I built and maintain WorkTrail, with help from contributors. Find me on [LinkedIn](https://www.linkedin.com/in/umbertogiacobbi/) or at [umbertogiacobbi.biz](https://umbertogiacobbi.biz/).

---

### What is VibeWare?

I coined the term [VibeWare](https://umbertogiacobbi.biz) and started an open initiative for developers who build with AI and care about the craft. I challenge the assumption that vibe coding is synonymous with low-quality code, while openly acknowledging the weaknesses and risks of AI-generated software: experienced developers must guide the process, test carefully, check security and take responsibility for the result. With this footer, I acknowledge that I developed this software with AI and ask you to judge the quality of the work.

[**Read the VibeWare manifesto**](https://umbertogiacobbi.biz/vibeware/manifesto)

*You are welcome to explore, adapt and reuse the open-source VibeWare materials under the project license — my contribution to the developer community.*

[Terms](https://umbertogiacobbi.biz/terms/) · [Privacy](https://umbertogiacobbi.biz/privacy/)

<p align="right">
  <a href="https://umbertogiacobbi.biz/vibeware/manifesto">
    <img src="https://raw.githubusercontent.com/umbertotechnopreneur/VibeWare/main/Branding/vibeware-logo.png" alt="VibeWare floppy logo" width="160">
  </a><br>
  <em>Human Intent, AI Execution, Plenty of Token</em>
</p>
