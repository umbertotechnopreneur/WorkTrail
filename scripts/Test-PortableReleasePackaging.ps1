#requires -Version 7.0
# VBWR B
#
# Project: WorkTrail
# Repository: https://github.com/umbertotechnopreneur/WorkTrail
# Creator: Umberto Giacobbi | https://umbertogiacobbi.biz
#
# VibeWare: Human intent, AI execution, and plenty of tokens
# Manifesto: https://umbertogiacobbi.biz/vibeware/manifesto
#
# Modified with AI: OpenAI Codex; added this header on 2026-10-10.
# Human guidance: Umberto Giacobbi; requested VibeWare branding.
#
# Copyright (c) 2026 Umberto Giacobbi
# License: MIT - see LICENSE
# SPDX-License-Identifier: MIT
#
# VBWR E

<#
.SYNOPSIS
Checks portable archives and rejection paths with synthetic publish directories.
.DESCRIPTION
Does not build or launch applications, install packages, or change certificate trust.
Fixtures remain under artifacts/portable-release-tests/ for inspection.
#>
[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$fixtureRoot = Join-Path $repositoryRoot "artifacts/portable-release-tests/$([Guid]::NewGuid().ToString('N'))"
[void][IO.Directory]::CreateDirectory($fixtureRoot)
$archiveScript = Join-Path $PSScriptRoot 'New-PortableReleaseArchive.ps1'
$utf8 = [Text.UTF8Encoding]::new($false)
$script:passed = 0

function Assert-PortableTest {
    param([bool]$Condition, [string]$Message)
    if (-not $Condition) { throw $Message }
}

function Invoke-TestScript {
    param([string]$Path, [string[]]$Arguments)

    $start = [Diagnostics.ProcessStartInfo]::new((Get-Command pwsh -ErrorAction Stop).Source)
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    foreach ($argument in (@('-NoProfile', '-NonInteractive', '-File', $Path) + $Arguments)) {
        [void]$start.ArgumentList.Add($argument)
    }
    $process = [Diagnostics.Process]::Start($start)
    try {
        $stdout = $process.StandardOutput.ReadToEndAsync()
        $stderr = $process.StandardError.ReadToEndAsync()
        $process.WaitForExit()
        return [pscustomobject]@{ ExitCode = $process.ExitCode; Output = $stdout.GetAwaiter().GetResult() + $stderr.GetAwaiter().GetResult() }
    }
    finally { $process.Dispose() }
}

function Invoke-Archive {
    param([string]$Source, [string]$Output, [string]$Platform = 'x64', [string]$Version = '1.2.3')

    return Invoke-TestScript -Path $archiveScript -Arguments @('-PublishDirectory', $Source,
        '-OutputDirectory', $Output, '-Platform', $Platform, '-Version', $Version)
}

function Assert-Rejected {
    param([string]$Name, [string]$Source, [string]$Message, [string]$Output,
        [string]$Version = '1.2.3', [switch]$ExistingOutput)

    if ([string]::IsNullOrEmpty($Output)) { $Output = Join-Path $fixtureRoot "$Name-output" }
    $result = Invoke-Archive -Source $Source -Output $Output -Version $Version
    Assert-PortableTest ($result.ExitCode -ne 0) "$Name unexpectedly succeeded."
    Assert-PortableTest ($result.Output -like "*$Message*") "$Name failed for the wrong reason: $($result.Output)"
    if (-not $ExistingOutput) {
        Assert-PortableTest (-not (Test-Path -LiteralPath $Output)) "$Name wrote output before rejecting its input."
    }
    $script:passed++
    Write-Host "PASS: $Name"
}

function Write-SyntheticPe {
    param([string]$Path, [string]$Platform)

    # Only DOS/PE signature and machine fields are needed; these fixtures are never run.
    $bytes = [byte[]]::new(128)
    [BitConverter]::GetBytes([uint16]0x5A4D).CopyTo($bytes, 0)
    [BitConverter]::GetBytes([uint32]64).CopyTo($bytes, 0x3C)
    [BitConverter]::GetBytes([uint32]0x00004550).CopyTo($bytes, 64)
    $machine = if ($Platform -eq 'x64') { 0x8664 } else { 0xAA64 }
    [BitConverter]::GetBytes([uint16]$machine).CopyTo($bytes, 68)
    [IO.File]::WriteAllBytes($Path, $bytes)
}

function New-SyntheticPublish {
    param([string]$Name, [string]$Platform = 'x64')

    $directory = Join-Path $fixtureRoot $Name
    [void][IO.Directory]::CreateDirectory((Join-Path $directory 'empty-directory'))
    foreach ($file in @('WorkTrail.dll', 'WorkTrail.deps.json', 'hostpolicy.dll', 'System.Private.CoreLib.dll',
        'Microsoft.ui.xaml.dll', 'Microsoft.WindowsAppRuntime.dll', 'Microsoft.Windows.ApplicationModel.Resources.dll',
        'WorkTrail.pri', '.hidden-payload')) {
        [IO.File]::WriteAllText((Join-Path $directory $file), "Synthetic payload: $file", $utf8)
    }
    foreach ($file in @('WorkTrail.exe', 'hostfxr.dll', 'coreclr.dll')) {
        Write-SyntheticPe -Path (Join-Path $directory $file) -Platform $Platform
    }
    $buildInfo = @{ schemaVersion = 1; semVer = '1.2.3'; packageVersion = '1.2.3.0'; platform = $Platform
        configuration = 'Release-Unpackaged'; runtimeIdentifier = "win-$($Platform.ToLowerInvariant())"
        gitCommit = ('1' * 40); gitDirty = $false }
    [IO.File]::WriteAllText((Join-Path $directory 'BuildInfo.json'), ($buildInfo | ConvertTo-Json), $utf8)
    [IO.File]::WriteAllText((Join-Path $directory 'WorkTrail.runtimeconfig.json'),
        '{"runtimeOptions":{"tfm":"net10.0","includedFrameworks":[{"name":"Microsoft.NETCore.App","version":"10.0.0"}]}}', $utf8)
    return $directory
}

function Read-ZipText {
    param([IO.Compression.ZipArchive]$Archive, [string]$Name)
    $entry = $Archive.GetEntry($Name)
    Assert-PortableTest ($null -ne $entry) "Archive entry missing: $Name"
    $reader = [IO.StreamReader]::new($entry.Open())
    try { return $reader.ReadToEnd() } finally { $reader.Dispose() }
}

foreach ($platform in @('x64', 'ARM64')) {
    $source = New-SyntheticPublish -Name "valid-$platform" -Platform $platform
    $output = Join-Path $fixtureRoot "$platform-output"
    $result = Invoke-Archive -Source $source -Output $output -Platform $platform
    Assert-PortableTest ($result.ExitCode -eq 0) "$platform packaging failed: $($result.Output)"
    $zipPath = Join-Path $output "WorkTrail-1.2.3-$platform-portable-unsigned.zip"
    $zipHash = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash
    Assert-PortableTest ((Get-Content -LiteralPath "$zipPath.sha256" -Raw).Trim() -ceq "$zipHash  $([IO.Path]::GetFileName($zipPath))") "$platform archive checksum differs."
    $archive = [IO.Compression.ZipFile]::OpenRead($zipPath)
    try {
        $release = Read-ZipText -Archive $archive -Name 'release.json' | ConvertFrom-Json
        Assert-PortableTest ($release.format -ceq 'portable' -and $release.signing -ceq 'unsigned' -and
            $release.platform -ceq $platform -and $release.version -ceq '1.2.3' -and
            $release.entryPoint -ceq 'WorkTrail.exe' -and $release.gitCommit -ceq ('1' * 40)) "$platform release metadata differs."
        Assert-PortableTest ($null -ne $archive.GetEntry('WorkTrail.exe') -and $null -eq $archive.GetEntry('Install.ps1')) "$platform archive is not directly extractable."
        Assert-PortableTest ($null -ne $archive.GetEntry('empty-directory/')) "$platform omitted an empty publish directory."
        $readme = Read-ZipText -Archive $archive -Name 'README.txt'
        Assert-PortableTest ($readme -like '*LocalAppData*' -and
            $readme -like '*On-device screenshot OCR requires the MSIX edition*') "$platform run instructions omit prerequisites or data behavior."
        $manifest = Read-ZipText -Archive $archive -Name 'SHA256SUMS.txt'
        $hashedNames = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
        foreach ($line in $manifest.TrimEnd().Split("`n")) {
            Assert-PortableTest ($line -cmatch '^([0-9A-F]{64})  (.+)$') "$platform invalid checksum line: $line"
            $expectedHash = $Matches[1]
            $name = $Matches[2]
            Assert-PortableTest ($hashedNames.Add($name)) "$platform duplicate checksum entry: $name"
            $entry = $archive.GetEntry($name)
            Assert-PortableTest ($null -ne $entry) "$platform checksum has no payload: $name"
            $stream = $entry.Open()
            try { $actualHash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($stream)) }
            finally { $stream.Dispose() }
            Assert-PortableTest ($actualHash -ceq $expectedHash) "$platform payload checksum differs: $name"
            $original = Join-Path $source $name
            if ($name -in @('LICENSE', 'TRADEMARKS.md', 'THIRD_PARTY_NOTICES.md')) { $original = Join-Path $repositoryRoot $name }
            if (Test-Path -LiteralPath $original -PathType Leaf) {
                Assert-PortableTest ($actualHash -ceq (Get-FileHash -LiteralPath $original -Algorithm SHA256).Hash) "$platform changed source content: $name"
            }
        }
        $payload = @($archive.Entries | Where-Object { $_.Name -ne '' -and $_.FullName -cne 'SHA256SUMS.txt' })
        Assert-PortableTest ($hashedNames.Count -eq $payload.Count) "$platform manifest does not cover every payload file."
        foreach ($file in Get-ChildItem -LiteralPath $source -Recurse -Force -File) {
            $name = [IO.Path]::GetRelativePath($source, $file.FullName).Replace('\', '/')
            Assert-PortableTest ($hashedNames.Contains($name)) "$platform omitted publish file: $name"
        }
    }
    finally { $archive.Dispose() }
    $script:passed++
    Write-Host "PASS: $platform portable payload, metadata, complete checksums, and archive hash"
}

$validSource = Join-Path $fixtureRoot 'valid-x64'
foreach ($version in @('0.1.2', '01.2.3', '1.2', '1.2.3.4', '1.2.3-beta',
    '65535.1.2', '1.65535.2', '1.2.65535', '65536.1.2', '1.65536.2', '1.2.65536')) {
    Assert-Rejected -Name "invalid-version-$version" -Source $validSource -Version $version -Message 'Version must be X.Y.Z'
}
foreach ($change in @(
    @{ Name = 'wrong-version'; Key = 'semVer'; Value = '1.2.4' },
    @{ Name = 'wrong-package-version'; Key = 'packageVersion'; Value = '1.2.4.0' },
    @{ Name = 'wrong-platform'; Key = 'platform'; Value = 'ARM64' },
    @{ Name = 'wrong-runtime-id'; Key = 'runtimeIdentifier'; Value = 'win-arm64' },
    @{ Name = 'wrong-configuration'; Key = 'configuration'; Value = 'Release' },
    @{ Name = 'missing-commit'; Key = 'gitCommit'; Value = '' }
)) {
    $source = New-SyntheticPublish -Name $change.Name
    $path = Join-Path $source 'BuildInfo.json'
    $metadata = Get-Content -LiteralPath $path -Raw | ConvertFrom-Json -AsHashtable
    $metadata[$change.Key] = $change.Value
    [IO.File]::WriteAllText($path, ($metadata | ConvertTo-Json), $utf8)
    Assert-Rejected -Name $change.Name -Source $source -Message 'Portable build information does not match'
}
foreach ($file in @('hostfxr.dll', 'coreclr.dll', 'Microsoft.WindowsAppRuntime.dll')) {
    $name = 'missing-' + $file.Replace('/', '-')
    $source = New-SyntheticPublish -Name $name
    [IO.File]::Delete((Join-Path $source $file))
    Assert-Rejected -Name $name -Source $source -Message 'Missing or empty required portable publish file'
}
foreach ($file in @('WorkTrail.exe', 'hostfxr.dll', 'coreclr.dll')) {
    $name = "wrong-machine-$file"
    $source = New-SyntheticPublish -Name $name
    Write-SyntheticPe -Path (Join-Path $source $file) -Platform 'ARM64'
    Assert-Rejected -Name $name -Source $source -Message 'Portable binary architecture does not match'
}
$source = New-SyntheticPublish -Name 'invalid-pe'
[IO.File]::WriteAllText((Join-Path $source 'WorkTrail.exe'), 'Not an executable.', $utf8)
Assert-Rejected -Name 'invalid-pe' -Source $source -Message 'Invalid PE file'
$source = New-SyntheticPublish -Name 'framework-dependent'
[IO.File]::WriteAllText((Join-Path $source 'WorkTrail.runtimeconfig.json'),
    '{"runtimeOptions":{"framework":{"name":"Microsoft.NETCore.App","version":"10.0.0"}}}', $utf8)
Assert-Rejected -Name 'framework-dependent' -Source $source -Message 'must describe a self-contained .NET application'
$source = New-SyntheticPublish -Name 'reserved-file'
[IO.File]::WriteAllText((Join-Path $source 'release.json'), '{}', $utf8)
Assert-Rejected -Name 'reserved-file' -Source $source -Message 'reserved release filename'

Assert-Rejected -Name 'missing-directory' -Source (Join-Path $fixtureRoot 'nonexistent') -Message 'must be an existing self-contained publish directory'
$outside = Join-Path $repositoryRoot "portable-test-outside-$([Guid]::NewGuid().ToString('N'))"
Assert-Rejected -Name 'output-outside-artifacts' -Source $validSource -Output $outside -Message 'inside this repository artifacts/'
Assert-Rejected -Name 'overlapping-output' -Source $validSource -Output (Join-Path $validSource 'nested-output') -Message 'must not overlap'
Assert-Rejected -Name 'overlapping-source' -Source $validSource -Output $fixtureRoot -Message 'must not overlap' -ExistingOutput
$existing = Join-Path $fixtureRoot 'existing-output'
[void][IO.Directory]::CreateDirectory($existing)
$sentinel = Join-Path $existing 'sentinel.txt'
[IO.File]::WriteAllText($sentinel, 'preserve', $utf8)
Assert-Rejected -Name 'existing-output' -Source $validSource -Output $existing -Message 'output already exists' -ExistingOutput
Assert-PortableTest ((Get-Content -LiteralPath $sentinel -Raw) -ceq 'preserve') 'Existing output was modified.'
Assert-PortableTest (@(Get-ChildItem -LiteralPath $existing -Force).Count -eq 1) 'Existing output received unexpected files.'

foreach ($case in @(
    @{ Name = 'publish-outside-artifacts'; Output = $outside; Message = 'Unpackaged publish output escaped' },
    @{ Name = 'publish-existing-output'; Output = $existing; Message = 'Unpackaged publish output directory must be empty' }
)) {
    $result = Invoke-TestScript -Path (Join-Path $PSScriptRoot 'WorkTrail.ps1') -Arguments @(
        '-Action', 'PublishUnpackaged', '-ReleaseVersion', '1.2.3', '-PublishOutputPath', $case.Output)
    Assert-PortableTest ($result.ExitCode -ne 0 -and $result.Output -like "*$($case.Message)*") "$($case.Name) failed for the wrong reason: $($result.Output)"
    Assert-PortableTest (-not (Test-Path -LiteralPath $outside)) 'Publish rejection wrote outside artifacts.'
    Assert-PortableTest ((Get-Content -LiteralPath $sentinel -Raw) -ceq 'preserve') 'Publish rejection changed existing output.'
    $script:passed++
    Write-Host "PASS: $($case.Name)"
}

$junctionTarget = Join-Path $fixtureRoot 'junction-target'
[void][IO.Directory]::CreateDirectory($junctionTarget)
$outputJunction = Join-Path $fixtureRoot 'output-junction'
[void](New-Item -ItemType Junction -Path $outputJunction -Target $junctionTarget)
Assert-Rejected -Name 'output-junction' -Source $validSource -Output (Join-Path $outputJunction 'output') -Message 'cannot pass through a symbolic link or junction'
$inputJunction = Join-Path $fixtureRoot 'input-junction'
[void](New-Item -ItemType Junction -Path $inputJunction -Target $validSource)
Assert-Rejected -Name 'input-junction' -Source $inputJunction -Message 'cannot pass through a symbolic link or junction'
$source = New-SyntheticPublish -Name 'nested-junction'
[void](New-Item -ItemType Junction -Path (Join-Path $source 'linked-data') -Target $junctionTarget)
Assert-Rejected -Name 'nested-junction' -Source $source -Message 'cannot contain symbolic links or junctions'

Write-Host "Portable release packaging checks passed: $script:passed. Fixtures: $fixtureRoot" -ForegroundColor Green
