param([switch]$Apply)
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
$root=[IO.Path]::GetFullPath((Split-Path -Parent (Split-Path -Parent $PSScriptRoot)))
$reportPath=Join-Path $PSScriptRoot 'verifiche\native-verification.json'
$report=Get-Content -LiteralPath $reportPath -Raw | ConvertFrom-Json -AsHashtable
$api=Get-Content -LiteralPath (Join-Path $PSScriptRoot 'verifiche\weather-api-native.json') -Raw | ConvertFrom-Json
if(-not $report.ReadyForVisualReview -or -not $api.NativePowerQueryApiVerified){throw 'Required verifications incomplete'}
if($report.Published){throw 'Already published; do not replay this one-time migration'}
$targetBook=Join-Path $root 'Planner_Availability_A4.xlsx'
if((Get-FileHash -LiteralPath $targetBook).Hash -ne $report.OriginalHash){throw 'Original workbook changed; publication stopped'}
if((Get-FileHash -LiteralPath $report.Workbook).Hash -ne $report.WorkbookSha256){throw 'Staged workbook changed after verification'}
$files=[Collections.Generic.List[object]]::new()
foreach($name in @('Cities.csv','Calendars.csv','Saints.csv','Weather.csv')){
    $source=Join-Path $report.WorkingFolder ('Calendar data\'+$name)
    $target=Join-Path $root ('Calendar data\'+$name)
    $previous=Join-Path $report.Backup $name
    if((Test-Path -LiteralPath $previous) -and ((Get-FileHash -LiteralPath $target).Hash -ne (Get-FileHash -LiteralPath $previous).Hash)){throw "Live dataset changed: $name"}
    if((-not (Test-Path -LiteralPath $previous)) -and (Test-Path -LiteralPath $target)){throw "New dataset appeared: $name"}
    $files.Add(@{Source=$source;Target=$target})
}
$files.Add(@{Source=(Join-Path $PSScriptRoot 'GETTING_STARTED-updated.md');Target=(Join-Path $root '01_support_scripts\GETTING_STARTED.md')})
$files.Add(@{Source=(Join-Path $PSScriptRoot 'Data_Guide-updated.md');Target=(Join-Path $root '01_support_scripts\Data_Guide.md')})
$files.Add(@{Source=(Join-Path $PSScriptRoot 'verifiche\Planner_A4.pdf');Target=(Join-Path $root '01_support_scripts\Planner_Availability_A4.pdf')})
# The workbook is replaced last, after its companion schemas have been deployed.
$files.Add(@{Source=$report.Workbook;Target=$targetBook})
foreach($file in $files){
    $resolved=[IO.Path]::GetFullPath($file.Target)
    if(-not $resolved.StartsWith($root+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){throw 'Target outside prototype folder'}
    if(-not (Test-Path -LiteralPath $file.Source -PathType Leaf)){throw 'Missing publication input'}
    if(Test-Path -LiteralPath $file.Target){$lock=[IO.File]::Open($file.Target,[IO.FileMode]::Open,[IO.FileAccess]::Read,[IO.FileShare]::None);$lock.Dispose()}
}
if(-not $Apply){Write-Output "Ready: $($files.Count) explicit files; unchanged source workbook; all destinations unlocked.";return}
$backup=Join-Path $PSScriptRoot ('published-backup-'+(Get-Date -Format 'yyyyMMdd-HHmmss'))
New-Item -ItemType Directory -Path $backup | Out-Null
$done=[Collections.Generic.List[object]]::new()
try {
    foreach($file in $files){
        $file.OriginalExisted=Test-Path -LiteralPath $file.Target
        if($file.OriginalExisted){$file.Backup=Join-Path $backup ([IO.Path]::GetFileName($file.Target));Copy-Item -LiteralPath $file.Target -Destination $file.Backup}
        Copy-Item -LiteralPath $file.Source -Destination $file.Target -Force
        $done.Add($file)
        if((Get-FileHash -LiteralPath $file.Source).Hash -ne (Get-FileHash -LiteralPath $file.Target).Hash){throw 'Published file checksum mismatch'}
    }
}catch{
    foreach($file in $done){
        if($file.OriginalExisted){Copy-Item -LiteralPath $file.Backup -Destination $file.Target -Force}
        else{
            # Preserve a newly created file in the scoped backup rather than deleting it.
            $resolved=[IO.Path]::GetFullPath($file.Target)
            if(-not $resolved.StartsWith($root+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){throw 'Unsafe rollback target'}
            Move-Item -LiteralPath $resolved -Destination (Join-Path $backup ('unpublished-'+[IO.Path]::GetFileName($resolved)))
        }
    }
    throw
}
$report.Published=$true;$report.PublishedAtUtc=[datetime]::UtcNow.ToString('o');$report.PublishedWorkbook=$targetBook;$report.PublicationBackup=$backup
$report | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $reportPath -Encoding utf8
Write-Output "Published $($files.Count) verified files. Previous versions retained in $backup"
