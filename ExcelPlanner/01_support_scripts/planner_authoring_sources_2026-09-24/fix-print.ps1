param([string]$WorkbookPath,[string]$PdfPath,[string]$ExtendedPdfPath)
$ErrorActionPreference='Stop';$excel=$null;$book=$null;$map=$null
try{
 $excel=New-Object -ComObject Excel.Application;$excel.Visible=$false;$excel.DisplayAlerts=$false;$excel.EnableEvents=$false;$excel.AutomationSecurity=3
 $book=$excel.Workbooks.Open($WorkbookPath,0,$false);if($book.AutoSaveOn){$book.AutoSaveOn=$false}
 $map=$excel.MapPaperSize;$excel.MapPaperSize=$false
 Write-Output "Printer: $($excel.ActivePrinter); MapPaperSize was $map; PrintCommunication $($excel.PrintCommunication)"
 $excel.PrintCommunication=$true
 foreach($name in @('Planner','Estesa')){
  $sh=$book.Worksheets.Item($name);$sh.Activate();$p=$sh.PageSetup
  Write-Output "Before $name Paper=$($p.PaperSize), Zoom=$($p.Zoom), Wide=$($p.FitToPagesWide), Tall=$($p.FitToPagesTall)"
  $p.PaperSize=9;$p.Orientation=2
  $p.Zoom=$false;$p.FitToPagesWide=1
  if($name -eq 'Planner'){$p.FitToPagesTall=1}else{
   $p.FitToPagesTall=2;$p.PrintTitleRows='$1:$16'
   $sh.ResetAllPageBreaks();$null=$sh.HPageBreaks.Add($sh.Range('A41'))
  }
  Write-Output "After $name Paper=$($p.PaperSize), Zoom=$($p.Zoom), Wide=$($p.FitToPagesWide), Tall=$($p.FitToPagesTall)"
 }
 $book.Worksheets.Item('Planner').ExportAsFixedFormat(0,$PdfPath)
 $book.Worksheets.Item('Estesa').ExportAsFixedFormat(0,$ExtendedPdfPath)
 $book.Worksheets.Item('Parametri').Activate();$book.Worksheets.Item('Parametri').Range('C5').Select();$book.Save()
}catch{Write-Error ("Line $($_.InvocationInfo.ScriptLineNumber): "+$_.Exception.Message);exit 1}
finally{if($book){$book.Close($false);[void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($book)};if($excel){if($null -ne $map){$excel.MapPaperSize=$map};$excel.Quit();[void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($excel)};[GC]::Collect();[GC]::WaitForPendingFinalizers()}
