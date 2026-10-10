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
Exercises automatic formatting in a disposable Git index without creating commits.
#>
[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$fixtureParent = [IO.Path]::GetFullPath((Join-Path $repositoryRoot 'artifacts/format-hook-tests'))
$fixture = Join-Path $fixtureParent ([Guid]::NewGuid().ToString('N'))
$utf8 = [Text.UTF8Encoding]::new($false)

function Assert-Condition {
    param([bool]$Condition, [string]$Message)
    if (-not $Condition) { throw $Message }
}

function Invoke-FixtureGit {
    param([string[]]$Arguments)
    $result = & git -C $fixture @Arguments
    if ($LASTEXITCODE -ne 0) { throw "Fixture Git command failed: $($Arguments[0])" }
    return $result
}

function Get-FixtureBlobText {
    param([string]$Path)
    $start = [Diagnostics.ProcessStartInfo]::new('git')
    $start.WorkingDirectory = $fixture
    $start.UseShellExecute = $false
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    $start.StandardOutputEncoding = $utf8
    foreach ($argument in @('show', ":$Path")) { $start.ArgumentList.Add($argument) }
    $process = [Diagnostics.Process]::new()
    $process.StartInfo = $start
    try {
        # Read the blob without PowerShell's line splitting so CRLF and final-newline assertions inspect its actual text.
        [void]$process.Start()
        $outputTask = $process.StandardOutput.ReadToEndAsync()
        $errorTask = $process.StandardError.ReadToEndAsync()
        $process.WaitForExit()
        $output = $outputTask.GetAwaiter().GetResult()
        $errorText = $errorTask.GetAwaiter().GetResult()
        if ($process.ExitCode -ne 0) { throw "Could not read the fixture index blob: $errorText" }
        return $output
    }
    finally {
        $process.Dispose()
    }
}

function Assert-CSharpWhitespace {
    param([string]$Text, [string]$Description)
    Assert-Condition ($Text -match '(?m)^ {4}public int Value = 1;') "$Description does not use four-space indentation and C# token spacing."
    Assert-Condition (-not $Text.Contains("`t")) "$Description still contains indentation tabs."
    Assert-Condition (-not $Text.Contains("`r")) "$Description does not use LF line endings."
    Assert-Condition ($Text.EndsWith("`n", [StringComparison]::Ordinal)) "$Description is missing its final newline."
    Assert-Condition ($Text -notmatch '(?m)[ \t]+$') "$Description still contains trailing whitespace."
}

try {
    [void][IO.Directory]::CreateDirectory((Join-Path $fixture 'scripts'))
    [void][IO.Directory]::CreateDirectory((Join-Path $fixture '.githooks'))
    [void][IO.Directory]::CreateDirectory((Join-Path $fixture 'space name'))
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Format-Code.ps1') -Destination (Join-Path $fixture 'scripts/Format-Code.ps1')
    Copy-Item -LiteralPath (Join-Path $repositoryRoot '.githooks/pre-commit') -Destination (Join-Path $fixture '.githooks/pre-commit')
    Copy-Item -LiteralPath (Join-Path $repositoryRoot '.gitattributes') -Destination (Join-Path $fixture '.gitattributes')
    Invoke-FixtureGit -Arguments @('init', '--quiet')
    Invoke-FixtureGit -Arguments @('config', 'core.autocrlf', 'true')
    Invoke-FixtureGit -Arguments @('config', 'core.hooksPath', '.githooks')

    $rules = [IO.File]::ReadAllText((Join-Path $repositoryRoot '.editorconfig'))
    $fullPath = Join-Path $fixture 'space name/Full à.cs'
    $partialPath = Join-Path $fixture 'Partial.cs'
    $fullSource = "// SPDX-License-Identifier: MIT`r`npublic class Full`r`n{  `r`n`tpublic int Value=1; `t`r`n}  "
    $partialSource = "// SPDX-License-Identifier: MIT`r`npublic class Partial`r`n{  `r`n`tpublic int Value=1; `t`r`n}  "
    [IO.File]::WriteAllText((Join-Path $fixture '.editorconfig'), $rules, $utf8)
    [IO.File]::WriteAllText($fullPath, $fullSource, $utf8)
    [IO.File]::WriteAllText($partialPath, $partialSource, $utf8)
    Invoke-FixtureGit -Arguments @('add', '--', '.editorconfig', '.gitattributes', 'space name/Full à.cs', 'Partial.cs')

    # Both unstaged source edits and unstaged formatting rules must stay out of the index.
    $unstagedSource = $partialSource + "`r`n// UNSTAGED_CHANGE`r`n"
    [IO.File]::WriteAllText($partialPath, $unstagedSource, $utf8)
    [IO.File]::WriteAllText((Join-Path $fixture '.editorconfig'), $rules.Replace('indent_size = 4', 'indent_size = 8'), $utf8)
    $partialBefore = [Convert]::ToBase64String([IO.File]::ReadAllBytes($partialPath))

    Invoke-FixtureGit -Arguments @('hook', 'run', 'pre-commit')
    $fullAfter = [IO.File]::ReadAllText($fullPath)
    $stagedFull = Get-FixtureBlobText -Path 'space name/Full à.cs'
    $stagedPartial = Get-FixtureBlobText -Path 'Partial.cs'
    Assert-CSharpWhitespace -Text $fullAfter -Description 'The fully staged working file'
    Assert-CSharpWhitespace -Text $stagedFull -Description 'The fully staged index blob'
    Assert-CSharpWhitespace -Text $stagedPartial -Description 'The partially staged index blob'
    Assert-Condition (-not $stagedPartial.Contains('UNSTAGED_CHANGE')) 'Unstaged content entered the index.'
    Assert-Condition ($partialBefore -ceq [Convert]::ToBase64String([IO.File]::ReadAllBytes($partialPath))) 'The partially staged working file changed.'
    Write-Host 'PASS: repository whitespace rules, LF/final newline, automatic/partial staging, staged rules, spaces and Unicode filenames.'

    $indexBefore = (Invoke-FixtureGit -Arguments @('ls-files', '--stage')) -join "`n"
    Invoke-FixtureGit -Arguments @('hook', 'run', 'pre-commit')
    Assert-Condition ($indexBefore -ceq ((Invoke-FixtureGit -Arguments @('ls-files', '--stage')) -join "`n")) 'A second hook run changed an already formatted index.'
    Write-Host 'PASS: formatting is idempotent.'

    # Read-only verification must fail on malformed source without modifying either copy.
    $failureOutput = & pwsh -NoProfile -File (Join-Path $PSScriptRoot 'Format-Code.ps1') -RepositoryRoot $fixture -Verify 2>&1
    Assert-Condition ($LASTEXITCODE -ne 0) 'Read-only verification accepted malformed source.'
    Assert-Condition ($failureOutput.Count -gt 0) 'Formatting failure did not explain the problem.'
    Assert-Condition ($partialBefore -ceq [Convert]::ToBase64String([IO.File]::ReadAllBytes($partialPath))) 'Read-only verification edited a source file.'
    Assert-Condition ($indexBefore -ceq ((Invoke-FixtureGit -Arguments @('ls-files', '--stage')) -join "`n")) 'Read-only verification changed the index.'
    Write-Host 'PASS: verification fails safely without edits.'

    & pwsh -NoProfile -File (Join-Path $PSScriptRoot 'Format-Code.ps1') -RepositoryRoot $fixture
    Assert-Condition ($LASTEXITCODE -eq 0) 'Manual whitespace formatting failed.'
    & pwsh -NoProfile -File (Join-Path $PSScriptRoot 'Format-Code.ps1') -RepositoryRoot $fixture -Verify
    Assert-Condition ($LASTEXITCODE -eq 0) 'Formatted working files do not pass the CI verification command.'
    Assert-Condition ([IO.File]::ReadAllText($partialPath).Contains('UNSTAGED_CHANGE')) 'Manual formatting lost an unstaged edit.'
    Assert-Condition ($indexBefore -ceq ((Invoke-FixtureGit -Arguments @('ls-files', '--stage')) -join "`n")) 'Manual formatting staged changes.'
    Write-Host 'PASS: manual formatting and CI verification agree, without staging edits.'

    Invoke-FixtureGit -Arguments @('read-tree', '--empty')
    Invoke-FixtureGit -Arguments @('hook', 'run', 'pre-commit')
    Write-Host 'PASS: an empty index needs no formatting.'
}
finally {
    if (Test-Path -LiteralPath $fixture) {
        # Keep cleanup confined to the unique fixture, never the repository or its parent.
        $resolvedFixture = (Resolve-Path -LiteralPath $fixture).Path
        if (-not $resolvedFixture.StartsWith($fixtureParent + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
            throw 'Refusing to remove a fixture outside artifacts/format-hook-tests.'
        }
        Remove-Item -LiteralPath $resolvedFixture -Recurse -Force
    }
}
