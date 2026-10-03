param([string]$OutputPath)
$ErrorActionPreference='Stop'
$defs=@(
 @('VN','Hanoi','SE Asia Standard Time','Asia/Ho_Chi_Minh'),
 @('IN','Mumbai','India Standard Time','Asia/Kolkata'),
 @('BY','Minsk','Belarus Standard Time','Europe/Minsk'),
 @('IT','Roma','W. Europe Standard Time','Europe/Rome'),
 @('GB','Londra','GMT Standard Time','Europe/London'),
 @('US-E','New York','Eastern Standard Time','America/New_York'),
 @('US-W','Los Angeles','Pacific Standard Time','America/Los_Angeles'),
 @('BD','Dhaka','Bangladesh Standard Time','Asia/Dhaka')
)
$start=[datetime]::new(2026,1,1,0,0,0,[DateTimeKind]::Utc)
$end=[datetime]::new(2029,1,1,0,0,0,[DateTimeKind]::Utc)
$zones=@(); $periods=@()
foreach($z in $defs){
 $tz=[TimeZoneInfo]::FindSystemTimeZoneById($z[2])
 $zones += [pscustomobject]@{key=$z[0];label=$z[1];windows=$z[2];iana=$z[3]}
 $from=$start; $offset=$tz.GetUtcOffset($start).TotalHours
 for($probe=$start.AddHours(6);$probe -lt $end;$probe=$probe.AddHours(6)){
  $next=$tz.GetUtcOffset($probe).TotalHours
  if($next -ne $offset){
   $transition=$probe.AddHours(-6)
   while($tz.GetUtcOffset($transition).TotalHours -eq $offset){$transition=$transition.AddMinutes(1)}
   $periods += [pscustomobject]@{key=$z[0];from=$from.ToString('o');until=$transition.ToString('o');offset=$offset}
   $from=$transition;$offset=$next
  }
 }
 $periods += [pscustomobject]@{key=$z[0];from=$from.ToString('o');until=$end.ToString('o');offset=$offset}
}
$bank=Invoke-RestMethod -Uri 'https://www.gov.uk/bank-holidays.json'
$events=@($bank.'england-and-wales'.events | Where-Object { $_.date -ge '2026-01-01' -and $_.date -lt '2029-01-01'} | ForEach-Object {[pscustomobject]@{calendar='GB-ENG';date=$_.date;name=$_.title;source='https://www.gov.uk/bank-holidays';active=1}})
$phaseNames=@{'New Moon'='Luna nuova';'First Quarter'='Primo quarto';'Full Moon'='Luna piena';'Last Quarter'='Ultimo quarto'}
$betweenNames=@{'New Moon'='Falce crescente';'First Quarter'='Gibbosa crescente';'Full Moon'='Gibbosa calante';'Last Quarter'='Falce calante'}
$moonEvents=@(foreach($y in 2025..2029){
 $response=Invoke-RestMethod -Uri "https://aa.usno.navy.mil/api/moon/phases/year?year=$y"
 if($response.error){throw "USNO: $($response.error)"}
 foreach($p in $response.phasedata){
  [pscustomobject]@{utc=('{0:0000}-{1:00}-{2:00}T{3}:00' -f $p.year,$p.month,$p.day,$p.time);phase=$phaseNames[$p.phase];between=$betweenNames[$p.phase];source="https://aa.usno.navy.mil/api/moon/phases/year?year=$y"}
 }
})
$data=[pscustomobject]@{generated=(Get-Date).ToString('o');zones=$zones;periods=$periods;holidays=$events;moon=$moonEvents}
$data | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $OutputPath -Encoding utf8
[pscustomobject]@{Zones=$zones.Count;Periods=$periods.Count;Holidays=$events.Count;CalendarYears=($events | Group-Object {$_.date.Substring(0,4)} | ForEach-Object {"$($_.Name):$($_.Count)"}) -join ', ' } | Format-List
