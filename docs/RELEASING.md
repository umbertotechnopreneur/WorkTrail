# Preparing a portable GitHub release

GitHub Releases contain **portable ZIPs for Windows 11 x64 and ARM64**, with their
adjacent SHA-256 files. The release workflow does not build or attach MSIX packages,
MSI installers or standalone EXE downloads. Microsoft Store submission is a separate
task requiring an explicit owner request.

## Build the approved source version

The `release packages` workflow reads `semVer` from `WorkTrail/build-version.json`.
The value must be `X.Y.Z`, with major `1..65534` and minor/patch `0..65534`, without
leading zeros or prerelease suffixes. The source commit must be reachable from `main`.

- Run the workflow manually from `main` to prepare downloadable workflow artifacts.
- Push an explicitly approved annotated `vX.Y.Z` tag after that version is on `main`
  to prepare the archives and a **draft** GitHub Release.
- The tag must exactly match the tracked version. The workflow refuses to replace
  an existing release and never publishes a draft automatically.

Each architecture has its own checkout and output directories. The workflow builds
`Release-Unpackaged` from source, runs the portable packaging safeguards and creates
the archives with `scripts/New-PortableReleaseArchive.ps1`. Build dependencies must
follow the first-import scan rule in [AGENTS.md](../AGENTS.md).

Only ZIPs and their checksum files are uploaded as release artifacts. Workflow
artifacts are retained for 30 days; draft release assets have separate retention.
No signing credentials or development certificates are exported.

## Archive contents

Each `WorkTrail-<version>-<platform>-portable-unsigned.zip` contains:

- `WorkTrail.exe`, its libraries and resource directories;
- the bundled .NET and Windows App SDK runtimes for that architecture;
- build/release metadata, license and trademark notices, and payload checksums.

The archive writer rejects missing runtimes, version/architecture mismatches and
existing output directories. An adjacent `.zip.sha256` verifies the archive.

Extract the entire ZIP and run `WorkTrail.exe`. Keep its DLLs, resources and
subfolders together. Portable deployment does not require MSIX registration,
certificate import, a separate runtime installer or administrator access.
Unsigned executables may trigger Windows security prompts.

Portable describes deployment, not where preferences are stored: application data
stays under `%LOCALAPPDATA%\WorkTrail`. The ZIP does not register an execution alias;
run the extracted executable directly for CLI commands such as `--version`.

## Validate before publication

Review both architectures, version metadata, archive contents and checksums. Verify
extraction, CLI and UI startup on clean Windows x64 and ARM64 machines without .NET
or Windows App SDK preinstalled. Cross-compilation does not prove native ARM64
execution. Publish a reviewed draft only after an explicit owner request.

## Local portable preparation and cleanup

When explicitly requested, use `WorkTrail.ps1 -Action PublishUnpackaged` and
`New-PortableReleaseArchive.ps1` with the same approved version and target
architecture. A normal Debug compile uses `Debug-Unpackaged`. Do not use the MSIX
installer helpers for GitHub release preparation.

Generate output only under a task-specific ignored `artifacts/` directory. After
the final use, run matching `dotnet clean` and remove verified task-owned
intermediates. Retain only the latest validated ZIP per requested architecture,
its checksum and minimal validation metadata. Preserve application data and shared
SDK/NuGet caches. See [development](DEVELOPMENT.md) and [AGENTS.md](../AGENTS.md).
