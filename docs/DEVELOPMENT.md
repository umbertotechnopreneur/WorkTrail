# Windows contributor setup

Build WorkTrail on Windows and check your changes before sharing them. This path
covers an x64 app build and automated tests. You do not need an AI
provider account or a running installation.

Read [CONTRIBUTING.md](../CONTRIBUTING.md) and [AGENTS.md](../AGENTS.md) before
editing. Run commands from the repository root unless a step says otherwise.

## Prepare the Windows tools

| Component | Contributor requirement |
| --- | --- |
| Windows | Use an x64 Windows 11 development machine. The app's minimum OS and API targets are separate from the requirements of your development tools. |
| Git | Install Git for Windows and make `git` available on `PATH`. |
| PowerShell | Install PowerShell 7 and make `pwsh` available on `PATH`. Every repository PowerShell invocation must use `-NoProfile`. Windows PowerShell 5.1 is unsupported. |
| .NET SDK | Install a stable **10.0.4xx SDK**, as selected by [global.json](../global.json). A runtime alone is not enough. |
| Windows/WinUI tools | Use a compatible Visual Studio 2026 installation with **WinUI application development**, C#, and Windows SDK components. See Microsoft's [WinUI setup guide](https://learn.microsoft.com/en-us/windows/apps/get-started/start-here#set-up-your-development-environment). |
| Network access | Initial restore needs access to the configured NuGet feeds. No AI-provider account or API key is required for the build or automated tests. |

Restore the SDK package versions pinned in the [app project](../WorkTrail/WorkTrail.csproj).
Fix missing local tools rather than changing project targets to work around them.

Developer Mode is relevant to local package deployment/debugging. Configure it only when performing that separate validation, following the Microsoft WinUI setup guide.

## Clone and check the environment

From a folder where you keep source repositories:

```powershell
pwsh -NoProfile -Command 'git clone https://github.com/umbertotechnopreneur/TrackMeUp.git WorkTrail'
```

Open a terminal in the resulting `WorkTrail` folder, containing `WorkTrail.slnx` and `global.json`. Check the tools from that folder so .NET SDK selection uses the repository's settings:

```powershell
pwsh -NoProfile -Command 'git --version'
pwsh -NoProfile -Command '$PSVersionTable.PSVersion'
pwsh -NoProfile -Command 'dotnet --list-sdks'
pwsh -NoProfile -Command 'dotnet --version'
pwsh -NoProfile -File ./scripts/WorkTrail.ps1 -Action Preflight
```

Expect PowerShell 7 and the selected stable `10.0.4xx` SDK. Stop at a failed command and resolve it before continuing.

`Preflight` checks the basic tools and repository files. Check the version output
above too: it does not verify SDK selection, WinUI components, OCR
languages, or signing certificates.

Enable the repository hook once per clone:

```powershell
pwsh -NoProfile -File ./scripts/Install-GitHooks.ps1
```

An existing custom hook configuration causes an explicit error. Integrate the formatter with those hooks before changing their configuration; do not bypass the check.

## Validate a clean x64 checkout

Run this sequence once after setup, or once the changes being validated are complete. It uses the same Release-Unpackaged configuration and analyzer settings as the [release CI job](../.github/workflows/build.yml).

First check the source contracts:

```powershell
pwsh -NoProfile -File ./scripts/Test-SourceLicenseHeaders.ps1
pwsh -NoProfile -File ./scripts/Format-Code.ps1 -Verify
pwsh -NoProfile -File ./scripts/Test-FormattingHooks.ps1
```

Then restore, build, and test x64:

```powershell
pwsh -NoProfile -Command 'dotnet restore ./WorkTrail.slnx -p:Configuration=Release-Unpackaged -p:Platform=x64'
pwsh -NoProfile -Command 'dotnet build ./WorkTrail.slnx --configuration Release-Unpackaged -p:Platform=x64 --no-restore -p:ContinuousIntegrationBuild=true -p:EnableNETAnalyzers=true -p:EnforceCodeStyleInBuild=true -p:TreatWarningsAsErrors=true'
pwsh -NoProfile -Command 'dotnet test ./WorkTrail.slnx --configuration Release-Unpackaged -p:Platform=x64 --no-build --no-restore -m:1'
```

Success means exit code zero, no build warnings/errors, and passing tests in every
project. CI builds x64 and ARM64 and runs the release test suite on x64. These
checks verify the build, not an installed or running app.

The repository utility offers shorter everyday equivalents:

```powershell
pwsh -NoProfile -File ./scripts/WorkTrail.ps1 -Action Restore
pwsh -NoProfile -File ./scripts/WorkTrail.ps1 -Action Build -Configuration Debug-Unpackaged -Platform x64 -WarnAsError
pwsh -NoProfile -File ./scripts/WorkTrail.ps1 -Action Test -Configuration Debug-Unpackaged -Platform x64 -WarnAsError
```

Use these instead of repeating the Release sequence for everyday work.
`Build` and `Test` accept `Debug-Unpackaged` or `Release-Unpackaged` and default
to the former. `Test` can rebuild; `-SkipRestore` does not apply to either action.

The direct app build documented in the repository rules is also supported:

```powershell
pwsh -NoProfile -Command 'dotnet build ./WorkTrail/WorkTrail.csproj -p:Platform=x64'
```

That command builds the app's default `Debug` configuration. Use x64 or ARM64
explicitly; WinUI is not built as AnyCPU. Run solution tests separately.

### Run Debug without MSIX packaging

From the repository root, compile and launch with the Debug symbol and unpackaged
deployment initialization:

```powershell
pwsh -NoProfile -Command 'dotnet run --project ./WorkTrail/WorkTrail.csproj --configuration Debug -p:Platform=x64 -p:WorkTrailDistributionMode=Unpackaged --runtime win-x64 --no-launch-profile'
```

For ARM64, use `-p:Platform=ARM64 --runtime win-arm64`. This is a local development
run, not a distribution package. The custom `Debug-Unpackaged` configuration currently
defines `DEBUG_UNPACKAGED` instead of `DEBUG` in the app; use the explicit command
above when you need Debug-only features such as Premium simulation. Unpackaged runs
do not provide the MSIX identity required by Windows screenshot OCR. Property
evaluation confirms the command selects `DEBUG` and `WindowsPackageType=None`;
this is not evidence of a successful UI launch on your machine.

## Before submitting C# changes

Run the formatter and then its read-only verification:

```powershell
pwsh -NoProfile -File ./scripts/Format-Code.ps1
pwsh -NoProfile -File ./scripts/Format-Code.ps1 -Verify
```

The [.editorconfig](../.editorconfig) and [.gitattributes](../.gitattributes) policy is four-space C# indentation, LF line endings, a final newline, and no trailing whitespace. The pre-commit hook formats the staged snapshot. Fully staged files receive those corrections in the working copy; partially staged files preserve their unstaged bytes. A staged `.editorconfig` change applies its rules to indexed C# sources. Formatting does not require NuGet restore.

Preserve unrelated edits and exclude generated files and automatic version
updates from commits. If the task produced build or test output, clean it once
after its last use, following [AGENTS.md](../AGENTS.md). Documentation-only work
needs a diff and link review, not builds, tests, formatter runs, or cleanup.

## Troubleshoot the failing stage

| Failure | Action |
| --- | --- |
| `pwsh`, `git`, or `dotnet` is missing | Install the corresponding tool, reopen the terminal, and rerun its version command from the repository root. |
| .NET reports that the SDK in `global.json` cannot be found, or `NETSDK1045` | Compare `dotnet --list-sdks` with `global.json`; install a stable compatible `10.0.4xx` SDK and check which `dotnet` is on `PATH`. A runtime-only installation is insufficient. Update the IDE if its MSBuild does not support the SDK. Do not edit `global.json` simply to use an older installation. |
| Windows SDK, XAML compiler, packaging target, or WinUI component is missing | Repair the Visual Studio WinUI workload/Windows SDK components identified in the error and restore the solution again. Check [the app project](../WorkTrail/WorkTrail.csproj) for its actual targets. Restart the IDE after changing installed components. |
| Unsupported solution configuration or platform | Use `Debug-Unpackaged` or `Release-Unpackaged` for the solution and x64 or ARM64 for the platform. `Debug` and `Release` are app-project MSIX configurations. |
| NuGet restore fails | Check the first feed/network/proxy/certificate error and the configured package sources. Keep the pinned package versions while diagnosing a clean-restore failure. |
| Formatting verification or the pre-commit hook fails | Run `Format-Code.ps1`, review the changes, then run `-Verify`. Check LF and staged `.editorconfig` rules. Resolve the failure without bypassing the hook; preserve unrelated unstaged edits. |
| Tests cannot find assemblies after cleaning | Run the matching configuration/platform build before using `--no-build`. Solution `Release-Unpackaged` maps supporting/test projects to their `Release` builds; avoid guessing a test DLL path from the app's output directory. |
| Windows OCR reports an unavailable language | Install the selected Windows OCR language capability, then reopen the app and select an available recognizer. Display language and OCR language are independent; Vietnamese UI support does not imply a Vietnamese Windows OCR recognizer. Explicit unsupported OCR languages fail instead of silently selecting another. |
| OCR engine initialization fails in an unpackaged run | Validate OCR using an installed MSIX with package identity and the selected recognizer installed. See [WorkTrail.Ocr](../WorkTrail.Ocr/README.md). An unpackaged UI smoke test cannot establish packaged OCR support. |
| Package signing fails or an MSIX cannot be installed | Check the signing tools, certificate private key, validity, publisher match, and required trust on the target machine. See the separate packaging section below. Compilation success does not verify those prerequisites. |

Windows exposes installed recognizers through `OcrEngine.AvailableRecognizerLanguages`; OCR capability installation is documented under Microsoft's [language Features on Demand](https://learn.microsoft.com/en-us/windows-hardware/manufacture/desktop/features-on-demand-language-fod?view=windows-11). OCR is optional and is not a prerequisite for compiling the solution.

## Keep validation evidence separate

| Evidence | What to record |
| --- | --- |
| Build and automated tests | Commit, Windows/tool versions, exact configuration/platform and commands, exit results, and test summaries. No app launch or installation is implied. |
| Unpackaged runtime | The published artifact used, its architecture, and the UI/CLI scenario actually exercised. A successful publish alone is not a successful launch. |
| Installed package | The exact signed package, successful deployment, and the scenario exercised through that installed app. Include OCR language/package-identity evidence when testing OCR. A signed archive alone is not an installation test. |

Portable release artifacts are prepared through GitHub Actions. See
[RELEASING.md](RELEASING.md) for that workflow and the
MSIX requirement for on-device screenshot OCR.

For an explicitly requested local MSIX installation task:

```powershell
pwsh -NoProfile -File ./scripts/WorkTrail.ps1 -Action PackageMsix -Platform x64
```

`PackageMsix` resolves a certificate, restores/cleans/publishes the packaged `Release` configuration, and checks the archive signature, manifest, architecture, and version metadata. That archive check does not verify certificate trust on another machine. It writes under `artifacts/packages/`. `CreateInstaller` creates the final installer under `artifacts/installers/` and normally runs the packaging step first.

Local packaging creates or reuses the current-user `WorkTrail Test Signing`
certificate (`CN=umber`), exports its public certificate under
`artifacts/certificates/`, and trusts it for the current user. A custom
`-PackageCertificateThumbprint` must refer to a certificate with a private key
and matching publisher in `Cert:\CurrentUser\My`. Test certificates are for
local sideloading; distribution requires appropriate signing. See Microsoft's
[certificate requirements](https://learn.microsoft.com/en-us/windows/msix/package/create-certificate-package-signing).

Package creation does not install or launch WorkTrail. Perform those steps only for an intended runtime/deployment validation and record the results separately. For documentation-only contributions, review the commands and links without installing or launching the app.

When reporting failures, include the stage, sanitized error, commit, and tool versions. Keep credentials, tokens, personal activity data, and private machine paths out of issue bodies and attached logs.
