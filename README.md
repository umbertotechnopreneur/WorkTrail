# WorkTrail — Clocks, calendars and sky on your desktop

[![Build status](https://github.com/umbertotechnopreneur/WorkTrail/actions/workflows/build.yml/badge.svg?branch=main)](https://github.com/umbertotechnopreneur/WorkTrail/actions/workflows/build.yml)
![Platform: Windows 11](https://img.shields.io/badge/platform-Windows_11-0078D4)
![Status: pre-production](https://img.shields.io/badge/status-pre--production-F9665B)
[![License: MIT](https://img.shields.io/badge/license-MIT-2EA44F)](LICENSE)

I kept WorkTrail's clocks and sky windows open because they were useful. The tracking and report workflow had become more work than I wanted to manage. I removed that workflow and made the main window a launcher for the desktop tools I use.

Choose your home city on first launch, then open world clocks, an astronomical calendar, the local sky, the Moon, a day/night map or a globe. Each widget remembers its window position. Closing the launcher lets you keep the widgets running in the notification area or quit the app. Use the notification-area icon to reopen the launcher or show your open windows.

**[Get started](#get-worktrail)** · [Desktop tools](docs/CELESTIAL_DESKTOP.md) · [Privacy](docs/PRIVACY.md) · [Build guide](docs/DEVELOPMENT.md)

<p align="center">
  <img src="docs/images/readme/trackmeup-world-clocks-it.png" alt="WorkTrail world clocks in Italian" width="100%" />
</p>

*Actual world-clock window from an earlier build, shown in Italian. The new launcher is not pictured. See the [screenshot provenance](docs/images/readme/celestial-screenshots-PROVENANCE.md).*

## Get WorkTrail

WorkTrail is in development and has no public download yet. Windows distribution targets Windows 11 x64 and ARM64 through MSIX. Linux and macOS are source-only, without binaries or compatibility promises.

To work on the source, install PowerShell 7, the .NET 10 SDK and the Windows build tools described in the [development guide](docs/DEVELOPMENT.md). Start with:

```powershell
git clone https://github.com/umbertotechnopreneur/WorkTrail.git WorkTrail
Set-Location .\WorkTrail
pwsh -NoProfile -File ./scripts/Install-GitHooks.ps1
```

On the first import of an external dependency, scan its archive and extracted files with Microsoft Defender before use. Reuse the evidence for the same unchanged import; see [AGENTS.md](AGENTS.md).

## Desktop tools

- Compare saved cities and convert a local date and time across their time zones.
- Open the astronomical agenda with local solar and lunar events, national holidays and saints.
- Inspect the local sky, lunar phase, day/night map and globe in separate windows.
- Keep window placement, snapping, theme, language and Windows startup preferences.
- Enable optional weather in the clock window with an OpenWeather API key.

The launcher does not start activity tracking, keyboard/mouse hooks, screenshot capture, AI analysis, timesheets, reports or history search. Existing personal history and screenshot files are left in place. Weather, satellite data and space-weather information use external sources when requested by their views; details are in the [privacy notes](docs/PRIVACY.md).

## ExcelPlanner

[`ExcelPlanner/`](ExcelPlanner/README.md) remains a separate availability-planning prototype. It includes the workbook, calendar CSVs, support scripts and source credits. It is not part of the app build or launcher.

## Build and contribute

The Windows views call `IWorkTrailApplication`. `WorkTrail.Core` owns settings, astronomy and Windows integration; `WorkTrail.Presentation` contains passive projection and window-state helpers. See [architecture](docs/ARCHITECTURE.md), [development](docs/DEVELOPMENT.md), [contributing](CONTRIBUTING.md) and [licenses](THIRD_PARTY_NOTICES.md).

Project-authored code and documentation use the [MIT License](LICENSE). The WorkTrail name and brand assets follow [TRADEMARKS.md](TRADEMARKS.md).

## More from MeUp

- [AgentInbox](https://github.com/umbertotechnopreneur/AgentInbox): connect your inboxes to your AI assistant.
- [PromptMeUp](https://github.com/umbertotechnopreneur/PromptMeUp): Can't remember that command? Git, Bash, or PowerShell: describe what you want to do. hm is here to help.
- [WorkTrail](https://github.com/umbertotechnopreneur/WorkTrail): clocks, calendars and sky on your desktop.

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
