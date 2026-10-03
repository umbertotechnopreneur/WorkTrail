$ErrorActionPreference='Stop'
$reportPath=Join-Path $PSScriptRoot 'verifiche\native-verification.json'
$report=Get-Content -LiteralPath $reportPath -Raw | ConvertFrom-Json -AsHashtable
$live=$report.PublishedWorkbook
$before=(Get-FileHash -LiteralPath $live).Hash
$copy=Join-Path $report.WorkingFolder 'Final_Verification.xlsx'
Copy-Item -LiteralPath $live -Destination $copy
$excel=$null;$book=$null;$results=@()
try {
    $excel=New-Object -ComObject Excel.Application
    $excel.Visible=$false;$excel.DisplayAlerts=$false;$excel.EnableEvents=$false;$excel.AutomationSecurity=3
    $oldMap=$excel.MapPaperSize;$excel.MapPaperSize=$false
    $book=$excel.Workbooks.Open($copy,0,$false);$book.Queries.FastCombine=$true
    if($book.AutoSaveOn){$book.AutoSaveOn=$false}
    if(-not [string]::IsNullOrEmpty([string]$book.Names.Item('WeatherApiKey').RefersToRange.Value2)){throw 'Expected blank delivered key field'}
    foreach($sheet in $book.Worksheets){foreach($table in $sheet.ListObjects){if($table.SourceType -eq 3){
        if(-not $table.QueryTable.Refresh($false)){throw 'Live-folder refresh failed'}
        $results+=@{Table=$table.Name;Rows=$table.ListRows.Count}
    }}}
    $excel.CalculateUntilAsyncQueriesDone();$excel.CalculateFullRebuild()
    foreach($sheet in $book.Worksheets){
        try{$errors=$sheet.UsedRange.SpecialCells(-4123,16);if($errors.Count -gt 0){throw "Formula errors in $($sheet.Name)"}}
        catch{if($_.Exception.Message -notmatch 'No cells were found|Non è stata trovata alcuna cella'){throw}}
    }
    # Preserve the already verified two-page fit; do not replace it with fixed scaling.
    $extended=$book.Worksheets.Item('Estesa')
    $extended.ExportAsFixedFormat(0,(Join-Path $PSScriptRoot 'verifiche\Estesa_A4.pdf'))
    $book.Worksheets.Item('Copertina').Activate()
    $book.Save();$book.Close($false);[void][Runtime.InteropServices.Marshal]::ReleaseComObject($book);$book=$null
}finally{
    if($book){$book.Close($false);[void][Runtime.InteropServices.Marshal]::ReleaseComObject($book)}
    if($excel){$excel.MapPaperSize=$oldMap;$excel.Quit();[void][Runtime.InteropServices.Marshal]::ReleaseComObject($excel)}
    [GC]::Collect();[GC]::WaitForPendingFinalizers()
}
if((Get-FileHash -LiteralPath $live).Hash -ne $before){throw 'Live workbook changed during final verification'}
$lock=[IO.File]::Open($live,[IO.FileMode]::Open,[IO.FileAccess]::Read,[IO.FileShare]::None);$lock.Dispose()
$paginationBackup=Join-Path $report.PublicationBackup 'Planner_before_final_pagination.xlsx'
if(-not(Test-Path -LiteralPath $paginationBackup)){Copy-Item -LiteralPath $live -Destination $paginationBackup}
Copy-Item -LiteralPath $copy -Destination $live -Force
$report.FinalRefresh=$results;$report.FinalFormulaErrors=0;$report.FinalWorkbookSha256=(Get-FileHash -LiteralPath $live).Hash
$report.FinalVerificationAtUtc=[datetime]::UtcNow.ToString('o')
$report | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $reportPath -Encoding utf8
Write-Output ('Final live-folder refresh passed: '+($results | ConvertTo-Json -Compress))
