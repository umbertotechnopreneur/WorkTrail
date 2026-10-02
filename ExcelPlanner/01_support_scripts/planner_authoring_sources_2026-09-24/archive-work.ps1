param(
 [Parameter(Mandatory)][string]$SourcePath,
 [Parameter(Mandatory)][string]$DestinationPath,
 [Parameter(Mandatory)][string]$AllowedRoot
)
$ErrorActionPreference = 'Stop'
$sourceRoot = (Resolve-Path -LiteralPath $SourcePath).Path.TrimEnd('\')
$destinationRoot = [IO.Path]::GetFullPath($DestinationPath).TrimEnd('\')
$allowedRoot = (Resolve-Path -LiteralPath $AllowedRoot).Path.TrimEnd('\')
if (-not $destinationRoot.StartsWith($allowedRoot + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Archive destination must be a child of Excel prototypes.' }
if (Test-Path -LiteralPath $destinationRoot) { throw 'Archive already exists; refusing to overwrite it.' }

# Inventory the task directory without traversing its external dependency junction.
$pending = [Collections.Generic.Stack[string]]::new()
$pending.Push($sourceRoot)
$files = [Collections.Generic.List[object]]::new()
$directories = [Collections.Generic.List[string]]::new()
$links = [Collections.Generic.List[object]]::new()
while ($pending.Count -gt 0) {
 $directory = $pending.Pop()
 foreach ($item in Get-ChildItem -LiteralPath $directory -Force) {
  $relative = [IO.Path]::GetRelativePath($sourceRoot, $item.FullName)
  if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
   $links.Add([pscustomobject]@{ RelativePath=$relative; LinkType=$item.LinkType; Target=$item.Target; Reason='External dependency link; recorded, not followed or copied.' })
  } elseif ($item.PSIsContainer) {
   $directories.Add($relative)
   $pending.Push($item.FullName)
  } else {
   $files.Add([pscustomobject]@{ RelativePath=$relative; SourcePath=$item.FullName; Length=$item.Length; LastWriteTimeUtc=$item.LastWriteTimeUtc.ToString('o') })
  }
 }
}
$null = New-Item -ItemType Directory -Path $destinationRoot
foreach ($relative in $directories | Sort-Object Length) { $null = New-Item -ItemType Directory -Path (Join-Path $destinationRoot $relative) -Force }
$verified = [Collections.Generic.List[object]]::new()
foreach ($file in $files | Sort-Object RelativePath) {
 $target = [IO.Path]::GetFullPath((Join-Path $destinationRoot $file.RelativePath))
 if (-not $target.StartsWith($destinationRoot + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Invalid relative archive path.' }
 $before = (Get-FileHash -LiteralPath $file.SourcePath -Algorithm SHA256).Hash
 Copy-Item -LiteralPath $file.SourcePath -Destination $target
 $after = (Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash
 if ($before -ne $after) { throw "Checksum mismatch: $($file.RelativePath)" }
 $verified.Add([pscustomobject]@{ Path=$file.RelativePath; Bytes=$file.Length; SHA256=$after; OriginalLastWriteTimeUtc=$file.LastWriteTimeUtc })
}
$manifest = [ordered]@{
 ArchivedAt=(Get-Date).ToString('o')
 Source=$sourceRoot
 Destination=$destinationRoot
 CopiedFiles=$verified.Count
 CopiedBytes=($verified | Measure-Object Bytes -Sum).Sum
 Files=$verified.ToArray()
 ExcludedLinks=$links.ToArray()
}
$manifest | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $destinationRoot 'ARCHIVE-MANIFEST.json') -Encoding utf8
[pscustomobject]@{ Destination=$destinationRoot; VerifiedFiles=$verified.Count; Bytes=$manifest.CopiedBytes; ExcludedLinks=$links.ToArray() } | ConvertTo-Json -Depth 4
