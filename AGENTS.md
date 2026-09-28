# WorkTrail repository instructions

These instructions apply to every change in this repository.

## Current feature freeze: September 21-27, 2026

- Through September 27, 2026 inclusive, accept only maintenance, stability, performance, documentation corrections, and polish of existing UI. Do not add capabilities or workflows.
- If the owner requests a feature, remind them of the freeze and suggest deferring it. Proceed only after the owner explicitly grants an exception or ends the freeze.
- The freeze does not waive approval requirements for tests, builds, Git delivery, installation, or publication.

## Product and documentation

- Follow the [MeUp style guide](docs/assets/meup/README.md). Keep a common README structure and author signature across AgentInbox, PromptMeUp, and WorkTrail while giving each a distinct accent and concrete benefit.
- Lead README and original project copy with what WorkTrail does for the reader. Use plain English, short sentences, concrete actions, useful examples, and "you"; use "I", "me", and "my" for the solo maintainer, never company-style "we".
- Avoid filler, hype, vague promises, corporate language, and AI-sounding prose. Never promise unlimited capacity or untested compatibility. Preserve exact commands, UI labels, privacy facts, limitations, contributor credits, and the distinction between implemented, tested, and planned behavior.
- Preserve third-party quotations, licenses, attribution, and historical records. Keep UI strings separate from business logic and detailed validation procedures out of the product README.
- Keep roadmaps, design explorations, pricing, commercial strategy, launch plans, future proposals, OAuth/Store procedures, owner checkpoints, and raw Store exports outside Git in the owner's Obsidian product notes under `40_Business/MeUp/`. Before publication or Store-listing work, read `40_Business/MeUp/Business decisions.md` and the relevant product notes; approved wording is not evidence of implemented licensing.
- Keep shipped assets, real screenshots, provenance, current public policies and terms, licenses, attribution, build/contributor guidance, and files required by code or CI in the repository. Split mixed public/private documents.

## Distribution and delivery

- Commercial support and binaries target Windows 11 x64 and ARM64 and use MSIX only. Linux and macOS are source-only; do not provide binaries, installers, or compatibility promises for them.
- A requested build defaults to local Debug. Store packaging, upload, publication, release-pipeline changes, and tags each require explicit owner authorization. Keep Debug artifacts separate from Store artifacts; do not propose ZIP, standalone EXE, MSI, or alternate distribution without an explicit decision change.
- Never create a branch or worktree, commit, or push without explicit owner authorization. An edit request does not authorize Git delivery.
- For documentation or repository-instruction changes, stay on the current branch and keep changes local until delivery is requested; when authorized, include `[skip ci]` unless the owner requests CI.
- For other work, protect `main`: use an authorized focused branch, open a pull request assigned to `umbertotechnopreneur` with matching existing labels, resolve checks and conversations, squash-merge, then delete the branch. Never bypass protections or checks.
- Local MSIX build, signing, and installation require explicit authorization. GitHub Actions is optional for packaging. Create an annotated `v<version>` tag only when that source version is on `main` and the owner explicitly requests it.
- Preserve unrelated changes. Never commit credentials, tokens, private/local data, logs, generated artifacts, machine paths, `.env`, `bin/`, `obj/`, `artifacts/`, or `.vs/`.

## Scope, validation, and tooling

- Read no more than five files per task without explicit approval. Warn before broad reads, unbounded searches, or large output; start with scoped `rg`, reuse context, bound output, and expand only when required.
- Make the smallest complete change. Avoid unrelated refactors, cleanup, documentation churn, and speculative abstractions. Use subagents only when explicitly requested.
- Do not run tests or test suites without explicit approval; a code-change request is not approval. Ask again before rerunning tests. Keep required CI checks intact and ask before a push that triggers them.
- For C# work, enable the hook once per clone with `pwsh -NoProfile -File ./scripts/Install-GitHooks.ps1`. Before handoff, run `pwsh -NoProfile -File ./scripts/Format-Code.ps1` and its `-Verify` mode; do not bypass the hook.
- For documentation/instruction-only work, inspect only the scoped diff and staging; do not build, test, format, install hooks, or clean.
- Use PowerShell 7 only and invoke commands through `pwsh -NoProfile`. Prefer `pwsh -NoProfile -File <script.ps1>` and, for short commands, `pwsh -NoProfile -Command '<single-quoted command>'`; pass arguments explicitly instead of composing nested shell strings.
- Prefer parser checks and dry runs before destructive scripts. Run builds only for supported Windows SDK x64/ARM64 targets.
- Write generated MSIX payloads, manifests, build information, and local build state under ignored `artifacts/`; scripts must not overwrite tracked manifest templates or release-version inputs.
- If a task generated build/test output, clean it once after final use with the relevant `dotnet clean` (x64 by default). Preserve the newly validated installer; remove older packages only after verifying that their resolved paths are under this repository's `artifacts/`.

Authorized checks, when relevant:

```powershell
git status
dotnet restore .\WorkTrail\WorkTrail.csproj
dotnet build .\WorkTrail\WorkTrail.csproj -p:Platform=x64
```

## Engineering invariants

- This repository is pre-production. Do not add or retain backward-compatibility layers for superseded contracts, persisted artifacts, filenames, paths, settings, or APIs unless explicitly requested. Replace obsolete paths, adapters, and fallbacks; make migrations explicit and reject unsupported legacy input.
- Fail fast on invalid input, unsupported state, missing required configuration, and persistence or interop failures. Do not silently normalize, ignore, or fall back unless documented product behavior requires it.
- Keep WinUI views/code-behind, Spectre commands, prompts, and renderers passive: they collect input, bind/render DTOs, and invoke `IWorkTrailApplication`. They must not construct infrastructure services or perform I/O, process, registry, environment, HTTP, capture, hook, or persistence work.
- Put application behavior, persistence, OS interop, capture, environment access, HTTP, retention, and startup behind `WorkTrail.Core` application services. Serialize persistence mutations in the application layer.
- Do not create a second tracking runtime. Use the hashed-installation mutex and same-user versioned named-pipe protocol through the shared facade.
- Use the environment-variable secret flow. Never put secrets in CLI arguments, settings, history, logs, IPC diagnostics, or tests.
- User-facing shared AI features say "AI provider" ("provider AI" in Italian). Name a vendor only for selected-provider or genuinely vendor-specific behavior.
- Start first-party C# files with `// SPDX-License-Identifier: MIT`; preserve notices in generated and third-party files. Add XML documentation to public/protected methods and short comments for critical I/O, OS/process interop, external calls, and non-obvious failure behavior.
- Give every icon-only WinUI button or toggle a localized tooltip and the same localized accessible name.
- For Screenshot UI work, reuse components, keep data/business logic in models/services, avoid duplicate large titles and unnecessary card wrappers, and favor a translucent Mica/Acrylic presentation.

## Repository map and records

- `WorkTrail/`: Windows-first WinUI 3 app (`net10.0-windows10.0.19041.0`, x64/ARM64).
- `scripts/`: repository utilities. `.github/`: governance, workflows, Copilot context, and tasks.
- Track active work in `.github/tasks/todo.md` and completed work in `.github/tasks/archive.md`.
- Keep repository-wide rules here; `.github/copilot-instructions.md` is only a pointer and need not be reread when this file is already current in context.

## Licensing

- Project-authored code and documentation use the canonical MIT License in `LICENSE` unless a file says otherwise; do not add commercial-use or distribution restrictions.
- The WorkTrail name, logos, icons, and project-authored brand art are outside the MIT grant; follow `TRADEMARKS.md` and asset provenance records.
- Preserve third-party licenses and attribution in `THIRD_PARTY_NOTICES.md` and adjacent notices.
