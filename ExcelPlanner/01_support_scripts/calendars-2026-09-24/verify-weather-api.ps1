# A disposable, never-saved workbook verifies the live API path. No secret is emitted.
param([Parameter(Mandatory)][string]$DataFolder,[Parameter(Mandatory)][string]$KeyFile,[Parameter(Mandatory)][string]$WorkbookPath,[switch]$Diagnose)
$ErrorActionPreference='Stop'
$excel=$null;$book=$null;$keyValue=$null
$report=[ordered]@{NativePowerQueryApiVerified=$false;WorkbookSaved=$false;LogicalCities=7;Rows=0;KeyPersistedInWorkbook=$false;Failure=$null}
try {
    $keyValue=[IO.File]::ReadAllText($KeyFile).Trim()
    if ($keyValue -notmatch '^[a-fA-F0-9]{32}$') { throw 'Invalid key file format' }
    $excel=New-Object -ComObject Excel.Application
    $excel.Visible=$false;$excel.DisplayAlerts=$false;$excel.EnableEvents=$false;$excel.AutomationSecurity=3
    if(-not [IO.Path]::GetFullPath($WorkbookPath).StartsWith([IO.Path]::GetTempPath(),[StringComparison]::OrdinalIgnoreCase)){throw 'API verification only accepts a local temporary workbook'}
    $book=$excel.Workbooks.Open($WorkbookPath,0,$false);$book.Queries.FastCombine=$true
    if($book.AutoSaveOn){$book.AutoSaveOn=$false}
    $book.Names.Item('WeatherApiKey').RefersToRange.Value2=$keyValue
    $book.Names.Item('DataFolder').RefersToRange.Value2=$DataFolder
    $m=Get-Content -LiteralPath (Join-Path $PSScriptRoot 'weather-agent\Weather.query.pq') -Raw
    if($Diagnose){
        $m=$m.Replace('if attempted[HasError] then Fail("WEATHER_API_ERROR") else attempted[Value]','if attempted[HasError] then {city[Code],null,null,null,null,Text.Replace(Text.From(attempted[Error][Message]),Key,"[KEY OMITTED]"),null,"api_error","diagnostic"} else attempted[Value]')
        $m=[regex]::Replace($m,'in\s+Result\s*$','in #table(type table [Diagnostic=text], {{let d=try Text.FromBinary(Json.FromValue(Result)) in if d[HasError] then Text.Replace(Text.From(d[Error][Message]),Key,"[KEY OMITTED]") else Text.Replace(d[Value],Key,"[KEY OMITTED]")}})')
    }
    $book.Queries.Item('Weather').Formula=$m
    $table=$book.Worksheets.Item('Info').ListObjects.Item('Weather');$query=$table.QueryTable
    $query.CommandType=2;$query.CommandText='SELECT * FROM [Weather]';$query.BackgroundQuery=$false;$query.RefreshOnFileOpen=$false
    $query.RefreshStyle=0
    $report.KeyCharactersBeforeRefresh=([string]$book.Names.Item('WeatherApiKey').RefersToRange.Value2).Length
    if (-not $query.Refresh($false)) { throw 'Refresh cancelled' }
    $excel.CalculateUntilAsyncQueriesDone()
    if($Diagnose){
        $report.Diagnostic=[regex]::Replace(([string]$table.DataBodyRange.Cells.Item(1,1).Value2).Replace($keyValue,'[KEY OMITTED]'),'https?://[^\s''"]+','[URL OMITTED]')
        $report.Mode='Diagnostic';$report.NativePowerQueryApiVerified=$false
        return
    }
    # Some Excel builds complete the query before the worksheet binding is materialized.
    $deadline=[datetime]::UtcNow.AddSeconds(15)
    do {
        $excel.CalculateFullRebuild()
        if (-not [string]::IsNullOrEmpty([string]$table.DataBodyRange.Cells.Item(1,1).Value2)) { break }
        Start-Sleep -Milliseconds 500
    } while ([datetime]::UtcNow -lt $deadline)
    if ($table.ListRows.Count -ne 7) { throw 'Wrong weather row count' }
    $report.KeyCharactersAfterRefresh=([string]$book.Names.Item('WeatherApiKey').RefersToRange.Value2).Length
    $report.OutputRange=$table.Range.Address()
    $report.Headers=@(foreach($col in $table.ListColumns){$col.Name.Replace($keyValue,'[KEY OMITTED]')})
    $report.FirstRow=@(foreach($cell in $table.DataBodyRange.Rows.Item(1).Cells){([string]$cell.Value2).Replace($keyValue,'[KEY OMITTED]')})
    $report.ReturnedStatuses=@(foreach($i in 1..7){([string]$table.DataBodyRange.Cells.Item($i,8).Value2).Replace($keyValue,'[KEY OMITTED]')})
    foreach($i in 1..7){if($table.DataBodyRange.Cells.Item($i,8).Value2 -ne 'api'){throw 'Unexpected weather status'}}
    $report.NativePowerQueryApiVerified=$true;$report.Rows=$table.ListRows.Count
} catch {
    # Never emit native errors: they may include authenticated request URLs.
    $report.Failure='Native API verification failed; no workbook saved. Check credentials or query setup interactively.'
    $safeMessage=[string]$_.Exception.Message
    if(-not [string]::IsNullOrEmpty($keyValue)){$safeMessage=$safeMessage.Replace($keyValue,'[KEY OMITTED]')}
    $safeMessage=[regex]::Replace($safeMessage,'https?://[^\s''"]+','[URL OMITTED]')
    $safeMessage=[regex]::Replace($safeMessage,'[a-fA-F0-9]{32}','[TOKEN OMITTED]')
    $report.Diagnostic=$safeMessage
    $report.StageLine=$_.InvocationInfo.ScriptLineNumber
}
finally {
    if($book){try{$book.Names.Item('WeatherApiKey').RefersToRange.MergeArea.ClearContents() | Out-Null}finally{$book.Close($false);[void][Runtime.InteropServices.Marshal]::ReleaseComObject($book)}}
    if($excel){$excel.Quit();[void][Runtime.InteropServices.Marshal]::ReleaseComObject($excel)}
    $keyValue=$null
    [GC]::Collect();[GC]::WaitForPendingFinalizers()
    if($Diagnose){$report | ConvertTo-Json -Depth 6}
}
$report | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $PSScriptRoot 'verifiche\weather-api-native.json') -Encoding utf8
$report | ConvertTo-Json
