param([string]$DataPath,[string]$PythonExecutable='python')
$ErrorActionPreference='Stop'
$catalog=(& $PythonExecutable (Join-Path $PSScriptRoot 'export-worldclocks.py')) | ConvertFrom-Json
if($LASTEXITCODE -ne 0){throw 'Cannot export WorldClocks read-only database'}
$defaults=@('hanoi','mumbai','minsk','rome','london','new-york','los-angeles')
$ordered=@(foreach($id in $defaults){$c=@($catalog.cities|Where-Object id -eq $id);if($c.Count -ne 1){throw "Missing city: $id"};$c[0]})
$ordered+=@($catalog.cities|Where-Object id -notin $defaults)
$start=[datetime]::new(2026,1,1,0,0,0,[DateTimeKind]::Utc);$end=[datetime]::new(2029,1,1,0,0,0,[DateTimeKind]::Utc)
$zones=@();$periods=@();$cache=@{}
foreach($c in $ordered){
 $tz=[TimeZoneInfo]::FindSystemTimeZoneById($c.timezone_id)
 $windowsId='';if(-not [TimeZoneInfo]::TryConvertIanaIdToWindowsId($c.timezone_id,[ref]$windowsId)){throw "No Windows mapping for $($c.timezone_id)"}
 $zones += [pscustomobject]@{key=$c.id;label=$c.name;windows=$windowsId;iana=$c.timezone_id;country=$c.country_code;latitude=$c.latitude;longitude=$c.longitude}
 if(-not $cache.ContainsKey($c.timezone_id)){
  $parts=@();$from=$start;$offset=$tz.GetUtcOffset($start).TotalHours
  for($probe=$start.AddHours(6);$probe -lt $end;$probe=$probe.AddHours(6)){
   $next=$tz.GetUtcOffset($probe).TotalHours
   if($next -ne $offset){
    $transition=$probe.AddHours(-6)
    while($tz.GetUtcOffset($transition).TotalHours -eq $offset){$transition=$transition.AddMinutes(1)}
    $parts += [pscustomobject]@{from=$from.ToString('o');until=$transition.ToString('o');offset=$offset};$from=$transition;$offset=$next
   }
  }
  $parts += [pscustomobject]@{from=$from.ToString('o');until=$end.ToString('o');offset=$offset};$cache[$c.timezone_id]=$parts
 }
 foreach($p in $cache[$c.timezone_id]){$periods += [pscustomobject]@{key=$c.id;from=$p.from;until=$p.until;offset=$p.offset}}
}
$data=Get-Content -LiteralPath $DataPath -Raw|ConvertFrom-Json
$data.zones=$zones;$data.periods=$periods
$data|Add-Member -NotePropertyName catalogSource -NotePropertyValue 'WorkTrail/Assets/WorldClocks/world-clocks.sqlite3' -Force
$data|Add-Member -NotePropertyName catalogSha256 -NotePropertyValue (Get-FileHash -LiteralPath (Join-Path $PSScriptRoot '../../../WorkTrail/Assets/WorldClocks/world-clocks.sqlite3') -Algorithm SHA256).Hash -Force
$data|ConvertTo-Json -Depth 10|Set-Content -LiteralPath $DataPath -Encoding utf8
[pscustomobject]@{Cities=$zones.Count;DistinctTimeZones=$cache.Count;UtcPeriods=$periods.Count;SourceSha256=$data.catalogSha256}|ConvertTo-Json -Compress
