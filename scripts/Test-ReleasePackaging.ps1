#requires -Version 7.0
<#
.SYNOPSIS
Checks release metadata and rejection paths using isolated synthetic packages.
.DESCRIPTION
Does not build the application, read repository version state, install packages,
or change certificate trust. Fixtures remain under artifacts/release-tests/.
#>
[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$fixtureRoot = Join-Path $repositoryRoot "artifacts/release-tests/$([Guid]::NewGuid().ToString('N'))"
[void][IO.Directory]::CreateDirectory($fixtureRoot)
$entryPoint = Join-Path $PSScriptRoot 'WorkTrail.ps1'
$archiveScript = Join-Path $PSScriptRoot 'New-ReleaseArchive.ps1'
$installScript = Join-Path $PSScriptRoot 'Install-WorkTrailRelease.ps1'
$utf8 = [Text.UTF8Encoding]::new($false)
$script:passed = 0

function Assert-ReleaseTest {
    param([bool]$Condition, [string]$Message)
    if (-not $Condition) { throw $Message }
}

function Invoke-TestScript {
    param([string]$Path, [string[]]$Arguments, [hashtable]$Environment = @{})

    $start = [Diagnostics.ProcessStartInfo]::new((Get-Command pwsh -ErrorAction Stop).Source)
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    foreach ($argument in (@('-NoProfile', '-NonInteractive', '-File', $Path) + $Arguments)) {
        [void]$start.ArgumentList.Add($argument)
    }
    # Each fixture controls its own version; an enclosing build must not affect this test.
    [void]$start.Environment.Remove('WORKTRAIL_RELEASE_VERSION')
    foreach ($key in $Environment.Keys) { $start.Environment[$key] = $Environment[$key] }
    $process = [Diagnostics.Process]::Start($start)
    try {
        $stdout = $process.StandardOutput.ReadToEndAsync()
        $stderr = $process.StandardError.ReadToEndAsync()
        $process.WaitForExit()
        return [pscustomobject]@{ ExitCode = $process.ExitCode; Output = $stdout.GetAwaiter().GetResult() + $stderr.GetAwaiter().GetResult() }
    }
    finally { $process.Dispose() }
}

function Assert-Rejected {
    param([string]$Name, [string]$Path, [string[]]$Arguments, [string]$ExpectedMessage, [string]$ForbiddenOutput)

    $result = Invoke-TestScript -Path $Path -Arguments $Arguments
    Assert-ReleaseTest ($result.ExitCode -ne 0) "$Name unexpectedly succeeded."
    Assert-ReleaseTest ($result.Output -like "*$ExpectedMessage*") "$Name failed for the wrong reason: $($result.Output)"
    if (-not [string]::IsNullOrEmpty($ForbiddenOutput)) {
        Assert-ReleaseTest (-not (Test-Path -LiteralPath $ForbiddenOutput)) "$Name wrote output before rejecting its input."
    }
    $script:passed++
    Write-Host "PASS: $Name"
}

function New-SyntheticPackage {
    param([string]$Name, [string]$Architecture = 'x64', [string]$PackageVersion = '1.2.3.0', [string]$BuildVersion = '1.2.3', [switch]$RequireFramework)

    $directory = Join-Path $fixtureRoot $Name
    [void][IO.Directory]::CreateDirectory($directory)
    $packagePath = Join-Path $directory 'Synthetic.msix'
    $dependency = if ($RequireFramework) {
        '<Dependencies><PackageDependency Name="Microsoft.WindowsAppRuntime.Test" Publisher="CN=Microsoft Corporation" MinVersion="1.0.0.0" /></Dependencies>'
    }
    else { '<Dependencies />' }
    $manifest = '<Package xmlns="http://schemas.microsoft.com/appx/manifest/foundation/windows10"><Identity Name="WorkTrail.ReleaseTest" Publisher="CN=ReleaseTest" Version="{0}" ProcessorArchitecture="{1}" />{2}</Package>' -f $PackageVersion, $Architecture, $dependency
    $buildInfo = @{ semVer = $BuildVersion; packageVersion = "$BuildVersion.0"; platform = $Architecture; gitCommit = ('1' * 40); gitDirty = $false } | ConvertTo-Json
    $archive = [IO.Compression.ZipFile]::Open($packagePath, [IO.Compression.ZipArchiveMode]::Create)
    try {
        foreach ($entry in @{ 'AppxManifest.xml' = $manifest; 'BuildInfo.json' = $buildInfo }.GetEnumerator()) {
            $writer = [IO.StreamWriter]::new($archive.CreateEntry($entry.Key).Open(), $utf8)
            try { $writer.Write($entry.Value) } finally { $writer.Dispose() }
        }
    }
    finally { $archive.Dispose() }
    return $directory
}

$sourceManifest = Join-Path $fixtureRoot 'Source.appxmanifest'
$statePath = Join-Path $fixtureRoot 'version-state.json'
[IO.File]::WriteAllText($sourceManifest, '<Package xmlns="http://schemas.microsoft.com/appx/manifest/foundation/windows10"><Identity Name="WorkTrail.ReleaseTest" Publisher="CN=ReleaseTest" Version="9.8.7.6" /></Package>', $utf8)
[IO.File]::WriteAllText($statePath, '{"semVer":"9.8.7"}', $utf8)
$sourceManifestHash = (Get-FileHash -LiteralPath $sourceManifest -Algorithm SHA256).Hash
$stateHash = (Get-FileHash -LiteralPath $statePath -Algorithm SHA256).Hash

foreach ($platform in @('x64', 'ARM64')) {
    $buildInfoPath = Join-Path $fixtureRoot "$platform/BuildInfo.json"
    $manifestPath = Join-Path $fixtureRoot "$platform/Package.appxmanifest"
    $arguments = @('-Action', 'BuildInfo', '-VersionStatePath', $statePath, '-PackageManifestPath', $sourceManifest,
        '-PackageManifestOutputPath', $manifestPath, '-OutputPath', $buildInfoPath, '-Platform', $platform, '-Configuration', 'Release')
    $environment = @{}
    if ($platform -eq 'x64') { $arguments += @('-ReleaseVersion', '1.2.3') }
    else { $environment.WORKTRAIL_RELEASE_VERSION = '1.2.3' }
    $result = Invoke-TestScript -Path $entryPoint -Arguments $arguments -Environment $environment
    Assert-ReleaseTest ($result.ExitCode -eq 0) "$platform release metadata failed: $($result.Output)"
    $info = Get-Content -LiteralPath $buildInfoPath -Raw | ConvertFrom-Json
    [xml]$manifest = Get-Content -LiteralPath $manifestPath -Raw
    Assert-ReleaseTest ($info.semVer -ceq '1.2.3' -and $info.packageVersion -ceq '1.2.3.0' -and $manifest.Package.Identity.Version -ceq '1.2.3.0') "$platform release versions differ."
    Assert-ReleaseTest ($info.platform -ceq $platform -and $info.runtimeIdentifier -ceq "win-$($platform.ToLowerInvariant())") "$platform build metadata has the wrong architecture."
    Assert-ReleaseTest ((Get-FileHash -LiteralPath $statePath -Algorithm SHA256).Hash -ceq $stateHash) "$platform changed the local build counter."
    Assert-ReleaseTest ((Get-FileHash -LiteralPath $sourceManifest -Algorithm SHA256).Hash -ceq $sourceManifestHash) "$platform changed the source manifest."
    $script:passed++
    Write-Host "PASS: $platform deterministic release metadata and unchanged source state"
}

$revisionInfoPath = Join-Path $fixtureRoot 'revision/BuildInfo.json'
$revisionManifestPath = Join-Path $fixtureRoot 'revision/Package.appxmanifest'
$revisionResult = Invoke-TestScript -Path $entryPoint -Arguments @(
    '-Action', 'BuildInfo', '-ReleaseVersion', '1.2.3.4', '-Configuration', 'Debug', '-Platform', 'x64',
    '-VersionStatePath', $statePath, '-PackageManifestPath', $sourceManifest,
    '-PackageManifestOutputPath', $revisionManifestPath, '-OutputPath', $revisionInfoPath)
Assert-ReleaseTest ($revisionResult.ExitCode -eq 0) "Four-part package metadata failed: $($revisionResult.Output)"
$revisionInfo = Get-Content -LiteralPath $revisionInfoPath -Raw | ConvertFrom-Json
[xml]$revisionManifest = Get-Content -LiteralPath $revisionManifestPath -Raw
Assert-ReleaseTest ($revisionInfo.semVer -ceq '1.2.3' -and $revisionInfo.packageVersion -ceq '1.2.3.4' -and
    $revisionManifest.Package.Identity.Version -ceq '1.2.3.4') 'Four-part package metadata lost the explicit revision.'
$script:passed++
Write-Host 'PASS: explicit four-part package revision'

$invalidIndex = 0
foreach ($invalidVersion in @('', '0.1.2', '01.2.3', '1.2', '1.2.3.4.5', '1.2.3.01', '1.2.3.65535', '1.2.3-beta', '65535.1.2', '1.65535.2', '1.2.65535', '65536.1.2', '1.65536.2', '1.2.65536')) {
    $invalidIndex++
    $output = Join-Path $fixtureRoot "invalid-$invalidIndex/BuildInfo.json"
    Assert-Rejected -Name "invalid release version '$invalidVersion'" -Path $entryPoint -ExpectedMessage 'ReleaseVersion must' -ForbiddenOutput $output -Arguments @(
        '-Action', 'BuildInfo', '-ReleaseVersion', $invalidVersion, '-VersionStatePath', $statePath, '-PackageManifestPath', $sourceManifest,
        '-PackageManifestOutputPath', (Join-Path $fixtureRoot "invalid-$invalidIndex/Package.appxmanifest"), '-OutputPath', $output)
}
Assert-Rejected -Name 'unsupported release-version action' -Path $entryPoint -ExpectedMessage 'ReleaseVersion is supported only' -Arguments @('-Action', 'Restore', '-ReleaseVersion', '1.2.3')
Assert-Rejected -Name 'unsigned certificate conflict' -Path $entryPoint -ExpectedMessage 'Unsigned packaging cannot use' -Arguments @('-Action', 'CreateInstaller', '-Unsigned', '-PackageCertificateThumbprint', 'TEST-ONLY-NOT-A-CERTIFICATE')
Assert-Rejected -Name 'skip build requires exact package directory' -Path $entryPoint -ExpectedMessage 'SkipPackageBuild requires' -Arguments @('-Action', 'CreateInstaller', '-Unsigned', '-SkipPackageBuild')
$outsidePath = Join-Path $repositoryRoot "release-test-outside-$([Guid]::NewGuid().ToString('N'))"
Assert-Rejected -Name 'package output outside artifacts' -Path $entryPoint -ExpectedMessage 'Package output must be a subdirectory' -ForbiddenOutput $outsidePath -Arguments @('-Action', 'CreateInstaller', '-Unsigned', '-SkipPackageBuild', '-PackageOutputPath', $outsidePath)

Add-Type -AssemblyName System.IO.Compression.FileSystem
foreach ($case in @(
    @{ Name = 'wrong-architecture'; Architecture = 'arm64'; PackageVersion = '1.2.3.0'; BuildVersion = '1.2.3'; Message = 'architecture/version does not match'; RequireFramework = $false },
    @{ Name = 'wrong-version'; Architecture = 'x64'; PackageVersion = '1.2.4.0'; BuildVersion = '1.2.4'; Message = 'architecture/version does not match'; RequireFramework = $false },
    @{ Name = 'wrong-build-info'; Architecture = 'x64'; PackageVersion = '1.2.3.0'; BuildVersion = '1.2.4'; Message = 'build information does not match'; RequireFramework = $false },
    @{ Name = 'missing-framework'; Architecture = 'x64'; PackageVersion = '1.2.3.0'; BuildVersion = '1.2.3'; Message = 'Missing or ambiguous framework dependency'; RequireFramework = $true }
)) {
    $packageDirectory = New-SyntheticPackage -Name $case.Name -Architecture $case.Architecture -PackageVersion $case.PackageVersion -BuildVersion $case.BuildVersion -RequireFramework:$case.RequireFramework
    $output = Join-Path $fixtureRoot "$($case.Name)-output"
    Assert-Rejected -Name "archive $($case.Name)" -Path $archiveScript -ExpectedMessage $case.Message -ForbiddenOutput $output -Arguments @('-PackageDirectory', $packageDirectory, '-Version', '1.2.3', '-Platform', 'x64', '-OutputDirectory', $output)
}

$installDirectory = Join-Path $fixtureRoot 'unsigned-install'
[void][IO.Directory]::CreateDirectory($installDirectory)
$unsignedPackage = Join-Path $installDirectory 'WorkTrail-1.2.3-x64-unsigned.msix'
Copy-Item -LiteralPath (Join-Path $fixtureRoot 'missing-framework/Synthetic.msix') -Destination $unsignedPackage
$release = [ordered]@{
    schemaVersion = 1; version = '1.2.3'; packageVersion = '1.2.3.0'; platform = 'x64'; signing = 'unsigned'
    package = @{ file = [IO.Path]::GetFileName($unsignedPackage); sha256 = (Get-FileHash -LiteralPath $unsignedPackage -Algorithm SHA256).Hash }
    dependencies = @()
}
[IO.File]::WriteAllText((Join-Path $installDirectory 'release.json'), ($release | ConvertTo-Json -Depth 5), $utf8)
Assert-Rejected -Name 'installer refuses unsigned release' -Path $installScript -ExpectedMessage 'This release is unsigned and is not installable' -Arguments @('-ReleaseDirectory', $installDirectory)

Assert-ReleaseTest ((Get-FileHash -LiteralPath $statePath -Algorithm SHA256).Hash -ceq $stateHash) 'A rejection test changed the synthetic build counter.'
Assert-ReleaseTest ((Get-FileHash -LiteralPath $sourceManifest -Algorithm SHA256).Hash -ceq $sourceManifestHash) 'A rejection test changed the synthetic source manifest.'
Write-Host "Release packaging checks passed: $script:passed. Fixtures: $fixtureRoot" -ForegroundColor Green
