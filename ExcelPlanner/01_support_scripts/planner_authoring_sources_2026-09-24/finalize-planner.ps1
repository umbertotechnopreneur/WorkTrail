param(
 [string]$WorkbookPath,
 [string]$PdfPath,
 [string]$ExtendedPdfPath
)
$ErrorActionPreference='Stop'
$excel=$null; $book=$null
try {
 $excel=New-Object -ComObject Excel.Application
 $excel.Visible=$false; $excel.DisplayAlerts=$false; $excel.EnableEvents=$false; $excel.AutomationSecurity=3
 $book=$excel.Workbooks.Open($WorkbookPath,0,$false)
 $book.AutoSaveOn=$false
 $book.Worksheets.Item('Parametri').Range('B25').Value2=[string](Join-Path (Split-Path -Parent $WorkbookPath) 'Calendar data')
 Write-Output "Opened: $($book.Worksheets.Count) sheets; Excel $($excel.Version)"
 $excel.Calculation=-4135
 $calc=$book.Worksheets.Item('Calcoli'); $info=$book.Worksheets.Item('Info')
 $formulas=$calc.Range('A1:AN88').Formula
 $moonFormula=$info.Range('B3').Formula; $saintFormula=$info.Range('B4').Formula
 $names=@{
  PlannerDate='=Parametri!$C$5'; StartLocalTime='=Parametri!$C$6'; ExtendedStepMinutes='=Parametri!$C$7';
  ReserveBeforeHours='=Parametri!$J$5';ReserveAfterHours='=Parametri!$J$6';LocationLabels='=Parametri!$B$12:$B$18';
  ZoneKeys='=Parametri!$C$12:$C$18';CalendarKeys='=Parametri!$D$12:$D$18';ActivityStart='=Parametri!$E$12:$E$18';
  ActivityEnd='=Parametri!$F$12:$F$18';WeeklyRestDays='=Parametri!$G$12:$M$18';DataFolder='=Parametri!$B$25';
  CalendarCodes='=Calendari!$A$6:$A$15';SchemaVersion='=1';AvailabilityGrid='=Planner!$B$17:$H$40';AvailabilityStates='=Calcoli!$R$17:$X$40'
 }
 foreach($n in $names.GetEnumerator()){$null=$book.Names.Add($n.Key,$n.Value)}
 $definitions=@(
  @{Name='ImportedHolidays';File='Calendars.csv';Sheet='Da_file';Cell='A5';Headers='"Calendar","Date","Name","Active","Source"';Types='{{"Calendar", type text},{"Date", type date},{"Name", type text},{"Active", Int64.Type},{"Source", type text}}';Extra='Validated = if Table.RowCount(Table.SelectRows(Typed, each [Calendar] = null or Text.Trim([Calendar]) = "" or [Date] = null or not List.Contains({0,1}, [Active]))) > 0 then error "Invalid holiday row" else Typed';Result='Validated'},
  @{Name='ZoneCatalog';File='Cities.csv';Sheet='Fusi';Cell='A5';Headers='"Code","City","WindowsId","IanaId"';Types='{{"Code", type text},{"City", type text},{"WindowsId", type text},{"IanaId", type text}}';Extra='Validated = if Table.RowCount(Typed) <> Table.RowCount(Table.Distinct(Typed,{"Code"})) or Table.RowCount(Table.SelectRows(Typed, each [Code] = null or Text.Trim([Code]) = "")) > 0 then error "Duplicate or empty zone code" else Typed';Result='Validated'},
  @{Name='ZonePeriods';File='DST_periods.csv';Sheet='Fusi';Cell='J5';Headers='"Code","FromUtc","UntilUtc","OffsetHours"';Types='{{"Code", type text},{"FromUtc", type datetime},{"UntilUtc", type datetime},{"OffsetHours", type number}}';Extra='Validated = if Table.RowCount(Table.SelectRows(Typed, each [FromUtc] >= [UntilUtc] or [OffsetHours] < -14 or [OffsetHours] > 14)) > 0 then error "Invalid timezone period" else Table.Sort(Typed,{{"Code",Order.Ascending},{"FromUtc",Order.Ascending}})';Result='Validated'},
  @{Name='Saints';File='Saints.csv';Sheet='Info';Cell='A7';Headers='"Month","Day","Name","Source"';Types='{{"Month", Int64.Type},{"Day", Int64.Type},{"Name", type text},{"Source", type text}}';Extra='Checked = Table.AddColumn(Typed,"Key", each Date.Month(#date(2000,[Month],[Day]))*100+[Day],Int64.Type), Validated = if Table.RowCount(Checked) <> Table.RowCount(Table.Distinct(Checked,{"Key"})) then error "Use one row per month/day" else Table.ReorderColumns(Checked,{"Key","Month","Day","Name","Source"})';Result='Validated'},
  @{Name='MoonPhases';File='Moon_phases.csv';Sheet='Info';Cell='G7';Headers='"Utc","Phase","Between","Source"';Types='{{"Utc", type datetime},{"Phase", type text},{"Between", type text},{"Source", type text}}';Extra='Validated = if Table.RowCount(Typed) <> Table.RowCount(Table.Distinct(Typed,{"Utc"})) then error "Duplicate lunar instant" else Table.Sort(Typed,{{"Utc",Order.Ascending}})';Result='Validated'}
 )
 foreach($d in $definitions){
  $sh=$book.Worksheets.Item($d.Sheet)
  $old=$sh.ListObjects.Item($d.Name);$range=$old.Range;$old.Unlist();$range.ClearContents()
  $m=@"
let
 Folder = Text.TrimEnd(Text.From(Excel.CurrentWorkbook(){[Name="DataFolder"]}[Content]{0}[Column1]), {"/", "\"}),
 Raw = Csv.Document(File.Contents(Folder & "/$($d.File)"), [Delimiter=";", Encoding=65001, QuoteStyle=QuoteStyle.Csv]),
 Headers = Table.PromoteHeaders(Raw, [PromoteAllScalars=true]),
 Required = {$($d.Headers)},
 CheckedHeaders = if List.Sort(Table.ColumnNames(Headers)) <> List.Sort(Required) then error "Unexpected CSV columns: $($d.File)" else Headers,
 Typed = Table.TransformColumnTypes(CheckedHeaders, $($d.Types), "en-US"),
 $($d.Extra)
in $($d.Result)
"@
  $null=$book.Queries.Add($d.Name,$m,"Local CSV; manual refresh; schema 1")
  $connection='OLEDB;Provider=Microsoft.Mashup.OleDb.1;Data Source=$Workbook$;Location='+$d.Name+';Extended Properties=""'
  $list=$sh.ListObjects.Add(3,$connection,[Type]::Missing,1,$sh.Range($d.Cell))
  $list.Name=$d.Name
  $qt=$list.QueryTable;$qt.CommandType=2;$qt.CommandText="SELECT * FROM [$($d.Name)]";$qt.BackgroundQuery=$false;$qt.RefreshOnFileOpen=$false;$qt.AdjustColumnWidth=$false;$qt.PreserveFormatting=$true;$qt.RefreshStyle=0
  if(-not $qt.Refresh($false)){throw "Refresh cancelled: $($d.Name)"}
  if($qt.FetchedRowOverflow){throw "CSV too large: $($d.Name)"}
  Write-Output "CSV loaded $($d.File): $($list.ListRows.Count) rows"
 }
 # Reconnect the formulas after native query tables replace the artifact-authored snapshots.
 $calc.Range('A1:AN88').Formula=$formulas
 $info.Range('B3').Formula=$moonFormula;$info.Range('B4').Formula=$saintFormula
 $null=$book.Names.Add('AvailableZoneCodes','=ZoneCatalog[Code]')
 $settings=$book.Worksheets.Item('Parametri')
 foreach($v in @(@('C12:C18','=AvailableZoneCodes'),@('D12:D18','=CalendarCodes'))){
  $validation=$settings.Range($v[0]).Validation;$validation.Delete();$validation.Add(3,1,1,$v[1]);$validation.InCellDropdown=$true;$validation.ShowError=$true;$validation.ErrorTitle='Valore non valido';$validation.ErrorMessage='Seleziona un codice presente nel menu.'
 }
 foreach($address in @('C5','C6','C7','E12:F18','J5:J6','G12:M18')){$settings.Range($address).Validation.ShowError=$true}
 Write-Output "Imported dates: $($book.Worksheets.Item('Da_file').Range('B6').Text); format $($book.Worksheets.Item('Da_file').Range('B6').NumberFormat); protected $($book.Worksheets.Item('Da_file').ProtectContents)"
 $settings.Range('B25').ShrinkToFit=$true
 $calc.Visible=0
 foreach($s in @('Planner','Estesa')){
  $sh=$book.Worksheets.Item($s);$sh.Range('J:X').EntireColumn.Hidden=$true
  $ps=$sh.PageSetup;$ps.PaperSize=9;$ps.Orientation=2;$ps.PrintGridlines=$false;$ps.PrintHeadings=$false
  $ps.LeftMargin=$excel.InchesToPoints(0.18);$ps.RightMargin=$excel.InchesToPoints(0.18);$ps.TopMargin=$excel.InchesToPoints(0.18);$ps.BottomMargin=$excel.InchesToPoints(0.22);$ps.HeaderMargin=$excel.InchesToPoints(0.05);$ps.FooterMargin=$excel.InchesToPoints(0.08)
  $ps.CenterHorizontally=$true;$ps.RightFooter='&"Arial"&8&P / &N';$ps.LeftFooter='&"Arial"&8WorkTrail · Planner indipendente'
  if($s -eq 'Planner'){$ps.PrintArea='$A$1:$H$42';$ps.Zoom=$false;$ps.FitToPagesWide=1;$ps.FitToPagesTall=1}
  else{$ps.PrintArea='$A$1:$H$66';$ps.PrintTitleRows='$1:$16';$ps.Zoom=84;$sh.ResetAllPageBreaks();$null=$sh.HPageBreaks.Add($sh.Range('A41'))}
  $sh.Range('B13:H13').ShrinkToFit=$true
 }
 foreach($sh in $book.Worksheets){$sh.Activate();$excel.ActiveWindow.DisplayGridlines=$false;$excel.ActiveWindow.DisplayHeadings=$true;$excel.ActiveWindow.Zoom=90}
 $excel.Calculation=-4105;$excel.CalculateFullRebuild()
 $errors=@()
 foreach($sh in $book.Worksheets){
  $vals=$sh.UsedRange.Value2
  if($vals -is [Array]){foreach($v in $vals){if($v -is [System.Runtime.InteropServices.ErrorWrapper]){$errors+=$sh.Name}}}
  try{$er=$sh.UsedRange.SpecialCells(-4123,16);if($er.Count -gt 0){$errors+="$($sh.Name):$($er.Address())"}}catch{if($_.Exception.Message -notmatch 'No cells|nessuna cella|Non è stata trovata alcuna cella|No se|0x800A03EC'){throw}}
 }
 if($errors.Count -gt 0){throw "Formula errors: $($errors -join '; ')"}
 $book.Worksheets.Item('Planner').ExportAsFixedFormat(0,$PdfPath)
 $book.Worksheets.Item('Estesa').ExportAsFixedFormat(0,$ExtendedPdfPath)
 $settings.Activate();$settings.Range('C5').Select();$excel.ActiveWindow.ScrollRow=1;$excel.ActiveWindow.ScrollColumn=1
 $book.Save()
 Write-Output "Saved workbook and PDFs. Info: $($info.Range('B3').Text)"
} catch {Write-Error ("At line $($_.InvocationInfo.ScriptLineNumber): "+$_.Exception.Message);exit 1}
finally{
 if($book){$book.Close($false);[void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($book)}
 if($excel){$excel.Quit();[void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($excel)}
 [GC]::Collect();[GC]::WaitForPendingFinalizers()
}
