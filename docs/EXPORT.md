# Export your activity as Excel, CSV or JSON

Open the player's **More** menu and choose **Export activity**. Select your dates, devices and file format. The preview shows up to five rows from each selected table. Use **File contents** to choose optional fields, then **Export…** to select the destination.

You can configure the report, preview your data and save field preferences in Free. Saving an export requires Full/Premium. The shared application service checks access again before writing, including calls through IPC. This checkout has an unlicensed Free default and a Debug-only profile switch; it does not yet integrate a commercial purchase provider. The Free export button opens the standard upgrade message.

## File formats

- **Excel (.xlsx):** separate sheets, column filters and frozen headers. Numeric measurements stay numeric. Text longer than an Excel cell can hold is preserved in ordered rows in **Text parts**, identified by source sheet, worksheet row, field and part number. The source cell contains an excerpt and a reference to that sheet.
- **CSV (.zip):** one UTF-8 CSV with BOM per selected table. Choose a comma or semicolon delimiter. Quotes and multiline text are escaped. Formula-like text receives a leading apostrophe so spreadsheets treat it as text; JSON retains the original selected text without that CSV-specific transformation.
- **JSON (.json):** schema version 1, creation timestamp, inclusive date range, time zone and selected tables. Missing values stay `null`. Column identifiers are stable English names in all formats; dates use ISO notation and duration columns explicitly use seconds.

The files are analytical exports, not importable WorkTrail archives. The existing installation archive transfer remains a separate operation.

## Understand what is included

**Summary**, **Days** and **Applications** use recorded activity samples. **Captures** contains retained screenshot metadata and selected saved AI descriptions. A screenshot does not establish the duration of a task. Days without samples are marked with `has_data: false`; missing measurements are not interpreted as zero.

Device filters apply to activity and captures. AI usage totals currently have no device dimension in the report contract, so you must choose **All devices** to include them. AI usage includes request and token counts, not costs or spending estimates.

Ordinary exports do not call an AI provider or include image bytes. Titles, device identifiers, paths and activity details are opt-in. Descriptions may themselves contain personal information even when the corresponding metadata columns are excluded. The short description is a local excerpt; the complete description retains its saved Markdown.

An export accepts up to 366 days, 50,000 retained captures and 32 million loaded text characters. Larger requests fail with guidance to shorten the range; records are never silently omitted to fit. The file is written to a sibling temporary file and finalized only after successful completion. Cancellation or failure before finalization preserves an existing destination.

## Optional AI summary

Select **AI summary**, choose sources, and select **Generate summary** to send the selected saved text through your configured AI provider and environment-variable key. No screenshots or current desktop capture are attached. This action is available in Free; saving its result as part of an export still requires Full.

Generation uses the existing provider configuration, cancellation and daily request limit. It records usage metadata without persisting the prompt or generated draft in activity history. Provider charges may apply; this feature does not currently calculate a reliable cost estimate. Requests containing more than 160,000 serialized source characters fail rather than silently truncating the inputs.

The result is editable and may be grouped by day, application or the whole period. Review it before sharing. Changing export/source choices clears the draft to avoid exporting text from an earlier selection. Drafts and dates are not saved with field preferences.

## Try the development build without MSIX

Use PowerShell 7 from the repository root:

```powershell
dotnet build .\WorkTrail\WorkTrail.csproj -c Debug -p:Platform=x64 -p:RuntimeIdentifier=win-x64 -p:WorkTrailDistributionMode=Unpackaged -p:GenerateAppxPackageOnBuild=false -p:AppxPackageSigningEnabled=false
& .\WorkTrail\bin\x64\Debug\net10.0-windows10.0.19041.0\win-x64\WorkTrail.exe
```

Close another running WorkTrail instance before trying the new build so the shared runtime serves the current code. Keep the executable together with its output folder. This local development launch is not a standalone distribution; commercial Windows releases remain MSIX-only.

In Debug, the player's More menu can simulate Free or Premium. This simulation is runtime-local and resets when the app exits. It is not an implemented Store license.
