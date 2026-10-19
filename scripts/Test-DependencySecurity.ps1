# SPDX-License-Identifier: MIT
#requires -Version 7.0
<#
.SYNOPSIS
Scans the .NET toolchain or restored NuGet packages before CI uses them.
.DESCRIPTION
Records hashes, package sources and the targeted Defender command under artifacts.
An unavailable scanner, unfinished scan or nonzero result stops the job.
See https://learn.microsoft.com/defender-endpoint/command-line-arguments-microsoft-defender-antivirus.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateSet('Archive', 'DotNet', 'NuGet')]
    [string]$Stage,
    [Parameter(Mandatory)]
    [string]$OutputDirectory,
    [string]$ArchivePath,
    [string]$ArchiveSource
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$artifactsRoot = [IO.Path]::GetFullPath((Join-Path $repositoryRoot 'artifacts'))
$outputRoot = [IO.Path]::GetFullPath($OutputDirectory)
if (-not $outputRoot.StartsWith($artifactsRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Security records must stay inside artifacts/.'
}
[void](New-Item -ItemType Directory -Path $outputRoot -Force)

$platformRoot = Join-Path $env:ProgramData 'Microsoft/Windows Defender/Platform'
$scanner = @(Get-ChildItem -LiteralPath $platformRoot -Directory -ErrorAction SilentlyContinue |
    Sort-Object Name -Descending | ForEach-Object { Join-Path $_.FullName 'MpCmdRun.exe' } |
    Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } | Select-Object -First 1)
if ($scanner.Count -eq 0) {
    $scanner = @(Join-Path $env:ProgramFiles 'Windows Defender/MpCmdRun.exe')
}
if (-not (Test-Path -LiteralPath $scanner[0] -PathType Leaf)) {
    throw 'Microsoft Defender is unavailable; dependencies have not been cleared for use.'
}

$sources = [Collections.Generic.List[object]]::new()
if ($Stage -eq 'Archive') {
    if ([string]::IsNullOrWhiteSpace($ArchivePath) -or [string]::IsNullOrWhiteSpace($ArchiveSource)) {
        throw 'Archive scans require the staged archive and its official source.'
    }
    $targets = @([IO.Path]::GetFullPath($ArchivePath))
    $sources.Add(@{ Archive = [IO.Path]::GetFileName($ArchivePath); Source = $ArchiveSource;
            Sha256 = (Get-FileHash -LiteralPath $targets[0] -Algorithm SHA256).Hash })
}
elseif ($Stage -eq 'DotNet') {
    # Resolve the installed SDK without executing its binaries before the scan.
    $dotnetRoot = Split-Path -Parent (Get-Command dotnet.exe -CommandType Application).Source
    $requestedSdk = [version](Get-Content -LiteralPath (Join-Path $repositoryRoot 'global.json') -Raw | ConvertFrom-Json).sdk.version
    # global.json allows the latest patch in this feature band, not necessarily its base version.
    $sdkVersion = Get-ChildItem -LiteralPath (Join-Path $dotnetRoot 'sdk') -Directory |
        Where-Object { $_.Name -match '^\d+\.\d+\.\d+$' } | ForEach-Object { [version]$_.Name } |
        Where-Object { $_.Major -eq $requestedSdk.Major -and $_.Minor -eq $requestedSdk.Minor -and
            [Math]::Floor($_.Build / 100) -eq [Math]::Floor($requestedSdk.Build / 100) -and $_ -ge $requestedSdk } |
        Sort-Object -Descending | Select-Object -First 1
    if ($null -eq $sdkVersion) { throw 'The requested .NET SDK feature band is unavailable.' }
    $sdkRoot = Join-Path $dotnetRoot "sdk/$sdkVersion"
    $requiredRuntime = [version](Get-Content -LiteralPath (Join-Path $sdkRoot 'dotnet.runtimeconfig.json') -Raw |
        ConvertFrom-Json).runtimeOptions.framework.version
    $runtimeVersion = Get-ChildItem -LiteralPath (Join-Path $dotnetRoot 'shared/Microsoft.NETCore.App') -Directory |
        Where-Object { $_.Name -match '^\d+\.\d+\.\d+$' } | ForEach-Object { [version]$_.Name } |
        Where-Object { $_.Major -eq $requiredRuntime.Major -and $_.Minor -eq $requiredRuntime.Minor -and $_ -ge $requiredRuntime } |
        Sort-Object -Descending | Select-Object -First 1
    if ($null -eq $runtimeVersion) { throw 'The required .NET runtime is unavailable.' }
    $targets = @((Join-Path $dotnetRoot 'dotnet.exe'), $sdkRoot,
        (Join-Path $dotnetRoot 'host/fxr'), (Join-Path $dotnetRoot 'packs'),
        (Join-Path $dotnetRoot "shared/Microsoft.NETCore.App/$runtimeVersion"))
    $sources.Add(@{ Source = 'https://dotnet.microsoft.com/download/dotnet'; Sdk = $sdkVersion.ToString(); Runtime = $runtimeVersion.ToString();
            HostSha256 = (Get-FileHash -LiteralPath $targets[0] -Algorithm SHA256).Hash })
}
else {
    if ([string]::IsNullOrWhiteSpace($env:NUGET_PACKAGES)) { throw 'NUGET_PACKAGES must identify the isolated CI package directory.' }
    $targets = @([IO.Path]::GetFullPath($env:NUGET_PACKAGES))
    $packages = @(Get-ChildItem -LiteralPath $targets[0] -Filter '*.nupkg' -Recurse -File)
    if ($packages.Count -eq 0) { throw 'No NuGet archives were found to scan.' }
    foreach ($package in $packages) {
        $metadataPath = Join-Path $package.DirectoryName '.nupkg.metadata'
        $metadata = Get-Content -LiteralPath $metadataPath -Raw | ConvertFrom-Json
        $source = [Uri]$metadata.source
        if ($source.Scheme -ne 'https' -or $source.Host -ne 'api.nuget.org' -or $source.UserInfo -or $source.Query) {
            throw 'A restored package has an unexpected source; review it before use.'
        }
        $sources.Add(@{ Archive = $package.Name; Source = $source.AbsoluteUri;
                Sha256 = (Get-FileHash -LiteralPath $package.FullName -Algorithm SHA256).Hash })
    }
}

$record = @{ Stage = $Stage; Sources = @($sources.ToArray()); Scans = @(); CompletedWithoutDetections = $false }
$recordPath = Join-Path $outputRoot "$Stage.json"
try {
    foreach ($target in $targets) {
        if (-not (Test-Path -LiteralPath $target)) { throw "The security scan target is missing: $target" }
        $arguments = @('-Scan', '-ScanType', '3', '-File', $target, '-DisableRemediation')
        $scan = @{ Scanner = $scanner[0]; Arguments = $arguments; StartedUtc = [DateTime]::UtcNow.ToString('O'); ExitCode = $null }
        $record.Scans += $scan
        # DisableRemediation scans archives, ignores exclusions and leaves detections untouched.
        & $scanner[0] @arguments 2>&1 | ForEach-Object {
            $line = $_.ToString()
            Add-Content -LiteralPath (Join-Path $outputRoot "$Stage.log") -Value $line -Encoding utf8NoBOM
            Write-Output $line
        }
        $scan.ExitCode = $LASTEXITCODE
        $scan.FinishedUtc = [DateTime]::UtcNow.ToString('O')
        if ($scan.ExitCode -ne 0) { throw "Defender did not clear the $Stage scan (exit $($scan.ExitCode))." }
    }
    $record.CompletedWithoutDetections = $true
}
finally {
    $record | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $recordPath -Encoding utf8NoBOM
}
