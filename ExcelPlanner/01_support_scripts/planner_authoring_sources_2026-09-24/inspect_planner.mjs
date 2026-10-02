import fs from "node:fs/promises";
import path from "node:path";
import {fileURLToPath} from "node:url";
import { FileBlob, SpreadsheetFile } from "@oai/artifact-tool";

const plannerRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "../..");
const inputPath = process.argv[2] ?? path.join(plannerRoot, "Planner_Availability_A4.xlsx");
const outputName = process.argv[3] ?? "planner-full";
const mode = process.argv[4] ?? "full";
const outputDir = path.join(plannerRoot, "artifacts/inspection");

const input = await FileBlob.load(inputPath);
const workbook = await SpreadsheetFile.importXlsx(input);

const printInspect = async (label, options) => {
  const result = await workbook.inspect(options);
  console.log(`\n### ${label}`);
  console.log(result.ndjson);
};

await printInspect("SHEETS", {
  kind: "sheet",
  include: "id,name",
  maxChars: 5000,
});

if (mode === "full") await printInspect("WORKBOOK SUMMARY", {
  kind: "workbook,sheet,table,definedName,drawing",
  maxChars: 15000,
  tableMaxRows: 8,
  tableMaxCols: 12,
  tableMaxCellChars: 120,
});

if (mode === "full") await printInspect("PLANNER VALUES AND FORMULAS", {
  kind: "table",
  range: "Planner!A1:S70",
  include: "values,formulas",
  maxChars: 50000,
  tableMaxRows: 70,
  tableMaxCols: 19,
  tableMaxCellChars: 160,
});

if (mode === "full") await printInspect("PLANNER FORMULAS", {
  kind: "formula",
  sheetId: "Planner",
  range: "A1:S90",
  maxChars: 45000,
  options: { maxResults: 500 },
});

if (mode === "logic") {
  await printInspect("PLANNER FORMULAS", {
    kind: "formula",
    sheetId: "Planner",
    range: "A1:K72",
    maxChars: 30000,
    options: { maxResults: 500 },
  });
  await printInspect("DST RULES FORMULAS", {
    kind: "formula",
    sheetId: "DST Rules",
    range: "A1:E37",
    maxChars: 30000,
    options: { maxResults: 500 },
  });
  await printInspect("CLOCKS FORMULAS", {
    kind: "formula",
    sheetId: "Clocks",
    range: "A1:I210",
    maxChars: 30000,
    options: { maxResults: 1000 },
  });
  await printInspect("FORMULA ERRORS", {
    kind: "match",
    searchTerm: "#REF!|#DIV/0!|#VALUE!|#NAME\\?|#N/A|#NUM!|#NULL!|#SPILL!|#CALC!",
    options: { useRegex: true, maxResults: 300 },
    maxChars: 10000,
  });
}

if (mode === "errors") {
  await printInspect("FORMULA ERRORS", {
    kind: "match",
    searchTerm: "#REF!|#DIV/0!|#VALUE!|#NAME\\?|#N/A|#NUM!|#NULL!|#SPILL!|#CALC!",
    options: { useRegex: true, maxResults: 300 },
    maxChars: 10000,
  });
}

await fs.mkdir(outputDir, { recursive: true });
if (mode === "full") {
const preview = await workbook.render({
  sheetName: "Planner",
  autoCrop: "all",
  scale: 1,
  format: "png",
});
await fs.writeFile(`${outputDir}/${outputName}.png`, new Uint8Array(await preview.arrayBuffer()));

console.log(`\n### PREVIEW\n${outputDir}/${outputName}.png`);
}
