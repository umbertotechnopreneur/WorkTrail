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
Validates and installs a signed WorkTrail release from its extracted offline ZIP.
.DESCRIPTION
Requires release.json and every declared package beside this script. Unsigned
releases cannot be installed. Certificates are never imported or trusted here.
.PARAMETER ForceApplicationShutdown
Allows Windows to close WorkTrail during installation, without closing other
applications that use its shared framework dependencies.
#>
[CmdletBinding()]
param(
    [string]$ReleaseDirectory = $PSScriptRoot,
    [switch]$ForceApplicationShutdown
)

#region Initialization

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

#endregion

#region Implementation

function Assert-ObjectFields {
    <#
    .SYNOPSIS
    Rejects manifest objects whose fields differ from the expected contract.
    #>

    param($Value, [string[]]$Fields, [string]$Context)

    if ($Value -isnot [System.Collections.IDictionary] -or
        $Value.Count -ne $Fields.Count -or
        @($Value.Keys | Where-Object { $_ -cnotin $Fields }).Count -gt 0) {
        throw "$Context must contain exactly these fields: $($Fields -join ', ')."
    }
}

function Resolve-ReleaseFile {
    <#
    .SYNOPSIS
    Resolves a release file while rejecting traversal and reparse-point paths.
    #>

    param([string]$RelativePath)

    # Reject traversal and links before any package is read or passed to Windows.
    if ([string]::IsNullOrWhiteSpace($RelativePath) -or
        [System.IO.Path]::IsPathRooted($RelativePath) -or
        $RelativePath -match '[<>:"|?*\x00-\x1f]' -or
        @($RelativePath -split '[/\\]' | Where-Object { $_ -in @('', '.', '..') -or $_ -match '[. ]$' }).Count -gt 0) {
        throw "Invalid release-relative path: '$RelativePath'."
    }
    $path = [System.IO.Path]::GetFullPath((Join-Path $releaseRoot $RelativePath))
    if (-not $path.StartsWith($releaseRootPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Release file is outside its directory: '$RelativePath'."
    }
    $current = $releaseRoot
    foreach ($segment in ($RelativePath -split '[/\\]')) {
        $current = Join-Path $current $segment
        $item = Get-Item -LiteralPath $current -Force
        if (($item.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw "Release paths cannot contain symbolic links or junctions: '$RelativePath'."
        }
    }
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Release file is missing: '$RelativePath'."
    }
    return $path
}

function Read-PackageManifest {
    <#
    .SYNOPSIS
    Reads the MSIX identity with bounded XML parsing and disposes archive streams.
    #>

    param([string]$Path)

    $archive = [System.IO.Compression.ZipFile]::OpenRead($Path)
    try {
        $entries = @($archive.Entries | Where-Object { $_.FullName -ceq 'AppxManifest.xml' })
        if ($entries.Count -ne 1) {
            throw "Package must contain one AppxManifest.xml: '$Path'."
        }
        $settings = [System.Xml.XmlReaderSettings]::new()
        $settings.DtdProcessing = [System.Xml.DtdProcessing]::Prohibit
        $settings.XmlResolver = $null
        $settings.MaxCharactersInDocument = 8388608
        $stream = $entries[0].Open()
        try {
            $reader = [System.Xml.XmlReader]::Create($stream, $settings)
            try {
                $document = [System.Xml.XmlDocument]::new()
                $document.XmlResolver = $null
                $document.Load($reader)
            }
            finally {
                $reader.Dispose()
            }
        }
        finally {
            $stream.Dispose()
        }
        $identity = $document.SelectSingleNode('/*[local-name()="Package"]/*[local-name()="Identity"]')
        if ($null -eq $identity -or [string]::IsNullOrWhiteSpace($identity.GetAttribute('Name')) -or
            [string]::IsNullOrWhiteSpace($identity.GetAttribute('Publisher')) -or
            $identity.GetAttribute('Version') -notmatch '^\d+\.\d+\.\d+\.\d+$') {
            throw "Invalid package identity: '$Path'."
        }
        return [pscustomobject]@{
            Name = $identity.GetAttribute('Name')
            Publisher = $identity.GetAttribute('Publisher')
            Version = [version]$identity.GetAttribute('Version')
            Architecture = $identity.GetAttribute('ProcessorArchitecture')
            Document = $document
        }
    }
    finally {
        $archive.Dispose()
    }
}

function Assert-PackageFile {
    <#
    .SYNOPSIS
    Checks a release package against its recorded hash and signature requirements.
    #>

    param($Description, [string]$Context)

    Assert-ObjectFields $Description @('file', 'sha256') $Context
    if ($Description.file -isnot [string] -or $Description.sha256 -isnot [string] -or
        $Description.sha256 -cnotmatch '^[0-9A-F]{64}$') {
        throw "$Context requires a filename and an uppercase SHA-256 hash."
    }
    $path = Resolve-ReleaseFile $Description.file
    if ((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -cne $Description.sha256) {
        throw "SHA-256 mismatch: '$($Description.file)'."
    }
    # Trust must already exist on this PC; there is no certificate-install fallback.
    $signature = Get-AuthenticodeSignature -LiteralPath $path
    if ($signature.Status -ne [System.Management.Automation.SignatureStatus]::Valid) {
        throw "Package signature is not trusted and valid: '$($Description.file)' ($($signature.Status))."
    }
    return $path
}

#endregion

$releaseRoot = [System.IO.Path]::GetFullPath($ReleaseDirectory)
$rootItem = Get-Item -LiteralPath $releaseRoot -Force
if (-not $rootItem.PSIsContainer) {
    throw 'ReleaseDirectory must be an existing directory.'
}
for ($ancestor = $rootItem; $null -ne $ancestor; $ancestor = $ancestor.Parent) {
    if (($ancestor.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw 'ReleaseDirectory cannot contain symbolic links or junctions.'
    }
}
$releaseRootPrefix = $releaseRoot.TrimEnd([char[]]@('/', '\')) + [System.IO.Path]::DirectorySeparatorChar
$metadataPath = Resolve-ReleaseFile 'release.json'
$release = Get-Content -LiteralPath $metadataPath -Raw | ConvertFrom-Json -AsHashtable -Depth 16
Assert-ObjectFields $release @('schemaVersion', 'version', 'packageVersion', 'platform', 'signing', 'package', 'dependencies') 'release.json'
if (($release.schemaVersion -isnot [long] -and $release.schemaVersion -isnot [int]) -or $release.schemaVersion -ne 1 -or
    $release.version -isnot [string] -or $release.version -cnotmatch '^[1-9][0-9]{0,4}\.(0|[1-9][0-9]{0,4})\.(0|[1-9][0-9]{0,4})$' -or
    $release.packageVersion -isnot [string] -or $release.packageVersion -cne "$($release.version).0" -or
    $release.platform -isnot [string] -or $release.platform -cnotin @('x64', 'ARM64') -or
    $release.signing -isnot [string] -or $release.signing -cnotin @('signed', 'unsigned') -or
    $release.dependencies -isnot [array]) {
    throw 'Unsupported or invalid release.json schema, version, platform, signing mode, or dependencies.'
}
if (@($release.version.Split('.') | Where-Object { [long]$_ -gt 65534 }).Count -gt 0) {
    throw 'Release version components cannot exceed 65534.'
}
if ($release.signing -ceq 'unsigned') {
    throw 'This release is unsigned and is not installable until signing. Obtain a signed release; this script never installs or trusts certificates.'
}
if (-not $IsWindows) {
    throw 'WorkTrail installation requires Windows.'
}
$osArchitecture = [System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture.ToString()
if ($osArchitecture -ine $release.platform) {
    throw "This release targets $($release.platform), but Windows is running on $osArchitecture. Download the matching release."
}
Assert-ObjectFields $release.package @('file', 'sha256') 'package'
if ($release.package.file -cne "WorkTrail-$($release.version)-$($release.platform).msix") {
    throw 'The application package filename does not match the release version and platform.'
}
$packagePath = Assert-PackageFile $release.package 'package'
$application = Read-PackageManifest $packagePath
if ($application.Version -ne [version]$release.packageVersion -or
    $application.Architecture -cne $release.platform.ToLowerInvariant()) {
    throw 'The application package version or architecture does not match release.json.'
}

$dependencyPaths = [System.Collections.Generic.List[string]]::new()
$dependencies = @{}
foreach ($entry in $release.dependencies) {
    Assert-ObjectFields $entry @('file', 'sha256') 'dependency'
    if ($entry.file -isnot [string] -or $entry.file -cnotmatch '^Dependencies/[^/\\]+\.(msix|appx)$') {
        throw 'Dependency filenames must be MSIX/AppX files directly under Dependencies/.'
    }
    $path = Assert-PackageFile $entry 'dependency'
    $dependency = Read-PackageManifest $path
    $framework = $dependency.Document.SelectSingleNode('/*[local-name()="Package"]/*[local-name()="Properties"]/*[local-name()="Framework"]')
    if ($null -eq $framework -or $framework.InnerText -cne 'true' -or
        $dependency.Architecture -cnotin @('neutral', $application.Architecture) -or
        $dependencies.ContainsKey($dependency.Name)) {
        throw "Dependency must be a unique framework package for this architecture: '$($entry.file)'."
    }
    $dependencies[$dependency.Name] = $dependency
    $dependencyPaths.Add($path)
}

$required = $application.Document.SelectNodes('/*[local-name()="Package"]/*[local-name()="Dependencies"]/*[local-name()="PackageDependency"]')
$requiredNames = @()
foreach ($requirement in $required) {
    $name = $requirement.GetAttribute('Name')
    $minimumVersion = $requirement.GetAttribute('MinVersion')
    if (-not $dependencies.ContainsKey($name) -or $minimumVersion -notmatch '^\d+\.\d+\.\d+\.\d+$' -or
        $dependencies[$name].Name -cne $name -or
        $dependencies[$name].Publisher -cne $requirement.GetAttribute('Publisher') -or
        $dependencies[$name].Version -lt [version]$minimumVersion) {
        throw "Missing or incompatible offline dependency: '$name'."
    }
    $requiredNames += $name
}
if (@($dependencies.Keys | Where-Object { $_ -cnotin $requiredNames }).Count -gt 0) {
    throw 'The release contains framework packages that the application does not declare.'
}

# All validation is complete. Deployment errors terminate; package trust is never changed.
$installArguments = @{ Path = $packagePath; ForceTargetApplicationShutdown = $ForceApplicationShutdown; ErrorAction = 'Stop' }
if ($dependencyPaths.Count -gt 0) {
    $installArguments.DependencyPath = $dependencyPaths.ToArray()
}
Add-AppxPackage @installArguments
$installed = @(Get-AppxPackage -Name $application.Name | Where-Object {
        $_.Name -ceq $application.Name -and $_.Publisher -ceq $application.Publisher -and
        $_.Version -eq $application.Version -and $_.Architecture.ToString() -ieq $application.Architecture -and
        $_.Status.ToString() -ceq 'Ok'
    })
if ($installed.Count -ne 1) {
    throw 'Deployment completed, but the expected package identity, version, architecture, and healthy status could not be verified.'
}
Write-Host "Installed WorkTrail $($release.version) ($($release.platform))." -ForegroundColor Green
