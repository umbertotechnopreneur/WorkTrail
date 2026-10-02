# Open an ignored working copy with the workbook-specific Fast Combine option.
$ErrorActionPreference='Stop'
$prototypeRoot=Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$template=Join-Path $prototypeRoot 'Planner_Availability_A4.xlsx'
$dataFolder=Join-Path $prototypeRoot 'Calendar data'
$localFolder=Join-Path $prototypeRoot '.local'
$path=Join-Path $localFolder 'Planner_Availability_A4.xlsx'
if(-not (Test-Path -LiteralPath $template -PathType Leaf)){throw 'Planner template not found'}
foreach($name in @('Citta','Periodi_DST','Calendari','Santi','Fasi_lunari','Meteo')){
    if(-not(Test-Path -LiteralPath (Join-Path $dataFolder ($name+'.csv')) -PathType Leaf)){throw "Missing CSV: $name"}
}
if(-not(Test-Path -LiteralPath $localFolder)){$null=New-Item -ItemType Directory -Path $localFolder}
if(-not(Test-Path -LiteralPath $path)){Copy-Item -LiteralPath $template -Destination $path}
$excel=New-Object -ComObject Excel.Application
$book=$null
try {
    $excel.Visible=$true
    $book=$excel.Workbooks.Open($path,0,$false)
    if($book.ReadOnly){throw 'The local workbook is already open or read-only.'}
    $book.Names.Item('DataFolder').RefersToRange.Value2=$dataFolder
    $book.Queries.FastCombine=$true
    $book.Worksheets.Item('Copertina').Activate()
    $book.Save()
    Write-Output 'Copia locale aperta in .local; percorso CSV configurato. Nessun aggiornamento automatico.'
} catch {
    if($null -ne $book){$book.Close($false)}
    $excel.Quit()
    throw
}
# Intentionally keep the visible application open for the user.
[void][Runtime.InteropServices.Marshal]::ReleaseComObject($excel)
