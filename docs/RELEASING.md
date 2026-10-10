# Preparing a GitHub release (For admins only)

WorkTrail produces native **x64** and **ARM64** MSIX packages and portable ZIPs.
The `release packages` workflow prepares all four artifacts from the same commit
and version. Until a distribution signing identity is configured, application
binaries are **unsigned**. MSIX packages require signing before installation;
portable executables can be extracted and run directly. The workflow creates drafts only.

## GitHub Actions

- Run **release packages** manually with a version such as `1.2.3` to produce downloadable
  workflow artifacts only.
- Push an approved `vX.Y.Z` tag to generate the same artifacts and attach them to a
  **draft** GitHub Release. The workflow never publishes the draft and refuses to
  replace an existing release for that tag.
- Versions have three numeric components: major `1..65534`, minor and patch `0..65534`,
  with no leading zeros or prerelease suffixes. Both MSIX packages use `X.Y.Z.0`.
- Each archive and its `.zip.sha256` file are retained as workflow artifacts for 30 days.
  Draft Release assets remain available to maintainers independently of that retention.

The workflow pins its actions by commit, grants package jobs read-only repository
access, and gives only the draft-creation job `contents: write`. No signing secrets
or development certificates are used or exported.

## Prepare a release

1. Merge the reviewed version changes to `main` after required checks pass.
2. Run **release packages** in GitHub Actions for the agreed version. For a draft
   Release, create an approved annotated `vX.Y.Z` tag only after that version is
   on `main`.
3. Review the x64 and ARM64 packages, portable ZIPs, metadata, and checksums.
4. Complete signing and clean-machine checks before explicitly publishing.

Follow [AGENTS.md](../AGENTS.md): release artifacts are built through GitHub
Actions, not built, signed, uploaded, or published locally. A local MSIX for an
explicit installation task is separate from publishing a release; see
[development](DEVELOPMENT.md).

The workflow uses fresh output folders and separate checkouts for each
architecture. Existing validated archives must not be overwritten silently.

Release builds generate their manifest and `BuildInfo.json` under
`WorkTrail/obj/release/<version>/<platform>/`; they do not advance the local build
counter or rewrite the tracked package manifest. Normal local builds retain their
automatic version increment.

## Archive contents and dependencies

Each `artifacts/releases/<version>/<platform>/WorkTrail-<version>-<platform>-unsigned.zip`
contains:

- the unsigned application MSIX, including its .NET runtime;
- the matching Microsoft-signed framework packages from the SDK's `Dependencies`
  output, validated against the application manifest's name, publisher, minimum
  version, and architecture;
- `release.json` with package versions and hashes, and `SHA256SUMS.txt` covering the
  payload files;
- `Install.ps1`, preparation instructions, and license/trademark notices.

Only dependencies for the target architecture or neutral architecture are copied.
The current application requires `Microsoft.WindowsAppRuntime.2`; the packaging
scripts consume the SDK's emitted files and do not download an arbitrary latest
runtime. Missing, ambiguous, or untrusted dependencies fail preparation.

`Install.ps1` refuses unsigned archives before making changes. Once a signed release
has been prepared, it verifies package hashes, signatures, architecture, version,
and dependency coverage before registering the app with `Add-AppxPackage`. It never
imports a certificate or changes certificate trust. The helper needs PowerShell 7.

On x64, the application package also contains the pinned, official PawnIO sensor
installer under `Hardware/PawnIO`. Its SHA-256 and Authenticode signature are
verified during the build; the build never executes it. After app registration,
`Install.ps1` installs PawnIO with Windows administrator consent if it is missing
or older than the bundled version. Errors are reported explicitly even though
the app has already been installed, and a required Windows restart is reported.
Direct MSIX installation can complete the same prerequisite from Sensors options
using **Install and activate advanced sensors**. MSIX itself cannot install drivers.
PawnIO is shared with other applications and is not removed with WorkTrail.
ARM64 does not bundle or install PawnIO because advanced collection is unsupported.

## Portable archives

Each `artifacts/releases/<version>/<platform>/portable/WorkTrail-<version>-<platform>-portable-unsigned.zip`
contains the complete `Release-Unpackaged` publish output, including .NET and Windows
App SDK, build/release metadata, notices, and payload checksums.
An adjacent `.zip.sha256` verifies the archive. The archive writer rejects missing
runtimes, mismatched version/architecture metadata, and an existing output directory.

Extract the entire ZIP and run `WorkTrail.exe`. Keep the executable with all DLLs,
resources, and subfolders; copying only the EXE is not a supported deployment.
Basic portable startup requires no MSIX registration, certificate import, runtime
installer, or administrator access. On x64, the optional advanced-sensor action
installs the bundled PawnIO driver with administrator consent when needed, then
starts the elevated sensor collector. This system driver remains installed after
the portable folder is removed; subsequent sessions reuse it.
Use `./WorkTrail.exe --version` for the CLI; portable ZIPs do not register an execution alias.
Unsigned executables may show a Windows security prompt.

Portable describes deployment, not a separate data-storage mode: settings and history
remain under `%LOCALAPPDATA%\WorkTrail`.

### Portable startup and feature limitations

| Component or feature | Portable distribution | Effect on startup and use |
| --- | --- | --- |
| .NET, WinUI, and Windows App SDK | Included in the ZIP for the selected architecture. | These supply the application startup libraries. An incomplete extraction or the wrong architecture can prevent launch. |
| On-device screenshot OCR | The Windows OCR API requires MSIX package identity, which this portable build does not have. | The OCR engine is created only when text extraction is requested. OCR failures are recorded on the capture without discarding the screenshot or terminating the application. Enabling OCR in saved settings does not provide package identity. |

Use the MSIX edition for supported on-device OCR. The lack of package identity
limits OCR, while the main player can open without it. The application does not
substitute another OCR engine. For implementation details, see
`ScreenshotTextExtractionCoordinator.AttachAsync`.

For `v1.0.900`, the extracted x64 portable passed `WorkTrail.exe --version` with exit
code zero and reported `1.0.900` on the development workstation. This exercises the
WinUI application bootstrap and CLI route, not full player initialization.
It does **not** establish full UI startup on a clean machine without preinstalled
development runtimes. Native ARM64 execution has not been verified.

Before publication, verify extraction, CLI and UI startup on clean Windows x64 and
ARM64 machines without .NET or Windows App SDK preinstalled. Cross-compilation and
archive checks do not establish native ARM64 launch behavior.

## Completing signing later

Before publishing a public release:

1. Configure the chosen signing provider and its publisher identity. The manifest
   publisher must match the certificate. Keep credentials in the provider/GitHub
   secret mechanism, never command-line arguments or repository files.
2. Add the provider's signing step after package creation and before archive/hash
   generation. Extend archive generation to accept and validate the signed application,
   record `signing: signed`, and remove the unsigned filename/instructions. The current
   archive script deliberately accepts only unsigned preparation packages.
3. Regenerate all hashes after signing; never relabel an existing unsigned ZIP as signed.
   A valid signature does not by itself guarantee immediate SmartScreen reputation.
4. Test MSIX installation, upgrade, launch, CLI access, and uninstall, and portable
   extraction/launch on clean Windows x64 and ARM64 machines. Cross-compilation does
   not verify execution on ARM64.
5. Replace the preparation assets in the draft with the validated signed archives,
   review the release notes, and publish explicitly.

Run the script contract tests with:

```powershell
pwsh -NoProfile -File ./scripts/Test-ReleasePackaging.ps1
pwsh -NoProfile -File ./scripts/Test-PortableReleasePackaging.ps1
```

References: [GitHub Releases](https://docs.github.com/en/repositories/releasing-projects-on-github/managing-releases-in-a-repository),
[Microsoft signing options](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/code-signing-options),
and [Add-AppxPackage dependencies](https://learn.microsoft.com/en-us/powershell/module/appx/add-appxpackage).
