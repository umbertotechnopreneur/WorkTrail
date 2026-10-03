$ErrorActionPreference='Stop'
$path=Join-Path $PSScriptRoot 'verifiche\native-verification.json'
$report=Get-Content -LiteralPath $path -Raw | ConvertFrom-Json -AsHashtable
$excel=$null;$book=$null
try {
    $excel=New-Object -ComObject Excel.Application
    $excel.Visible=$false;$excel.DisplayAlerts=$false;$excel.EnableEvents=$false;$excel.AutomationSecurity=3
    $oldMap=$excel.MapPaperSize;$excel.MapPaperSize=$false
    $book=$excel.Workbooks.Open($report.Workbook,0,$false)
    $book.Queries.FastCombine=$true
    if(-not [string]::IsNullOrEmpty([string]$book.Names.Item('WeatherApiKey').RefersToRange.Value2)){throw 'Key must remain blank in the deliverable'}
    $finalDataFolder=[string]$book.Names.Item('DataFolder').RefersToRange.Value2
    $book.Names.Item('DataFolder').RefersToRange.Value2=Join-Path $report.WorkingFolder 'Calendar data'
    $book.Queries.Item('Weather').Formula=Get-Content -LiteralPath (Join-Path $PSScriptRoot 'weather-agent\Weather.query.pq') -Raw
    if(-not $book.Worksheets.Item('Info').ListObjects.Item('Weather').QueryTable.Refresh($false)){throw 'Final weather CSV verification cancelled'}
    $book.Names.Item('DataFolder').RefersToRange.Value2=$finalDataFolder
    $excel.CalculateFullRebuild()
    $info=$book.Worksheets.Item('Info')
    $settings=$book.Worksheets.Item('Parametri')
    $settings.Range('A32').Value2='API'
    $settings.Range('B31:F31').Merge();$settings.Range('B31').Value2='Chiave API OpenWeather';$settings.Range('B31:F31').Font.Bold=$true
    $info.Range('B:C').ColumnWidth=19;$info.Range('F:F').ColumnWidth=30
    $info.ListObjects.Item('Weather').TableStyle=''
    $info.Range('A7:I7').Interior.Color=5061411;$info.Range('A7:I7').Font.Color=16777215;$info.Range('A7:I7').Font.Bold=$true
    $info.Range('A7:I14').Font.Size=11
    foreach($r in 8..14){$info.Range("A${r}:I${r}").Interior.Color=$(if($r%2 -eq 0){16119285}else{16777215})}
    $info.Range('A7:I7').WrapText=$true
    $info.Range('A7:I14').RowHeight=30
    $info.ListObjects.Item('Saints').ListColumns.Item('Name').DataBodyRange.EntireRow.AutoFit() | Out-Null
    $info.Range('A19:J19').Merge();$info.Range('A19').Value2='Fast Combine attivo per questo prototipo; nessuna modifica alla privacy globale di Excel.'
    $info.Range('A19:J19').Font.Size=10;$info.Range('A19:J19').RowHeight=24
    $book.Queries.FastCombine=$true
    foreach($spec in @(@('Parametri','$A$1:$M$36','Parametri_preview.pdf'),@('Info','$A$6:$I$19','Meteo_preview.pdf'))) {
        $s=$book.Worksheets.Item($spec[0]);$area=$s.PageSetup.PrintArea;$zoom=$s.PageSetup.Zoom;$orientation=$s.PageSetup.Orientation
        $s.PageSetup.PrintArea=$spec[1];$s.PageSetup.PaperSize=9;$s.PageSetup.Orientation=2;$s.PageSetup.Zoom=$false;$s.PageSetup.FitToPagesWide=1;$s.PageSetup.FitToPagesTall=1
        $s.ExportAsFixedFormat(0,(Join-Path $PSScriptRoot ('verifiche\'+$spec[2])))
        $s.PageSetup.PrintArea=$area;$s.PageSetup.Zoom=$zoom;$s.PageSetup.Orientation=$orientation
    }
    $book.Worksheets.Item('Copertina').Activate()
    $book.Save();$book.Close($false);[void][Runtime.InteropServices.Marshal]::ReleaseComObject($book);$book=$null
    $book=$excel.Workbooks.Open($report.Workbook,0,$true)
    $report.FastCombineAfterReopen=[bool]$book.Queries.FastCombine
    $report.Scenarios=@($report.Scenarios | Where-Object {$null -ne $_ -and $_ -is [Collections.IDictionary]})
    $report.ScenarioCount=$report.Scenarios.Count
    $report.WorkbookSha256=(Get-FileHash -LiteralPath $report.Workbook).Hash
    $report | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $path -Encoding utf8
    Write-Output "Polished staging. $($report.ScenarioCount) scenario assertions passed. FastCombine after reopen: $($report.FastCombineAfterReopen)"
}
finally {
    if($book){$book.Close($false);[void][Runtime.InteropServices.Marshal]::ReleaseComObject($book)}
    if($excel){$excel.MapPaperSize=$oldMap;$excel.Quit();[void][Runtime.InteropServices.Marshal]::ReleaseComObject($excel)}
    [GC]::Collect();[GC]::WaitForPendingFinalizers()
}
