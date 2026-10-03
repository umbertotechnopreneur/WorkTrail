param([switch]$Apply)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$root = Split-Path -Parent $PSScriptRoot
$workbookPath = Join-Path $root 'Planner_Availability_A4.xlsx'
$dataPath = Join-Path $root 'Calendar data'
$csvNames = @('Cities.csv', 'DST_periods.csv', 'Calendars.csv', 'Saints.csv', 'Moon_phases.csv')
foreach ($name in $csvNames) { if (-not (Test-Path -LiteralPath (Join-Path $dataPath $name) -PathType Leaf)) { throw "Missing CSV: $name" } }
if (-not $Apply) { Write-Output "Would update DataFolder to $dataPath and refresh five existing queries."; return }
$lock = [IO.File]::Open($workbookPath, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::None)
$lock.Dispose()
$backupPath = Join-Path $PSScriptRoot ('Planner_prima_percorso_' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '.xlsx')
if (Test-Path -LiteralPath $backupPath) { throw "Backup already exists: $backupPath" }
Copy-Item -LiteralPath $workbookPath -Destination $backupPath
$originalHash = (Get-FileHash -LiteralPath $workbookPath -Algorithm SHA256).Hash
# Work outside OneDrive so opening Excel cannot trigger a cloud autosave.
$workingPath = Join-Path ([IO.Path]::GetTempPath()) ('codex-planner-relocation-' + [guid]::NewGuid().ToString('N') + '.xlsx')
Copy-Item -LiteralPath $workbookPath -Destination $workingPath
$csvHashes = @($csvNames | ForEach-Object { Get-FileHash -LiteralPath (Join-Path $dataPath $_) -Algorithm SHA256 })

function Get-BookState($Book) {
    $sheets = foreach ($sheet in $Book.Worksheets) {
        $range = $sheet.UsedRange
        $cells = $range.Formula
        if ($sheet.Name -eq 'Parametri') { $cells[(25 - $range.Row + 1), (2 - $range.Column + 1)] = '<DataFolder>' }
        $flat = @($cells | ForEach-Object { $_ })
        $json = ConvertTo-Json -InputObject $flat -Depth 5 -Compress
        $hash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($json)))
        [ordered]@{Name=$sheet.Name; Address=$range.Address(); FormulaAndValueHash=$hash; ConditionalFormats=$range.FormatConditions.Count; Tables=$sheet.ListObjects.Count; Visible=$sheet.Visible}
    }
    $names = @($Book.Names | ForEach-Object { "$($_.Name)=$($_.RefersTo)" })
    $queries = @($Book.Queries | ForEach-Object { "$($_.Name)=$($_.Formula)" })
    return [ordered]@{Sheets=@($sheets); Names=$names; Queries=$queries; Connections=$Book.Connections.Count}
}

$excel = $null
$book = $null
$saved = $false
try {
    # Own hidden Excel process; do not attach to or close the user's session.
    $excel = New-Object -ComObject Excel.Application
    $excel.Visible = $false
    $excel.DisplayAlerts = $false
    $excel.EnableEvents = $false
    $excel.AutomationSecurity = 3
    $book = $excel.Workbooks.Open($workingPath, 0, $false)
    if ($book.ReadOnly) { throw 'Workbook opened read-only; no changes saved.' }
    if ($book.AutoSaveOn) { $book.AutoSaveOn = $false }
    $before = Get-BookState $book
    $oldFolder = [string]$book.Names.Item('DataFolder').RefersToRange.Value2
    $book.Names.Item('DataFolder').RefersToRange.Value2 = [string]$dataPath
    $tables = @(
        @{Sheet='Da_file'; Name='ImportedHolidays'},
        @{Sheet='Fusi'; Name='ZoneCatalog'},
        @{Sheet='Fusi'; Name='ZonePeriods'},
        @{Sheet='Info'; Name='Saints'},
        @{Sheet='Info'; Name='MoonPhases'}
    )
    $refresh = foreach ($table in $tables) {
        $list = $book.Worksheets.Item($table.Sheet).ListObjects.Item($table.Name)
        $rowsBefore = $list.ListRows.Count
        if (-not $list.QueryTable.Refresh($false)) { throw "Refresh cancelled: $($table.Name)" }
        if ($rowsBefore -ne $list.ListRows.Count) { throw "Unexpected row count change: $($table.Name)" }
        Write-Host "Refreshed $($table.Name): $($list.ListRows.Count) rows"
        [ordered]@{Table=$table.Name; Rows=$list.ListRows.Count; Refreshed=$true}
    }
    $excel.CalculateUntilAsyncQueriesDone()
    $excel.CalculateFullRebuild()
    $after = Get-BookState $book
    if ((ConvertTo-Json $before -Depth 10 -Compress) -cne (ConvertTo-Json $after -Depth 10 -Compress)) {
        throw 'Workbook content or structure changed beyond DataFolder; no changes saved.'
    }
    foreach ($entry in $csvHashes) {
        if ((Get-FileHash -LiteralPath $entry.Path -Algorithm SHA256).Hash -ne $entry.Hash) { throw "CSV changed during refresh: $($entry.Path)" }
    }
    $book.Save()
    $book.Close($false)
    [void][Runtime.InteropServices.Marshal]::ReleaseComObject($book)
    $book = $excel.Workbooks.Open($workingPath, 0, $true)
    if ([string]$book.Names.Item('DataFolder').RefersToRange.Value2 -cne $dataPath) { throw 'Saved DataFolder verification failed.' }
    if ((ConvertTo-Json (Get-BookState $book) -Depth 10 -Compress) -cne (ConvertTo-Json $after -Depth 10 -Compress)) { throw 'Reopened workbook differs from verified state.' }
    $book.Close($false)
    [void][Runtime.InteropServices.Marshal]::ReleaseComObject($book)
    $book = $null
    if ((Get-FileHash -LiteralPath $workbookPath -Algorithm SHA256).Hash -ne $originalHash) { throw 'Destination changed during verification; refusing to replace it.' }
    $lock = [IO.File]::Open($workbookPath, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::None)
    $lock.Dispose()
    Copy-Item -LiteralPath $workingPath -Destination $workbookPath -Force
    if ((Get-FileHash -LiteralPath $workbookPath).Hash -ne (Get-FileHash -LiteralPath $workingPath).Hash) { throw 'Final copy hash mismatch.' }
    $saved = $true
    $report = [ordered]@{Timestamp=(Get-Date -Format o); Workbook=$workbookPath; PreviousDataFolder=$oldFolder; DataFolder=$dataPath; Queries=$refresh; UnchangedContentAndStructure=$true; ReopenedAndVerified=$true; Backup=$backupPath; WorkingCopy=$workingPath}
    $report | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $PSScriptRoot 'relocation-verification.json') -Encoding utf8
    Write-Output 'Saved, reopened and verified. Only DataFolder changed; all five native Power Query refreshes succeeded.'
}
finally {
    if ($null -ne $book) { $book.Close($false); [void][Runtime.InteropServices.Marshal]::ReleaseComObject($book) }
    if ($null -ne $excel) { $excel.Quit(); [void][Runtime.InteropServices.Marshal]::ReleaseComObject($excel) }
    if (-not $saved) { Write-Warning "Update not verified. Original backup preserved at $backupPath" }
}
