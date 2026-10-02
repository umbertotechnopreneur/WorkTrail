param([Parameter(Mandatory)]$Excel,[Parameter(Mandatory)]$Book,[Parameter(Mandatory)][string]$StageData)
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
$settings=$Book.Worksheets.Item('Parametri')
$calc=$Book.Worksheets.Item('Calcoli')
$exception=$Book.Worksheets.Item('Eccezioni')
$manual=$Book.Worksheets.Item('Calendari').ListObjects.Item('HolidayDates')
$snapshot=[ordered]@{Inputs=$settings.Range('A12:M18').Formula;Date=$settings.Range('C5:C7').Formula;Margins=$settings.Range('J5:J6').Formula;Exceptions=$exception.Range('A10:F109').Formula;Manual=$manual.DataBodyRange.Formula}
$results=[Collections.Generic.List[object]]::new()
function Assert-Equal([string]$Name,$Actual,$Expected) {
    if ($Actual -ne $Expected) { throw "Scenario failed: $Name; actual=$Actual expected=$Expected" }
    $results.Add([ordered]@{Name=$Name;Passed=$true})
}
function Day([string]$Date,[string]$City='rome',[string]$Calendar='IT') {
    $settings.Range('C5').Value2=[double]([datetime]$Date).ToOADate()
    $settings.Range('C6').Value2=[double]0
    $settings.Range('B12').Value2=$City;$settings.Range('C12').Value2=$City;$settings.Range('D12').Value2=$Calendar
    $settings.Range('E12').Value2=[double](9/24);$settings.Range('F12').Value2=[double](18/24)
    $settings.Range('G12:M12').Value2=[double]0
    $settings.Range('J5:J6').Value2=[double]2
    $exception.Range('A10:F109').ClearContents()
}
function Personal([int]$Row,[string]$From,[string]$To,[string]$Effect,[int]$Column=1) {
    $exception.Cells.Item($Row,1).Value2=[double]$Column
    $exception.Cells.Item($Row,2).Value2=[double]([datetime]$From).ToOADate()
    $exception.Cells.Item($Row,3).Value2=[double]([datetime]$To).ToOADate()
    $exception.Cells.Item($Row,4).Value2=$Effect
    $exception.Cells.Item($Row,5).Value2='Temporary native verification; removed before saving'
    $exception.Cells.Item($Row,6).Value2=[double]1
}
function State([string]$Name,[int]$Hour,[int]$Expected) {
    $Excel.CalculateFullRebuild()
    $r=17+$Hour
    try { $local=[datetime]::FromOADate([double]$calc.Cells.Item($r,2).Value2) }
    catch { throw "Invalid local date in scenario $Name : date=$($settings.Range('C5').Value2) city=$($settings.Range('C12').Value2) local=$($calc.Cells.Item($r,2).Text) formula=$($calc.Cells.Item($r,2).Formula) warning=$($Book.Worksheets.Item('Planner').Range('A7').Value2)" }
    if ($local.Hour -ne $Hour) { throw "Verification grid alignment failed: $local vs hour $Hour" }
    Assert-Equal $Name $calc.Cells.Item($r,18).Value2 $Expected
}
try {
    Assert-Equal 'Weather key shipped blank' ([string]$Book.Names.Item('WeatherApiKey').RefersToRange.Value2) ''
    Assert-Equal 'Weather snapshot has seven selected cities' $Book.Worksheets.Item('Info').ListObjects.Item('Weather').ListRows.Count 7
    Assert-Equal 'City catalogue has 206 locations and coordinates' $Book.Worksheets.Item('Fusi').ListObjects.Item('ZoneCatalog').ListRows.Count 206
    Day '2026-12-25'
    State 'National holiday closes activity' 10 0
    Personal 10 '2026-12-25' '2026-12-25' 'Attivita'
    State 'Personal recovery overrides holiday' 10 2
    State 'Recovery retains reserve before activity' 7 1
    State 'Recovery is not an all-day opening' 6 0
    Assert-Equal 'Holiday marker remains visible under personal override' ([int]$calc.Range('Z27').Value2 -ge 1) $true
    $exception.Range('F10').Value2=[double]0
    State 'Disabled exception does not override holiday' 10 0
    Day '2026-09-24'
    State 'Ordinary working day' 10 2
    State 'Reserve starts two hours before' 7 1
    State 'Before reserve is unavailable' 6 0
    State 'Activity end becomes reserve' 18 1
    State 'After reserve is unavailable' 20 0
    Personal 10 '2026-09-24' '2026-09-24' 'Riposo'
    State 'Personal vacation overrides ordinary hours' 10 0
    $exception.Range('F10').Value2=[double]0
    State 'Disabled vacation restores ordinary hours' 10 2
    Day '2026-07-19';$settings.Range('M12').Value2=[double]1
    State 'Per-column Sunday rest closes hours' 10 0
    Personal 10 '2026-07-19' '2026-07-19' 'Attivita'
    State 'Personal recovery overrides weekly rest' 10 2
    $workdays=Import-Csv -LiteralPath (Join-Path $StageData 'Calendars.csv') -Delimiter ';' | Where-Object {$_.Calendar -eq 'CN' -and $_.Kind -eq 'WORKDAY' -and $_.Date -like '2026-*'}
    if (@($workdays).Count -eq 0) { throw 'Chinese recovery days missing' }
    $recoveryDate=[string]@($workdays)[0].Date
    $chineseCities=@(Import-Csv -LiteralPath (Join-Path $StageData 'Cities.csv') -Delimiter ';' | Where-Object CountryCode -eq 'CN')
    if ($chineseCities.Count -eq 0) { throw 'Chinese city missing from supplied catalogue' }
    $chinaCode=[string]$chineseCities[0].Code
    Day $recoveryDate $chinaCode 'CN';$settings.Range('L12:M12').Value2=[double]1
    State 'National Chinese recovery opens weekend hours' 10 2
    Personal 10 $recoveryDate $recoveryDate 'Riposo'
    State 'Personal vacation overrides Chinese recovery' 10 0
    Day '2026-09-24'
    Personal 10 '2026-09-24' '2026-09-25' 'Attivita'
    Personal 11 '2026-09-24' '2026-09-24' 'Riposo'
    State 'Overlapping exceptions produce invalid state' 10 -1
    $exception.Range('A11:F11').ClearContents()
    State 'Removing overlap restores valid activity' 10 2
    $exception.Range('A10').Value2=[double]8
    State 'Out-of-range column rejected' 10 -1
    Assert-Equal 'Invalid row is labelled CONTROLLA' $exception.Range('G10').Value2 'CONTROLLA'
    $exception.Range('A10').Value2=[double]1;$exception.Range('B10').Value2='not a date'
    State 'Text date rejected without formula error' 10 -1
    Assert-Equal 'Text date produces validation message' $exception.Range('G10').Value2 'CONTROLLA'
    Day '2026-09-24'
    Personal 10 '2026-09-24' '2026-09-25' 'Riposo'
    State 'Exception start date included' 10 0
    $settings.Range('C5').Value2=[double]([datetime]'2026-09-25').ToOADate()
    State 'Exception end date included' 10 0
    $settings.Range('C5').Value2=[double]([datetime]'2026-09-26').ToOADate()
    State 'Date after exception is not closed' 10 2
    Day '2026-09-24'
    $manual.DataBodyRange.Cells.Item(1,1).Value2='IT'
    $manual.DataBodyRange.Cells.Item(1,2).Value2=[double]([datetime]'2026-09-24').ToOADate()
    $manual.DataBodyRange.Cells.Item(1,3).Value2='Temporary local patron verification'
    $manual.DataBodyRange.Cells.Item(1,4).Value2=[double]1
    State 'Applicable manual local patron closes hours' 10 0
    Personal 10 '2026-09-24' '2026-09-24' 'Attivita'
    State 'Personal recovery overrides local patron' 10 2
    $manual.DataBodyRange.Formula=$snapshot.Manual
    Day '2026-09-24'
    State 'Saint information does not close hours' 10 2
    $settings.Range('E12').Value2=[double](22/24);$settings.Range('F12').Value2=[double](6/24)
    State 'Overnight activity includes early morning' 5 2
    State 'Overnight end becomes reserve' 6 1
    State 'Overnight early reserve' 20 1
    State 'Overnight core begins at 22' 22 2
    Day '2026-09-24'
    Personal 10 '2026-09-24' '2026-09-24' 'Riposo'
    $before=$exception.Range('A10:G10').Formula | ConvertTo-Json -Compress
    $null=$Book.Worksheets.Item('Info').ListObjects.Item('Weather').QueryTable.Refresh($false)
    $after=$exception.Range('A10:G10').Formula | ConvertTo-Json -Compress
    Assert-Equal 'Refresh preserves manual exceptions' $after $before
    State 'CSV refresh preserves personal day off' 10 0
    $Excel.CalculateFullRebuild()
    Assert-Equal 'Weather follows selected city code' ($Book.Worksheets.Item('Planner').Range('B41').Value2 -match 'UTC') $true
    $settings.Range('C12').Value2=$chinaCode;$Excel.CalculateFullRebuild()
    Assert-Equal 'Missing weather does not reuse previous city' $Book.Worksheets.Item('Planner').Range('B41').Value2 'Non caricato'
    foreach ($sheet in $Book.Worksheets) {
        foreach ($table in $sheet.ListObjects) {
            if ($table.SourceType -eq 3) { Assert-Equal ("No refresh on open: " + $table.Name) $table.QueryTable.RefreshOnFileOpen $false }
        }
    }
}
finally {
    $Excel.Calculation=-4135
    $settings.Range('A12:M18').Formula=$snapshot.Inputs
    $settings.Range('C5:C7').Formula=$snapshot.Date
    $settings.Range('J5:J6').Formula=$snapshot.Margins
    $exception.Range('A10:F109').Formula=$snapshot.Exceptions
    $manual.DataBodyRange.Formula=$snapshot.Manual
    $Excel.Calculation=-4105;$Excel.CalculateFullRebuild()
}
$results.ToArray()
