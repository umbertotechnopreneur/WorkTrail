# Contributing to WorkTrail

Want to fix a bug, improve a guide, or suggest something new? You're welcome to help.

WorkTrail is an open-source Windows app that keeps your history on your PC by default. It's still in development. Start with a small change and show how you checked it; that makes review easier for everyone.

## Before you start

- Read the [README](README.md) and [privacy guide](docs/PRIVACY.md).
- Read [AGENTS.md](AGENTS.md) for the repository's development rules.
- Search existing issues and pull requests before opening a new one.
- Keep credentials, API keys, tokens, personal data, and private local paths out of commits, logs, screenshots, and issue reports.
- If you've found a security problem, follow [SECURITY.md](SECURITY.md) and report it privately.
- Open an issue before making a large change to how the app works.

## Set up your copy

Use PowerShell 7. After cloning the repository, run this once to enable the formatting hook:

```powershell
pwsh -NoProfile -File ./scripts/Install-GitHooks.ps1
```

Before each commit, the hook fixes C# spacing with the same formatter used by
the automated checks. If you've staged a whole file, it also fixes your working
copy. If you've staged only part of a file, it leaves your working copy alone
and formats only the staged version. Your unstaged edits stay out of the commit.
If you stage a change to `.editorconfig`, the hook applies those rules to all
C# files in the Git index. If formatting fails, the commit stops; fix the error
before trying again.

C# sources use four spaces for indentation, LF line endings, a final newline,
and no trailing whitespace. Git checkout and the formatter share these rules so
local commits and CI validate the same text.

To format the current working files manually, or check them without editing:

```powershell
pwsh -NoProfile -File ./scripts/Format-Code.ps1
pwsh -NoProfile -File ./scripts/Format-Code.ps1 -Verify
```

These commands only check spacing and don't need a NuGet restore. The automated
checks run the same verification and won't rewrite commits you've pushed.

To restore packages, build, and run tests:

```powershell
pwsh -NoProfile -Command "dotnet restore .\WorkTrail.slnx"
pwsh -NoProfile -Command "dotnet build .\WorkTrail.slnx -p:Platform=x64 -warnaserror"
pwsh -NoProfile -Command "dotnet test .\WorkTrail.slnx -p:Platform=x64 -warnaserror"
```

Or use the repository script:

```powershell
pwsh -NoProfile -File .\scripts\WorkTrail.ps1 -Action Preflight
pwsh -NoProfile -File .\scripts\WorkTrail.ps1 -Action Build -Platform x64 -WarnAsError
pwsh -NoProfile -File .\scripts\WorkTrail.ps1 -Action Test -Platform x64 -WarnAsError
```

## A few development rules

- Keep app behavior in `WorkTrail.Core` services. The UI and CLI should collect input, display results, and call `IWorkTrailApplication`.
- Report invalid input and unsupported states clearly. Don't silently try another path unless that behavior is documented.
- Remove replaced contracts instead of adding compatibility code, unless compatibility was explicitly requested.
- Use the existing shared tracker, mutex, and named pipe. Don't start a second tracker.
- Never pass secrets in command arguments or save them in settings, history, logs, or diagnostics.
- Start every first-party C# source file with `// SPDX-License-Identifier: MIT`; preserve original notices in generated or third-party files.
- Keep your change focused. Leave unrelated files and formatting alone.

## Opening a pull request

Tell me:

- what changed;
- why it changed;
- where the behavior is implemented;
- how you checked it;
- known limitations or follow-up work.

Keep each PR focused. Do not include build output, generated artifacts, or unrelated edits.

## If you use AI tools

You can use AI tools to help write, test, or improve a change. Make sure you understand the result, review it, and check that it's correct, secure, and properly licensed.

If AI played a substantial part, say what it helped with and how you reviewed and checked the result in your pull request. See the [AI contribution policy](AI_CONTRIBUTION_POLICY.md).

## License and Provenance

By submitting a contribution, you confirm that you have the right to provide
the material and agree to license it under the [MIT License](LICENSE), without
additional terms, unless the maintainers explicitly agree otherwise in
writing before acceptance. Your contribution must not contain confidential or
license-incompatible content.

Record third-party code, assets, and generated material in
`THIRD_PARTY_NOTICES.md` or companion provenance records. A proposed
WorkTrail mark or Brand Asset must include provenance and be explicitly
accepted under separate written terms. Accepting an ordinary MIT-licensed
contribution does not grant rights to the existing marks or assets described
in [`TRADEMARKS.md`](TRADEMARKS.md).
