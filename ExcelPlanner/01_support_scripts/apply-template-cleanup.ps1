# Apply the artifact-authored privacy patch without round-tripping native queries.
param([switch]$Apply)
$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
$path=Join-Path $root 'Planner_Availability_A4.xlsx'
$patch=Get-Content -LiteralPath (Join-Path $root 'artifacts/template-cleanup/clear-fields.json') -Raw | ConvertFrom-Json
if($patch.Count -ne 2 -or @($patch | Where-Object {$_.sheet -ne 'Parametri' -or $_.address -notin @('B25','B32') -or $_.value}).Count){throw 'Unexpected privacy patch.'}
$original=[IO.File]::ReadAllBytes($path)
$memory=[IO.MemoryStream]::new()
$memory.Write($original,0,$original.Length);$memory.Position=0
$zip=[IO.Compression.ZipArchive]::new($memory,[IO.Compression.ZipArchiveMode]::Update,$true)
function Read-Xml([string]$name){
 $reader=[IO.StreamReader]::new($zip.GetEntry($name).Open())
 try{[xml]$reader.ReadToEnd()}finally{$reader.Dispose()}
}
function Write-Xml([string]$name,[xml]$xml){
 $entry=$zip.GetEntry($name);$entry.Delete()
 $stream=$zip.CreateEntry($name).Open()
 $settings=[Xml.XmlWriterSettings]::new();$settings.Encoding=[Text.UTF8Encoding]::new($false);$settings.Indent=$false
 $writer=[Xml.XmlWriter]::Create($stream,$settings)
 try{$xml.Save($writer)}finally{$writer.Dispose();$stream.Dispose()}
}
try{
 $book=Read-Xml 'xl/workbook.xml';$rels=Read-Xml 'xl/_rels/workbook.xml.rels'
 $sheet=@($book.workbook.sheets.sheet | Where-Object name -eq 'Parametri')[0]
 $id=$sheet.GetAttribute('id','http://schemas.openxmlformats.org/officeDocument/2006/relationships')
 $target=($rels.Relationships.Relationship | Where-Object Id -eq $id).Target
 $part=if($target.StartsWith('/')){$target.TrimStart('/')}else{'xl/'+$target}
 $xml=Read-Xml $part;$strings=Read-Xml 'xl/sharedStrings.xml';$ids=[Collections.Generic.HashSet[int]]::new()
 foreach($item in $patch){
  $cell=@($xml.worksheet.sheetData.row.c | Where-Object r -eq $item.address)[0]
  if($null -eq $cell){continue}
  if($cell.t -eq 's' -and $cell.v){$null=$ids.Add([int]$cell.v)}
  foreach($child in @($cell.ChildNodes)){$null=$cell.RemoveChild($child)}
  $cell.RemoveAttribute('t')
 }
 # Sensitive shared strings may only be removed if no other cell uses them.
 foreach($entry in @($zip.Entries | Where-Object FullName -Match '^xl/worksheets/sheet\d+\.xml$')){
  $other=if($entry.FullName -eq $part){$xml}else{Read-Xml $entry.FullName}
  foreach($cell in $other.worksheet.sheetData.row.c){if($cell.t -eq 's' -and $cell.v -and $ids.Contains([int]$cell.v)){throw 'A private field is referenced elsewhere; manual review required.'}}
 }
 foreach($index in $ids){$si=$strings.sst.si[$index];$si.RemoveAll();$null=$si.AppendChild($strings.CreateElement('t',$strings.DocumentElement.NamespaceURI))}
 if($Apply){Write-Xml $part $xml;Write-Xml 'xl/sharedStrings.xml' $strings}
}finally{$zip.Dispose()}
if($Apply){
 $backup=Join-Path $root 'artifacts/template-cleanup/before-cleanup.xlsx'
 if(Test-Path -LiteralPath $backup){throw 'Cleanup backup already exists.'}
 [IO.File]::WriteAllBytes($backup,$original)
 $new=$memory.ToArray()
 $current=[IO.File]::ReadAllBytes($path)
 if([Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($current)) -ne [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($original))){throw 'Template changed during cleanup.'}
 [IO.File]::WriteAllBytes($path,$new)
}
$memory.Dispose()
Write-Output "Privacy patch validated. Applied: $Apply. No formulas or native query parts changed."
