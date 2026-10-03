// SPDX-License-Identifier: MIT

using System.IO.Compression;
using WorkTrail.Application;

namespace WorkTrail.Services;

/// <summary>Writes native Office themes so Excel can restyle every sheet without changing report data.</summary>
internal static class ReportExcelTheme
{
    // zip receives the theme part referenced by the workbook relationships.
    // theme selects a printable palette; unsupported values fail before publication.
    internal static void Write(ZipArchive zip, ReportWorkbookTheme theme)
    {
        var (name, accent) = theme switch
        {
            ReportWorkbookTheme.WorkTrail => ("WorkTrail", "6038A0"),
            ReportWorkbookTheme.SpreadsheetGreen => ("WorkTrail Green", "217346"),
            ReportWorkbookTheme.InkBlue => ("WorkTrail Blue", "244B75"),
            _ => throw new ArgumentOutOfRangeException(nameof(theme))
        };
        using var writer = new StreamWriter(zip.CreateEntry("xl/theme/theme1.xml").Open());
        // The accent drives headers, links and row tints. Yellow remains reserved for editable cells.
        writer.Write($$"""
            <?xml version="1.0" encoding="utf-8"?>
            <a:theme xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main" name="{{name}}">
              <a:themeElements>
                <a:clrScheme name="{{name}}">
                  <a:dk1><a:srgbClr val="252233"/></a:dk1><a:lt1><a:srgbClr val="FFFFFF"/></a:lt1>
                  <a:dk2><a:srgbClr val="454152"/></a:dk2><a:lt2><a:srgbClr val="F6F5F8"/></a:lt2>
                  <a:accent1><a:srgbClr val="{{accent}}"/></a:accent1>
                  <a:accent2><a:srgbClr val="398577"/></a:accent2><a:accent3><a:srgbClr val="4F81A8"/></a:accent3>
                  <a:accent4><a:srgbClr val="A57231"/></a:accent4><a:accent5><a:srgbClr val="865E90"/></a:accent5>
                  <a:accent6><a:srgbClr val="7E8666"/></a:accent6>
                  <a:hlink><a:srgbClr val="{{accent}}"/></a:hlink><a:folHlink><a:srgbClr val="686374"/></a:folHlink>
                </a:clrScheme>
                <a:fontScheme name="WorkTrail Arial">
                  <a:majorFont><a:latin typeface="Arial"/><a:ea typeface=""/><a:cs typeface=""/></a:majorFont>
                  <a:minorFont><a:latin typeface="Arial"/><a:ea typeface=""/><a:cs typeface=""/></a:minorFont>
                </a:fontScheme>
                <a:fmtScheme name="WorkTrail">
                  <a:fillStyleLst>
                    <a:solidFill><a:schemeClr val="phClr"/></a:solidFill>
                    <a:solidFill><a:schemeClr val="phClr"><a:tint val="50000"/></a:schemeClr></a:solidFill>
                    <a:solidFill><a:schemeClr val="phClr"><a:shade val="80000"/></a:schemeClr></a:solidFill>
                  </a:fillStyleLst>
                  <a:lnStyleLst>
                    <a:ln w="6350" cap="flat" cmpd="sng" algn="ctr"><a:solidFill><a:schemeClr val="phClr"/></a:solidFill><a:prstDash val="solid"/><a:miter lim="800000"/></a:ln>
                    <a:ln w="12700" cap="flat" cmpd="sng" algn="ctr"><a:solidFill><a:schemeClr val="phClr"/></a:solidFill><a:prstDash val="solid"/><a:miter lim="800000"/></a:ln>
                    <a:ln w="19050" cap="flat" cmpd="sng" algn="ctr"><a:solidFill><a:schemeClr val="phClr"/></a:solidFill><a:prstDash val="solid"/><a:miter lim="800000"/></a:ln>
                  </a:lnStyleLst>
                  <a:effectStyleLst>
                    <a:effectStyle><a:effectLst/></a:effectStyle><a:effectStyle><a:effectLst/></a:effectStyle><a:effectStyle><a:effectLst/></a:effectStyle>
                  </a:effectStyleLst>
                  <a:bgFillStyleLst>
                    <a:solidFill><a:schemeClr val="phClr"/></a:solidFill>
                    <a:solidFill><a:schemeClr val="phClr"><a:tint val="95000"/></a:schemeClr></a:solidFill>
                    <a:solidFill><a:schemeClr val="phClr"><a:shade val="20000"/></a:schemeClr></a:solidFill>
                  </a:bgFillStyleLst>
                </a:fmtScheme>
              </a:themeElements>
              <a:objectDefaults/><a:extraClrSchemeLst/>
            </a:theme>
            """);
    }
}
