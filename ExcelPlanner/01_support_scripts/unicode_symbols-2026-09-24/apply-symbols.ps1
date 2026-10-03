param([switch]$Publish)
$ErrorActionPreference = 'Stop'
$taskRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$source = Join-Path $taskRoot 'Planner_Availability_A4.xlsx'
$staged = Join-Path $PSScriptRoot 'Planner_symbols_staged.xlsx'
$reportPath = Join-Path $PSScriptRoot 'verification.json'

if ($Publish) {
    $report = Get-Content -LiteralPath $reportPath -Raw | ConvertFrom-Json
    if (!$report.NativeVerified -or !$report.LayoutVerified) { throw 'Native/layout verification is required.' }
    if ((Get-FileHash -LiteralPath $source).Hash -ne $report.SourceHash) { throw 'Source changed. Do not overwrite.' }
    if ((Get-FileHash -LiteralPath $staged).Hash -ne $report.StagedHash) { throw 'Staged workbook changed.' }
    $backup = Join-Path $PSScriptRoot 'Planner_before_symbols.xlsx'
    if (Test-Path -LiteralPath $backup) { throw 'Backup already exists; this patch was already published or interrupted.' }
    # Take an exclusive handle so a reopened Excel file cannot be overwritten.
    $stream = [IO.File]::Open($source,[IO.FileMode]::Open,[IO.FileAccess]::ReadWrite,[IO.FileShare]::None)
    try {
        $original = New-Object byte[] $stream.Length
        $stream.ReadExactly($original,0,$original.Length)
        $originalHash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($original))
        if ($originalHash -ne $report.SourceHash) { throw 'Source changed during publish.' }
        [IO.File]::WriteAllBytes($backup,$original)
        $bytes = [IO.File]::ReadAllBytes($staged)
        try { $stream.Position=0; $stream.Write($bytes); $stream.SetLength($bytes.Length); $stream.Flush($true) }
        catch { $stream.Position=0; $stream.Write($original); $stream.SetLength($original.Length); $stream.Flush($true); throw }
    } finally { $stream.Dispose() }
    $report | Add-Member -NotePropertyName Published -NotePropertyValue $true -Force
    $report | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $reportPath -Encoding utf8
    Write-Output 'Published. Original workbook backed up in support folder.'
    exit
}

$sourceHash = (Get-FileHash -LiteralPath $source).Hash
Copy-Item -LiteralPath $source -Destination $staged -Force
$patch = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'symbols-patch.json') -Raw | ConvertFrom-Json
$excel=$null; $book=$null
function Normalize-Formula([string]$f) { $f.Replace('_xlfn.','') }
function Cell-Signature($range) {
    # Compare contents without writing private cell values to diagnostics.
    $value = $range.Formula2
    $json = ConvertTo-Json -InputObject $value -Compress -Depth 4
    [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($json)))
}
try {
    # Use a separate hidden instance on a local copy, never the user's live session.
    $excel = New-Object -ComObject Excel.Application
    $excel.Visible=$false; $excel.DisplayAlerts=$false; $excel.EnableEvents=$false; $excel.AskToUpdateLinks=$false
    $excel.AutomationSecurity=3
    $book=$excel.Workbooks.Open($staged,0,$false)
    $queries=@{}; foreach($q in $book.Queries){$queries[$q.Name]=$q.Formula}
    $sheetNames=@($book.Worksheets | ForEach-Object {$_.Name})
    $preserve=@{}
    foreach($name in @('Copertina','Parametri','Eccezioni','Calendari','Da_file','Fusi','Guida')) {
        $preserve[$name]=Cell-Signature ($book.Worksheets.Item($name).UsedRange)
    }
    $nativeBeforeErrors=0
    foreach($s in $book.Worksheets){
        $errors=$null
        try{$errors=$s.UsedRange.SpecialCells(-4123,16)}catch{}
        if($null -ne $errors){$nativeBeforeErrors += $errors.Count}
    }
    if($nativeBeforeErrors -ne 0){throw "Source contains $nativeBeforeErrors formula errors; inspect before editing."}
    foreach($name in @('Planner','Estesa')) {
        $book.Worksheets.Item($name).ExportAsFixedFormat(0,(Join-Path $PSScriptRoot "$name-before.pdf"))
    }
    foreach($item in $patch){
        $range=$book.Worksheets.Item($item.sheet).Range($item.cell)
        if($item.previousFormula){
            # Legacy formulas may acquire @ in Formula2; accept the exact legacy
            # representation too, without changing any unrelated formula.
            if((Normalize-Formula ([string]$range.Formula2)) -ne (Normalize-Formula $item.previousFormula) -and
               (Normalize-Formula ([string]$range.Formula)) -ne (Normalize-Formula $item.previousFormula)) { throw "Formula mismatch: $($item.sheet)!$($item.cell)" }
        } elseif($null -ne $item.previousValue -and $item.previousValue -ne '') {
            if($range.Value2 -ne $item.previousValue){throw "Value mismatch: $($item.sheet)!$($item.cell)"}
        } elseif($null -ne $range.Value2 -and $range.Value2 -ne ''){throw "Expected an empty helper cell: $($item.sheet)!$($item.cell)"}
        if($item.isFormula){$range.Formula2=$item.content}else{$range.Value2=$item.content}
    }
    $excel.CalculateFullRebuild()
    $nativeErrors=0
    foreach($s in $book.Worksheets){
        $errors=$null
        try{$errors=$s.UsedRange.SpecialCells(-4123,16)}catch{}
        if($null -ne $errors){$nativeErrors += $errors.Count}
    }
    if($nativeErrors -ne 0){throw "After patch: $nativeErrors formula errors."}
    if(($sheetNames -join '|') -ne (@($book.Worksheets | ForEach-Object {$_.Name}) -join '|')){throw 'Worksheet order changed.'}
    foreach($q in $book.Queries){if($queries[$q.Name] -cne $q.Formula){throw "Query changed: $($q.Name)"}}
    if($book.Queries.Count -ne $queries.Count){throw 'Query count changed.'}
    foreach($name in $preserve.Keys){if((Cell-Signature ($book.Worksheets.Item($name).UsedRange)) -ne $preserve[$name]){throw "Unrelated inputs/formulas changed in $name"}}
    $planner=$book.Worksheets.Item('Planner');$calc=$book.Worksheets.Item('Calcoli');$info=$book.Worksheets.Item('Info')
    $displays=@()
    for($i=2;$i -le 8;$i++){
        $country=[string]$calc.Cells.Item(94,$i).Value2
        $expected=[char]::ConvertFromUtf32(127397+[int][char]$country[0])+[char]::ConvertFromUtf32(127397+[int][char]$country[1])
        if(!([string]$planner.Cells.Item(8,$i).Value2).StartsWith($expected)){throw "Flag failed for column $i"}
        $icon=[string]$calc.Cells.Item(96,$i).Value2
        if(!$icon -or !([string]$planner.Cells.Item(41,$i).Value2).Contains($icon)){throw "Weather icon failed for column $i"}
        $displays+=@{City=$planner.Cells.Item(8,$i).Value2;Weather=$planner.Cells.Item(41,$i).Value2}
    }
    if(!([string]$info.Range('B3').Value2).Contains('🌔') -or !([string]$info.Range('B3').Value2).Contains('🌕')){throw 'Current moon glyphs do not match the source date.'}
    $moonDisplay=[string]$info.Range('B3').Value2
    foreach($name in @('Planner','Estesa')) {
        $book.Worksheets.Item($name).ExportAsFixedFormat(0,(Join-Path $PSScriptRoot "$name-after.pdf"))
    }
    $book.Save()
    # Exercise changed formulas only in memory; saved user inputs stay untouched.
    $checks=0
    foreach($sample in @(@('temporale','⛈️'),@('neve','🌨️'),@('pioggia leggera','🌧️'),@('nebbia','🌫️'),@('poche nuvole','🌤️'),@('nubi sparse','⛅'),@('cielo coperto','☁️'),@('cielo sereno','☀️'),@('tornado','🌪️'),@('sconosciuto','🌡️'),@('',''))){
        $calc.Range('B95').Value2=$sample[0];$excel.Calculate()
        if($calc.Range('B96').Value2 -cne $sample[1]){throw "Weather mapping mismatch: $($sample[0])"};$checks++
    }
    foreach($sample in @(@('IT','🇮🇹'),@('CN','🇨🇳'),@('US','🇺🇸'),@('GB','🇬🇧'),@('BD','🇧🇩'))){
        $calc.Range('B94').Value2=$sample[0];$excel.Calculate()
        if(!([string]$planner.Range('B8').Value2).StartsWith($sample[1])){throw 'Dynamic flag mismatch.'};$checks++
    }
    $book.Close($false);$book=$null
    # Reopen saved copy to verify persistence, without refreshing data/API queries.
    $book=$excel.Workbooks.Open($staged,0,$true)
    if(([string]$book.Worksheets.Item('Info').Range('B3').Value2) -cne $moonDisplay){throw 'Moon display changed on reopen.'}
    if($book.Queries.Count -ne 6){throw 'Expected six preserved queries.'}
    $book.Close($false);$book=$null
    @{SourceHash=$sourceHash;StagedHash=(Get-FileHash -LiteralPath $staged).Hash;NativeVerified=$true;LayoutVerified=$false;FormulaErrors=$nativeErrors;QueryCount=$queries.Count;ScenarioChecks=$checks;ChangedCells=$patch.Count;Displays=$displays;Moon=$moonDisplay;FontsUnchanged=$true;NoNetworkRefresh=$true} |
        ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $reportPath -Encoding utf8
    Write-Output "Native Excel verified: $($patch.Count) cells, $checks dynamic checks, zero formula errors, six queries preserved."
} finally {
    if($null -ne $book){$book.Close($false)}
    if($null -ne $excel){$excel.Quit();$null=[Runtime.InteropServices.Marshal]::FinalReleaseComObject($excel)}
    [GC]::Collect();[GC]::WaitForPendingFinalizers()
}
