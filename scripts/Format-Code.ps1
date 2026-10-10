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
Applies the repository C# whitespace rules, or verifies them without changes.
.DESCRIPTION
The pre-commit mode formats an isolated copy of the Git index. It updates only
formatted staged blobs; partially staged working files remain byte-for-byte intact.
Folder mode uses the same Roslyn whitespace formatter locally and in CI without
loading projects, restoring packages, or running code analyzers.
.EXAMPLE
pwsh -NoProfile -File ./scripts/Format-Code.ps1
.EXAMPLE
pwsh -NoProfile -File ./scripts/Format-Code.ps1 -Verify
#>
[CmdletBinding(DefaultParameterSetName = 'WorkingTree')]
param(
    [Parameter(ParameterSetName = 'WorkingTree')]
    [switch]$Verify,
    [Parameter(Mandatory, ParameterSetName = 'Staged')]
    [switch]$Staged,
    [string]$RepositoryRoot = (Split-Path -Parent $PSScriptRoot)
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$RepositoryRoot = [IO.Path]::GetFullPath($RepositoryRoot)
$utf8 = [Text.UTF8Encoding]::new($false)

function Invoke-Git {
    param(
        [Parameter(Mandatory)][string[]]$Arguments,
        [byte[]]$InputBytes,
        [int[]]$AllowedExitCodes = @(0)
    )

    $start = [Diagnostics.ProcessStartInfo]::new('git')
    $start.WorkingDirectory = $RepositoryRoot
    $start.UseShellExecute = $false
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    $start.RedirectStandardInput = $null -ne $InputBytes
    foreach ($argument in $Arguments) { $start.ArgumentList.Add($argument) }
    $process = [Diagnostics.Process]::new()
    $process.StartInfo = $start
    $output = [IO.MemoryStream]::new()
    try {
        # Read/write blobs as bytes: shell text redirection can change encoding or line endings.
        [void]$process.Start()
        $errorTask = $process.StandardError.ReadToEndAsync()
        $outputTask = $process.StandardOutput.BaseStream.CopyToAsync($output)
        if ($null -ne $InputBytes) {
            $process.StandardInput.BaseStream.Write($InputBytes, 0, $InputBytes.Length)
            $process.StandardInput.Close()
        }
        $process.WaitForExit()
        [void]$outputTask.GetAwaiter().GetResult()
        $errorText = $errorTask.GetAwaiter().GetResult()
        if ($process.ExitCode -notin $AllowedExitCodes) {
            # Git failures stop before committing; no working-copy fallback is attempted.
            throw "Git failed ($($process.ExitCode)): $errorText"
        }
        return [pscustomobject]@{ Bytes = $output.ToArray(); ExitCode = $process.ExitCode }
    }
    finally {
        $output.Dispose()
        $process.Dispose()
    }
}

function Get-GitPaths {
    param([string[]]$Arguments)
    $result = Invoke-Git -Arguments $Arguments
    return $utf8.GetString($result.Bytes).Split([char]0, [StringSplitOptions]::RemoveEmptyEntries)
}

function Invoke-WhitespaceFormatter {
    param([string]$Root, [string[]]$Paths, [switch]$Check)
    if ($Paths.Count -eq 0) { return }
    # One folder workspace avoids repeated MSBuild loads and keeps local/CI rules identical.
    $remaining = [Collections.Generic.List[string]]::new()
    $length = 0
    foreach ($path in @($Paths) + @($null)) {
        if ($remaining.Count -gt 0 -and ($null -eq $path -or $length + $path.Length -gt 20000)) {
            $arguments = @('format', 'whitespace', $Root, '--folder', '--verbosity', 'minimal', '--include') + $remaining.ToArray()
            if ($Check) { $arguments += '--verify-no-changes' }
            & dotnet @arguments
            if ($LASTEXITCODE -ne 0) {
                throw 'C# whitespace formatting failed. Run pwsh -NoProfile -File ./scripts/Format-Code.ps1 and review the changes.'
            }
            $remaining.Clear()
            $length = 0
        }
        if ($null -ne $path) {
            $remaining.Add($path)
            $length += $path.Length + 3
        }
    }
}

function Test-SameBytes {
    param([byte[]]$Left, [byte[]]$Right)
    return [Convert]::ToBase64String($Left) -ceq [Convert]::ToBase64String($Right)
}

function Get-ContainedPath {
    param([string]$Root, [string]$RelativePath)
    $rootPath = [IO.Path]::GetFullPath($Root)
    $fullPath = [IO.Path]::GetFullPath((Join-Path $rootPath $RelativePath))
    if (-not $fullPath.StartsWith($rootPath.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'A formatting path escapes its task directory.'
    }
    return $fullPath
}

$scratch = $null
$scratchParent = Join-Path $RepositoryRoot 'artifacts/format'
Push-Location -LiteralPath $RepositoryRoot
try {
    $gitRoot = $utf8.GetString((Invoke-Git -Arguments @('rev-parse', '--show-toplevel')).Bytes).Trim()
    if (-not [string]::Equals([IO.Path]::GetFullPath($gitRoot), $RepositoryRoot, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'RepositoryRoot must be the Git repository root.'
    }

    if (-not $Staged) {
        $paths = @(Get-GitPaths -Arguments @('ls-files', '--cached', '--others', '--exclude-standard', '-z', '--', '*.cs') |
            Sort-Object -Unique | Where-Object { Test-Path -LiteralPath (Join-Path $RepositoryRoot $_) -PathType Leaf })
        Invoke-WhitespaceFormatter -Root $RepositoryRoot -Paths $paths -Check:$Verify
        Write-Host "C# whitespace $(if ($Verify) { 'verified' } else { 'formatted' }): $($paths.Count) source files."
        return
    }

    $paths = @(Get-GitPaths -Arguments @('diff', '--cached', '--name-only', '--diff-filter=ACMR', '-z', '--', '*.cs'))
    $changedRules = @(Get-GitPaths -Arguments @('diff', '--cached', '--name-only', '-z', '--', '.editorconfig', '**/.editorconfig'))
    if ($changedRules.Count -gt 0) {
        $paths = @(Get-GitPaths -Arguments @('ls-files', '-z', '--', '*.cs'))
    }
    if ($paths.Count -eq 0) { return }

    $scratch = Get-ContainedPath -Root $scratchParent -RelativePath ([Guid]::NewGuid().ToString('N'))
    [void][IO.Directory]::CreateDirectory($scratch)
    $configs = @(Get-GitPaths -Arguments @('ls-files', '-z', '--', '.editorconfig', '**/.editorconfig'))
    $entries = @{}
    foreach ($path in @($paths + $configs | Sort-Object -Unique)) {
        $entryText = $utf8.GetString((Invoke-Git -Arguments @('ls-files', '--stage', '-z', '--', $path)).Bytes)
        $indexEntries = @($entryText.Split([char]0, [StringSplitOptions]::RemoveEmptyEntries))
        if ($indexEntries.Count -ne 1 -or $indexEntries[0] -notmatch '^(100644|100755) ([0-9a-f]+) 0\t') {
            throw "Cannot format an unmerged or non-regular index entry: $path"
        }
        $mode = $Matches[1]
        $objectId = $Matches[2]
        $bytes = (Invoke-Git -Arguments @('cat-file', 'blob', $objectId)).Bytes
        $destination = Get-ContainedPath -Root $scratch -RelativePath $path
        [void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($destination))
        [IO.File]::WriteAllBytes($destination, $bytes)
        $entries[$path] = [pscustomobject]@{ Mode = $mode; ObjectId = $objectId; Bytes = $bytes; Snapshot = $destination }
    }

    Invoke-WhitespaceFormatter -Root $scratch -Paths $paths
    $updates = [Text.StringBuilder]::new()
    $workingUpdates = @()
    foreach ($path in $paths) {
        $entry = $entries[$path]
        $formatted = [IO.File]::ReadAllBytes($entry.Snapshot)
        if (Test-SameBytes -Left $entry.Bytes -Right $formatted) { continue }

        $currentEntry = $utf8.GetString((Invoke-Git -Arguments @('ls-files', '--stage', '-z', '--', $path)).Bytes)
        $expectedEntry = "$($entry.Mode) $($entry.ObjectId) 0" + [char]9 + $path + [char]0
        if ($currentEntry -cne $expectedEntry) {
            throw 'The index changed during formatting. Review the staged changes before retrying the commit.'
        }
        $newId = $utf8.GetString((Invoke-Git -Arguments @('hash-object', '-w', '--path', $path, '--stdin') -InputBytes $formatted).Bytes).Trim()
        if ($newId -eq $entry.ObjectId) { continue }
        $clean = Invoke-Git -Arguments @('diff', '--quiet', '--', $path) -AllowedExitCodes @(0, 1)
        if ($clean.ExitCode -eq 0) {
            $workingPath = Get-ContainedPath -Root $RepositoryRoot -RelativePath $path
            $workingUpdates += [pscustomobject]@{
                Path = $workingPath
                Before = [IO.File]::ReadAllBytes($workingPath)
                After = $formatted
            }
        }
        # Update only the formatted index blob, never git-add a partially staged working file.
        [void]$updates.Append("$($entry.Mode) $newId" + [char]9 + $path + [char]0)
        Write-Host "Formatted staged C#: $path"
    }
    if ($updates.Length -gt 0) {
        [void](Invoke-Git -Arguments @('update-index', '-z', '--index-info') -InputBytes $utf8.GetBytes($updates.ToString()))
        foreach ($update in $workingUpdates) {
            if (-not (Test-SameBytes -Left $update.Before -Right ([IO.File]::ReadAllBytes($update.Path)))) {
                # Concurrent editor changes are preserved; the already-formatted index remains authoritative.
                throw 'A working file changed during formatting. Its content was preserved; review it before retrying the commit.'
            }
            [IO.File]::WriteAllBytes($update.Path, $update.After)
        }
    }
}
finally {
    Pop-Location
    if ($null -ne $scratch -and (Test-Path -LiteralPath $scratch)) {
        # Delete only the generated task directory after verifying its resolved path under artifacts/format.
        $resolvedScratch = (Resolve-Path -LiteralPath $scratch).Path
        $expectedScratch = Get-ContainedPath -Root $scratchParent -RelativePath ([IO.Path]::GetFileName($scratch))
        if (-not [string]::Equals($resolvedScratch, $expectedScratch, [StringComparison]::OrdinalIgnoreCase)) {
            throw 'Refusing to remove an unexpected formatting directory.'
        }
        Remove-Item -LiteralPath $resolvedScratch -Recurse -Force
    }
}
