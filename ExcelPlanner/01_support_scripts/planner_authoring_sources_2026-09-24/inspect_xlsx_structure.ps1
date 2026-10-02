param(
    [Parameter(Mandatory = $true)]
    [string]$Path
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem

function Read-ZipXml {
    param($Zip, [string]$EntryName)
    $entry = $Zip.GetEntry($EntryName)
    if ($null -eq $entry) { return $null }
    $reader = [System.IO.StreamReader]::new($entry.Open())
    try { return [xml]$reader.ReadToEnd() }
    finally { $reader.Dispose() }
}

$zip = [System.IO.Compression.ZipFile]::OpenRead($Path)
try {
    $workbook = Read-ZipXml $zip 'xl/workbook.xml'
    $workbookRels = Read-ZipXml $zip 'xl/_rels/workbook.xml.rels'
    $styles = Read-ZipXml $zip 'xl/styles.xml'

    $nsWorkbook = [System.Xml.XmlNamespaceManager]::new($workbook.NameTable)
    $nsWorkbook.AddNamespace('x', 'http://schemas.openxmlformats.org/spreadsheetml/2006/main')
    $nsWorkbook.AddNamespace('r', 'http://schemas.openxmlformats.org/officeDocument/2006/relationships')
    $planner = $workbook.SelectSingleNode('//x:sheet[@name="Planner"]', $nsWorkbook)
    $relationshipId = $planner.GetAttribute('id', 'http://schemas.openxmlformats.org/officeDocument/2006/relationships')

    $nsRels = [System.Xml.XmlNamespaceManager]::new($workbookRels.NameTable)
    $nsRels.AddNamespace('p', 'http://schemas.openxmlformats.org/package/2006/relationships')
    $relationship = $workbookRels.SelectSingleNode("//p:Relationship[@Id='$relationshipId']", $nsRels)
    $sheetEntryName = if ($relationship.Target.StartsWith('/')) { $relationship.Target.TrimStart('/') } else { 'xl/' + $relationship.Target.TrimStart('/') }
    $sheet = Read-ZipXml $zip $sheetEntryName

    $nsSheet = [System.Xml.XmlNamespaceManager]::new($sheet.NameTable)
    $nsSheet.AddNamespace('x', 'http://schemas.openxmlformats.org/spreadsheetml/2006/main')
    $nsStyles = [System.Xml.XmlNamespaceManager]::new($styles.NameTable)
    $nsStyles.AddNamespace('x', 'http://schemas.openxmlformats.org/spreadsheetml/2006/main')
    $dxfs = @($styles.SelectNodes('//x:dxfs/x:dxf', $nsStyles))

    $ruleRows = foreach ($block in $sheet.SelectNodes('//x:conditionalFormatting', $nsSheet)) {
        foreach ($rule in $block.SelectNodes('x:cfRule', $nsSheet)) {
            $dxfId = if ($rule.HasAttribute('dxfId')) { [int]$rule.dxfId } else { $null }
            $dxf = if ($null -ne $dxfId -and $dxfId -lt $dxfs.Count) { $dxfs[$dxfId] } else { $null }
            $patternFill = if ($null -ne $dxf) { $dxf.SelectSingleNode('x:fill/x:patternFill', $nsStyles) } else { $null }
            $fill = if ($null -ne $patternFill) { $patternFill.SelectSingleNode('x:fgColor', $nsStyles) } else { $null }
            $backgroundFill = if ($null -ne $patternFill) { $patternFill.SelectSingleNode('x:bgColor', $nsStyles) } else { $null }
            $font = if ($null -ne $dxf) { $dxf.SelectSingleNode('x:font', $nsStyles) } else { $null }
            $colorNodes = @($rule.SelectNodes('x:colorScale/x:color', $nsSheet) | ForEach-Object {
                if ($_.rgb) { $_.rgb } elseif ($_.theme) { "theme:$($_.theme);tint:$($_.tint)" } else { $_.OuterXml }
            })
            [PSCustomObject]@{
                Range      = $block.sqref
                Priority   = [int]$rule.priority
                Type       = $rule.type
                Operator   = $rule.operator
                StopIfTrue = $rule.stopIfTrue
                DxfId      = $dxfId
                Pattern    = if ($null -ne $patternFill) { $patternFill.patternType } else { $null }
                Fill       = if ($null -ne $fill) { if ($fill.rgb) { $fill.rgb } elseif ($fill.theme) { "theme:$($fill.theme);tint:$($fill.tint)" } else { $fill.OuterXml } } else { $null }
                Background = if ($null -ne $backgroundFill) { if ($backgroundFill.rgb) { $backgroundFill.rgb } elseif ($backgroundFill.indexed) { "indexed:$($backgroundFill.indexed)" } else { $backgroundFill.OuterXml } } else { $null }
                Font       = if ($null -ne $font) { ($font.OuterXml -replace '\s+', ' ') } else { $null }
                DxfXml     = if ($null -ne $dxf) { ($dxf.OuterXml -replace '\s+', ' ') } else { $null }
                Formula    = (@($rule.SelectNodes('x:formula', $nsSheet) | ForEach-Object { $_.InnerText }) -join ' | ')
                Scale      = ($colorNodes -join ' -> ')
            }
        }
    }

    Write-Output "Conditional formatting blocks: $(@($sheet.SelectNodes('//x:conditionalFormatting', $nsSheet)).Count)"
    Write-Output "Conditional formatting rules: $($ruleRows.Count)"
    $ruleRows | Sort-Object Priority | ConvertTo-Json -Depth 5

    Write-Output "`nData validations:"
    $validations = @($sheet.SelectNodes('//x:dataValidations/x:dataValidation', $nsSheet) | ForEach-Object {
        [PSCustomObject]@{
            Range = $_.sqref
            Type = $_.type
            Formula1 = $_.SelectSingleNode('x:formula1', $nsSheet).InnerText
            Formula2 = $_.SelectSingleNode('x:formula2', $nsSheet).InnerText
        }
    })
    if ($validations.Count -eq 0) { Write-Output '(none)' } else { $validations | Format-Table -Wrap -AutoSize }

    Write-Output "`nLegacy comments/notes:"
    $commentEntries = @($zip.Entries | Where-Object { $_.FullName -like 'xl/comments*.xml' })
    foreach ($commentEntry in $commentEntries) {
        $comments = Read-ZipXml $zip $commentEntry.FullName
        $nsComments = [System.Xml.XmlNamespaceManager]::new($comments.NameTable)
        $nsComments.AddNamespace('x', 'http://schemas.openxmlformats.org/spreadsheetml/2006/main')
        $authors = @($comments.SelectNodes('//x:authors/x:author', $nsComments) | ForEach-Object { $_.InnerText })
        $comments.SelectNodes('//x:commentList/x:comment', $nsComments) | ForEach-Object {
            [PSCustomObject]@{
                Entry = $commentEntry.FullName
                Cell = $_.ref
                Author = $authors[[int]$_.authorId]
                Text = (($_.SelectNodes('.//x:t', $nsComments) | ForEach-Object { $_.InnerText }) -join '')
            }
        } | Format-List
    }
}
finally {
    $zip.Dispose()
}
