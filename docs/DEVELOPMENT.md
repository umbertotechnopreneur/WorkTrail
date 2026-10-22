# Build WorkTrail from source

Use Windows 11 x64 or ARM64, PowerShell 7, the .NET 10 SDK selected by `global.json`, and the Windows SDK/MSIX tools. Commercial binaries target these Windows architectures and use MSIX. Linux and macOS are source-only.

## Prepare a checkout

```powershell
pwsh -NoProfile -File ./scripts/Install-GitHooks.ps1
pwsh -NoProfile -File ./scripts/WorkTrail.ps1 -Action Preflight
```

Before the first use of an external library or binary, scan the downloaded archive and extracted dependency files with Microsoft Defender. Record the official source, version or hash, command and clean result. Reuse the evidence for unchanged imports; do not scan whole installed SDK trees or generated output. See [AGENTS.md](../AGENTS.md).

## Compile and check

The app project accepts `Debug` directly. Solution configurations map the shared projects separately; do not assume that `Debug|x64` is a solution configuration.

```powershell
dotnet build ./WorkTrail/WorkTrail.csproj --configuration Debug -p:Platform=x64 -p:RuntimeIdentifier=win-x64 --artifacts-path ./artifacts/development -p:GenerateAppxPackageOnBuild=false -p:AppxPackageSigningEnabled=false
dotnet test ./WorkTrail.Core.Tests/WorkTrail.Core.Tests.csproj --configuration Debug -p:Platform=x64 --artifacts-path ./artifacts/development --results-directory ./artifacts/development/test-results/core
dotnet test ./WorkTrail.Presentation.Tests/WorkTrail.Presentation.Tests.csproj --configuration Debug -p:Platform=x64 --artifacts-path ./artifacts/development --results-directory ./artifacts/development/test-results/presentation
pwsh -NoProfile -File ./scripts/Format-Code.ps1
pwsh -NoProfile -File ./scripts/Format-Code.ps1 -Verify
```

These are contributor commands, not standing authorization for an agent to build or run tests. Builds, tests, reruns, MSIX packaging, installation and Git delivery follow the owner authorization rules in AGENTS.md. Compile from the current source; never substitute an installed or previously packaged app.

## Clean generated files

Use ignored task-specific directories under `artifacts/`. After the last use, run `dotnet clean` with the matching configuration, platform, runtime and artifacts path, then remove task-owned leftovers after checking their resolved paths. Preserve only the latest validated MSIX for the requested channel/architecture, its checksum and minimal installation metadata. Preserve existing application data and shared SDK/NuGet caches.

A successful compile or test run does not prove installation, native window behavior or external provider behavior. See [the current open acceptance outcomes](../.github/tasks/todo.md).