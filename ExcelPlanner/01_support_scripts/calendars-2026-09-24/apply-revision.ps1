param([switch]$Apply)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$runRoot = $PSScriptRoot
$root = Split-Path -Parent (Split-Path -Parent $runRoot)
$liveBook = Join-Path $root 'Planner_Availability_A4.xlsx'
$liveData = Join-Path $root 'Calendar data'
$patchPath = Join-Path $runRoot 'verifiche\revision-patch.json'
$exceptionBook = Join-Path $runRoot 'verifiche\eccezioni.xlsx'
$coverPath = Join-Path $runRoot 'cover-agent\cover.xlsx'
$newSaints = Join-Path $runRoot 'saints-agent\Saints.csv'
$newWeather = Join-Path $runRoot 'weather-agent\Weather.csv'
$newCities = Join-Path $runRoot 'weather-agent\Cities.csv'
$weatherQueryPath = Join-Path $runRoot 'weather-agent\Weather.query.pq'
foreach ($path in @($liveBook,$patchPath,$exceptionBook,$coverPath,$newSaints,$newWeather,$newCities,$weatherQueryPath)) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Missing required input: $path" }
}
if (-not $Apply) { Write-Output 'Inputs present; would update a local copy, validate, then publish workbook and three CSVs.'; return }
$lock = [IO.File]::Open($liveBook,[IO.FileMode]::Open,[IO.FileAccess]::Read,[IO.FileShare]::None); $lock.Dispose()
$originalHash = (Get-FileHash -LiteralPath $liveBook).Hash
$working = Join-Path ([IO.Path]::GetTempPath()) ('codex-planner-national-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $working | Out-Null
$stageData = Join-Path $working 'Calendar data'; New-Item -ItemType Directory -Path $stageData | Out-Null
$workbookPath = Join-Path $working 'Planner_Availability_A4.xlsx'
Copy-Item -LiteralPath $liveBook -Destination $workbookPath
foreach ($name in @('DST_periods.csv','Moon_phases.csv')) { Copy-Item -LiteralPath (Join-Path $liveData $name) -Destination (Join-Path $stageData $name) }
Copy-Item -LiteralPath $newCities -Destination (Join-Path $stageData 'Cities.csv')
Copy-Item -LiteralPath (Join-Path $runRoot 'dati_preparati\Calendars.csv') -Destination (Join-Path $stageData 'Calendars.csv')
Copy-Item -LiteralPath $newSaints -Destination (Join-Path $stageData 'Saints.csv')
Copy-Item -LiteralPath $newWeather -Destination (Join-Path $stageData 'Weather.csv')
$backup = Join-Path $runRoot ('backup-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
New-Item -ItemType Directory -Path $backup | Out-Null
Copy-Item -LiteralPath $liveBook -Destination (Join-Path $backup 'Planner_Availability_A4.xlsx')
# Explicit CSV list: the API key is never copied into backups or reports.
foreach ($name in @('Calendars.csv','Saints.csv','Weather.csv','Cities.csv')) {
    if (Test-Path -LiteralPath (Join-Path $liveData $name)) { Copy-Item -LiteralPath (Join-Path $liveData $name) -Destination (Join-Path $backup $name) }
}
$patch = Get-Content -LiteralPath $patchPath -Raw | ConvertFrom-Json -AsHashtable
$manifest = Get-Content -LiteralPath (Join-Path $runRoot 'holiday-manifest.json') -Raw | ConvertFrom-Json
$nativeReport = Join-Path $runRoot 'verifiche\native-verification.json'
$excel = $null; $book = $null; $donor = $null
$oldMap = $null

function Set-Value($Sheet,[string]$Address,$Value) { $Sheet.Range($Address).Value2 = $Value }
function Add-Query($Book,[string]$Name,[string]$Formula,$Sheet,[string]$Cell) {
    $null = $Book.Queries.Add($Name,$Formula,'Blank key: local CSV. Optional user key: current OpenWeather data on refresh.')
    $connection = 'OLEDB;Provider=Microsoft.Mashup.OleDb.1;Data Source=$Workbook$;Location=' + $Name + ';Extended Properties=""'
    $table = $Sheet.ListObjects.Add(3,$connection,[Type]::Missing,1,$Sheet.Range($Cell))
    $table.Name = $Name
    $qt = $table.QueryTable
    $qt.CommandType = 2; $qt.CommandText = "SELECT * FROM [$Name]"
    $qt.BackgroundQuery = $false; $qt.RefreshOnFileOpen = $false
    $qt.AdjustColumnWidth = $false; $qt.PreserveFormatting = $true; $qt.RefreshStyle = 0
    return $table
}
function Apply-FormulaPatch($Sheet,$Entries) {
    foreach ($entry in $Entries.GetEnumerator()) { $Sheet.Range($entry.Key).Formula = $entry.Value }
}
function Check-FormulaErrors($Book) {
    $errors = @()
    foreach ($sheet in $Book.Worksheets) {
        try {
            $cells = $sheet.UsedRange.SpecialCells(-4123,16)
            if ($cells.Count -gt 0) { $errors += "$($sheet.Name):$($cells.Address())" }
        } catch {
            if ($_.Exception.Message -notmatch 'No cells were found|Non è stata trovata alcuna cella') { throw }
        }
    }
    if ($errors.Count) { throw "Unexpected formula errors: $($errors -join '; ')" }
}

try {
    $excel = New-Object -ComObject Excel.Application
    $excel.Visible = $false; $excel.DisplayAlerts = $false; $excel.EnableEvents = $false; $excel.AutomationSecurity = 3
    $oldMap = $excel.MapPaperSize; $excel.MapPaperSize = $false
    $book = $excel.Workbooks.Open($workbookPath,0,$false)
    if ($book.ReadOnly) { throw 'Working workbook is read-only' }
    # Explicitly authorized by the owner; affects this open workbook, not Excel globally.
    $book.Queries.FastCombine = $true
    if ($book.AutoSaveOn) { $book.AutoSaveOn = $false }
    if ($book.Queries.Count -ne 5) { throw 'Unexpected original query count' }
    $settings = $book.Worksheets.Item('Parametri'); $calc = $book.Worksheets.Item('Calcoli')
    # Reuse Excel's own locale-specific format instead of hard-coded English tokens.
    $nativeDateFormat = $settings.Range('C5').NumberFormatLocal
    $nativeDecimalFormat = '0' + $excel.International(3) + '0'
    $info = $book.Worksheets.Item('Info'); $calendar = $book.Worksheets.Item('Calendari')
    $originalInputs = [ordered]@{Parameters=$settings.Range('A12:M18').Formula; Date=$settings.Range('C5:C7').Formula; Margins=$settings.Range('J5:J6').Formula}
    $beforeManual = $calendar.ListObjects.Item('HolidayDates').DataBodyRange.Formula
    $book.Names.Item('DataFolder').RefersToRange.Value2 = [string]$stageData
    $excel.Calculation = -4135

    $donor = $excel.Workbooks.Open($exceptionBook,0,$true)
    $donor.Worksheets.Item('Eccezioni').Copy([Type]::Missing,$book.Worksheets.Item('Parametri'))
    $donor.Close($false); [void][Runtime.InteropServices.Marshal]::ReleaseComObject($donor); $donor = $null
    $donor = $excel.Workbooks.Open($coverPath,0,$true)
    $donor.Worksheets.Item('Copertina').Copy($book.Worksheets.Item(1))
    $donor.Close($false); [void][Runtime.InteropServices.Marshal]::ReleaseComObject($donor); $donor = $null

    # Native range insertion preserves the user's manual holiday table and names.
    $calendar.Range('A16:A25').EntireRow.Insert(-4121)
    $calendar.Range('A6:F6').Copy()
    $calendar.Range('A16:F25').PasteSpecial(-4122)
    $excel.CutCopyMode = $false
    $calendar.Range('A16:F25').ClearContents()
    foreach ($country in $manifest.countries) {
        $row = $null
        foreach ($r in 6..25) { if ([string]$calendar.Cells.Item($r,1).Value2 -eq $country.code) { $row=$r; break } }
        if ($null -eq $row) { foreach ($r in 6..25) { if ([string]::IsNullOrEmpty([string]$calendar.Cells.Item($r,1).Value2)) { $row=$r; break } } }
        if ($null -eq $row) { throw 'Calendar registry is full' }
        $calendar.Cells.Item($row,1).Value2 = [string]$country.code
        $calendar.Cells.Item($row,2).Value2 = [string]$country.name
        $calendar.Cells.Item($row,3).Value2 = [double]([datetime]'2026-01-01').ToOADate()
        $calendar.Cells.Item($row,4).Value2 = [double]([datetime]'2028-12-31').ToOADate()
        $calendar.Cells.Item($row,5).Value2 = [double]1
        $calendar.Cells.Item($row,6).Value2 = [string]($country.scope + '. holidays ' + $manifest.holiday_library + '; snapshot, non verifica ufficiale completa.')
    }
    $calendar.Range('C6:D25').NumberFormatLocal = $nativeDateFormat
    $calendar.Range('E5').Value2 = 'Dati 0/1'
    $calendar.Range('A2').Value2 = 'Base nazionale 2026-2028. Dati = 1 indica dati presenti, non certifica completezza o applicabilità aziendale.'
    $calendar.Range('A3').Value2 = 'Patroni locali: aggiungi date sotto con il calendario applicabile. Ferie e recuperi personali: usa Eccezioni.'
    $calendar.Range('A27').Value2 = 'DATE MANUALI: patroni o altre festività applicabili. Attiva = 1 chiude il giorno; Eccezioni può derogare.'
    $calendar.Range('F6:F25').WrapText = $true; $calendar.Range('A6:F25').RowHeight = 38
    $null = $book.Names.Add('CalendarCodes','=Calendari!$A$6:$A$25')
    $validation = $settings.Range('D12:D18').Validation
    $validation.Delete(); $validation.Add(3,1,1,'=CalendarCodes'); $validation.InCellDropdown=$true; $validation.ShowError=$true

    $holidayM = @'
let
 Folder = Text.TrimEnd(Text.From(Excel.CurrentWorkbook(){[Name="DataFolder"]}[Content]{0}[Column1]), {"/", "\"}),
 Raw = Csv.Document(File.Contents(Folder & "/Calendars.csv"), [Delimiter=";",Encoding=65001,QuoteStyle=QuoteStyle.Csv]),
 Headers = Table.PromoteHeaders(Raw,[PromoteAllScalars=true]),
 Required = {"Calendar","Date","Name","Active","Source","Kind","Status","Version","Updated"},
 Checked = if List.Sort(Table.ColumnNames(Headers)) <> List.Sort(Required) then error "Unexpected holiday CSV schema" else Headers,
 Typed = Table.TransformColumnTypes(Checked,{{"Calendar",type text},{"Date",type date},{"Name",type text},{"Active",Int64.Type},{"Source",type text},{"Kind",type text},{"Status",type text},{"Version",type text},{"Updated",type text}},"en-US"),
 Validated = if Table.RowCount(Table.SelectRows(Typed,each [Calendar]=null or Text.Trim([Calendar])="" or [Date]=null or [Name]=null or not List.Contains({0,1},[Active]) or not List.Contains({"HOLIDAY","WORKDAY"},[Kind])))>0 then error "Invalid holiday row" else Typed,
 Unique = if Table.RowCount(Validated)<>Table.RowCount(Table.Distinct(Validated,{"Calendar","Date","Kind"})) then error "Duplicate calendar/date/kind" else Validated
in Unique
'@
    $book.Queries.Item('ImportedHolidays').Formula = $holidayM
    $cityM = @'
let
 Folder = Text.TrimEnd(Text.From(Excel.CurrentWorkbook(){[Name="DataFolder"]}[Content]{0}[Column1]), {"/", "\"}),
 Raw = Csv.Document(File.Contents(Folder & "/Cities.csv"),[Delimiter=";",Encoding=65001,QuoteStyle=QuoteStyle.Csv]),
 Headers = Table.PromoteHeaders(Raw,[PromoteAllScalars=true]),
 Required = {"Code","City","WindowsId","IanaId","Latitude","Longitude","CountryCode"},
 Checked = if Table.ColumnNames(Headers)<>Required then error "Unexpected city CSV schema" else Headers,
 Typed = Table.TransformColumnTypes(Checked,{{"Code",type text},{"City",type text},{"WindowsId",type text},{"IanaId",type text},{"Latitude",type number},{"Longitude",type number},{"CountryCode",type text}},"en-US"),
 Validated = if Table.RowCount(Typed)<>Table.RowCount(Table.Distinct(Typed,{"Code"})) then error "Duplicate city code" else Typed
in Validated
'@
    $book.Queries.Item('ZoneCatalog').Formula = $cityM
    $settings.Range('A30:M30').Merge();$settings.Range('A30').Value2='METEO OPENWEATHER - opzionale, aggiornamento manuale'
    $settings.Range('A30:M30').Interior.Color=5061411;$settings.Range('A30:M30').Font.Color=16777215;$settings.Range('A30:M30').Font.Bold=$true
    $settings.Range('A32').Value2='Chiave API'
    $settings.Range('B32:F32').Merge();$settings.Range('B32:F32').NumberFormat='@';$settings.Range('B32:F32').Interior.Color=14021631
    $settings.Range('B32:F32').ClearContents()
    $null=$book.Names.Add('WeatherApiKey','=Parametri!$B$32')
    $settings.Range('A34:M34').Merge();$settings.Range('A34').Value2='Vuota: legge Weather.csv. Compilata: Dati > Aggiorna tutto interroga le città selezionate (meteo attuale).'
    $settings.Range('A35:M35').Merge();$settings.Range('A35').Value2='La chiave è in chiaro e viene salvata nel file: non condividere una copia in cui hai inserito la chiave.'
    $settings.Range('A36:M36').Merge();$settings.Range('A36').Value2='Prima connessione: autorizzazioni origini dati / accesso Anonimo a OpenWeather. Nessun refresh automatico in apertura.'
    $settings.Range('A30:M36').RowHeight=24;$settings.Range('A34:M36').Font.Size=10
    $settings.Range('A35:M35').Font.Color=2375567
    # Make a compact weather section above the two existing informational tables.
    $info.Range('A6:A25').EntireRow.Insert(-4121)
    $info.Range('A6:J6').Merge(); $info.Range('A6').Value2 = 'METEO ATTUALE - ultima lettura, non previsione per la data del Planner'
    $info.Range('A6:J6').Font.Bold=$true; $info.Range('A6:J6').RowHeight=24
    $weatherM = @'
let
 Folder = Text.TrimEnd(Text.From(Excel.CurrentWorkbook(){[Name="DataFolder"]}[Content]{0}[Column1]), {"/", "\"}),
 Raw = Csv.Document(File.Contents(Folder & "/Weather.csv"),[Delimiter=";",Encoding=65001,QuoteStyle=QuoteStyle.Csv]),
 Headers = Table.PromoteHeaders(Raw,[PromoteAllScalars=true]),
 Required = {"Code","ObservedUtc","FetchedUtc","TemperatureC","FeelsLikeC","Description","WindMs","Status","Source"},
 Checked = if List.Sort(Table.ColumnNames(Headers))<>List.Sort(Required) then error "Unexpected weather CSV schema" else Headers,
 Typed = Table.TransformColumnTypes(Checked,{{"Code",type text},{"ObservedUtc",type datetime},{"FetchedUtc",type datetime},{"TemperatureC",type number},{"FeelsLikeC",type number},{"Description",type text},{"WindMs",type number},{"Status",type text},{"Source",type text}},"en-US"),
 Validated = if Table.RowCount(Typed)<>Table.RowCount(Table.Distinct(Typed,{"Code"})) then error "Duplicate weather city" else Typed
in Validated
'@
    $weatherM = Get-Content -LiteralPath $weatherQueryPath -Raw
    $null = Add-Query $book 'Weather' $weatherM $info 'A7'
    $info.Range('A16:J16').Merge(); $info.Range('A16').Value2 = 'OpenWeather. ObservedUtc = osservazione; FetchedUtc = download. Date e ore in UTC. Leggi anche Status.'
    $info.Range('A17:J17').Merge(); $info.Range('A17').Value2 = 'Aggiorna tutto: chiave vuota in Parametri B32 = Weather.csv; chiave compilata = API OpenWeather per le città selezionate.'
    $info.Range('A18:J18').Merge(); $info.Range('A18').Value2 = 'La chiave è opzionale e in chiaro. Non condividere il workbook compilato con la chiave né il file .key.'
    $info.Range('A16:J18').Font.Size=10; $info.Range('A16:J18').RowHeight=24
    $info.Range('A26').Value2 = 'SANTI: selezione informativa di ricorrenze fisse, non calendario liturgico completo.'

    # Materialize new schemas before assigning formulas with their structured references.
    $refresh = @()
    foreach ($spec in @(@('Da_file','ImportedHolidays'),@('Fusi','ZoneCatalog'),@('Fusi','ZonePeriods'),@('Info','Saints'),@('Info','MoonPhases'),@('Info','Weather'))) {
        $table=$book.Worksheets.Item($spec[0]).ListObjects.Item($spec[1])
        if (-not $table.QueryTable.Refresh($false)) { throw "Refresh cancelled: $($spec[1])" }
        if ($table.QueryTable.FetchedRowOverflow) { throw 'CSV overflow' }
        $refresh += [ordered]@{Table=$spec[1];Rows=$table.ListRows.Count}
        Write-Output "Refreshed $($spec[1]): $($table.ListRows.Count) rows"
    }
    $excel.CalculateUntilAsyncQueriesDone()

    foreach ($sheetEntry in $patch.values.GetEnumerator()) {
        $sheet = $book.Worksheets.Item($sheetEntry.Key)
        foreach ($entry in $sheetEntry.Value.GetEnumerator()) { $sheet.Range($entry.Key).Value2 = $entry.Value }
    }
    # One bounded matrix write avoids thousands of per-cell COM round trips.
    $matrix = $calc.Range('A1:BS88').Formula
    foreach ($entry in $patch.formulas.Calcoli.GetEnumerator()) {
        if ($entry.Key -notmatch '^([A-Z]+)([0-9]+)$') { throw 'Invalid patch cell' }
        $column=0; foreach($character in $Matches[1].ToCharArray()) { $column=$column*26+([int]$character-64) }
        $matrix[[int]$Matches[2],$column] = $entry.Value
    }
    $calc.Range('A1:BS88').Formula = $matrix
    foreach ($name in @('Planner','Estesa','Info')) { Apply-FormulaPatch ($book.Worksheets.Item($name)) $patch.formulas[$name] }
    $exception = $book.Worksheets.Item('Eccezioni')
    foreach ($pair in $patch.exceptionValidation) { $exception.Range($pair[0]).Formula=$pair[1] }
    foreach ($address in @('A10:A109','D10:D109','F10:F109')) { $exception.Range($address).Validation.ShowError=$true }
    $null = $book.Names.Add('SchemaVersion','=2')

    $info.Range('B8:C14').NumberFormatLocal=$nativeDateFormat+' hh:mm'
    $info.Range('D8:E14').NumberFormatLocal=$nativeDecimalFormat;$info.Range('G8:G14').NumberFormatLocal=$nativeDecimalFormat
    $info.Range('A7:I14').Font.Size=9;$info.Range('A7:I14').RowHeight=28
    $info.Range('B8:C14').WrapText=$true;$info.Range('F8:F14').WrapText=$true
    $info.ListObjects.Item('Saints').DataBodyRange.RowHeight=28
    $info.ListObjects.Item('Saints').ListColumns.Item('Name').DataBodyRange.WrapText=$true
    $imported=$book.Worksheets.Item('Da_file')
    $imported.Range('F:I').ColumnWidth=25
    $imported.Range('A3').Value2='HOLIDAY = festività; WORKDAY = recupero nazionale. Status distingue stime e base da verificare. Personalizzazioni in Calendari/Eccezioni.'

    foreach ($spec in @(@('Planner',41,42),@('Estesa',65,66))) {
        $sheet=$book.Worksheets.Item($spec[0]);$r=[int]$spec[1]
        $sheet.Range("A${r}").Value2='Meteo attuale*'
        $sheet.Range("A${r}:H${r}").RowHeight=28
        $sheet.Range("A${r}:H${r}").Font.Size=9
        $sheet.Range("A${r}:H${r}").WrapText=$true
        $sheet.Range("A${r}:H${r}").Interior.Color=16119285
        $sheet.Range("A$($spec[2])").Value2='F: festività locale, anche in presenza di una deroga. *Meteo: ultima lettura e stato in Info; non è una previsione.'
    }
    $guide=$book.Worksheets.Item('Guida')
    $guideUpdates=@{
        'Festività'='Il calendario nazionale e le date manuali in Calendari chiudono il giorno locale. Le eccezioni personali attive hanno precedenza. Il suffisso F segnala comunque la ricorrenza festiva.'
        'Aggiungere un calendario'='Registro Calendari A6:F25. I codici nazionali richiesti sono già presenti. I patroni si aggiungono manualmente al codice applicabile; nessuna selezione automatica di stati o province.'
        'Caricato / Non caricato'='Base nazionale indica una fotografia delle regole 2026-2028, non una certificazione di completezza o applicabilità aziendale. Controlla Status in Da_file. Nessuno disattiva le festività.'
        'Dati iniziali'='Cina continentale, India, Corea del Sud, Giappone, Bangladesh, Italia, Francia, Norvegia, Svezia, Stati Uniti e Canada: base nazionale holidays 0.105, 2026-2028. Le stime e i recuperi futuri richiedono aggiornamento. Calendari precedenti preservati.'
        'File esterni'='Sei CSV UTF-8 separati da ; in Parametri B25: Citta, Periodi_DST, Calendari, Santi, Fasi_lunari, Meteo. La chiave API non è un dato da importare o condividere.'
        'Aggiornamento CSV'='Dati > Aggiorna tutto rilegge i file locali; con chiave OpenWeather in Parametri B32 scarica il meteo delle città selezionate. Nessun refresh in apertura. Dopo un errore può restare la copia precedente: controlla le query.'
        'Santi e ricorrenze'='Selezione con nomi italiani, non un santorale completo né un calendario liturgico annuale. Ricorrenze fisse, una riga per giorno. Solo informazione; non chiudono la disponibilità.'
        'Excel'='XLSX senza macro con sei query, meteo anche via API opzionale. Cover informativa A4, Planner una A4, Estesa due A4. Cambia DataFolder in Parametri B25 se sposti la cartella.'
    }
    foreach ($r in 3..35) { $label=[string]$guide.Cells.Item($r,1).Value2; if($guideUpdates.ContainsKey($label)){$guide.Cells.Item($r,2).Value2=$guideUpdates[$label]} }
    $guide.Range('A28').Value2='Eccezioni personali';$guide.Range('B28').Value2='Colonna 1-7, date locali Dal/Al incluse, Riposo oppure Attivita con gli orari standard, Attiva=1. Una sovrapposizione nello stesso giorno viene segnalata come errore.'
    $guide.Range('A29').Value2='Meteo attuale';$guide.Range('B29').Value2='Chiave vuota: snapshot CSV. Chiave in Parametri B32: API con Aggiorna tutto. Info mostra osservazione e download UTC. La data del Planner non seleziona una previsione. Non condividere il file con la chiave salvata.'

    $excel.Calculation=-4105; $excel.CalculateFullRebuild()
    Check-FormulaErrors $book
    $afterInputs=[ordered]@{Parameters=$settings.Range('A12:M18').Formula; Date=$settings.Range('C5:C7').Formula; Margins=$settings.Range('J5:J6').Formula}
    if ((ConvertTo-Json $originalInputs -Depth 5 -Compress) -cne (ConvertTo-Json $afterInputs -Depth 5 -Compress)) { throw 'User inputs were altered' }
    if ((ConvertTo-Json -InputObject $beforeManual -Compress) -cne (ConvertTo-Json -InputObject $calendar.ListObjects.Item('HolidayDates').DataBodyRange.Formula -Compress)) { throw 'Manual holidays changed' }
    if ($book.Connections.Count -ne 6 -or $book.Queries.Count -ne 6) { throw 'Expected six native queries and connections' }
    # Scenario verification is supplied as a separate, reviewable script.
    $verificationScript=Join-Path $runRoot 'verify-scenarios.ps1'
    if (-not (Test-Path -LiteralPath $verificationScript)) { throw 'Missing scenario verification script' }
    $scenarioResults = & $verificationScript -Excel $excel -Book $book -StageData $stageData
    $excel.CalculateFullRebuild(); Check-FormulaErrors $book

    $cover=$book.Worksheets.Item('Copertina')
    $cover.PageSetup.PaperSize=9;$cover.PageSetup.Orientation=1;$cover.PageSetup.Zoom=$false;$cover.PageSetup.FitToPagesWide=1;$cover.PageSetup.FitToPagesTall=1
    $cover.PageSetup.PrintArea='$A$1:$H$44'
    $cover.PageSetup.LeftMargin=$excel.InchesToPoints(0.3);$cover.PageSetup.RightMargin=$excel.InchesToPoints(0.3)
    $cover.PageSetup.TopMargin=$excel.InchesToPoints(0.3);$cover.PageSetup.BottomMargin=$excel.InchesToPoints(0.3)
    $cover.Activate();$excel.ActiveWindow.DisplayGridlines=$false;$excel.ActiveWindow.Zoom=90;$cover.Range('A1').Select()
    $book.Worksheets.Item('Planner').ExportAsFixedFormat(0,(Join-Path $runRoot 'verifiche\Planner_A4.pdf'))
    $book.Worksheets.Item('Estesa').ExportAsFixedFormat(0,(Join-Path $runRoot 'verifiche\Estesa_A4.pdf'))
    $cover.ExportAsFixedFormat(0,(Join-Path $runRoot 'verifiche\Copertina_A4.pdf'))
    $exception.PageSetup.PrintArea='$A$1:$G$16';$exception.PageSetup.PaperSize=9;$exception.PageSetup.Orientation=2;$exception.PageSetup.Zoom=$false;$exception.PageSetup.FitToPagesWide=1;$exception.PageSetup.FitToPagesTall=1
    $exception.ExportAsFixedFormat(0,(Join-Path $runRoot 'verifiche\Eccezioni_preview.pdf'))
    $book.Names.Item('DataFolder').RefersToRange.Value2=[string]$liveData
    $book.Save();$book.Close($false);[void][Runtime.InteropServices.Marshal]::ReleaseComObject($book);$book=$null
    $book=$excel.Workbooks.Open($workbookPath,0,$true)
    Check-FormulaErrors $book
    if ([string]$book.Names.Item('DataFolder').RefersToRange.Value2 -cne $liveData) { throw 'Final data folder not saved' }
    $book.Close($false);[void][Runtime.InteropServices.Marshal]::ReleaseComObject($book);$book=$null
    if ((Get-FileHash -LiteralPath $liveBook).Hash -ne $originalHash) { throw 'Live workbook changed; refusing to replace it' }
    $report=[ordered]@{WorkingFolder=$working;Workbook=$workbookPath;OriginalHash=$originalHash;Refresh=$refresh;Scenarios=$scenarioResults;InputsPreserved=$true;ManualHolidaysPreserved=$true;QueryCount=6;Backup=$backup;ReadyForVisualReview=$true;Published=$false}
    $report | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $nativeReport -Encoding utf8
    Write-Output "Prepared and verified; visual review required before publication. $workbookPath"
}
finally {
    if ($null -ne $donor) { $donor.Close($false);[void][Runtime.InteropServices.Marshal]::ReleaseComObject($donor) }
    if ($null -ne $book) { $book.Close($false);[void][Runtime.InteropServices.Marshal]::ReleaseComObject($book) }
    if ($null -ne $excel) { if($null -ne $oldMap){$excel.MapPaperSize=$oldMap};$excel.Quit();[void][Runtime.InteropServices.Marshal]::ReleaseComObject($excel) }
    [GC]::Collect();[GC]::WaitForPendingFinalizers()
}
