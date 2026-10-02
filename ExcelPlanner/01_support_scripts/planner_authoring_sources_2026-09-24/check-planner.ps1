param([string]$WorkbookPath)
$ErrorActionPreference='Stop'
$qa=Join-Path $PSScriptRoot ('qa-'+[guid]::NewGuid().ToString('N'))
$null=New-Item -ItemType Directory -Path $qa
$copy=Join-Path $qa 'Planner-QA.xlsx';Copy-Item -LiteralPath $WorkbookPath -Destination $copy
$dataDir=Join-Path $qa 'Calendar data';$null=New-Item -ItemType Directory -Path $dataDir
foreach($f in @('Calendars.csv','Cities.csv','DST_periods.csv','Saints.csv','Moon_phases.csv')){Copy-Item -LiteralPath (Join-Path (Split-Path -Parent $WorkbookPath) "Calendar data\$f") -Destination (Join-Path $dataDir $f)}
$excel=$null;$book=$null;$script:checks=0
function Check([bool]$ok,[string]$label){if(-not $ok){throw "FAILED: $label"};$script:checks++;Write-Output "PASS $label"}
try{
 $excel=New-Object -ComObject Excel.Application;$excel.Visible=$false;$excel.DisplayAlerts=$false;$excel.EnableEvents=$false;$excel.AutomationSecurity=3
 $book=$excel.Workbooks.Open($copy,0,$false);if($book.AutoSaveOn){$book.AutoSaveOn=$false};$excel.Calculation=-4135
 $settings=$book.Worksheets.Item('Parametri');$calc=$book.Worksheets.Item('Calcoli');$planner=$book.Worksheets.Item('Planner');$info=$book.Worksheets.Item('Info')
 $baseRows=$settings.Range('B12:M18').Value2
 function Reset-Inputs{
  $settings.Range('B12:M18').Value2=$baseRows;$settings.Range('C5').Value2=([datetime]'2026-09-24').ToOADate();$settings.Range('C6').Value2=[double]0;$settings.Range('C7').Value2=[double]30;$settings.Range('J5:J6').Value2=[double]2;$excel.Calculate()
 }
 function State([int]$hour,[int]$expected,[string]$label){Check ($calc.Range("R$($hour+17)").Value2 -eq $expected) $label}
 Reset-Inputs
 Check ($book.Queries.Count -eq 5) 'five CSV queries'
 Check ($book.Worksheets.Item('Fusi').ListObjects.Item('ZoneCatalog').ListRows.Count -eq 206) '206 catalogue cities'
 Check ($book.Worksheets.Item('Fusi').ListObjects.Item('ZonePeriods').ListRows.Count -eq 670) '670 timezone periods'
 Check ($planner.Range('B17:H40').FormatConditions.Count -eq 4) 'four conditional rules on planner'
 Check ($info.Range('B4').Text -match 'Mercede') 'saint information shown for selected date'
 Check ($info.Range('B3').Text -match 'Gibbosa crescente.*Luna piena') 'lunar interval and next primary phase shown'
 State 0 1 'reserve wraps past midnight';State 1 0 'reserve end excluded';State 3 1 'reserve before begins';State 5 2 'activity start included';State 23 1 'activity end excluded'
 $settings.Range('E12').Value2=[double](9/24);$settings.Range('F12').Value2=[double](18/24);$excel.Calculate()
 State 7 1 '09-18 before reserve';State 9 2 '09-18 start';State 17 2 '09-18 last active hour';State 18 1 '09-18 after reserve';State 20 0 '09-18 after reserve end'
 $settings.Range('E12').Value2=[double](22/24);$settings.Range('F12').Value2=[double](6/24);$excel.Calculate()
 State 20 1 'overnight before reserve';State 22 2 'overnight start';State 0 2 'overnight midnight';State 6 1 'overnight after reserve';State 8 0 'overnight reserve end'
 $settings.Range('E12').Value2=[double](9/24);$settings.Range('F12').Value2=[double](18/24);$settings.Range('J5:J6').Value2=[double]0;$excel.Calculate()
 State 8 0 'zero margin before';State 9 2 'zero margin active';State 18 0 'zero margin after'
 $settings.Range('J5').Value2=[double]0;$settings.Range('J6').Value2=[double]2;$excel.Calculate();State 8 0 'asymmetric margin before';State 18 1 'asymmetric margin after'
 $settings.Range('E12').Value2=[double](9.5/24);$settings.Range('F12').Value2=[double](18.5/24);$excel.Calculate()
 Check ($calc.Range('R59').Value2 -eq 0) 'half-hour 09:00 before activity';Check ($calc.Range('R60').Value2 -eq 2) 'half-hour 09:30 start';Check ($calc.Range('R78').Value2 -eq 1) 'half-hour 18:30 end'
 Reset-Inputs;$settings.Range('C12').Value2='dhaka';$settings.Range('D12').Value2='BD';$settings.Range('K12').Value2=[double]1;$settings.Range('C5').Value2=([datetime]'2026-09-25').ToOADate();$excel.Calculate();State 12 0 'Friday rest in Dhaka'
 $settings.Range('C5').Value2=([datetime]'2026-09-24').ToOADate();$excel.Calculate();State 12 2 'Thursday in Dhaka still active'
 Reset-Inputs
 $cal=$book.Worksheets.Item('Calendari');$cal.Range('A20').Value2='VN';$cal.Range('B20').Value2=([datetime]'2026-09-24').ToOADate();$cal.Range('C20').Value2='QA local closure';$cal.Range('D20').Value2=[double]1;$cal.Range('E20').Value2='QA';$excel.Calculate()
 State 12 0 'manual holiday closes local day';Check ($calc.Range('Z29').Value2 -eq 1) 'manual holiday flag'
 $planner.Activate();$excel.CalculateFullRebuild()
 Write-Output "Holiday display: $($planner.Range('B29').Text), format=$($planner.Range('B29').DisplayFormat.NumberFormat), flag=$($planner.Range('R29').Value2), rule=$($planner.Range('B17:H40').FormatConditions.Item(4).Formula1)"
 Check ($planner.Range('B29').Text -match ' F$') 'holiday suffix conditional formatting'
 $cal.Range('A21:E21').Value2=$cal.Range('A20:E20').Value2;$excel.Calculate();Check ($calc.Range('Z29').Value2 -eq 2) 'duplicate holidays counted';Check ($planner.Range('B29').Text -match ' F$') 'duplicate holidays still show suffix'
 $cal.Range('A20:E21').ClearContents();$excel.Calculate();State 12 2 'saint info alone does not close activity'
 Reset-Inputs;$settings.Range('C12').Value2='rome';$settings.Range('C5').Value2=([datetime]'2026-03-29').ToOADate();$excel.Calculate()
 $h0=([datetime]::FromOADate($calc.Range('B17').Value2)).Hour;$h1=([datetime]::FromOADate($calc.Range('B18').Value2)).Hour;$h2=([datetime]::FromOADate($calc.Range('B19').Value2)).Hour
 Check ($h0 -eq 0 -and $h1 -eq 1 -and $h2 -eq 3) 'Rome spring-forward skips local 02:00'
 $settings.Range('C6').Value2=[double](2.5/24);$excel.Calculate();Check ($calc.Range('B4').Value2 -eq 0 -and $calc.Range('B7').Value2 -eq $false) 'nonexistent home time rejected'
 $settings.Range('C5').Value2=([datetime]'2026-10-25').ToOADate();$excel.Calculate();Check ($calc.Range('B4').Value2 -eq 2 -and $calc.Range('B7').Value2 -eq $false) 'ambiguous home time rejected'
 $settings.Range('C6').Value2=[double]0;$excel.Calculate()
 Check (([datetime]::FromOADate($calc.Range('B19').Value2)).Hour -eq 2 -and ([datetime]::FromOADate($calc.Range('B20').Value2)).Hour -eq 2) 'Rome fall-back repeats local 02:00'
 Reset-Inputs;$settings.Range('C5').Value2=([datetime]'2029-01-02').ToOADate();$excel.Calculate();Check ($calc.Range('B7').Value2 -eq $false) 'out-of-coverage home date rejected'
 Reset-Inputs;$settings.Range('E12').Value2=$settings.Range('F12').Value2;$excel.Calculate();State 12 -1 'equal activity endpoints rejected'
 Reset-Inputs;$settings.Range('G12').Value2=[double]2;$excel.Calculate();State 12 -1 'invalid weekly rest flag rejected'
 Reset-Inputs;$settings.Range('C7').Value2=[double]60;$excel.Calculate();Check ([math]::Abs(($calc.Range('A88').Value2-$calc.Range('A41').Value2)*24-47) -lt 0.00001) 'extended view supports 48 hourly instants'
 Reset-Inputs;$settings.Range('B25').Value2=[string]$dataDir
 $csv=Join-Path $dataDir 'Calendars.csv';$events=@(Import-Csv -LiteralPath $csv -Delimiter ';');$events+=[pscustomobject]@{Calendar='VN';Date='2026-09-24';Name='QA imported closure';Active=1;Source='QA'}
 $events|Export-Csv -LiteralPath $csv -Delimiter ';' -NoTypeInformation -Encoding utf8BOM
 $q=$book.Worksheets.Item('Da_file').ListObjects.Item('ImportedHolidays').QueryTable;$null=$q.Refresh($false);$excel.Calculate()
 State 12 0 'CSV add then refresh affects planner';Check ($book.Worksheets.Item('Da_file').ListObjects.Item('ImportedHolidays').ListRows.Count -eq 25) 'CSV table expands on refresh'
 $events[-1].Active=0;$events|Export-Csv -LiteralPath $csv -Delimiter ';' -NoTypeInformation -Encoding utf8BOM;$null=$q.Refresh($false);$excel.Calculate();State 12 2 'CSV edit then refresh removes closure'
 $settings.Range('B25').Value2=[string](Join-Path $qa 'missing');$failed=$false;try{$null=$q.Refresh($false)}catch{$failed=$true};Check $failed 'missing CSV folder fails explicitly';Check ($book.Worksheets.Item('Da_file').ListObjects.Item('ImportedHolidays').ListRows.Count -eq 25) 'failed refresh preserves cached rows (documented)'
 [pscustomobject]@{Passed=$script:checks;Workbook=$WorkbookPath;TemporaryCopy=$copy;When=(Get-Date).ToString('o')}|ConvertTo-Json|Set-Content -LiteralPath (Join-Path $qa 'result.json') -Encoding utf8
 Write-Output "ALL $script:checks CHECKS PASSED; temporary workbook not saved"
}catch{Write-Error ("At line $($_.InvocationInfo.ScriptLineNumber): "+$_.Exception.Message);exit 1}
finally{if($book){$book.Close($false);[void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($book)};if($excel){$excel.Quit();[void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($excel)};[GC]::Collect();[GC]::WaitForPendingFinalizers()}
