# SPDX-License-Identifier: MIT
#requires -Version 7.0
<#
.SYNOPSIS
Stages an official .NET SDK archive and scans it before extraction or execution.
#>
[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if ($env:GITHUB_ACTIONS -ne 'true' -or $env:RUNNER_ENVIRONMENT -ne 'github-hosted') {
    throw 'This SDK bootstrap is restricted to disposable GitHub-hosted CI jobs.'
}
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$requestedSdk = [version](Get-Content -LiteralPath (Join-Path $repositoryRoot 'global.json') -Raw | ConvertFrom-Json).sdk.version
$stagingRoot = Join-Path $repositoryRoot 'artifacts/ci/toolchain'
if (Test-Path -LiteralPath $stagingRoot) { throw 'The CI SDK staging directory already exists.' }
[void](New-Item -ItemType Directory -Path $stagingRoot)

# Resolve only stable patches in the requested feature band, from Microsoft's release metadata.
$metadataUrl = "https://builds.dotnet.microsoft.com/dotnet/release-metadata/$($requestedSdk.Major).$($requestedSdk.Minor)/releases.json"
$metadata = Invoke-RestMethod -Uri $metadataUrl
$sdk = $metadata.releases | ForEach-Object { $_.sdks } |
    Where-Object { $_.version -match '^\d+\.\d+\.\d+$' } |
    Where-Object {
        $version = [version]$_.version
        $version.Major -eq $requestedSdk.Major -and $version.Minor -eq $requestedSdk.Minor -and
        [Math]::Floor($version.Build / 100) -eq [Math]::Floor($requestedSdk.Build / 100) -and $version -ge $requestedSdk
    } | Sort-Object { [version]$_.version } -Descending | Select-Object -First 1
if ($null -eq $sdk) { throw 'No stable SDK matches global.json in the official release metadata.' }
$archive = @($sdk.files | Where-Object { $_.rid -eq 'win-x64' -and $_.name.EndsWith('.zip') })
if ($archive.Count -ne 1) { throw 'The official SDK metadata did not identify one Windows x64 archive.' }
$source = [Uri]$archive[0].url
if ($source.Scheme -ne 'https' -or $source.Host -ne 'builds.dotnet.microsoft.com' -or $source.UserInfo -or $source.Query) {
    throw 'The SDK archive is not hosted on the expected official source.'
}
$archivePath = Join-Path $stagingRoot 'sdk.zip'
Invoke-WebRequest -Uri $source -OutFile $archivePath
if ((Get-FileHash -LiteralPath $archivePath -Algorithm SHA512).Hash -ne $archive[0].hash) {
    throw 'The staged SDK archive does not match the official SHA-512 hash.'
}
& (Join-Path $PSScriptRoot 'Test-DependencySecurity.ps1') -Stage Archive -ArchivePath $archivePath `
    -ArchiveSource $source.AbsoluteUri -OutputDirectory (Join-Path $repositoryRoot 'artifacts/ci/security')

# Do not expand or expose the downloaded binaries until their archive scan has succeeded.
$sdkRoot = Join-Path $stagingRoot 'sdk'
Expand-Archive -LiteralPath $archivePath -DestinationPath $sdkRoot
if (-not (Test-Path -LiteralPath (Join-Path $sdkRoot 'dotnet.exe') -PathType Leaf)) {
    throw 'The extracted SDK does not contain its Windows host.'
}
@("DOTNET_ROOT=$sdkRoot", "DOTNET_ROOT_X64=$sdkRoot") | Add-Content -LiteralPath $env:GITHUB_ENV -Encoding utf8NoBOM
Add-Content -LiteralPath $env:GITHUB_ENV -Value "PATH=$sdkRoot;$env:PATH" -Encoding utf8NoBOM
Add-Content -LiteralPath $env:GITHUB_PATH -Value $sdkRoot -Encoding utf8NoBOM
Write-Output "Staged .NET SDK $($sdk.version); extracted-file scan is required before use."
